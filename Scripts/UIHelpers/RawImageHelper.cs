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
    public sealed class RawImageHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode rawImage;

        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(rawImage);
        }

        internal override void ParseAndAttachUIElements()
        {
            rawImage = LayerNode;
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var imgCom = uiRoot.GetComponentInChildren<UnityEngine.UI.RawImage>();
            UGUIParser.SetRectTransform(rawImage, imgCom);
            imgCom.texture = UGUIParser.LayerNode2Texture(rawImage);
        }
    }
}
#endif
