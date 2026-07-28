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
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class PanelHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] private PsdLayerNode background = null;

        internal override PsdLayerNode[] GetDependencies()
        {
            RefreshBackgroundReference();
            return CalculateDependencies(background);
        }

        internal override void ResolveDependencyClaims(Dictionary<int, HashSet<int>> dependencyOwnerIds)
        {
            if (IsDependencyClaimedByOtherHelper(background, dependencyOwnerIds))
            {
                background = null;
            }
        }

        internal override void ParseAndAttachUIElements()
        {
            RefreshBackgroundReference();
        }

        protected override GameObject CreateOrReuseRoot(GameObject uiInstance)
        {
            return LayerNode.RasterizeGroupForOpacity || background != null
                ? CreateOrReuseImageRoot(uiInstance)
                : CreateOrReuseContainerRoot(uiInstance);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            if (LayerNode.RasterizeGroupForOpacity)
            {
                var image = uiRoot.GetComponent<Image>() ?? uiRoot.AddComponent<Image>();
                UGUIParser.SetRectTransform(LayerNode, image);
                UGUIParser.Instance.BindImage(LayerNode, image);
            }
            else if (background != null)
            {
                var image = uiRoot.GetComponent<Image>() ?? uiRoot.AddComponent<Image>();
                UGUIParser.SetRectTransform(LayerNode, image);
                UGUIParser.Instance.BindImage(background, image);
            }
            else
            {
                var image = uiRoot.GetComponent<Image>();
                if (image != null)
                {
                    DestroyImmediate(image);
                }

                var canvasRenderer = uiRoot.GetComponent<CanvasRenderer>();
                if (canvasRenderer != null)
                {
                    DestroyImmediate(canvasRenderer);
                }

                UGUIParser.SetRectTransform(LayerNode, uiRoot.transform);
            }
        }

        private void RefreshBackgroundReference()
        {
            background = LayerNode != null
                && !LayerNode.RasterizeGroupForOpacity
                && LayerNode.LayerType == PsdLayerType.LayerGroup
                ? FindOwnedNode(GUIType.Background)
                : null;
        }

        private GameObject CreateOrReuseContainerRoot(GameObject uiInstance)
        {
            if (uiInstance == null)
            {
                return new GameObject(LayerNode.GetGeneratedObjectName(), typeof(RectTransform));
            }

            StripUnexpectedGeneratedRootComponents(uiInstance, false);
            return uiInstance;
        }

        private GameObject CreateOrReuseImageRoot(GameObject uiInstance)
        {
            if (uiInstance == null)
            {
                return new GameObject(LayerNode.GetGeneratedObjectName(), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            }

            StripUnexpectedGeneratedRootComponents(uiInstance, true);
            if (uiInstance.GetComponent<CanvasRenderer>() == null)
            {
                uiInstance.AddComponent<CanvasRenderer>();
            }
            if (uiInstance.GetComponent<Image>() == null)
            {
                uiInstance.AddComponent<Image>();
            }
            return uiInstance;
        }

        private static void StripUnexpectedGeneratedRootComponents(GameObject target, bool withBackground)
        {
            if (target == null) return;

            var components = target.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component == null) continue;

                var type = component.GetType();
                if (type == typeof(Transform)
                    || type == typeof(RectTransform)
                    || type == typeof(UIStringKey)
                    || type == typeof(PsdGeneratedKey))
                {
                    continue;
                }

                if (withBackground && (type == typeof(CanvasRenderer) || type == typeof(Image)))
                {
                    continue;
                }

                if (IsManagedGeneratedRootComponent(type))
                {
                    DestroyImmediate(component);
                }
            }
        }

        private static bool IsManagedGeneratedRootComponent(Type type)
        {
            return typeof(Selectable).IsAssignableFrom(type)
                || typeof(Graphic).IsAssignableFrom(type)
                || type == typeof(Mask)
                || type == typeof(ScrollRect)
                || type == typeof(CanvasRenderer);
        }
    }
}
#endif
