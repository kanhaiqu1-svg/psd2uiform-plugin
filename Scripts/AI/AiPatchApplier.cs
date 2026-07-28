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
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiPatchApplier
    {
        private sealed class NodeIndex
        {
            public readonly Dictionary<string, GameObject> ObjectsById = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, PsdLayerNode> NodesById = new Dictionary<string, PsdLayerNode>(StringComparer.OrdinalIgnoreCase);
            public readonly List<PsdLayerNode> GeneratedNodes = new List<PsdLayerNode>();
            public readonly Dictionary<Transform, int> OrderKeysByTransform = new Dictionary<Transform, int>();
        }

        internal bool Apply(Psd2UIFormConverter converter, AiPatchDocument patch, out string error)
        {
            error = null;
            if (converter == null)
            {
                error = "Converter is null.";
                return false;
            }
            if (patch == null)
            {
                error = "Patch document is null.";
                return false;
            }

            AiPatchSemanticNormalizer.NormalizeRequiredFields(patch);
            if (patch.operations == null)
            {
                patch.operations = new List<AiPatchOperation>();
            }
            var validator = new AiPatchValidator();
            if (!validator.Validate(patch, out error))
            {
                Debug.LogWarning("[PSD2UIForm.AI] Patch 基础校验未完全通过，将继续应用可执行项并跳过无效项。error=" + error);
                error = null;
            }

            var nodeIndex = BuildIndex(converter);
            var warnings = new List<string>(16);
            var skippedOperationIndexes = new HashSet<int>();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply AI Patch");
            Undo.RegisterFullObjectHierarchyUndo(converter.gameObject, "Apply AI Patch");

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (!TryGetOperationKind(operation, i, warnings, skippedOperationIndexes, out var opKind))
                {
                    continue;
                }

                try
                {
                    switch (opKind)
                    {
                        case AiPatchOperationKind.CreateGroup:
                            ApplyCreateGroup(converter, nodeIndex, operation, warnings);
                            break;
                        case AiPatchOperationKind.MoveNode:
                            ApplyMoveNode(nodeIndex, operation, warnings);
                            break;
                        case AiPatchOperationKind.FlattenGroup:
                            ApplyFlattenGroup(nodeIndex, operation, warnings);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Skipped operation[{i}] '{operation.op}' on '{GetOperationTarget(operation)}': {ex.Message}");
                }
            }

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (!TryGetOperationKind(operation, i, warnings, skippedOperationIndexes, out var opKind))
                {
                    continue;
                }

                try
                {
                    switch (opKind)
                    {
                        case AiPatchOperationKind.SetUIType:
                            ApplySetUIType(nodeIndex, operation, warnings);
                            break;
                        case AiPatchOperationKind.RenameNode:
                            ApplyRenameNode(nodeIndex, operation, warnings);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Skipped operation[{i}] '{operation.op}' on '{GetOperationTarget(operation)}': {ex.Message}");
                }
            }

            for (int i = 0; i < patch.operations.Count; i++)
            {
                var operation = patch.operations[i];
                if (!TryGetOperationKind(operation, i, warnings, skippedOperationIndexes, out var opKind))
                {
                    continue;
                }

                if (opKind != AiPatchOperationKind.DeleteGeneratedGroup)
                {
                    continue;
                }

                try
                {
                    ApplyDeleteGeneratedGroup(nodeIndex, operation, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Skipped operation[{i}] '{operation.op}' on '{GetOperationTarget(operation)}': {ex.Message}");
                }
            }

            RefreshGeneratedGroupRects(nodeIndex.GeneratedNodes);

            var structureValidator = new AiStructureProtocolValidator();
            structureValidator.AppendWarnings(converter, warnings);
            EditorUtility.SetDirty(converter.gameObject);
            Undo.CollapseUndoOperations(undoGroup);

            if (warnings.Count > 0)
            {
                Debug.LogWarning("Apply AI Patch completed with warnings:\n- " + string.Join("\n- ", warnings));
            }

            return true;
        }

        private static bool TryGetOperationKind(AiPatchOperation operation, int index, List<string> warnings, HashSet<int> skippedOperationIndexes, out AiPatchOperationKind opKind)
        {
            opKind = AiPatchOperationKind.None;
            if (operation == null)
            {
                AddOperationWarningOnce(index, skippedOperationIndexes, warnings, $"Skipped operation[{index}]: operation is null.");
                return false;
            }
            if (!AiPatchOperationNames.TryParse(operation.op, out opKind))
            {
                AddOperationWarningOnce(index, skippedOperationIndexes, warnings, $"Skipped operation[{index}]: unsupported patch operation '{operation.op}'.");
                return false;
            }
            return true;
        }

        private static void AddOperationWarningOnce(int index, HashSet<int> skippedOperationIndexes, List<string> warnings, string warning)
        {
            if (warnings == null || skippedOperationIndexes == null || !skippedOperationIndexes.Add(index))
            {
                return;
            }
            warnings.Add(warning);
        }

        private static string GetOperationTarget(AiPatchOperation operation)
        {
            if (operation == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(operation.targetId)) return operation.targetId;
            if (!string.IsNullOrWhiteSpace(operation.id)) return operation.id;
            return string.Empty;
        }

        private static NodeIndex BuildIndex(Psd2UIFormConverter converter)
        {
            var index = new NodeIndex();
            index.ObjectsById["root"] = converter.gameObject;

            var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var nodeId = AiNodeIdUtility.GetNodeId(converter, node);
                if (string.IsNullOrWhiteSpace(nodeId)) continue;

                index.ObjectsById[nodeId] = node.gameObject;
                index.NodesById[nodeId] = node;
            }
            return index;
        }

        private static void ApplyCreateGroup(Psd2UIFormConverter converter, NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(operation.id) || nodeIndex.ObjectsById.ContainsKey(operation.id))
            {
                warnings?.Add($"Skipped create_group '{operation.id}': generated id is empty or already exists.");
                return;
            }
            if (!nodeIndex.ObjectsById.TryGetValue(operation.parentId, out var parentObject) || parentObject == null)
            {
                warnings?.Add($"Skipped create_group '{operation.id}': parent '{operation.parentId}' does not exist.");
                return;
            }

            var uiType = GUIType.Null;
            AiPatchValidator.TryParseUIType(operation.uiType, out uiType);
            var nodeName = string.IsNullOrWhiteSpace(operation.name) ? operation.id : operation.name;
            if (TryReuseExistingGeneratedGroup(nodeIndex, parentObject.transform, operation.id, nodeName, uiType, out var existingNode))
            {
                if (operation.insertIndex >= 0)
                {
                    PlaceTransformByOrderKey(nodeIndex, existingNode.transform, parentObject.transform, operation.insertIndex);
                    nodeIndex.OrderKeysByTransform[existingNode.transform] = operation.insertIndex;
                }

                nodeIndex.ObjectsById[operation.id] = existingNode.gameObject;
                nodeIndex.NodesById[operation.id] = existingNode;
                return;
            }

            var groupObject = new GameObject(nodeName, typeof(RectTransform));
            groupObject.transform.SetParent(parentObject.transform, false);

            if (operation.insertIndex >= 0)
            {
                PlaceTransformByOrderKey(nodeIndex, groupObject.transform, parentObject.transform, operation.insertIndex);
            }

            var layerNode = groupObject.AddComponent<PsdLayerNode>();
            layerNode.ConfigureGeneratedGroupNode(nodeName, Rect.zero, operation.id);
            layerNode.SetUIType(uiType, true);

            nodeIndex.ObjectsById[operation.id] = groupObject;
            nodeIndex.NodesById[operation.id] = layerNode;
            nodeIndex.GeneratedNodes.Add(layerNode);
            nodeIndex.OrderKeysByTransform[groupObject.transform] = operation.insertIndex;
        }

        private static bool TryReuseExistingGeneratedGroup(
            NodeIndex nodeIndex,
            Transform parent,
            string operationId,
            string nodeName,
            GUIType uiType,
            out PsdLayerNode node)
        {
            node = null;
            if (parent == null || string.IsNullOrWhiteSpace(operationId))
            {
                return false;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode == null || !AiNodeIdUtility.IsGeneratedGroupNode(childNode))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(childNode.GeneratedNodeId))
                {
                    if (!string.Equals(childNode.GeneratedNodeId, operationId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!string.Equals(child.name, nodeName, StringComparison.Ordinal)
                        || childNode.UIType != uiType)
                    {
                        continue;
                    }
                }

                childNode.SetGeneratedNodeId(operationId);
                nodeIndex.ObjectsById[operationId] = child.gameObject;
                nodeIndex.NodesById[operationId] = childNode;
                node = childNode;
                return true;
            }

            return false;
        }

        private static void ApplyMoveNode(NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (!nodeIndex.ObjectsById.TryGetValue(operation.targetId, out var targetObject) || targetObject == null)
            {
                warnings?.Add($"Skipped move_node '{operation.targetId}': target does not exist.");
                return;
            }
            if (!nodeIndex.ObjectsById.TryGetValue(operation.newParentId, out var newParentObject) || newParentObject == null)
            {
                warnings?.Add($"Skipped move_node '{operation.targetId}': new parent '{operation.newParentId}' does not exist.");
                return;
            }
            if (newParentObject.transform.IsChildOf(targetObject.transform))
            {
                warnings?.Add($"Skipped move_node '{operation.targetId}': target cannot be parent of its own ancestor chain.");
                return;
            }

            targetObject.transform.SetParent(newParentObject.transform, true);
            if (operation.insertIndex >= 0)
            {
                PlaceTransformByOrderKey(nodeIndex, targetObject.transform, newParentObject.transform, operation.insertIndex);
                nodeIndex.OrderKeysByTransform[targetObject.transform] = operation.insertIndex;
            }
        }

        private static void PlaceTransformByOrderKey(NodeIndex nodeIndex, Transform target, Transform parent, int desiredOrderKey)
        {
            if (target == null || parent == null)
            {
                return;
            }

            int siblingIndex = parent.childCount - 1;
            for (int i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling == null || sibling == target)
                {
                    continue;
                }

                int siblingOrderKey = GetOrderKey(nodeIndex, sibling, i);
                if (desiredOrderKey < siblingOrderKey)
                {
                    siblingIndex = i;
                    break;
                }
            }

            target.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1)));
        }

        private static int GetOrderKey(NodeIndex nodeIndex, Transform transform, int fallbackSiblingIndex)
        {
            if (transform == null)
            {
                return int.MaxValue;
            }

            if (nodeIndex != null && nodeIndex.OrderKeysByTransform.TryGetValue(transform, out var orderKey))
            {
                return orderKey;
            }

            return fallbackSiblingIndex;
        }

        private static void ApplyFlattenGroup(NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (!nodeIndex.ObjectsById.TryGetValue(operation.targetId, out var targetObject) || targetObject == null)
            {
                warnings?.Add($"Skipped flatten_group '{operation.targetId}': target does not exist.");
                return;
            }

            var targetNode = targetObject.GetComponent<PsdLayerNode>();
            if (targetNode == null || (targetNode.UIType != GUIType.Panel && targetNode.UIType != GUIType.Null))
            {
                warnings?.Add($"Skipped flatten_group '{operation.targetId}': target is not a Null/Panel wrapper.");
                return;
            }

            if (HasDirectChildWithUIType(targetObject.transform, GUIType.Background))
            {
                warnings?.Add($"Skipped flatten_group '{operation.targetId}': target has a direct Background child.");
                return;
            }

            var parent = targetObject.transform.parent;
            if (parent == null)
            {
                warnings?.Add($"Skipped flatten_group '{operation.targetId}': target has no parent.");
                return;
            }

            int insertIndex = targetObject.transform.GetSiblingIndex();
            int childCount = targetObject.transform.childCount;
            var children = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                children[i] = targetObject.transform.GetChild(i);
            }

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null)
                {
                    continue;
                }
                child.SetParent(parent, true);
                child.SetSiblingIndex(Mathf.Min(insertIndex + i, parent.childCount - 1));
            }

            nodeIndex.ObjectsById.Remove(operation.targetId);
            nodeIndex.NodesById.Remove(operation.targetId);
            if (targetNode != null)
            {
                nodeIndex.GeneratedNodes.Remove(targetNode);
            }
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        private static void ApplySetUIType(NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(operation.targetId))
            {
                warnings?.Add("Skipped set_ui_type: targetId is empty.");
                return;
            }
            if (!nodeIndex.NodesById.TryGetValue(operation.targetId, out var node) || node == null)
            {
                warnings?.Add($"Skipped set_ui_type '{operation.targetId}': target does not exist.");
                return;
            }
            if (!AiPatchValidator.TryParseUIType(operation.uiType, out var uiType))
            {
                warnings?.Add($"Skipped set_ui_type '{operation.targetId}': invalid uiType '{operation.uiType}'.");
                return;
            }
            if (!CanApplyUITypeToNode(node, uiType))
            {
                warnings?.Add($"Skipped set_ui_type '{operation.targetId}' -> '{uiType}': incompatible with current layer type.");
                return;
            }
            node.SetUIType(uiType, true);
        }

        private static void ApplyRenameNode(NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (!nodeIndex.ObjectsById.TryGetValue(operation.targetId, out var targetObject) || targetObject == null)
            {
                warnings?.Add($"Skipped rename_node '{operation.targetId}': target does not exist.");
                return;
            }
            if (string.IsNullOrWhiteSpace(operation.name))
            {
                warnings?.Add($"Skipped rename_node '{operation.targetId}': name is empty.");
                return;
            }
            targetObject.name = operation.name.Trim();
        }

        private static void ApplyDeleteGeneratedGroup(NodeIndex nodeIndex, AiPatchOperation operation, List<string> warnings)
        {
            if (!nodeIndex.ObjectsById.TryGetValue(operation.targetId, out var targetObject) || targetObject == null)
            {
                warnings?.Add($"Skipped delete_generated_group '{operation.targetId}': target does not exist.");
                return;
            }
            var node = targetObject.GetComponent<PsdLayerNode>();
            if (!AiNodeIdUtility.IsGeneratedGroupNode(node))
            {
                warnings?.Add($"Skipped delete_generated_group '{operation.targetId}': target is not a generated group.");
                return;
            }
            if (targetObject.transform.childCount > 0)
            {
                warnings?.Add($"Skipped delete_generated_group '{operation.targetId}': target still has children.");
                return;
            }

            nodeIndex.ObjectsById.Remove(operation.targetId);
            nodeIndex.NodesById.Remove(operation.targetId);
            if (node != null)
            {
                nodeIndex.GeneratedNodes.Remove(node);
            }
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        private static bool CanApplyUITypeToNode(PsdLayerNode node, GUIType uiType)
        {
            if (node == null)
            {
                return false;
            }

            if (uiType == GUIType.Null)
            {
                return true;
            }

            if (IsTextSemanticType(uiType))
            {
                return node.LayerType == PsdLayerType.TextLayer;
            }

            if (uiType == GUIType.Text)
            {
                return node.LayerType == PsdLayerType.TextLayer;
            }

            if (IsImageSemanticType(uiType))
            {
                return node.LayerType != PsdLayerType.TextLayer
                    && node.LayerType != PsdLayerType.Unknown;
            }

            return true;
        }

        private static bool IsTextSemanticType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Button_Text:
                case GUIType.Dropdown_Label:
                case GUIType.InputField_Placeholder:
                case GUIType.InputField_Text:
                case GUIType.Toggle_Label:
                    return true;
                default:
                    return false;
            }
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

        private static bool HasDescendantTextLayer(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode != null && childNode.LayerType == PsdLayerType.TextLayer)
                {
                    return true;
                }

                if (HasDescendantTextLayer(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDirectChildWithUIType(Transform parent, GUIType uiType)
        {
            if (parent == null)
            {
                return false;
            }

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

        private static void RefreshGeneratedGroupRects(List<PsdLayerNode> generatedNodes)
        {
            if (generatedNodes == null || generatedNodes.Count < 1) return;

            generatedNodes.Sort((a, b) => GetTransformDepth(b?.transform).CompareTo(GetTransformDepth(a?.transform)));
            for (int i = 0; i < generatedNodes.Count; i++)
            {
                var node = generatedNodes[i];
                if (node == null) continue;

                if (!TryCalculateChildrenBounds(node.transform, out var bounds))
                {
                    bounds = Rect.zero;
                }
                node.UpdateLayerRectData(bounds);
            }
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

        private static bool TryCalculateChildrenBounds(Transform parent, out Rect bounds)
        {
            bounds = Rect.zero;
            var childNodes = parent.GetComponentsInChildren<PsdLayerNode>(true)
                .Where(node => node != null && node.transform.parent == parent)
                .ToArray();
            if (childNodes.Length < 1)
            {
                return false;
            }

            bool hasAny = false;
            float xMin = 0f;
            float yMin = 0f;
            float xMax = 0f;
            float yMax = 0f;

            for (int i = 0; i < childNodes.Length; i++)
            {
                var rect = childNodes[i].LayerRect;
                if (!hasAny)
                {
                    xMin = rect.xMin;
                    yMin = rect.yMin;
                    xMax = rect.xMax;
                    yMax = rect.yMax;
                    hasAny = true;
                    continue;
                }

                xMin = Mathf.Min(xMin, rect.xMin);
                yMin = Mathf.Min(yMin, rect.yMin);
                xMax = Mathf.Max(xMax, rect.xMax);
                yMax = Mathf.Max(yMax, rect.yMax);
            }

            if (!hasAny)
            {
                return false;
            }

            bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

    }
}
#endif
