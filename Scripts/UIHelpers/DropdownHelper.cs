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
    public sealed class DropdownHelper : UIHelperBase
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
            return CalculateDependencies(background, label, arrow, scrollView, toggleItem);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = LayerNode.FindSubLayerNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            label = LayerNode.FindSubLayerNode(GUIType.Dropdown_Label, GUIType.Text, GUIType.TMPText);
            arrow = LayerNode.FindSubLayerNode(GUIType.Dropdown_Arrow);
            scrollView = LayerNode.FindSubLayerNode(GUIType.ScrollView);
            toggleItem = LayerNode.FindSubLayerNode(GUIType.Toggle);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var dpd = uiRoot.GetComponent<Dropdown>();
            UGUIParser.SetRectTransform(background, dpd);
            var bgImg = dpd.targetGraphic as Image;
            UGUIParser.Instance.BindImage(background, bgImg);
            
            UGUIParser.SetTextStyle(label, dpd.captionText);
            UGUIParser.SetRectTransform(label, dpd.captionText);
            var arrowImg = dpd.transform.Find("Arrow")?.GetComponent<Image>();
            if (arrowImg != null)
            {
                UGUIParser.SetRectTransform(arrow, arrowImg);
                UGUIParser.Instance.BindImage(arrow, arrowImg);
            }
            if (scrollView != null)
            {
                var svTmp = uiRoot.GetComponentInChildren<ScrollRect>(true);
                var sViewGo = scrollView.GetComponent<ScrollViewHelper>()?.CreateUI(svTmp.gameObject);
                if (sViewGo != null)
                {
                    var sViewRect = sViewGo.GetComponent<RectTransform>();
                    UGUIParser.SetRectTransform(scrollView, sViewRect);
                }
            }
            else //若没有滚动列表元素,则隐藏默认滚动列表的元素
            {
                var svTmp = uiRoot.GetComponentInChildren<ScrollRect>(true);
                svTmp.GetComponent<Image>().enabled = false;
                if (svTmp.horizontalScrollbar != null)
                {
                    var hbarTmp = svTmp.horizontalScrollbar;
                    svTmp.horizontalScrollbar = null;
                    hbarTmp.gameObject.SetActive(false);
                }
                if (svTmp.verticalScrollbar != null)
                {
                    var vbarTmp = svTmp.verticalScrollbar;
                    svTmp.verticalScrollbar = null;
                    vbarTmp.gameObject.SetActive(false);
                }
            }
            if (toggleItem != null)
            {
                var itemTmp = dpd.itemText != null ? dpd.itemText.transform.parent : null;
                if (itemTmp != null) toggleItem.GetComponent<ToggleHelper>()?.CreateUI(itemTmp.gameObject);
            }
            var scrollRect = uiRoot.GetComponentInChildren<ScrollRect>(true);
            if (scrollRect != null)
            {
                var layerout = scrollRect.content?.GetComponent<LayoutGroup>();
                if (layerout != null) layerout.enabled = false;
                var sizeFilter = scrollRect.content?.GetComponent<ContentSizeFitter>();
                if (sizeFilter != null) sizeFilter.enabled = false;
            }
        }
    }
}
#endif
