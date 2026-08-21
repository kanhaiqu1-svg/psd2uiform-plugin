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
using System;

namespace UGF.EditorTools.Psd2UGUI
{
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal enum AiProviderKind
    {
        CodexCli = 1,
        ClaudeCodeCli = 2,
        OpenCodeCli = 3
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiProviderConfig
    {
        public AiProviderKind provider = AiProviderKind.CodexCli;
        public bool showCliWindow = true;
    }
}
#endif
