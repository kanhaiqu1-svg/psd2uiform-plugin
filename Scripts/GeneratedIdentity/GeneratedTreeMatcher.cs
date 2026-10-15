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

/*
插件获取:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// 期望树节点(由转换器从 PsdLayerNode 树构建,匹配器在其上填写决策结果)。
    /// 节点身份使用编辑文档中的永久 ID，与名称和本轮 AI ID 无关。
    /// 模板子节点(如按钮文本)不属于期望树,由各 helper 在其元素内自行复用/更新。
    /// </summary>
    internal sealed class GeneratedTreeNode
    {
        public PsdLayerNode LayerNode;
        public string GeneratedName;
        public string SourceIdentity;
        public string PreviousTypeKey;
        public bool IsContainer;
        public bool IsPrefabRef;
        public UIHelperBase Helper;//UI 元素节点(非容器)用
        public GeneratedTreeNode Parent;
        public List<GeneratedTreeNode> Children = new List<GeneratedTreeNode>();

        // 匹配结果
        public NodeMatchKind MatchKind = NodeMatchKind.Create;
        public GameObject ReuseGo;//旧节点(contents)
        public bool Revived;//从孤儿区复活
        public bool PreservePreviousType;

        internal bool IsResolved => MatchKind == NodeMatchKind.ReuseSameType
            || MatchKind == NodeMatchKind.ReviveOrphan;
    }

    // Permanent document identity is the only matching key. Names and sibling order are presentation.
    internal static class GeneratedTreeMatcher
    {
        internal const string ContainerTypeKey = "__Container";
        internal const string OrphansRootName = "__PSD2UIForm_Orphans";

        public static void Match(GenerationContext context, GameObject contentsRoot,
            List<GeneratedTreeNode> desiredRoots, Dictionary<string, GameObject> generatedBySourceIdentity,
            HashSet<GameObject> knownGeneratedNodes)
        {
            var claimed = new HashSet<GameObject>();
            ClaimStableIdentities(desiredRoots, generatedBySourceIdentity, claimed, FindOrphansRoot(contentsRoot.transform));
            context.NodesToOrphan.Clear();
            if (knownGeneratedNodes == null) return;
            foreach (var old in knownGeneratedNodes)
                if (old != null && !claimed.Contains(old) && !HasUnclaimedGeneratedAncestor(old.transform, knownGeneratedNodes, claimed))
                    context.NodesToOrphan.Add(old);
        }

        private static void ClaimStableIdentities(
            List<GeneratedTreeNode> nodes,
            Dictionary<string, GameObject> generatedBySourceIdentity,
            HashSet<GameObject> claimed,
            Transform orphanRoot)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!string.IsNullOrEmpty(node.SourceIdentity)
                    && generatedBySourceIdentity != null
                    && generatedBySourceIdentity.TryGetValue(node.SourceIdentity, out var matched)
                    && matched != null
                    && claimed.Add(matched))
                {
                    bool revived = orphanRoot != null && IsUnder(matched.transform, orphanRoot);
                    node.ReuseGo = matched;
                    node.Revived = revived;
                    node.MatchKind = revived ? NodeMatchKind.ReviveOrphan : NodeMatchKind.ReuseSameType;
                }

                ClaimStableIdentities(node.Children, generatedBySourceIdentity, claimed, orphanRoot);
            }
        }

        private static bool HasUnclaimedGeneratedAncestor(
            Transform node,
            HashSet<GameObject> knownGeneratedNodes,
            HashSet<GameObject> claimed)
        {
            var current = node.parent;
            while (current != null)
            {
                var go = current.gameObject;
                if (knownGeneratedNodes.Contains(go) && !claimed.Contains(go)) return true;
                current = current.parent;
            }
            return false;
        }

        private static bool IsUnder(Transform node, Transform ancestor)
        {
            var current = node;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.parent;
            }
            return false;
        }

        private static Transform FindOrphansRoot(Transform contentsRoot)
        {
            for (int i = 0; i < contentsRoot.childCount; i++)
            {
                var child = contentsRoot.GetChild(i);
                if (string.Equals(child.name, OrphansRootName, StringComparison.Ordinal))
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// 从组件推导旧节点类型键(与 GUIType 名对齐,推导不出视为容器)。
        /// </summary>
        internal static string DeriveTypeKeyFromComponents(GameObject go)
        {
            if (go == null) return ContainerTypeKey;
            if (go.GetComponent<ScrollRect>() != null) return "ScrollView";
            if (go.GetComponent<TMP_Dropdown>() != null) return "TMPDropdown";
            if (go.GetComponent<Dropdown>() != null) return "Dropdown";
            if (go.GetComponent<TMP_InputField>() != null) return "TMPInputField";
            if (go.GetComponent<InputField>() != null) return "InputField";
            if (go.GetComponent<Slider>() != null) return "Slider";
            if (go.GetComponent<Toggle>() != null)
            {
                return go.GetComponentInChildren<TextMeshProUGUI>(true) != null ? "TMPToggle" : "Toggle";
            }
            if (go.GetComponent<Button>() != null)
            {
                return go.GetComponentInChildren<TextMeshProUGUI>(true) != null ? "TMPButton" : "Button";
            }
            if (go.GetComponent<Mask>() != null) return "Mask";
            if (go.GetComponent<TextMeshProUGUI>() != null) return "TMPText";
            if (go.GetComponent<Text>() != null) return "Text";
            if (go.GetComponent<ToggleGroup>() != null) return "ToggleGroup";
            if (go.GetComponent<RawImage>() != null) return "RawImage";
            if (go.GetComponent<Image>() != null) return "Image";
            return ContainerTypeKey;
        }
    }
}
#endif
