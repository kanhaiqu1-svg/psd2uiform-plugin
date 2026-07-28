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
    internal static class AiProviderFactory
    {
        internal static IAiProvider Create(UGUIParser parser)
        {
            var config = parser != null ? parser.AiProviderConfig : new AiProviderConfig();
            return Create(config.provider);
        }

        internal static IAiProvider Create(AiProviderKind providerKind)
        {
            switch (providerKind)
            {
                case AiProviderKind.CodexCli:
                    return new CodexCliProvider();
                case AiProviderKind.ClaudeCodeCli:
                    return new ClaudeCodeCliProvider();
                case AiProviderKind.OpenCodeCli:
                    return new OpenCodeCliProvider();
                default:
                    return new CodexCliProvider();
            }
        }

        internal static IAiProvider CreateById(string providerId)
        {
            switch ((providerId ?? string.Empty).Trim())
            {
                case "codex-cli":
                case "codex":
                    return new CodexCliProvider();
                case "claude-code-cli":
                case "claude":
                    return new ClaudeCodeCliProvider();
                case "opencode-cli":
                case "opencode":
                    return new OpenCodeCliProvider();
                default:
                    return null;
            }
        }
    }
}
#endif
