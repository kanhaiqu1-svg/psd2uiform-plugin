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
using System.IO;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiPathDefaults
    {
        internal const string PluginRootFolderName = "PSD2UIForm";
        internal const string JobsRootRelative = "Library/Psd2UIForm/AiJobs";
        internal const string SharedRootRelative = "Library/Psd2UIForm/AiShared";
        internal const string UiHelpersDirectoryRelative = "Scripts/UIHelpers";
        internal const string OrchestratorPromptTemplateRelative = "AIPrompts/OrchestratorPrompt.md";
        internal const string CombinedRecognitionPromptTemplateRelative = "AIPrompts/TaskPrompt.md";
        internal const string MainTypePromptTemplateRelative = "AIPrompts/MainTypePrompt.md";
        internal const string ChildRelationPromptTemplateRelative = "AIPrompts/ChildRelationPrompt.md";
        internal const string StructuralPromptTemplateRelative = "AIPrompts/StructuralPrompt.md";

        internal static string ToAbsoluteProjectPath(string projectRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            return Path.Combine(projectRoot, relativePath);
        }

        internal static string ToAbsolutePluginPath(string projectRoot, string pluginRelativePath)
        {
            if (string.IsNullOrWhiteSpace(pluginRelativePath))
            {
                return string.Empty;
            }

            string pluginRootAssetPath = GetPluginRootAssetPath();
            if (string.IsNullOrWhiteSpace(pluginRootAssetPath))
            {
                return string.Empty;
            }

            return ToAbsoluteProjectPath(projectRoot, CombineAssetRelative(pluginRootAssetPath, pluginRelativePath));
        }

        internal static string ToPluginDisplayPath(string pluginRelativePath)
        {
            if (string.IsNullOrWhiteSpace(pluginRelativePath))
            {
                return PluginRootFolderName;
            }

            return PluginRootFolderName + "/" + pluginRelativePath.Replace("\\", "/").TrimStart('/');
        }

        internal static string GetPsdAssetFolderName(string psdAssetPath)
        {
            string normalizedPath = string.IsNullOrWhiteSpace(psdAssetPath)
                ? string.Empty
                : psdAssetPath.Trim().Replace("\\", "/");
            string fileName = Path.GetFileName(normalizedPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "psd";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = fileName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c <= 31 || Array.IndexOf(invalid, c) >= 0)
                {
                    chars[i] = '_';
                }
            }

            string safeName = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(safeName) ? "psd" : safeName;
        }

        internal static string GetPluginRootAssetPath()
        {
            return Psd2UIFormPluginPathUtility.GetPluginRootAssetPath();
        }

        private static string CombineAssetRelative(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
            {
                return right ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return left ?? string.Empty;
            }

            return left.TrimEnd('/', '\\') + "/" + right.Replace("\\", "/").TrimStart('/');
        }
    }
}
#endif
