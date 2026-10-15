#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UGF.EditorTools.Psd2UGUI
{
    // One export transaction: compare before importing, then bind persistent assets before saving a prefab.
    internal sealed class PsdImageExportBatch : IDisposable
    {
        private sealed class Entry
        {
            internal PsdLayerNode node;
            internal PsdImageRecord record;
            internal string token, suggested, source, hash, settings, contract, previous, resolved;
            internal bool sprite, highDepth, slice, existing, applied, nearest, customBorder;
            internal PsdImagePixels pixels;
            internal Vector4 border, generatedBorder;
            internal float pixelsPerUnit;
            internal PsdImageCandidateIndex.Feature[] features;
            internal readonly List<DrawSize> drawSizes = new List<DrawSize>();
            internal Texture2D temporaryTexture;
            internal Sprite temporarySprite;
        }
        private struct DrawSize
        {
            internal float width, height;
            internal bool fillCenter;
        }
        private sealed class Group
        {
            internal Entry master;
            internal bool immutable;
            internal readonly List<Entry> members = new List<Entry>();
        }

        private const string CacheDirectory = "Library/PSD2UIForm/image-content";
        private const int ReuseDetailLimit = 8;
        private readonly Psd2UIFormConverter converter;
        private readonly PsdImageExportBatch previousBatch;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, Entry> tokens = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> requests = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PsdImagePixels> descriptors = new Dictionary<string, PsdImagePixels>(StringComparer.Ordinal);
        private readonly List<PsdImagePixels> resident = new List<PsdImagePixels>();
        private readonly Dictionary<string, PsdImageCandidateIndex.Feature[]> featureCache = new Dictionary<string, PsdImageCandidateIndex.Feature[]>(StringComparer.Ordinal);
        private long residentBytes;
        internal long PeakResidentBytes { get; private set; }
        private readonly HashSet<string> registeredImages = PsdResourceRegistry.ImageGuids();
        private readonly bool crop = Psd2UIFormSettings.Instance.AutoCropMinimalNineSlice;
        private readonly bool compress = Psd2UIFormSettings.Instance.CompressImage;
        private readonly PsdImageReuseTolerance tolerance = Psd2UIFormSettings.Instance.ImageReuseTolerance;
        internal readonly Dictionary<Object, Object> Replacements = new Dictionary<Object, Object>();

        internal PsdImageExportBatch(Psd2UIFormConverter converter)
        {
            this.converter = converter;
            previousBatch = converter.ImageExport;
            converter.ImageExport = this;
        }

        internal string Add(PsdLayerNode node, string suggested, byte[] bytes, bool sprite, bool highDepth, bool slice)
        {
            slice &= sprite;
            string settings = (sprite ? "sprite" : "texture") + ":" + highDepth + ":" + slice + ":" + crop + ":" + compress;
            string key = node.PersistentId + ":" + settings;
            if (requests.TryGetValue(key, out var existing)) return existing.token;
            string hash = Psd2UIFormConverter.ImageHash(bytes);
            Directory.CreateDirectory(CacheDirectory);
            string cache = CacheDirectory + "/" + hash + ".png";
            if (!File.Exists(cache)) File.WriteAllBytes(cache, bytes);
            var record = converter.Document.images.Find(r => r.nodeId == node.PersistentId && r.settingsKey == settings);
            if (record == null) record = converter.Document.images.Find(r => r.nodeId == node.PersistentId && !entries.Exists(e => e.record == r));
            var entry = new Entry { node = node, record = record, suggested = suggested, source = cache, hash = hash,
                settings = settings, sprite = sprite, highDepth = highDepth, slice = slice,
                previous = record == null ? null : AssetDatabase.GUIDToAssetPath(record.assetGuid) };
            AddEntry(entry);
            requests.Add(key, entry);
            return entry.token;
        }

        // Existing generated assets can be consolidated without rendering or calling the recognition model.
        internal void AddExisting(PsdImageRecord record)
        {
            if (registeredImages.Contains(record.assetGuid)) return;
            string path = AssetDatabase.GUIDToAssetPath(record.assetGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            bool sprite = importer.textureType == TextureImporterType.Sprite;
            if (sprite && importer.spriteImportMode != SpriteImportMode.Single) return;
            var entry = new Entry { record = record, suggested = path, source = path, previous = path, existing = true,
                hash = Psd2UIFormConverter.ImageHash(File.ReadAllBytes(path)), settings = record.settingsKey,
                sprite = sprite, slice = sprite && importer.spriteBorder != Vector4.zero, highDepth = IsHighDepth(record.settingsKey) };
            AddEntry(entry);
        }

        private void AddEntry(Entry entry)
        {
            entry.token = "psd-image:" + entries.Count;
            var importer = string.IsNullOrEmpty(entry.previous) ? null : AssetImporter.GetAtPath(entry.previous) as TextureImporter;
            entry.nearest = importer != null && importer.filterMode == FilterMode.Point;
            ReadDescriptor(entry);
            // A former shared master may have a different size/border. Its border does not
            // describe newly rendered member pixels. Preserve custom settings only for the
            // actual existing source, or an unchanged source that was itself the master.
            if (entry.slice && importer != null && (entry.existing || entry.record != null
                && entry.record.contentHash == entry.hash && entry.record.masterSourceHash == entry.hash)) entry.border = importer.spriteBorder;
            else if (entry.slice && importer != null && entry.record != null && entry.record.hasGeneratedBorder
                && importer.spriteBorder != entry.record.generatedBorder)
            {
                var border = importer.spriteBorder;
                if (border.x >= 0 && border.y >= 0 && border.z >= 0 && border.w >= 0
                    && border.x + border.z <= entry.pixels.width && border.y + border.w <= entry.pixels.height) entry.border = border;
                else converter.RecordGenerationIssue(entry.node, "image_border_invalid", "用户九宫格边框超过新图尺寸，使用本次素材的有效边框: " + entry.suggested);
            }
            entry.pixelsPerUnit = importer == null ? 100 : importer.spritePixelsPerUnit;
            entry.customBorder = entry.slice && importer != null && importer.spriteBorder !=
                (entry.record != null && entry.record.hasGeneratedBorder ? entry.record.generatedBorder : entry.pixels.border);
            entry.contract = ImportContract(entry.previous, entry.sprite, entry.slice, entry.highDepth);
            Describe(entry);
            entries.Add(entry);
            tokens.Add(entry.token, entry);
        }

        internal bool TryTemporary(string token, bool sprite, out Object value)
        {
            value = null;
            if (token == null || !tokens.TryGetValue(token, out var entry)) return false;
            // Helpers use PSD rectangles for layout; a unique one-pixel object is sufficient as a binding token.
            if (entry.temporaryTexture == null)
                entry.temporaryTexture = new Texture2D(1, 1) { name = entry.token, hideFlags = HideFlags.HideAndDontSave };
            if (sprite && entry.temporarySprite == null)
            {
                entry.temporarySprite = Sprite.Create(entry.temporaryTexture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
                entry.temporarySprite.hideFlags = HideFlags.HideAndDontSave;
            }
            value = sprite ? (Object)entry.temporarySprite : entry.temporaryTexture;
            return true;
        }

        internal string ResolvedPath(string token) => tokens[token].resolved;

        internal void ObserveBindings(GameObject output)
        {
            var bySprite = new Dictionary<Sprite, List<Entry>>();
            foreach (var entry in entries)
            {
                if (!entry.slice) continue;
                var sprite = entry.temporarySprite != null ? entry.temporarySprite : PsdLayerNode.LoadSpriteAssetAtPath(entry.previous);
                if (sprite == null) continue;
                if (!bySprite.TryGetValue(sprite, out var owners)) bySprite.Add(sprite, owners = new List<Entry>());
                owners.Add(entry);
            }
            foreach (var image in output.GetComponentsInChildren<Image>(true))
            {
                if (image.type != Image.Type.Sliced) continue;
                AddDrawSize(image.sprite, image, bySprite);
                var selectable = image.GetComponent<Selectable>();
                if (selectable == null || selectable.targetGraphic != image || selectable.transition != Selectable.Transition.SpriteSwap) continue;
                var state = selectable.spriteState;
                AddDrawSize(state.highlightedSprite, image, bySprite); AddDrawSize(state.pressedSprite, image, bySprite);
                AddDrawSize(state.selectedSprite, image, bySprite); AddDrawSize(state.disabledSprite, image, bySprite);
            }
        }

        private static void AddDrawSize(Sprite sprite, Image image, Dictionary<Sprite, List<Entry>> entries)
        {
            if (sprite == null || !entries.TryGetValue(sprite, out var owners)) return;
            var rect = image.rectTransform.rect;
            foreach (var entry in owners)
            {
                float ppu = entry.pixelsPerUnit / (image.canvas == null ? 100 : image.canvas.referencePixelsPerUnit) * image.pixelsPerUnitMultiplier;
                var size = new DrawSize { width = Mathf.Max(1, rect.width * ppu), height = Mathf.Max(1, rect.height * ppu), fillCenter = image.fillCenter };
                if (!entry.drawSizes.Contains(size)) entry.drawSizes.Add(size);
            }
        }

        internal void Complete(GameObject output)
        {
            if (entries.Count == 0) return;
            if (output != null) ObserveBindings(output);
            var participating = new HashSet<PsdImageRecord>();
            foreach (var entry in entries) if (entry.record != null) participating.Add(entry.record);
            var groups = new List<Group>();
            var index = new PsdImageCandidateIndex();
            var candidates = new List<int>();
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Unselected owners keep their pixels and GUID. A local update may reuse them, never upgrade them.
            foreach (var record in converter.Document.images)
            {
                if (participating.Contains(record) && !registeredImages.Contains(record.assetGuid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(record.assetGuid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || !protectedPaths.Add(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                try
                {
                    bool sprite = importer.textureType == TextureImporterType.Sprite;
                    if (sprite && importer.spriteImportMode != SpriteImportMode.Single) continue;
                    var fixedEntry = new Entry { record = record, source = path, previous = path, resolved = path, existing = true,
                        hash = Psd2UIFormConverter.ImageHash(File.ReadAllBytes(path)), sprite = sprite,
                        highDepth = IsHighDepth(record.settingsKey), slice = sprite && importer.spriteBorder != Vector4.zero };
                    fixedEntry.contract = ImportContract(path, sprite, fixedEntry.slice, fixedEntry.highDepth);
                    fixedEntry.nearest = importer.filterMode == FilterMode.Point;
                    ReadDescriptor(fixedEntry);
                    fixedEntry.border = importer.spriteBorder;
                    Describe(fixedEntry);
                    index.Add(groups.Count, fixedEntry.contract, fixedEntry.hash, fixedEntry.pixels.pixelHash, fixedEntry.features);
                    groups.Add(new Group { master = fixedEntry, immutable = true });
                }
                catch (Exception ex) { converter.RecordGenerationIssue(null, "image_candidate_unreadable", path + ": " + ex.Message); }
            }
            entries.Sort((a, b) => {
                int size = ((long)b.pixels.width * b.pixels.height).CompareTo((long)a.pixels.width * a.pixels.height);
                if (size != 0) return size;
                int prior = IsPriorMaster(b).CompareTo(IsPriorMaster(a));
                if (prior != 0) return prior;
                // Renaming a layer must not choose a different existing asset/GUID.
                return string.CompareOrdinal(a.previous ?? a.suggested, b.previous ?? b.suggested);
            });
            int reused = 0, precise = 0, noise = 0, scaled = 0, sliced = 0;
            var merged = new List<string>();
            foreach (var entry in entries)
            {
                Group chosen = null;
                index.Query(entry.contract, entry.hash, entry.pixels.pixelHash, entry.features, candidates);
                foreach (int candidate in candidates)
                {
                    var group = groups[candidate];
                    var master = group.master;
                    if (entry.contract != master.contract) continue;
                    if (entry.highDepth && (entry.hash != master.hash || entry.slice && entry.border != master.border)) continue;
                    if (!entry.slice && (master.pixels.width < entry.pixels.width || master.pixels.height < entry.pixels.height)) continue;
                    PsdImageComparer.Match match;
                    if (entry.hash == master.hash && (!entry.slice || entry.border == master.border)) match = PsdImageComparer.Match.Exact;
                    else
                    {
                        LoadPair(master, entry);
                        match = PsdImageComparer.Compare(master.pixels, entry.pixels, master.border, entry.border, entry.slice, entry.customBorder, tolerance);
                    }
                    if (match == PsdImageComparer.Match.Different) continue;
                    if (entry.slice && (entry.hash != master.hash || entry.border != master.border) && !MatchesDrawSizes(master, entry)) continue;
                    chosen = group; reused++;
                    if (match == PsdImageComparer.Match.Exact) precise++;
                    else
                    {
                        if (match == PsdImageComparer.Match.EdgeTolerance) noise++;
                        if (match == PsdImageComparer.Match.Scaled) scaled++;
                        if (match == PsdImageComparer.Match.Sliced) sliced++;
                        // 非字节相同的归并要留痕,误合时可据此回溯是哪一对、哪一档判据放行的。
                        if (merged.Count < ReuseDetailLimit)
                            merged.Add(DisplayName(entry) + "→" + DisplayName(master) + "(" + TierName(match)
                                + (match == PsdImageComparer.Match.Sliced && entry.border != master.border ? ", 切线 " + entry.border + "→" + master.border : "") + ")");
                    }
                    break;
                }
                if (chosen == null)
                {
                    chosen = new Group { master = entry };
                    index.Add(groups.Count, entry.contract, entry.hash, entry.pixels.pixelHash, entry.features);
                    groups.Add(chosen);
                }
                chosen.members.Add(entry);
            }
            var reserved = new HashSet<string>(protectedPaths, StringComparer.OrdinalIgnoreCase);
            var owners = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            var unchangedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
                foreach (var entry in group.members)
                {
                    if (string.IsNullOrEmpty(entry.previous) || protectedPaths.Contains(entry.previous)) continue;
                    bool unchanged = entry.existing || entry.record.contentHash == entry.hash;
                    if (!owners.ContainsKey(entry.previous) || unchanged && !unchangedOwners.Contains(entry.previous)) owners[entry.previous] = group;
                    if (unchanged) unchangedOwners.Add(entry.previous);
                }
            foreach (var group in groups)
            {
                if (group.members.Count == 0) continue;
                string path = group.master.resolved;
                try
                {
                    if (!group.immutable) path = WriteMaster(group, owners, reserved);
                    string assetHash = Psd2UIFormConverter.ImageHash(File.ReadAllBytes(path));
                    foreach (var entry in group.members) Resolve(entry, path, group.master.hash, assetHash, group.master.generatedBorder, true);
                }
                catch (Exception ex)
                {
                    foreach (var entry in group.members)
                    {
                        converter.RecordGenerationIssue(entry.node, "image_export_failed", entry.suggested + ": " + ex.Message);
                        Resolve(entry, entry.previous, null, null, Vector4.zero, false);
                    }
                }
            }
            if (output != null && Replacements.Count > 0) ReplaceReferences(output, Replacements);
            int bound = 0, retained = 0;
            foreach (var entry in entries)
                if (entry.applied) bound++;
                else if (!string.IsNullOrEmpty(entry.resolved)) retained++;
            converter.Document.imageReuseSummary = $"{entries.Count} 个图片请求，成功绑定 {bound} 个，保留旧图 {retained} 个，未绑定 {entries.Count - bound - retained} 个，复用候选 {reused} 个（相同 {precise}，噪声 {noise}，缩放 {scaled}，九宫格 {sliced}）。"
                + (merged.Count == 0 ? string.Empty : " 非完全相同归并: " + string.Join("、", merged) + "。");
            EditorUtility.SetDirty(converter.Document);
            if (AssetDatabase.Contains(converter.Document)) PsdEditorAssetUtility.SaveAssetIfDirty(converter.Document);
        }

        private string WriteMaster(Group group, Dictionary<string, Group> owners, HashSet<string> reserved)
        {
            var entry = group.master;
            string path = entry.previous;
            if (string.IsNullOrEmpty(path) || reserved.Contains(path) || !owners.TryGetValue(path, out var owner) || owner != group)
            {
                path = null;
                foreach (var member in group.members)
                    if (!string.IsNullOrEmpty(member.previous) && !reserved.Contains(member.previous)
                        && owners.TryGetValue(member.previous, out owner) && owner == group) { path = member.previous; break; }
                if (path == null) path = AssetDatabase.GenerateUniqueAssetPath(entry.suggested);
            }
            reserved.Add(path);
            // AssetDatabase can still resolve a GUID after an external deletion. The output must exist on disk.
            bool unchanged = path == entry.previous && File.Exists(path) && (entry.existing || entry.record.masterSourceHash == entry.hash
                && entry.record.assetContentHash == Psd2UIFormConverter.ImageHash(File.ReadAllBytes(path)));
            if (unchanged) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(entry.source, path, true);
            if (compress) Psd2UIFormConverter.CompressImageFile(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Psd2UIFormConverter.ConvertTexturesType(new[] { path }, entry.sprite, entry.highDepth);
            if (entry.slice)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.spriteBorder != entry.border) { importer.spriteBorder = entry.border; importer.SaveAndReimport(); }
                if (crop) RightClickExtension.TryCropMinimalNineSlice(path);
            }
            return path;
        }

        private void Resolve(Entry entry, string path, string masterHash, string assetHash, Vector4 generatedBorder, bool success)
        {
            bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            var sprite = exists && entry.sprite ? PsdLayerNode.LoadSpriteAssetAtPath(path) : null;
            var texture = exists ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
            bool valid = entry.sprite ? sprite != null : texture != null;
            entry.resolved = valid ? path : null;
            entry.applied = success && valid;
            if (entry.temporarySprite != null) Replacements[entry.temporarySprite] = sprite;
            if (entry.temporaryTexture != null) Replacements[entry.temporaryTexture] = texture;
            if (success && !valid) converter.RecordGenerationIssue(entry.node, "image_binding_failed", path + ": 导出图片未能加载为所需资源类型。");
            if (!entry.applied) return;
            if (entry.existing && entry.previous != path)
            {
                var oldSprite = PsdLayerNode.LoadSpriteAssetAtPath(entry.previous);
                var oldTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.previous);
                if (oldSprite != null) Replacements[oldSprite] = sprite;
                if (oldTexture != null) Replacements[oldTexture] = texture;
            }
            var record = entry.record;
            if (record == null) { record = new PsdImageRecord { nodeId = entry.node.PersistentId }; converter.Document.images.Add(record); }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(record.assetGuid) && record.assetGuid != guid
                && !converter.Document.retiredImages.Exists(r => r.fromGuid == record.assetGuid && r.toGuid == guid))
                converter.Document.retiredImages.Add(new PsdImageAlias { fromGuid = record.assetGuid, toGuid = guid });
            record.assetGuid = guid; record.path = path;
            if (!entry.existing) record.contentHash = entry.hash;
            record.masterSourceHash = masterHash;
            record.assetContentHash = assetHash;
            record.settingsKey = entry.settings;
            // Store the automatic value, not a retained user override: subsequent PSD
            // updates must still recognize that the current importer border is user-owned.
            record.generatedBorder = generatedBorder;
            record.hasGeneratedBorder = true;
            converter.RegisterExportedImagePath(path);
        }

        private void LoadPair(Entry master, Entry member)
        {
            LoadPixels(master); LoadPixels(member);
            TrimPixels(master.pixels, member.pixels);
        }

        private void LoadPixels(Entry entry)
        {
            if (entry.pixels.pixels == null)
            {
                entry.pixels.pixels = PsdImagePixels.Read(entry.source, false, entry.nearest).pixels;
                residentBytes += entry.pixels.pixels.LongLength * 4;
            }
            resident.Remove(entry.pixels); resident.Add(entry.pixels);
            PeakResidentBytes = Math.Max(PeakResidentBytes, residentBytes);
        }

        private void TrimPixels(PsdImagePixels first, PsdImagePixels second)
        {
            for (int i = 0; residentBytes > 64L * 1024 * 1024 && i < resident.Count;)
            {
                var item = resident[i];
                if (item == first || item == second) { i++; continue; }
                resident.RemoveAt(i);
                residentBytes -= item.pixels.LongLength * 4; item.pixels = null;
            }
        }

        private void ReadDescriptor(Entry entry)
        {
            string key = entry.hash + ":" + entry.slice + ":" + entry.nearest;
            if (!descriptors.TryGetValue(key, out entry.pixels))
            {
                entry.pixels = PsdImagePixels.Read(entry.source, entry.slice, entry.nearest);
                descriptors.Add(key, entry.pixels);
                resident.Add(entry.pixels);
                residentBytes += entry.pixels.pixels.LongLength * 4;
                PeakResidentBytes = Math.Max(PeakResidentBytes, residentBytes);
            }
            entry.border = entry.pixels.border;
            entry.generatedBorder = entry.existing && entry.record != null && entry.record.hasGeneratedBorder
                ? entry.record.generatedBorder : entry.border;
        }

        private void Describe(Entry entry)
        {
            string key = entry.hash + ":" + entry.contract + ":" + entry.border.x + ":" + entry.border.y + ":" + entry.border.z + ":" + entry.border.w;
            if (!featureCache.TryGetValue(key, out entry.features))
            {
                LoadPixels(entry);
                entry.features = PsdImageCandidateIndex.Describe(entry.pixels, entry.border, entry.slice);
                featureCache.Add(key, entry.features);
            }
            TrimPixels(entry.pixels, null);
        }

        private static bool IsHighDepth(string settings) => settings != null && settings.IndexOf(":True:", StringComparison.Ordinal) == settings.IndexOf(':') && settings.IndexOf(':') >= 0;

        private static bool IsPriorMaster(Entry entry) => !string.IsNullOrEmpty(entry.previous) && File.Exists(entry.previous)
            && (entry.existing || entry.record != null && entry.record.masterSourceHash == entry.hash);

        private bool MatchesDrawSizes(Entry master, Entry member)
        {
            LoadPair(master, member);
            foreach (var size in member.drawSizes)
                if (!PsdImageComparer.SlicedAt(master.pixels, member.pixels, master.border, member.border, size.width, size.height, size.fillCenter, tolerance)) return false;
            return true;
        }

        private static string DisplayName(Entry entry)
            => string.IsNullOrEmpty(entry.suggested) ? "<未命名>" : Path.GetFileNameWithoutExtension(entry.suggested);

        private static string TierName(PsdImageComparer.Match match)
        {
            switch (match)
            {
                case PsdImageComparer.Match.EdgeTolerance: return "噪声";
                case PsdImageComparer.Match.Scaled: return "缩放";
                default: return "九宫格";
            }
        }

        internal static string ImportContract(string path, bool sprite, bool slice, bool highDepth)
        {
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;
            var settings = new TextureImporterSettings();
            if (importer != null) importer.ReadTextureSettings(settings);
            else
            {
                settings.filterMode = FilterMode.Bilinear;
                settings.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                settings.spritePixelsPerUnit = 100; settings.spriteMeshType = SpriteMeshType.Tight;
                settings.sRGBTexture = true; settings.mipmapEnabled = false;
            }
            string contract = sprite + ":" + slice + ":" + highDepth + ":" + settings.filterMode + ":" + settings.wrapModeU + ":" + settings.wrapModeV
                + ":" + settings.spritePixelsPerUnit + ":" + settings.spriteMeshType + ":" + settings.sRGBTexture + ":" + settings.mipmapEnabled
                + ":" + (importer != null ? importer.maxTextureSize : 16384) + ":" + (importer != null ? importer.textureCompression : TextureImporterCompression.Uncompressed)
                + ":" + (importer == null || importer.alphaIsTransparency) + ":" + (importer != null ? importer.alphaSource : TextureImporterAlphaSource.FromInput);
            if (importer != null)
                foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                {
                    var platformSettings = importer.GetPlatformTextureSettings(platform);
                    if (platformSettings.overridden) contract += ":" + JsonUtility.ToJson(platformSettings);
                }
            return contract;
        }

        internal static int ReplaceReferences(GameObject root, Dictionary<Object, Object> replacements)
        {
            int changed = 0;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null
                            || !replacements.TryGetValue(property.objectReferenceValue, out var replacement)) continue;
                        property.objectReferenceValue = replacement; changed++;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            return changed;
        }

        public void Dispose()
        {
            converter.ImageExport = previousBatch;
            foreach (var entry in entries)
            {
                if (entry.temporarySprite != null) Object.DestroyImmediate(entry.temporarySprite);
                if (entry.temporaryTexture != null) Object.DestroyImmediate(entry.temporaryTexture);
            }
        }
    }
}
#endif
