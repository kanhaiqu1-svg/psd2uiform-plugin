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
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class Psd2UIFormPluginPathUtility
    {
        internal const string PluginAssemblyName = "cn.efunstudio.psd2ugui";

        private const string RuntimeDllFileName = "cn.efunstudio.psd2ugui.dll";
        private const string ScriptsFolderToken = "/Scripts/";
        private static string cachedPluginRootAssetPath;
        private static string cachedProjectRoot;

        internal static string GetPluginRootAssetPath()
        {
            if (!string.IsNullOrWhiteSpace(cachedPluginRootAssetPath))
            {
                return cachedPluginRootAssetPath;
            }

            if (string.Equals(typeof(UGUIParser).Assembly.GetName().Name, PluginAssemblyName, StringComparison.Ordinal)
                && TryResolvePluginRootFromScriptAsset(out cachedPluginRootAssetPath))
            {
                return cachedPluginRootAssetPath;
            }

            if (TryResolvePluginRootFromRuntimeDll(out cachedPluginRootAssetPath))
            {
                return cachedPluginRootAssetPath;
            }

            return string.Empty;
        }

        private static bool TryResolvePluginRootFromScriptAsset(out string pluginRootAssetPath)
        {
            pluginRootAssetPath = string.Empty;

            var temp = ScriptableObject.CreateInstance<UGUIParser>();
            try
            {
                var script = MonoScript.FromScriptableObject(temp);
                string scriptPath = script != null ? NormalizeAssetPath(AssetDatabase.GetAssetPath(script)) : string.Empty;
                int scriptsIndex = scriptPath.IndexOf(ScriptsFolderToken, StringComparison.OrdinalIgnoreCase);
                if (scriptsIndex <= 0)
                {
                    return false;
                }

                string candidate = scriptPath.Substring(0, scriptsIndex);
                if (!IsValidPluginRootAssetPath(candidate))
                {
                    return false;
                }

                pluginRootAssetPath = candidate;
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        private static bool TryResolvePluginRootFromRuntimeDll(out string pluginRootAssetPath)
        {
            pluginRootAssetPath = string.Empty;

            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(RuntimeDllFileName));
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!string.Equals(Path.GetFileName(assetPath), RuntimeDllFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string libsDirectory = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
                string candidate = NormalizeAssetPath(Path.GetDirectoryName(libsDirectory));
                if (IsValidPluginRootAssetPath(candidate))
                {
                    pluginRootAssetPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidPluginRootAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            return AssetDatabase.IsValidFolder(normalizedPath + "/AIPrompts")
                || AssetDatabase.IsValidFolder(normalizedPath + "/Scripts")
                || AssetDatabase.IsValidFolder(normalizedPath + "/PSDReader");
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace("\\", "/").Trim();
        }

        internal static string GetPluginAssetPath(string pluginRelativePath)
        {
            string root = GetPluginRootAssetPath();
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(pluginRelativePath))
            {
                return root;
            }

            return root.TrimEnd('/', '\\') + "/" + pluginRelativePath.Replace("\\", "/").TrimStart('/');
        }

        internal static string GetPluginAbsolutePath(string pluginRelativePath)
        {
            return AssetPathToAbsolutePath(GetPluginAssetPath(pluginRelativePath));
        }

        internal static string AssetPathToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            string projectRoot = GetProjectRoot();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return assetPath;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string GetProjectRoot()
        {
            if (!string.IsNullOrWhiteSpace(cachedProjectRoot))
            {
                return cachedProjectRoot;
            }

            cachedProjectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return cachedProjectRoot;
        }
    }
}
#endif
