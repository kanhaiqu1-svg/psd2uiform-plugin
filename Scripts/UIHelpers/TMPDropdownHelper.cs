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
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class TMPDropdownHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode label;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode arrow;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode scrollView;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode toggleItem;
        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies( background, label, arrow, scrollView, toggleItem);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = FindOwnedNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            label = FindOwnedNode(GUIType.Dropdown_Label, GUIType.Text);
            arrow = FindOwnedNode(GUIType.Dropdown_Arrow);
            scrollView = FindOwnedNode(GUIType.ScrollView);
            toggleItem = FindOwnedNode(GUIType.TMPToggle, GUIType.Toggle);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var dpd = uiRoot.GetComponent<TMP_Dropdown>();
            UGUIParser.SetRectTransform(background != null ? background : LayerNode, dpd);
            var bgImg = dpd.targetGraphic as Image;
            if (bgImg != null && background != null)
            {
                bgImg.color = Color.white;
                UGUIParser.Instance.BindImage(background, bgImg);
            }
            else if (bgImg != null)
            {
                // 没有背景图层时不能留着模板底图和模板尺寸。
                bgImg.sprite = null;
                bgImg.color = Color.clear;
            }

            var captionText = dpd.captionText as TextMeshProUGUI;
            UGUIParser.SetTextStyle(label, captionText);
            if (captionText != null)
            {
                captionText.verticalAlignment = VerticalAlignmentOptions.Geometry;
            }
            UGUIParser.SetTextRectTransform(label, dpd.captionText);
            UGUIParser.SetTextRotation(label, dpd.captionText);
            var arrowImg = dpd.transform.Find("Arrow")?.GetComponent<Image>();
            if (arrowImg != null)
            {
                arrowImg.gameObject.SetActive(arrow != null);
                UGUIParser.SetRectTransform(arrow, arrowImg);
                UGUIParser.Instance.BindImage(arrow, arrowImg);
            }
            if (scrollView != null)
            {
                var svTmp = uiRoot.GetComponentInChildren<ScrollRect>(true);
                if (svTmp != null)
                {
                    // 列表 Image 可能被上一轮"无滚动列表图层"时禁用,本轮恢复可见
                    var svImg = svTmp.GetComponent<Image>();
                    if (svImg != null)
                    {
                        svImg.enabled = true;
                    }
                    var sViewGo = scrollView.GetComponent<ScrollViewHelper>()?.CreateUI(svTmp.transform.parent, svTmp.gameObject);
                    if (sViewGo != null)
                    {
                        var sViewRect = sViewGo.GetComponent<RectTransform>();
                        UGUIParser.SetRectTransform(scrollView, sViewRect);
                    }
                }
            }
            else //若没有滚动列表元素,则隐藏默认滚动列表的元素
            {
                var svTmp = uiRoot.GetComponentInChildren<ScrollRect>(true);
                var svImg = svTmp != null ? svTmp.GetComponent<Image>() : null;
                if (svImg != null) svImg.enabled = false;
                if (svTmp != null)
                {
                    if (svTmp.horizontalScrollbar != null)
                    {
                        var hbarTmp = svTmp.horizontalScrollbar;
                        svTmp.horizontalScrollbar = null;
                        // 无滚动列表图层:删除模板滚动条节点,保证 UI 纯净
                        RetireGeneratedObject(hbarTmp.gameObject);
                    }
                    if (svTmp.verticalScrollbar != null)
                    {
                        var vbarTmp = svTmp.verticalScrollbar;
                        svTmp.verticalScrollbar = null;
                        RetireGeneratedObject(vbarTmp.gameObject);
                    }
                }
            }
            if (toggleItem != null)
            {
                var itemTmp = dpd.itemText != null ? dpd.itemText.transform.parent : null;
                if (itemTmp != null) toggleItem.GetComponent<UIHelperBase>()?.CreateUI(itemTmp.parent, itemTmp.gameObject);
            }
            var scrollRect = uiRoot.GetComponentInChildren<ScrollRect>(true);
            if (scrollRect != null)
            {
                // 生成列表为固定摆放:删除模板自带的布局/自适应组件,保证 UI 纯净
                var layerout = scrollRect.content?.GetComponent<LayoutGroup>();
                if (layerout != null) RetireGeneratedObject(layerout);
                var sizeFilter = scrollRect.content?.GetComponent<ContentSizeFitter>();
                if (sizeFilter != null) RetireGeneratedObject(sizeFilter);
            }

            var options = new List<TMP_Dropdown.OptionData>();
            // 只数真正挂了 Helper 的条目（与 DropdownHelper 同口径），但 Toggle/TMPToggle 都认。
            var scope = scrollView != null ? (Component)scrollView : this;
            var itemHelpers = scope.GetComponentsInChildren<UIHelperBase>(true);
            for (int i = 0; i < itemHelpers.Length; i++)
            {
                var itemNode = itemHelpers[i] != null ? itemHelpers[i].LayerNode : null;
                if (itemNode == null) continue;
                if (itemNode.UIType != GUIType.Toggle && itemNode.UIType != GUIType.TMPToggle) continue;
                // AI 只读取明确的 Toggle_Label；普通模式允许已有的文本命名规则。
                var itemLabel = itemNode.FindOwnedSubLayerNode(GUIType.Toggle_Label, GUIType.TMPText, GUIType.Text);
                if (itemLabel != null && itemLabel.ParseTextLayerInfo(out var itemInfo))
                    options.Add(new TMP_Dropdown.OptionData(itemInfo.Text));
            }
            string caption = label != null && label.ParseTextLayerInfo(out var captionInfo) ? captionInfo.Text : string.Empty;
            int selected = options.FindIndex(option => option.text == caption);
            if (selected < 0)
            {
                selected = 0;
                options.Insert(0, new TMP_Dropdown.OptionData(caption));
            }
            dpd.options = options;
            dpd.SetValueWithoutNotify(selected);
            dpd.RefreshShownValue();
            if (dpd.template != null) dpd.template.gameObject.SetActive(false);
        }

    }
}
#endif
