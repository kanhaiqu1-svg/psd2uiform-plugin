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

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiRecognitionInputBuilder
    {
        private const int NodesPerShard = 64;

        internal bool Build(AiJobContext context, AiAnalysisPackageDocument package, string projectRoot, out string error)
        {
            error = null;
            if (context == null || package == null || package.document == null)
            {
                error = "Recognition input build context is invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.RecognitionInputManifestPath)
                || string.IsNullOrWhiteSpace(context.RecognitionNodeShardDirectory))
            {
                error = "Recognition input output paths are invalid.";
                return false;
            }

            try
            {
                AiJobFileUtility.EnsureDirectory(Path.GetDirectoryName(context.RecognitionInputManifestPath));
                AiJobFileUtility.EnsureDirectory(context.RecognitionNodeShardDirectory);

                var compactNodes = BuildCompactNodes(package.nodes);
                int totalShards = Math.Max(1, (compactNodes.Count + NodesPerShard - 1) / NodesPerShard);
                var shardPaths = new string[totalShards];

                for (int shardIndex = 0; shardIndex < totalShards; shardIndex++)
                {
                    int start = shardIndex * NodesPerShard;
                    int count = Math.Min(NodesPerShard, compactNodes.Count - start);
                    var shard = new AiRecognitionNodeShardDocument
                    {
                        version = AiProtocolVersions.RecognitionInput,
                        treeHash = package.treeHash ?? string.Empty,
                        shardIndex = shardIndex,
                        totalShards = totalShards
                    };

                    for (int i = 0; i < count; i++)
                    {
                        shard.nodes.Add(compactNodes[start + i]);
                    }

                    string shardPath = Path.Combine(context.RecognitionNodeShardDirectory, $"nodes-{shardIndex:000}.json");
                    AiJobFileUtility.WriteJson(shardPath, shard);
                    shardPaths[shardIndex] = MakeRelativePath(projectRoot, shardPath);
                }

                var manifest = new AiRecognitionInputManifestDocument
                {
                    version = AiProtocolVersions.RecognitionInput,
                    treeHash = package.treeHash ?? string.Empty,
                    document = BuildDocumentInfo(package.document, compactNodes),
                    config = package.config ?? new AiAnalysisConfigInfo(),
                    nodeCount = compactNodes.Count,
                    nodeShardPaths = shardPaths
                };

                AiJobFileUtility.WriteJson(context.RecognitionInputManifestPath, manifest);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to build recognition input: {ex.Message}";
                return false;
            }
        }

        private static List<AiRecognitionInputNodeEntry> BuildCompactNodes(List<AiAnalysisNodeEntry> source)
        {
            var result = new List<AiRecognitionInputNodeEntry>(source != null ? source.Count : 0);
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                var node = source[i];
                if (node == null)
                {
                    continue;
                }

                result.Add(new AiRecognitionInputNodeEntry
                {
                    id = node.id ?? string.Empty,
                    shortId = node.shortId ?? string.Empty,
                    parentId = node.parentId ?? string.Empty,
                    childIds = CloneArray(node.childIds),
                    onlyChildId = node.onlyChildId ?? string.Empty,
                    idPath = node.idPath ?? string.Empty,
                    displayPath = node.displayPath ?? string.Empty,
                    name = node.name ?? string.Empty,
                    layerName = node.layerName ?? string.Empty,
                    nameTokens = CloneArray(node.nameTokens),
                    layerType = node.layerType ?? string.Empty,
                    isGroupLayer = node.isGroupLayer,
                    isTextLayer = node.isTextLayer,
                    isGeneratedNode = node.isGeneratedNode,
                    uiType = node.uiType ?? string.Empty,
                    rect = CloneRect(node.rect),
                    previewFileName = Path.GetFileName(node.imageFile ?? string.Empty),
                    previewKind = node.previewKind ?? string.Empty,
                    previewSourceId = node.previewSourceId ?? string.Empty,
                    visualHash = node.visualHash ?? string.Empty,
                    renderLeafCount = node.renderLeafCount,
                    atlas = node.atlas == null
                        ? null
                        : new AiRecognitionInputAtlasRef
                        {
                            page = node.atlas.page ?? string.Empty,
                            cell = node.atlas.cell,
                            label = node.atlas.label ?? string.Empty
                        },
                    suffixMatch = node.suffixMatch ?? string.Empty,
                    siblingIndex = node.siblingIndex,
                    childCount = node.childCount
                });
            }

            return result;
        }

        private static AiRecognitionInputDocumentInfo BuildDocumentInfo(AiAnalysisDocumentInfo source, List<AiRecognitionInputNodeEntry> nodes)
        {
            var atlasPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var atlas = nodes[i] != null ? nodes[i].atlas : null;
                    if (atlas != null && !string.IsNullOrWhiteSpace(atlas.page))
                    {
                        atlasPages.Add(atlas.page);
                    }
                }
            }

            return new AiRecognitionInputDocumentInfo
            {
                previewImagePath = source.previewImagePath ?? string.Empty,
                annotatedPreviewImagePath = source.annotatedPreviewImagePath ?? string.Empty,
                nodePreviewDirectoryPath = source.nodePreviewDirectoryPath ?? string.Empty,
                nodeAtlasDirectoryPath = source.nodeAtlasDirectoryPath ?? string.Empty,
                atlasPageCount = atlasPages.Count,
                width = source.width,
                height = source.height
            };
        }

        private static RectData CloneRect(RectData source)
        {
            if (source == null)
            {
                return null;
            }

            return new RectData
            {
                x = source.x,
                y = source.y,
                w = source.w,
                h = source.h
            };
        }

        private static string[] CloneArray(string[] source)
        {
            if (source == null || source.Length < 1)
            {
                return Array.Empty<string>();
            }

            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetRelativePath(rootPath, fullPath).Replace("\\", "/");
            }
            catch
            {
                return fullPath.Replace("\\", "/");
            }
        }
    }
}
#endif
