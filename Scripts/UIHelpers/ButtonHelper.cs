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
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class ButtonHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background = null;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode text = null;
        [Header("Sprite Swap:")]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode highlight = null;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode press = null;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode select = null;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode disable = null;

        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(background, text, highlight, press, select, disable);
        }

        internal override void ParseAndAttachUIElements()
        {
            if (LayerNode.LayerType == PsdLayerType.LayerGroup)
            {
            background = FindOwnedNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            text = FindOwnedNode(GUIType.Button_Text, GUIType.Text);
            highlight = FindOwnedNode(GUIType.Button_Highlight);
            press = FindOwnedNode(GUIType.Button_Press);
            select = FindOwnedNode(GUIType.Button_Select);
            disable = FindOwnedNode(GUIType.Button_Disable);
            }
            else
            {
                background = LayerNode;
            }
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var button = uiRoot.GetComponent<Button>();
            var btImg = button.GetComponent<Image>();
            var rectSource = background != null ? background : LayerNode;
            UGUIParser.Instance.BindImage(background, btImg);
            UGUIParser.SetRectTransform(rectSource, button);
            var btText = FindTemplateText(uiRoot);
            if (text == null)
            {
                if (btText != null)
                {
                    DestroyImmediate(btText.gameObject);
                }
            }
            else
            {
                btText = btText ?? CreateTemplateText(uiRoot);
                UGUIParser.SetTextStyle(text, btText);
                UGUIParser.SetTextRectTransform(text, btText);
                UGUIParser.SetTextRotation(text, btText);
            }
            bool useSpriteSwap = highlight != null || press != null || select != null || disable != null;
            button.transition = useSpriteSwap ? Selectable.Transition.SpriteSwap : Selectable.Transition.ColorTint;
            if (button.transition == Selectable.Transition.SpriteSwap)
            {
                bool useSliceSp = btImg.type == Image.Type.Sliced || btImg.type == Image.Type.Tiled;
                var spState = new SpriteState();
                spState.highlightedSprite = UGUIParser.LayerNode2Sprite(highlight, useSliceSp);
                spState.pressedSprite = UGUIParser.LayerNode2Sprite(press, useSliceSp);
                spState.selectedSprite = UGUIParser.LayerNode2Sprite(select, useSliceSp);
                spState.disabledSprite = UGUIParser.LayerNode2Sprite(disable, useSliceSp);
                button.spriteState = spState;
            }
        }

        private Text CreateTemplateText(GameObject uiRoot)
        {
            var prefab = UGUIParser.Instance?.GetRule(LayerNode.UIType)?.UIPrefab;
            var prefabText = prefab != null ? prefab.GetComponentInChildren<Text>(true) : null;
            if (prefabText == null) return null;

            var textGo = Instantiate(prefabText.gameObject, uiRoot.transform, false);
            textGo.name = prefabText.gameObject.name;
            textGo.transform.SetAsLastSibling();
            return textGo.GetComponent<Text>();
        }

        private static Text FindTemplateText(GameObject uiRoot)
        {
            if (uiRoot == null) return null;

            var root = uiRoot.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null || child.GetComponent<PsdGeneratedKey>() != null) continue;

                var text = child.GetComponent<Text>();
                if (text != null) return text;
            }
            return null;
        }
    }
}
#endif
