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
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class ScrollViewHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode viewport;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode horizontalBarBG;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode horizontalBar;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode verticalBarBG;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode verticalBar;

        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(background, viewport, horizontalBarBG, horizontalBar, verticalBarBG, verticalBar);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = FindOwnedNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            viewport = FindOwnedNode(GUIType.ScrollView_Viewport);
            horizontalBarBG = FindOwnedNode(GUIType.ScrollView_HorizontalBarBG);
            horizontalBar = FindOwnedNode(GUIType.ScrollView_HorizontalBar);
            verticalBarBG = FindOwnedNode(GUIType.ScrollView_VerticalBarBG);
            verticalBar = FindOwnedNode(GUIType.ScrollView_VerticalBar);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var listView = uiRoot.GetComponent<ScrollRect>();
            UGUIParser.SetRectTransform(background != null ? background : LayerNode, listView);

            // 设置遮罩窗口:模板的 viewport 是退化基线(点锚 0 尺寸),必须按 PSD viewport 图层设置,
            // 否则遮罩窗口为 0、content 顶边退化到列表底边,嵌套内容全部不可见/错位。
            if (listView.viewport != null)
            {
                if (viewport != null)
                {
                    UGUIParser.SetRectTransform(viewport, listView.viewport);
                }
                else if (LayerNode.IsAiAuthoritative)
                {
                    UGUIParser.SetRectTransform(LayerNode, listView.viewport);
                }
                else
                {
                    // 无 viewport 图层:遮罩窗口 = 整个列表窗口(恢复为拉伸基线)
                    var vpRect = listView.viewport;
                    vpRect.anchorMin = Vector2.zero;
                    vpRect.anchorMax = Vector2.one;
                    vpRect.sizeDelta = Vector2.zero;
                    vpRect.anchoredPosition = Vector2.zero;
                }
            }

            // 滚动条节点可能已被上一轮"无对应图层"时销毁:本轮图层存在时按模板重建,保证销毁可逆
            var rulePrefab = UGUIParser.Instance?.GetRule(LayerNode.UIType)?.UIPrefab;
            if ((horizontalBarBG != null || horizontalBar != null) && listView.horizontalScrollbar == null)
            {
                var restoredBar = RestoreTemplateScrollbar(uiRoot, rulePrefab, true);
                if (restoredBar != null)
                {
                    listView.horizontalScrollbar = restoredBar;
                }
            }
            if ((verticalBarBG != null || verticalBar != null) && listView.verticalScrollbar == null)
            {
                var restoredBar = RestoreTemplateScrollbar(uiRoot, rulePrefab, false);
                if (restoredBar != null)
                {
                    listView.verticalScrollbar = restoredBar;
                }
            }

            var bgCom = listView.GetComponent<Image>();
            BindOptionalControlImage(background, bgCom);
            if (LayerNode.IsAiAuthoritative)
            {
                if ((viewport == null || !viewport.HasAiSources) && listView.viewport != null)
                {
                    var maskImage = listView.viewport.GetComponent<Image>();
                    if (maskImage != null)
                    {
                        maskImage.overrideSprite = null;
                        maskImage.sprite = null;
                        maskImage.type = Image.Type.Simple;
                        maskImage.color = Color.white;
                    }
                }
            }
            else if (bgCom != null)
            {
                if (viewport == null && listView.viewport != null)
                {
                    var maskImg = listView.viewport.GetComponent<Image>();
                    if (maskImg != null) maskImg.sprite = bgCom.sprite;
                }
            }
            if (viewport != null && listView.viewport != null && (!LayerNode.IsAiAuthoritative || viewport.HasAiSources))
            {
                var maskImg = listView.viewport.GetComponent<Image>();
                UGUIParser.Instance.BindImage(viewport, maskImg);
            }

            var hbar = listView.horizontalScrollbar;
            var vbar = listView.verticalScrollbar;

            if ((horizontalBarBG != null || horizontalBar != null) && hbar != null)
            {
                var hbarBg = hbar.GetComponent<Image>();
                BindOptionalControlImage(horizontalBarBG, hbarBg);
                UGUIParser.SetRectTransform(horizontalBarBG != null ? horizontalBarBG : horizontalBar, hbar);
                UGUIParser.SetRectTransform(background != null ? background : LayerNode, listView.content, true, true, false);
            }
            else
            {
                var hbarGo = listView.horizontalScrollbar;
                listView.horizontalScrollbar = null;
                if (hbarGo != null)
                {
                    // 无横向滚动条图层:删除模板滚动条节点,保证 UI 纯净
                    RetireGeneratedObject(hbarGo.gameObject);
                }
            }
            if ((verticalBarBG != null || verticalBar != null) && vbar != null)
            {
                var vbarBg = vbar.GetComponent<Image>();
                BindOptionalControlImage(verticalBarBG, vbarBg);
                UGUIParser.SetRectTransform(verticalBarBG != null ? verticalBarBG : verticalBar, vbar);
                UGUIParser.SetRectTransform(background != null ? background : LayerNode, listView.content, true, false, true);
            }
            else
            {
                var vbarGo = listView.verticalScrollbar;
                listView.verticalScrollbar = null;
                if (vbarGo != null)
                {
                    // 无纵向滚动条图层:删除模板滚动条节点,保证 UI 纯净
                    RetireGeneratedObject(vbarGo.gameObject);
                }
            }

            if (hbar != null)
            {
                var hbarHandle = hbar.targetGraphic as Image;
                BindOptionalControlImage(horizontalBar, hbarHandle);
                // Scrollbar 只用 movement 轴的 anchor 定手柄长度，交叉轴归我们：横向条的 PSD 厚度＝高度。
                UGUIParser.SetRectTransform(horizontalBar, hbarHandle, false, false, true);
            }
            if (vbar != null)
            {
                var vbarHandle = vbar.targetGraphic as Image;
                BindOptionalControlImage(verticalBar, vbarHandle);
                // 纵向条反之取宽度；长度一律不写，否则会和运行时拖拽算出的 anchor 结果打架。
                UGUIParser.SetRectTransform(verticalBar, vbarHandle, false, true, false);
            }
        }

        /// <summary>
        /// 生成树就绪后:把 ScrollView 组的嵌套内容(除 viewport/滚动条骨架外的全部直接子节点)
        /// 重挂到 listView.content 下,并把 content 中保留的用户节点纳入最终滚动范围。
        /// 位置还原策略(确定性,不依赖 worldPositionStays 转换与 content 当时的 rect 状态):
        ///   1. 捕获最终子节点的世界位置与实际尺寸,再设置 content 顶部锚定基线;
        ///   2. 重挂后统一为左上点锚,content 尺寸和原点确定后恢复全部子节点世界位置;
        ///   3. content 尺寸 = viewport 与最终保留子节点的联合包围盒,空内容时回落到 viewport 尺寸。
        /// </summary>
        internal override void OnGeneratedHierarchyReady(GameObject uiRoot)
        {
            base.OnGeneratedHierarchyReady(uiRoot);
            if (uiRoot == null) return;

            var scrollRect = uiRoot.GetComponent<ScrollRect>();
            if (scrollRect == null || scrollRect.content == null) return;

            var chrome = new System.Collections.Generic.HashSet<Transform>();
            if (scrollRect.viewport != null)
            {
                chrome.Add(scrollRect.viewport.transform);
            }
            chrome.Add(scrollRect.content);
            if (scrollRect.horizontalScrollbar != null)
            {
                chrome.Add(scrollRect.horizontalScrollbar.transform);
            }
            if (scrollRect.verticalScrollbar != null)
            {
                chrome.Add(scrollRect.verticalScrollbar.transform);
            }

            var content = scrollRect.content;

            // content 内已有的用户节点保持在前；待孤儿节点稍后会移出生成树,不参与最终范围。
            var contentChildren = new System.Collections.Generic.List<Transform>(
                content.childCount + uiRoot.transform.childCount);
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (!IsScheduledForOrphan(child))
                {
                    contentChildren.Add(child);
                }
            }

            // 再收集根下的 PSD 业务节点,保证追加顺序与原相对顺序一致。
            for (int i = 0; i < uiRoot.transform.childCount; i++)
            {
                var child = uiRoot.transform.GetChild(i);
                if (IsChrome(child, chrome) || IsScheduledForOrphan(child)) continue;
                contentChildren.Add(child);
            }

            var childWorldPositions = new Vector3[contentChildren.Count];
            var childLocalSizes = new Vector2[contentChildren.Count];
            for (int i = 0; i < contentChildren.Count; i++)
            {
                var child = contentChildren[i];
                childWorldPositions[i] = child.position;
                if (child is RectTransform rectTransform)
                {
                    childLocalSizes[i] = rectTransform.rect.size;
                }
            }

            // 1) content 基线:顶部锚定、归零偏移、宽度拉伸随 viewport
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            // 2) 迁移并统一为左上点锚；实际尺寸和世界位置均保持不变。
            for (int i = 0; i < contentChildren.Count; i++)
            {
                var child = contentChildren[i];
                var rectTransform = child as RectTransform;
                if (child.parent != content)
                {
                    child.SetParent(content, false);
                }
                if (rectTransform != null)
                {
                    rectTransform.anchorMin = new Vector2(0f, 1f);
                    rectTransform.anchorMax = new Vector2(0f, 1f);
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, childLocalSizes[i].x);
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, childLocalSizes[i].y);
                }
                child.position = childWorldPositions[i];
            }

            // 3) content 尺寸 = viewport 与最终子节点联合包围盒。
            float viewportWidth = scrollRect.viewport != null ? scrollRect.viewport.rect.width : content.rect.width;
            float viewportHeight = scrollRect.viewport != null ? scrollRect.viewport.rect.height : 0f;
            float minX = 0f;
            float maxX = viewportWidth;
            float minY = -viewportHeight;
            float maxY = 0f;
            var childCorners = new Vector3[4];
            for (int i = 0; i < contentChildren.Count; i++)
            {
                var rectTransform = contentChildren[i] as RectTransform;
                if (rectTransform == null) continue;
                rectTransform.GetWorldCorners(childCorners);
                for (int j = 0; j < childCorners.Length; j++)
                {
                    var localCorner = content.InverseTransformPoint(childCorners[j]);
                    minX = Mathf.Min(minX, localCorner.x);
                    maxX = Mathf.Max(maxX, localCorner.x);
                    minY = Mathf.Min(minY, localCorner.y);
                    maxY = Mathf.Max(maxY, localCorner.y);
                }
            }

            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxX - minX);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, maxY - minY);
            content.anchoredPosition = new Vector2(minX, maxY);

            // content 原点与尺寸变化后统一恢复世界位置；点锚保证后续滚动尺寸变化保持稳定。
            for (int i = 0; i < contentChildren.Count; i++)
            {
                contentChildren[i].position = childWorldPositions[i];
            }
        }

        private static bool IsScheduledForOrphan(Transform child)
        {
            var context = GenerationContext.Current;
            if (context == null) return false;

            if (context.RetiredObjects.Contains(child.gameObject)) return true;
            var nodesToOrphan = context.NodesToOrphan;
            for (int i = 0; i < nodesToOrphan.Count; i++)
            {
                var orphan = nodesToOrphan[i];
                if (orphan == null) continue;

                var orphanTransform = orphan.transform;
                if (child == orphanTransform || child.IsChildOf(orphanTransform))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsChrome(Transform child, System.Collections.Generic.HashSet<Transform> chrome)
        {
            var current = child;
            while (current != null)
            {
                if (chrome.Contains(current)) return true;
                current = current.parent;
            }
            return false;
        }
    }
}
#endif
