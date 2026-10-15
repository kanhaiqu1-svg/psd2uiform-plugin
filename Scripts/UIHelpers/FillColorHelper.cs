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
    public sealed class FillColorHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode fillColor;

        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(fillColor);
        }

        internal override void ParseAndAttachUIElements()
        {
            if (LayerNode.UIType != GUIType.FillColor && LayerNode.LayerType != PsdLayerType.FillLayer)
            {
                if (LayerNode.IsAiAuthoritative) throw new System.InvalidOperationException("AI 声明与 Helper 输入不匹配；保留类型和素材绑定。");
                if (ParsingFromOnEnable)
                {
                    DeferTypeFallback(UGUIParser.Instance.DefaultImage);
                    return;
                }
                LayerNode.SetUIType(UGUIParser.Instance.DefaultImage);
                return;
            }
            fillColor = LayerNode;
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var imgCom = uiRoot.GetComponentInChildren<UnityEngine.UI.RawImage>();
            if (imgCom == null)
            {
                Debug.LogWarning($"FillColor 模板缺少 RawImage，已跳过填充色绑定: {uiRoot.name}");
                return;
            }
            UGUIParser.SetRectTransform(fillColor, imgCom);
            if (fillColor.IsAiAuthoritative) PsdGeneratedDrawOrder.Register(fillColor, imgCom);
            imgCom.color = UGUIParser.LayerNode2Color(fillColor, imgCom.color);
        }
    }
}
#endif
