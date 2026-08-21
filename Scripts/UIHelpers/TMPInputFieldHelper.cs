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
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class TMPInputFieldHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode placeholder;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode text;
        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(background, placeholder, text);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = FindOwnedNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            placeholder = FindOwnedNode(GUIType.InputField_Placeholder);
            text = FindOwnedNode(GUIType.InputField_Text, GUIType.Text);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var input = uiRoot.GetComponent<TMP_InputField>();
            if (input == null)
            {
                Debug.LogWarning($"TMPInputField缺少TMP_InputField组件, 已跳过初始化: {uiRoot.name}");
                return;
            }

            UGUIParser.SetRectTransform(background, input);

            var bgImage = input.targetGraphic as Image ?? uiRoot.GetComponent<Image>();
            if (bgImage != null)
            {
                input.targetGraphic = bgImage;
                UGUIParser.Instance.BindImage(background, bgImage);
            }

            var placeholderText = input.placeholder as TextMeshProUGUI;
            if (placeholderText == null)
            {
                placeholderText = uiRoot.transform.Find("Text Area/Placeholder")?.GetComponent<TextMeshProUGUI>();
                if (placeholderText != null)
                {
                    input.placeholder = placeholderText;
                }
            }

            var contentText = input.textComponent as TextMeshProUGUI;
            if (contentText == null)
            {
                contentText = uiRoot.transform.Find("Text Area/Text")?.GetComponent<TextMeshProUGUI>();
                if (contentText != null)
                {
                    input.textComponent = contentText;
                }
            }

            UGUIParser.SetTextStyle(placeholder, placeholderText);
            UGUIParser.SetTextStyle(text, contentText);
            var inputBounds = background != null ? background : LayerNode;
            UGUIParser.SetInputFieldTextRectTransform(inputBounds, placeholder, placeholderText);
            UGUIParser.SetInputFieldTextRectTransform(inputBounds, text, contentText);

            if (text != null && text.ParseTextLayerInfo(out var textInfo))
            {
                input.text = textInfo.Text ?? string.Empty;
                input.ForceLabelUpdate();
            }
        }
    }
}
#endif
