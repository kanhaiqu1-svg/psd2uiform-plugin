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
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiOwnerScopeBuilder
    {
        private const int ScopePadding = 24;

        internal bool Build(AiJobContext context, AiAnalysisPackageDocument package, AiMainTypeResultDocument mainTypeResult, out AiOwnerScopeManifestDocument manifest, out string error)
        {
            manifest = null;
            error = null;
            if (context == null || package == null || package.document == null || mainTypeResult == null)
            {
                error = "Owner scope build context is invalid.";
                return false;
            }

            try
            {
                var nodeById = BuildNodeMap(package.nodes);
                manifest = new AiOwnerScopeManifestDocument
                {
                    version = AiProtocolVersions.OwnerScopeManifest,
                    treeHash = package.treeHash
                };

                int previewWidth = package.document.width;
                int previewHeight = package.document.height;
                for (int i = 0; i < mainTypeResult.nodes.Count; i++)
                {
                    var entry = mainTypeResult.nodes[i];
                    if (entry == null
                        || !AiPatchValidator.TryParseUIType(entry.predictedUIType, out var ownerType)
                        || !UGUIParser.IsCompositeControlType(ownerType))
                    {
                        continue;
                    }

                    if (!nodeById.TryGetValue(entry.targetId, out var ownerNode) || ownerNode == null || ownerNode.rect == null)
                    {
                        continue;
                    }

                    Rect ownerRect = ToRect(ownerNode.rect);
                    if (ownerRect.width <= 0f || ownerRect.height <= 0f)
                    {
                        continue;
                    }

                    Rect scopeRect = ExpandAndClamp(ownerRect, previewWidth, previewHeight, ScopePadding);
                    manifest.scopes.Add(new AiOwnerScopeEntry
                    {
                        ownerId = entry.targetId,
                        ownerShortId = ownerNode.shortId ?? string.Empty,
                        ownerType = ownerType.ToString(),
                        rect = new RectData
                        {
                            x = scopeRect.x,
                            y = scopeRect.y,
                            w = scopeRect.width,
                            h = scopeRect.height
                        },
                        scopeImagePath = string.Empty,
                        annotatedScopeImagePath = string.Empty,
                        candidateNodeIds = BuildCandidateNodeIds(package.nodes, nodeById, entry.targetId, ownerRect, scopeRect)
                    });
                }

                AiJobFileUtility.WriteJson(context.OwnerScopeManifestPath, manifest);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to build owner scopes: {ex.Message}";
                return false;
            }
        }

        private static Dictionary<string, AiAnalysisNodeEntry> BuildNodeMap(List<AiAnalysisNodeEntry> nodes)
        {
            var result = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.OrdinalIgnoreCase);
            if (nodes == null)
            {
                return result;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node != null && !string.IsNullOrWhiteSpace(node.id))
                {
                    result[node.id] = node;
                }
            }
            return result;
        }

        private static string[] BuildCandidateNodeIds(
            List<AiAnalysisNodeEntry> nodes,
            Dictionary<string, AiAnalysisNodeEntry> nodeById,
            string ownerId,
            Rect ownerRect,
            Rect scopeRect)
        {
            var candidates = new List<string>(16);
            if (nodes == null)
            {
                return candidates.ToArray();
            }

            float ownerArea = Mathf.Max(1f, ownerRect.width * ownerRect.height);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id) || string.Equals(node.id, ownerId, StringComparison.OrdinalIgnoreCase) || node.rect == null)
                {
                    continue;
                }

                var rect = ToRect(node.rect);
                if (rect.width <= 0f || rect.height <= 0f)
                {
                    continue;
                }

                if (IsAncestorOf(nodeById, node.id, ownerId))
                {
                    continue;
                }

                if (IsDescendantOf(nodeById, node.id, ownerId))
                {
                    candidates.Add(node.id);
                    continue;
                }

                Vector2 center = rect.center;
                float area = rect.width * rect.height;
                bool intersectsScope = scopeRect.Contains(center) || scopeRect.Overlaps(rect);
                if (!intersectsScope)
                {
                    continue;
                }

                if (area > ownerArea * 1.2f)
                {
                    continue;
                }

                if (GetOverlapRatio(ownerRect, rect) >= 0.12f || ownerRect.Contains(center))
                {
                    candidates.Add(node.id);
                }
            }

            return candidates.ToArray();
        }

        private static Rect ToRect(RectData data)
        {
            return data == null ? Rect.zero : new Rect(data.x, data.y, data.w, data.h);
        }

        private static Rect ExpandAndClamp(Rect rect, int width, int height, int padding)
        {
            float xMin = Mathf.Max(0f, rect.xMin - padding);
            float yMin = Mathf.Max(0f, rect.yMin - padding);
            float xMax = Mathf.Min(width, rect.xMax + padding);
            float yMax = Mathf.Min(height, rect.yMax + padding);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool IsDescendantOf(Dictionary<string, AiAnalysisNodeEntry> nodeById, string nodeId, string ancestorId)
        {
            return HasAncestor(nodeById, nodeId, ancestorId);
        }

        private static bool IsAncestorOf(Dictionary<string, AiAnalysisNodeEntry> nodeById, string nodeId, string descendantId)
        {
            return HasAncestor(nodeById, descendantId, nodeId);
        }

        private static bool HasAncestor(Dictionary<string, AiAnalysisNodeEntry> nodeById, string nodeId, string ancestorId)
        {
            if (nodeById == null || string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(ancestorId))
            {
                return false;
            }

            string currentId = nodeId;
            for (int guard = 0; guard < 128; guard++)
            {
                if (!nodeById.TryGetValue(currentId, out var node) || node == null || string.IsNullOrWhiteSpace(node.parentId) || string.Equals(node.parentId, "root", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(node.parentId, ancestorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentId = node.parentId;
            }

            return false;
        }

        private static float GetOverlapRatio(Rect left, Rect right)
        {
            float width = Mathf.Max(0f, Mathf.Min(left.xMax, right.xMax) - Mathf.Max(left.xMin, right.xMin));
            float height = Mathf.Max(0f, Mathf.Min(left.yMax, right.yMax) - Mathf.Max(left.yMin, right.yMin));
            float area = width * height;
            if (area <= 0f)
            {
                return 0f;
            }

            float minArea = Mathf.Max(1f, Mathf.Min(left.width * left.height, right.width * right.height));
            return area / minArea;
        }

    }
}
#endif
