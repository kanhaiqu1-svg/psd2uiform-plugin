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

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class ToggleHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode checkmark;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode label;

        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(background, checkmark, label);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = FindOwnedNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            checkmark = FindOwnedNode(GUIType.Toggle_Checkmark);
            label = FindOwnedNode(GUIType.Toggle_Label, GUIType.Text);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var tgCom = uiRoot.GetComponent<UnityEngine.UI.Toggle>();
            UGUIParser.SetRectTransform(LayerNode, tgCom);

            var bgCom = tgCom.targetGraphic as UnityEngine.UI.Image;
            if (bgCom != null)
            {
                UGUIParser.SetRectTransform(background != null ? background : LayerNode, bgCom);
                BindOptionalControlImage(background, bgCom);
                if (background == null) bgCom.raycastTarget = true;
            }

            var markCom = tgCom.graphic as UnityEngine.UI.Image;
            if (markCom != null)
            {
                UGUIParser.SetRectTransform(checkmark, markCom);
                BindOptionalControlImage(checkmark, markCom);
            }

            var textCom = tgCom.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>()
                ?? tgCom.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (textCom != null && label == null)
            {
                // 无标签图层:删除模板标签节点,保证 UI 纯净、无冗余节点
                RetireGeneratedObject(textCom.gameObject);
                textCom = null;
            }
            if (textCom == null && label != null)
            {
                // 标签节点曾被删除(上一轮无标签图层),按模板重建
                textCom = CreateTemplateLabel(uiRoot);
            }
            if (label != null && textCom != null) textCom.gameObject.SetActive(true);
            UGUIParser.SetTextStyle(label, textCom);
            UGUIParser.SetTextRectTransform(label, textCom);
            UGUIParser.SetTextRotation(label, textCom);
            if (LayerNode.IsAiAuthoritative)
                tgCom.SetIsOnWithoutNotify(checkmark != null && checkmark.AiSourceVisible);
        }

        private UnityEngine.UI.Text CreateTemplateLabel(GameObject uiRoot)
        {
            var prefab = UGUIParser.Instance?.GetRule(LayerNode.UIType)?.UIPrefab;
            var prefabLabel = prefab != null ? prefab.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
            if (prefabLabel == null) return null;

            var labelGo = Instantiate(prefabLabel.gameObject, uiRoot.transform, false);
            labelGo.name = prefabLabel.gameObject.name;
            labelGo.transform.SetAsLastSibling();
            return labelGo.GetComponent<UnityEngine.UI.Text>();
        }

        internal override void OnUIParented(GameObject uiRoot)
        {
            var toggle = uiRoot != null ? uiRoot.GetComponent<UnityEngine.UI.Toggle>() : null;
            if (toggle == null) return;

            toggle.group = FindParentComponent<UnityEngine.UI.ToggleGroup>(uiRoot.transform.parent);
        }
    }
}
#endif
