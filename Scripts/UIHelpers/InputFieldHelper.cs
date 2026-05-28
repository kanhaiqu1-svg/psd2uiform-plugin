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
            background = LayerNode.FindSubLayerNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            placeholder = LayerNode.FindSubLayerNode(GUIType.InputField_Placeholder);
            text = LayerNode.FindSubLayerNode(GUIType.InputField_Text, GUIType.Text);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var input = uiRoot.GetComponent<InputField>();
            UGUIParser.SetRectTransform(background, input);

            var bgImage = input.targetGraphic as Image;
            UGUIParser.Instance.BindImage(background, bgImage);
            UGUIParser.SetRectTransform(placeholder, input.placeholder);
            UGUIParser.SetRectTransform(text, input.textComponent);
            UGUIParser.SetTextStyle(placeholder, input.placeholder as Text);
            var textInfo = UGUIParser.SetTextStyle(text, input.textComponent);
            input.text = textInfo.Text;
        }
    }
}
#endif
