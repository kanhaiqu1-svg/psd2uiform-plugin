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
using cn.efunstudio.psdreader.PsdParser;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiStructureProtocolValidator
    {
        internal void AppendWarnings(Psd2UIFormConverter converter, List<string> warnings)
        {
            if (converter == null)
            {
                warnings?.Add("Skipped AI structure validation: converter is null.");
                return;
            }

            var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            if (nodes == null || nodes.Length < 1)
            {
                return;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                AppendMainTypeWarning(node, warnings);
                AppendContractWarnings(node, warnings);
            }
        }

        private enum RoleBacking
        {
            Image,
            Text
        }

        private static void AppendMainTypeWarning(PsdLayerNode node, List<string> warnings)
        {
            if (node == null || !AiHelperContractCatalog.IsForbiddenAiType(node.UIType))
            {
                return;
            }

            warnings?.Add($"Node '{node.name}' still uses unsupported AI main type '{node.UIType}'. The patch was kept, but this node may not parse as ordinary uGUI.");
        }

        private static void AppendContractWarnings(PsdLayerNode node, List<string> warnings)
        {
            if (node == null)
            {
                return;
            }

            var ownerType = AiHelperContractCatalog.NormalizeAiType(node.UIType);
            var contract = AiHelperContractCatalog.GetOwnerContract(ownerType);
            if (contract == null)
            {
                return;
            }

            if ((ownerType == GUIType.Panel || ownerType == GUIType.ToggleGroup) && IsGroupNode(node))
            {
                AppendGroupWarnings(node, warnings);
            }

            if (ownerType != GUIType.Null
                && ownerType != GUIType.Image
                && ownerType != GUIType.Text
                && ownerType != GUIType.Mask
                && ownerType != GUIType.FillColor)
            {
                AppendNestedSameTypeWarnings(node, warnings, ownerType, ownerType.ToString());
            }

            for (int i = 0; i < contract.allowedRoles.Length; i++)
            {
                var roleType = contract.allowedRoles[i];
                AppendRoleWarnings(node, warnings, roleType, roleType.ToString(), ResolveRoleBacking(roleType));
            }

            for (int i = 0; i < contract.allowedDirectChildControls.Length; i++)
            {
                var childType = contract.allowedDirectChildControls[i];
                AppendMainChildWarnings(node, warnings, childType, childType.ToString());
            }
        }

        private static void AppendGroupWarnings(PsdLayerNode groupNode, List<string> warnings)
        {
            if (!IsGroupNode(groupNode))
            {
                return;
            }

            int nestedBackgroundCount = CountRoleCandidateNodes(groupNode, GUIType.Background);
            int directBackgroundCount = CountDirectSemanticNodes(groupNode, GUIType.Background);
            if (directBackgroundCount > 1)
            {
                warnings?.Add($"Protocol violation: {groupNode.UIType} '{groupNode.name}' contains {directBackgroundCount} direct Background nodes. AI should keep one Background and leave other artwork as ordinary Image/Panel.");
            }

            if (nestedBackgroundCount > directBackgroundCount)
            {
                warnings?.Add($"Protocol violation: {groupNode.UIType} '{groupNode.name}' has nested Background semantics. AI should move_node the Background under '{groupNode.name}', tag the direct outer Panel as Background, or leave non-primary artwork as ordinary Image/Panel.");
            }
        }

        private static void AppendRoleWarnings(PsdLayerNode owner, List<string> warnings, GUIType uiType, string typeLabel, RoleBacking backing)
        {
            if (owner == null)
            {
                return;
            }

            int scopedCount = CountRoleCandidateNodes(owner, uiType);
            if (scopedCount > 1)
            {
                warnings?.Add($"Protocol violation: {owner.UIType} '{owner.name}' contains {scopedCount} '{typeLabel}' nodes. AI should keep exactly one primary role candidate and leave other content as ordinary Image/Text/Panel.");
            }

            var roleNode = FindFirstRoleCandidateNode(owner, uiType);
            if (roleNode == null)
            {
                return;
            }

            if (roleNode.transform.parent != owner.transform)
            {
                warnings?.Add($"Protocol violation: {owner.UIType} '{owner.name}' has '{typeLabel}' at '{GetRelativePath(owner, roleNode)}', but composite role nodes must be direct children. AI should move_node it under '{owner.name}' or tag the direct outer Panel as '{typeLabel}'.");
            }

            if (backing == RoleBacking.Text)
            {
                if (!HasTextBacking(roleNode))
                {
                    warnings?.Add($"{owner.UIType} '{owner.name}' uses '{typeLabel}' on node '{roleNode.name}' without text backing. That node was kept, but it may be ignored or downgraded later.");
                }
                else if (roleNode.LayerType != PsdLayerType.TextLayer)
                {
                    warnings?.Add($"{owner.UIType} '{owner.name}' uses '{typeLabel}' on node '{roleNode.name}' through a wrapper Panel/Null. Prefer the actual TextLayer as the final direct role node and clear intermediate text wrappers.");
                }
                return;
            }

            if (!HasImageBacking(roleNode))
            {
                warnings?.Add($"{owner.UIType} '{owner.name}' uses '{typeLabel}' on node '{roleNode.name}' without image backing. That node was kept, but it may be ignored or export unexpectedly.");
            }
        }

        private static void AppendMainChildWarnings(PsdLayerNode owner, List<string> warnings, GUIType uiType, string typeLabel)
        {
            int count = CountRoleCandidateNodes(owner, uiType);
            if (count > 1)
            {
                warnings?.Add($"Protocol violation: {owner.UIType} '{owner.name}' contains {count} '{typeLabel}' nodes. AI should keep exactly one primary child candidate and leave other content as ordinary Image/Text/Panel.");
            }

            var childNode = FindFirstRoleCandidateNode(owner, uiType);
            if (childNode != null && childNode.transform.parent != owner.transform)
            {
                warnings?.Add($"Protocol violation: {owner.UIType} '{owner.name}' has '{typeLabel}' at '{GetRelativePath(owner, childNode)}', but composite child controls must be direct children. AI should move_node it under '{owner.name}'.");
            }
        }

        private static void AppendNestedSameTypeWarnings(PsdLayerNode owner, List<string> warnings, GUIType uiType, string typeLabel)
        {
            if (owner == null)
            {
                return;
            }

            var nestedNode = FindFirstNestedSameTypeNode(owner, uiType);
            if (nestedNode == null)
            {
                return;
            }

            warnings?.Add($"Protocol suspicion: {typeLabel} '{owner.name}' still contains nested {typeLabel} '{nestedNode.name}' at '{GetRelativePath(owner, nestedNode)}'. Prefer the nearest complete main-control boundary and avoid tagging both parent and child as the same main control.");
        }

        private static int CountRoleCandidateNodes(PsdLayerNode owner, GUIType uiType)
        {
            if (owner == null || uiType == GUIType.Null)
            {
                return 0;
            }

            int count = 0;
            CountRoleCandidatesRecursive(owner.transform, uiType, ref count);
            return count;
        }

        private static PsdLayerNode FindFirstRoleCandidateNode(PsdLayerNode owner, GUIType uiType)
        {
            if (owner == null || uiType == GUIType.Null)
            {
                return null;
            }

            return FindFirstRoleCandidateRecursive(owner.transform, uiType);
        }

        private static PsdLayerNode FindFirstNestedSameTypeNode(PsdLayerNode owner, GUIType uiType)
        {
            if (owner == null)
            {
                return null;
            }

            return FindFirstNestedSameTypeRecursive(owner.transform, uiType);
        }

        private static void CountRoleCandidatesRecursive(UnityEngine.Transform parent, GUIType uiType, ref int count)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode == null)
                {
                    CountRoleCandidatesRecursive(child, uiType, ref count);
                    continue;
                }

                if (childNode.UIType == uiType)
                {
                    count++;
                }

                if (IsRoleCandidateTraversalBoundary(childNode))
                {
                    continue;
                }

                CountRoleCandidatesRecursive(child, uiType, ref count);
            }
        }

        private static PsdLayerNode FindFirstRoleCandidateRecursive(UnityEngine.Transform parent, GUIType uiType)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode == null)
                {
                    var childResult = FindFirstRoleCandidateRecursive(child, uiType);
                    if (childResult != null)
                    {
                        return childResult;
                    }
                    continue;
                }

                if (childNode.UIType == uiType)
                {
                    return childNode;
                }

                if (IsRoleCandidateTraversalBoundary(childNode))
                {
                    continue;
                }

                var result = FindFirstRoleCandidateRecursive(child, uiType);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static PsdLayerNode FindFirstNestedSameTypeRecursive(UnityEngine.Transform parent, GUIType uiType)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode == null)
                {
                    var childResult = FindFirstNestedSameTypeRecursive(child, uiType);
                    if (childResult != null)
                    {
                        return childResult;
                    }
                    continue;
                }

                if (childNode.UIType == uiType)
                {
                    return childNode;
                }

                if (IsRoleCandidateTraversalBoundary(childNode))
                {
                    continue;
                }

                var nestedResult = FindFirstNestedSameTypeRecursive(child, uiType);
                if (nestedResult != null)
                {
                    return nestedResult;
                }
            }

            return null;
        }

        private static bool IsRoleCandidateTraversalBoundary(PsdLayerNode node)
        {
            return node != null && node.IsMainUIType && node.UIType != GUIType.Null && node.UIType != GUIType.Panel;
        }

        private static string GetRelativePath(PsdLayerNode owner, PsdLayerNode node)
        {
            if (owner == null || node == null)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            var ownerTransform = owner.transform;
            var current = node.transform;
            while (current != null && current != ownerTransform)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return stack.Count > 0 ? string.Join("/", stack.ToArray()) : node.name;
        }

        private static int CountDirectSemanticNodes(PsdLayerNode owner, GUIType uiType)
        {
            if (owner == null)
            {
                return 0;
            }

            int count = 0;
            var transform = owner.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                var childNode = transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode != null && childNode.UIType == uiType)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsGroupNode(PsdLayerNode node)
        {
            return node != null && node.LayerType == PsdLayerType.LayerGroup;
        }

        private static bool HasImageBacking(PsdLayerNode node)
        {
            if (node == null || node.LayerType == PsdLayerType.Unknown || node.LayerType == PsdLayerType.TextLayer)
            {
                return false;
            }

            return true;
        }

        private static bool HasTextBacking(PsdLayerNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.LayerType == PsdLayerType.TextLayer)
            {
                return true;
            }

            if (node.LayerType != PsdLayerType.LayerGroup)
            {
                return false;
            }

            var descendants = node.GetComponentsInChildren<PsdLayerNode>(true);
            if (descendants == null || descendants.Length < 1)
            {
                return false;
            }

            for (int i = 0; i < descendants.Length; i++)
            {
                var child = descendants[i];
                if (child != null && child != node && child.LayerType == PsdLayerType.TextLayer)
                {
                    return true;
                }
            }

            return false;
        }

        private static RoleBacking ResolveRoleBacking(GUIType roleType)
        {
            switch (AiHelperContractCatalog.GetRoleBacking(roleType))
            {
                case AiHelperContractCatalog.RoleBackingKind.Text:
                    return RoleBacking.Text;
                default:
                    return RoleBacking.Image;
            }
        }
    }
}
#endif
