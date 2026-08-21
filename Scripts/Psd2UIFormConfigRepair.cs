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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    [InitializeOnLoad]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal static class Psd2UIFormConfigRepair
    {
        private const string ConfigAssetRelativePath = "Psd2UIFormConfig.asset";
        private const string ConfigJsonRelativePath = "Psd2UIFormConfig.json";
        private const string ScriptsRelativePath = "Scripts/";

        internal static string ConfigAssetPath => Psd2UIFormPluginPathUtility.GetPluginAssetPath(ConfigAssetRelativePath);
        internal static string ConfigJsonPath => Psd2UIFormPluginPathUtility.GetPluginAssetPath(ConfigJsonRelativePath);
        private static string ScriptsAssetPathPrefix => Psd2UIFormPluginPathUtility.GetPluginAssetPath(ScriptsRelativePath);

        private static readonly Regex ScriptReferenceRegex = new Regex(
            @"^(\s*m_Script:\s*)\{fileID:\s*-?\d+,\s*guid:\s*[0-9a-fA-F]+,\s*type:\s*\d+\}\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static bool s_IsRepairing;
        private static bool s_IsScheduled;

        static Psd2UIFormConfigRepair()
        {
            ScheduleEnsureConfigReady();
        }

        internal static void TryEnsureConfigReady()
        {
            if (s_IsRepairing)
            {
                return;
            }

            s_IsRepairing = true;
            try
            {
                if (string.IsNullOrWhiteSpace(ConfigAssetPath) || string.IsNullOrWhiteSpace(ConfigJsonPath))
                {
                    return;
                }

                bool assetChanged = false;

                if (!File.Exists(GetFullPath(ConfigAssetPath)))
                {
                    assetChanged |= CreateConfigAssetIfMissing();
                }

                if (RepairConfigScriptReference())
                {
                    assetChanged = true;
                    AssetDatabase.ImportAsset(ConfigAssetPath, ImportAssetOptions.ForceSynchronousImport);
                }

                UGUIParser config = AssetDatabase.LoadAssetAtPath<UGUIParser>(ConfigAssetPath);
                if (config == null && File.Exists(GetFullPath(ConfigAssetPath)) && RecreateBrokenConfigAsset())
                {
                    assetChanged = true;
                    config = AssetDatabase.LoadAssetAtPath<UGUIParser>(ConfigAssetPath);
                }

                if (config == null && CreateConfigAssetIfMissing())
                {
                    assetChanged = true;
                    config = AssetDatabase.LoadAssetAtPath<UGUIParser>(ConfigAssetPath);
                }

                if (config != null && NeedsJsonRestore(config) && ImportConfigJson(config))
                {
                    assetChanged = true;
                }

                if (assetChanged)
                {
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                s_IsRepairing = false;
            }
        }

        internal static void ScheduleEnsureConfigReady()
        {
            if (s_IsScheduled)
            {
                return;
            }

            s_IsScheduled = true;
            EditorApplication.delayCall += OnDelayCall;
        }

        private static void OnDelayCall()
        {
            s_IsScheduled = false;
            TryEnsureConfigReady();
        }

        private static bool CreateConfigAssetIfMissing()
        {
            if (File.Exists(GetFullPath(ConfigAssetPath)))
            {
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<UGUIParser>(ConfigAssetPath) != null)
            {
                return false;
            }

            string directoryPath = Path.GetDirectoryName(ConfigAssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(directoryPath) && !AssetDatabase.IsValidFolder(directoryPath))
            {
                return false;
            }

            var config = ScriptableObject.CreateInstance<UGUIParser>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.ImportAsset(ConfigAssetPath, ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        private static bool RecreateBrokenConfigAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UGUIParser>(ConfigAssetPath) != null)
            {
                return false;
            }

            if (!AssetDatabase.DeleteAsset(ConfigAssetPath))
            {
                return false;
            }

            return CreateConfigAssetIfMissing();
        }

        private static bool RepairConfigScriptReference()
        {
            string fullPath = GetFullPath(ConfigAssetPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            string yaml = File.ReadAllText(fullPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return false;
            }

            Match match = ScriptReferenceRegex.Match(yaml);
            if (!match.Success || !TryGetCurrentScriptReference(out string guid, out long fileId))
            {
                return false;
            }

            string replacement = $"{match.Groups[1].Value}{{fileID: {fileId}, guid: {guid}, type: 3}}";
            if (string.Equals(match.Value, replacement, StringComparison.Ordinal))
            {
                return false;
            }

            string migratedYaml = yaml.Substring(0, match.Index) + replacement + yaml.Substring(match.Index + match.Length);
            File.WriteAllText(fullPath, migratedYaml, new UTF8Encoding(false));
            return true;
        }

        private static bool NeedsJsonRestore(UGUIParser config)
        {
            SerializedObject serializedObject = new SerializedObject(config);
            SerializedProperty rulesProp = serializedObject.FindProperty("rules");
            if (rulesProp == null || !rulesProp.isArray || rulesProp.arraySize <= 0)
            {
                return true;
            }

            SerializedProperty templateProp = serializedObject.FindProperty("uiFormTemplate");
            if (templateProp == null)
            {
                return true;
            }

            return false;
        }

        private static bool ImportConfigJson(UGUIParser config)
        {
            string jsonFullPath = GetFullPath(ConfigJsonPath);
            if (!File.Exists(jsonFullPath))
            {
                Debug.LogWarning($"Psd2UIForm: 配置修复失败，未找到 JSON 配置文件: {ConfigJsonPath}");
                return false;
            }

            string json = File.ReadAllText(jsonFullPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            ConfigSnapshot snapshot = JsonUtility.FromJson<ConfigSnapshot>(json);
            if (snapshot == null || snapshot.rules == null || snapshot.rules.Length == 0)
            {
                return false;
            }

            SerializedObject serializedObject = new SerializedObject(config);
            serializedObject.FindProperty("defaultTextType").enumValueIndex = (int)snapshot.defaultTextType;
            serializedObject.FindProperty("defaultImageType").enumValueIndex = (int)snapshot.defaultImageType;
            serializedObject.FindProperty("forceUseTMP").boolValue = snapshot.forceUseTMP;
            serializedObject.FindProperty("tmpGradientConversionMode").enumValueIndex = (int)snapshot.tmpGradientConversionMode;
            serializedObject.FindProperty("readmeDoc").stringValue = snapshot.readmeDoc ?? string.Empty;
            serializedObject.FindProperty("convertZh2En").boolValue = snapshot.convertZh2En;
            serializedObject.FindProperty("nineSliceBorderTolerance").intValue = Mathf.Clamp(snapshot.nineSliceBorderTolerance, 0, 255);
            serializedObject.FindProperty("sharedAssetsOutput").stringValue = snapshot.sharedAssetsOutput ?? string.Empty;
            serializedObject.FindProperty("sharedPrefabOutput").stringValue = snapshot.sharedPrefabOutput ?? string.Empty;

            SerializedProperty templateProp = serializedObject.FindProperty("uiFormTemplate");
            templateProp.objectReferenceValue = LoadGuidAsset<GameObject>(snapshot.uiFormTemplateGuid);

            SerializedProperty rulesProp = serializedObject.FindProperty("rules");
            rulesProp.ClearArray();
            for (int i = 0; i < snapshot.rules.Length; i++)
            {
                RuleSnapshot rule = snapshot.rules[i];
                rulesProp.InsertArrayElementAtIndex(i);
                SerializedProperty ruleProp = rulesProp.GetArrayElementAtIndex(i);
                ruleProp.FindPropertyRelative("UIType").enumValueIndex = (int)rule.UIType;
                ruleProp.FindPropertyRelative("UITypeDesc").stringValue = rule.UITypeDesc ?? string.Empty;
                ruleProp.FindPropertyRelative("UIHelper").stringValue = rule.UIHelper ?? string.Empty;
                ruleProp.FindPropertyRelative("Comment").stringValue = rule.Comment ?? string.Empty;

                SerializedProperty matchesProp = ruleProp.FindPropertyRelative("TypeMatches");
                matchesProp.ClearArray();
                if (rule.TypeMatches != null)
                {
                    for (int j = 0; j < rule.TypeMatches.Length; j++)
                    {
                        matchesProp.InsertArrayElementAtIndex(j);
                        matchesProp.GetArrayElementAtIndex(j).stringValue = rule.TypeMatches[j] ?? string.Empty;
                    }
                }

                SerializedProperty prefabProp = ruleProp.FindPropertyRelative("UIPrefab");
                prefabProp.objectReferenceValue = LoadGuidAsset<GameObject>(rule.UIPrefabGuid);
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            Debug.Log($"Psd2UIForm: 已自动从 JSON 恢复配置 {ConfigJsonPath}");
            return true;
        }

        private static T LoadGuidAsset<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static bool TryGetCurrentScriptReference(out string guid, out long fileId)
        {
            guid = null;
            fileId = 0;

            UGUIParser tempInstance = ScriptableObject.CreateInstance<UGUIParser>();
            try
            {
                MonoScript script = MonoScript.FromScriptableObject(tempInstance);
                return script != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out fileId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempInstance);
            }
        }

        private static string GetFullPath(string assetPath)
        {
            return Psd2UIFormPluginPathUtility.AssetPathToAbsolutePath(assetPath);
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ConfigSnapshot
        {
            public GUIType defaultTextType;
            public GUIType defaultImageType;
            public bool forceUseTMP;
            public TMPGradientConversionMode tmpGradientConversionMode;
            public string uiFormTemplateGuid;
            public RuleSnapshot[] rules;
            public string readmeDoc;
            public bool convertZh2En;
            public int nineSliceBorderTolerance;
            public string sharedAssetsOutput;
            public string sharedPrefabOutput;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class RuleSnapshot
        {
            public GUIType UIType;
            public string UITypeDesc;
            public string[] TypeMatches;
            public string UIPrefabGuid;
            public string UIHelper;
            public string Comment;
        }

        internal static bool ContainsRelevantAsset(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string scriptsPrefix = ScriptsAssetPathPrefix;
                if (string.Equals(path, ConfigAssetPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, ConfigJsonPath, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(scriptsPrefix) && path.StartsWith(scriptsPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class Psd2UIFormConfigRepairPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!Psd2UIFormConfigRepair.ContainsRelevantAsset(importedAssets)
                && !Psd2UIFormConfigRepair.ContainsRelevantAsset(deletedAssets)
                && !Psd2UIFormConfigRepair.ContainsRelevantAsset(movedAssets)
                && !Psd2UIFormConfigRepair.ContainsRelevantAsset(movedFromAssetPaths))
            {
                return;
            }

            Psd2UIFormConfigRepair.ScheduleEnsureConfigReady();
        }
    }
}
#endif
