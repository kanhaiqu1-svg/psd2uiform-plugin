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
    public sealed class InputFieldHelper : UIHelperBase
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
            var input = uiRoot.GetComponent<InputField>();
            UGUIParser.SetRectTransform(background != null ? background : LayerNode, input);

            var bgImage = input.targetGraphic as Image;
            BindOptionalControlImage(background, bgImage);
            UGUIParser.SetTextStyle(placeholder, input.placeholder as Text);
            // A PSD may only draw the hint. The editable Text is still required at runtime.
            var textStyleSource = text != null ? text : placeholder;
            if (textStyleSource != null && input.textComponent != null) UGUIParser.SetTextStyle(textStyleSource, input.textComponent);
            if (input.textComponent != null) input.textComponent.gameObject.SetActive(true);
            var inputBounds = background != null ? background : LayerNode;
            UGUIParser.SetInputFieldTextRectTransform(inputBounds, placeholder, input.placeholder);
            UGUIParser.SetInputFieldTextRectTransform(inputBounds, textStyleSource, input.textComponent);
            input.text = text != null && text.ParseTextLayerInfo(out var textInfo) ? textInfo.Text : string.Empty;
            input.ForceLabelUpdate();
        }
    }
}
#endif
