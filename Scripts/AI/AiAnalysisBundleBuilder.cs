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
            public readonly Dictionary<PsdLayerNode, NodePreviewExportResult> NodeResults = new Dictionary<PsdLayerNode, NodePreviewExportResult>();
            public readonly Dictionary<string, string> ContentHashToPath = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly HashSet<string> UsedPreviewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string ProjectRoot;
        }

        private struct NodePreviewExportResult
        {
            public string PreviewImagePath;
            public string PreviewKind;
            public string PreviewSourceId;
            public string VisualHash;
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
        private const string AnnotatedPreviewFileName = "annotated-preview.png";
        private const string NodePreviewDirectoryName = "node-previews";
        private const string AtlasDirectoryName = "node-atlas";
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
        private const int AnnotatedLabelWidth = 38;
        private const int AnnotatedLabelHeight = 13;

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
                var sharedBundle = BuildSharedBundleContext(converter);
                var previewExportContext = new NodePreviewExportContext
                {
                    ProjectRoot = Directory.GetParent(Application.dataPath).FullName
                };
                var package = new AiAnalysisPackageDocument
                {
                    version = AiProtocolVersions.AnalysisPackage,
                    document = BuildDocumentInfo(converter, sharedBundle, out var geometry),
                    config = BuildConfigInfo()
                };

                var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                var shortIds = BuildShortIdMap(nodes);
                for (int i = 0; i < nodes.Length; i++)
                {
                    var nodeEntry = BuildNodeEntry(converter, sharedBundle, previewExportContext, nodes[i], geometry, shortIds);
                    if (nodeEntry != null)
                    {
                        package.nodes.Add(nodeEntry);
                    }
                }
                CleanupUnusedNodePreviews(sharedBundle, previewExportContext);
                BuildVisualInputs(package, sharedBundle, previewExportContext.ProjectRoot);

                string packageJson = JsonUtility.ToJson(package, false);
                treeHash = ComputeSha256(packageJson);
                package.treeHash = treeHash;
                packageJson = JsonUtility.ToJson(package, false);

                AiJobFileUtility.WriteText(context.AnalysisPackagePath, packageJson);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to build AI analysis package: {ex.Message}";
                return false;
            }
        }

        private static SharedBundleContext BuildSharedBundleContext(Psd2UIFormConverter converter)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sharedRoot = AiPathDefaults.ToAbsoluteProjectPath(projectRoot, AiPathDefaults.SharedRootRelative);
            string bundleDirectory = Path.Combine(sharedRoot, AiPathDefaults.GetPsdAssetFolderName(converter != null ? converter.PsdAssetName : null));
            return new SharedBundleContext
            {
                SharedBundleDirectory = bundleDirectory,
                SharedNodePreviewDirectory = Path.Combine(bundleDirectory, NodePreviewDirectoryName),
                AnnotatedPreviewPath = Path.Combine(bundleDirectory, AnnotatedPreviewFileName),
                AtlasDirectory = Path.Combine(bundleDirectory, AtlasDirectoryName)
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

            return new AiAnalysisDocumentInfo
            {
                previewImagePath = MakeRelativePath(projectRoot, previewImagePath),
                nodePreviewDirectoryPath = MakeRelativePath(projectRoot, sharedBundle.SharedNodePreviewDirectory),
                nodeAtlasDirectoryPath = MakeRelativePath(projectRoot, sharedBundle.AtlasDirectory),
                width = canvasWidth,
                height = canvasHeight
            };
        }

        private static AiAnalysisConfigInfo BuildConfigInfo()
        {
            var info = new AiAnalysisConfigInfo();
            var parser = UGUIParser.Instance;
            if (parser == null) return info;

            var rules = parser.GetRules();
            if (rules == null) return info;

            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule == null) continue;
                if (IsTmpUiType(rule.UIType)) continue;

                info.uiTypeRules.Add(new AiAnalysisUiTypeRuleInfo
                {
                    uiType = rule.UIType.ToString(),
                    uiTypeDesc = rule.UITypeDesc ?? string.Empty,
                    typeMatches = CloneStringArray(rule.TypeMatches)
                });
            }

            return info;
        }

        private static string[] CloneStringArray(string[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<string>();
            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static Dictionary<PsdLayerNode, string> BuildShortIdMap(PsdLayerNode[] nodes)
        {
            var result = new Dictionary<PsdLayerNode, string>();
            if (nodes == null) return result;

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || result.ContainsKey(node)) continue;
                result[node] = "n" + i.ToString("000");
            }
            return result;
        }

        private static string BuildIdPath(Psd2UIFormConverter converter, Transform transform, Dictionary<PsdLayerNode, string> shortIds)
        {
            if (transform == null) return string.Empty;

            var stack = new Stack<string>();
            var current = transform;
            while (current != null && (converter == null || current != converter.transform))
            {
                var node = current.GetComponent<PsdLayerNode>();
                if (node != null && shortIds != null && shortIds.TryGetValue(node, out var shortId))
                {
                    stack.Push(shortId);
                }
                current = current.parent;
            }
            return stack.Count > 0 ? string.Join("/", stack.ToArray()) : string.Empty;
        }

        private static string BuildDisplayPath(Psd2UIFormConverter converter, Transform transform)
        {
            if (transform == null) return string.Empty;

            var stack = new Stack<string>();
            var current = transform;
            while (current != null && (converter == null || current != converter.transform))
            {
                stack.Push(current.name ?? string.Empty);
                current = current.parent;
            }
            return stack.Count > 0 ? string.Join("/", stack.ToArray()) : string.Empty;
        }

        private static string ResolveSuffixMatch(PsdLayerNode node)
        {
            if (node == null) return string.Empty;

            string token = TryExtractDotSuffix(node.gameObject.name);
            if (string.IsNullOrWhiteSpace(token))
            {
                token = TryExtractDotSuffix(node.SourceLayerName);
            }
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;

            var parser = UGUIParser.Instance;
            var rules = parser != null ? parser.GetRules() : null;
            if (rules == null) return string.Empty;

            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule == null || IsTmpUiType(rule.UIType) || rule.TypeMatches == null) continue;
                for (int j = 0; j < rule.TypeMatches.Length; j++)
                {
                    if (string.Equals(token, rule.TypeMatches[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return rule.UIType.ToString();
                    }
                }
            }
            return string.Empty;
        }

        private static string TryExtractDotSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            int dot = value.LastIndexOf('.');
            if (dot < 0 || dot + 1 >= value.Length) return string.Empty;
            string suffix = value.Substring(dot + 1).Trim();
            if (suffix.Length == 0) return string.Empty;
            for (int i = 0; i < suffix.Length; i++)
            {
                char ch = suffix[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                {
                    return string.Empty;
                }
            }
            return suffix;
        }

        private static bool IsTmpUiType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.TMPText:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                    return true;
                default:
                    return false;
            }
        }

        private static AiAnalysisNodeEntry BuildNodeEntry(Psd2UIFormConverter converter, SharedBundleContext sharedBundle, NodePreviewExportContext previewExportContext, PsdLayerNode node, PreviewCanvasGeometry geometry, Dictionary<PsdLayerNode, string> shortIds)
        {
            if (node == null) return null;

            ResolvePreviewRect(node, geometry, out float previewX, out float previewY, out float previewW, out float previewH);
            bool isTextLayer = node.IsTextLayer(out _);
            string imageFile = ExportNodePreview(converter, node, sharedBundle, previewExportContext);
            string nodeId = AiNodeIdUtility.GetNodeId(converter, node);
            shortIds.TryGetValue(node, out var shortId);
            previewExportContext.NodeResults.TryGetValue(node, out var previewResult);

            return new AiAnalysisNodeEntry
            {
                id = nodeId,
                shortId = shortId ?? string.Empty,
                parentId = GetParentId(converter, node.transform.parent),
                childIds = BuildChildIds(converter, node.transform),
                onlyChildId = ResolveOnlyChildId(converter, node.transform),
                idPath = BuildIdPath(converter, node.transform, shortIds),
                displayPath = BuildDisplayPath(converter, node.transform),
                name = node.gameObject.name,
                layerName = node.SourceLayerName,
                nameTokens = BuildNameTokens(node.gameObject.name, node.SourceLayerName),
                layerType = node.LayerType.ToString(),
                isGroupLayer = node.LayerType == PsdLayerType.LayerGroup,
                isTextLayer = isTextLayer,
                isGeneratedNode = node.BindPsdLayerIndex < 0,
                uiType = node.UIType.ToString(),
                rect = new RectData { x = previewX, y = previewY, w = previewW, h = previewH },
                imageFile = imageFile,
                previewKind = string.IsNullOrWhiteSpace(previewResult.PreviewKind) ? "empty" : previewResult.PreviewKind,
                previewSourceId = previewResult.PreviewSourceId ?? string.Empty,
                visualHash = previewResult.VisualHash ?? string.Empty,
                renderLeafCount = CountRenderableLeafLayers(node),
                suffixMatch = ResolveSuffixMatch(node),
                siblingIndex = node.transform.GetSiblingIndex(),
                childCount = node.transform.childCount
            };
        }

        private static string[] BuildChildIds(Psd2UIFormConverter converter, Transform parent)
        {
            if (converter == null || parent == null || parent.childCount < 1)
            {
                return Array.Empty<string>();
            }

            var childIds = new List<string>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                var childNode = parent.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                string childId = AiNodeIdUtility.GetNodeId(converter, childNode);
                if (!string.IsNullOrWhiteSpace(childId))
                {
                    childIds.Add(childId);
                }
            }

            return childIds.Count > 0 ? childIds.ToArray() : Array.Empty<string>();
        }

        private static string ResolveOnlyChildId(Psd2UIFormConverter converter, Transform parent)
        {
            if (converter == null || parent == null)
            {
                return string.Empty;
            }

            PsdLayerNode onlyChild = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var childNode = parent.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                if (onlyChild != null)
                {
                    return string.Empty;
                }

                onlyChild = childNode;
            }

            return onlyChild != null ? AiNodeIdUtility.GetNodeId(converter, onlyChild) : string.Empty;
        }

        private static string[] BuildNameTokens(string name, string layerName)
        {
            var tokens = new List<string>(8);
            AppendNameTokens(tokens, name);
            AppendNameTokens(tokens, layerName);
            return tokens.Count > 0 ? tokens.ToArray() : Array.Empty<string>();
        }

        private static void AppendNameTokens(List<string> tokens, string value)
        {
            if (tokens == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            int length = value.Length;
            var builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char ch = value[i];
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                    continue;
                }

                FlushNameToken(tokens, builder);
            }

            FlushNameToken(tokens, builder);
        }

        private static void FlushNameToken(List<string> tokens, StringBuilder builder)
        {
            if (tokens == null || builder == null || builder.Length < 1)
            {
                return;
            }

            string token = builder.ToString();
            builder.Length = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], token, StringComparison.Ordinal))
                {
                    return;
                }
            }

            tokens.Add(token);
        }

        private static void ResolvePreviewRect(PsdLayerNode node, PreviewCanvasGeometry geometry, out float x, out float y, out float w, out float h)
        {
            x = 0f; y = 0f; w = 0f; h = 0f;
            if (node == null) return;

            var bindLayer = node.BindPsdLayer;
            if (bindLayer != null)
            {
                x = bindLayer.Left - geometry.CanvasLeft;
                y = bindLayer.Top - geometry.CanvasTop;
                w = bindLayer.Right - bindLayer.Left;
                h = bindLayer.Bottom - bindLayer.Top;
                return;
            }

            var rect = node.LayerRect;
            float halfW = rect.width * 0.5f;
            float halfH = rect.height * 0.5f;
            float psdDocW = geometry.PsdDocWidth > 0 ? geometry.PsdDocWidth : geometry.CanvasWidth;
            float psdDocH = geometry.PsdDocHeight > 0 ? geometry.PsdDocHeight : geometry.CanvasHeight;
            x = rect.x + psdDocW * 0.5f - halfW - geometry.CanvasLeft;
            y = psdDocH * 0.5f - rect.y - halfH - geometry.CanvasTop;
            w = rect.width;
            h = rect.height;
        }

        private static string GetParentId(Psd2UIFormConverter converter, Transform parent)
        {
            if (converter == null || parent == null || parent == converter.transform)
                return "root";
            var parentNode = parent.GetComponent<PsdLayerNode>();
            return parentNode != null ? AiNodeIdUtility.GetNodeId(converter, parentNode) : "root";
        }

        private static string ExportNodePreview(Psd2UIFormConverter converter, PsdLayerNode node, SharedBundleContext sharedBundle, NodePreviewExportContext previewExportContext)
        {
            if (node == null || sharedBundle == null || string.IsNullOrWhiteSpace(sharedBundle.SharedNodePreviewDirectory))
                return string.Empty;

            if (previewExportContext != null && previewExportContext.NodeResults.TryGetValue(node, out var cached))
                return cached.PreviewImagePath;

            if (TryGetVisualPassthroughPreviewSource(node, out var previewSource))
            {
                var sourcePath = ExportNodePreview(converter, previewSource, sharedBundle, previewExportContext);
                previewExportContext.NodeResults.TryGetValue(previewSource, out var sourceResult);
                var inherited = new NodePreviewExportResult
                {
                    PreviewImagePath = sourcePath,
                    PreviewKind = "inherited",
                    PreviewSourceId = AiNodeIdUtility.GetNodeId(converter, previewSource),
                    VisualHash = sourceResult.VisualHash ?? ComputePreviewFileHash(sharedBundle, sourcePath)
                };
                StorePreviewResult(previewExportContext, node, inherited, sharedBundle);
                return sourcePath;
            }

            Texture2D previewTexture = null;
            bool ownsPreviewTexture = false;
            string previewKind = "empty";
            try
            {
                if (!TryAcquirePreviewTexture(node, out previewTexture, out ownsPreviewTexture, out previewKind)
                    || previewTexture == null)
                {
                    StorePreviewResult(previewExportContext, node, new NodePreviewExportResult { PreviewImagePath = string.Empty, PreviewKind = "empty", PreviewSourceId = string.Empty, VisualHash = string.Empty }, sharedBundle);
                    return string.Empty;
                }

                string fileName = SanitizeFileName(AiNodeIdUtility.GetNodeId(converter, node));
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "node";

                string outputPath = Path.Combine(sharedBundle.SharedNodePreviewDirectory, fileName + ".png");
                outputPath = ExportTextureToPngIfMissing(previewTexture, outputPath, previewExportContext, NodePreviewTransparentPadding);
                string projectRoot = previewExportContext != null && !string.IsNullOrWhiteSpace(previewExportContext.ProjectRoot)
                    ? previewExportContext.ProjectRoot
                    : Directory.GetParent(Application.dataPath).FullName;

                var result = new NodePreviewExportResult
                {
                    PreviewImagePath = MakeRelativePath(projectRoot, outputPath),
                    PreviewKind = previewKind,
                    PreviewSourceId = string.Empty,
                    VisualHash = ComputePreviewFileHash(sharedBundle, MakeRelativePath(projectRoot, outputPath))
                };
                StorePreviewResult(previewExportContext, node, result, sharedBundle);
                return result.PreviewImagePath;
            }
            finally
            {
                if (ownsPreviewTexture && previewTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(previewTexture);
                }
            }
        }

        private static bool TryAcquirePreviewTexture(PsdLayerNode node, out Texture2D previewTexture, out bool ownsPreviewTexture, out string previewKind)
        {
            previewTexture = null;
            ownsPreviewTexture = false;
            previewKind = "empty";
            if (node == null)
            {
                return false;
            }

            // AI 识别图像必须反映“当前节点树状态”，不能回退成原始 BindPsdLayer 的 LayerGroup 合图。
            if (ShouldRenderPreviewFromCurrentNodeTree(node)
                && TryRenderPreviewTextureFromCurrentNodeTree(node, out previewTexture))
            {
                ownsPreviewTexture = true;
                previewKind = "composite";
                return true;
            }

            if (!node.RefreshLayerTexture() || node.PreviewTexture == null)
            {
                return false;
            }

            previewTexture = node.PreviewTexture;
            previewKind = node.BindPsdLayerIndex < 0 ? "generated" : "self";
            return true;
        }

        private static bool ShouldRenderPreviewFromCurrentNodeTree(PsdLayerNode node)
        {
            return node != null
                && node.LayerType == PsdLayerType.LayerGroup
                && node.transform != null
                && node.transform.childCount > 0;
        }

        private static bool TryRenderPreviewTextureFromCurrentNodeTree(PsdLayerNode node, out Texture2D texture)
        {
            texture = null;
            var renderTree = node.BuildCurrentRenderTree();
            var rendered = PsdLayerRenderer.RenderTree(
                renderTree,
                includeHiddenLayers: false,
                applyClippingMasks: true,
                isPreviewRender: true);
            texture = CreateTextureFromRenderedImage(rendered);
            return texture != null;
        }

        private static Texture2D CreateTextureFromRenderedImage(PsdRenderedImage rendered)
        {
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            TextureFormat textureFormat = rendered.IsHighBitDepth ? TextureFormat.RGBA64 : TextureFormat.RGBA32;
            var texture = new Texture2D(rendered.Width, rendered.Height, textureFormat, false);
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

        private static bool TryGetVisualPassthroughPreviewSource(PsdLayerNode node, out PsdLayerNode previewSource)
        {
            previewSource = null;
            if (node == null || node.LayerType != PsdLayerType.LayerGroup
                || (node.UIType != GUIType.Panel && node.UIType != GUIType.Null)
                || node.CollapseChildrenForGeneration)
                return false;

            var layer = node.BindPsdLayer;
            if (layer == null || !layer.IsPreviewPassthroughGroup())
                return false;

            PsdLayerNode onlyChild = null;
            for (int i = 0; i < node.transform.childCount; i++)
            {
                var childNode = node.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null || !childNode.gameObject.activeSelf) continue;
                if (childNode.BindPsdLayer != null && !childNode.BindPsdLayer.IsVisible) continue;
                if (onlyChild != null) return false;
                onlyChild = childNode;
            }

            previewSource = onlyChild;
            return previewSource != null;
        }

        private static void StorePreviewResult(NodePreviewExportContext previewExportContext, PsdLayerNode node, NodePreviewExportResult result, SharedBundleContext sharedBundle)
        {
            if (previewExportContext == null || node == null) return;
            previewExportContext.NodeResults[node] = result;
            if (!string.IsNullOrWhiteSpace(result.PreviewImagePath))
            {
                string fullPath = Path.Combine(previewExportContext.ProjectRoot, result.PreviewImagePath).Replace("\\", "/");
                previewExportContext.UsedPreviewPaths.Add(Path.GetFullPath(fullPath).Replace("\\", "/"));
            }
        }

        private static int CountRenderableLeafLayers(PsdLayerNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int childNodeCount = 0;
            int descendantLeafCount = 0;
            for (int i = 0; i < node.transform.childCount; i++)
            {
                var childNode = node.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                childNodeCount++;
                descendantLeafCount += CountRenderableLeafLayers(childNode);
            }

            if (node.BindPsdLayer == null)
            {
                return descendantLeafCount;
            }

            if (node.LayerType == PsdLayerType.LayerGroup && childNodeCount > 0)
            {
                return descendantLeafCount;
            }

            return descendantLeafCount > 0 ? descendantLeafCount : 1;
        }

        private static string ComputePreviewFileHash(SharedBundleContext sharedBundle, string relativeOrAbsolutePath)
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
            {
                byte[] bytes = File.ReadAllBytes(resolvedPath);
                byte[] hash = sha.ComputeHash(bytes);
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

            if (TryBuildAnnotatedPreview(package, sharedBundle, projectRoot, out var annotatedRelativePath))
            {
                package.document.annotatedPreviewImagePath = annotatedRelativePath;
                AddVisualInput(package.document.visualInputPaths, annotatedRelativePath);
            }

            BuildNodeAtlases(package, sharedBundle, projectRoot);
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

        private static bool TryBuildAnnotatedPreview(AiAnalysisPackageDocument package, SharedBundleContext sharedBundle, string projectRoot, out string relativePath)
        {
            relativePath = string.Empty;
            string previewPath = ResolveProjectRelativePath(projectRoot, package.document.previewImagePath);
            if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath)) return false;

            var texture = LoadPngTexture(previewPath);
            if (texture == null) return false;

            try
            {
                var nodes = package.nodes;
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var node = nodes[i];
                        if (node == null || node.rect == null || string.IsNullOrWhiteSpace(node.shortId)) continue;
                        DrawRect(texture, Mathf.RoundToInt(node.rect.x), Mathf.RoundToInt(node.rect.y), Mathf.RoundToInt(node.rect.w), Mathf.RoundToInt(node.rect.h), new Color32(255, 215, 64, 255));
                        DrawAnnotatedLabel(texture, node.shortId, Mathf.RoundToInt(node.rect.x), Mathf.RoundToInt(node.rect.y));
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(sharedBundle.AnnotatedPreviewPath));
                texture.Apply(false, false);
                File.WriteAllBytes(sharedBundle.AnnotatedPreviewPath, texture.EncodeToPNG());
                relativePath = MakeRelativePath(projectRoot, sharedBundle.AnnotatedPreviewPath);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void DrawAnnotatedLabel(Texture2D texture, string label, int rectX, int rectY)
        {
            if (texture == null || string.IsNullOrWhiteSpace(label)) return;
            int x = Mathf.Clamp(rectX, 0, Mathf.Max(0, texture.width - AnnotatedLabelWidth));
            int y = rectY - AnnotatedLabelHeight;
            if (y < 0) y = Mathf.Clamp(rectY, 0, Mathf.Max(0, texture.height - AnnotatedLabelHeight));
            FillRect(texture, x, y, AnnotatedLabelWidth, AnnotatedLabelHeight, new Color32(0, 0, 0, 190));
            DrawText(texture, label.ToUpperInvariant(), x + 3, y + 3, new Color32(255, 255, 255, 255), 1);
        }

        private static void BuildNodeAtlases(AiAnalysisPackageDocument package, SharedBundleContext sharedBundle, string projectRoot)
        {
            if (package == null || package.nodes == null || package.nodes.Count == 0 || sharedBundle == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(sharedBundle.AtlasDirectory) && Directory.Exists(sharedBundle.AtlasDirectory))
            {
                var oldFiles = Directory.GetFiles(sharedBundle.AtlasDirectory, "node-atlas-*.png", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < oldFiles.Length; i++)
                {
                    AiJobFileUtility.DeleteFileIfExists(oldFiles[i]);
                }
            }
            Directory.CreateDirectory(sharedBundle.AtlasDirectory);

            int cellsPerPage = AtlasColumns * AtlasRows;
            int pageCount = (package.nodes.Count + cellsPerPage - 1) / cellsPerPage;
            for (int page = 0; page < pageCount; page++)
            {
                var atlas = new Texture2D(AtlasColumns * AtlasCellWidth, AtlasRows * AtlasCellHeight, TextureFormat.RGBA32, false);
                FillRect(atlas, 0, 0, atlas.width, atlas.height, TransparentBlack);

                for (int cell = 0; cell < cellsPerPage; cell++)
                {
                    int nodeIndex = page * cellsPerPage + cell;
                    if (nodeIndex >= package.nodes.Count) break;

                    var node = package.nodes[nodeIndex];
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

                    DrawRectThickness(atlas, cellX, cellY, AtlasCellWidth, AtlasCellHeight, AtlasGridBorderColor, AtlasGridBorderWidth);

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

                        DrawNodePreviewIntoAtlas(atlas, node, projectRoot, package.document.nodePreviewDirectoryPath, previewX, previewY, previewW, previewH, out var actualImageRect);
                        string pageName = $"node-atlas-{page:000}.png";
                        node.atlas = new AiAtlasRef
                        {
                            page = pageName,
                            cell = cell,
                            label = node.shortId ?? string.Empty,
                            imageRect = actualImageRect,
                            labelRect = new RectData { x = labelX, y = labelY, w = labelW, h = labelH }
                        };

                        string label = $"{node.shortId} {CompactLayerTypeLabel(node.layerType)} {CompactUiTypeLabel(node.uiType)}";
                        DrawTextClipped(atlas, label.ToUpperInvariant(), labelX, labelY, labelW, new Color32(245, 245, 245, 255), 2);
                    }
                }

                string atlasPath = Path.Combine(sharedBundle.AtlasDirectory, $"node-atlas-{page:000}.png");
                ZeroTransparentPixels(atlas);
                atlas.Apply(false, false);
                File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
                AddVisualInput(package.document.visualInputPaths, MakeRelativePath(projectRoot, atlasPath));
                UnityEngine.Object.DestroyImmediate(atlas);
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

        private static string CompactUiTypeLabel(string uiType)
        {
            if (string.IsNullOrWhiteSpace(uiType)) return "NULL";
            switch (uiType)
            {
                case "Null": return "NULL";
                case "Image": return "IMG";
                case "RawImage": return "RAW";
                case "Text": return "TXT";
                case "Button": return "BTN";
                case "Dropdown": return "DPD";
                case "InputField": return "IPT";
                case "Toggle": return "TGL";
                case "Slider": return "SLD";
                case "ScrollView": return "SV";
                case "Mask": return "MSK";
                case "FillColor": return "COL";
                case "Panel": return "PNL";
                case "ToggleGroup": return "TGG";
                case "Background": return "BG";
                case "Button_Text": return "BT_TXT";
                case "Slider_Fill": return "SLD_FILL";
                case "Slider_Handle": return "SLD_HDL";
                case "Toggle_Checkmark": return "TG_MARK";
                case "Toggle_Label": return "TG_LBL";
                case "Dropdown_Label": return "DPD_LBL";
                case "Dropdown_Arrow": return "DPD_ARR";
                case "InputField_Placeholder": return "IPT_PH";
                case "InputField_Text": return "IPT_TXT";
                default: return uiType.Length > 8 ? uiType.Substring(0, 8) : uiType;
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
                string fullPath = Path.GetFullPath(files[i]).Replace("\\", "/");
                if (!previewExportContext.UsedPreviewPaths.Contains(fullPath))
                    AiJobFileUtility.DeleteFileIfExists(files[i]);
            }
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
            try { return Path.GetRelativePath(rootPath, fullPath).Replace("\\", "/"); }
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
