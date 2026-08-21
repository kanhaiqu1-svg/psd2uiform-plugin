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
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiResultLoader
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal bool TryLoadPatch(AiJobContext context, out AiPatchDocument patch, out string error)
        {
            patch = null;
            error = null;
            if (context == null)
            {
                error = "AI job context is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.PatchPath) || !File.Exists(context.PatchPath))
            {
                error = $"Patch file not found: {context?.PatchPath}";
                return false;
            }

            try
            {
                string json;
                using (var stream = new FileStream(context.PatchPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Utf8NoBom))
                {
                    json = reader.ReadToEnd();
                }
                if (!AiProviderUtility.TryValidatePatchJsonText(json, out error))
                {
                    return false;
                }

                patch = JsonUtility.FromJson<AiPatchDocument>(json);
                if (patch == null)
                {
                    error = "Patch file is empty or invalid.";
                    return false;
                }

                AiAnalysisPackageDocument package = null;
                if (!string.IsNullOrWhiteSpace(context.AnalysisPackagePath) && File.Exists(context.AnalysisPackagePath))
                {
                    AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out package);
                }

                bool requiredNormalized = AiPatchSemanticNormalizer.NormalizeRequiredFields(patch);
                bool reordered = AiPatchValidator.NormalizeOperationOrder(patch);
                if (reordered)
                {
                    AiJobFileUtility.AppendDebugLog(context, "Normalized patch operation order to create -> structure -> type -> cleanup.");
                    Debug.LogWarning("[PSD2UIForm.AI] Patch operations were out of phase order and have been normalized locally.");
                }

                bool semanticNormalized = AiPatchSemanticNormalizer.NormalizeAnalysisEntries(patch, package);
                if (semanticNormalized)
                {
                    AiJobFileUtility.AppendDebugLog(context, "Normalized ownerless semantic analysis entries.");
                    Debug.LogWarning("[PSD2UIForm.AI] Patch 包含无 ownerId 的子控件语义，已按最近兼容 owner 或独立 Image 本地归一化。");
                }

                if (requiredNormalized || reordered || semanticNormalized)
                {
                    AiJobFileUtility.WriteJson(context.PatchPath, patch);
                }

                var validator = new AiPatchValidator();
                if (!validator.Validate(patch, package, out error))
                {
                    string validationError = error;
                    AiJobFileUtility.AppendDebugLog(context, $"Patch validation warning; loading patch and letting applier skip invalid items. error={validationError}");
                    Debug.LogWarning("[PSD2UIForm.AI] Patch 本地校验未完全通过，将继续加载并在应用时跳过无效项。error=" + validationError);
                    error = null;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                error = $"Failed to parse patch file: {ex.Message}";
                patch = null;
                return false;
            }
        }
    }
}
#endif
