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
        internal const string JobsRootRelative = "Library/Psd2UIForm";
        internal const string CombinedRecognitionPromptTemplateRelative = "AIPrompts/TaskPrompt.md";

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
            string fileName = Path.GetFileNameWithoutExtension(normalizedPath);
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
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "psd";
            // 仅按文件名分目录会让不同目录下的同名 PSD 互覆缓存/作业，追加路径稳定后缀。
            return $"{safeName}-{GetStableToken(normalizedPath.ToLowerInvariant())}";
        }

        internal static string GetStableToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "0";
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
                var builder = new System.Text.StringBuilder(8);
                for (int i = 0; i < 4; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
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
