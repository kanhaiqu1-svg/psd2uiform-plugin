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

#if UNITY_EDITOR
/*
插件获取:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class PsdGeneratedKey : MonoBehaviour
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField]
        [HideInInspector]
        private string m_Key = null;

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField]
        [HideInInspector]
        private string m_TypeKey = null;

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField]
        [HideInInspector]
        private bool m_IsContainer = false;

        internal string Key
        {
            get => m_Key ?? string.Empty;
            set => m_Key = value;
        }

        internal string TypeKey
        {
            get => m_TypeKey ?? string.Empty;
            set => m_TypeKey = value;
        }

        internal bool IsContainer
        {
            get => m_IsContainer;
            set => m_IsContainer = value;
        }

        internal string CompositeKey => $"{(m_IsContainer ? "C" : "N")}:{TypeKey}:{Key}";
    }
}
#endif
