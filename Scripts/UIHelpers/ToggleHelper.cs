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
                UGUIParser.SetRectTransform(background, bgCom);
                UGUIParser.Instance.BindImage(background, bgCom);
            }

            var markCom = tgCom.graphic as UnityEngine.UI.Image;
            if (markCom != null)
            {
                UGUIParser.SetRectTransform(checkmark, markCom);
                UGUIParser.Instance.BindImage(checkmark, markCom);
            }

            var textCom = tgCom.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
            if (textCom != null)
            {
                textCom.gameObject.SetActive(label != null);
            }
            UGUIParser.SetTextStyle(label, textCom);
            UGUIParser.SetTextRectTransform(label, textCom);
            UGUIParser.SetTextRotation(label, textCom);
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
