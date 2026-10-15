/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 *
 * The software and associated documentation (including but not limited to source code,
 * object code, documents, images, audio, and video files) are the intellectual property
 * of Beijing Yiqu Technology Co., Ltd.
 *
 * For commercial use or licensing inquiries, please contact:
 * Email: efunstudio@gmail.com
 * Website: https://efunstudio.cn
 */

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using cn.efunstudio.psdreader;
using cn.efunstudio.psdreader.PsdParser;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiAnalysisBundleBuilder
    {
        private sealed class SharedBundleContext
        {
            public string SharedBundleDirectory;
            public string SharedNodePreviewDirectory;
            public string AnnotatedPreviewPath;
            public string AtlasDirectory;
        }

        private sealed class NodePreviewExportContext
        {
            public readonly Dictionary<string, string> ContentHashToPath = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly HashSet<string> UsedPreviewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string ProjectRoot;
        }

        private struct PreviewCanvasGeometry
        {
            public int CanvasLeft;
            public int CanvasTop;
            public int CanvasWidth;
            public int CanvasHeight;
            public int PsdDocWidth;
            public int PsdDocHeight;
        }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private const string PreviewFileName = "preview.png";
        private const int AtlasColumns = 8;
        private const int AtlasRows = 4;
        private const int AtlasCellWidth = 256;
        private const int AtlasCellHeight = 220;
        private const int AtlasLabelHeight = 34;
        private const int AtlasCellPadding = 8;
        private const int AtlasGridBorderWidth = 1;
        private const int AtlasImageSafePadding = 12;
        private const int NodePreviewTransparentPadding = 4;
        private const float AtlasSourceTransparentBorderPixels = 1f;
        private static readonly Color32 TransparentBlack = new Color32(0, 0, 0, 0);
        private static readonly Color32 AtlasGridBorderColor = new Color32(176, 176, 176, 255);

        internal bool Build(Psd2UIFormConverter converter, AiJobContext context, out string treeHash, out string error)
        {
            treeHash = string.Empty;
            error = null;
            if (converter == null)
            {
                error = "Converter is null.";
                return false;
            }
            if (context == null)
            {
                error = "AI job context is null.";
                return false;
            }

            try
            {
                var exportTimer = System.Diagnostics.Stopwatch.StartNew();
                var sharedBundle = BuildSharedBundleContext(converter, context);
                var previewExportContext = new NodePreviewExportContext
                {
                    ProjectRoot = Directory.GetParent(Application.dataPath).FullName
                };
                var package = new AiAnalysisPackageDocument
                {
                    version = AiProtocolVersions.AnalysisPackage,
                    document = BuildDocumentInfo(converter, sharedBundle, out var geometry)
                };

                int nextIndex = 0;
                BuildSourceEntries(converter.PsdInstance.Childs, string.Empty, ref nextIndex, package,
                    sharedBundle, previewExportContext, geometry);
                new AiRenderOrderIndex(package.nodes);
                CleanupUnusedNodePreviews(sharedBundle, previewExportContext);
                BuildVisualInputs(package, sharedBundle, previewExportContext.ProjectRoot);
                CleanupUnusedAtlasPages(sharedBundle, package);
                NormalizePackageImagePaths(package);

                string packageJson = JsonUtility.ToJson(package, false);
                treeHash = ComputeSha256(packageJson);
                package.treeHash = treeHash;
                packageJson = JsonUtility.ToJson(package, false);

                AiJobFileUtility.WriteText(context.AnalysisPackagePath, packageJson);
                AiJobFileUtility.WriteJson(Path.Combine(context.RequestDirectory, "export-metrics.json"),
                    new AiRecognitionRunReport { exportMilliseconds = exportTimer.ElapsedMilliseconds });
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to build AI analysis package: {ex.Message}";
                return false;
            }
        }

        private static SharedBundleContext BuildSharedBundleContext(Psd2UIFormConverter converter, AiJobContext context)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string bundleDirectory = context != null && !string.IsNullOrWhiteSpace(context.JobDirectory)
                ? Path.Combine(context.JobDirectory, "img")
                : Path.Combine(projectRoot, "Library", "Psd2UIForm", AiPathDefaults.GetPsdAssetFolderName(converter != null ? converter.PsdAssetName : null), "img");
            return new SharedBundleContext
            {
                SharedBundleDirectory = bundleDirectory,
                // All exported images live directly under img. The manifest declares
                // that root once and all image references are basenames.
                SharedNodePreviewDirectory = bundleDirectory,
                AtlasDirectory = bundleDirectory,
                AnnotatedPreviewPath = Path.Combine(bundleDirectory, "annotated.png")
            };
        }

        private static AiAnalysisDocumentInfo BuildDocumentInfo(Psd2UIFormConverter converter, SharedBundleContext sharedBundle, out PreviewCanvasGeometry geometry)
        {
            geometry = default;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string previewImagePath = Path.Combine(sharedBundle.SharedBundleDirectory, PreviewFileName);

            int canvasLeft = 0, canvasTop = 0, canvasWidth = 0, canvasHeight = 0;
            int psdDocWidth = 0, psdDocHeight = 0;

            var psdInstance = converter.PsdInstance;
            byte[] previewBytes = null;
            if (psdInstance != null)
            {
                psdDocWidth = psdInstance.Width;
                psdDocHeight = psdInstance.Height;
                try
                {
                    previewBytes = Psd2UIFormConverter.BuildAnalysisPreviewPng(psdInstance, out canvasLeft, out canvasTop, out canvasWidth, out canvasHeight);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"AI 分析包预览图合成失败: {ex.Message}");
                    previewBytes = null;
                }
            }

            if (previewBytes != null && previewBytes.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(previewImagePath));
                File.WriteAllBytes(previewImagePath, previewBytes);
            }
            else
            {
                var sprite = converter.BindPsdAsset;
                if (sprite != null && sprite.texture != null)
                {
                    // 上一轮的 preview.png 可能是按裁剪画布写的，而这里的几何是整图且 left/top=0；
                    // IfMissing 会直接复用旧文件导致坐标对不上，所以先删掉再按整图重出。
                    AiJobFileUtility.DeleteFileIfExists(previewImagePath);
                    ExportTextureToPngIfMissing(sprite.texture, previewImagePath);
                    canvasWidth = sprite.texture.width;
                    canvasHeight = sprite.texture.height;
                    if (psdDocWidth <= 0) psdDocWidth = canvasWidth;
                    if (psdDocHeight <= 0) psdDocHeight = canvasHeight;
                }
            }

            geometry = new PreviewCanvasGeometry
            {
                CanvasLeft = canvasLeft,
                CanvasTop = canvasTop,
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                PsdDocWidth = psdDocWidth,
                PsdDocHeight = psdDocHeight
            };

            string sourceHash = ComputeFileHash(converter.PsdAssetName);
            if (string.IsNullOrEmpty(sourceHash))
                Debug.LogWarning($"AI 分析包读不到源文档哈希（{converter.PsdAssetName}），源变更探针本轮失效。");
            return new AiAnalysisDocumentInfo
            {
                sourceHash = sourceHash,
                imageRoot = string.Empty,
                previewImagePath = MakeRelativePath(projectRoot, previewImagePath),
                nodePreviewDirectoryPath = MakeRelativePath(projectRoot, sharedBundle.SharedNodePreviewDirectory),
                nodeAtlasDirectoryPath = MakeRelativePath(projectRoot, sharedBundle.AtlasDirectory),
                canvasLeft = canvasLeft, canvasTop = canvasTop, psdWidth = psdDocWidth, psdHeight = psdDocHeight,
                width = canvasWidth,
                height = canvasHeight
            };
        }

        private static void BuildSourceEntries(PsdLayer[] layers, string parent, ref int nextIndex,
            AiAnalysisPackageDocument package, SharedBundleContext bundle, NodePreviewExportContext export,
            PreviewCanvasGeometry geometry)
        {
            for (int sibling = 0; sibling < layers.Length; sibling++)
            {
                var layer = layers[sibling];
                int flatIndex = layer.IsGroup ? nextIndex + layer.CountFlatIndexSlots() - 1 : nextIndex;
                string id = "psd:" + flatIndex;
                bool textLayer = layer.TryGetTextLayerInfo(out var text);
                var entry = new AiAnalysisNodeEntry {
                    id = id, shortId = "S" + package.nodes.Count.ToString("000"), parentId = parent,
                    name = layer.Name, layerName = layer.Name, text = textLayer ? text.Text : string.Empty,
                    layerType = layer.GetLayerType().ToString(), isGroupLayer = layer.IsGroup, isTextLayer = textLayer,
                    rect = new RectData { x = layer.Left - geometry.CanvasLeft, y = layer.Top - geometry.CanvasTop, w = layer.Width, h = layer.Height },
                    active = layer.IsVisibleInHierarchy, siblingIndex = sibling, childCount = layer.Childs.Length,
                    compositeBoundary = layer.IsGroup && !layer.CanRegroupChildren(),
                    clipping = layer.IsClipping,
                    requiresBackdrop = layer.RequiresBackdropComposition,
                    sourceIndex = flatIndex, sourceLayerId = layer.TryGetLayerId(out int nativeId) ? nativeId : -1
                };
                if (textLayer && text.StyleRuns != null && text.StyleRuns.Length > 0)
                {
                    var style = PsdLayerNode.ConvertTextStyleRun(text.StyleRuns[0]);
                    entry.textStyle = new AiTextStyle { font = style.FontName, fontSize = style.FontSize,
                        color = "#" + ColorUtility.ToHtmlStringRGBA(style.Color), fontStyle = style.FontStyle.ToString() };
                }
                PsdResourceNaming.Parse(layer.Name, out entry.referenceKind, out entry.referenceKey);
                package.nodes.Add(entry);
                var tree = AiSourceRenderTree.Build(layer.Document, new HashSet<PsdLayer> { layer });
                // 组证据按 PS 观感合成：整图预览走 Render()（includeHiddenLayers 默认 false，递归跳过隐藏子层），
                // 这里若带进隐藏子层就和预览对不上。隐藏信息不会丢：nodes.tsv 的 active 列 + TaskPrompt 已说明。
                var rendered = PsdLayerRenderer.RenderTree(tree, includeHiddenLayers: !layer.IsVisible, isPreviewRender: false);
                var texture = CreateTextureFromRenderedImage(rendered);
                if (texture != null)
                {
                    try {
                        string path = ExportTextureToPngIfMissing(texture, Path.Combine(bundle.SharedNodePreviewDirectory, entry.shortId + ".png"), export, NodePreviewTransparentPadding);
                        entry.imageFile = MakeRelativePath(export.ProjectRoot, path);
                        entry.visualHash = ComputeFileHash(entry.imageFile);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
                // Explicit reuse groups are opaque resources. Their native descendants remain in the
                // PSD render tree, but must not become additional generated UI/assets.
                if (layer.IsGroup && string.IsNullOrEmpty(entry.referenceKind))
                {
                    int childIndex = nextIndex + 1;
                    int start = package.nodes.Count;
                    BuildSourceEntries(layer.Childs, id, ref childIndex, package, bundle, export, geometry);
                    for (int i = start; i < package.nodes.Count; i++)
                        if (!package.nodes[i].isGroupLayer) entry.renderLeafCount++;
                }
                else entry.renderLeafCount = 1;
                nextIndex += layer.CountFlatIndexSlots();
            }
        }

        private static Texture2D CreateTextureFromRenderedImage(PsdRenderedImage rendered)
        {
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            GraphicsFormat textureFormat = rendered.IsHighBitDepth ? GraphicsFormat.R16G16B16A16_UNorm : GraphicsFormat.R8G8B8A8_SRGB;
            var texture = new Texture2D(rendered.Width, rendered.Height, textureFormat, TextureCreationFlags.None);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.alphaIsTransparency = true;
            if (rendered.IsHighBitDepth)
            {
                ushort[] rgba64 = rendered.Rgba64;
                byte[] rawData = new byte[rgba64.Length * sizeof(ushort)];
                Buffer.BlockCopy(rgba64, 0, rawData, 0, rawData.Length);
                texture.LoadRawTextureData(rawData);
            }
            else
            {
                texture.LoadRawTextureData(rendered.Rgba32);
            }

            texture.Apply(false, false);
            return texture;
        }

        private static string ComputeFileHash(string relativeOrAbsolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string resolvedPath = relativeOrAbsolutePath;
            if (!string.IsNullOrWhiteSpace(resolvedPath) && !Path.IsPathRooted(resolvedPath))
            {
                resolvedPath = Path.Combine(projectRoot, resolvedPath);
            }

            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(resolvedPath))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static void BuildVisualInputs(AiAnalysisPackageDocument package, SharedBundleContext sharedBundle, string projectRoot)
        {
            if (package == null || package.document == null || sharedBundle == null || string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            package.document.visualInputPaths.Clear();
            AddVisualInput(package.document.visualInputPaths, package.document.previewImagePath);

            BuildAnnotatedPreview(package, sharedBundle, projectRoot);
            AddVisualInput(package.document.visualInputPaths, package.document.annotatedPreviewImagePath);

            BuildNodeAtlases(package, sharedBundle, projectRoot);
        }

        private static void NormalizePackageImagePaths(AiAnalysisPackageDocument package)
        {
            if (package == null || package.document == null)
            {
                return;
            }

            package.document.imageRoot = "img";
            package.document.previewImagePath = Path.GetFileName(package.document.previewImagePath ?? string.Empty);
            package.document.annotatedPreviewImagePath = Path.GetFileName(package.document.annotatedPreviewImagePath ?? string.Empty);
            package.document.nodePreviewDirectoryPath = "img";
            package.document.nodeAtlasDirectoryPath = "img";

            if (package.nodes == null)
            {
                return;
            }

            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null)
                {
                    continue;
                }

                node.imageFile = Path.GetFileName(node.imageFile ?? string.Empty);
                if (node.atlas != null)
                {
                    node.atlas.page = Path.GetFileName(node.atlas.page ?? string.Empty);
                }
            }
        }

        private static void BuildAnnotatedPreview(AiAnalysisPackageDocument package, SharedBundleContext sharedBundle, string projectRoot)
        {
            var texture = LoadPngTexture(ResolveProjectRelativePath(projectRoot, package.document.previewImagePath));
            // 标注图是可选证据：读不到就跳过，不能让整次识别输入构建失败。
            if (texture == null) return;
            try
            {
                foreach (var node in package.nodes)
                {
                    if (node.rect == null || node.rect.w <= 0 || node.rect.h <= 0 || string.IsNullOrEmpty(node.shortId)) continue;
                    int x = Mathf.RoundToInt(node.rect.x), y = Mathf.RoundToInt(node.rect.y);
                    DrawRect(texture, x, y, Mathf.RoundToInt(node.rect.w), Mathf.RoundToInt(node.rect.h), new Color32(255, 215, 64, 255));
                    x = Mathf.Clamp(x, 0, Mathf.Max(0, texture.width - 38));
                    y = Mathf.Clamp(y - 13, 0, Mathf.Max(0, texture.height - 13));
                    FillRect(texture, x, y, 38, 13, new Color32(0, 0, 0, 190));
                    DrawText(texture, node.shortId.ToUpperInvariant(), x + 3, y + 3, new Color32(255, 255, 255, 255), 1);
                }
                texture.Apply(false, false);
                File.WriteAllBytes(sharedBundle.AnnotatedPreviewPath, texture.EncodeToPNG());
                package.document.annotatedPreviewImagePath = MakeRelativePath(projectRoot, sharedBundle.AnnotatedPreviewPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void AddVisualInput(List<string> visualInputPaths, string path)
        {
            if (visualInputPaths == null || string.IsNullOrWhiteSpace(path)) return;
            for (int i = 0; i < visualInputPaths.Count; i++)
            {
                if (string.Equals(visualInputPaths[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            visualInputPaths.Add(path);
        }

        private static void BuildNodeAtlases(AiAnalysisPackageDocument package, SharedBundleContext sharedBundle, string projectRoot)
        {
            if (package == null || package.nodes == null || package.nodes.Count == 0 || sharedBundle == null)
            {
                return;
            }

            Directory.CreateDirectory(sharedBundle.AtlasDirectory);
            var visualNodes = new List<AiAnalysisNodeEntry>();
            foreach (var node in package.nodes)
                if (!string.IsNullOrEmpty(node.imageFile) && (!node.isGroupLayer || node.renderLeafCount > 1)) visualNodes.Add(node);
            int cellsPerPage = AtlasColumns * AtlasRows;
            int pageCount = (visualNodes.Count + cellsPerPage - 1) / cellsPerPage;
            for (int page = 0; page < pageCount; page++)
            {
                var pageKey = new StringBuilder("atlas-v3|");
                for (int j = page * cellsPerPage; j < Math.Min(visualNodes.Count, (page + 1) * cellsPerPage); j++)
                    pageKey.Append(visualNodes[j].visualHash).Append('|').Append(visualNodes[j].shortId).Append('|');
                string pageName = "a-" + ComputeSha256(pageKey.ToString()).Substring(0, 12) + ".png";
                string atlasPath = Path.Combine(sharedBundle.AtlasDirectory, pageName);
                bool cached = File.Exists(atlasPath);
                var atlas = cached ? null : new Texture2D(AtlasColumns * AtlasCellWidth, AtlasRows * AtlasCellHeight, TextureFormat.RGBA32, false);
                if (atlas != null) FillRect(atlas, 0, 0, atlas.width, atlas.height, TransparentBlack);

                for (int cell = 0; cell < cellsPerPage; cell++)
                {
                    int nodeIndex = page * cellsPerPage + cell;
                    if (nodeIndex >= visualNodes.Count) break;

                    var node = visualNodes[nodeIndex];
                    int col = cell % AtlasColumns;
                    int row = cell / AtlasColumns;
                    int cellX = col * AtlasCellWidth;
                    int cellY = row * AtlasCellHeight;
                    int imageX = cellX + AtlasCellPadding;
                    int imageY = cellY + AtlasCellPadding;
                    int imageW = AtlasCellWidth - AtlasCellPadding * 2;
                    int imageH = AtlasCellHeight - AtlasLabelHeight - AtlasCellPadding * 2;
                    int labelX = cellX + AtlasCellPadding;
                    int labelY = cellY + AtlasCellHeight - AtlasLabelHeight + 4;
                    int labelW = AtlasCellWidth - AtlasCellPadding * 2;
                    int labelH = AtlasLabelHeight - 8;

                    if (atlas != null) DrawRectThickness(atlas, cellX, cellY, AtlasCellWidth, AtlasCellHeight, AtlasGridBorderColor, AtlasGridBorderWidth);

                    if (node != null)
                    {
                        int previewX = imageX + AtlasImageSafePadding;
                        int previewY = imageY + AtlasImageSafePadding;
                        int previewW = imageW - AtlasImageSafePadding * 2;
                        int previewH = imageH - AtlasImageSafePadding * 2;
                        if (previewW <= 0 || previewH <= 0)
                        {
                            previewX = imageX;
                            previewY = imageY;
                            previewW = imageW;
                            previewH = imageH;
                        }

                        var actualImageRect = new RectData();
                        if (!cached) DrawNodePreviewIntoAtlas(atlas, node, projectRoot, package.document.nodePreviewDirectoryPath, previewX, previewY, previewW, previewH, out actualImageRect);
                        node.atlas = new AiAtlasRef
                        {
                            page = pageName,
                            cell = cell,
                            label = node.shortId ?? string.Empty,
                            imageRect = new RectData { x = previewX, y = previewY, w = previewW, h = previewH },
                            labelRect = new RectData { x = labelX, y = labelY, w = labelW, h = labelH }
                        };

                        string label = $"{node.shortId} {CompactLayerTypeLabel(node.layerType)}";
                        if (atlas != null) DrawTextClipped(atlas, label.ToUpperInvariant(), labelX, labelY, labelW, new Color32(245, 245, 245, 255), 2);
                    }
                }


                if (atlas != null)
                {
                    try { ZeroTransparentPixels(atlas); atlas.Apply(false, false); File.WriteAllBytes(atlasPath, atlas.EncodeToPNG()); }
                    finally { UnityEngine.Object.DestroyImmediate(atlas); }
                }
                AddVisualInput(package.document.visualInputPaths, MakeRelativePath(projectRoot, atlasPath));
            }
        }

        private static void DrawNodePreviewIntoAtlas(Texture2D atlas, AiAnalysisNodeEntry node, string projectRoot, string nodePreviewDirectoryPath, int x, int y, int w, int h, out RectData actualImageRect)
        {
            actualImageRect = new RectData { x = x, y = y, w = 0, h = 0 };
            if (atlas == null || node == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.imageFile)) return;

            string imagePath = ResolveNodePreviewPath(projectRoot, nodePreviewDirectoryPath, node.imageFile);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return;

            var source = LoadPngTexture(imagePath);
            if (source == null) return;

            try
            {
                float scale = Mathf.Min(w / (float)Mathf.Max(1, source.width), h / (float)Mathf.Max(1, source.height));
                int drawW = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int drawH = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                int drawX = x + (w - drawW) / 2;
                int drawY = y + (h - drawH) / 2;
                BlitScaled(source, atlas, drawX, drawY, drawW, drawH);
                actualImageRect = new RectData { x = drawX, y = drawY, w = drawW, h = drawH };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static string CompactLayerTypeLabel(string layerType)
        {
            if (string.IsNullOrWhiteSpace(layerType)) return "L";
            switch (layerType)
            {
                case "LayerGroup": return "G";
                case "TextLayer": return "T";
                case "FillLayer": return "F";
                case "Layer": return "L";
                default: return layerType.Length > 3 ? layerType.Substring(0, 3) : layerType;
            }
        }

        private static Texture2D LoadPngTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }
            return texture;
        }

        private static string ResolveProjectRelativePath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (Path.IsPathRooted(path)) return path;
            if (string.IsNullOrWhiteSpace(projectRoot)) return path;
            return Path.Combine(projectRoot, path).Replace("\\", "/");
        }

        private static string ResolveNodePreviewPath(string projectRoot, string nodePreviewDirectoryPath, string imageFile)
        {
            if (string.IsNullOrWhiteSpace(imageFile))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(imageFile))
            {
                return imageFile;
            }

            if (imageFile.IndexOf('/') >= 0 || imageFile.IndexOf('\\') >= 0)
            {
                return ResolveProjectRelativePath(projectRoot, imageFile);
            }

            string previewRoot = ResolveProjectRelativePath(projectRoot, nodePreviewDirectoryPath);
            if (string.IsNullOrWhiteSpace(previewRoot))
            {
                return string.Empty;
            }

            return Path.Combine(previewRoot, imageFile).Replace("\\", "/");
        }

        private static void DrawRect(Texture2D texture, int x, int y, int w, int h, Color32 color)
        {
            if (texture == null || w <= 0 || h <= 0) return;
            for (int i = 0; i < w; i++)
            {
                SetPixelTopLeft(texture, x + i, y, color);
                SetPixelTopLeft(texture, x + i, y + h - 1, color);
            }
            for (int i = 0; i < h; i++)
            {
                SetPixelTopLeft(texture, x, y + i, color);
                SetPixelTopLeft(texture, x + w - 1, y + i, color);
            }
        }

        private static void DrawRectThickness(Texture2D texture, int x, int y, int w, int h, Color32 color, int thickness)
        {
            if (texture == null || w <= 0 || h <= 0 || thickness <= 0)
            {
                return;
            }

            for (int i = 0; i < thickness; i++)
            {
                DrawRect(texture, x + i, y + i, w - i * 2, h - i * 2, color);
            }
        }

        private static void FillRect(Texture2D texture, int x, int y, int w, int h, Color32 color)
        {
            if (texture == null || w <= 0 || h <= 0) return;
            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    SetPixelTopLeft(texture, x + xx, y + yy, color);
                }
            }
        }

        private static void BlitScaled(Texture2D source, Texture2D destination, int x, int y, int w, int h)
        {
            if (source == null || destination == null || w <= 0 || h <= 0) return;
            for (int yy = 0; yy < h; yy++)
            {
                float v = h > 1 ? yy / (float)(h - 1) : 0f;
                for (int xx = 0; xx < w; xx++)
                {
                    float u = w > 1 ? xx / (float)(w - 1) : 0f;
                    var src = SamplePremultipliedBilinear(source, u, 1f - v);
                    var dst = GetPixelTopLeft(destination, x + xx, y + yy);
                    SetPixelTopLeft(destination, x + xx, y + yy, AlphaBlend(src, dst));
                }
            }
        }

        private static Color SamplePremultipliedBilinear(Texture2D texture, float u, float v)
        {
            float virtualWidth = texture.width + AtlasSourceTransparentBorderPixels * 2f;
            float virtualHeight = texture.height + AtlasSourceTransparentBorderPixels * 2f;
            float px = Mathf.Clamp01(u) * (virtualWidth - 1f) - AtlasSourceTransparentBorderPixels;
            float py = Mathf.Clamp01(v) * (virtualHeight - 1f) - AtlasSourceTransparentBorderPixels;
            int x0 = Mathf.FloorToInt(px);
            int y0 = Mathf.FloorToInt(py);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float tx = px - x0;
            float ty = py - y0;

            var c00 = GetSourcePixelOrClear(texture, x0, y0);
            var c10 = GetSourcePixelOrClear(texture, x1, y0);
            var c01 = GetSourcePixelOrClear(texture, x0, y1);
            var c11 = GetSourcePixelOrClear(texture, x1, y1);

            float w00 = (1f - tx) * (1f - ty);
            float w10 = tx * (1f - ty);
            float w01 = (1f - tx) * ty;
            float w11 = tx * ty;

            float a = c00.a * w00 + c10.a * w10 + c01.a * w01 + c11.a * w11;
            if (a <= 0.0001f) return Color.clear;

            float r = (c00.r * c00.a * w00 + c10.r * c10.a * w10 + c01.r * c01.a * w01 + c11.r * c11.a * w11) / a;
            float g = (c00.g * c00.a * w00 + c10.g * c10.a * w10 + c01.g * c01.a * w01 + c11.g * c11.a * w11) / a;
            float b = (c00.b * c00.a * w00 + c10.b * c10.a * w10 + c01.b * c01.a * w01 + c11.b * c11.a * w11) / a;
            return new Color(r, g, b, a);
        }

        private static Color GetSourcePixelOrClear(Texture2D texture, int x, int y)
        {
            if (x < 0 || y < 0 || x >= texture.width || y >= texture.height)
            {
                return Color.clear;
            }

            return texture.GetPixel(x, y);
        }

        private static Color AlphaBlend(Color src, Color dst)
        {
            float srcA = src.a;
            float dstA = dst.a;
            float invSrcA = 1f - srcA;
            float outA = srcA + dstA * invSrcA;
            if (outA <= 0.0001f)
            {
                return Color.clear;
            }

            float outR = (src.r * srcA + dst.r * dstA * invSrcA) / outA;
            float outG = (src.g * srcA + dst.g * dstA * invSrcA) / outA;
            float outB = (src.b * srcA + dst.b * dstA * invSrcA) / outA;
            return new Color(outR, outG, outB, outA);
        }

        private static Color GetPixelTopLeft(Texture2D texture, int x, int y)
        {
            if (texture == null || x < 0 || y < 0 || x >= texture.width || y >= texture.height) return Color.clear;
            return texture.GetPixel(x, texture.height - 1 - y);
        }

        private static void SetPixelTopLeft(Texture2D texture, int x, int y, Color color)
        {
            if (texture == null || x < 0 || y < 0 || x >= texture.width || y >= texture.height) return;
            texture.SetPixel(x, texture.height - 1 - y, color);
        }

        private static void DrawText(Texture2D texture, string text, int x, int y, Color32 color, int scale)
        {
            if (texture == null || string.IsNullOrEmpty(text)) return;
            if (scale < 1) scale = 1;

            int penX = x;
            int maxX = texture.width - 1;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                var glyph = GetGlyph(ch);
                if (glyph == null)
                {
                    penX += 4 * scale;
                    continue;
                }

                for (int gy = 0; gy < glyph.Length; gy++)
                {
                    string row = glyph[gy];
                    for (int gx = 0; gx < row.Length; gx++)
                    {
                        if (row[gx] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                SetPixelTopLeft(texture, penX + gx * scale + sx, y + gy * scale + sy, color);
                            }
                        }
                    }
                }

                penX += 6 * scale;
                if (penX > maxX) return;
            }
        }

        private static void DrawTextClipped(Texture2D texture, string text, int x, int y, int maxWidth, Color32 color, int scale)
        {
            if (texture == null || string.IsNullOrEmpty(text) || maxWidth <= 0) return;
            if (scale < 1) scale = 1;

            int penX = x;
            int clipRight = x + maxWidth;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                var glyph = GetGlyph(ch);
                int glyphWidth = glyph != null ? 5 * scale : 4 * scale;
                if (penX + glyphWidth > clipRight) return;
                if (glyph == null)
                {
                    penX += 4 * scale;
                    continue;
                }

                for (int gy = 0; gy < glyph.Length; gy++)
                {
                    string row = glyph[gy];
                    for (int gx = 0; gx < row.Length; gx++)
                    {
                        if (row[gx] != '1') continue;
                        int pixelX = penX + gx * scale;
                        if (pixelX >= clipRight) continue;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int targetX = pixelX + sx;
                                if (targetX < clipRight)
                                {
                                    SetPixelTopLeft(texture, targetX, y + gy * scale + sy, color);
                                }
                            }
                        }
                    }
                }

                penX += 6 * scale;
            }
        }

        private static string[] GetGlyph(char ch)
        {
            switch (char.ToUpperInvariant(ch))
            {
                case 'A': return new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'B': return new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
                case 'C': return new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
                case 'D': return new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
                case 'E': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
                case 'F': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
                case 'G': return new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01110" };
                case 'H': return new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'I': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
                case 'J': return new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
                case 'K': return new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
                case 'L': return new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
                case 'M': return new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
                case 'N': return new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
                case 'O': return new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'P': return new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
                case 'Q': return new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
                case 'R': return new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
                case 'S': return new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
                case 'T': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
                case 'U': return new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'V': return new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
                case 'W': return new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
                case 'X': return new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
                case 'Y': return new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
                case 'Z': return new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
                case '0': return new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
                case '1': return new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" };
                case '2': return new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" };
                case '3': return new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" };
                case '4': return new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" };
                case '5': return new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" };
                case '6': return new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" };
                case '7': return new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" };
                case '8': return new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };
                case '9': return new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" };
                case '_': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "11111" };
                case '-': return new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" };
                case '|': return new[] { "00100", "00100", "00100", "00100", "00100", "00100", "00100" };
                case ':': return new[] { "00000", "00100", "00100", "00000", "00100", "00100", "00000" };
                case '/': return new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" };
                case '.': return new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" };
                case ' ': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" };
                default: return null;
            }
        }

        private static void CleanupUnusedNodePreviews(SharedBundleContext sharedBundle, NodePreviewExportContext previewExportContext)
        {
            if (sharedBundle == null || previewExportContext == null) return;
            string directory = sharedBundle.SharedNodePreviewDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

            var files = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                // Node previews share the flat img directory with preview and atlas
                // assets. Only remove files produced from node IDs.
                string sourceId = Path.GetFileNameWithoutExtension(fileName);
                if (sourceId.Length < 4 || sourceId[0] != 'S'
                    || !int.TryParse(sourceId.Substring(1), out _))
                {
                    continue;
                }
                string fullPath = Path.GetFullPath(files[i]).Replace("\\", "/");
                if (!previewExportContext.UsedPreviewPaths.Contains(fullPath))
                    AiJobFileUtility.DeleteFileIfExists(files[i]);
            }
        }

        // 图集页按内容哈希命名，旧页不会被本轮引用；不清理就只在磁盘上越堆越多。
        private static void CleanupUnusedAtlasPages(SharedBundleContext sharedBundle, AiAnalysisPackageDocument package)
        {
            if (sharedBundle == null || package == null || package.nodes == null) return;
            string directory = sharedBundle.AtlasDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in package.nodes)
                if (node.atlas != null && !string.IsNullOrEmpty(node.atlas.page))
                    used.Add(Path.GetFileName(node.atlas.page));

            foreach (string file in Directory.GetFiles(directory, "a-*.png", SearchOption.TopDirectoryOnly))
                if (!used.Contains(Path.GetFileName(file))) AiJobFileUtility.DeleteFileIfExists(file);
        }

        private static string ExportTextureToPngIfMissing(Texture texture, string outputPath, NodePreviewExportContext previewExportContext = null, int transparentPadding = 0)
        {
            if (texture == null || string.IsNullOrWhiteSpace(outputPath)) return outputPath;
            if (transparentPadding <= 0 && File.Exists(outputPath))
            {
                if (previewExportContext != null) previewExportContext.UsedPreviewPaths.Add(Path.GetFullPath(outputPath).Replace("\\", "/"));
                return outputPath;
            }
            return ExportTextureToPng(texture, outputPath, previewExportContext, transparentPadding);
        }

        private static string ExportTextureToPng(Texture texture, string outputPath, NodePreviewExportContext previewExportContext, int transparentPadding = 0)
        {
            if (texture == null || string.IsNullOrWhiteSpace(outputPath)) return outputPath;
            int width = texture.width, height = texture.height;

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply(false, false);
                copy = PadTexture(copy, transparentPadding);
                ZeroTransparentPixels(copy);

                byte[] pngBytes = copy.EncodeToPNG();
                string contentHash = ComputeTextureContentHash(copy.width, copy.height, pngBytes);
                if (previewExportContext != null && !string.IsNullOrEmpty(contentHash)
                    && previewExportContext.ContentHashToPath.TryGetValue(contentHash, out var existingPath)
                    && !string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                {
                    previewExportContext.UsedPreviewPaths.Add(Path.GetFullPath(existingPath).Replace("\\", "/"));
                    UnityEngine.Object.DestroyImmediate(copy);
                    return existingPath;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllBytes(outputPath, pngBytes);
                if (previewExportContext != null && !string.IsNullOrEmpty(contentHash))
                    previewExportContext.ContentHashToPath[contentHash] = outputPath;
                if (previewExportContext != null)
                    previewExportContext.UsedPreviewPaths.Add(Path.GetFullPath(outputPath).Replace("\\", "/"));
                UnityEngine.Object.DestroyImmediate(copy);
                return outputPath;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D PadTexture(Texture2D source, int padding)
        {
            if (source == null || padding <= 0) return source;

            int sourceWidth = source.width;
            int sourceHeight = source.height;
            int paddedWidth = sourceWidth + padding * 2;
            int paddedHeight = sourceHeight + padding * 2;
            var padded = new Texture2D(paddedWidth, paddedHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[paddedWidth * paddedHeight];
            var sourcePixels = source.GetPixels32();
            for (int y = 0; y < sourceHeight; y++)
            {
                Array.Copy(sourcePixels, y * sourceWidth, pixels, (y + padding) * paddedWidth + padding, sourceWidth);
            }

            padded.SetPixels32(pixels);
            padded.Apply(false, false);
            UnityEngine.Object.DestroyImmediate(source);
            return padded;
        }

        private static void ZeroTransparentPixels(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            var pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length < 1)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a != 0)
                {
                    continue;
                }

                if (pixels[i].r == 0 && pixels[i].g == 0 && pixels[i].b == 0)
                {
                    continue;
                }

                pixels[i] = TransparentBlack;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private static string ComputeTextureContentHash(int width, int height, byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0) return string.Empty;
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(BitConverter.GetBytes(width), 0, 4, null, 0);
                sha.TransformBlock(BitConverter.GetBytes(height), 0, 4, null, 0);
                sha.TransformFinalBlock(pngBytes, 0, pngBytes.Length);
                var hash = sha.Hash;
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string ComputeSha256(string content)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Utf8NoBom.GetBytes(content ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
            try { return PathExtensions.GetRelativePath(rootPath, fullPath).Replace("\\", "/"); }
            catch { return fullPath.Replace("\\", "/"); }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            }
            return builder.ToString().Trim('_');
        }
    }
}
#endif
