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
    internal static class AiPatchSemanticNormalizer
    {
        internal static bool NormalizeRequiredFields(AiPatchDocument patch)
        {
            if (patch == null)
            {
                return false;
            }

            bool changed = false;
            if (patch.analysis == null)
            {
                patch.analysis = new List<AiAuditEntry>();
                changed = true;
            }
            if (patch.operations == null)
            {
                patch.operations = new List<AiPatchOperation>();
                changed = true;
            }

            for (int i = patch.analysis.Count - 1; i >= 0; i--)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId))
                {
                    patch.analysis.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (entry.ownerId == null)
                {
                    entry.ownerId = string.Empty;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(entry.predictedUIType) || !TryParsePredictedAuditUIType(entry.predictedUIType))
                {
                    patch.analysis.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.currentUIType) || !TryParseCurrentAuditUIType(entry.currentUIType))
                {
                    entry.currentUIType = entry.predictedUIType;
                    changed = true;
                }

                if (!IsValidVerdict(entry.verdict))
                {
                    entry.verdict = string.Equals(entry.currentUIType, entry.predictedUIType, StringComparison.Ordinal)
                        ? "correct"
                        : "corrected";
                    changed = true;
                }

                if (entry.confidence <= 0f || entry.confidence > 1f)
                {
                    patch.analysis.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.reason))
                {
                    entry.reason = "本地补齐：AI 未提供 reason";
                    changed = true;
                }
            }

            for (int i = patch.operations.Count - 1; i >= 0; i--)
            {
                var operation = patch.operations[i];
                if (!NormalizeOperationRequiredFields(operation))
                {
                    patch.operations.RemoveAt(i);
                    changed = true;
                    continue;
                }
                changed = true;

                if (operation.confidence <= 0f || operation.confidence > 1f)
                {
                    operation.confidence = NormalizeConfidence(operation.confidence);
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(operation.reason))
                {
                    operation.reason = "本地补齐：AI 未提供 reason";
                    changed = true;
                }
            }

            return changed;
        }

        internal static bool NormalizeAnalysisEntries(AiPatchDocument patch)
        {
            return NormalizeAnalysisEntries(patch, null);
        }

        internal static bool NormalizeAnalysisEntries(AiPatchDocument patch, AiAnalysisPackageDocument package)
        {
            if (patch == null || patch.analysis == null)
            {
                return false;
            }

            bool changed = NormalizeDuplicateAnalysisTargets(patch);
            var parentIds = BuildPackageParentMap(package);
            var predictedTypes = BuildPredictedTypeMap(patch);
            var ownerlessBackgroundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId) || !string.IsNullOrWhiteSpace(entry.ownerId))
                {
                    continue;
                }

                if (!AiPatchValidator.TryParseUIType(entry.predictedUIType, out var predictedType) || !IsSemanticChild(predictedType))
                {
                    continue;
                }

                if (TryFindCompatibleOwner(entry.targetId, predictedType, parentIds, predictedTypes, out var ownerId))
                {
                    entry.ownerId = ownerId;
                    entry.reason = "按最近兼容父节点补齐 ownerId";
                    changed = true;
                    continue;
                }

                if (predictedType == GUIType.Background)
                {
                    entry.predictedUIType = nameof(GUIType.Image);
                    entry.verdict = string.Equals(entry.currentUIType, entry.predictedUIType, StringComparison.Ordinal)
                        ? "correct"
                        : "corrected";
                    entry.reason = "无兼容 owner 的 Background 修正为独立 Image";
                    ownerlessBackgroundIds.Add(entry.targetId);
                    changed = true;
                }
            }

            changed |= NormalizeDuplicateOwnerRoles(patch, package);
            if (changed)
            {
                SyncSetUiTypeOperationsWithAnalysis(patch);
            }

            if (ownerlessBackgroundIds.Count == 0)
            {
                return changed;
            }

            if (patch.operations != null)
            {
                for (int i = 0; i < patch.operations.Count; i++)
                {
                    var operation = patch.operations[i];
                    if (operation == null
                        || !string.Equals(operation.op, AiPatchOperationNames.SetUIType, StringComparison.Ordinal)
                        || !ownerlessBackgroundIds.Contains(operation.targetId)
                        || !string.Equals(operation.uiType, nameof(GUIType.Background), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    operation.uiType = nameof(GUIType.Image);
                    operation.reason = "无兼容 owner 的 Background 修正为独立 Image";
                }
            }

            return true;
        }

        private static bool NormalizeDuplicateAnalysisTargets(AiPatchDocument patch)
        {
            if (patch == null || patch.analysis == null || patch.analysis.Count < 2)
            {
                return false;
            }

            var selected = new Dictionary<string, AiAuditEntry>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>(patch.analysis.Count);
            bool changed = false;
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId))
                {
                    changed = true;
                    continue;
                }

                if (!selected.TryGetValue(entry.targetId, out var existing))
                {
                    selected.Add(entry.targetId, entry);
                    order.Add(entry.targetId);
                    continue;
                }

                changed = true;
                if (GetAnalysisSelectionScore(entry) > GetAnalysisSelectionScore(existing))
                {
                    selected[entry.targetId] = entry;
                }
            }

            if (!changed)
            {
                return false;
            }

            patch.analysis.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                var entry = selected[order[i]];
                entry.reason = "本地去重后保留的唯一 analysis 结论";
                patch.analysis.Add(entry);
            }
            return true;
        }

        private static int GetAnalysisSelectionScore(AiAuditEntry entry)
        {
            if (entry == null) return int.MinValue;

            int score = Mathf.RoundToInt(Mathf.Clamp01(entry.confidence) * 100f);
            if (!string.IsNullOrWhiteSpace(entry.ownerId)) score += 1000;
            if (AiPatchValidator.TryParseUIType(entry.predictedUIType, out var predictedType))
            {
                if (IsSemanticChild(predictedType)) score += 500;
                if (predictedType != GUIType.Panel && predictedType != GUIType.Null) score += 100;
                if (predictedType == GUIType.Text || predictedType == GUIType.Image) score += 20;
            }
            if (string.Equals(entry.verdict, "removed", StringComparison.Ordinal)) score -= 50;
            return score;
        }

        private static bool NormalizeDuplicateOwnerRoles(AiPatchDocument patch, AiAnalysisPackageDocument package)
        {
            if (patch == null || patch.analysis == null || patch.analysis.Count < 2)
            {
                return false;
            }

            var packageNodes = BuildPackageNodeMap(package);
            var selected = new Dictionary<string, AiAuditEntry>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.ownerId)
                    || !AiPatchValidator.TryParseUIType(entry.predictedUIType, out var predictedType)
                    || !IsSemanticChild(predictedType))
                {
                    continue;
                }

                string key = entry.ownerId + "\n" + entry.predictedUIType;
                if (!selected.TryGetValue(key, out var existing))
                {
                    selected.Add(key, entry);
                    continue;
                }

                changed = true;
                var winner = GetOwnerRoleSelectionScore(entry, packageNodes) > GetOwnerRoleSelectionScore(existing, packageNodes)
                    ? entry
                    : existing;
                var loser = ReferenceEquals(winner, entry) ? existing : entry;
                selected[key] = winner;
                DowngradeDuplicateSemanticChild(loser, predictedType, packageNodes);
            }

            return changed;
        }

        private static Dictionary<string, AiAnalysisNodeEntry> BuildPackageNodeMap(AiAnalysisPackageDocument package)
        {
            var result = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.OrdinalIgnoreCase);
            if (package == null || package.nodes == null) return result;

            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id)) continue;
                result[node.id] = node;
            }
            return result;
        }

        private static int GetOwnerRoleSelectionScore(AiAuditEntry entry, Dictionary<string, AiAnalysisNodeEntry> packageNodes)
        {
            if (entry == null) return int.MinValue;

            int score = Mathf.RoundToInt(Mathf.Clamp01(entry.confidence) * 100f);
            if (packageNodes != null
                && packageNodes.TryGetValue(entry.targetId, out var node)
                && node != null
                && string.Equals(node.parentId, entry.ownerId, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }
            if (AiPatchValidator.TryParseUIType(entry.predictedUIType, out var predictedType))
            {
                if (IsTextSemanticType(predictedType)) score += 100;
                if (NodeIsTextLayer(packageNodes, entry.targetId)) score += 50;
            }
            return score;
        }

        private static bool NodeIsTextLayer(Dictionary<string, AiAnalysisNodeEntry> packageNodes, string targetId)
        {
            return packageNodes != null
                && !string.IsNullOrWhiteSpace(targetId)
                && packageNodes.TryGetValue(targetId, out var node)
                && node != null
                && node.isTextLayer;
        }

        private static void DowngradeDuplicateSemanticChild(AiAuditEntry entry, GUIType semanticType, Dictionary<string, AiAnalysisNodeEntry> packageNodes)
        {
            if (entry == null) return;

            entry.ownerId = string.Empty;
            entry.predictedUIType = IsTextSemanticType(semanticType) && NodeIsTextLayer(packageNodes, entry.targetId)
                ? nameof(GUIType.Text)
                : nameof(GUIType.Image);
            entry.verdict = string.Equals(entry.currentUIType, entry.predictedUIType, StringComparison.Ordinal)
                ? "correct"
                : "corrected";
            entry.reason = "同一 owner 下同类子控件重复，降级为普通元素";
        }

        private static void SyncSetUiTypeOperationsWithAnalysis(AiPatchDocument patch)
        {
            if (patch == null || patch.analysis == null || patch.operations == null || patch.operations.Count == 0)
            {
                return;
            }

            var predictedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId) || string.IsNullOrWhiteSpace(entry.predictedUIType)) continue;
                predictedTypes[entry.targetId] = entry.predictedUIType;
            }

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (operation == null
                    || !string.Equals(operation.op, AiPatchOperationNames.SetUIType, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(operation.targetId)
                    || !predictedTypes.TryGetValue(operation.targetId, out var predictedType)
                    || string.Equals(operation.uiType, predictedType, StringComparison.Ordinal))
                {
                    continue;
                }

                operation.uiType = predictedType;
                operation.reason = "同步本地归一化后的 analysis.predictedUIType";
            }
        }

        internal static void Normalize(Psd2UIFormConverter converter, AiPatchDocument patch)
        {
            if (converter == null || patch == null || patch.analysis == null)
            {
                return;
            }

            if (patch.operations == null)
            {
                patch.operations = new List<AiPatchOperation>();
            }

            var existingSetTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (operation == null) continue;
                if (!AiPatchOperationNames.TryParse(operation.op, out var kind)) continue;

                if (kind == AiPatchOperationKind.SetUIType)
                {
                    if (!string.IsNullOrWhiteSpace(operation.targetId))
                    {
                        existingSetTargets.Add(operation.targetId);
                    }
                }
            }

            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId)) continue;

                if (AiPatchValidator.TryParseUIType(entry.predictedUIType, out var predictedType)
                    && !string.Equals(entry.currentUIType, entry.predictedUIType, StringComparison.Ordinal)
                    && !existingSetTargets.Contains(entry.targetId))
                {
                    patch.operations.Add(new AiPatchOperation
                    {
                        op = AiPatchOperationNames.SetUIType,
                        targetId = entry.targetId,
                        uiType = entry.predictedUIType,
                        confidence = NormalizeConfidence(entry.confidence),
                        reason = "由 analysis.predictedUIType 自动补充类型修正"
                    });
                    existingSetTargets.Add(entry.targetId);
                }
            }
            AiPatchValidator.NormalizeOperationOrder(patch);
        }

        private static void AddMeaninglessWrapperCleanupOperations(
            Psd2UIFormConverter converter,
            AiPatchDocument patch,
            Dictionary<string, PsdLayerNode> currentNodes,
            HashSet<string> existingMoveTargets)
        {
            if (converter == null || patch == null || currentNodes == null || currentNodes.Count == 0)
            {
                return;
            }

            var existingFlattenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedParentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (patch.analysis != null)
            {
                for (int i = 0; i < patch.analysis.Count; i++)
                {
                    var entry = patch.analysis[i];
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.ownerId))
                    {
                        protectedParentIds.Add(entry.ownerId);
                    }
                }
            }
            if (patch.operations != null)
            {
                for (int i = 0; i < patch.operations.Count; i++)
                {
                    var operation = patch.operations[i];
                    if (operation == null || !AiPatchOperationNames.TryParse(operation.op, out var kind)) continue;
                    if (kind == AiPatchOperationKind.FlattenGroup && !string.IsNullOrWhiteSpace(operation.targetId))
                    {
                        existingFlattenTargets.Add(operation.targetId);
                    }
                    if ((kind == AiPatchOperationKind.CreateGroup || kind == AiPatchOperationKind.MoveNode)
                        && !string.IsNullOrWhiteSpace(operation.newParentId))
                    {
                        protectedParentIds.Add(operation.newParentId);
                    }
                    if (kind == AiPatchOperationKind.CreateGroup && !string.IsNullOrWhiteSpace(operation.parentId))
                    {
                        protectedParentIds.Add(operation.parentId);
                    }
                }
            }

            var candidates = new List<PsdLayerNode>(currentNodes.Values);
            candidates.Sort((a, b) => GetTransformDepth(a != null ? a.transform : null).CompareTo(GetTransformDepth(b != null ? b.transform : null)));

            for (int i = 0; i < candidates.Count; i++)
            {
                var node = candidates[i];
                if (!IsMeaninglessWrapperCandidate(converter, node))
                {
                    continue;
                }

                string nodeId = AiNodeIdUtility.GetNodeId(converter, node);
                if (string.IsNullOrWhiteSpace(nodeId)
                    || existingFlattenTargets.Contains(nodeId)
                    || existingMoveTargets.Contains(nodeId)
                    || protectedParentIds.Contains(nodeId))
                {
                    continue;
                }

                string reason;
                if (node.transform.childCount == 0)
                {
                    reason = "本地结构归一化：删除无图像意义的空 Null/Panel 节点";
                }
                else if (HasSingleMeaningfulChildThroughWrapper(node))
                {
                    reason = "本地结构归一化：消除单子节点无意义 Null/Panel 嵌套";
                }
                else
                {
                    continue;
                }

                patch.operations.Add(new AiPatchOperation
                {
                    op = AiPatchOperationNames.FlattenGroup,
                    targetId = nodeId,
                    confidence = 0.95f,
                    reason = reason
                });
                existingFlattenTargets.Add(nodeId);
                MarkAnalysisEntryRemoved(patch, nodeId, reason);
            }
        }

        private static bool IsMeaninglessWrapperCandidate(Psd2UIFormConverter converter, PsdLayerNode node)
        {
            if (converter == null || node == null || node.transform == null || node.transform == converter.transform)
            {
                return false;
            }
            if (node.LayerType != PsdLayerType.LayerGroup || (node.UIType != GUIType.Panel && node.UIType != GUIType.Null))
            {
                return false;
            }
            if (node.NeedExportImage() || node.HasReuseReference || node.HasReusePrefabReference)
            {
                return false;
            }
            if (HasDirectChildWithUIType(node.transform, GUIType.Background))
            {
                return false;
            }
            return true;
        }

        private static bool HasSingleMeaningfulChildThroughWrapper(PsdLayerNode node)
        {
            if (node == null || node.transform == null || node.transform.childCount != 1)
            {
                return false;
            }

            var childNode = node.transform.GetChild(0)?.GetComponent<PsdLayerNode>();
            if (childNode == null)
            {
                return false;
            }

            return RectsNearlySame(node.LayerRect, childNode.LayerRect) || node.LayerRect.size == Vector2.zero;
        }

        private static bool HasDirectChildWithUIType(Transform parent, GUIType uiType)
        {
            if (parent == null) return false;
            for (int i = 0; i < parent.childCount; i++)
            {
                var childNode = parent.GetChild(i)?.GetComponent<PsdLayerNode>();
                if (childNode != null && childNode.UIType == uiType)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RectsNearlySame(Rect a, Rect b)
        {
            const float tolerance = 0.5f;
            return Mathf.Abs(a.x - b.x) <= tolerance
                && Mathf.Abs(a.y - b.y) <= tolerance
                && Mathf.Abs(a.width - b.width) <= tolerance
                && Mathf.Abs(a.height - b.height) <= tolerance;
        }

        private static int GetTransformDepth(Transform transform)
        {
            int depth = 0;
            var current = transform;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private static void MarkAnalysisEntryRemoved(AiPatchDocument patch, string targetId, string reason)
        {
            if (patch == null || patch.analysis == null || string.IsNullOrWhiteSpace(targetId)) return;

            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || !string.Equals(entry.targetId, targetId, StringComparison.OrdinalIgnoreCase)) continue;

                entry.verdict = "removed";
                entry.predictedUIType = string.IsNullOrWhiteSpace(entry.predictedUIType) ? nameof(GUIType.Null) : entry.predictedUIType;
                entry.confidence = NormalizeConfidence(entry.confidence);
                entry.reason = reason;
                return;
            }
        }

        private static Dictionary<string, string> BuildPackageParentMap(AiAnalysisPackageDocument package)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (package == null || package.nodes == null) return result;

            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id)) continue;
                result[node.id] = string.IsNullOrWhiteSpace(node.parentId) ? "root" : node.parentId;
            }
            return result;
        }

        private static Dictionary<string, GUIType> BuildPredictedTypeMap(AiPatchDocument patch)
        {
            var result = new Dictionary<string, GUIType>(StringComparer.OrdinalIgnoreCase);
            if (patch == null || patch.analysis == null) return result;

            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId)) continue;
                if (AiPatchValidator.TryParseUIType(entry.predictedUIType, out var uiType))
                {
                    result[entry.targetId] = uiType;
                }
            }
            return result;
        }

        private static bool TryFindCompatibleOwner(string targetId, GUIType roleType, Dictionary<string, string> parentIds, Dictionary<string, GUIType> predictedTypes, out string ownerId)
        {
            ownerId = string.Empty;
            if (string.IsNullOrWhiteSpace(targetId) || parentIds == null || predictedTypes == null)
            {
                return false;
            }

            string currentId = targetId;
            for (int depth = 0; depth < 64; depth++)
            {
                if (!parentIds.TryGetValue(currentId, out var parentId) || string.IsNullOrWhiteSpace(parentId) || string.Equals(parentId, "root", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (predictedTypes.TryGetValue(parentId, out var ownerType) && AiHelperContractCatalog.AllowsRole(ownerType, roleType))
                {
                    ownerId = parentId;
                    return true;
                }

                currentId = parentId;
            }

            return false;
        }

        private static Dictionary<string, PsdLayerNode> BuildCurrentNodeMap(Psd2UIFormConverter converter)
        {
            var result = new Dictionary<string, PsdLayerNode>(StringComparer.OrdinalIgnoreCase);
            if (converter == null) return result;

            var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            if (nodes == null) return result;

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                string id = AiNodeIdUtility.GetNodeId(converter, node);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[id] = node;
                }
            }
            return result;
        }

        private static string GetParentId(Psd2UIFormConverter converter, Transform parent)
        {
            if (converter == null || parent == null || parent == converter.transform)
            {
                return "root";
            }

            var parentNode = parent.GetComponent<PsdLayerNode>();
            return parentNode != null ? AiNodeIdUtility.GetNodeId(converter, parentNode) : "root";
        }

        private static bool NormalizeOperationRequiredFields(AiPatchOperation operation)
        {
            if (operation == null || !AiPatchOperationNames.TryParse(operation.op, out var kind))
            {
                return false;
            }

            switch (kind)
            {
                case AiPatchOperationKind.CreateGroup:
                    if (string.IsNullOrWhiteSpace(operation.id)
                        || !operation.id.StartsWith("gen:", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(operation.parentId)
                        || string.IsNullOrWhiteSpace(operation.name))
                    {
                        return false;
                    }
                    if (!AiPatchValidator.TryParseUIType(operation.uiType, out var createdType)
                        || UGUIParser.IsSemanticUIType(createdType))
                    {
                        operation.uiType = nameof(GUIType.Null);
                    }
                    if (operation.insertIndex < -1) operation.insertIndex = -1;
                    return true;

                case AiPatchOperationKind.MoveNode:
                    if (string.IsNullOrWhiteSpace(operation.targetId) || string.IsNullOrWhiteSpace(operation.newParentId))
                    {
                        return false;
                    }
                    operation.id = string.Empty;
                    operation.parentId = string.Empty;
                    operation.name = string.Empty;
                    operation.uiType = string.Empty;
                    if (operation.insertIndex < -1) operation.insertIndex = -1;
                    return true;

                case AiPatchOperationKind.FlattenGroup:
                    if (string.IsNullOrWhiteSpace(operation.targetId))
                    {
                        return false;
                    }
                    operation.id = string.Empty;
                    operation.parentId = string.Empty;
                    operation.newParentId = string.Empty;
                    operation.name = string.Empty;
                    operation.uiType = string.Empty;
                    operation.insertIndex = -1;
                    return true;

                case AiPatchOperationKind.SetUIType:
                    return !string.IsNullOrWhiteSpace(operation.targetId)
                        && AiPatchValidator.TryParseUIType(operation.uiType, out _);

                case AiPatchOperationKind.RenameNode:
                    if (string.IsNullOrWhiteSpace(operation.targetId) || string.IsNullOrWhiteSpace(operation.name))
                    {
                        return false;
                    }
                    operation.id = string.Empty;
                    operation.parentId = string.Empty;
                    operation.newParentId = string.Empty;
                    operation.uiType = string.Empty;
                    operation.insertIndex = -1;
                    return true;

                case AiPatchOperationKind.DeleteGeneratedGroup:
                    if (string.IsNullOrWhiteSpace(operation.targetId)
                        || !operation.targetId.StartsWith("gen:", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    operation.id = string.Empty;
                    operation.parentId = string.Empty;
                    operation.newParentId = string.Empty;
                    operation.name = string.Empty;
                    operation.uiType = string.Empty;
                    operation.insertIndex = -1;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryParseCurrentAuditUIType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, true, out GUIType uiType) && Enum.IsDefined(typeof(GUIType), uiType);
        }

        private static bool TryParsePredictedAuditUIType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, nameof(GUIType.Null), StringComparison.Ordinal))
            {
                return true;
            }

            return AiPatchValidator.TryParseUIType(value, out _);
        }

        private static bool IsValidVerdict(string value)
        {
            return string.Equals(value, "correct", StringComparison.Ordinal)
                || string.Equals(value, "corrected", StringComparison.Ordinal)
                || string.Equals(value, "removed", StringComparison.Ordinal);
        }

        private static bool IsSemanticChild(GUIType uiType)
        {
            return (int)uiType > 100;
        }

        private static bool IsTextSemanticType(GUIType uiType)
        {
            return AiHelperContractCatalog.IsTextRole(uiType);
        }

        private static float NormalizeConfidence(float confidence)
        {
            if (confidence <= 0f) return 0.5f;
            return Mathf.Clamp01(confidence);
        }
    }
}
#endif
