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
using System.Collections.Generic;
using System.Text;
using cn.efunstudio.psdreader.PsdParser;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class PsdStructureNormalizationReport
    {
        internal int MovedCount;
        internal int BackgroundDowngradeCount;
        internal int FlattenedNullCount;
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> Operations = new List<string>();

        internal bool HasChanges => MovedCount > 0 || BackgroundDowngradeCount > 0 || FlattenedNullCount > 0;

        internal string BuildSummary()
        {
            var builder = new StringBuilder(256);
            builder.AppendLine("本地归一化完成。");
            builder.AppendLine($"移动子控件: {MovedCount}");
            builder.AppendLine($"Background 降级为 Image: {BackgroundDowngradeCount}");
            builder.AppendLine($"扁平化 Null 包裹层: {FlattenedNullCount}");
            if (Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("警告:");
                for (int i = 0; i < Warnings.Count; i++)
                {
                    builder.AppendLine("- " + Warnings[i]);
                }
            }
            return builder.ToString().TrimEnd();
        }
    }

    internal static class PsdStructureNormalizer
    {
        private sealed class MovePlan
        {
            internal PsdLayerNode Node;
            internal PsdLayerNode Owner;
            internal int OrderKey;
        }

        internal static bool Normalize(Psd2UIFormConverter converter, bool registerUndo, out PsdStructureNormalizationReport report)
        {
            report = new PsdStructureNormalizationReport();
            if (converter == null)
            {
                report.Warnings.Add("Converter 为空，无法执行本地归一化。");
                return false;
            }

            int undoGroup = -1;
            if (registerUndo)
            {
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("PSD2UIForm Normalize Structure");
                Undo.RegisterCompleteObjectUndo(converter.gameObject, "PSD2UIForm Normalize Structure");
            }

            try
            {
                converter.ApplyGroupDerivedStates();

                var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                if (nodes == null || nodes.Length == 0)
                {
                    return true;
                }

                NormalizeOwnership(nodes, report, registerUndo);
                FlattenMeaninglessNullWrappers(converter, report, registerUndo);
                converter.RefreshAllLayerNodeHelpers();
                EditorUtility.SetDirty(converter.gameObject);
                return true;
            }
            finally
            {
                if (registerUndo && undoGroup >= 0)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                }
            }
        }

        private static void NormalizeOwnership(PsdLayerNode[] nodes, PsdStructureNormalizationReport report, bool registerUndo)
        {
            var orderKeys = CaptureHierarchyOrderKeys(nodes);
            var moves = new List<MovePlan>(16);
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || !UGUIParser.RequiresOwnerResolution(node.UIType))
                {
                    continue;
                }

                var owner = node.FindNearestCompatibleOwner();
                if (owner == null)
                {
                    if (node.UIType == GUIType.Background)
                    {
                        if (registerUndo)
                        {
                            Undo.RecordObject(node, "Normalize Background Role");
                        }
                        node.SetUIType(GUIType.Image, false);
                        EditorUtility.SetDirty(node);
                        report.BackgroundDowngradeCount++;
                        report.Operations.Add($"background_to_image:{node.name}");
                    }
                    else
                    {
                        report.Warnings.Add($"节点 '{node.name}' ({node.UIType}) 未找到兼容 owner，跳过结构修正。");
                    }
                    continue;
                }

                if (node.transform.parent == owner.transform)
                {
                    continue;
                }

                moves.Add(new MovePlan
                {
                    Node = node,
                    Owner = owner,
                    OrderKey = GetOrderKey(orderKeys, node.transform)
                });
            }

            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                if (move?.Node == null || move.Owner == null)
                {
                    continue;
                }

                if (move.Owner.transform.IsChildOf(move.Node.transform))
                {
                    report.Warnings.Add($"节点 '{move.Node.name}' 不能移动到自己的子层级 '{move.Owner.name}' 下，已跳过。");
                    continue;
                }

                if (registerUndo)
                {
                    Undo.SetTransformParent(move.Node.transform, move.Owner.transform, "Normalize PSD Structure");
                }
                else
                {
                    move.Node.transform.SetParent(move.Owner.transform, true);
                }

                PlaceTransformByOrderKey(move.Node.transform, move.Owner.transform, move.OrderKey, orderKeys);
                orderKeys[move.Node.transform] = move.OrderKey;
                EditorUtility.SetDirty(move.Node);
                report.MovedCount++;
                report.Operations.Add($"move:{move.Node.name}->{move.Owner.name}");
            }
        }

        private static Dictionary<Transform, int> CaptureHierarchyOrderKeys(PsdLayerNode[] nodes)
        {
            var orderKeys = new Dictionary<Transform, int>(nodes != null ? nodes.Length : 0);
            if (nodes == null || nodes.Length == 0)
            {
                return orderKeys;
            }

            var root = nodes[0] != null ? nodes[0].transform.root : null;
            if (root == null)
            {
                return orderKeys;
            }

            int nextOrderKey = 0;
            CaptureHierarchyOrderKeysRecursive(root, orderKeys, ref nextOrderKey);
            return orderKeys;
        }

        private static void CaptureHierarchyOrderKeysRecursive(Transform current, Dictionary<Transform, int> orderKeys, ref int nextOrderKey)
        {
            if (current == null || orderKeys == null)
            {
                return;
            }

            orderKeys[current] = nextOrderKey++;
            for (int i = 0; i < current.childCount; i++)
            {
                CaptureHierarchyOrderKeysRecursive(current.GetChild(i), orderKeys, ref nextOrderKey);
            }
        }

        private static void PlaceTransformByOrderKey(Transform target, Transform parent, int desiredOrderKey, Dictionary<Transform, int> orderKeys)
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

                if (desiredOrderKey < GetOrderKey(orderKeys, sibling))
                {
                    siblingIndex = i;
                    break;
                }
            }

            target.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1)));
        }

        private static int GetOrderKey(Dictionary<Transform, int> orderKeys, Transform transform)
        {
            if (transform == null)
            {
                return int.MaxValue;
            }

            if (orderKeys != null && orderKeys.TryGetValue(transform, out var orderKey))
            {
                return orderKey;
            }

            return int.MaxValue;
        }

        private static void FlattenMeaninglessNullWrappers(Psd2UIFormConverter converter, PsdStructureNormalizationReport report, bool registerUndo)
        {
            bool changed;
            do
            {
                changed = false;
                var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                for (int i = nodes.Length - 1; i >= 0; i--)
                {
                    var node = nodes[i];
                    if (!IsMeaninglessNullWrapper(converter, node))
                    {
                        continue;
                    }

                    var parent = node.transform.parent;
                    if (parent == null)
                    {
                        continue;
                    }

                    int insertIndex = node.transform.GetSiblingIndex();
                    while (node.transform.childCount > 0)
                    {
                        var child = node.transform.GetChild(0);
                        if (registerUndo)
                        {
                            Undo.SetTransformParent(child, parent, "Normalize PSD Structure");
                        }
                        else
                        {
                            child.SetParent(parent, true);
                        }
                        child.SetSiblingIndex(insertIndex++);
                    }

                    if (registerUndo)
                    {
                        Undo.DestroyObjectImmediate(node.gameObject);
                    }
                    else
                    {
                        Object.DestroyImmediate(node.gameObject);
                    }

                    report.FlattenedNullCount++;
                    report.Operations.Add("flatten_null");
                    changed = true;
                    break;
                }
            }
            while (changed);
        }

        private static bool IsMeaninglessNullWrapper(Psd2UIFormConverter converter, PsdLayerNode node)
        {
            if (converter == null || node == null || node.transform == converter.transform)
            {
                return false;
            }

            if (node.UIType != GUIType.Null || node.LayerType != PsdLayerType.LayerGroup)
            {
                return false;
            }

            if (node.HasReuseReference || node.HasReusePrefabReference || node.CollapseChildrenForGeneration)
            {
                return false;
            }

            if (node.transform.childCount > 1)
            {
                return false;
            }

            for (int i = 0; i < node.transform.childCount; i++)
            {
                var childNode = node.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode != null && childNode.UIType == GUIType.Background)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
