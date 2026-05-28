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
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    public abstract class UIHelperBase : MonoBehaviour
    {
        internal PsdLayerNode LayerNode => this.GetComponent<PsdLayerNode>();

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

        /// <summary>
        /// 把UI实例进行UI元素初始化
        /// </summary>
        protected abstract void InitUIElements(GameObject uiRoot);

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

        internal GameObject CreateUI(GameObject uiInstance = null)
        {
            if (!this.LayerNode.IsMainUIType || LayerNode.UIType == GUIType.Null) return null;

            var rule = UGUIParser.Instance.GetRule(this.LayerNode.UIType);
            if (rule == null || rule.UIPrefab == null)
            {
                Debug.LogWarning($"创建UI类型{LayerNode.UIType}失败:Rule配置项不存在或UIPrefab为空");
                return null;
            }

            if (uiInstance != null && !IsCompatibleRoot(uiInstance, rule.UIPrefab))
            {
                DestroyImmediate(uiInstance);
                uiInstance = null;
            }

            if (uiInstance == null)
            {
                uiInstance = GameObject.Instantiate(rule.UIPrefab, Vector3.zero, Quaternion.identity);
            }

            if (LayerNode.IsMainUIType)
            {
                uiInstance.name = LayerNode.GetGeneratedObjectName();
            }

            InitUIElements(uiInstance);
            return uiInstance;
        }

        private static bool IsCompatibleRoot(GameObject existing, GameObject prefabRoot)
        {
            var expectedType = GetPrimaryRootComponentType(prefabRoot);
            var existingType = GetPrimaryRootComponentType(existing);
            if (expectedType == null || existingType == null)
            {
                return false;
            }

            return expectedType == existingType;
        }

        private static Type GetPrimaryRootComponentType(GameObject target)
        {
            if (target == null) return null;

            var components = target.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;

                var type = component.GetType();
                if (type == typeof(Transform)
                    || type == typeof(RectTransform)
                    || type == typeof(CanvasRenderer)
                    || type == typeof(UIStringKey)
                    || type == typeof(PsdGeneratedKey))
                {
                    continue;
                }

                return type;
            }

            return null;
        }
    }
}
#endif
