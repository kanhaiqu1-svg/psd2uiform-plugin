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
#if UNITY_6000_3_OR_NEWER
using PsdObjectId = UnityEngine.EntityId;
#else
using PsdObjectId = System.Int32;
#endif
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class ToggleGroupHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] private PsdLayerNode background = null;
        // 依赖裁决释放标记:让出所有权后,RefreshBackgroundReference 不得再自动找回背景节点
        private bool backgroundClaimReleased = false;

        internal override PsdLayerNode[] GetDependencies()
        {
            RefreshBackgroundReference();
            return CalculateDependencies(background);
        }

        internal override void ResolveDependencyClaims(Dictionary<PsdObjectId, HashSet<PsdObjectId>> dependencyOwnerIds)
        {
            if (IsDependencyClaimedByOtherHelper(background, dependencyOwnerIds))
            {
                background = null;
                backgroundClaimReleased = true;
            }
        }

        internal override void ParseAndAttachUIElements()
        {
            backgroundClaimReleased = false;
            RefreshBackgroundReference();
        }

        protected override GameObject CreateOrReuseRoot(GameObject uiInstance, bool forceRebuild)
        {
            return background != null
                ? CreateOrReuseImageRoot(uiInstance)
                : CreateOrReuseContainerRoot(uiInstance);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            if (background != null)
            {
                var image = uiRoot.GetComponent<Image>() ?? uiRoot.AddComponent<Image>();
                image.enabled = true;
                var canvasRenderer = uiRoot.GetComponent<CanvasRenderer>();
                if (canvasRenderer != null) canvasRenderer.cull = false;
                UGUIParser.SetRectTransform(background, image);
                BindOptionalControlImage(background, image);
            }
            else
            {
                var image = uiRoot.GetComponent<Image>();
                if (image != null)
                {
                    RetireGeneratedObject(image);
                }

                var canvasRenderer = uiRoot.GetComponent<CanvasRenderer>();
                if (canvasRenderer != null)
                {
                    RetireGeneratedObject(canvasRenderer);
                }

                UGUIParser.SetRectTransform(LayerNode, uiRoot.transform);
            }

            if (uiRoot.GetComponent<ToggleGroup>() == null)
            {
                uiRoot.AddComponent<ToggleGroup>();
            }
        }

        private void RefreshBackgroundReference()
        {
            if (backgroundClaimReleased) return;//已裁决让出所有权,不再自动找回

            background = LayerNode != null && LayerNode.LayerType == PsdLayerType.LayerGroup
                ? LayerNode.FindOwnedContainerBackground()
                : null;
        }

        private GameObject CreateOrReuseContainerRoot(GameObject uiInstance)
        {
            if (uiInstance == null)
            {
                return new GameObject(LayerNode.GetGeneratedObjectName(), typeof(RectTransform), typeof(ToggleGroup));
            }

            StripUnexpectedGeneratedRootComponents(uiInstance, false);
            EnsureToggleGroup(uiInstance);
            return uiInstance;
        }

        private GameObject CreateOrReuseImageRoot(GameObject uiInstance)
        {
            if (uiInstance == null)
            {
                return new GameObject(LayerNode.GetGeneratedObjectName(), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ToggleGroup));
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
            EnsureToggleGroup(uiInstance);
            return uiInstance;
        }

        private static void EnsureToggleGroup(GameObject target)
        {
            if (target != null && target.GetComponent<ToggleGroup>() == null)
            {
                target.AddComponent<ToggleGroup>();
            }
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
                    || type == typeof(ToggleGroup))
                {
                    continue;
                }

                if (withBackground && (type == typeof(CanvasRenderer) || type == typeof(Image)))
                {
                    continue;
                }

                if (IsManagedGeneratedRootComponent(type))
                {
                    RetireGeneratedObject(component);
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
