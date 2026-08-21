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

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// AI patch 校验器。
    ///
    /// 核心原则：**永远不要因个别节点的识别错误导致整个 patch 应用失败。**
    /// AI 返回的 patch 中即使存在少量语义错误（如孤儿角色、类型不兼容），也必须
    /// 尽可能应用正确的部分。校验只负责格式/结构合法性（version、treeHash、必填字段、
    /// 操作顺序），不因语义问题阻断应用。语义问题通过 warning 透出供人工复核。
    /// </summary>
    internal sealed class AiPatchValidator
    {
        private sealed class DryNode
        {
            public string Id;
            public string ParentId;
            public string LayerType;
            public GUIType UIType;
            public bool Generated;
            public bool HasDescendantTextLayer;
        }

        internal bool Validate(AiPatchDocument patch, AiAnalysisPackageDocument package, out string error)
        {
            if (!Validate(patch, out error))
            {
                return false;
            }

            return ValidateAnalysisPackageCoverage(patch, package, out error)
                && ValidateAnalysisSemantics(patch, package, out error)
                && ValidatePatchDryRun(patch, package, out error);
        }

        internal bool Validate(AiPatchDocument patch, out string error)
        {
            error = null;
            if (patch == null)
            {
                error = "Patch document is null.";
                return false;
            }

            if (!string.Equals(patch.version, AiProtocolVersions.Patch, StringComparison.Ordinal))
            {
                error = $"Patch document version must be '{AiProtocolVersions.Patch}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(patch.treeHash))
            {
                error = "Patch document requires treeHash.";
                return false;
            }

            if (patch.analysis == null)
            {
                patch.analysis = new List<AiAuditEntry>();
            }

            if (patch.operations == null)
            {
                patch.operations = new List<AiPatchOperation>();
            }

            NormalizeOperationOrder(patch);

            if (patch.analysis.Count < 1)
            {
                error = "Patch document requires at least one analysis entry.";
                return false;
            }

            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null)
                {
                    error = $"Analysis[{i}] is null.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.targetId))
                {
                    error = $"Analysis[{i}] requires targetId.";
                    return false;
                }

                if (entry.ownerId == null)
                {
                    entry.ownerId = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(entry.verdict))
                {
                    error = $"Analysis[{i}] requires verdict.";
                    return false;
                }
                if (!IsValidVerdict(entry.verdict))
                {
                    error = $"Analysis[{i}] verdict is invalid: '{entry.verdict}'.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.reason))
                {
                    error = $"Analysis[{i}] requires reason.";
                    return false;
                }

                if (entry.confidence <= 0f || entry.confidence > 1f)
                {
                    error = $"Analysis[{i}] confidence must be within (0, 1].";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.currentUIType) || !TryParseCurrentAuditUIType(entry.currentUIType))
                {
                    error = $"Analysis[{i}] currentUIType is invalid: '{entry.currentUIType}'.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.predictedUIType) || !TryParsePredictedAuditUIType(entry.predictedUIType))
                {
                    error = $"Analysis[{i}] predictedUIType is invalid: '{entry.predictedUIType}'.";
                    return false;
                }
            }

            if (patch.operations.Count < 1)
            {
                return true;
            }

            var generatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (operation == null)
                {
                    error = $"Operation[{i}] is null.";
                    return false;
                }

                if (!AiPatchOperationNames.TryParse(operation.op, out var opKind))
                {
                    error = $"Operation[{i}] has unsupported op '{operation.op}'.";
                    return false;
                }

                if (operation.confidence <= 0f || operation.confidence > 1f)
                {
                    error = $"Operation[{i}] confidence must be within (0, 1].";
                    return false;
                }

                switch (opKind)
                {
                    case AiPatchOperationKind.CreateGroup:
                        if (string.IsNullOrWhiteSpace(operation.id) || !operation.id.StartsWith("gen:", StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"Operation[{i}] create_group requires a generated id with 'gen:' prefix.";
                            return false;
                        }
                        if (!generatedIds.Add(operation.id))
                        {
                            error = $"Operation[{i}] create_group duplicates generated id '{operation.id}'.";
                            return false;
                        }
                        if (string.IsNullOrWhiteSpace(operation.parentId))
                        {
                            error = $"Operation[{i}] create_group requires parentId.";
                            return false;
                        }
                        if (string.IsNullOrWhiteSpace(operation.name))
                        {
                            error = $"Operation[{i}] create_group requires name.";
                            return false;
                        }
                        if (operation.insertIndex < -1)
                        {
                            error = $"Operation[{i}] create_group insertIndex must be >= -1.";
                            return false;
                        }
                        if (!TryParseUIType(operation.uiType, out var createdGroupType)
                            || (!UGUIParser.IsMainUIType(createdGroupType) && createdGroupType != GUIType.Null)
                            || UGUIParser.IsSemanticUIType(createdGroupType))
                        {
                            error = $"Operation[{i}] create_group uiType must be a main UI type or Null wrapper.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.MoveNode:
                        if (string.IsNullOrWhiteSpace(operation.targetId))
                        {
                            error = $"Operation[{i}] move_node requires targetId.";
                            return false;
                        }
                        if (string.IsNullOrWhiteSpace(operation.newParentId))
                        {
                            error = $"Operation[{i}] move_node requires newParentId.";
                            return false;
                        }
                        if (operation.insertIndex < -1)
                        {
                            error = $"Operation[{i}] move_node insertIndex must be >= -1.";
                            return false;
                        }
                        if (!string.IsNullOrWhiteSpace(operation.uiType))
                        {
                            error = $"Operation[{i}] move_node must not set uiType.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.FlattenGroup:
                        if (string.IsNullOrWhiteSpace(operation.targetId))
                        {
                            error = $"Operation[{i}] flatten_group requires targetId.";
                            return false;
                        }
                        if (!string.IsNullOrWhiteSpace(operation.id)
                            || !string.IsNullOrWhiteSpace(operation.parentId)
                            || !string.IsNullOrWhiteSpace(operation.newParentId)
                            || !string.IsNullOrWhiteSpace(operation.name)
                            || !string.IsNullOrWhiteSpace(operation.uiType))
                        {
                            error = $"Operation[{i}] flatten_group only supports targetId, confidence, and reason.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.SetUIType:
                        if (string.IsNullOrWhiteSpace(operation.targetId))
                        {
                            error = $"Operation[{i}] set_ui_type requires targetId.";
                            return false;
                        }
                        if (!TryParseUIType(operation.uiType, out _))
                        {
                            error = $"Operation[{i}] set_ui_type has invalid uiType '{operation.uiType}'.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.RenameNode:
                        if (string.IsNullOrWhiteSpace(operation.targetId) || string.IsNullOrWhiteSpace(operation.name))
                        {
                            error = $"Operation[{i}] rename_node requires valid targetId and name.";
                            return false;
                        }
                        if (!string.IsNullOrWhiteSpace(operation.uiType))
                        {
                            error = $"Operation[{i}] rename_node must not set uiType.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.DeleteGeneratedGroup:
                        if (string.IsNullOrWhiteSpace(operation.targetId) || !operation.targetId.StartsWith("gen:", StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"Operation[{i}] delete_generated_group only supports generated group ids.";
                            return false;
                        }
                        if (!string.IsNullOrWhiteSpace(operation.uiType))
                        {
                            error = $"Operation[{i}] delete_generated_group must not set uiType.";
                            return false;
                        }
                        break;
                }

                if (string.IsNullOrWhiteSpace(operation.reason))
                {
                    error = $"Operation[{i}] requires reason.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateAnalysisPackageCoverage(AiPatchDocument patch, AiAnalysisPackageDocument package, out string error)
        {
            error = null;
            if (patch == null || package == null)
            {
                return true;
            }

            var analysisIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (patch.analysis != null)
            {
                for (int i = 0; i < patch.analysis.Count; i++)
                {
                    var entry = patch.analysis[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.targetId))
                    {
                        continue;
                    }
                    if (!analysisIds.Add(entry.targetId))
                    {
                        error = $"Analysis contains duplicate targetId '{entry.targetId}'.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateAnalysisSemantics(AiPatchDocument patch, AiAnalysisPackageDocument package, out string error)
        {
            error = null;
            if (patch == null || package == null || patch.analysis == null)
            {
                return true;
            }

            var packageNodes = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.OrdinalIgnoreCase);
            if (package.nodes != null)
            {
                for (int i = 0; i < package.nodes.Count; i++)
                {
                    var node = package.nodes[i];
                    if (node == null || string.IsNullOrWhiteSpace(node.id)) continue;
                    packageNodes[node.id] = node;
                }
            }

            var predictedTypes = new Dictionary<string, GUIType>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.targetId)) continue;
                if (TryParsePredictedAuditUIType(entry.predictedUIType) && Enum.TryParse(entry.predictedUIType, true, out GUIType predicted))
                {
                    predictedTypes[entry.targetId] = predicted;
                }
            }

            var ownerRoleTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < patch.analysis.Count; i++)
            {
                var entry = patch.analysis[i];
                if (entry == null) continue;

                if (!TryParseUIType(entry.predictedUIType, out var predictedType))
                {
                    continue;
                }

                if (IsTextSemanticType(predictedType) || predictedType == GUIType.Text)
                {
                    if (!packageNodes.TryGetValue(entry.targetId, out var node) || node == null || !node.isTextLayer)
                    {
                        error = $"Analysis[{i}] marks '{entry.targetId}' as text semantic '{predictedType}', but the target is not an isTextLayer node.";
                        return false;
                    }
                }

                if (UGUIParser.IsSemanticUIType(predictedType))
                {
                    if (string.IsNullOrWhiteSpace(entry.ownerId))
                    {
                        error = $"Analysis[{i}] semantic child '{entry.targetId}' with uiType '{predictedType}' requires ownerId.";
                        return false;
                    }

                    if (!predictedTypes.TryGetValue(entry.ownerId, out var ownerType))
                    {
                        error = $"Analysis[{i}] ownerId '{entry.ownerId}' for semantic child '{entry.targetId}' is not present in analysis.";
                        return false;
                    }

                    if (!AiHelperContractCatalog.AllowsRole(ownerType, predictedType))
                    {
                        error = $"Analysis[{i}] owner '{entry.ownerId}' with uiType '{ownerType}' cannot own semantic child '{predictedType}'.";
                        return false;
                    }

                    string ownerRoleKey = entry.ownerId + "\n" + predictedType;
                    if (ownerRoleTargets.TryGetValue(ownerRoleKey, out var existingTargetId))
                    {
                        error = $"Analysis[{i}] owner '{entry.ownerId}' has duplicate semantic child '{predictedType}' on '{existingTargetId}' and '{entry.targetId}'.";
                        return false;
                    }
                    ownerRoleTargets.Add(ownerRoleKey, entry.targetId);
                }
            }

            return true;
        }

        private static bool ValidatePatchDryRun(AiPatchDocument patch, AiAnalysisPackageDocument package, out string error)
        {
            error = null;
            if (patch == null || package == null || package.nodes == null)
            {
                return true;
            }

            var nodes = BuildDryNodeMap(package);
            var createdGeneratedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedGeneratedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var movedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AiPatchOperationKind previousPhase = AiPatchOperationKind.CreateGroup;

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                AiPatchOperationNames.TryParse(operation.op, out var opKind);
                if (!ValidateOperationOrder(previousPhase, opKind))
                {
                    error = $"Operation[{i}] '{operation.op}' is out of order. Expected create_group, then move_node/flatten_group, then set_ui_type/rename_node, then delete_generated_group.";
                    return false;
                }
                previousPhase = opKind;

                switch (opKind)
                {
                    case AiPatchOperationKind.CreateGroup:
                        GUIType generatedUiType = GUIType.Null;
                        TryParseUIType(operation.uiType, out generatedUiType);
                        if (!nodes.ContainsKey(operation.parentId))
                        {
                            error = $"Operation[{i}] create_group parent '{operation.parentId}' does not exist in analysis package or generated operations.";
                            return false;
                        }
                        nodes[operation.id] = new DryNode
                        {
                            Id = operation.id,
                            ParentId = operation.parentId,
                            LayerType = PsdLayerType.LayerGroup.ToString(),
                            UIType = generatedUiType,
                            Generated = true,
                            HasDescendantTextLayer = false
                        };
                        createdGeneratedIds.Add(operation.id);
                        break;

                    case AiPatchOperationKind.MoveNode:
                        if (!nodes.ContainsKey(operation.targetId))
                        {
                            error = $"Operation[{i}] move_node target '{operation.targetId}' does not exist.";
                            return false;
                        }
                        if (!nodes.ContainsKey(operation.newParentId))
                        {
                            error = $"Operation[{i}] move_node newParentId '{operation.newParentId}' does not exist.";
                            return false;
                        }
                        if (!movedIds.Add(operation.targetId))
                        {
                            error = $"Operation[{i}] moves target '{operation.targetId}' more than once.";
                            return false;
                        }
                        if (WouldCreateCycle(nodes, operation.targetId, operation.newParentId))
                        {
                            error = $"Operation[{i}] move_node would parent '{operation.targetId}' into its own descendant chain.";
                            return false;
                        }
                        nodes[operation.targetId].ParentId = operation.newParentId;
                        if (createdGeneratedIds.Contains(operation.newParentId))
                        {
                            usedGeneratedIds.Add(operation.newParentId);
                        }
                        break;

                    case AiPatchOperationKind.FlattenGroup:
                        if (!nodes.TryGetValue(operation.targetId, out var flattenNode))
                        {
                            error = $"Operation[{i}] flatten_group target '{operation.targetId}' does not exist.";
                            return false;
                        }
                        if (string.Equals(operation.targetId, "root", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrWhiteSpace(flattenNode.ParentId))
                        {
                            error = $"Operation[{i}] flatten_group cannot target the root.";
                            return false;
                        }
                        if (flattenNode.UIType != GUIType.Panel && flattenNode.UIType != GUIType.Null)
                        {
                            error = $"Operation[{i}] flatten_group target '{operation.targetId}' must be a Null/Panel wrapper.";
                            return false;
                        }
                        if (HasDirectChildWithUIType(nodes, operation.targetId, GUIType.Background))
                        {
                            error = $"Operation[{i}] flatten_group target '{operation.targetId}' has a direct Background child.";
                            return false;
                        }
                        FlattenDryNode(nodes, operation.targetId, flattenNode.ParentId);
                        if (createdGeneratedIds.Contains(operation.targetId))
                        {
                            usedGeneratedIds.Add(operation.targetId);
                        }
                        break;

                    case AiPatchOperationKind.SetUIType:
                        if (!nodes.TryGetValue(operation.targetId, out var setNode))
                        {
                            error = $"Operation[{i}] set_ui_type target '{operation.targetId}' does not exist.";
                            return false;
                        }
                        TryParseUIType(operation.uiType, out var uiType);
                        if (!CanApplyUITypeToDryNode(nodes, setNode, uiType))
                        {
                            error = $"Operation[{i}] set_ui_type '{operation.targetId}' -> '{uiType}' is incompatible with layerType '{setNode.LayerType}'.";
                            return false;
                        }
                        setNode.UIType = uiType;
                        if (createdGeneratedIds.Contains(operation.targetId))
                        {
                            usedGeneratedIds.Add(operation.targetId);
                        }
                        break;

                    case AiPatchOperationKind.RenameNode:
                        if (!nodes.ContainsKey(operation.targetId))
                        {
                            error = $"Operation[{i}] rename_node target '{operation.targetId}' does not exist.";
                            return false;
                        }
                        break;

                    case AiPatchOperationKind.DeleteGeneratedGroup:
                        if (!nodes.TryGetValue(operation.targetId, out var deleteNode))
                        {
                            error = $"Operation[{i}] delete_generated_group target '{operation.targetId}' does not exist.";
                            return false;
                        }
                        if (!deleteNode.Generated)
                        {
                            error = $"Operation[{i}] delete_generated_group target '{operation.targetId}' is not a generated group.";
                            return false;
                        }
                        if (HasDirectChildren(nodes, operation.targetId))
                        {
                            error = $"Operation[{i}] delete_generated_group target '{operation.targetId}' still has children in dry-run tree.";
                            return false;
                        }
                        nodes.Remove(operation.targetId);
                        usedGeneratedIds.Add(operation.targetId);
                        break;
                }
            }

            foreach (var generatedId in createdGeneratedIds)
            {
                if (!usedGeneratedIds.Contains(generatedId))
                {
                    error = $"Generated group '{generatedId}' is never used by move_node, flatten_group, set_ui_type, or delete_generated_group.";
                    return false;
                }
            }

            return ValidateFinalSemanticParents(nodes, patch, out error);
        }

        private static Dictionary<string, DryNode> BuildDryNodeMap(AiAnalysisPackageDocument package)
        {
            var nodes = new Dictionary<string, DryNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["root"] = new DryNode
                {
                    Id = "root",
                    ParentId = string.Empty,
                    LayerType = PsdLayerType.LayerGroup.ToString(),
                    UIType = GUIType.Null,
                    Generated = false,
                    HasDescendantTextLayer = false
                }
            };

            for (int i = 0; i < package.nodes.Count; i++)
            {
                var node = package.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                GUIType uiType = GUIType.Null;
                Enum.TryParse(node.uiType, true, out uiType);
                nodes[node.id] = new DryNode
                {
                    Id = node.id,
                    ParentId = string.IsNullOrWhiteSpace(node.parentId) ? "root" : node.parentId,
                    LayerType = node.layerType ?? string.Empty,
                    UIType = uiType,
                    Generated = node.isGeneratedNode,
                    HasDescendantTextLayer = false
                };
            }

            return nodes;
        }

        private static bool ValidateOperationOrder(AiPatchOperationKind previous, AiPatchOperationKind current)
        {
            return GetOperationPhase(current) >= GetOperationPhase(previous);
        }

        internal static bool NormalizeOperationOrder(AiPatchDocument patch)
        {
            if (patch == null || patch.operations == null || patch.operations.Count < 2)
            {
                return false;
            }

            int previousPhase = int.MinValue;
            bool needsReorder = false;
            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (operation == null || !AiPatchOperationNames.TryParse(operation.op, out var kind))
                {
                    return false;
                }

                int phase = GetOperationPhase(kind);
                if (phase < previousPhase)
                {
                    needsReorder = true;
                    break;
                }

                previousPhase = phase;
            }

            if (!needsReorder)
            {
                return false;
            }

            var createPhase = new List<AiPatchOperation>();
            var structurePhase = new List<AiPatchOperation>();
            var typePhase = new List<AiPatchOperation>();
            var cleanupPhase = new List<AiPatchOperation>();

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                AiPatchOperationNames.TryParse(operation.op, out var kind);
                switch (GetOperationPhase(kind))
                {
                    case 0:
                        createPhase.Add(operation);
                        break;
                    case 1:
                        structurePhase.Add(operation);
                        break;
                    case 2:
                        typePhase.Add(operation);
                        break;
                    case 3:
                        cleanupPhase.Add(operation);
                        break;
                }
            }

            patch.operations.Clear();
            patch.operations.AddRange(createPhase);
            patch.operations.AddRange(structurePhase);
            patch.operations.AddRange(typePhase);
            patch.operations.AddRange(cleanupPhase);
            return true;
        }

        internal static int GetOperationPhase(AiPatchOperationKind kind)
        {
            switch (kind)
            {
                case AiPatchOperationKind.CreateGroup:
                    return 0;
                case AiPatchOperationKind.MoveNode:
                case AiPatchOperationKind.FlattenGroup:
                    return 1;
                case AiPatchOperationKind.SetUIType:
                case AiPatchOperationKind.RenameNode:
                    return 2;
                case AiPatchOperationKind.DeleteGeneratedGroup:
                    return 3;
                default:
                    return 0;
            }
        }

        private static bool WouldCreateCycle(Dictionary<string, DryNode> nodes, string targetId, string newParentId)
        {
            string current = newParentId;
            for (int guard = 0; guard < nodes.Count + 1; guard++)
            {
                if (string.Equals(current, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!nodes.TryGetValue(current, out var node) || string.IsNullOrWhiteSpace(node.ParentId))
                {
                    return false;
                }
                current = node.ParentId;
            }
            return true;
        }

        private static bool HasDirectChildren(Dictionary<string, DryNode> nodes, string parentId)
        {
            foreach (var pair in nodes)
            {
                if (string.Equals(pair.Value.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasDirectChildWithUIType(Dictionary<string, DryNode> nodes, string parentId, GUIType uiType)
        {
            foreach (var pair in nodes)
            {
                var child = pair.Value;
                if (child != null
                    && string.Equals(child.ParentId, parentId, StringComparison.OrdinalIgnoreCase)
                    && child.UIType == uiType)
                {
                    return true;
                }
            }
            return false;
        }

        private static void FlattenDryNode(Dictionary<string, DryNode> nodes, string targetId, string parentId)
        {
            foreach (var pair in nodes)
            {
                var child = pair.Value;
                if (child != null && string.Equals(child.ParentId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    child.ParentId = parentId;
                }
            }

            nodes.Remove(targetId);
        }

        private static bool CanApplyUITypeToDryNode(Dictionary<string, DryNode> nodes, DryNode node, GUIType uiType)
        {
            if (node == null)
            {
                return false;
            }
            if (uiType == GUIType.Null)
            {
                return true;
            }
            if (UGUIParser.IsCompositeControlType(uiType))
            {
                return string.Equals(node.LayerType, PsdLayerType.LayerGroup.ToString(), StringComparison.Ordinal);
            }
            if (IsTextSemanticType(uiType))
            {
                return string.Equals(node.LayerType, PsdLayerType.TextLayer.ToString(), StringComparison.Ordinal);
            }
            if (uiType == GUIType.Text)
            {
                return string.Equals(node.LayerType, PsdLayerType.TextLayer.ToString(), StringComparison.Ordinal);
            }
            if (IsImageSemanticType(uiType))
            {
                return !string.Equals(node.LayerType, PsdLayerType.TextLayer.ToString(), StringComparison.Ordinal)
                    && !string.Equals(node.LayerType, PsdLayerType.Unknown.ToString(), StringComparison.Ordinal);
            }
            return true;
        }

        private static bool HasDescendantTextLayer(Dictionary<string, DryNode> nodes, string nodeId)
        {
            foreach (var pair in nodes)
            {
                var child = pair.Value;
                if (!string.Equals(child.ParentId, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(child.LayerType, PsdLayerType.TextLayer.ToString(), StringComparison.Ordinal) || HasDescendantTextLayer(nodes, child.Id))
                {
                    return true;
                }
            }
            return false;
        }

        // 原则：个别语义角色的 owner 缺失不阻断 patch 应用。
        // 只收集 warning，始终返回 true。AI 的正确部分必须生效。
        private static bool ValidateFinalSemanticParents(Dictionary<string, DryNode> nodes, AiPatchDocument patch, out string error)
        {
            error = null;
            var warnings = new List<string>();
            foreach (var pair in nodes)
            {
                var node = pair.Value;
                if (node == null || !UGUIParser.IsSemanticUIType(node.UIType))
                {
                    continue;
                }
                if (!nodes.TryGetValue(node.ParentId, out var parent) || !AiHelperContractCatalog.AllowsRole(parent.UIType, node.UIType))
                {
                    warnings.Add($"Semantic node '{node.Id}' with uiType '{node.UIType}' has no compatible owner after dry-run (parent is '{parent?.UIType}').");
                }
            }

            if (warnings.Count > 0)
            {
                error = string.Join("\n", warnings);
            }
            return true;
        }

        private static bool IsTextSemanticType(GUIType uiType)
        {
            return AiHelperContractCatalog.IsTextRole(uiType);
        }

        private static bool IsImageSemanticType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Background:
                case GUIType.Button_Highlight:
                case GUIType.Button_Press:
                case GUIType.Button_Select:
                case GUIType.Button_Disable:
                case GUIType.Dropdown_Arrow:
                case GUIType.Toggle_Checkmark:
                case GUIType.Slider_Fill:
                case GUIType.Slider_Handle:
                case GUIType.ScrollView_Viewport:
                case GUIType.ScrollView_HorizontalBarBG:
                case GUIType.ScrollView_HorizontalBar:
                case GUIType.ScrollView_VerticalBarBG:
                case GUIType.ScrollView_VerticalBar:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryParseUIType(string value, out GUIType uiType)
        {
            uiType = GUIType.Null;
            if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value, true, out uiType) || !Enum.IsDefined(typeof(GUIType), uiType))
            {
                return false;
            }
            if (uiType == GUIType.Null)
            {
                return true;
            }
            return IsAllowedAiUIType(uiType);
        }

        private static bool TryParseCurrentAuditUIType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, GUIType.Null.ToString(), StringComparison.Ordinal))
            {
                return true;
            }

            return Enum.TryParse(value, true, out GUIType uiType) && Enum.IsDefined(typeof(GUIType), uiType);
        }

        private static bool TryParsePredictedAuditUIType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, GUIType.Null.ToString(), StringComparison.Ordinal))
            {
                return true;
            }

            return TryParseUIType(value, out _);
        }

        private static bool IsValidVerdict(string value)
        {
            return string.Equals(value, "correct", StringComparison.Ordinal)
                || string.Equals(value, "corrected", StringComparison.Ordinal)
                || string.Equals(value, "removed", StringComparison.Ordinal);
        }

        private static bool IsAllowedAiUIType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.TMPText:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                    return false;
                default:
                    return true;
            }
        }
    }
}
#endif
