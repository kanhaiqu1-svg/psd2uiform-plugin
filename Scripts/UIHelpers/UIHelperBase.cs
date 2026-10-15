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
#if UNITY_6000_3_OR_NEWER
using PsdObjectId = UnityEngine.EntityId;
#else
using PsdObjectId = System.Int32;
#endif
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public abstract class UIHelperBase : MonoBehaviour
    {
        internal PsdLayerNode LayerNode => this.GetComponent<PsdLayerNode>();

        // OnEnable 里做类型回退会经 SetUIType→RemoveUIHelper 在自己的调用栈上销毁组件，Unity 会拒绝并报错。
        // 用深度计数而不是布尔量：Helper 的 OnEnable 会因实例化/激活嵌套，布尔量会被内层提前清掉。
        private static int onEnableDepth;

        internal static bool ParsingFromOnEnable => onEnableDepth > 0;

        internal void DeferTypeFallback(GUIType fallbackType)
        {
            var node = LayerNode;
            var expectedType = node.UIType;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && node != null && !node.IsAiAuthoritative && node.UIType == expectedType)
                    node.SetUIType(fallbackType);
            };
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            onEnableDepth++;
            try
            {
                ParseAndAttachUIElements();
            }
            finally
            {
                onEnableDepth--;
            }
        }

        /// <summary>
        /// 解析并关联UI元素,并且返回已经关联过的图层(已关联图层不再处理)
        /// </summary>
        internal abstract void ParseAndAttachUIElements();

        /// <summary>
        /// 获取UI依赖的LayerNodes
        /// </summary>
        internal abstract PsdLayerNode[] GetDependencies();

        internal virtual void ResolveDependencyClaims(Dictionary<PsdObjectId, HashSet<PsdObjectId>> dependencyOwnerIds)
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
            if (LayerNode != null && LayerNode.IsAiAuthoritative && uiTypes.Length > 0 && AiUiContract.IsRole(uiTypes[0]))
                return LayerNode.FindOwnedSubLayerNode(uiTypes[0]);
            return LayerNode != null ? LayerNode.FindOwnedSubLayerNode(uiTypes) : null;
        }

        protected static void BindOptionalControlImage(PsdLayerNode source, Image image)
        {
            if (image == null) return;
            image.overrideSprite = null;
            var color = image.color;
            color.a = source != null ? 1f : 0f;
            image.color = color;
            if (source != null) UGUIParser.Instance.BindImage(source, image);
            else image.sprite = null;
        }

        protected PsdLayerNode[] FindOwnedNodes(params GUIType[] uiTypes)
        {
            return LayerNode != null ? LayerNode.FindOwnedSubLayerNodes(uiTypes) : null;
        }

        protected bool IsDependencyClaimedByOtherHelper(PsdLayerNode node, Dictionary<PsdObjectId, HashSet<PsdObjectId>> dependencyOwnerIds)
        {
            if (node == null || dependencyOwnerIds == null) return false;
            if (!dependencyOwnerIds.TryGetValue(GetDependencyClaimId(node), out var ownerIds) || ownerIds == null) return false;
            
#if UNITY_6000_3_OR_NEWER
            var selfId = GetEntityId();
#else
            int selfId = GetInstanceID();
#endif
            foreach (var ownerId in ownerIds)
            {
                if (ownerId != selfId)
                {
                    return true;
                }
            }
            return false;
        }

        internal static PsdObjectId GetDependencyClaimId(PsdLayerNode node)
        {
#if UNITY_6000_3_OR_NEWER
            return node != null && node.gameObject != null ? node.gameObject.GetEntityId() : default(PsdObjectId);
#else
            return node != null && node.gameObject != null ? node.gameObject.GetInstanceID() : 0;
#endif
        }

        internal GameObject CreateUI(
            Transform parent,
            GameObject uiInstance = null,
            GUIType? previousUIType = null)
        {
            if (!LayerNode.IsMainUIType
                || (LayerNode.UIType == GUIType.Null && !LayerNode.RasterizeGroupForOpacity))
            {
                return null;
            }

            bool forceRebuild = uiInstance != null
                && previousUIType.HasValue
                && previousUIType.Value != LayerNode.UIType;
            if (forceRebuild)
            {
                var previousPrefab = UGUIParser.Instance?.GetRule(previousUIType.Value)?.UIPrefab;
                RemoveManagedTemplateHierarchy(uiInstance, previousPrefab);
            }

            bool created = uiInstance == null;
            uiInstance = CreateOrReuseRoot(uiInstance, forceRebuild);
            if (uiInstance == null) return null;
            try
            {
                if (LayerNode.IsMainUIType) uiInstance.name = LayerNode.GetGeneratedObjectName();
                if (parent != null)
                {
                    uiInstance.transform.SetParent(parent, false);
                    uiInstance.transform.localScale = Vector3.one;
                }
                InitUIElements(uiInstance);
                if (LayerNode.IsAiTarget && LayerNode.HasAiSources) uiInstance.SetActive(LayerNode.AiSourceVisible);
                if (parent != null) OnUIParented(uiInstance);
                if (GenerationContext.Current != null) GenerationContext.Current.HelperRoots[LayerNode] = uiInstance;
                return uiInstance;
            }
            catch
            {
                if (created && uiInstance != null) DestroyImmediate(uiInstance);
                throw;
            }
        }

        protected virtual GameObject CreateOrReuseRoot(GameObject uiInstance, bool forceRebuild)
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
            else if (forceRebuild || !IsCompatibleRoot(uiInstance, rule.UIPrefab))
            {
                RebuildRootComponentsInPlace(uiInstance, rule.UIPrefab);
            }

            return uiInstance;
        }

        private static void RebuildRootComponentsInPlace(GameObject target, GameObject prefabRoot)
        {
            if (target == null || prefabRoot == null) return;

            RemoveManagedTemplateHierarchy(target, null);

            var templateObjects = CloneTemplateChildren(prefabRoot, target);
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
                    targetComponent = target.GetComponent(type);
                }

                // ComponentUtility 会把模板组件里指向"模板资产子对象"的引用(fillRect/viewport/scrollbars/textComponent 等)
                // 原样带过来 —— 后续 helper 写 rect/sprite 会污染模板资产。此处按源 Transform → 克隆 Transform 映射到目标树,
                // 目标树无对应对象时置空(由 helper 的判空/重建路径兜底)。
                if (targetComponent != null)
                {
                    RemapComponentReferences(targetComponent, prefabRoot, templateObjects);
                }
            }
        }

        private static void PreserveScrollViewContent(GameObject target)
        {
            var scrollRect = target.GetComponent<ScrollRect>();
            if (scrollRect == null || scrollRect.content == null) return;

            var content = scrollRect.content;
            while (content.childCount > 0)
            {
                content.GetChild(0).SetParent(target.transform, true);
            }
        }

        private static HashSet<GameObject> CollectReferencedTemplateRoots(GameObject target)
        {
            var result = new HashSet<GameObject>();
            var components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || !IsManagedGeneratedRootComponent(component.GetType())) continue;

                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                while (prop.NextVisible(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference || prop.name == "m_Script") continue;

                    var referenced = prop.objectReferenceValue;
                    var referencedTransform = referenced is Component referencedComponent
                        ? referencedComponent.transform
                        : (referenced is GameObject referencedGo ? referencedGo.transform : null);
                    if (referencedTransform == null || !referencedTransform.IsChildOf(target.transform)) continue;

                    var templateRoot = referencedTransform;
                    while (templateRoot.parent != null && templateRoot.parent != target.transform)
                    {
                        templateRoot = templateRoot.parent;
                    }
                    if (templateRoot.parent == target.transform)
                    {
                        result.Add(templateRoot.gameObject);
                    }
                }
            }
            return result;
        }

        private static void RemoveManagedTemplateHierarchy(GameObject target, GameObject previousPrefabRoot)
        {
            if (target == null) return;

            PreserveScrollViewContent(target);
            var roots = CollectReferencedTemplateRoots(target);
            if (previousPrefabRoot != null)
            {
                for (int i = 0; i < previousPrefabRoot.transform.childCount; i++)
                {
                    var templateChild = previousPrefabRoot.transform.GetChild(i);
                    for (int j = 0; j < target.transform.childCount; j++)
                    {
                        var candidate = target.transform.GetChild(j);
                        if (string.Equals(candidate.name, templateChild.name, StringComparison.Ordinal)
                            && HasTemplateRootSignature(candidate, templateChild))
                        {
                            roots.Add(candidate.gameObject);
                        }
                    }
                }
            }

            foreach (var root in roots)
            {
                if (root != null)
                {
                    DestroyImmediate(root);
                }
            }
        }

        private static bool HasTemplateRootSignature(Transform candidate, Transform template)
        {
            var components = template.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform) continue;
                if (candidate.GetComponent(component.GetType()) == null) return false;
            }
            return true;
        }

        private static Dictionary<Transform, Transform> CloneTemplateChildren(GameObject sourceRoot, GameObject targetRoot)
        {
            var result = new Dictionary<Transform, Transform>
            {
                [sourceRoot.transform] = targetRoot.transform,
            };

            for (int i = 0; i < sourceRoot.transform.childCount; i++)
            {
                var sourceChild = sourceRoot.transform.GetChild(i);
                var clone = Instantiate(sourceChild.gameObject, targetRoot.transform, false);
                clone.name = sourceChild.name;
                clone.transform.SetSiblingIndex(i);
                MapTemplateTransforms(sourceChild, clone.transform, result);
            }
            return result;
        }

        private static void MapTemplateTransforms(
            Transform source,
            Transform target,
            Dictionary<Transform, Transform> result)
        {
            result[source] = target;
            int count = Mathf.Min(source.childCount, target.childCount);
            for (int i = 0; i < count; i++)
            {
                MapTemplateTransforms(source.GetChild(i), target.GetChild(i), result);
            }
        }

        /// <summary>
        /// 把组件序列化引用中指向 sourceRoot 子树的对象,重映射到本轮克隆树中的对应 Transform/组件;无对应则置空。
        /// 外部引用(共享 sprite 等)保持不变。
        /// </summary>
        private static void RemapComponentReferences(
            Component component,
            GameObject sourceRoot,
            Dictionary<Transform, Transform> templateObjects)
        {
            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            while (prop.NextVisible(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (prop.name == "m_Script") continue;

                var referenced = prop.objectReferenceValue;
                if (referenced == null) continue;

                Transform refTransform = null;
                if (referenced is Component refComponent) refTransform = refComponent.transform;
                else if (referenced is GameObject refGameObject) refTransform = refGameObject.transform;
                if (refTransform == null) continue;

                var current = refTransform;
                if (current != sourceRoot.transform && !current.IsChildOf(sourceRoot.transform)) continue;//外部引用,保留

                if (!templateObjects.TryGetValue(refTransform, out var targetTransform))
                {
                    prop.objectReferenceValue = null;
                    continue;
                }

                if (referenced is GameObject)
                {
                    prop.objectReferenceValue = targetTransform.gameObject;
                }
                else
                {
                    prop.objectReferenceValue = targetTransform.GetComponent(referenced.GetType());
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
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

        /// <summary>
        /// 已有输出对象停用并保留引用身份，本轮新建的对象直接删除。
        /// </summary>
        protected static void RetireGeneratedObject(UnityEngine.Object target)
        {
            var context = GenerationContext.Current;
            if (context != null && context.ExistingOutputObjects.Contains(target))
            {
                if (target is GameObject go) { go.SetActive(false); context.RetiredObjects.Add(go); }
                else if (target is Behaviour behaviour) behaviour.enabled = false;
                else if (target is CanvasRenderer renderer) renderer.cull = true;
                return;
            }
            DestroyImmediate(target);
        }

        /// <summary>重新启用保留的滚动条，缺失时从模板创建。</summary>
        protected static Scrollbar RestoreTemplateScrollbar(GameObject uiRoot, GameObject prefabRoot, bool horizontal)
        {
            if (uiRoot == null || prefabRoot == null) return null;

            var templateScrollRect = prefabRoot.GetComponentInChildren<ScrollRect>(true);
            if (templateScrollRect == null) return null;

            var templateBar = horizontal ? templateScrollRect.horizontalScrollbar : templateScrollRect.verticalScrollbar;
            if (templateBar == null) return null;

            var retained = uiRoot.transform.Find(templateBar.name)?.GetComponent<Scrollbar>();
            if (retained != null) { retained.gameObject.SetActive(true); return retained; }

            var barGo = Instantiate(templateBar.gameObject, uiRoot.transform, false);
            barGo.name = templateBar.gameObject.name;
            barGo.transform.SetAsLastSibling();
            return barGo.GetComponent<Scrollbar>();
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
                || type == typeof(CanvasRenderer);
        }

        private static bool IsManagedGeneratedRootComponent(Type type)
        {
            return typeof(Selectable).IsAssignableFrom(type)
                || typeof(Graphic).IsAssignableFrom(type)
                || type == typeof(Mask)
                || type == typeof(ScrollRect)
                || type == typeof(ToggleGroup)
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
