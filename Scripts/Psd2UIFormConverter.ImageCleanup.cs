#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
#if !UNITY_2021_2_OR_NEWER
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace UGF.EditorTools.Psd2UGUI
{
    public sealed partial class Psd2UIFormConverter
    {
        internal string ConsolidateImages()
        {
            int bindings = 0, removed = 0, retained = 0;
            using (var batch = new PsdImageExportBatch(this))
            {
                foreach (var record in Document.images)
                    try { batch.AddExisting(record); }
                    catch (Exception ex) { RecordGenerationIssue(null, "image_candidate_unreadable", record.path + ": " + ex.Message); }
                foreach (var output in Document.outputs)
                {
                    string path = AssetDatabase.GUIDToAssetPath(output.PrefabAssetGuid);
                    if (string.IsNullOrEmpty(path)) path = output.PrefabAssetPath;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null) batch.ObserveBindings(prefab);
                }
                batch.Complete(null);
                AddRetiredImageReplacements(batch.Replacements);
                foreach (var output in Document.outputs)
                {
                    string path = AssetDatabase.GUIDToAssetPath(output.PrefabAssetGuid);
                    if (string.IsNullOrEmpty(path)) path = output.PrefabAssetPath;
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset == null) continue;
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage != null && stage.assetPath == path)
                    {
                        RecordGenerationIssue(null, "image_output_open", "请关闭生成结果的 Prefab 编辑模式后再次整理: " + path);
                        continue;
                    }
                    GameObject contents = null;
                    try
                    {
                        contents = PrefabUtility.LoadPrefabContents(path);
                        var baseline = new Dictionary<string, PsdPropertyRecord>(StringComparer.Ordinal);
                        foreach (var property in output.Properties) baseline[property.targetId] = property;
                        int changed = 0;
                        foreach (var component in contents.GetComponentsInChildren<Component>(true))
                        {
                            if (component == null) continue;
                            var saved = PsdManagedProperties.ResolveSavedObject(component, contents.transform, asset.transform);
                            if (saved == null || !baseline.TryGetValue(GlobalObjectId.GetGlobalObjectIdSlow(saved).ToString(), out var record)) continue;
                            using (var serialized = new SerializedObject(component))
                            {
                                foreach (var value in record.values)
                                {
                                    if (value.asset == null || !batch.Replacements.TryGetValue(value.asset, out var replacement)) continue;
                                    var property = serialized.FindProperty(value.path);
                                    // Keep user overrides. Only migrate a binding that still equals the last generated value.
                                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != value.asset) continue;
                                    property.objectReferenceValue = replacement; changed++;
                                }
                                serialized.ApplyModifiedPropertiesWithoutUndo();
                            }
                        }
                        if (changed > 0 && PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException("保存 prefab 失败: " + path);
                        bindings += changed;
                        foreach (var record in output.Properties)
                            foreach (var value in record.values)
                                if (value.asset != null && batch.Replacements.TryGetValue(value.asset, out var replacement)) value.asset = replacement;
                    }
                    catch (Exception ex) { RecordGenerationIssue(null, "image_output_migration_failed", path + ": " + ex.Message); }
                    finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
                }
            }
            EditorUtility.SetDirty(Document);
            if (AssetDatabase.Contains(Document)) PsdEditorAssetUtility.SaveAssetIfDirty(Document);
            var retired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in Document.retiredImages)
            {
                if (Document.images.Exists(r => r.assetGuid == alias.fromGuid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(alias.fromGuid);
                if (!string.IsNullOrEmpty(path)) retired[path] = alias.fromGuid;
            }
            if (retired.Count > 0)
            {
                HashSet<string> used;
                try { used = FindProtectedImages(retired); }
                catch (Exception ex)
                {
                    used = new HashSet<string>(retired.Keys, StringComparer.OrdinalIgnoreCase);
                    RecordGenerationIssue(null, "image_dependency_scan_failed", "引用检查未完成，保留旧图: " + ex.Message);
                }
                foreach (var pair in retired)
                {
                    if (used.Contains(pair.Key)) { retained++; continue; }
                    try { if (AssetDatabase.DeleteAsset(pair.Key)) removed++; else retained++; }
                    catch (Exception ex) { retained++; RecordGenerationIssue(null, "image_cleanup_failed", pair.Key + ": " + ex.Message); }
                }
            }
            Document.retiredImages.RemoveAll(r => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(r.fromGuid)));
            Document.imageReuseSummary += $" 已更新 {bindings} 处绑定，清理 {removed} 张闲置图片，保留 {retained} 张受引用或路径保护的图片。";
            EditorUtility.SetDirty(Document);
            if (AssetDatabase.Contains(Document)) PsdEditorAssetUtility.SaveAssetIfDirty(Document);
            return Document.imageReuseSummary;
        }

        private void AddRetiredImageReplacements(Dictionary<Object, Object> replacements)
        {
            var registeredImages = PsdResourceRegistry.ImageGuids();
            foreach (var alias in Document.retiredImages)
            {
                if (registeredImages.Contains(alias.fromGuid)) continue;
                if (Document.images.Exists(r => r.assetGuid == alias.fromGuid)) continue;
                string from = AssetDatabase.GUIDToAssetPath(alias.fromGuid), to = AssetDatabase.GUIDToAssetPath(alias.toGuid);
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) continue;
                try
                {
                    if (!(AssetImporter.GetAtPath(from) is TextureImporter a) || !(AssetImporter.GetAtPath(to) is TextureImporter b)
                        || a.textureType != b.textureType || (a.spriteBorder == Vector4.zero) != (b.spriteBorder == Vector4.zero)) continue;
                    bool sprite = a.textureType == TextureImporterType.Sprite, sliced = b.spriteBorder != Vector4.zero;
                    if (PsdImageExportBatch.ImportContract(from, sprite, sliced, false) != PsdImageExportBatch.ImportContract(to, sprite, sliced, false)) continue;
                    if (PsdTextureAssetUtility.MatchesExportMode(from, true) || PsdTextureAssetUtility.MatchesExportMode(to, true))
                        if (a.spriteBorder != b.spriteBorder || ImageHash(File.ReadAllBytes(from)) != ImageHash(File.ReadAllBytes(to))) continue;
                    var oldPixels = PsdImagePixels.Read(from, sliced, a.filterMode == FilterMode.Point);
                    var newPixels = PsdImagePixels.Read(to, false, b.filterMode == FilterMode.Point);
                    if (PsdImageComparer.Compare(newPixels, oldPixels, b.spriteBorder, a.spriteBorder, sliced, true, Psd2UIFormSettings.Instance.ImageReuseTolerance) == PsdImageComparer.Match.Different) continue;
                    var oldSprite = PsdLayerNode.LoadSpriteAssetAtPath(from); var newSprite = PsdLayerNode.LoadSpriteAssetAtPath(to);
                    if (oldSprite != null && newSprite != null) replacements[oldSprite] = newSprite;
                    var oldTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(from); var newTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(to);
                    if (oldTexture != null && newTexture != null) replacements[oldTexture] = newTexture;
                }
                catch (Exception ex) { RecordGenerationIssue(null, "image_alias_unreadable", from + ": " + ex.Message); }
            }
        }

        private HashSet<string> FindProtectedImages(Dictionary<string, string> retired)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string exportDirectory = GetUIFormImagesOutputDir().Replace('\\', '/').TrimEnd('/') + "/";
            foreach (string path in retired.Keys)
                if (!path.StartsWith(exportDirectory, StringComparison.OrdinalIgnoreCase) || path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0
                    || AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path)).Length > 0) used.Add(path);
            foreach (string guid in AssetDatabase.FindAssets("t:PsdResourceRegistry"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<PsdResourceRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var pair in retired) if (registry.ContainsGuid(pair.Value)) used.Add(pair.Key);
            }
            // Editor documents contain GUID strings as well as object references.
            foreach (string guid in AssetDatabase.FindAssets("t:PsdUiDocument"))
            {
                var document = AssetDatabase.LoadAssetAtPath<PsdUiDocument>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var image in document.images)
                {
                    string path = AssetDatabase.GUIDToAssetPath(image.assetGuid);
                    if (retired.ContainsKey(path)) used.Add(path);
                }
            }
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path) || retired.ContainsKey(path)) continue;
                foreach (string dependency in AssetDatabase.GetDependencies(path, false)) if (retired.ContainsKey(dependency)) used.Add(dependency);
                // Addressable entries and code-driven loads need not appear in Unity's dependency graph.
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension != ".cs" && extension != ".json" && extension != ".asset" && extension != ".uxml") continue;
                if (path == AssetDatabase.GetAssetPath(Document) || new FileInfo(path).Length > 4 * 1024 * 1024) continue;
                string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                foreach (var pair in retired)
                    if (text.IndexOf(pair.Value, StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf(pair.Key, StringComparison.Ordinal) >= 0) used.Add(pair.Key);
            }
            return used;
        }
    }
}
#endif
