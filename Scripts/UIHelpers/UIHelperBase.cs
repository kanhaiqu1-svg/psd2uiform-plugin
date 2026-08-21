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
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public abstract class UIHelperBase : MonoBehaviour
    {
        internal PsdLayerNode LayerNode => this.GetComponent<PsdLayerNode>();

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            ParseAndAttachUIElements();
        }

        /// <summary>
        /// 解析并关联UI元素,并且返回已经关联过的图层(已关联图层不再处理)
        /// </summary>
        internal abstract void ParseAndAttachUIElements();

        /// <summary>
        /// 获取UI依赖的LayerNodes
        /// </summary>
        internal abstract PsdLayerNode[] GetDependencies();

        internal virtual void ResolveDependencyClaims(Dictionary<int, HashSet<int>> dependencyOwnerIds)
        {
        }

        /// <summary>
        /// 把UI实例进行UI元素初始化
        /// </summary>
        protected abstract void InitUIElements(GameObject uiRoot);

        /// <summary>
        /// UI生成并挂到目标父节点后触发。
        /// </summary>
        internal virtual void OnUIParented(GameObject uiRoot)
        {
        }

        /// <summary>
        /// UI生成树全部就绪后触发。
        /// </summary>
        internal virtual void OnGeneratedHierarchyReady(GameObject uiRoot)
        {
        }

        /// <summary>
        /// 筛选出UI依赖的非空LayerNode
        /// </summary>
        protected PsdLayerNode[] CalculateDependencies(params PsdLayerNode[] nodes)
        {
            if (nodes == null || nodes.Length == 0) return null;

            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node == null || node == LayerNode) ArrayUtility.RemoveAt(ref nodes, i);
            }
            return nodes;
        }

        protected PsdLayerNode FindOwnedNode(params GUIType[] uiTypes)
        {
            return LayerNode != null ? LayerNode.FindOwnedSubLayerNode(uiTypes) : null;
        }

        protected PsdLayerNode[] FindOwnedNodes(params GUIType[] uiTypes)
        {
            return LayerNode != null ? LayerNode.FindOwnedSubLayerNodes(uiTypes) : null;
        }

        protected bool IsDependencyClaimedByOtherHelper(PsdLayerNode node, Dictionary<int, HashSet<int>> dependencyOwnerIds)
        {
            if (node == null || dependencyOwnerIds == null) return false;
            if (!dependencyOwnerIds.TryGetValue(GetDependencyClaimId(node), out var ownerIds) || ownerIds == null) return false;

            int selfId = GetInstanceID();
            foreach (var ownerId in ownerIds)
            {
                if (ownerId != selfId)
                {
                    return true;
                }
            }
            return false;
        }

        internal static int GetDependencyClaimId(PsdLayerNode node)
        {
            return node != null && node.gameObject != null ? node.gameObject.GetInstanceID() : 0;
        }

        internal GameObject CreateUI(GameObject uiInstance = null)
        {
            if (!LayerNode.IsMainUIType
                || (LayerNode.UIType == GUIType.Null && !LayerNode.RasterizeGroupForOpacity))
            {
                return null;
            }

            uiInstance = CreateOrReuseRoot(uiInstance);
            if (uiInstance == null) return null;

            if (LayerNode.IsMainUIType)
            {
                uiInstance.name = LayerNode.GetGeneratedObjectName();
            }

            InitUIElements(uiInstance);
            return uiInstance;
        }

        protected virtual GameObject CreateOrReuseRoot(GameObject uiInstance)
        {
            var rule = UGUIParser.Instance.GetRule(this.LayerNode.UIType);
            if (rule == null || rule.UIPrefab == null)
            {
                Debug.LogWarning($"创建UI类型{LayerNode.UIType}失败:Rule配置项不存在或UIPrefab为空");
                return null;
            }

            if (uiInstance == null)
            {
                uiInstance = GameObject.Instantiate(rule.UIPrefab, Vector3.zero, Quaternion.identity);
            }
            else if (!IsCompatibleRoot(uiInstance, rule.UIPrefab))
            {
                RebuildRootComponentsInPlace(uiInstance, rule.UIPrefab);
            }

            return uiInstance;
        }

        private static void RebuildRootComponentsInPlace(GameObject target, GameObject prefabRoot)
        {
            if (target == null || prefabRoot == null) return;

            RemoveObsoleteRootComponents(target, prefabRoot);

            var prefabComponents = prefabRoot.GetComponents<Component>();
            for (int i = 0; i < prefabComponents.Length; i++)
            {
                var prefabComponent = prefabComponents[i];
                if (prefabComponent == null) continue;

                var type = prefabComponent.GetType();
                if (type == typeof(Transform) || type == typeof(RectTransform))
                {
                    continue;
                }

                if (type == typeof(CanvasRenderer))
                {
                    if (target.GetComponent<CanvasRenderer>() == null)
                    {
                        target.AddComponent<CanvasRenderer>();
                    }
                    continue;
                }

                var targetComponent = target.GetComponent(type);
                ComponentUtility.CopyComponent(prefabComponent);
                if (targetComponent != null)
                {
                    ComponentUtility.PasteComponentValues(targetComponent);
                }
                else
                {
                    ComponentUtility.PasteComponentAsNew(target);
                }
            }
        }

        private static void RemoveObsoleteRootComponents(GameObject target, GameObject prefabRoot)
        {
            var prefabComponents = prefabRoot.GetComponents<Component>();
            var prefabTypes = new HashSet<Type>();
            for (int i = 0; i < prefabComponents.Length; i++)
            {
                var component = prefabComponents[i];
                if (component != null)
                {
                    prefabTypes.Add(component.GetType());
                }
            }

            var targetComponents = target.GetComponents<Component>();
            for (int i = targetComponents.Length - 1; i >= 0; i--)
            {
                var component = targetComponents[i];
                if (component == null) continue;

                var type = component.GetType();
                if (prefabTypes.Contains(type)) continue;
                if (!IsManagedGeneratedRootComponent(type)) continue;

                DestroyImmediate(component);
            }
        }

        protected static bool IsCompatibleRoot(GameObject existing, GameObject prefabRoot)
        {
            var expectedType = GetPrimaryRootComponentType(prefabRoot);
            var existingType = GetPrimaryRootComponentType(existing);
            if (expectedType == null || existingType == null)
            {
                return false;
            }

            return expectedType == existingType;
        }

        protected static Type GetPrimaryRootComponentType(GameObject target)
        {
            if (target == null) return null;

            var components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || IsIgnoredRootComponent(component.GetType())) continue;

                if (component is ScrollRect || component is Selectable || component is Mask)
                {
                    return component.GetType();
                }
            }

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || IsIgnoredRootComponent(component.GetType())) continue;

                if (component is Graphic)
                {
                    return component.GetType();
                }
            }

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;

                var type = component.GetType();
                if (!IsIgnoredRootComponent(type)) return type;
            }

            return null;
        }

        private static bool IsIgnoredRootComponent(Type type)
        {
            return type == typeof(Transform)
                || type == typeof(RectTransform)
                || type == typeof(CanvasRenderer)
                || type == typeof(UIStringKey)
                || type == typeof(PsdGeneratedKey);
        }

        private static bool IsManagedGeneratedRootComponent(Type type)
        {
            return typeof(Selectable).IsAssignableFrom(type)
                || typeof(Graphic).IsAssignableFrom(type)
                || type == typeof(Mask)
                || type == typeof(ScrollRect)
                || type == typeof(CanvasRenderer);
        }

        protected static T FindParentComponent<T>(Transform current) where T : Component
        {
            while (current != null)
            {
                var component = current.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
#endif
