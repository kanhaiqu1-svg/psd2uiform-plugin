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
using System.Threading;

namespace UGF.EditorTools.Psd2UGUI
{
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiProviderCapabilities
    {
        public bool SupportsImages;
        public bool SupportsStrictJson;
        public bool UsesVisibleCliExecution;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisRequest
    {
        public string StageName;
        public string StageDisplayName;
        public AiResultDocumentKind ResultDocumentKind;
        public string AnalysisPackageJsonPath;
        public string PromptTemplatePath;
        public AiPromptTag[] PromptTags;
        public string OutputJsonPath;
        public string OutputTempJsonPath;
        public string OutputRawTextPath;
        public string OutputCompletedPath;
        public string StreamOutputPath;
        public string VisibleCliCompletionPath;
        public string WorkingDirectory;
        public string[] ImageInputPaths;
        public bool AllowVisibleCliExecution;
        public bool RequireExplicitOutputJsonFile;
        public bool DisableOutputRecovery;
        public CancellationToken CancellationToken;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisResult
    {
        public bool Success;
        public string ProviderId;
        public string RawOutput;
        public string PatchJsonPath;
        public string ErrorMessage;
    }

    internal interface IAiProvider
    {
        string ProviderId { get; }
        AiProviderCapabilities Capabilities { get; }

        void ExecuteJob(AiJobContext context, AiAnalysisRequest request);
    }
}
#endif
