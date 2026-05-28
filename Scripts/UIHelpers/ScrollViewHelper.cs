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
            background = LayerNode.FindSubLayerNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            viewport = LayerNode.FindSubLayerNode(GUIType.ScrollView_Viewport, GUIType.Mask);
            horizontalBarBG = LayerNode.FindSubLayerNode(GUIType.ScrollView_HorizontalBarBG);
            horizontalBar = LayerNode.FindSubLayerNode(GUIType.ScrollView_HorizontalBar);
            verticalBarBG = LayerNode.FindSubLayerNode(GUIType.ScrollView_VerticalBarBG);
            verticalBar = LayerNode.FindSubLayerNode(GUIType.ScrollView_VerticalBar);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var listView = uiRoot.GetComponent<ScrollRect>();
            UGUIParser.SetRectTransform(background, listView);
            
            var bgCom = listView.GetComponent<Image>();
            if (bgCom != null)
            {
                UGUIParser.Instance.BindImage(background, bgCom);
                if (viewport == null)
                {
                    var maskImg = listView.viewport.GetComponent<Image>();
                    maskImg.sprite = bgCom.sprite;
                }
            }
            if (viewport != null)
            {
                var maskImg = listView.viewport.GetComponent<Image>();
                UGUIParser.Instance.BindImage(viewport, maskImg);
            }

            var hbar = listView.horizontalScrollbar;
            var vbar = listView.verticalScrollbar;

            if (horizontalBarBG != null && hbar != null)
            {
                var hbarBg = hbar.GetComponent<Image>();
                UGUIParser.Instance.BindImage(horizontalBarBG, hbarBg);
                UGUIParser.SetRectTransform(horizontalBarBG, hbarBg);
                UGUIParser.SetRectTransform(background, listView.content, true, true, false);
            }
            else
            {
                var hbarGo = listView.horizontalScrollbar;
                listView.horizontalScrollbar = null;
                if (hbarGo != null)
                {
                    hbarGo.gameObject.SetActive(false);
                }
            }
            if (verticalBarBG != null && vbar != null)
            {
                var vbarBg = vbar.GetComponent<Image>();
                UGUIParser.Instance.BindImage(verticalBarBG, vbarBg);
                UGUIParser.SetRectTransform(verticalBarBG, vbarBg);
                UGUIParser.SetRectTransform(background, listView.content, true, false, true);
            }
            else
            {
                var vbarGo = listView.verticalScrollbar;
                listView.verticalScrollbar = null;
                if (vbarGo != null)
                {
                    vbarGo.gameObject.SetActive(false);
                }
            }

            if (horizontalBar != null && hbar != null)
            {
                var hbarHandle = hbar.targetGraphic as Image;
                UGUIParser.Instance.BindImage(horizontalBar, hbarHandle);
                UGUIParser.SetRectTransform(horizontalBar, hbarHandle, false, false, false);
            }
            if (verticalBar != null && vbar != null)
            {
                var vbarHandle = vbar.targetGraphic as Image;
                UGUIParser.Instance.BindImage(verticalBar, vbarHandle);
                UGUIParser.SetRectTransform(verticalBar, vbarHandle, false, false, false);
            }
        }
    }
}
#endif
