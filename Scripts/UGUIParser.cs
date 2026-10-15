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
Plugin links:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using cn.efunstudio.psdreader;
using cn.efunstudio.psdreader.PsdParser;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_6000_3_OR_NEWER
using UnityEngine.TextCore;
#endif

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum GUIType
    {
        Null = 0,
        Image,
        RawImage,
        Text,
        Button,
        Dropdown,
        InputField,
        Toggle,
        Slider,
        ScrollView,
        Mask,
        FillColor = 11, // Solid color fill
        TMPText,
        TMPButton,
        TMPDropdown,
        TMPInputField,
        TMPToggle,
        Panel = 17,
        ToggleGroup,

        // UI subtypes start at 101. Values 0-100 are reserved for primary UI types.
        Background = 101, // Generic background

        // Button subtypes
        Button_Highlight,
        Button_Press,
        Button_Select,
        Button_Disable,
        Button_Text,

        // Dropdown/TMPDropdown subtypes
        Dropdown_Label,
        Dropdown_Arrow,

        // InputField/TMPInputField subtypes
        InputField_Placeholder,
        InputField_Text,

        // Toggle subtypes
        Toggle_Checkmark,
        Toggle_Label,

        // Slider subtypes
        Slider_Fill,
        Slider_Handle,

        // ScrollView subtypes
        ScrollView_Viewport, // Mask graphic for the visible list area
        ScrollView_HorizontalBarBG, // Horizontal scrollbar background
        ScrollView_HorizontalBar,// Horizontal scrollbar handle
        ScrollView_VerticalBarBG, // Vertical scrollbar background
        ScrollView_VerticalBar, // Vertical scrollbar handle
    }
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum TMPGradientConversionMode
    {
        Auto = 0,
        EditableNative = 1,
        ExactTexture = 2,
    }
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public class UGUIParseRule
    {
        public GUIType UIType;
        public string UITypeDesc;
        public string[] TypeMatches; // Type matching tags
        public GameObject UIPrefab; // UI template
        public string UIHelper; // Fully-qualified UIHelper type name
        public string Comment;// Comment
    }
    [CustomEditor(typeof(UGUIParser))]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class UGUIParserEditor : Editor
    {
        private static readonly GUIContent[] ImageReuseToleranceNames = {
            new GUIContent("视觉一致（推荐）", "允许轻微色阶和边缘栅格化差异。"),
            new GUIContent("严格", "使用更小的图片差异容差。"),
            new GUIContent("宽松", "允许更明显的细节差异。")
        };
        private static readonly GUIContent[] TmpGradientModeNames = {
            new GUIContent("自动（推荐）", "简单渐变使用原生 TMP 渐变，复杂渐变使用纹理还原。"),
            new GUIContent("原生渐变（可编辑）", "使用 TMP 原生渐变，方便后续编辑。"),
            new GUIContent("纹理渐变（精确还原）", "生成渐变纹理，保留 PSD 渐变效果。")
        };
        private static readonly GUIContent UIFormTemplateLabel = new GUIContent("界面模板", "生成 UI 界面时使用的根预制体。");
        private static readonly GUIContent ForceTmpLabel = new GUIContent("优先使用 TMP", "将文本、按钮、下拉框、输入框和开关转换为 TMP 类型，包含 AI 识别结果。常规 PSD 图层的显式后端标签仍优先。");
        private static readonly GUIContent TmpGradientLabel = new GUIContent("TMP 渐变", "PSD 文本渐变转换为 TMP 效果时使用的方式。");
        private static readonly GUIContent NamingLabel = new GUIContent("中文名转拼音", "将生成对象名称中的中文转换为拼音，不影响显示的文本内容。");
        private static readonly GUIContent NineSliceLabel = new GUIContent("九宫格识别容差", "默认值为 5。值越大，自动识别九宫格边框时允许的像素差异越大。");
        private static readonly GUIContent ImageReuseLabel = new GUIContent("图片复用精度", "用于普通图片和九宫格图片的自动复用，重新导出或整理图片后生效。Point 采样与高位深图片仍使用精确匹配。");
        private static readonly GUIContent AutoCropLabel = new GUIContent("自动裁剪九宫格", "导出时裁剪九宫格的中心区域。建议先检查边框，再通过图片右键菜单 Psd2UIForm > Crop Minimal 9-Slice 手动裁剪。");
        private static readonly GUIContent SharedImagesLabel = new GUIContent("复用图片目录", "复用图片的导出目录，路径相对于 Unity 工程根目录。");
        private static readonly GUIContent SharedPrefabsLabel = new GUIContent("复用预制体目录", "复用预制体的导出目录，路径相对于 Unity 工程根目录。");
        private static readonly GUIContent CliWindowLabel = new GUIContent("显示调试窗口", "AI 识别时显示 CLI 日志窗口，任务结束或取消后自动关闭。");
        private SerializedProperty readmeProperty;
        private SerializedProperty uiFormTemplate;
        private SerializedProperty defaultTextType;
        private SerializedProperty defaultImageType;
        private SerializedProperty forceUseTMP;
        private SerializedProperty tmpGradientConversionMode;
        private SerializedProperty convertZh2En;
        private SerializedProperty nineSliceBorderTolerance;
        private SerializedProperty sharedAssetsOutput;
        private SerializedProperty sharedPrefabOutput;
        private SerializedProperty aiMode;
        private SerializedProperty showCliWindow;
        private string[] textTypesDisplay;
        private int[] textTypes;
        private string[] imageTypesDisplay;
        private int[] imageTypes;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            readmeProperty = serializedObject.FindProperty("readmeDoc");
            uiFormTemplate = serializedObject.FindProperty("uiFormTemplate");
            defaultTextType = serializedObject.FindProperty("defaultTextType");
            defaultImageType = serializedObject.FindProperty("defaultImageType");
            forceUseTMP = serializedObject.FindProperty("forceUseTMP");
            tmpGradientConversionMode = serializedObject.FindProperty("tmpGradientConversionMode");
            convertZh2En = serializedObject.FindProperty("convertZh2En");
            nineSliceBorderTolerance = serializedObject.FindProperty("nineSliceBorderTolerance");
            sharedAssetsOutput = serializedObject.FindProperty("sharedAssetsOutput");
            sharedPrefabOutput = serializedObject.FindProperty("sharedPrefabOutput");
            var aiProviderConfig = serializedObject.FindProperty("aiProviderConfig");
            aiMode = aiProviderConfig.FindPropertyRelative("mode");
            showCliWindow = aiProviderConfig.FindPropertyRelative("showCliWindow");
            var textEnums = new GUIType[] { GUIType.Text, GUIType.TMPText };
            textTypes = new int[textEnums.Length];
            textTypesDisplay = new string[textEnums.Length];
            for (int i = 0; i < textEnums.Length; i++)
            {
                var textEnum = textEnums[i];
                textTypes[i] = (int)textEnum;
                textTypesDisplay[i] = textEnum.ToString();
            }
            var imageEnums = new GUIType[] { GUIType.Image, GUIType.RawImage };
            imageTypes = new int[imageEnums.Length];
            imageTypesDisplay = new string[imageEnums.Length];
            for (int i = 0; i < imageEnums.Length; i++)
            {
                var imageEnum = imageEnums[i];
                imageTypes[i] = (int)imageEnum;
                imageTypesDisplay[i] = imageEnum.ToString();
            }
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            PsdReaderProductAccess.EnsureDailyUpdateCheck();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("工具与帮助", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("新手指引")) Psd2UIFormOnboardingWindow.ShowWindow();
                    if (GUILayout.Button("在线教程")) Application.OpenURL("https://efunstudio.cn");
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("导出使用文档")) ((UGUIParser)target).ExportReadmeDoc();
                    if (GUILayout.Button("导出并安装 PS 脚本"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        ((UGUIParser)target).ExportLayerTagMenuConfig();
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("导出配置 JSON")) ExportConfigJson(serializedObject);
                    if (GUILayout.Button("导入配置 JSON"))
                    {
                        ImportConfigFromJson(serializedObject);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField("使用说明", EditorStyles.miniBoldLabel);
                readmeProperty.stringValue = EditorGUILayout.TextArea(readmeProperty.stringValue, GUILayout.Height(80));
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("授权与更新", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("授权状态", PsdReaderProductAccess.GetStatusLabel());
                EditorGUILayout.HelpBox(PsdReaderProductAccess.GetOverviewMessage(),
                    PsdReaderProductAccess.NeedsAttention ? MessageType.Warning : MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("授权管理")) PsdReaderProductAccess.OpenManagementWindow();
                    if (GUILayout.Button("获取订单号")) Application.OpenURL("https://shop106471535.taobao.com");
                }
                if (PsdReaderProductAccess.HasPendingUpdateTip()
                    && Psd2UIFormEditorNoticeUtility.DrawVersionUpdateNotice(PsdReaderProductAccess.GetPendingUpdateTipMessage(), "下载"))
                {
                    if (PsdReaderProductAccess.TryOpenPendingUpdateDownloadUrl()) GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("界面生成", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(uiFormTemplate, UIFormTemplateLabel);
                defaultImageType.intValue = EditorGUILayout.IntPopup("默认图片类型", defaultImageType.intValue, imageTypesDisplay, imageTypes);
                defaultTextType.intValue = EditorGUILayout.IntPopup("默认文本类型", defaultTextType.intValue, textTypesDisplay, textTypes);
                EditorGUILayout.PropertyField(forceUseTMP, ForceTmpLabel);
                tmpGradientConversionMode.intValue = EditorGUILayout.Popup(TmpGradientLabel, tmpGradientConversionMode.intValue, TmpGradientModeNames);
                EditorGUILayout.PropertyField(convertZh2En, NamingLabel);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("AI 识别", EditorStyles.boldLabel);
                DrawAiProviderConfig();
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("图片处理", EditorStyles.boldLabel);
                var settings = Psd2UIFormSettings.Instance;
                var reuseTolerance = (PsdImageReuseTolerance)EditorGUILayout.Popup(ImageReuseLabel, (int)settings.ImageReuseTolerance, ImageReuseToleranceNames);
                nineSliceBorderTolerance.intValue = EditorGUILayout.IntSlider(NineSliceLabel, nineSliceBorderTolerance.intValue, 1, 10);
                bool autoCrop = EditorGUILayout.Toggle(AutoCropLabel, settings.AutoCropMinimalNineSlice);
                if (reuseTolerance != settings.ImageReuseTolerance || autoCrop != settings.AutoCropMinimalNineSlice)
                {
                    Undo.RecordObject(settings, "修改图片处理设置");
                    settings.ImageReuseTolerance = reuseTolerance;
                    settings.AutoCropMinimalNineSlice = autoCrop;
                    Psd2UIFormSettings.Save();
                }
                if (autoCrop) EditorGUILayout.HelpBox("自动识别的边框可能不准确，导出后请检查。", MessageType.Info);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("资源输出", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    sharedAssetsOutput.stringValue = EditorGUILayout.TextField(SharedImagesLabel, sharedAssetsOutput.stringValue);
                    if (GUILayout.Button("选择…", GUILayout.Width(54)))
                    {
                        string path = EditorUtility.OpenFolderPanel("选择复用图片目录", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(path))
                            sharedAssetsOutput.stringValue = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, path);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    sharedPrefabOutput.stringValue = EditorGUILayout.TextField(SharedPrefabsLabel, sharedPrefabOutput.stringValue);
                    if (GUILayout.Button("选择…", GUILayout.Width(54)))
                    {
                        string path = EditorUtility.OpenFolderPanel("选择复用预制体目录", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(path))
                            sharedPrefabOutput.stringValue = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, path);
                    }
                }
            }

            EditorGUILayout.Space(6);
            DrawPropertiesExcluding(serializedObject, "m_Script", "uiFormTemplate");
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAiProviderConfig()
        {
            int selected = aiMode.intValue;
            if ((uint)selected >= UGF.EditorTools.Psd2UGUI.Cli.AiProfiles.LlmModes.Length) selected = (int)UGF.EditorTools.Psd2UGUI.Cli.AiMode.Qoder;
            aiMode.intValue = EditorGUILayout.Popup("CLI 工具", selected, UGF.EditorTools.Psd2UGUI.Cli.AiProfiles.LlmModeNames);
            EditorGUILayout.PropertyField(showCliWindow, CliWindowLabel);
        }

        private void ImportConfigFromJson(SerializedObject serializedObject)
        {
            string path = EditorUtility.OpenFilePanel("Select Config Json", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                var snapshot = JsonUtility.FromJson<UGUIParserSnapshot>(json);
                if (snapshot == null)
                {
                    Debug.LogError("Failed to parse config json");
                    return;
                }

                // Restore simple fields
                serializedObject.FindProperty("defaultTextType").intValue = (int)snapshot.defaultTextType;
                serializedObject.FindProperty("defaultImageType").intValue = (int)snapshot.defaultImageType;
                serializedObject.FindProperty("forceUseTMP").boolValue = snapshot.forceUseTMP;
                serializedObject.FindProperty("tmpGradientConversionMode").enumValueIndex = (int)snapshot.tmpGradientConversionMode;
                serializedObject.FindProperty("readmeDoc").stringValue = snapshot.readmeDoc;
                serializedObject.FindProperty("convertZh2En").boolValue = snapshot.convertZh2En;
                if (json.IndexOf("\"nineSliceBorderTolerance\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    serializedObject.FindProperty("nineSliceBorderTolerance").intValue = Mathf.Clamp(snapshot.nineSliceBorderTolerance, 0, 255);
                }
                serializedObject.FindProperty("sharedAssetsOutput").stringValue = snapshot.sharedAssetsOutput;
                serializedObject.FindProperty("sharedPrefabOutput").stringValue = snapshot.sharedPrefabOutput;
                RestoreAiProviderConfigSnapshot(serializedObject.FindProperty("aiProviderConfig"), snapshot.aiProviderConfig);
                if (json.IndexOf("\"showCliWindow\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var showCliWindowProp = serializedObject.FindProperty("aiProviderConfig")?.FindPropertyRelative("showCliWindow");
                    if (showCliWindowProp != null)
                    {
                        showCliWindowProp.boolValue = snapshot.aiProviderConfig != null && snapshot.aiProviderConfig.showCliWindow;
                    }
                }

                // Restore UIForm Template
                var uiFormTemplateProp = serializedObject.FindProperty("uiFormTemplate");
                uiFormTemplateProp.objectReferenceValue = null;
                if (!string.IsNullOrEmpty(snapshot.uiFormTemplateGuid))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(snapshot.uiFormTemplateGuid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var template = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        uiFormTemplateProp.objectReferenceValue = template;
                    }
                }

                // Restore Rules
                if (snapshot.rules != null)
                {
                    var rulesProp = serializedObject.FindProperty("rules");
                    rulesProp.ClearArray();
                    for (int i = 0; i < snapshot.rules.Length; i++)
                    {
                        var ruleSnapshot = snapshot.rules[i];
                        rulesProp.InsertArrayElementAtIndex(i);
                        var ruleProp = rulesProp.GetArrayElementAtIndex(i);

                        ruleProp.FindPropertyRelative("UIType").intValue = (int)ruleSnapshot.UIType;
                        ruleProp.FindPropertyRelative("UITypeDesc").stringValue = ruleSnapshot.UITypeDesc;
                        ruleProp.FindPropertyRelative("UIHelper").stringValue = ruleSnapshot.UIHelper;
                        ruleProp.FindPropertyRelative("Comment").stringValue = ruleSnapshot.Comment;

                        // TypeMatches
                        var matchesProp = ruleProp.FindPropertyRelative("TypeMatches");
                        matchesProp.ClearArray();
                        if (ruleSnapshot.TypeMatches != null)
                        {
                            for (int j = 0; j < ruleSnapshot.TypeMatches.Length; j++)
                            {
                                matchesProp.InsertArrayElementAtIndex(j);
                                matchesProp.GetArrayElementAtIndex(j).stringValue = ruleSnapshot.TypeMatches[j];
                            }
                        }

                        // Restore UIPrefab
                        var uiPrefabProp = ruleProp.FindPropertyRelative("UIPrefab");
                        uiPrefabProp.objectReferenceValue = null;
                        if (!string.IsNullOrEmpty(ruleSnapshot.UIPrefabGuid))
                        {
                            string prefabPath = AssetDatabase.GUIDToAssetPath(ruleSnapshot.UIPrefabGuid);
                            if (!string.IsNullOrEmpty(prefabPath))
                            {
                                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                                uiPrefabProp.objectReferenceValue = prefab;
                            }
                        }
                    }
                }

                serializedObject.ApplyModifiedProperties();
                Debug.Log($"Config imported from {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Import config failed: {e.Message}");
            }
        }

        private void ExportConfigJson(SerializedObject serializedObject)
        {
            string path = EditorUtility.SaveFilePanel("Save Config Json", Application.dataPath, "Psd2UIFormConfig", "json");
            if (string.IsNullOrEmpty(path)) return;

            var snapshot = new UGUIParserSnapshot();
            var target = serializedObject.targetObject as UGUIParser;
            if (target == null) return;

            // Use SerializedObject to access private fields via reflection or just use public properties if available.
            // But since we are inside the class (or editor class), let's use the serialized properties to be safe or access fields if we are inside the class.
            // Wait, UGUIParserEditor is an Editor class, it cannot access private fields of UGUIParser directly unless they are internal or via SerializedProperty.
            // However, we are writing code inside UGUIParserEditor which is nested or separate?
            // The user provided code shows UGUIParserEditor : Editor.
            // So we should use serializedObject to read values to ensure we get what's in inspector.

            snapshot.defaultTextType = (GUIType)serializedObject.FindProperty("defaultTextType").intValue;
            snapshot.defaultImageType = (GUIType)serializedObject.FindProperty("defaultImageType").intValue;
            snapshot.forceUseTMP = serializedObject.FindProperty("forceUseTMP").boolValue;
            snapshot.tmpGradientConversionMode = (TMPGradientConversionMode)serializedObject.FindProperty("tmpGradientConversionMode").enumValueIndex;
            snapshot.readmeDoc = serializedObject.FindProperty("readmeDoc").stringValue;
            snapshot.convertZh2En = serializedObject.FindProperty("convertZh2En").boolValue;
            snapshot.nineSliceBorderTolerance = Mathf.Clamp(serializedObject.FindProperty("nineSliceBorderTolerance").intValue, 0, 255);
            snapshot.sharedAssetsOutput = serializedObject.FindProperty("sharedAssetsOutput").stringValue;
            snapshot.sharedPrefabOutput = serializedObject.FindProperty("sharedPrefabOutput").stringValue;
            snapshot.aiProviderConfig = BuildAiProviderConfigSnapshot(serializedObject.FindProperty("aiProviderConfig"));

            // UIForm Template
            var template = serializedObject.FindProperty("uiFormTemplate").objectReferenceValue;
            if (template != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(template);
                snapshot.uiFormTemplateGuid = AssetDatabase.AssetPathToGUID(assetPath);
            }

            // Rules
            var rulesProp = serializedObject.FindProperty("rules");
            if (rulesProp != null && rulesProp.isArray)
            {
                var rulesList = new List<UGUIParseRuleSnapshot>();
                for (int i = 0; i < rulesProp.arraySize; i++)
                {
                    var ruleProp = rulesProp.GetArrayElementAtIndex(i);
                    var ruleSnapshot = new UGUIParseRuleSnapshot();
                    ruleSnapshot.UIType = (GUIType)ruleProp.FindPropertyRelative("UIType").intValue;
                    ruleSnapshot.UITypeDesc = ruleProp.FindPropertyRelative("UITypeDesc").stringValue;
                    ruleSnapshot.UIHelper = ruleProp.FindPropertyRelative("UIHelper").stringValue;
                    ruleSnapshot.Comment = ruleProp.FindPropertyRelative("Comment").stringValue;

                    // TypeMatches
                    var matchesProp = ruleProp.FindPropertyRelative("TypeMatches");
                    if (matchesProp != null && matchesProp.isArray)
                    {
                        var matches = new List<string>();
                        for (int j = 0; j < matchesProp.arraySize; j++)
                        {
                            matches.Add(matchesProp.GetArrayElementAtIndex(j).stringValue);
                        }
                        ruleSnapshot.TypeMatches = matches.ToArray();
                    }

                    // UIPrefab
                    var prefab = ruleProp.FindPropertyRelative("UIPrefab").objectReferenceValue;
                    if (prefab != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(prefab);
                        ruleSnapshot.UIPrefabGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    }

                    rulesList.Add(ruleSnapshot);
                }
                snapshot.rules = rulesList.ToArray();
            }

            try
            {
                string json = JsonUtility.ToJson(snapshot, true);
                File.WriteAllText(path, json);
                Debug.Log($"Config exported to {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"Export config failed: {e.Message}");
            }
        }

        private static AiProviderConfigSnapshot BuildAiProviderConfigSnapshot(SerializedProperty aiProviderConfigProperty)
        {
            if (aiProviderConfigProperty == null) return null;

            return new AiProviderConfigSnapshot
            {
                mode = (UGF.EditorTools.Psd2UGUI.Cli.AiMode)aiProviderConfigProperty.FindPropertyRelative("mode").intValue,
                showCliWindow = aiProviderConfigProperty.FindPropertyRelative("showCliWindow").boolValue
            };
        }

        private static void RestoreAiProviderConfigSnapshot(SerializedProperty aiProviderConfigProperty, AiProviderConfigSnapshot snapshot)
        {
            if (aiProviderConfigProperty == null || snapshot == null) return;

            aiProviderConfigProperty.FindPropertyRelative("mode").intValue = (int)snapshot.mode;
            var showCliWindowProp = aiProviderConfigProperty.FindPropertyRelative("showCliWindow");
            if (showCliWindowProp != null)
            {
                showCliWindowProp.boolValue = snapshot.showCliWindow;
            }
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class UGUIParserSnapshot
        {
            public GUIType defaultTextType;
            public GUIType defaultImageType;
            public bool forceUseTMP;
            public TMPGradientConversionMode tmpGradientConversionMode;
            public string uiFormTemplateGuid;
            public UGUIParseRuleSnapshot[] rules;
            public string readmeDoc;
            public bool convertZh2En;
            public int nineSliceBorderTolerance;
            public string sharedAssetsOutput;
            public string sharedPrefabOutput;
            public AiProviderConfigSnapshot aiProviderConfig;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class UGUIParseRuleSnapshot
        {
            public GUIType UIType;
            public string UITypeDesc;
            public string[] TypeMatches;
            public string UIPrefabGuid;
            public string UIHelper;
            public string Comment;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class AiProviderConfigSnapshot
        {
            public UGF.EditorTools.Psd2UGUI.Cli.AiMode mode = UGF.EditorTools.Psd2UGUI.Cli.AiMode.Qoder;
            public bool showCliWindow;
        }
    }
    [CanEditMultipleObjects]
    [CreateAssetMenu(fileName = "Psd2UIFormConfig", menuName = "ScriptableObject/Psd2UIForm Config")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class UGUIParser : ScriptableObject
    {
        internal const char UITYPE_SPLIT_CHAR = '.';
        internal const int UITYPE_MAX = 100;
        private const string LayerTagMenuScriptRelativePath = "PSScript/PSD2UGUI-LayerTagMenu.jsx";
        private const string LayerExportScriptRelativePath = "PSScript/PSD2UIForm-导出PSD.jsx";
        internal const string REF_TAG = "ref ";
        internal const string REF_PREFAB_TAG = "refp ";
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] GUIType defaultTextType = GUIType.Text;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] GUIType defaultImageType = GUIType.Image;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField, HideInInspector] bool forceUseTMP = false;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField, HideInInspector] TMPGradientConversionMode tmpGradientConversionMode = TMPGradientConversionMode.Auto;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] GameObject uiFormTemplate;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] UGUIParseRule[] rules;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] string readmeDoc = "使用说明";
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] bool convertZh2En = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] int nineSliceBorderTolerance = 5;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] string sharedAssetsOutput = "Assets/SharedUIAssets";
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] string sharedPrefabOutput = "Assets/SharedPrefab";
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] AiProviderConfig aiProviderConfig = new AiProviderConfig();
        /// <summary>
        /// Shared export directory for reused assets.
        /// </summary>
        internal string SharedAssetsOutput => sharedAssetsOutput;
        internal string SharedPrefabOutput => sharedPrefabOutput;
        internal AiProviderConfig AiProviderConfig => aiProviderConfig ?? (aiProviderConfig = new AiProviderConfig());
        internal int NineSliceBorderTolerance => Mathf.Clamp(nineSliceBorderTolerance, 0, 255);
        internal GUIType DefaultText => ResolvePreferredUIType(defaultTextType);
        internal GUIType DefaultImage => defaultImageType;
        internal GameObject UIFormTemplate => uiFormTemplate;
        internal bool ConvertZh2En => convertZh2En;
        internal bool ForceUseTMP => forceUseTMP;
        internal TMPGradientConversionMode TMPGradientMode => tmpGradientConversionMode;
        private static UGUIParser mInstance = null;
        private const string TmpEffectMaterialSignatureUserDataPrefix = "PSD2UI_TMPFX_SIG:";
        private const string LegacyTmpEffectMaterialNameToken = "__PSD2UI_TMPFX__";
        private static readonly Dictionary<string, Material> tmpEffectMaterialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> tmpGradientTextureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<
#if UNITY_6000_3_OR_NEWER
            EntityId,
#else
            int,
#endif
            Material> tmpEffectMaterialBaseLookup = new Dictionary<
#if UNITY_6000_3_OR_NEWER
                EntityId,
#else
                int,
#endif
                Material>();

        private static
#if UNITY_6000_3_OR_NEWER
            EntityId
#else
            int
#endif
            GetObjectId(UnityEngine.Object obj)
        {
#if UNITY_6000_3_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        private static string GetObjectIdKey(UnityEngine.Object obj)
        {
            return obj != null ? GetObjectId(obj).ToString() : "0";
        }

        private static void SetTMPTextWrapping(TextMeshProUGUI text, bool isMultiLine)
        {
#if UNITY_6000_0_OR_NEWER
            text.textWrappingMode = isMultiLine ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#else
            text.enableWordWrapping = isMultiLine;
#endif
        }
        private static readonly int TmpFaceDilatePropertyId = Shader.PropertyToID("_FaceDilate");
        private static readonly int TmpFaceTexturePropertyId = Shader.PropertyToID("_FaceTex");
        private static readonly int TmpFaceTextureTransformPropertyId = Shader.PropertyToID("_FaceTex_ST");
        private static readonly int TmpShaderFlagsPropertyId = Shader.PropertyToID("_ShaderFlags");
        private static readonly int TmpBevelOffsetPropertyId = Shader.PropertyToID("_BevelOffset");
        private static readonly int TmpBevelWidthPropertyId = Shader.PropertyToID("_BevelWidth");
        private static readonly int TmpBevelClampPropertyId = Shader.PropertyToID("_BevelClamp");
        private static readonly int TmpBevelRoundnessPropertyId = Shader.PropertyToID("_BevelRoundness");
        private static readonly int TmpSpecularColorPropertyId = Shader.PropertyToID("_SpecularColor");
        private static readonly int TmpSpecularPowerPropertyId = Shader.PropertyToID("_SpecularPower");
        private static readonly int TmpReflectivityPropertyId = Shader.PropertyToID("_Reflectivity");
        private static readonly int TmpReflectFaceColorPropertyId = Shader.PropertyToID("_ReflectFaceColor");
        private static readonly int TmpReflectOutlineColorPropertyId = Shader.PropertyToID("_ReflectOutlineColor");
        private static readonly int TmpDiffusePropertyId = Shader.PropertyToID("_Diffuse");
        private static readonly int TmpAmbientPropertyId = Shader.PropertyToID("_Ambient");
        private const string TmpUnderlayInnerKeyword = "UNDERLAY_INNER";
        private const float TmpInnerGlowCompositePeakCoverage = 0.65f;
        private static readonly byte[] TmpGradientBayer4x4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        internal static UGUIParser Instance
        {
            get
            {
                if (mInstance == null)
                {
                    var guids = AssetDatabase.FindAssets("t:UGUIParser");
                    if (guids == null || guids.Length == 0) return null;
                    // FindAssets 顺序不固定：多份配置时用户改的可能永远不是被取用的那份。固定取路径最靠前的，
                    // 并优先插件自带目录，保证跨机器一致。
                    System.Array.Sort(guids, (a, b) => string.Compare(
                        AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b), System.StringComparison.OrdinalIgnoreCase));
                    var preferred = guids.FirstOrDefault(g =>
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g);
                        return !string.IsNullOrEmpty(p) && p.IndexOf("Plugins/PSD2UIForm", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    var picked = string.IsNullOrEmpty(preferred) ? guids[0] : preferred;
                    var assetPath = AssetDatabase.GUIDToAssetPath(picked);
                    if (string.IsNullOrEmpty(assetPath)) return null;
                    mInstance = AssetDatabase.LoadAssetAtPath<UGUIParser>(assetPath);
                }
                return mInstance;
            }
        }
        internal static bool IsMainUIType(GUIType tp)
        {
            return tp != GUIType.Null && (int)tp <= UITYPE_MAX;
        }
        internal static bool IsSemanticUIType(GUIType uiType)
        {
            return (int)uiType > UITYPE_MAX;
        }
        internal static bool IsTransparentContainerType(GUIType uiType)
        {
            return uiType == GUIType.Null || uiType == GUIType.Panel;
        }
        internal static bool IsCompositeControlType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Button:
                case GUIType.Dropdown:
                case GUIType.InputField:
                case GUIType.Toggle:
                case GUIType.Slider:
                case GUIType.ScrollView:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                case GUIType.Panel:
                case GUIType.ToggleGroup:
                    return true;
                default:
                    return false;
            }
        }
        internal static bool IsStandaloneGraphicMainType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Image:
                case GUIType.RawImage:
                case GUIType.Text:
                case GUIType.TMPText:
                case GUIType.Mask:
                case GUIType.FillColor:
                    return true;
                default:
                    return false;
            }
        }
        internal static bool HasExplicitMainChildOwnerRule(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.ScrollView:
                case GUIType.Toggle:
                case GUIType.TMPToggle:
                    return true;
                default:
                    return false;
            }
        }
        internal static bool RequiresOwnerResolution(GUIType uiType)
        {
            return IsSemanticUIType(uiType) || HasExplicitMainChildOwnerRule(uiType);
        }
        internal static bool CanOwnSemanticRole(GUIType ownerType, GUIType roleType)
        {
            switch (roleType)
            {
                case GUIType.Background:
                    return IsMainUIType(ownerType);
                case GUIType.Button_Highlight:
                case GUIType.Button_Press:
                case GUIType.Button_Select:
                case GUIType.Button_Disable:
                case GUIType.Button_Text:
                    return ownerType == GUIType.Button || ownerType == GUIType.TMPButton;
                case GUIType.Dropdown_Label:
                case GUIType.Dropdown_Arrow:
                    return ownerType == GUIType.Dropdown || ownerType == GUIType.TMPDropdown;
                case GUIType.InputField_Placeholder:
                case GUIType.InputField_Text:
                    return ownerType == GUIType.InputField || ownerType == GUIType.TMPInputField;
                case GUIType.Toggle_Checkmark:
                case GUIType.Toggle_Label:
                    return ownerType == GUIType.Toggle || ownerType == GUIType.TMPToggle;
                case GUIType.Slider_Fill:
                case GUIType.Slider_Handle:
                    return ownerType == GUIType.Slider;
                case GUIType.ScrollView_Viewport:
                case GUIType.ScrollView_HorizontalBarBG:
                case GUIType.ScrollView_HorizontalBar:
                case GUIType.ScrollView_VerticalBarBG:
                case GUIType.ScrollView_VerticalBar:
                    return ownerType == GUIType.ScrollView;
                default:
                    return false;
            }
        }
        internal static bool CanOwnMainChild(GUIType ownerType, GUIType childType)
        {
            switch (ownerType)
            {
                case GUIType.Dropdown:
                case GUIType.TMPDropdown:
                    return childType == GUIType.ScrollView
                        || childType == GUIType.Toggle
                        || childType == GUIType.TMPToggle;
                case GUIType.ToggleGroup:
                    return childType == GUIType.Toggle || childType == GUIType.TMPToggle;
                default:
                    return false;
            }
        }
        internal GUIType ResolvePreferredUIType(GUIType uiType, string layerName = null, PsdLayerType layerType = PsdLayerType.Unknown)
        {
            if (!forceUseTMP) return uiType;
            if (layerName != null)
            {
                TryParseLayerTags(layerName, layerType, out var tags, emitWarnings: false);
                if (tags.HasExplicitTextBackend) return uiType;
            }

            switch (uiType)
            {
                case GUIType.Text:
                    return GUIType.TMPText;
                case GUIType.Button:
                    return GUIType.TMPButton;
                case GUIType.Dropdown:
                    return GUIType.TMPDropdown;
                case GUIType.InputField:
                    return GUIType.TMPInputField;
                case GUIType.Toggle:
                    return GUIType.TMPToggle;
                default:
                    return uiType;
            }
        }
        private UGUIParseRule ResolvePreferredRule(UGUIParseRule rule)
        {
            if (rule == null) return null;

            var preferredUIType = ResolvePreferredUIType(rule.UIType);
            if (preferredUIType == rule.UIType)
            {
                return rule;
            }

            return GetRule(preferredUIType) ?? rule;
        }
        private enum LayerTagFamily
        {
            Main = 0,
            Role = 1,
            ImageType = 2,
            TextBackend = 3
        }
        private sealed class LayerTagToken
        {
            public string RawTag;
            public string CanonicalTag;
            public LayerTagFamily Family;
            public GUIType UIType;
            public Image.Type ImageType;
        }
        private sealed class ParsedLayerTagInfo
        {
            public string RawName;
            public string DisplayNameOrRefPath;
            public bool IsRef;
            public bool IsRefPrefab;
            public List<LayerTagToken> OrderedTags = new List<LayerTagToken>();
            public HashSet<string> TagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<LayerTagFamily, LayerTagToken> FamilyWinners = new Dictionary<LayerTagFamily, LayerTagToken>();
            public List<string> Warnings = new List<string>();
            public GUIType MainType = GUIType.Null;
            public bool HasExplicitMainType;
            public bool HasExplicitImageType;
            public Image.Type ExplicitImageType = Image.Type.Simple;
            public bool HasExplicitTextBackend;
            public bool ForceTMP;
            public bool ForceUGUI;
        }
        private static GUIType ConvertToUGUIType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.TMPText:
                    return GUIType.Text;
                case GUIType.TMPButton:
                    return GUIType.Button;
                case GUIType.TMPDropdown:
                    return GUIType.Dropdown;
                case GUIType.TMPInputField:
                    return GUIType.InputField;
                case GUIType.TMPToggle:
                    return GUIType.Toggle;
                default:
                    return uiType;
            }
        }
        private static GUIType ConvertToTMPType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Text:
                    return GUIType.TMPText;
                case GUIType.Button:
                    return GUIType.TMPButton;
                case GUIType.Dropdown:
                    return GUIType.TMPDropdown;
                case GUIType.InputField:
                    return GUIType.TMPInputField;
                case GUIType.Toggle:
                    return GUIType.TMPToggle;
                default:
                    return uiType;
            }
        }
        private static bool SupportsTextBackendOverride(GUIType uiType)
        {
            switch (ConvertToUGUIType(uiType))
            {
                case GUIType.Text:
                case GUIType.Button:
                case GUIType.Dropdown:
                case GUIType.InputField:
                case GUIType.Toggle:
                    return true;
                default:
                    return false;
            }
        }
        private static bool ShouldAutoCalculateNineSlice(Image.Type imageType)
        {
            return imageType == Image.Type.Sliced || imageType == Image.Type.Tiled;
        }
        private static bool TryResolveSpecialTag(string token, out LayerTagToken tagInfo)
        {
            tagInfo = null;
            if (string.IsNullOrWhiteSpace(token)) return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "simple":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "simple", Family = LayerTagFamily.ImageType, ImageType = Image.Type.Simple };
                    return true;
                case "sliced":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "sliced", Family = LayerTagFamily.ImageType, ImageType = Image.Type.Sliced };
                    return true;
                case "tiled":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "tiled", Family = LayerTagFamily.ImageType, ImageType = Image.Type.Tiled };
                    return true;
                case "filled":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "filled", Family = LayerTagFamily.ImageType, ImageType = Image.Type.Filled };
                    return true;
                case "tmp":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "tmp", Family = LayerTagFamily.TextBackend };
                    return true;
                case "ugui":
                    tagInfo = new LayerTagToken() { RawTag = token, CanonicalTag = "ugui", Family = LayerTagFamily.TextBackend };
                    return true;
                default:
                    return false;
            }
        }
        private bool TryResolveRuleTag(string token, out LayerTagToken tagInfo)
        {
            tagInfo = null;
            if (string.IsNullOrWhiteSpace(token) || rules == null) return false;

            var normalizedToken = token.Trim().ToLowerInvariant();
            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule?.TypeMatches == null) continue;

                for (int j = 0; j < rule.TypeMatches.Length; j++)
                {
                    var match = rule.TypeMatches[j];
                    if (!string.Equals(match, normalizedToken, StringComparison.OrdinalIgnoreCase)) continue;

                    var canonicalTag = rule.TypeMatches.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
                    canonicalTag = string.IsNullOrWhiteSpace(canonicalTag) ? normalizedToken : canonicalTag.Trim().ToLowerInvariant();
                    tagInfo = new LayerTagToken()
                    {
                        RawTag = token,
                        CanonicalTag = canonicalTag,
                        Family = IsSemanticUIType(rule.UIType) ? LayerTagFamily.Role : LayerTagFamily.Main,
                        UIType = rule.UIType
                    };
                    return true;
                }
            }

            return false;
        }
        private bool TryResolveTag(string token, out LayerTagToken tagInfo)
        {
            return TryResolveSpecialTag(token, out tagInfo) || TryResolveRuleTag(token, out tagInfo);
        }
        private bool TryParseLayerTags(string rawName, PsdLayerType layerType, out ParsedLayerTagInfo parsedInfo, bool emitWarnings = false)
        {
            parsedInfo = new ParsedLayerTagInfo();
            parsedInfo.RawName = rawName ?? string.Empty;

            var workingName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
            if (string.IsNullOrEmpty(workingName))
            {
                ResolveParsedTypes(parsedInfo, layerType);
                return true;
            }

            if (workingName.StartsWith(REF_PREFAB_TAG, StringComparison.OrdinalIgnoreCase))
            {
                parsedInfo.IsRefPrefab = true;
                workingName = workingName.Substring(REF_PREFAB_TAG.Length).Trim();
            }
            else if (workingName.StartsWith(REF_TAG, StringComparison.OrdinalIgnoreCase))
            {
                parsedInfo.IsRef = true;
                workingName = workingName.Substring(REF_TAG.Length).Trim();
            }

            var reversedTags = new List<LayerTagToken>();
            while (!string.IsNullOrWhiteSpace(workingName))
            {
                int splitIndex = workingName.LastIndexOf(UITYPE_SPLIT_CHAR);
                // splitIndex==0 表示整个名字就是一个标签（.btn/.tmp），按文档口径应当识别。
                if (splitIndex < 0 || splitIndex >= workingName.Length - 1)
                {
                    break;
                }

                var token = workingName.Substring(splitIndex + 1);
                if (!TryResolveTag(token, out var tagInfo))
                {
                    break;
                }

                reversedTags.Add(tagInfo);
                workingName = workingName.Substring(0, splitIndex).TrimEnd();
            }

            reversedTags.Reverse();
            parsedInfo.OrderedTags.AddRange(reversedTags);
            parsedInfo.DisplayNameOrRefPath = workingName.Trim();

            for (int i = 0; i < parsedInfo.OrderedTags.Count; i++)
            {
                var tagInfo = parsedInfo.OrderedTags[i];
                parsedInfo.TagSet.Add(tagInfo.CanonicalTag);
                if (parsedInfo.FamilyWinners.TryGetValue(tagInfo.Family, out var previousWinner)
                    && !string.Equals(previousWinner.CanonicalTag, tagInfo.CanonicalTag, StringComparison.OrdinalIgnoreCase))
                {
                    parsedInfo.Warnings.Add($"{tagInfo.Family}: {previousWinner.CanonicalTag} -> {tagInfo.CanonicalTag}");
                }
                parsedInfo.FamilyWinners[tagInfo.Family] = tagInfo;
            }

            ResolveParsedTypes(parsedInfo, layerType);
#if EFUN_DEBUG
            if (emitWarnings && parsedInfo.Warnings.Count > 0)
            {
                for (int i = 0; i < parsedInfo.Warnings.Count; i++)
                {
                    Debug.LogWarning($"Layer tag warning [{rawName}]: {parsedInfo.Warnings[i]}");
                }
            }
#endif
            return true;
        }
        private void ResolveParsedTypes(ParsedLayerTagInfo parsedInfo, PsdLayerType layerType)
        {
            if (parsedInfo == null) return;

            GUIType GetImplicitMainType()
            {
                switch (layerType)
                {
                    case PsdLayerType.TextLayer:
                        return defaultTextType;
                    case PsdLayerType.FillLayer:
                        return GUIType.FillColor;
                    case PsdLayerType.LayerGroup:
                        return GUIType.Null;
                    default:
                        return defaultImageType;
                }
            }

            GUIType mainType = GetImplicitMainType();
            if (parsedInfo.FamilyWinners.TryGetValue(LayerTagFamily.Main, out var mainWinner))
            {
                var requestedMainType = mainWinner.UIType;
                var requestedBaseType = ConvertToUGUIType(requestedMainType);
                if (requestedBaseType == GUIType.Text && layerType != PsdLayerType.TextLayer)
                {
                    parsedInfo.Warnings.Add($"main tag '{mainWinner.CanonicalTag}' ignored on non-text layer");
                }
                else if ((requestedMainType == GUIType.Panel || requestedMainType == GUIType.ToggleGroup) && layerType != PsdLayerType.LayerGroup)
                {
                    parsedInfo.Warnings.Add($"main tag '{mainWinner.CanonicalTag}' ignored on non-group layer");
                }
                else
                {
                    mainType = requestedMainType;
                    parsedInfo.HasExplicitMainType = true;
                }
            }
            if (parsedInfo.FamilyWinners.TryGetValue(LayerTagFamily.Role, out var roleWinner))
            {
                mainType = roleWinner.UIType;
                parsedInfo.HasExplicitMainType = true;
            }
            if (parsedInfo.FamilyWinners.TryGetValue(LayerTagFamily.TextBackend, out var backendWinner))
            {
                parsedInfo.HasExplicitTextBackend = true;
                parsedInfo.ForceTMP = string.Equals(backendWinner.CanonicalTag, "tmp", StringComparison.OrdinalIgnoreCase);
                parsedInfo.ForceUGUI = string.Equals(backendWinner.CanonicalTag, "ugui", StringComparison.OrdinalIgnoreCase);

                if (SupportsTextBackendOverride(mainType))
                {
                    mainType = parsedInfo.ForceTMP ? ConvertToTMPType(mainType) : ConvertToUGUIType(mainType);
                }
            }
            else
            {
                mainType = ResolvePreferredUIType(mainType);
            }

            parsedInfo.MainType = mainType;

            if (parsedInfo.FamilyWinners.TryGetValue(LayerTagFamily.ImageType, out var imageTypeWinner))
            {
                parsedInfo.HasExplicitImageType = true;
                parsedInfo.ExplicitImageType = imageTypeWinner.ImageType;
            }
        }
        internal Type GetHelperType(GUIType uiType)
        {
            if (uiType == GUIType.Null) return null;
            var rule = GetRule(uiType);
            if (rule == null || string.IsNullOrWhiteSpace(rule.UIHelper)) return null;

            // Helper 常在别的程序集里，Type.GetType 只认当前程序集与限定程序集名，会静默不挂控件。
            return ResolveType(rule.UIHelper);
        }
        internal UGUIParseRule[] GetRules()
        {
            return rules ?? Array.Empty<UGUIParseRule>();
        }
        internal UGUIParseRule GetRule(GUIType uiType)
        {
            // rules 允许为空（GetRules 就有 ?? 兜底），这里直接 foreach 会在配置缺失时全线 NRE。
            foreach (var rule in GetRules())
            {
                if (rule != null && rule.UIType == uiType) return rule;
            }
            return null;
        }
        internal string GetSafeLayerName(string rawName, int fallbackIndex = -1)
        {
            var workingName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
            var refDetectSource = workingName;
            bool isRefName = refDetectSource.StartsWith(REF_TAG, StringComparison.OrdinalIgnoreCase);
            bool isRefPrefabName = refDetectSource.StartsWith(REF_PREFAB_TAG, StringComparison.OrdinalIgnoreCase);
            if (convertZh2En && !string.IsNullOrEmpty(workingName))
            {
                workingName = LayerNameUtility.ConvertChineseToLetters(workingName);
                refDetectSource = workingName;
            }
            if (isRefName || isRefPrefabName)
            {
                var refTag = isRefPrefabName ? REF_PREFAB_TAG : REF_TAG;
                var refTargetRaw = refDetectSource.Length > refTag.Length ? refDetectSource.Substring(refTag.Length).Trim() : string.Empty;
                refTargetRaw = RemoveLayerTypeFlags(refTargetRaw);
                var sanitizedTarget = LayerNameUtility.Sanitize(refTargetRaw);
                workingName = $"{refTag}{(string.IsNullOrEmpty(sanitizedTarget) ? refTargetRaw : sanitizedTarget)}".TrimEnd();
            }
            else
            {
                workingName = RemoveLayerTypeFlags(workingName);
                workingName = LayerNameUtility.Sanitize(workingName);
            }
            if (string.IsNullOrEmpty(workingName))
            {
                workingName = fallbackIndex >= 0 ? $"PsdLayer-{fallbackIndex}" : "PsdLayer";
            }
            return workingName;
        }
        /// <summary>
        /// Resolve the UI rule from the PSD layer naming convention.
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="comType"></param>
        /// <returns></returns>
        internal bool TryParse(PsdLayerNode layer, out UGUIParseRule result)
        {
            result = null;
            if (layer == null)
            {
                return false;
            }

            var layerName = layer.BindPsdLayer != null ? layer.BindPsdLayer.GetDisplayName() : layer.SourceLayerName;
            TryParseLayerTags(layerName, layer.LayerType, out var parsedInfo, emitWarnings: true);

            if (parsedInfo.MainType != GUIType.Null)
            {
                var mainRule = GetRule(parsedInfo.MainType);
                // 图层已显式标了 .tmp/.ugui，类型在标签解析时就定好了，不能再被"强制优先使用TMP"改写一次。
                result = parsedInfo.HasExplicitTextBackend ? mainRule : ResolvePreferredRule(mainRule);
            }

            return result != null;
        }
        internal static bool HasUITypeFlag(string layerName, out string tpFlag)
        {
            tpFlag = null;
            if (string.IsNullOrWhiteSpace(layerName) || layerName.EndsWith(UGUIParser.UITYPE_SPLIT_CHAR.ToString())) return false;
            int startIdx = -1;
            for (int i = layerName.Length - 1; i >= 0; i--)
            {
                if (layerName[i] == UGUIParser.UITYPE_SPLIT_CHAR)
                {
                    startIdx = i;
                    break;
                }
            }
            if (startIdx <= 0) return false;

            tpFlag = layerName.Substring(startIdx);
            return true;
        }
        /// <summary>
        /// Remove known UI type suffixes such as ".text" or ".btn" from a layer name.
        /// </summary>
        /// <param name="layerName">Raw PSD layer name.</param>
        /// <returns>Layer name without trailing UI type markers.</returns>
        internal string RemoveLayerTypeFlags(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return string.Empty;

            TryParseLayerTags(layerName, PsdLayerType.Unknown, out var parsedInfo, emitWarnings: false);
            var baseName = parsedInfo.DisplayNameOrRefPath;
            if (parsedInfo.IsRefPrefab)
            {
                return $"{REF_PREFAB_TAG}{baseName}".TrimEnd();
            }
            if (parsedInfo.IsRef)
            {
                return $"{REF_TAG}{baseName}".TrimEnd();
            }
            return baseName;
        }
        private string StripLayerTypeFlags(string layerName)
        {
            return RemoveLayerTypeFlags(layerName);
        }

        private bool IsKnownLayerTypeTag(string typeTag)
        {
            return TryResolveTag(typeTag, out _);
        }

        internal static void SetRectTransform(PsdLayerNode layerNode, UnityEngine.Component uiNode, bool pos = true, bool width = true, bool height = true, int extSize = 0)
        {
            if (uiNode == null || layerNode == null) return;

            ApplyRectTransform(layerNode.LayerRect, uiNode, pos, width, height, extSize);
        }

        private static void ApplyRectTransform(Rect rect, UnityEngine.Component uiNode, bool pos, bool width, bool height, int extSize)
        {
            var rectTransform = uiNode.GetComponent<RectTransform>();

            // Resize first so rectTransform.rect reports the final dimensions.
            if (width)
            {
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.size.x + extSize);
            }
            if (height)
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.size.y + extSize);

            if (pos)
            {
                // Read back the actual size after resizing the RectTransform.
                Vector2 actualSize = rectTransform.rect.size;

                // Calculate the offset from the layer center to the RectTransform pivot.
                Vector2 pivotOffset = (rectTransform.pivot - Vector2.one * 0.5f) * actualSize;

                // Treat rect.position as the PSD layer center in world space.
                // Pivot world position = layer center world position + pivot offset.
                Vector3 pivotWorldPos = new Vector3(
                    rect.position.x + pivotOffset.x,
                    rect.position.y + pivotOffset.y,
                    rectTransform.position.z
                );

                // Apply the world-space pivot position directly.
                rectTransform.position = pivotWorldPos;
            }
        }

        internal static void SetTextRotation(PsdLayerNode layerNode, UnityEngine.Component uiNode)
        {
            if (uiNode == null) return;

            var rectTransform = uiNode.GetComponent<RectTransform>();
            if (rectTransform == null) return;

            rectTransform.localEulerAngles = layerNode != null
                && layerNode.TryGetTextUnityRotation(out var rotation)
                ? rotation
                : Vector3.zero;
        }

        internal static void SetTextRectTransform(PsdLayerNode layerNode, UnityEngine.Component uiNode)
        {
            if (uiNode == null || layerNode == null) return;

            // 文本图层按图层 rect 自由定位。模板里的拉伸锚点(如 Dropdown 的 caption Label)会让
            // SetSizeWithCurrentAnchors 以父节点尺寸为基准,图层宽高无法生效 —— 先归一化为点锚点。
            var rectTransform = uiNode.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            }

            var sourceRect = layerNode.TryGetTextLayoutRect(out var textRect)
                ? textRect
                : layerNode.LayerRect;
            ApplyRectTransform(sourceRect, uiNode, true, true, true, 0);

            // AI supplied this layout; font substitution must not rewrite its dimensions.
            if (layerNode.HasAuthoredAiText || layerNode.HasExplicitAiTextRect) return;

            // 单行宽度校准:字体替代后 Unity 渲染宽度与 PSD 框宽存在度量差异,
            // Wrap 模式下按框宽换行会破坏单行文本的 PSD 还原。
            // 判定:文本框高度仅够一行(≤ 无约束实测行高 ×1.3)且内容在框宽下放不下(无约束宽 > 框宽)→ 扩宽 rect 到无约束实测宽。
            if (rectTransform != null)
            {
                float unconstrainedWidth = -1f;
                float unconstrainedHeight = -1f;
                if (uiNode is Text uiText && !string.IsNullOrEmpty(uiText.text))
                {
                    var settings = uiText.GetGenerationSettings(Vector2.zero);
                    unconstrainedWidth = uiText.cachedTextGenerator.GetPreferredWidth(uiText.text, settings);
                    unconstrainedHeight = uiText.cachedTextGenerator.GetPreferredHeight(uiText.text, settings);
                }
                else if (uiNode is TMPro.TextMeshProUGUI tmpText && !string.IsNullOrEmpty(tmpText.text))
                {
                    var preferred = tmpText.GetPreferredValues(tmpText.text, float.MaxValue, float.MaxValue);
                    unconstrainedWidth = preferred.x;
                    unconstrainedHeight = preferred.y;
                }

                if (unconstrainedWidth > sourceRect.width + 0.01f
                    && unconstrainedHeight > 0f
                    && sourceRect.height <= unconstrainedHeight * 1.3f)
                {
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, unconstrainedWidth);
                }
            }
        }

        internal static void SetInputFieldTextRectTransform(
            PsdLayerNode inputLayerNode,
            PsdLayerNode textLayerNode,
            UnityEngine.Component uiNode)
        {
            if (inputLayerNode == null || textLayerNode == null || uiNode == null) return;

            var inputRect = inputLayerNode.LayerRect;
            var textRect = textLayerNode.LayerRect;
            if (inputRect.width <= 0f || textRect.width <= 0f)
            {
                SetTextRectTransform(textLayerNode, uiNode);
                return;
            }

            float inputLeft = inputRect.position.x - inputRect.width * 0.5f;
            float inputRight = inputRect.position.x + inputRect.width * 0.5f;
            float textLeft = textRect.position.x - textRect.width * 0.5f;
            float textRight = textRect.position.x + textRect.width * 0.5f;

            var justification = textLayerNode.ParseTextLayerInfo(out var textInfo)
                ? textInfo.Justification
                : PsdTextJustification.Left;
            switch (justification)
            {
                case PsdTextJustification.Right:
                case PsdTextJustification.JustifyLastRight:
                    float rightInset = Mathf.Max(0f, inputRight - textRight);
                    textLeft = Mathf.Min(textLeft, inputLeft + rightInset);
                    break;
                case PsdTextJustification.Center:
                case PsdTextJustification.JustifyLastCenter:
                    float halfWidth = Mathf.Min(
                        textRect.position.x - inputLeft,
                        inputRight - textRect.position.x);
                    if (halfWidth > 0f)
                    {
                        halfWidth = Mathf.Max(halfWidth, textRect.width * 0.5f);
                        textLeft = textRect.position.x - halfWidth;
                        textRight = textRect.position.x + halfWidth;
                    }
                    break;
                default:
                    float leftInset = Mathf.Max(0f, textLeft - inputLeft);
                    textRight = Mathf.Max(textRight, inputRight - leftInset);
                    break;
            }

            var contentRect = new Rect(
                (textLeft + textRight) * 0.5f,
                textRect.position.y,
                textRight - textLeft,
                textRect.height);
            ApplyRectTransform(contentRect, uiNode, true, true, true, 0);
        }

        /// <summary>
        /// Convert the layer node image to a Texture2D asset and return it.
        /// </summary>
        /// <param name="layerNode"></param>
        /// <returns></returns>
        internal static Texture2D LayerNode2Texture(PsdLayerNode layerNode)
        {
            if (layerNode == null) return null;
            try
            {
                var spAssetName = layerNode.ExportImageAsset(false);
                if (Psd2UIFormConverter.Instance.ImageExport != null
                    && Psd2UIFormConverter.Instance.ImageExport.TryTemporary(spAssetName, false, out var temporary)) return (Texture2D)temporary;
                var texture = string.IsNullOrEmpty(spAssetName) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(spAssetName);
                if (texture == null) Psd2UIFormConverter.Instance.RecordGenerationIssue(layerNode, "texture_binding_failed", "图片资源无法生成或加载。");
                return texture;
            }
            catch (Exception ex) { Psd2UIFormConverter.Instance.RecordGenerationIssue(layerNode, "texture_binding_failed", ex.Message); }
            return null;
        }
        internal Image.Type ResolveFinalImageType(PsdLayerNode layerNode, Image.Type templateImageType)
        {
            if (layerNode == null || layerNode.IsAiAuthoritative)
            {
                return templateImageType;
            }

            var layerName = !string.IsNullOrWhiteSpace(layerNode.SourceLayerName)
                ? layerNode.SourceLayerName
                : layerNode.BindPsdLayer?.GetDisplayName();
            TryParseLayerTags(layerName, layerNode.LayerType, out var parsedInfo, emitWarnings: false);
            return parsedInfo.HasExplicitImageType ? parsedInfo.ExplicitImageType : templateImageType;
        }
        internal Sprite BindImage(PsdLayerNode layerNode, Image image)
        {
            if (layerNode == null || image == null)
            {
                return null;
            }

            PsdGeneratedDrawOrder.Register(layerNode, image);
            var finalImageType = ResolveFinalImageType(layerNode, image.type);
            image.type = finalImageType;
            var sprite = LayerNode2Sprite(layerNode, ShouldAutoCalculateNineSlice(finalImageType));
            image.sprite = sprite;
            return sprite;
        }
        /// <summary>
        /// Convert the layer node image to a Sprite asset and return it.
        /// </summary>
        /// <param name="layerNode"></param>
        /// <param name="auto9Slice">Auto-calculate 9-slice borders when the sprite has none.</param>
        /// <returns></returns>
        internal static Sprite LayerNode2Sprite(PsdLayerNode layerNode, bool auto9Slice = false)
        {
            if (layerNode == null) return null;
            try
            {
                var spAssetName = layerNode.ExportImageAsset(true, auto9Slice: auto9Slice);
                if (Psd2UIFormConverter.Instance.ImageExport != null
                    && Psd2UIFormConverter.Instance.ImageExport.TryTemporary(spAssetName, true, out var temporary)) return (Sprite)temporary;
                bool shared = layerNode.HasReuseReference || Psd2UIFormConverter.Instance.TryGetSharedSpritePath(layerNode, out _);
                if (shared && !string.IsNullOrEmpty(spAssetName) && AssetImporter.GetAtPath(spAssetName) is TextureImporter importer
                    && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
                // Fatcat定制: 加载失败时修正导入类型/路径并重试(部分机器导出时图片尚未导入完成,
                // 或类型被工程脚本改回Default, 导致prefab绑定空Sprite显示白图且无任何报错)
                var sprite = LoadSpriteAssetWithRetry(spAssetName);
                if (sprite != null)
                {
                    if (auto9Slice)
                    {
                        Psd2UIFormConverter.ApplySpriteNineSlice(spAssetName);
                        if (Psd2UIFormSettings.Instance.AutoCropMinimalNineSlice)
                        {
                            RightClickExtension.TryCropMinimalNineSlice(spAssetName);
                        }
                        sprite = PsdLayerNode.LoadSpriteAssetAtPath(Psd2UIFormConverter.NormalizeToAssetPath(spAssetName)) ?? sprite;
                    }
                    return sprite;
                }
                Psd2UIFormConverter.Instance.RecordGenerationIssue(layerNode, "sprite_binding_failed", "Sprite 资源无法生成或加载: " + spAssetName);
            }
            catch (Exception ex) { Psd2UIFormConverter.Instance.RecordGenerationIssue(layerNode, "sprite_binding_failed", ex.Message); }
            return null;
        }

        // Fatcat定制: 加载导出PNG的Sprite子资产, 失败时强制修正导入类型并重试。
        // 修复"个别机器导出prefab白图(Image组件None Sprite)"问题: 导出PNG写盘后Unity的导入
        // 可能尚未完成或类型被AutoSetTextureUISprite改回Default, LoadSpriteAssetAtPath会
        // 静默返回null; 另对工程外软链接路径做标准化(LoadAssetAtPath只认Assets内路径)。
        private static Sprite LoadSpriteAssetWithRetry(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;

            var normalizedPath = Psd2UIFormConverter.NormalizeToAssetPath(assetPath);
            var sprite = PsdLayerNode.LoadSpriteAssetAtPath(normalizedPath);
            if (sprite != null) return sprite;

            var importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
            if (importer != null)
            {
                if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
                {
                    Debug.LogWarning($"[Psd2UIForm] 导出图片未按Sprite导入, 强制修正后重试: {normalizedPath}");
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
                AssetDatabase.Refresh();
                sprite = PsdLayerNode.LoadSpriteAssetAtPath(normalizedPath);
                if (sprite != null) return sprite;
            }

            Debug.LogError($"[Psd2UIForm] 加载导出图片失败(prefab将显示白图): {assetPath}\n" +
                $"标准化路径: {normalizedPath}\n" +
                $"Importer: {(importer == null ? "null" : importer.textureType.ToString())}");
            return null;
        }
        #region 9-Slice Border Detection
        /// <summary>
        /// Estimate sprite borders by locating stable repeat regions inside the opaque content area.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="alphaThreshold">Opaque alpha threshold in [0, 255].</param>
        /// <param name="variationTolerance">Allowed channel delta when comparing adjacent rows or columns.</param>
        /// <returns>Border as (left, bottom, right, top).</returns>
        internal static Vector4 CalculateTexture9SliceBorder(Texture2D texture, byte alphaThreshold = 0, int variationTolerance = -1)
        {
            if (texture == null) return Vector4.zero;

            int width = texture.width;
            int height = texture.height;
            if (width <= 0 || height <= 0)
            {
                return Vector4.zero;
            }

            Color32[] pixels = texture.GetPixels32();
            byte effectiveAlphaThreshold = alphaThreshold == 0 ? (byte)1 : alphaThreshold;
            int effectiveVariationTolerance = variationTolerance >= 0
                ? Mathf.Clamp(variationTolerance, 0, 255)
                : (Instance != null ? Instance.NineSliceBorderTolerance : 5);

            // Ignore fully transparent padding first. The previous implementation treated
            // alpha == 0 as content when alphaThreshold was left at its default value.
            RectInt contentRect = FindContentBounds(pixels, width, height, effectiveAlphaThreshold);

            RangeInt horizontalStretch = FindAxisStretchBand(
                pixels, width, contentRect,
                true, effectiveAlphaThreshold, effectiveVariationTolerance);
            RangeInt verticalStretch = FindAxisStretchBand(
                pixels, width, contentRect,
                false, effectiveAlphaThreshold, effectiveVariationTolerance);
            bool hasHorizontalStretch = horizontalStretch.length > 1;
            bool hasVerticalStretch = verticalStretch.length > 1;

            // spriteBorder 以贴图边缘为原点，透明留白属于边框区，不能按内容包围盒起算。
            int stretchEndX = horizontalStretch.start + horizontalStretch.length - 1;
            int stretchEndY = verticalStretch.start + verticalStretch.length - 1;
            int leftBorder = hasHorizontalStretch
                ? Mathf.Clamp(horizontalStretch.start, 0, Mathf.Max(0, width - 1))
                : 0;
            int rightBorder = hasHorizontalStretch
                ? Mathf.Clamp(width - 1 - stretchEndX, 0, Mathf.Max(0, width - 1))
                : 0;
            int bottomBorder = hasVerticalStretch
                ? Mathf.Clamp(verticalStretch.start, 0, Mathf.Max(0, height - 1))
                : 0;
            int topBorder = hasVerticalStretch
                ? Mathf.Clamp(height - 1 - stretchEndY, 0, Mathf.Max(0, height - 1))
                : 0;

            return new Vector4(leftBorder, bottomBorder, rightBorder, topBorder);
        }

        private static RangeInt FindAxisStretchBand(
            Color32[] pixels, int width, RectInt contentRect,
            bool isHorizontal, byte alphaThreshold, int tolerance)
        {
            int axisMin = isHorizontal ? contentRect.xMin : contentRect.yMin;
            int axisMax = isHorizontal ? contentRect.xMax - 1 : contentRect.yMax - 1;
            int crossMin = isHorizontal ? contentRect.yMin : contentRect.xMin;
            int crossMax = isHorizontal ? contentRect.yMax - 1 : contentRect.xMax - 1;
            int axisLength = axisMax - axisMin + 1;
            int crossCount = crossMax - crossMin + 1;
            if (axisLength < 3 || crossCount <= 0)
            {
                return new RangeInt(axisMin + Mathf.Max(0, axisLength / 2), 1);
            }

            tolerance = Mathf.Max(0, tolerance);
            var minR = new int[crossCount];
            var minG = new int[crossCount];
            var minB = new int[crossCount];
            var minA = new int[crossCount];
            var maxR = new int[crossCount];
            var maxG = new int[crossCount];
            var maxB = new int[crossCount];
            var maxA = new int[crossCount];

            int bestStart = axisMin + axisLength / 2;
            int bestLength = 1;
            int bestBalance = int.MaxValue;
            int bestMinMargin = -1;
            float bestCenterDistance = float.PositiveInfinity;
            float axisCenter = axisMin + (axisLength - 1) * 0.5f;

            for (int start = axisMin + 1; start < axisMax; start++)
            {
                InitializeAxisBandExtents(
                    pixels, width, isHorizontal,
                    start, crossMin, crossMax,
                    alphaThreshold,
                    minR, minG, minB, minA,
                    maxR, maxG, maxB, maxA);

                for (int end = start; end < axisMax; end++)
                {
                    if (end > start && !TryAccumulateAxisBandLine(
                        pixels, width, isHorizontal,
                        end, crossMin, crossMax,
                        alphaThreshold, tolerance,
                        minR, minG, minB, minA,
                        maxR, maxG, maxB, maxA))
                    {
                        break;
                    }

                    int candidateLength = end - start + 1;
                    if (candidateLength <= 1)
                    {
                        continue;
                    }

                    int leftMargin = start - axisMin;
                    int rightMargin = axisMax - end;
                    if (leftMargin <= 0 || rightMargin <= 0)
                    {
                        continue;
                    }

                    int candidateBalance = Mathf.Abs(leftMargin - rightMargin);
                    int candidateMinMargin = Mathf.Min(leftMargin, rightMargin);
                    float candidateCenter = start + (candidateLength - 1) * 0.5f;
                    float candidateCenterDistance = Mathf.Abs(candidateCenter - axisCenter);
                    bool isBetterCandidate = false;
                    if (candidateLength > bestLength)
                    {
                        isBetterCandidate = true;
                    }
                    else if (candidateLength == bestLength)
                    {
                        if (candidateBalance < bestBalance)
                        {
                            isBetterCandidate = true;
                        }
                        else if (candidateBalance == bestBalance)
                        {
                            if (candidateCenterDistance < bestCenterDistance - 0.001f)
                            {
                                isBetterCandidate = true;
                            }
                            else if (Mathf.Abs(candidateCenterDistance - bestCenterDistance) <= 0.001f &&
                                     candidateMinMargin > bestMinMargin)
                            {
                                isBetterCandidate = true;
                            }
                        }
                    }

                    if (!isBetterCandidate)
                    {
                        continue;
                    }

                    bestStart = start;
                    bestLength = candidateLength;
                    bestBalance = candidateBalance;
                    bestMinMargin = candidateMinMargin;
                    bestCenterDistance = candidateCenterDistance;
                }
            }

            return new RangeInt(bestStart, bestLength);
        }

        private static void InitializeAxisBandExtents(
            Color32[] pixels, int width, bool isHorizontal,
            int axisIndex, int crossMin, int crossMax,
            byte alphaThreshold,
            int[] minR, int[] minG, int[] minB, int[] minA,
            int[] maxR, int[] maxG, int[] maxB, int[] maxA)
        {
            for (int cross = crossMin; cross <= crossMax; cross++)
            {
                Color32 pixel = NormalizeUniformRectPixel(
                    GetSlicePixel(pixels, width, isHorizontal, axisIndex, cross),
                    alphaThreshold);
                int localIndex = cross - crossMin;
                minR[localIndex] = maxR[localIndex] = pixel.r;
                minG[localIndex] = maxG[localIndex] = pixel.g;
                minB[localIndex] = maxB[localIndex] = pixel.b;
                minA[localIndex] = maxA[localIndex] = pixel.a;
            }
        }

        private static bool TryAccumulateAxisBandLine(
            Color32[] pixels, int width, bool isHorizontal,
            int axisIndex, int crossMin, int crossMax,
            byte alphaThreshold, int tolerance,
            int[] minR, int[] minG, int[] minB, int[] minA,
            int[] maxR, int[] maxG, int[] maxB, int[] maxA)
        {
            for (int cross = crossMin; cross <= crossMax; cross++)
            {
                Color32 pixel = NormalizeUniformRectPixel(
                    GetSlicePixel(pixels, width, isHorizontal, axisIndex, cross),
                    alphaThreshold);
                int localIndex = cross - crossMin;

                minR[localIndex] = Mathf.Min(minR[localIndex], pixel.r);
                minG[localIndex] = Mathf.Min(minG[localIndex], pixel.g);
                minB[localIndex] = Mathf.Min(minB[localIndex], pixel.b);
                minA[localIndex] = Mathf.Min(minA[localIndex], pixel.a);
                maxR[localIndex] = Mathf.Max(maxR[localIndex], pixel.r);
                maxG[localIndex] = Mathf.Max(maxG[localIndex], pixel.g);
                maxB[localIndex] = Mathf.Max(maxB[localIndex], pixel.b);
                maxA[localIndex] = Mathf.Max(maxA[localIndex], pixel.a);

                if (maxR[localIndex] - minR[localIndex] > tolerance ||
                    maxG[localIndex] - minG[localIndex] > tolerance ||
                    maxB[localIndex] - minB[localIndex] > tolerance ||
                    maxA[localIndex] - minA[localIndex] > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static Color32 NormalizeUniformRectPixel(Color32 pixel, byte alphaThreshold)
        {
            if (pixel.a < alphaThreshold)
            {
                pixel.r = 0;
                pixel.g = 0;
                pixel.b = 0;
            }

            return pixel;
        }

        private static Color32 GetSlicePixel(Color32[] pixels, int width, bool isHorizontal, int lineIndex, int crossIndex)
        {
            int x = isHorizontal ? lineIndex : crossIndex;
            int y = isHorizontal ? crossIndex : lineIndex;
            int pixelIndex = Mathf.Clamp(y * width + x, 0, pixels.Length - 1);
            return pixels[pixelIndex];
        }

        private static RectInt FindContentBounds(Color32[] pixels, int width, int height, byte alphaThreshold)
        {
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a < alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return new RectInt(0, 0, width, height);
            }

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        #endregion

        private struct CanonicalTextRun
        {
            public int Start;
            public int Length;
            public int SourceStyleIndex;
            public bool IsWhitespace;
        }

        private struct CanonicalTextData
        {
            public string Text;
            public CanonicalTextRun[] Runs;
            public int RunCount;
            public int BaseStyleIndex;
        }

        private struct UGUITextRunStyle
        {
            public int FontSize;
            public Color32 Color;
            public FontStyle FontStyle;
        }

        private struct UGUITextRenderRun
        {
            public int Start;
            public int Length;
            public UGUITextRunStyle Style;
        }

        private struct UGUITextRenderPlan
        {
            public string Text;
            public UGUITextRenderRun[] Runs;
            public int RunCount;
            public string BaseFontName;
            public UGUITextRunStyle ComponentStyle;
            public bool HasInlineTags;
        }

        private struct TMPTextRunStyle
        {
            public TMP_FontAsset Font;
            public Material Material;
            public float FontSize;
            public Color32 Color;
            public float CharacterSpacing;
            public float BaselineShift;
            public float HorizontalScale;
            public FontStyles FontStyle;
            public PsdTextCapitalization Capitalization;
            public bool NoBreak;
        }

        private struct TMPTextRenderRun
        {
            public int Start;
            public int Length;
            public TMPTextRunStyle Style;
        }

        private struct TMPTextRenderPlan
        {
            public string Text;
            public TMPTextRenderRun[] Runs;
            public int RunCount;
            public TMPTextRunStyle ComponentStyle;
            public FontStyles ComponentFontStyle;
            public float ComponentCharacterSpacing;
            public bool EmitFontStyleTags;
            public bool EmitCharacterSpacingTags;
            public bool EmitRunColors;
            public bool HasInlineTags;
            public bool HasParagraphTags;

            public bool HasRichText => HasInlineTags || HasParagraphTags;
        }

        /// <summary>
        /// Apply parsed PSD text data to a legacy uGUI Text component.
        /// </summary>
        /// <param name="txtLayer"></param>
        /// <param name="text"></param>
        internal static TextLayerInfo SetTextStyle(PsdLayerNode txtLayer, UnityEngine.UI.Text text)
        {
            if (text == null) return default;
            PsdGeneratedDrawOrder.Register(txtLayer, text);
            text.gameObject.SetActive(txtLayer != null);
            if (txtLayer != null && txtLayer.ParseTextLayerInfo(out var textInfo))
            {
                CanonicalTextData canonical = BuildCanonicalTextData(in textInfo);
                UGUITextRenderPlan plan = BuildUGUITextRenderPlan(in textInfo, in canonical);
                var tFont = FindFontAsset(plan.BaseFontName);
                if (tFont != null) text.font = tFont;
                text.text = plan.HasInlineTags ? SerializeUGUIText(in plan) : plan.Text;
                text.supportRichText = plan.HasInlineTags;
                text.fontSize = plan.ComponentStyle.FontSize;
                text.fontStyle = plan.ComponentStyle.FontStyle;
                text.color = plan.ComponentStyle.Color;
                text.resizeTextForBestFit = false;
                text.lineSpacing = ConvertPsdLeadingToUGUILineSpacing(text, in textInfo);
                text.alignment = ConvertPsdAlignment(text.alignment, textInfo.Justification);
                if (txtLayer.HasAuthoredAiText) text.alignment = txtLayer.AiTextAlignment;
                bool isRotated = txtLayer.TryGetTextUnityRotation(out var rotation)
                    && Mathf.Abs(rotation.z) > 0.001f;
                text.alignByGeometry = !textInfo.IsParagraphText && !isRotated;
                text.horizontalOverflow = textInfo.IsParagraphText ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                ApplyUGUITextEffects(text, in textInfo);
                if (txtLayer.IsAiTarget)
                {
                    var group = text.GetComponent<CanvasGroup>();
                    if (group != null || txtLayer.AiTextAncestorOpacity < 1f)
                    {
                        if (group == null) group = text.gameObject.AddComponent<CanvasGroup>();
                        group.alpha = txtLayer.AiTextAncestorOpacity;
                    }
                }
                return textInfo;
            }
            return default;
        }
        /// <summary>
        /// Apply parsed PSD text data to a TextMeshProUGUI component.
        /// </summary>
        /// <param name="txtLayer"></param>
        /// <param name="text"></param>
        internal static void SetTextStyle(PsdLayerNode txtLayer, TextMeshProUGUI text)
        {
            if (text == null) return;
            PsdGeneratedDrawOrder.Register(txtLayer, text);
            text.gameObject.SetActive(txtLayer != null);
            if (txtLayer != null && txtLayer.ParseTextLayerInfo(out var textInfo))
            {
                CanonicalTextData canonical = BuildCanonicalTextData(in textInfo);
                var runFonts = ResolveTMPFontAssets(text, in textInfo);
                int baseStyleIndex = canonical.BaseStyleIndex;
                var tFont = baseStyleIndex >= 0 && baseStyleIndex < runFonts.Length
                    ? runFonts[baseStyleIndex]
                    : text.font;
                if (tFont != null)
                {
                    var fontMaterial = EnsureTMPFontAssetResources(tFont);
                    text.font = tFont;
                    if (fontMaterial != null)
                    {
                        text.fontSharedMaterial = fontMaterial;
                    }
                }

                Vector2 effectSize = txtLayer.LayerRect.size;
                TMPGradientOutput gradientOutput = ResolveTMPGradientOutput(in textInfo);
                bool useGradientFill = gradientOutput != TMPGradientOutput.None;
                TextStyleRunInfo baseSourceStyle = baseStyleIndex >= 0
                    ? textInfo.StyleRuns[baseStyleIndex]
                    : textInfo.StyleRuns[0];
                text.fontSize = QuantizeTMPValue(baseSourceStyle.FontSize);
                text.color = useGradientFill ? Color.white : (Color)(Color32)baseSourceStyle.Color;
                ApplyTMPTextEffects(text, effectSize, in textInfo);
                var runMaterials = ResolveTMPRunEffectMaterials(runFonts, effectSize, in textInfo);
                TMPTextRenderPlan plan = BuildTMPTextRenderPlan(
                    in textInfo,
                    in canonical,
                    runFonts,
                    runMaterials,
                    !useGradientFill);

                if (plan.ComponentStyle.Font != null)
                    text.font = plan.ComponentStyle.Font;
                if (plan.ComponentStyle.Material != null
                    && text.fontSharedMaterial != plan.ComponentStyle.Material)
                {
                    text.fontSharedMaterial = plan.ComponentStyle.Material;
                    text.UpdateMeshPadding();
                    text.SetMaterialDirty();
                }
                text.fontSize = plan.ComponentStyle.FontSize;
                text.fontStyle = plan.ComponentFontStyle;
                text.characterSpacing = plan.ComponentCharacterSpacing;
                bool serializedTextHasLineHeightTags = textInfo.ParagraphRuns != null
                    && textInfo.ParagraphRuns.Length > 0
                    && plan.HasRichText;
                text.lineSpacing = serializedTextHasLineHeightTags
                    ? 0f
                    : ConvertPsdLeadingToTMPLineSpacing(text, in textInfo);
                text.paragraphSpacing = 0f;
#if UNITY_6000_3_OR_NEWER
                text.fontFeatures = ResolveTMPAutoKerning(in textInfo)
                    ? new List<OTL_FeatureTag> { OTL_FeatureTag.kern }
                    : new List<OTL_FeatureTag>();
#else
                text.enableKerning = ResolveTMPAutoKerning(in textInfo);
#endif
                text.horizontalAlignment = ConvertPsdAlignment(textInfo.Justification);
                text.enableAutoSizing = false;
                SetTMPTextWrapping(text, textInfo.IsParagraphText);
                text.overflowMode = TextOverflowModes.Overflow;
                text.margin = ResolveTMPParagraphMargins(in textInfo);
                text.extraPadding = textInfo.HasOutline || textInfo.HasShadow || textInfo.HasGlow || textInfo.HasBevel;
                text.color = plan.ComponentStyle.Color;
                text.richText = plan.HasRichText;
                text.text = plan.HasRichText
                    ? SerializeTMPText(in textInfo, in plan)
                    : plan.Text;
                ApplyTMPTextGradientMapping(text, effectSize, gradientOutput, in textInfo);
                text.ForceMeshUpdate();
            }
        }
        private static TMP_FontAsset[] ResolveTMPFontAssets(TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            var runs = textInfo.StyleRuns;
            if (runs == null || runs.Length == 0)
                return Array.Empty<TMP_FontAsset>();

            var fonts = new TMP_FontAsset[runs.Length];
            TMP_FontAsset baseFont = FindTMPFontAsset(runs[0].FontName) ?? text.font;
            string sourceText = textInfo.Text ?? string.Empty;
            for (int i = 0; i < runs.Length; i++)
            {
                var font = i == 0
                    ? baseFont
                    : FindTMPFontAsset(runs[i].FontName) ?? baseFont;
                font = EnsureTMPEffectFontCapacity(font, runs[i].FontSize, in textInfo) ?? font;
                if (i == 0)
                    baseFont = font;
                if (font != null && baseFont != null && HasSameTMPFontSource(font, baseFont))
                {
                    font = baseFont;
                }
                else if (font != null && baseFont != null)
                {
                    font = EnsureTMPRichTextFontAsset(font) ?? baseFont;
                }
                fonts[i] = font;
                if (font == null)
                    continue;

                EnsureTMPFontAssetResources(font);
                int start = Mathf.Clamp(runs[i].Start, 0, sourceText.Length);
                int length = Mathf.Clamp(runs[i].Length, 0, sourceText.Length - start);
                if (length > 0)
                {
                    EnsureTMPFontHasCharacters(font, sourceText.Substring(start, length));
                }
                MaterialReferenceManager.AddFontAsset(font);
            }

            return fonts;
        }

        private static Material[] ResolveTMPRunEffectMaterials(
            TMP_FontAsset[] runFonts,
            Vector2 effectSize,
            in TextLayerInfo textInfo)
        {
            if (runFonts == null
                || runFonts.Length == 0
                || !HasTMPMaterialEffects(in textInfo))
            {
                return Array.Empty<Material>();
            }

            var materials = new Material[runFonts.Length];
            for (int i = 0; i < runFonts.Length; i++)
            {
                var font = runFonts[i];
                if (font == null)
                {
                    continue;
                }

                var baseMaterial = EnsureTMPFontAssetResources(font);
                var run = textInfo.StyleRuns[i];
                materials[i] = GetOrCreateTMPEffectMaterial(
                    font,
                    baseMaterial,
                    run.FontSize,
                    run.Color,
                    effectSize,
                    in textInfo);

                if (i > 0 && materials[i] != null)
                {
                    RegisterTMPFontMaterial(materials[i]);
                }
            }

            return materials;
        }

        private static void RegisterTMPFontMaterial(Material material)
        {
            int hashCode = TMP_TextUtilities.GetSimpleHashCode(material.name);
            if (!MaterialReferenceManager.TryGetMaterial(hashCode, out _))
            {
                MaterialReferenceManager.AddFontMaterial(hashCode, material);
            }
        }

        private static CanonicalTextData BuildCanonicalTextData(in TextLayerInfo textInfo)
        {
            string text = textInfo.Text ?? string.Empty;
            var sourceRuns = textInfo.StyleRuns;
            if (text.Length == 0 || sourceRuns == null || sourceRuns.Length == 0)
            {
                return new CanonicalTextData
                {
                    Text = text,
                    Runs = Array.Empty<CanonicalTextRun>(),
                    RunCount = 0,
                    BaseStyleIndex = sourceRuns != null && sourceRuns.Length > 0 ? 0 : -1,
                };
            }

            var runs = new CanonicalTextRun[sourceRuns.Length * 2 + 1];
            int count = 0;
            int offset = 0;
            int baseStyleIndex = -1;
            for (int i = 0; i < sourceRuns.Length && offset < text.Length; i++)
            {
                var sourceRun = sourceRuns[i];
                int start = Mathf.Clamp(sourceRun.Start, offset, text.Length);
                if (start > offset)
                {
                    int gapStyleIndex = i > 0 ? i - 1 : 0;
                    AddCanonicalTextRun(text, runs, ref count, offset, start, gapStyleIndex, ref baseStyleIndex);
                    offset = start;
                }

                int end = Mathf.Clamp(sourceRun.Start + sourceRun.Length, start, text.Length);
                if (end > start)
                {
                    AddCanonicalTextRun(text, runs, ref count, start, end, i, ref baseStyleIndex);
                    offset = end;
                }
            }

            if (offset < text.Length)
            {
                AddCanonicalTextRun(
                    text,
                    runs,
                    ref count,
                    offset,
                    text.Length,
                    sourceRuns.Length - 1,
                    ref baseStyleIndex);
            }

            if (baseStyleIndex < 0 && count > 0)
                baseStyleIndex = runs[0].SourceStyleIndex;
            return new CanonicalTextData
            {
                Text = text,
                Runs = runs,
                RunCount = count,
                BaseStyleIndex = baseStyleIndex,
            };
        }

        private static void AddCanonicalTextRun(
            string text,
            CanonicalTextRun[] runs,
            ref int count,
            int start,
            int end,
            int sourceStyleIndex,
            ref int baseStyleIndex)
        {
            bool isWhitespace = IsWhitespaceRange(text, start, end - start);
            runs[count++] = new CanonicalTextRun
            {
                Start = start,
                Length = end - start,
                SourceStyleIndex = sourceStyleIndex,
                IsWhitespace = isWhitespace,
            };
            if (!isWhitespace && baseStyleIndex < 0)
                baseStyleIndex = sourceStyleIndex;
        }

        private static bool IsWhitespaceRange(string text, int start, int length)
        {
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                    return false;
            }
            return true;
        }

        private static UGUITextRenderPlan BuildUGUITextRenderPlan(
            in TextLayerInfo textInfo,
            in CanonicalTextData canonical)
        {
            var sourceRuns = textInfo.StyleRuns;
            int baseStyleIndex = canonical.BaseStyleIndex >= 0 ? canonical.BaseStyleIndex : 0;
            UGUITextRunStyle baseStyle = ProjectUGUITextStyle(in sourceRuns[baseStyleIndex]);
            var projectedRuns = new UGUITextRenderRun[canonical.RunCount];

            int baseRunIndex = 0;
            bool foundBaseRun = false;
            for (int i = 0; i < canonical.RunCount; i++)
            {
                CanonicalTextRun canonicalRun = canonical.Runs[i];
                projectedRuns[i] = new UGUITextRenderRun
                {
                    Start = canonicalRun.Start,
                    Length = canonicalRun.Length,
                    Style = ProjectUGUITextStyle(in sourceRuns[canonicalRun.SourceStyleIndex]),
                };
                if (!foundBaseRun && !canonicalRun.IsWhitespace)
                {
                    baseRunIndex = i;
                    foundBaseRun = true;
                }
            }

            for (int i = 0; i < canonical.RunCount; i++)
            {
                if (!canonical.Runs[i].IsWhitespace)
                    continue;

                UGUITextRunStyle whitespaceStyle = projectedRuns[i].Style;
                if (i > 0 && AreUGUITextLayoutStylesEqual(in projectedRuns[i - 1].Style, in whitespaceStyle))
                {
                    whitespaceStyle.Color = projectedRuns[i - 1].Style.Color;
                    projectedRuns[i].Style = whitespaceStyle;
                    continue;
                }

                for (int j = i + 1; j < canonical.RunCount; j++)
                {
                    if (!AreUGUITextLayoutStylesEqual(in whitespaceStyle, in projectedRuns[j].Style))
                        break;
                    if (canonical.Runs[j].IsWhitespace)
                        continue;

                    whitespaceStyle.Color = projectedRuns[j].Style.Color;
                    projectedRuns[i].Style = whitespaceStyle;
                    break;
                }
            }

            baseStyle = canonical.RunCount > 0 ? projectedRuns[baseRunIndex].Style : baseStyle;
            bool allBold = canonical.RunCount > 0;
            bool allItalic = canonical.RunCount > 0;
            for (int i = 0; i < canonical.RunCount; i++)
            {
                FontStyle style = projectedRuns[i].Style.FontStyle;
                allBold &= style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
                allItalic &= style == FontStyle.Italic || style == FontStyle.BoldAndItalic;
            }
            baseStyle.FontStyle = allBold
                ? (allItalic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                : (allItalic ? FontStyle.Italic : FontStyle.Normal);

            int runCount = MergeUGUITextRuns(projectedRuns, canonical.RunCount);
            bool hasInlineTags = false;
            for (int i = 0; i < runCount; i++)
            {
                if (!AreUGUITextStylesEqual(in baseStyle, in projectedRuns[i].Style))
                {
                    hasInlineTags = true;
                    break;
                }
            }

            if (ContainsUGUIRichTextTag(canonical.Text) && hasInlineTags)
            {
                // 文本里有字面 <b>/<color> 时 uGUI 只能整段按纯文本走，分段样式确实会丢，说清楚别静默。
                hasInlineTags = false;
                string preview = canonical.Text.Length > 60 ? canonical.Text.Substring(0, 60) + "…" : canonical.Text;
                Debug.LogWarning($"uGUI Text 图层文本包含字面富文本标签，已按纯文本导出，该图层的分段字号/颜色/加粗未应用: {preview}");
            }
            char[] capitalized = null;
            for (int i = 0; i < canonical.RunCount; i++)
            {
                var run = canonical.Runs[i];
                if ((sourceRuns[run.SourceStyleIndex].TMPFontStyle & TMPro.FontStyles.UpperCase) == 0) continue;
                if (capitalized == null) capitalized = canonical.Text.ToCharArray();
                for (int c = run.Start; c < run.Start + run.Length; c++) capitalized[c] = char.ToUpperInvariant(capitalized[c]);
            }
            return new UGUITextRenderPlan
            {
                Text = capitalized == null ? canonical.Text : new string(capitalized),
                Runs = projectedRuns,
                RunCount = runCount,
                BaseFontName = sourceRuns[baseStyleIndex].FontName,
                ComponentStyle = baseStyle,
                HasInlineTags = hasInlineTags,
            };
        }

        private static UGUITextRunStyle ProjectUGUITextStyle(in TextStyleRunInfo source)
        {
            return new UGUITextRunStyle
            {
                FontSize = Mathf.Max(1, Mathf.RoundToInt(source.FontSize)),
                Color = source.Color,
                FontStyle = source.FontStyle,
            };
        }

        private static int MergeUGUITextRuns(UGUITextRenderRun[] runs, int count)
        {
            int writeIndex = 0;
            for (int i = 0; i < count; i++)
            {
                UGUITextRenderRun run = runs[i];
                if (writeIndex > 0)
                {
                    ref UGUITextRenderRun previous = ref runs[writeIndex - 1];
                    if (previous.Start + previous.Length == run.Start
                        && AreUGUITextStylesEqual(in previous.Style, in run.Style))
                    {
                        previous.Length += run.Length;
                        continue;
                    }
                }
                runs[writeIndex++] = run;
            }
            return writeIndex;
        }

        private static bool ContainsUGUIRichTextTag(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '<')
                    continue;

                int nameStart = i + 1;
                if (nameStart < text.Length && text[nameStart] == '/')
                    nameStart++;
                int nameLength = 0;
                while (nameStart + nameLength < text.Length)
                {
                    char character = text[nameStart + nameLength];
                    if ((character < 'a' || character > 'z') && (character < 'A' || character > 'Z'))
                        break;
                    nameLength++;
                }

                if (nameLength == 0 || nameStart + nameLength >= text.Length)
                    continue;

                char delimiter = text[nameStart + nameLength];
                if (delimiter != '>' && delimiter != '=' && delimiter != ' ')
                    continue;

                if ((nameLength == 1 && (IsAsciiEqual(text[nameStart], 'b') || IsAsciiEqual(text[nameStart], 'i')))
                    || MatchesAscii(text, nameStart, nameLength, "size")
                    || MatchesAscii(text, nameStart, nameLength, "color")
                    || MatchesAscii(text, nameStart, nameLength, "material")
                    || MatchesAscii(text, nameStart, nameLength, "quad"))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MatchesAscii(string text, int start, int length, string expected)
        {
            if (length != expected.Length)
                return false;
            for (int i = 0; i < length; i++)
            {
                if (!IsAsciiEqual(text[start + i], expected[i]))
                    return false;
            }
            return true;
        }

        private static bool IsAsciiEqual(char value, char expected)
        {
            return value == expected || value == expected - ('a' - 'A');
        }

        private static bool AreUGUITextStylesEqual(
            in UGUITextRunStyle left,
            in UGUITextRunStyle right)
        {
            return left.FontSize == right.FontSize
                && AreColorsEqual(left.Color, right.Color)
                && left.FontStyle == right.FontStyle;
        }

        private static bool AreUGUITextLayoutStylesEqual(
            in UGUITextRunStyle left,
            in UGUITextRunStyle right)
        {
            return left.FontSize == right.FontSize
                && left.FontStyle == right.FontStyle;
        }

        private static bool AreColorsEqual(Color32 left, Color32 right)
        {
            return left.r == right.r
                && left.g == right.g
                && left.b == right.b
                && left.a == right.a;
        }

        private enum UGUITextTagKind : byte
        {
            Size,
            Color,
            Bold,
            Italic,
        }

        private struct UGUITextTag
        {
            public UGUITextTagKind Kind;
            public int FontSize;
            public Color32 Color;
        }

        private static string SerializeUGUIText(in UGUITextRenderPlan plan)
        {
            var builder = new StringBuilder(plan.Text.Length + plan.RunCount * 32);
            var currentTags = new UGUITextTag[4];
            var nextTags = new UGUITextTag[4];
            int currentCount = 0;
            for (int i = 0; i < plan.RunCount; i++)
            {
                UGUITextRenderRun run = plan.Runs[i];
                int nextCount = FillUGUITextTags(in run.Style, in plan.ComponentStyle, nextTags);
                int commonCount = 0;
                while (commonCount < currentCount
                    && commonCount < nextCount
                    && AreUGUITextTagsEqual(in currentTags[commonCount], in nextTags[commonCount]))
                {
                    commonCount++;
                }
                for (int j = currentCount - 1; j >= commonCount; j--)
                    AppendUGUITextTagClosing(builder, currentTags[j].Kind);
                for (int j = commonCount; j < nextCount; j++)
                    AppendUGUITextTagOpening(builder, in nextTags[j]);

                builder.Append(plan.Text, run.Start, run.Length);
                var swap = currentTags;
                currentTags = nextTags;
                nextTags = swap;
                currentCount = nextCount;
            }
            for (int i = currentCount - 1; i >= 0; i--)
                AppendUGUITextTagClosing(builder, currentTags[i].Kind);
            return builder.ToString();
        }

        private static int FillUGUITextTags(
            in UGUITextRunStyle style,
            in UGUITextRunStyle componentStyle,
            UGUITextTag[] tags)
        {
            int count = 0;
            if (style.FontSize != componentStyle.FontSize)
                tags[count++] = new UGUITextTag { Kind = UGUITextTagKind.Size, FontSize = style.FontSize };
            if (!AreColorsEqual(style.Color, componentStyle.Color))
                tags[count++] = new UGUITextTag { Kind = UGUITextTagKind.Color, Color = style.Color };
            bool componentBold = componentStyle.FontStyle == FontStyle.Bold
                || componentStyle.FontStyle == FontStyle.BoldAndItalic;
            bool componentItalic = componentStyle.FontStyle == FontStyle.Italic
                || componentStyle.FontStyle == FontStyle.BoldAndItalic;
            bool styleBold = style.FontStyle == FontStyle.Bold || style.FontStyle == FontStyle.BoldAndItalic;
            bool styleItalic = style.FontStyle == FontStyle.Italic || style.FontStyle == FontStyle.BoldAndItalic;
            if (styleBold && !componentBold)
                tags[count++] = new UGUITextTag { Kind = UGUITextTagKind.Bold };
            if (styleItalic && !componentItalic)
                tags[count++] = new UGUITextTag { Kind = UGUITextTagKind.Italic };
            return count;
        }

        private static bool AreUGUITextTagsEqual(in UGUITextTag left, in UGUITextTag right)
        {
            return left.Kind == right.Kind
                && left.FontSize == right.FontSize
                && AreColorsEqual(left.Color, right.Color);
        }

        private static void AppendUGUITextTagOpening(StringBuilder builder, in UGUITextTag tag)
        {
            switch (tag.Kind)
            {
                case UGUITextTagKind.Size:
                    builder.Append("<size=").Append(tag.FontSize).Append('>');
                    break;
                case UGUITextTagKind.Color:
                    builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(tag.Color)).Append('>');
                    break;
                case UGUITextTagKind.Bold:
                    builder.Append("<b>");
                    break;
                case UGUITextTagKind.Italic:
                    builder.Append("<i>");
                    break;
            }
        }

        private static void AppendUGUITextTagClosing(StringBuilder builder, UGUITextTagKind kind)
        {
            switch (kind)
            {
                case UGUITextTagKind.Size:
                    builder.Append("</size>");
                    break;
                case UGUITextTagKind.Color:
                    builder.Append("</color>");
                    break;
                case UGUITextTagKind.Bold:
                    builder.Append("</b>");
                    break;
                case UGUITextTagKind.Italic:
                    builder.Append("</i>");
                    break;
            }
        }

        internal static bool ResolveTMPAutoKerning(in TextLayerInfo textInfo)
        {
            var runs = textInfo.StyleRuns;
            if (runs == null || runs.Length == 0)
                return textInfo.AutoKerning;

            bool value = runs[0].AutoKerning;
            for (int i = 1; i < runs.Length; i++)
            {
                if (runs[i].AutoKerning != value)
                    return false;
            }
            return value;
        }

        private static TMPTextRenderPlan BuildTMPTextRenderPlan(
            in TextLayerInfo textInfo,
            in CanonicalTextData canonical,
            TMP_FontAsset[] runFonts,
            Material[] runMaterials,
            bool emitRunColors)
        {
            var sourceRuns = textInfo.StyleRuns;
            int baseStyleIndex = canonical.BaseStyleIndex >= 0 ? canonical.BaseStyleIndex : 0;
            TMPTextRunStyle baseStyle = ProjectTMPTextStyle(
                in sourceRuns[baseStyleIndex],
                baseStyleIndex,
                runFonts,
                runMaterials,
                emitRunColors);
            var projectedRuns = new TMPTextRenderRun[canonical.RunCount];
            int baseRunIndex = 0;
            bool foundBaseRun = false;
            for (int i = 0; i < canonical.RunCount; i++)
            {
                CanonicalTextRun canonicalRun = canonical.Runs[i];
                projectedRuns[i] = new TMPTextRenderRun
                {
                    Start = canonicalRun.Start,
                    Length = canonicalRun.Length,
                    Style = ProjectTMPTextStyle(
                        in sourceRuns[canonicalRun.SourceStyleIndex],
                        canonicalRun.SourceStyleIndex,
                        runFonts,
                        runMaterials,
                        emitRunColors),
                };
                if (!foundBaseRun && !canonicalRun.IsWhitespace)
                {
                    baseRunIndex = i;
                    foundBaseRun = true;
                }
            }

            for (int i = 0; i < canonical.RunCount; i++)
            {
                if (!canonical.Runs[i].IsWhitespace || !CanFoldTMPWhitespaceColor(in projectedRuns[i].Style))
                    continue;

                TMPTextRunStyle whitespaceStyle = projectedRuns[i].Style;
                if (i > 0 && AreTMPTextLayoutStylesEqual(in projectedRuns[i - 1].Style, in whitespaceStyle))
                {
                    whitespaceStyle.Color = projectedRuns[i - 1].Style.Color;
                    projectedRuns[i].Style = whitespaceStyle;
                    continue;
                }

                for (int j = i + 1; j < canonical.RunCount; j++)
                {
                    if (!AreTMPTextLayoutStylesEqual(in whitespaceStyle, in projectedRuns[j].Style))
                        break;
                    if (canonical.Runs[j].IsWhitespace)
                        continue;

                    whitespaceStyle.Color = projectedRuns[j].Style.Color;
                    projectedRuns[i].Style = whitespaceStyle;
                    break;
                }
            }

            baseStyle = canonical.RunCount > 0 ? projectedRuns[baseRunIndex].Style : baseStyle;
            bool uniformCharacterSpacing = true;
            bool uniformFontStyle = true;
            for (int i = 0; i < canonical.RunCount; i++)
            {
                TMPTextRunStyle style = projectedRuns[i].Style;
                uniformCharacterSpacing &= style.CharacterSpacing == baseStyle.CharacterSpacing;
                uniformFontStyle &= style.FontStyle == baseStyle.FontStyle
                    && style.Capitalization == baseStyle.Capitalization;
            }

            int runCount = MergeTMPTextRuns(projectedRuns, canonical.RunCount);
            var plan = new TMPTextRenderPlan
            {
                Text = canonical.Text,
                Runs = projectedRuns,
                RunCount = runCount,
                ComponentStyle = baseStyle,
                ComponentFontStyle = uniformFontStyle
                    ? GetTMPComponentFontStyle(in baseStyle)
                    : FontStyles.Normal,
                ComponentCharacterSpacing = uniformCharacterSpacing ? baseStyle.CharacterSpacing : 0f,
                EmitFontStyleTags = !uniformFontStyle,
                EmitCharacterSpacingTags = !uniformCharacterSpacing,
                EmitRunColors = emitRunColors,
                HasParagraphTags = HasTMPParagraphTags(in textInfo),
            };

            for (int i = 0; i < runCount; i++)
            {
                if (HasTMPTextTags(in projectedRuns[i].Style, in plan))
                {
                    plan.HasInlineTags = true;
                    break;
                }
            }
            if (!plan.HasInlineTags && canonical.Text.IndexOf('<') >= 0)
                plan.HasInlineTags = true;
            return plan;
        }

        private static bool HasTMPTextTags(in TMPTextRunStyle style, in TMPTextRenderPlan plan)
        {
            return (style.Font != null && style.Font != plan.ComponentStyle.Font)
                || (style.Material != null && style.Material != plan.ComponentStyle.Material)
                || style.FontSize != plan.ComponentStyle.FontSize
                || (plan.EmitRunColors && !AreColorsEqual(style.Color, plan.ComponentStyle.Color))
                || (plan.EmitCharacterSpacingTags && style.CharacterSpacing != 0f)
                || style.BaselineShift != 0f
                || style.HorizontalScale != 1f
                || (plan.EmitFontStyleTags
                    && (style.FontStyle != FontStyles.Normal
                        || style.Capitalization != PsdTextCapitalization.Normal))
                || style.NoBreak;
        }

        private static TMPTextRunStyle ProjectTMPTextStyle(
            in TextStyleRunInfo source,
            int sourceStyleIndex,
            TMP_FontAsset[] runFonts,
            Material[] runMaterials,
            bool emitRunColors)
        {
            const FontStyles inlineStyles = FontStyles.Bold
                | FontStyles.Italic
                | FontStyles.Underline
                | FontStyles.Strikethrough;
            return new TMPTextRunStyle
            {
                Font = sourceStyleIndex < runFonts.Length ? runFonts[sourceStyleIndex] : null,
                Material = runMaterials != null && sourceStyleIndex < runMaterials.Length
                    ? runMaterials[sourceStyleIndex]
                    : null,
                FontSize = QuantizeTMPValue(source.FontSize),
                Color = emitRunColors ? (Color32)source.Color : (Color32)Color.white,
                CharacterSpacing = QuantizeTMPCharacterSpacing(source.CharacterSpacing),
                BaselineShift = QuantizeTMPValue(source.BaselineShift),
                HorizontalScale = QuantizeTMPValue(source.HorizontalScale),
                FontStyle = source.TMPFontStyle & inlineStyles,
                Capitalization = source.Capitalization,
                NoBreak = source.NoBreak,
            };
        }

        private static float QuantizeTMPCharacterSpacing(float value)
        {
            return QuantizeTMPValue(QuantizeTMPValue(value * 0.01f) * 100f);
        }

        private static bool CanFoldTMPWhitespaceColor(in TMPTextRunStyle style)
        {
            return (style.FontStyle & (FontStyles.Underline | FontStyles.Strikethrough)) == 0;
        }

        private static bool AreTMPTextStylesEqual(in TMPTextRunStyle left, in TMPTextRunStyle right)
        {
            return AreTMPTextLayoutStylesEqual(in left, in right)
                && AreColorsEqual(left.Color, right.Color);
        }

        private static bool AreTMPTextLayoutStylesEqual(in TMPTextRunStyle left, in TMPTextRunStyle right)
        {
            return left.Font == right.Font
                && left.Material == right.Material
                && left.FontSize == right.FontSize
                && left.CharacterSpacing == right.CharacterSpacing
                && left.BaselineShift == right.BaselineShift
                && left.HorizontalScale == right.HorizontalScale
                && left.FontStyle == right.FontStyle
                && left.Capitalization == right.Capitalization
                && left.NoBreak == right.NoBreak;
        }

        private static int MergeTMPTextRuns(TMPTextRenderRun[] runs, int count)
        {
            int writeIndex = 0;
            for (int i = 0; i < count; i++)
            {
                TMPTextRenderRun run = runs[i];
                if (writeIndex > 0)
                {
                    ref TMPTextRenderRun previous = ref runs[writeIndex - 1];
                    if (previous.Start + previous.Length == run.Start
                        && AreTMPTextStylesEqual(in previous.Style, in run.Style))
                    {
                        previous.Length += run.Length;
                        continue;
                    }
                }
                runs[writeIndex++] = run;
            }
            return writeIndex;
        }

        private static FontStyles GetTMPComponentFontStyle(in TMPTextRunStyle style)
        {
            FontStyles result = style.FontStyle;
            if (style.Capitalization == PsdTextCapitalization.AllCaps)
                result |= FontStyles.UpperCase;
            else if (style.Capitalization == PsdTextCapitalization.SmallCaps)
                result |= FontStyles.SmallCaps;
            return result;
        }

        [Flags]
        private enum TMPParagraphTagFlags : byte
        {
            None = 0,
            Alignment = 1 << 0,
            Margin = 1 << 1,
            LineIndent = 1 << 2,
            LineHeight = 1 << 3,
        }

        private static bool HasTMPParagraphTags(in TextLayerInfo textInfo)
        {
            string text = textInfo.Text ?? string.Empty;
            var paragraphs = textInfo.ParagraphRuns;
            if (text.Length == 0 || paragraphs == null)
                return false;

            int offset = 0;
            for (int i = 0; i < paragraphs.Length && offset < text.Length; i++)
            {
                TextParagraphRunInfo paragraph = paragraphs[i];
                int start = Mathf.Clamp(paragraph.Start, offset, text.Length);
                int end = Mathf.Clamp(paragraph.Start + paragraph.Length, start, text.Length);
                if (GetTMPParagraphTagFlags(in textInfo, in paragraph, start, end) != TMPParagraphTagFlags.None)
                    return true;
                if (FindTrailingLineBreakStart(text, start, end) < end)
                {
                    float boundarySpacing = paragraph.SpaceAfter;
                    if (i + 1 < paragraphs.Length)
                        boundarySpacing += paragraphs[i + 1].SpaceBefore;
                    if (QuantizeTMPValue(boundarySpacing) != 0f)
                        return true;
                }
                offset = end;
            }
            return false;
        }

        private static TMPParagraphTagFlags GetTMPParagraphTagFlags(
            in TextLayerInfo textInfo,
            in TextParagraphRunInfo paragraph,
            int paragraphStart,
            int paragraphEnd)
        {
            TMPParagraphTagFlags flags = TMPParagraphTagFlags.None;
            if (paragraph.Justification != textInfo.Justification)
                flags |= TMPParagraphTagFlags.Alignment;
            if (QuantizeTMPValue(paragraph.StartIndent) != 0f
                || QuantizeTMPValue(paragraph.EndIndent) != 0f)
            {
                flags |= TMPParagraphTagFlags.Margin;
            }
            if (QuantizeTMPValue(paragraph.FirstLineIndent) != 0f)
                flags |= TMPParagraphTagFlags.LineIndent;
            float paragraphLineHeight = CalculateParagraphLineHeight(in textInfo, paragraphStart, paragraphEnd);
            if (QuantizeTMPValue(paragraphLineHeight) > 0f
                && QuantizeTMPValue(Mathf.Abs(paragraphLineHeight - GetUniformTMPLineHeight(in textInfo))) > 0f)
                flags |= TMPParagraphTagFlags.LineHeight;
            return flags;
        }

        private static float GetUniformTMPLineHeight(in TextLayerInfo textInfo)
        {
            float fontSize = Mathf.Max(1f, textInfo.FontSize);
            return textInfo.IsAutoLineSpacing || textInfo.LineSpacing <= 0f
                ? fontSize * 1.2f
                : textInfo.LineSpacing;
        }

        private static string SerializeTMPText(in TextLayerInfo textInfo, in TMPTextRenderPlan plan)
        {
            string sourceText = plan.Text;
            var builder = new StringBuilder(sourceText.Length + plan.RunCount * 48);
            var currentTags = new TMPTextTag[13];
            var nextTags = new TMPTextTag[13];
            int currentTagCount = 0;
            int runIndex = 0;
            int textOffset = 0;
            var paragraphRuns = textInfo.ParagraphRuns;
            bool emitLineHeightTags = plan.HasRichText;
            if (paragraphRuns == null || paragraphRuns.Length == 0)
            {
                AppendTMPStyledRange(
                    builder,
                    in plan,
                    0,
                    sourceText.Length,
                    ref runIndex,
                    ref currentTags,
                    ref nextTags,
                    ref currentTagCount);
                return builder.ToString();
            }

            for (int i = 0; i < paragraphRuns.Length && textOffset < sourceText.Length; i++)
            {
                TextParagraphRunInfo paragraph = paragraphRuns[i];
                int paragraphStart = Mathf.Clamp(paragraph.Start, textOffset, sourceText.Length);
                int paragraphEnd = Mathf.Clamp(paragraph.Start + paragraph.Length, paragraphStart, sourceText.Length);
                if (paragraphStart > textOffset)
                {
                    AppendTMPStyledRange(
                        builder,
                        in plan,
                        textOffset,
                        paragraphStart,
                        ref runIndex,
                        ref currentTags,
                        ref nextTags,
                        ref currentTagCount);
                }

                TMPParagraphTagFlags paragraphTags = AppendTMPParagraphOpening(
                    builder,
                    in textInfo,
                    in paragraph,
                    paragraphStart,
                    paragraphEnd,
                    emitLineHeightTags);
                int lineBreakStart = FindTrailingLineBreakStart(sourceText, paragraphStart, paragraphEnd);
                AppendTMPStyledRange(
                    builder,
                    in plan,
                    paragraphStart,
                    lineBreakStart,
                    ref runIndex,
                    ref currentTags,
                    ref nextTags,
                    ref currentTagCount);
                if (lineBreakStart < paragraphEnd)
                {
                    float boundarySpacing = paragraph.SpaceAfter;
                    if (i + 1 < paragraphRuns.Length)
                        boundarySpacing += paragraphRuns[i + 1].SpaceBefore;
                    bool hasBoundaryLineHeight = QuantizeTMPValue(boundarySpacing) != 0f;
                    if (hasBoundaryLineHeight)
                    {
                        float lineHeight = CalculateParagraphLineHeight(
                            in textInfo,
                            paragraphStart,
                            paragraphEnd);
                        builder.Append("<line-height=")
                            .Append(FormatTMPValue(Mathf.Max(0.01f, lineHeight + boundarySpacing)))
                            .Append("px>");
                    }

                    AppendTMPStyledRange(
                        builder,
                        in plan,
                        lineBreakStart,
                        paragraphEnd,
                        ref runIndex,
                        ref currentTags,
                        ref nextTags,
                        ref currentTagCount);
                    if (hasBoundaryLineHeight)
                        builder.Append("</line-height>");
                }
                AppendTMPParagraphClosing(builder, paragraphTags);
                textOffset = paragraphEnd;
            }

            if (textOffset < sourceText.Length)
            {
                AppendTMPStyledRange(
                    builder,
                    in plan,
                    textOffset,
                    sourceText.Length,
                    ref runIndex,
                    ref currentTags,
                    ref nextTags,
                    ref currentTagCount);
            }
            return builder.ToString();
        }

        private static TMPParagraphTagFlags AppendTMPParagraphOpening(
            StringBuilder builder,
            in TextLayerInfo textInfo,
            in TextParagraphRunInfo paragraph,
            int paragraphStart,
            int paragraphEnd,
            bool emitLineHeightTags)
        {
            TMPParagraphTagFlags flags = GetTMPParagraphTagFlags(
                in textInfo,
                in paragraph,
                paragraphStart,
                paragraphEnd);
            if ((flags & TMPParagraphTagFlags.Alignment) != 0)
                builder.Append("<align=").Append(GetTMPAlignmentTag(paragraph.Justification)).Append('>');
            if ((flags & TMPParagraphTagFlags.Margin) != 0)
            {
                if (QuantizeTMPValue(paragraph.StartIndent) != 0f)
                    builder.Append("<margin-left=").Append(FormatTMPValue(paragraph.StartIndent)).Append("px>");
                if (QuantizeTMPValue(paragraph.EndIndent) != 0f)
                    builder.Append("<margin-right=").Append(FormatTMPValue(paragraph.EndIndent)).Append("px>");
            }
            if ((flags & TMPParagraphTagFlags.LineIndent) != 0)
                builder.Append("<line-indent=").Append(FormatTMPValue(paragraph.FirstLineIndent)).Append("px>");
            if ((flags & TMPParagraphTagFlags.LineHeight) != 0 || emitLineHeightTags)
            {
                float lineHeight = CalculateParagraphLineHeight(in textInfo, paragraphStart, paragraphEnd);
                // 0px 是绝对行零高，会让多行叠在一起；算不出行高就交给 TMP 自己的 lineSpacing。
                if (lineHeight > 0f)
                {
                    flags |= TMPParagraphTagFlags.LineHeight;
                    builder.Append("<line-height=").Append(FormatTMPValue(lineHeight)).Append("px>");
                }
            }
            return flags;
        }

        private static void AppendTMPParagraphClosing(StringBuilder builder, TMPParagraphTagFlags flags)
        {
            if ((flags & TMPParagraphTagFlags.LineHeight) != 0)
                builder.Append("</line-height>");
            if ((flags & TMPParagraphTagFlags.LineIndent) != 0)
                builder.Append("</line-indent>");
            if ((flags & TMPParagraphTagFlags.Margin) != 0)
                builder.Append("</margin>");
            if ((flags & TMPParagraphTagFlags.Alignment) != 0)
                builder.Append("</align>");
        }

        private static int FindTrailingLineBreakStart(string text, int start, int end)
        {
            while (end > start && (text[end - 1] == '\r' || text[end - 1] == '\n'))
            {
                end--;
            }

            return end;
        }

        private static float CalculateParagraphLineHeight(
            in TextLayerInfo textInfo,
            int paragraphStart,
            int paragraphEnd)
        {
            float lineHeight = 0f;
            var runs = textInfo.StyleRuns;
            for (int i = 0; i < runs.Length; i++)
            {
                var run = runs[i];
                if (run.Start >= paragraphEnd || run.Start + run.Length <= paragraphStart)
                    continue;

                float runHeight = run.IsAutoLineSpacing
                    ? run.FontSize * 1.2f
                    : run.LineSpacing;
                lineHeight = Mathf.Max(lineHeight, runHeight);
            }
            return lineHeight;
        }

        private enum TMPTextTagKind : byte
        {
            Font,
            Material,
            Size,
            Color,
            CharacterSpacing,
            BaselineShift,
            HorizontalScale,
            Bold,
            Italic,
            Underline,
            Strikethrough,
            Uppercase,
            SmallCaps,
            NoBreak,
        }

        private struct TMPTextTag
        {
            public TMPTextTagKind Kind;
            public UnityEngine.Object Asset;
            public Material Material;
            public float Value;
            public Color32 Color;
        }

        private static void AppendTMPStyledRange(
            StringBuilder builder,
            in TMPTextRenderPlan plan,
            int rangeStart,
            int rangeEnd,
            ref int runIndex,
            ref TMPTextTag[] currentTags,
            ref TMPTextTag[] nextTags,
            ref int currentTagCount)
        {
            var runs = plan.Runs;
            while (runIndex < plan.RunCount && runs[runIndex].Start + runs[runIndex].Length <= rangeStart)
                runIndex++;

            int offset = rangeStart;
            while (offset < rangeEnd && runIndex < plan.RunCount)
            {
                TMPTextRenderRun run = runs[runIndex];
                int runStart = Mathf.Max(offset, run.Start);
                int runEnd = Mathf.Min(rangeEnd, run.Start + run.Length);
                if (runStart > offset)
                    AppendEscapedTMPText(builder, plan.Text, offset, runStart - offset);
                if (runEnd > runStart)
                {
                    int nextTagCount = FillTMPTextTags(in run.Style, in plan, nextTags);
                    int commonCount = 0;
                    while (commonCount < currentTagCount
                        && commonCount < nextTagCount
                        && AreTMPTextTagsEqual(in currentTags[commonCount], in nextTags[commonCount]))
                    {
                        commonCount++;
                    }
                    for (int i = currentTagCount - 1; i >= commonCount; i--)
                        AppendTMPTextTagClosing(builder, currentTags[i].Kind);
                    for (int i = commonCount; i < nextTagCount; i++)
                        AppendTMPTextTagOpening(builder, in nextTags[i]);

                    AppendEscapedTMPText(builder, plan.Text, runStart, runEnd - runStart);
                    TMPTextTag[] swap = currentTags;
                    currentTags = nextTags;
                    nextTags = swap;
                    currentTagCount = nextTagCount;
                    offset = runEnd;
                }
                if (run.Start + run.Length <= offset)
                    runIndex++;
            }

            if (offset < rangeEnd)
                AppendEscapedTMPText(builder, plan.Text, offset, rangeEnd - offset);
            for (int i = currentTagCount - 1; i >= 0; i--)
                AppendTMPTextTagClosing(builder, currentTags[i].Kind);
            currentTagCount = 0;
        }

        private static int FillTMPTextTags(
            in TMPTextRunStyle style,
            in TMPTextRenderPlan plan,
            TMPTextTag[] tags)
        {
            int count = 0;
            bool emitFont = style.Font != null && style.Font != plan.ComponentStyle.Font;
            bool emitMaterial = style.Material != null && style.Material != plan.ComponentStyle.Material;
            if (emitFont)
            {
                tags[count++] = new TMPTextTag
                {
                    Kind = TMPTextTagKind.Font,
                    Asset = style.Font,
                    Material = emitMaterial ? style.Material : null,
                };
            }
            else if (emitMaterial)
            {
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Material, Asset = style.Material };
            }
            if (style.FontSize != plan.ComponentStyle.FontSize)
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Size, Value = style.FontSize };
            if (plan.EmitRunColors && !AreColorsEqual(style.Color, plan.ComponentStyle.Color))
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Color, Color = style.Color };
            if (plan.EmitCharacterSpacingTags && style.CharacterSpacing != 0f)
            {
                tags[count++] = new TMPTextTag
                {
                    Kind = TMPTextTagKind.CharacterSpacing,
                    Value = QuantizeTMPValue(style.CharacterSpacing * 0.01f),
                };
            }
            if (style.BaselineShift != 0f)
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.BaselineShift, Value = style.BaselineShift };
            if (style.HorizontalScale != 1f)
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.HorizontalScale, Value = style.HorizontalScale };
            if (plan.EmitFontStyleTags)
            {
                if ((style.FontStyle & FontStyles.Bold) != 0)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Bold };
                if ((style.FontStyle & FontStyles.Italic) != 0)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Italic };
                if ((style.FontStyle & FontStyles.Underline) != 0)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Underline };
                if ((style.FontStyle & FontStyles.Strikethrough) != 0)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Strikethrough };
                if (style.Capitalization == PsdTextCapitalization.AllCaps)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.Uppercase };
                else if (style.Capitalization == PsdTextCapitalization.SmallCaps)
                    tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.SmallCaps };
            }
            if (style.NoBreak)
                tags[count++] = new TMPTextTag { Kind = TMPTextTagKind.NoBreak };
            return count;
        }

        private static bool AreTMPTextTagsEqual(in TMPTextTag left, in TMPTextTag right)
        {
            return left.Kind == right.Kind
                && left.Asset == right.Asset
                && left.Material == right.Material
                && left.Value == right.Value
                && AreColorsEqual(left.Color, right.Color);
        }

        private static void AppendTMPTextTagOpening(StringBuilder builder, in TMPTextTag tag)
        {
            switch (tag.Kind)
            {
                case TMPTextTagKind.Font:
                    builder.Append("<font=\"").Append(tag.Asset.name).Append('"');
                    if (tag.Material != null)
                        builder.Append(" material=\"").Append(tag.Material.name).Append('"');
                    builder.Append('>');
                    break;
                case TMPTextTagKind.Material:
                    builder.Append("<material=\"").Append(tag.Asset.name).Append("\">");
                    break;
                case TMPTextTagKind.Size:
                    builder.Append("<size=").Append(FormatTMPValue(tag.Value)).Append("px>");
                    break;
                case TMPTextTagKind.Color:
                    builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(tag.Color)).Append('>');
                    break;
                case TMPTextTagKind.CharacterSpacing:
                    builder.Append("<cspace=").Append(FormatTMPValue(tag.Value)).Append("em>");
                    break;
                case TMPTextTagKind.BaselineShift:
                    builder.Append("<voffset=").Append(FormatTMPValue(tag.Value)).Append("px>");
                    break;
                case TMPTextTagKind.HorizontalScale:
                    builder.Append("<scale=").Append(FormatTMPValue(tag.Value)).Append('>');
                    break;
                case TMPTextTagKind.Bold:
                    builder.Append("<b>");
                    break;
                case TMPTextTagKind.Italic:
                    builder.Append("<i>");
                    break;
                case TMPTextTagKind.Underline:
                    builder.Append("<u>");
                    break;
                case TMPTextTagKind.Strikethrough:
                    builder.Append("<s>");
                    break;
                case TMPTextTagKind.Uppercase:
                    builder.Append("<uppercase>");
                    break;
                case TMPTextTagKind.SmallCaps:
                    builder.Append("<smallcaps>");
                    break;
                case TMPTextTagKind.NoBreak:
                    builder.Append("<nobr>");
                    break;
            }
        }

        private static void AppendTMPTextTagClosing(StringBuilder builder, TMPTextTagKind kind)
        {
            switch (kind)
            {
                case TMPTextTagKind.Font:
                    builder.Append("</font>");
                    break;
                case TMPTextTagKind.Material:
                    builder.Append("</material>");
                    break;
                case TMPTextTagKind.Size:
                    builder.Append("</size>");
                    break;
                case TMPTextTagKind.Color:
                    builder.Append("</color>");
                    break;
                case TMPTextTagKind.CharacterSpacing:
                    builder.Append("</cspace>");
                    break;
                case TMPTextTagKind.BaselineShift:
                    builder.Append("</voffset>");
                    break;
                case TMPTextTagKind.HorizontalScale:
                    builder.Append("</scale>");
                    break;
                case TMPTextTagKind.Bold:
                    builder.Append("</b>");
                    break;
                case TMPTextTagKind.Italic:
                    builder.Append("</i>");
                    break;
                case TMPTextTagKind.Underline:
                    builder.Append("</u>");
                    break;
                case TMPTextTagKind.Strikethrough:
                    builder.Append("</s>");
                    break;
                case TMPTextTagKind.Uppercase:
                    builder.Append("</uppercase>");
                    break;
                case TMPTextTagKind.SmallCaps:
                    builder.Append("</smallcaps>");
                    break;
                case TMPTextTagKind.NoBreak:
                    builder.Append("</nobr>");
                    break;
            }
        }

        private static void AppendEscapedTMPText(
            StringBuilder builder,
            string text,
            int start,
            int length)
        {
            int end = start + length;
            int chunkStart = start;
            for (int i = start; i < end; i++)
            {
                if (text[i] != '<')
                    continue;

                if (i > chunkStart)
                    builder.Append(text, chunkStart, i - chunkStart);
                builder.Append("<noparse><</noparse>");
                chunkStart = i + 1;
            }
            if (chunkStart < end)
                builder.Append(text, chunkStart, end - chunkStart);
        }

        private static string FormatTMPValue(float value)
        {
            return QuantizeTMPValue(value).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float QuantizeTMPValue(float value)
        {
            float quantized = Mathf.Round(value * 1000f) * 0.001f;
            return Mathf.Abs(quantized) < 0.0005f ? 0f : quantized;
        }

        private static string GetTMPAlignmentTag(PsdTextJustification justification)
        {
            switch (justification)
            {
                case PsdTextJustification.Right:
                    return "right";
                case PsdTextJustification.Center:
                    return "center";
                case PsdTextJustification.JustifyAll:
                    return "flush";
                case PsdTextJustification.JustifyLastLeft:
                case PsdTextJustification.JustifyLastRight:
                case PsdTextJustification.JustifyLastCenter:
                    return "justified";
                default:
                    return "left";
            }
        }

        private static HorizontalAlignmentOptions ConvertPsdAlignment(PsdTextJustification justification)
        {
            switch (justification)
            {
                case PsdTextJustification.Right:
                    return HorizontalAlignmentOptions.Right;
                case PsdTextJustification.Center:
                    return HorizontalAlignmentOptions.Center;
                case PsdTextJustification.JustifyAll:
                    return HorizontalAlignmentOptions.Flush;
                case PsdTextJustification.JustifyLastLeft:
                case PsdTextJustification.JustifyLastRight:
                case PsdTextJustification.JustifyLastCenter:
                    return HorizontalAlignmentOptions.Justified;
                default:
                    return HorizontalAlignmentOptions.Left;
            }
        }

        private static TextAnchor ConvertPsdAlignment(
            TextAnchor current,
            PsdTextJustification justification)
        {
            int column;
            switch (justification)
            {
                case PsdTextJustification.Right:
                case PsdTextJustification.JustifyLastRight:
                    column = 2;
                    break;
                case PsdTextJustification.Center:
                case PsdTextJustification.JustifyLastCenter:
                    column = 1;
                    break;
                default:
                    column = 0;
                    break;
            }
            return (TextAnchor)(((int)current / 3) * 3 + column);
        }

        private static Vector4 ResolveTMPParagraphMargins(in TextLayerInfo textInfo)
        {
            var runs = textInfo.ParagraphRuns;
            if (runs == null || runs.Length == 0)
            {
                return Vector4.zero;
            }

            return new Vector4(
                0f,
                runs[0].SpaceBefore,
                0f,
                runs[runs.Length - 1].SpaceAfter);
        }
        private static float ConvertPsdLeadingToUGUILineSpacing(UnityEngine.UI.Text text, in TextLayerInfo textInfo)
        {
            float baseLineHeight = Mathf.Max(1f, textInfo.FontSize);
            var font = text != null ? text.font : null;
            if (font != null && font.fontSize > 0 && font.lineHeight > 0)
            {
                baseLineHeight = font.lineHeight * (textInfo.FontSize / (float)font.fontSize);
            }

            float targetLineHeight = textInfo.IsAutoLineSpacing || textInfo.LineSpacing <= 0f
                ? textInfo.FontSize * 1.2f
                : textInfo.LineSpacing;
            float spacingFactor = targetLineHeight / Mathf.Max(1f, baseLineHeight);
            if (float.IsNaN(spacingFactor) || float.IsInfinity(spacingFactor))
            {
                return 1f;
            }

            return Mathf.Max(0.01f, spacingFactor);
        }
        private static float ConvertPsdLeadingToTMPLineSpacing(TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            float fontSize = Mathf.Max(1f, textInfo.FontSize);
            float baseLineHeight = fontSize;
            var font = text != null ? text.font : null;
            if (font != null)
            {
                var faceInfo = font.faceInfo;
                if (faceInfo.pointSize > 0f && faceInfo.lineHeight > 0f)
                {
                    float scale = fontSize / faceInfo.pointSize;
                    baseLineHeight = faceInfo.lineHeight * scale;
                }
            }

            float targetLineHeight = textInfo.IsAutoLineSpacing || textInfo.LineSpacing <= 0f
                ? fontSize * 1.2f
                : textInfo.LineSpacing;
            float additionalSpacing = targetLineHeight - baseLineHeight;
            float spacing = additionalSpacing * 100f / fontSize;
            if (float.IsNaN(spacing) || float.IsInfinity(spacing))
            {
                return 0f;
            }

            // Avoid tiny negative jitter caused by metric mismatch when PS uses auto leading.
            if (spacing < 0f && spacing > -0.25f)
            {
                return 0f;
            }

            return spacing;
        }
        private static void RemoveLegacyTextEffects(UnityEngine.UI.Graphic textCom)
        {
            if (textCom == null) return;
            var effects = textCom.GetComponents<Shadow>();
            for (int i = 0; i < effects.Length; i++)
            {
                var fx = effects[i];
                if (fx != null)
                {
                    GameObject.DestroyImmediate(fx);
                }
            }
            // Outline 与 Shadow 成对清理：复用同一个 Text 节点时不能逐轮叠加，PSD 去掉描边后也不能残留。
            var outlines = textCom.GetComponents<Outline>();
            for (int i = 0; i < outlines.Length; i++)
            {
                var outline = outlines[i];
                if (outline != null)
                {
                    GameObject.DestroyImmediate(outline);
                }
            }
        }
        private static void ApplyUGUITextEffects(UnityEngine.UI.Text text, in TextLayerInfo textInfo)
        {
            if (text == null) return;
            RemoveLegacyTextEffects(text);

            if (textInfo.HasOutline)
            {
                var outline = text.gameObject.AddComponent<Outline>();
                outline.enabled = true;
                float distance = textInfo.OutlineSize;
                outline.effectColor = textInfo.OutlineColor;
                outline.effectDistance = new Vector2(distance, distance);
                outline.useGraphicAlpha = true;
            }

            // Legacy uGUI Text cannot represent inner shadows, so only apply outer shadows here.
            if (textInfo.HasShadow && !textInfo.ShadowIsInner)
            {
                var shadow = text.gameObject.AddComponent<Shadow>();
                shadow.enabled = true;
                shadow.effectColor = textInfo.ShadowColor;
                shadow.effectDistance = textInfo.ShadowOffset;
                shadow.useGraphicAlpha = true;
            }
        }
        private enum TMPGradientOutput
        {
            None = 0,
            Native = 1,
            Texture = 2,
        }
        private static void ApplyTMPTextGradientMapping(
            TextMeshProUGUI text,
            Vector2 effectSize,
            TMPGradientOutput output,
            in TextLayerInfo textInfo)
        {
            if (text == null)
                return;

            bool useNative = output == TMPGradientOutput.Native;
            bool useTexture = output == TMPGradientOutput.Texture;
            text.enableVertexGradient = useNative;
            text.colorGradientPreset = null;
            if (useNative)
            {
                float width = Mathf.Max(1f, Mathf.Abs(effectSize.x));
                float height = Mathf.Max(1f, Mathf.Abs(effectSize.y));
                float topT = EvaluateTMPGradientCoordinate(0.5f, 1f, width, height, in textInfo);
                float bottomT = EvaluateTMPGradientCoordinate(0.5f, 0f, width, height, in textInfo);
                Color top = CompositeTMPGradient(textInfo.Color, EvaluateTMPGradientColor(topT, in textInfo), in textInfo);
                Color bottom = CompositeTMPGradient(textInfo.Color, EvaluateTMPGradientColor(bottomT, in textInfo), in textInfo);
                text.colorGradient = new VertexGradient(top, top, bottom, bottom);
            }
            else
            {
                text.colorGradient = new VertexGradient(Color.white);
            }
            text.horizontalMapping = useTexture ? TextureMappingOptions.Paragraph : TextureMappingOptions.Character;
            text.verticalMapping = useTexture ? TextureMappingOptions.Paragraph : TextureMappingOptions.Character;
        }
        private static bool HasUsableTMPGradientStops(in TextLayerInfo textInfo)
        {
            return textInfo.HasGradient && textInfo.GradientStops != null && textInfo.GradientStops.Length >= 2;
        }
        private static bool HasTMPMaterialEffects(in TextLayerInfo textInfo)
        {
            return textInfo.HasOutline
                || textInfo.HasShadow
                || textInfo.HasGlow
                || textInfo.HasBevel
                || ResolveTMPGradientOutput(in textInfo) == TMPGradientOutput.Texture;
        }
        private static TMPGradientOutput ResolveTMPGradientOutput(in TextLayerInfo textInfo)
        {
            if (!HasUsableTMPGradientStops(in textInfo))
                return TMPGradientOutput.None;

            TMPGradientConversionMode mode = Instance != null
                ? Instance.TMPGradientMode
                : TMPGradientConversionMode.Auto;
            if (mode == TMPGradientConversionMode.ExactTexture)
                return TMPGradientOutput.Texture;
            if (mode == TMPGradientConversionMode.EditableNative)
                return TMPGradientOutput.Native;
            return CanUseNativeTMPGradient(in textInfo)
                ? TMPGradientOutput.Native
                : TMPGradientOutput.Texture;
        }
        internal static bool CanUseNativeTMPGradient(in TextLayerInfo textInfo)
        {
            if (!HasUsableTMPGradientStops(in textInfo)
                || textInfo.GradientStops.Length != 2
                || textInfo.GradientDither
                || !textInfo.GradientAlignWithLayer
                || Mathf.Abs(textInfo.GradientScale - 1f) > 0.001f
                || textInfo.GradientOffset.sqrMagnitude > 0.000001f)
            {
                return false;
            }

            string style = (textInfo.GradientStyleKey ?? string.Empty).Trim();
            if (style.Length > 0
                && !string.Equals(style, "Lnr", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(style, "Linear", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string interpolation = (textInfo.GradientInterpolationKey ?? string.Empty).Trim();
            if (interpolation.Length > 0
                && !string.Equals(interpolation, "Clsc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(interpolation, "Classic", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (textInfo.GradientStops[0].Location > 0.0001f
                || textInfo.GradientStops[1].Location < 0.9999f)
            {
                return false;
            }

            float horizontal = Mathf.Abs(Mathf.Cos(textInfo.GradientAngle * Mathf.Deg2Rad));
            return horizontal < 0.001f;
        }
        private static void ApplyTMPTextEffects(
            TextMeshProUGUI text,
            Vector2 effectSize,
            in TextLayerInfo textInfo)
        {
            if (text == null) return;
            RemoveLegacyTextEffects(text);

            var baseMaterial = ResolveTMPBaseMaterial(text);
            if (baseMaterial == null)
            {
                return;
            }

            if (!HasTMPMaterialEffects(in textInfo))
            {
                if (text.fontSharedMaterial != baseMaterial)
                {
                    text.fontSharedMaterial = baseMaterial;
                    text.UpdateMeshPadding();
                    text.SetMaterialDirty();
                }
                return;
            }

            var effectMaterial = GetOrCreateTMPEffectMaterial(
                text.font,
                baseMaterial,
                text.fontSize,
                textInfo.Color,
                effectSize,
                in textInfo);
            if (effectMaterial != null && text.fontSharedMaterial != effectMaterial)
            {
                text.fontSharedMaterial = effectMaterial;
                text.UpdateMeshPadding();
                text.havePropertiesChanged = true;
                text.SetMaterialDirty();
            }
        }
        private static Material ResolveTMPBaseMaterial(TextMeshProUGUI text)
        {
            var current = text.fontSharedMaterial;
            var fontMaterial = text.font != null ? EnsureTMPFontAssetResources(text.font) : null;
            if (current != null && tmpEffectMaterialBaseLookup.TryGetValue(GetObjectId(current), out var baseMaterial) && baseMaterial != null)
            {
                if (fontMaterial != null && HasSameTMPAtlas(baseMaterial, fontMaterial))
                {
                    return baseMaterial;
                }
            }
            if (current != null && fontMaterial != null && HasSameTMPAtlas(current, fontMaterial))
            {
                return current;
            }
            return fontMaterial;
        }
        private static bool HasSameTMPAtlas(Material a, Material b)
        {
            if (a == null || b == null) return false;
            var texA = a.GetTexture(ShaderUtilities.ID_MainTex);
            var texB = b.GetTexture(ShaderUtilities.ID_MainTex);
            if (texA == null || texB == null) return false;
            return GetObjectId(texA) == GetObjectId(texB);
        }
        private static Material GetOrCreateTMPEffectMaterial(
            TMP_FontAsset fontAsset,
            Material baseMaterial,
            float fontSize,
            Color baseColor,
            Vector2 effectSize,
            in TextLayerInfo textInfo)
        {
            if (fontAsset != null)
            {
                var repairedBaseMaterial = EnsureTMPFontAssetResources(fontAsset);
                if (repairedBaseMaterial != null)
                {
                    baseMaterial = repairedBaseMaterial;
                }
            }
            if (baseMaterial == null) return null;
            fontSize = Mathf.Max(1f, fontSize);
#if !UNITY_6000_6_OR_NEWER
            ShaderUtilities.GetShaderPropertyIDs();
#endif

            float gradientScale = GetTMPMaterialGradientScale(baseMaterial);
            float atlasPointSize = fontAsset != null && fontAsset.faceInfo.pointSize > 0f
                ? fontAsset.faceInfo.pointSize
                : fontSize;
            float renderedFontScale = Mathf.Max(0.01f, fontSize / atlasPointSize);
            float effectPixelRange = gradientScale * renderedFontScale;
            // Photoshop effects live in screen-pixel distance space. In TMP the same
            // distance is property * ScaleRatio * GradientScale * renderedFontScale.
            float faceSoftness = 0f;
            SolveTMPOutlineProperties(
                baseMaterial,
                effectPixelRange,
                textInfo.HasOutline ? textInfo.TMPOutlineSize : 0f,
                textInfo.TMPOutlinePosition,
                out float outlineWidth,
                out float faceDilate);
            float outlineFaceDilate = faceDilate;
            float shadowOffsetX = 0f;
            float shadowOffsetY = 0f;
            float shadowDilate = 0f;
            float shadowSoftness = 0f;
            bool shadowIsInner = textInfo.HasShadow && textInfo.ShadowIsInner;
            TextGlowEffectInfo outerGlow = textInfo.OuterGlow;
            TextGlowEffectInfo innerGlow = textInfo.InnerGlow;
            TMPTextGlowSolution glowSolution = TMPTextEffectParameterSolver.Solve(
                baseColor,
                effectPixelRange,
                in outerGlow,
                in innerGlow,
                !textInfo.HasShadow);
            bool usesInnerGlowComposite = glowSolution.HasInnerGlowComposite;
            bool glowUsesUnderlay = glowSolution.HasUnderlayGlow;
            bool hasUnderlay = textInfo.HasShadow || glowUsesUnderlay || usesInnerGlowComposite;
            bool underlayIsInner = textInfo.HasShadow && shadowIsInner;
            Color underlayColor = textInfo.HasShadow
                ? textInfo.ShadowColor
                : usesInnerGlowComposite
                    ? glowSolution.InnerGlowComposite.UnderlayColor
                    : glowSolution.UnderlayGlow.MaterialColor;
            if (textInfo.HasShadow)
            {
                float solidPixels = textInfo.ShadowSoftness * Mathf.Clamp01(textInfo.ShadowSpread);
                float falloffPixels = Mathf.Max(0f, textInfo.ShadowSoftness - solidPixels);
                Vector2 offset = shadowIsInner ? -textInfo.ShadowOffset : textInfo.ShadowOffset;
                float dilatePixels = (shadowIsInner ? -1f : 1f) * 2f * solidPixels;
                float softnessPixels = Mathf.Max(0f, 2f * falloffPixels - 1f);
                SolveTMPUnderlayProperties(
                    baseMaterial,
                    faceDilate,
                    outlineWidth,
                    faceSoftness,
                    effectPixelRange,
                    offset,
                    dilatePixels,
                    softnessPixels,
                    out shadowOffsetX,
                    out shadowOffsetY,
                    out shadowDilate,
                    out shadowSoftness);
            }
            else if (usesInnerGlowComposite)
            {
                SolveTMPInnerGlowCompositeProperties(
                    baseMaterial,
                    outlineWidth,
                    outlineFaceDilate,
                    effectPixelRange,
                    in innerGlow,
                    in glowSolution.InnerGlowComposite,
                    out faceDilate,
                    out faceSoftness,
                    out shadowDilate,
                    out shadowSoftness);
            }
            else if (glowUsesUnderlay)
            {
                TMPTextGlowFit underlayFit = glowSolution.UnderlayGlow;
                SolveTMPUnderlayProperties(
                    baseMaterial,
                    faceDilate,
                    outlineWidth,
                    faceSoftness,
                    effectPixelRange,
                    Vector2.zero,
                    underlayFit.UnderlayDilatePixels,
                    underlayFit.UnderlaySoftnessPixels,
                    out shadowOffsetX,
                    out shadowOffsetY,
                    out shadowDilate,
                    out shadowSoftness);
            }
            float glowInner = 0f;
            float glowOuter = 0f;
            float glowOffset = 0f;
            float glowPower = 0f;
            TMPTextGlowFit shaderGlow = glowSolution.ShaderGlow;
            Color tmpGlowColor = shaderGlow.MaterialColor;
            bool hasShaderGlow = glowSolution.HasShaderGlow;
            bool shaderGlowIsInner = hasShaderGlow && shaderGlow.IsInner;
            if (hasShaderGlow)
            {
                glowPower = shaderGlow.GlowPower;
                glowInner = shaderGlow.IsInner
                    ? Mathf.Clamp01(2f * shaderGlow.GlowKernelPixels / Mathf.Max(0.01f, effectPixelRange))
                    : 0f;
                SolveTMPGlowProperties(
                    baseMaterial,
                    faceDilate,
                    outlineWidth,
                    faceSoftness,
                    effectPixelRange,
                    shaderGlow.GlowOffsetPixels,
                    shaderGlow.IsInner ? 0f : shaderGlow.GlowKernelPixels,
                    out glowOffset,
                    out glowOuter);
            }
            float bevelSize = textInfo.HasBevel ? Mathf.Max(0f, textInfo.BevelSize) : 0f;
            float bevelSoften = textInfo.HasBevel ? Mathf.Max(0f, textInfo.BevelSoften) : 0f;
            // Independent smoothing kernels add in quadrature. This preserves bevel
            // support while reducing slope by the same geometric ratio.
            float effectiveBevelSize = Mathf.Sqrt(bevelSize * bevelSize + bevelSoften * bevelSoften);
            float bevelSlopeAttenuation = effectiveBevelSize > 0.0001f ? bevelSize / effectiveBevelSize : 0f;
            float bevelWidth = textInfo.HasBevel
                ? Mathf.Clamp(effectiveBevelSize / Mathf.Max(0.01f, effectPixelRange) - outlineWidth, -0.5f, 0.5f)
                : 0f;
            // GetSurfaceNormal produces a maximum tangent slope of 2 * _Bevel.
            // Photoshop Depth describes that slope; Altitude belongs to lighting,
            // not to the height field, and is fitted below against TMP's fixed 45° light.
            float bevelSurfaceSlope = Mathf.Min(2f, Mathf.Max(0f, textInfo.BevelDepth) * bevelSlopeAttenuation);
            float bevelAmount = textInfo.HasBevel ? Mathf.Clamp01(0.5f * bevelSurfaceSlope) : 0f;
            float bevelOffset = 0f;
            float bevelClamp = 0f;
            float bevelRoundness = textInfo.HasBevel && IsPSDSmoothBevelTechnique(textInfo.BevelTechniqueKey) ? 1f : 0f;
            float lightAngle = textInfo.HasBevel ? ConvertPSDBevelAngleToTMPLightAngle(textInfo.BevelAngle) : 0f;
            if (textInfo.HasBevel && !IsPSDBevelDirectionDown(textInfo.BevelDirectionKey))
                lightAngle = Mathf.Repeat(lightAngle + Mathf.PI, Mathf.PI * 2f);
            float bevelShaderFlags = textInfo.HasBevel && !textInfo.BevelIsInner ? 1f : 0f;
            Color bevelSpecularColor = Color.clear;
            Color bevelReflectFaceColor = Color.black;
            Color bevelReflectOutlineColor = Color.black;
            float bevelSpecularPower = 0f;
            float bevelReflectivity = 10f;
            float bevelDiffuse = 0f;
            float bevelAmbient = 1f;
            if (textInfo.HasBevel)
            {
                bevelSpecularColor = CompensateTMPAdditiveEffectColor(
                    baseColor,
                    new Color(textInfo.BevelHighlightColor.r, textInfo.BevelHighlightColor.g, textInfo.BevelHighlightColor.b, 1f),
                    textInfo.BevelHighlightBlendModeKey);
                FitTMPBevelMaterial(
                    baseColor,
                    bevelSurfaceSlope,
                    textInfo.BevelAltitude,
                    bevelSpecularColor,
                    textInfo.BevelHighlightOpacity,
                    textInfo.BevelShadowColor,
                    textInfo.BevelShadowOpacity,
                    textInfo.BevelShadowBlendModeKey,
                    textInfo.BevelGlossContour,
                    out bevelAmbient,
                    out bevelDiffuse,
                    out bevelReflectivity,
                    out bevelSpecularPower);
            }
            Color32 outlineColor = textInfo.OutlineColor;
            Color32 shadowColor = underlayColor;
            Color32 glowColor = tmpGlowColor;

            int ow = Mathf.RoundToInt(outlineWidth * 10000f);
            int fd = Mathf.RoundToInt(faceDilate * 10000f);
            int sx = Mathf.RoundToInt(shadowOffsetX * 10000f);
            int sy = Mathf.RoundToInt(shadowOffsetY * 10000f);
            int od = Mathf.RoundToInt(faceSoftness * 10000f);
            int sd = Mathf.RoundToInt(shadowDilate * 10000f);
            int ss = Mathf.RoundToInt(shadowSoftness * 10000f);
            int go = Mathf.RoundToInt(glowOuter * 10000f);
            int gi = Mathf.RoundToInt(glowInner * 10000f);
            int gOfs = Mathf.RoundToInt(glowOffset * 10000f);
            int gp = Mathf.RoundToInt(glowPower * 10000f);
            int ba = Mathf.RoundToInt(bevelAmount * 10000f);
            int bo = Mathf.RoundToInt(bevelOffset * 10000f);
            int bw = Mathf.RoundToInt(bevelWidth * 10000f);
            int bc = Mathf.RoundToInt(bevelClamp * 10000f);
            int br = Mathf.RoundToInt(bevelRoundness * 10000f);
            int la = Mathf.RoundToInt(lightAngle * 10000f);
            Color32 bevelSpecularColor32 = bevelSpecularColor;
            int bsp = Mathf.RoundToInt(bevelSpecularPower * 10000f);
            int brv = Mathf.RoundToInt(bevelReflectivity * 10000f);
            int bdf = Mathf.RoundToInt(bevelDiffuse * 10000f);
            int bam = Mathf.RoundToInt(bevelAmbient * 10000f);
            int bf = Mathf.RoundToInt(bevelShaderFlags * 10000f);
            bool hasGradient = ResolveTMPGradientOutput(in textInfo) == TMPGradientOutput.Texture;
            string gradientSignature = hasGradient
                ? BuildTMPGradientSignature(baseColor, effectSize, in textInfo)
                : "0";
            Texture2D gradientTexture = hasGradient
                ? GetOrCreateTMPGradientTexture(fontAsset, gradientSignature, baseColor, effectSize, in textInfo)
                : null;
            string effectSignature =
                $"o:{(textInfo.HasOutline ? 1 : 0)}|op:{(int)textInfo.TMPOutlinePosition}|ow:{ow}|fd:{fd}|os:{od}|oc:{outlineColor.r},{outlineColor.g},{outlineColor.b},{outlineColor.a}" +
                $"|s:{(hasUnderlay ? 1 : 0)}|sg:{(glowUsesUnderlay ? 1 : 0)}|si:{(underlayIsInner ? 1 : 0)}|so:{sx},{sy}|sd:{sd}|ss:{ss}|sc:{shadowColor.r},{shadowColor.g},{shadowColor.b},{shadowColor.a}" +
                $"|g:{(hasShaderGlow ? 1 : 0)}|gi:{(shaderGlowIsInner ? 1 : 0)}|go:{go}|giw:{gi}|gof:{gOfs}|gp:{gp}|gc:{glowColor.r},{glowColor.g},{glowColor.b},{glowColor.a}" +
                $"|b:{(textInfo.HasBevel ? 1 : 0)}|bi:{(textInfo.BevelIsInner ? 1 : 0)}|ba:{ba}|bo:{bo}|bw:{bw}|bc:{bc}|br:{br}|la:{la}|bsc:{bevelSpecularColor32.r},{bevelSpecularColor32.g},{bevelSpecularColor32.b},{bevelSpecularColor32.a}|bsp:{bsp}|brv:{brv}|bdf:{bdf}|bam:{bam}|bf:{bf}" +
                $"|f:{gradientSignature}";
            string key = $"{GetObjectIdKey(fontAsset)}|{effectSignature}";

            if (tmpEffectMaterialCache.TryGetValue(key, out var cachedMaterial) && cachedMaterial != null)
            {
                EnsureTMPMaterialHasAtlas(cachedMaterial, fontAsset);
                return cachedMaterial;
            }

            string materialName = BuildTMPEffectMaterialName(fontAsset, baseMaterial, in textInfo);
            var materialAsset = LoadPersistentTMPEffectMaterial(fontAsset, effectSignature);
            if (materialAsset != null)
            {
                EnsureTMPMaterialHasAtlas(materialAsset, fontAsset);
                ApplyTMPEffectMaterialProperties(materialAsset, outlineWidth, faceDilate, faceSoftness, outlineColor, textInfo.HasOutline,
                    shadowOffsetX, shadowOffsetY, shadowDilate, shadowSoftness, shadowColor, hasUnderlay, underlayIsInner,
                    glowColor, glowOffset, glowInner, glowOuter, glowPower, hasShaderGlow,
                    bevelAmount, bevelOffset, bevelWidth, bevelClamp, bevelRoundness, lightAngle,
                    bevelSpecularColor, bevelReflectFaceColor, bevelReflectOutlineColor, bevelSpecularPower, bevelReflectivity, bevelDiffuse, bevelAmbient,
                    bevelShaderFlags, textInfo.HasBevel, gradientTexture, hasGradient);
                EditorUtility.SetDirty(materialAsset);
                tmpEffectMaterialCache[key] = materialAsset;
                tmpEffectMaterialBaseLookup[GetObjectId(materialAsset)] = baseMaterial;
                return materialAsset;
            }

            var mat = CreateTMPMaterialCopy(baseMaterial, true);
            if (mat == null)
            {
                return null;
            }
            mat.name = materialName;
            EnsureTMPMaterialHasAtlas(mat, fontAsset);
            ApplyTMPEffectMaterialProperties(mat, outlineWidth, faceDilate, faceSoftness, outlineColor, textInfo.HasOutline,
                shadowOffsetX, shadowOffsetY, shadowDilate, shadowSoftness, shadowColor, hasUnderlay, underlayIsInner,
                glowColor, glowOffset, glowInner, glowOuter, glowPower, hasShaderGlow,
                bevelAmount, bevelOffset, bevelWidth, bevelClamp, bevelRoundness, lightAngle,
                bevelSpecularColor, bevelReflectFaceColor, bevelReflectOutlineColor, bevelSpecularPower, bevelReflectivity, bevelDiffuse, bevelAmbient,
                bevelShaderFlags, textInfo.HasBevel, gradientTexture, hasGradient);

            if (fontAsset != null)
            {
                PersistTMPEffectMaterial(fontAsset, mat, effectSignature);
            }

            tmpEffectMaterialCache[key] = mat;
            tmpEffectMaterialBaseLookup[GetObjectId(mat)] = baseMaterial;
            return mat;
        }
        private static float GetTMPMaterialGradientScale(Material mat)
        {
            if (mat == null || !mat.HasProperty(ShaderUtilities.ID_GradientScale))
            {
                return 10f;
            }

            return Mathf.Max(1f, mat.GetFloat(ShaderUtilities.ID_GradientScale));
        }
        private static float GetTMPMaterialScaleRatio(Material mat, int propertyId)
        {
            return mat != null && mat.HasProperty(propertyId)
                ? Mathf.Max(0.0001f, mat.GetFloat(propertyId))
                : 1f;
        }
        private static void SolveTMPOutlineProperties(
            Material baseMaterial,
            float effectPixelRange,
            float targetPixels,
            TextLayerInfo.TMPOutlineMode position,
            out float outlineWidth,
            out float faceDilate)
        {
            var probe = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            float positionFactor = position == TextLayerInfo.TMPOutlineMode.Inside
                ? -1f
                : position == TextLayerInfo.TMPOutlineMode.Outside ? 1f : 0f;
            float low = 0f;
            float high = 1f;
            targetPixels = Mathf.Max(0f, targetPixels);
            for (int i = 0; i < 24; i++)
            {
                float candidate = (low + high) * 0.5f;
                float effectivePixels = EvaluateTMPOutlinePixels(
                    probe,
                    candidate,
                    positionFactor,
                    effectPixelRange);
                if (effectivePixels < targetPixels)
                    low = candidate;
                else
                    high = candidate;
            }

            outlineWidth = targetPixels > 0f ? (low + high) * 0.5f : 0f;
            faceDilate = outlineWidth * positionFactor;
            UnityEngine.Object.DestroyImmediate(probe);
        }
        private static float EvaluateTMPOutlinePixels(
            Material probe,
            float outlineWidth,
            float positionFactor,
            float effectPixelRange)
        {
            SetTMPPrimaryProperties(probe, outlineWidth * positionFactor, outlineWidth, 0f);
            ShaderUtilities.UpdateShaderRatios(probe);
            float ratio = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_A);
            return outlineWidth * ratio * effectPixelRange;
        }
        private static void SetTMPPrimaryProperties(
            Material material,
            float faceDilate,
            float outlineWidth,
            float outlineSoftness)
        {
            if (material.HasProperty(TmpFaceDilatePropertyId))
                material.SetFloat(TmpFaceDilatePropertyId, faceDilate);
            if (material.HasProperty(ShaderUtilities.ID_OutlineWidth))
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            if (material.HasProperty(ShaderUtilities.ID_OutlineSoftness))
                material.SetFloat(ShaderUtilities.ID_OutlineSoftness, outlineSoftness);
        }
        private static void SolveTMPInnerGlowCompositeProperties(
            Material baseMaterial,
            float outlineWidth,
            float outlineFaceDilate,
            float effectPixelRange,
            in TextGlowEffectInfo innerGlow,
            in TMPTextInnerGlowCompositeFit seed,
            out float faceDilate,
            out float faceSoftness,
            out float underlayDilate,
            out float underlaySoftness)
        {
            var probe = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            SetTMPPrimaryProperties(probe, outlineFaceDilate, outlineWidth, 0f);
            SetTMPUnderlayProbe(probe, 0f, 0f, 0f, 0f);
            ShaderUtilities.UpdateShaderRatios(probe);

            float ratioA = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_A);
            float ratioC = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_C);
            float inverseRange = 1f / Mathf.Max(0.01f, effectPixelRange);
            float maxFaceDilate = Mathf.Min(1f, outlineFaceDilate);
            faceDilate = Mathf.Clamp(
                outlineFaceDilate - 2f * seed.FaceInsetPixels * inverseRange / ratioA,
                -1f,
                maxFaceDilate);
            faceSoftness = Mathf.Clamp01(seed.FaceSoftnessPixels * inverseRange / ratioA);
            underlayDilate = Mathf.Clamp(seed.UnderlayDilatePixels * inverseRange * 2f / ratioC, -1f, 1f);
            underlaySoftness = Mathf.Clamp01(seed.UnderlaySoftnessPixels * inverseRange / ratioC);

            float error = EvaluateTMPInnerGlowCompositeError(
                probe,
                outlineWidth,
                faceDilate,
                faceSoftness,
                underlayDilate,
                underlaySoftness,
                effectPixelRange,
                in innerGlow);

            // The initial profile fit is already in physical pixels. A short coordinate
            // descent then accounts for TMP's dynamic ScaleRatio constraints without a
            // costly multidimensional grid search for every converted text layer.
            for (int iteration = 0; iteration < 6; iteration++)
            {
                float factor = 1f / (1 << iteration);
                ImproveTMPInnerGlowCompositeParameter(
                    probe, 0, 0.08f * factor, outlineWidth, maxFaceDilate, effectPixelRange, in innerGlow,
                    ref faceDilate, ref faceSoftness, ref underlayDilate, ref underlaySoftness, ref error);
                ImproveTMPInnerGlowCompositeParameter(
                    probe, 1, 0.10f * factor, outlineWidth, maxFaceDilate, effectPixelRange, in innerGlow,
                    ref faceDilate, ref faceSoftness, ref underlayDilate, ref underlaySoftness, ref error);
                ImproveTMPInnerGlowCompositeParameter(
                    probe, 2, 0.08f * factor, outlineWidth, maxFaceDilate, effectPixelRange, in innerGlow,
                    ref faceDilate, ref faceSoftness, ref underlayDilate, ref underlaySoftness, ref error);
                ImproveTMPInnerGlowCompositeParameter(
                    probe, 3, 0.06f * factor, outlineWidth, maxFaceDilate, effectPixelRange, in innerGlow,
                    ref faceDilate, ref faceSoftness, ref underlayDilate, ref underlaySoftness, ref error);
            }

            UnityEngine.Object.DestroyImmediate(probe);
        }
        private static void ImproveTMPInnerGlowCompositeParameter(
            Material probe,
            int parameter,
            float step,
            float outlineWidth,
            float maxFaceDilate,
            float effectPixelRange,
            in TextGlowEffectInfo innerGlow,
            ref float faceDilate,
            ref float faceSoftness,
            ref float underlayDilate,
            ref float underlaySoftness,
            ref float currentError)
        {
            for (int direction = -1; direction <= 1; direction += 2)
            {
                float candidateFaceDilate = faceDilate;
                float candidateFaceSoftness = faceSoftness;
                float candidateUnderlayDilate = underlayDilate;
                float candidateUnderlaySoftness = underlaySoftness;
                float delta = direction * step;
                switch (parameter)
                {
                    case 0:
                        candidateFaceDilate = Mathf.Clamp(faceDilate + delta, -1f, maxFaceDilate);
                        break;
                    case 1:
                        candidateFaceSoftness = Mathf.Clamp01(faceSoftness + delta);
                        break;
                    case 2:
                        candidateUnderlayDilate = Mathf.Clamp(underlayDilate + delta, -1f, 1f);
                        break;
                    default:
                        candidateUnderlaySoftness = Mathf.Clamp01(underlaySoftness + delta);
                        break;
                }

                float error = EvaluateTMPInnerGlowCompositeError(
                    probe,
                    outlineWidth,
                    candidateFaceDilate,
                    candidateFaceSoftness,
                    candidateUnderlayDilate,
                    candidateUnderlaySoftness,
                    effectPixelRange,
                    in innerGlow);
                if (error >= currentError)
                    continue;

                faceDilate = candidateFaceDilate;
                faceSoftness = candidateFaceSoftness;
                underlayDilate = candidateUnderlayDilate;
                underlaySoftness = candidateUnderlaySoftness;
                currentError = error;
            }
        }
        private static float EvaluateTMPInnerGlowCompositeError(
            Material probe,
            float outlineWidth,
            float faceDilate,
            float faceSoftness,
            float underlayDilate,
            float underlaySoftness,
            float effectPixelRange,
            in TextGlowEffectInfo innerGlow)
        {
            SetTMPPrimaryProperties(probe, faceDilate, outlineWidth, faceSoftness);
            SetTMPUnderlayProbe(probe, 0f, 0f, underlayDilate, underlaySoftness);
            ShaderUtilities.UpdateShaderRatios(probe);

            float ratioA = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_A);
            float ratioC = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_C);
            float normalWeight = probe.HasProperty(ShaderUtilities.ID_WeightNormal)
                ? probe.GetFloat(ShaderUtilities.ID_WeightNormal) * 0.25f
                : 0f;
            float weight = (normalWeight + faceDilate) * ratioA * 0.5f;
            float outlinePixels = outlineWidth * ratioA * effectPixelRange;
            float faceSoftnessPixels = faceSoftness * ratioA * effectPixelRange;
            float underlayScale = effectPixelRange / (1f + underlaySoftness * ratioC * effectPixelRange);
            float underlayBias = (0.5f - weight) * underlayScale
                - 0.5f
                - underlayDilate * ratioC * 0.5f * underlayScale;

            float insideExtent = Mathf.Min(
                Mathf.Max(1f, innerGlow.Size * 1.5f),
                Mathf.Max(1f, effectPixelRange * 0.75f));
            float opacity = Mathf.Clamp01(innerGlow.Color.a);
            float error = 0f;
            const int InsideSamples = 48;
            for (int i = 0; i < InsideSamples; i++)
            {
                float insideDistance = insideExtent * i / (InsideSamples - 1f);
                // Photoshop composites an inner effect through the antialiased glyph
                // coverage. TMP's normal Underlay is applied after Face, so its fitted
                // peak must account for that coverage rather than force an opaque edge.
                float target = opacity
                    * TmpInnerGlowCompositePeakCoverage
                    * TMPTextEffectParameterSolver.EvaluatePSGlowProfile(in innerGlow, insideDistance);
                EvaluateTMPInnerGlowCompositeSample(
                    insideDistance,
                    target,
                    1f,
                    weight,
                    outlinePixels,
                    faceSoftnessPixels,
                    underlayScale,
                    underlayBias,
                    effectPixelRange,
                    ref error);
            }

            float outsideExtent = Mathf.Min(3f, Mathf.Max(1f, insideExtent * 0.25f));
            const int OutsideSamples = 16;
            for (int i = 1; i <= OutsideSamples; i++)
            {
                float outsideDistance = -outsideExtent * i / OutsideSamples;
                EvaluateTMPInnerGlowCompositeSample(
                    outsideDistance,
                    0f,
                    0f,
                    weight,
                    outlinePixels,
                    faceSoftnessPixels,
                    underlayScale,
                    underlayBias,
                    effectPixelRange,
                    ref error);
            }

            return error;
        }
        private static void EvaluateTMPInnerGlowCompositeSample(
            float insideDistance,
            float targetGlowWeight,
            float targetAlpha,
            float weight,
            float outlinePixels,
            float faceSoftnessPixels,
            float underlayScale,
            float underlayBias,
            float effectPixelRange,
            ref float error)
        {
            float sd = 0.5f - insideDistance - weight * effectPixelRange;
            float faceAlpha = 1f - Mathf.Clamp01(
                (sd - outlinePixels * 0.5f + faceSoftnessPixels * 0.5f)
                / (1f + faceSoftnessPixels));
            float sdfAlpha = Mathf.Clamp01(0.5f + insideDistance / effectPixelRange);
            float underlayAlpha = Mathf.Clamp01(sdfAlpha * underlayScale - underlayBias);
            float glowWeight = underlayAlpha * (1f - faceAlpha);
            float totalAlpha = faceAlpha + glowWeight;
            float glowError = glowWeight - targetGlowWeight;
            float alphaError = totalAlpha - targetAlpha;
            error += glowError * glowError * 3f + alphaError * alphaError;
        }
        private static void SolveTMPUnderlayProperties(
            Material baseMaterial,
            float faceDilate,
            float outlineWidth,
            float outlineSoftness,
            float effectPixelRange,
            Vector2 offsetPixels,
            float dilatePixels,
            float softnessPixels,
            out float offsetX,
            out float offsetY,
            out float dilate,
            out float softness)
        {
            float inverseRange = 1f / Mathf.Max(0.01f, effectPixelRange);
            float baseOffsetX = offsetPixels.x * inverseRange;
            float baseOffsetY = offsetPixels.y * inverseRange;
            float baseDilate = dilatePixels * inverseRange;
            float baseSoftness = Mathf.Max(0f, softnessPixels) * inverseRange;
            float maxBase = Mathf.Max(
                Mathf.Max(Mathf.Abs(baseOffsetX), Mathf.Abs(baseOffsetY)),
                Mathf.Max(Mathf.Abs(baseDilate), baseSoftness));
            if (maxBase <= 0.000001f)
            {
                offsetX = 0f;
                offsetY = 0f;
                dilate = 0f;
                softness = 0f;
                return;
            }

            var probe = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            SetTMPPrimaryProperties(probe, faceDilate, outlineWidth, outlineSoftness);
            float low = 0f;
            float high = Mathf.Min(64f, 1f / maxBase);
            for (int i = 0; i < 28; i++)
            {
                float multiplier = (low + high) * 0.5f;
                SetTMPUnderlayProbe(
                    probe,
                    baseOffsetX * multiplier,
                    baseOffsetY * multiplier,
                    baseDilate * multiplier,
                    baseSoftness * multiplier);
                ShaderUtilities.UpdateShaderRatios(probe);
                float ratio = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_C);
                if (multiplier * ratio < 1f)
                    low = multiplier;
                else
                    high = multiplier;
            }

            float solved = (low + high) * 0.5f;
            offsetX = Mathf.Clamp(baseOffsetX * solved, -1f, 1f);
            offsetY = Mathf.Clamp(baseOffsetY * solved, -1f, 1f);
            dilate = Mathf.Clamp(baseDilate * solved, -1f, 1f);
            softness = Mathf.Clamp01(baseSoftness * solved);
            UnityEngine.Object.DestroyImmediate(probe);
        }
        private static void SetTMPUnderlayProbe(
            Material material,
            float offsetX,
            float offsetY,
            float dilate,
            float softness)
        {
            if (material.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, offsetX);
            if (material.HasProperty(ShaderUtilities.ID_UnderlayOffsetY))
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offsetY);
            if (material.HasProperty(ShaderUtilities.ID_UnderlayDilate))
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            if (material.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
        }
        private static void SolveTMPGlowProperties(
            Material baseMaterial,
            float faceDilate,
            float outlineWidth,
            float outlineSoftness,
            float effectPixelRange,
            float offsetPixels,
            float outerKernelPixels,
            out float offset,
            out float outer)
        {
            float inverseHalfRange = 2f / Mathf.Max(0.01f, effectPixelRange);
            float baseOffset = Mathf.Max(0f, offsetPixels) * inverseHalfRange;
            float baseOuter = Mathf.Max(0f, outerKernelPixels) * inverseHalfRange;
            float maxBase = Mathf.Max(baseOffset, baseOuter);
            if (maxBase <= 0.000001f)
            {
                offset = 0f;
                outer = 0f;
                return;
            }

            var probe = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            SetTMPPrimaryProperties(probe, faceDilate, outlineWidth, outlineSoftness);
            float low = 0f;
            float high = Mathf.Min(64f, 1f / maxBase);
            for (int i = 0; i < 24; i++)
            {
                float multiplier = (low + high) * 0.5f;
                probe.SetFloat(ShaderUtilities.ID_GlowOffset, baseOffset * multiplier);
                probe.SetFloat(ShaderUtilities.ID_GlowOuter, baseOuter * multiplier);
                ShaderUtilities.UpdateShaderRatios(probe);
                float ratio = GetTMPMaterialScaleRatio(probe, ShaderUtilities.ID_ScaleRatio_B);
                if (multiplier * ratio < 1f)
                    low = multiplier;
                else
                    high = multiplier;
            }

            float solved = (low + high) * 0.5f;
            offset = Mathf.Clamp01(baseOffset * solved);
            outer = Mathf.Clamp01(baseOuter * solved);
            UnityEngine.Object.DestroyImmediate(probe);
        }
        private static void FitTMPBevelMaterial(
            Color baseColor,
            float surfaceSlope,
            float altitudeDegrees,
            Color specularColor,
            float highlightOpacity,
            Color shadowColor,
            float shadowOpacity,
            string shadowBlendMode,
            TextEffectContourPoint[] contour,
            out float ambient,
            out float diffuse,
            out float reflectivity,
            out float specularPower)
        {
            surfaceSlope = Mathf.Clamp(surfaceSlope, 0f, 2f);
            float altitude = Mathf.Clamp(altitudeDegrees, 0.01f, 89.99f) * Mathf.Deg2Rad;
            float normalScale = 1f / Mathf.Sqrt(1f + surfaceSlope * surfaceSlope);
            float normalZSquared = normalScale * normalScale;
            highlightOpacity = Mathf.Clamp01(highlightOpacity);
            shadowOpacity = Mathf.Clamp01(shadowOpacity);

            const int ambientSteps = 14;
            const int diffuseSteps = 14;
            const int exponentSteps = 14;
            const int sampleCount = 72;
            float bestError = float.MaxValue;
            ambient = 1f;
            diffuse = 0f;
            reflectivity = 10f;
            specularPower = 0f;

            for (int ambientIndex = 0; ambientIndex < ambientSteps; ambientIndex++)
            {
                float candidateAmbient = Mathf.Lerp(0.35f, 1f, ambientIndex / (float)(ambientSteps - 1));
                for (int diffuseIndex = 0; diffuseIndex < diffuseSteps; diffuseIndex++)
                {
                    float candidateDiffuse = diffuseIndex / (float)(diffuseSteps - 1);
                    for (int exponentIndex = 0; exponentIndex < exponentSteps; exponentIndex++)
                    {
                        float candidateExponent = Mathf.Lerp(5f, 15f, exponentIndex / (float)(exponentSteps - 1));
                        float numerator = 0f;
                        float denominator = 0f;
                        for (int sample = 0; sample < sampleCount; sample++)
                        {
                            GetTMPBevelFitSample(
                                sample,
                                sampleCount,
                                surfaceSlope,
                                altitude,
                                normalScale,
                                contour,
                                baseColor,
                                shadowColor,
                                shadowOpacity,
                                shadowBlendMode,
                                specularColor,
                                highlightOpacity,
                                candidateAmbient,
                                candidateDiffuse,
                                candidateExponent,
                                out Color baseApproximation,
                                out Color specularBasis,
                                out Color target);
                            numerator += DotRGB(specularBasis, target - baseApproximation);
                            denominator += DotRGB(specularBasis, specularBasis);
                        }

                        float candidatePower = denominator > 0.000001f
                            ? Mathf.Clamp(numerator / denominator, 0f, 4f)
                            : 0f;
                        float error = 0f;
                        for (int sample = 0; sample < sampleCount; sample++)
                        {
                            GetTMPBevelFitSample(
                                sample,
                                sampleCount,
                                surfaceSlope,
                                altitude,
                                normalScale,
                                contour,
                                baseColor,
                                shadowColor,
                                shadowOpacity,
                                shadowBlendMode,
                                specularColor,
                                highlightOpacity,
                                candidateAmbient,
                                candidateDiffuse,
                                candidateExponent,
                                out Color baseApproximation,
                                out Color specularBasis,
                                out Color target);
                            Color delta = baseApproximation + specularBasis * candidatePower - target;
                            error += DotRGB(delta, delta);
                        }

                        if (error >= bestError)
                            continue;
                        bestError = error;
                        ambient = candidateAmbient;
                        diffuse = candidateDiffuse;
                        reflectivity = candidateExponent;
                        specularPower = candidatePower;
                    }
                }
            }
        }
        private static void GetTMPBevelFitSample(
            int sample,
            int sampleCount,
            float surfaceSlope,
            float altitude,
            float normalScale,
            TextEffectContourPoint[] contour,
            Color baseColor,
            Color shadowColor,
            float shadowOpacity,
            string shadowBlendMode,
            Color specularColor,
            float highlightOpacity,
            float ambient,
            float diffuse,
            float exponent,
            out Color baseApproximation,
            out Color specularBasis,
            out Color target)
        {
            float azimuthCos = Mathf.Cos((sample + 0.5f) * Mathf.PI * 2f / sampleCount);
            float tmpDot = (surfaceSlope * azimuthCos + 1f) * normalScale * 0.70710678118f;
            float diffuseMultiplier = 1f - tmpDot * diffuse;
            float ambientMultiplier = Mathf.Lerp(ambient, 1f, normalScale * normalScale);
            float lightingMultiplier = diffuseMultiplier * ambientMultiplier;
            baseApproximation = baseColor * lightingMultiplier;
            float specular = Mathf.Pow(Mathf.Max(0f, tmpDot), exponent) * lightingMultiplier;
            specularBasis = specularColor * specular;

            float psIncidence = Mathf.Clamp01((surfaceSlope * Mathf.Cos(altitude) * azimuthCos
                + Mathf.Sin(altitude)) * normalScale);
            float highlight = EvaluateTextEffectContour(contour, psIncidence) * highlightOpacity;
            float shadow = EvaluateTextEffectContour(contour, 1f - psIncidence) * shadowOpacity;
            Color shadowBlend = BlendTMPGradientColor(baseColor, shadowColor, shadowBlendMode);
            target = Color.LerpUnclamped(baseColor, shadowBlend, shadow);
            target += specularColor * highlight;
        }
        private static float DotRGB(Color left, Color right)
        {
            return left.r * right.r + left.g * right.g + left.b * right.b;
        }
        private static float EvaluateTextEffectContour(TextEffectContourPoint[] contour, float input)
        {
            input = Mathf.Clamp01(input);
            if (contour == null || contour.Length == 0)
                return input;
            if (input <= contour[0].Input)
                return contour[0].Output;

            for (int i = 1; i < contour.Length; i++)
            {
                if (input > contour[i].Input)
                    continue;

                float range = contour[i].Input - contour[i - 1].Input;
                if (range <= 0.00001f)
                    return contour[i].Output;
                float t = (input - contour[i - 1].Input) / range;
                return Mathf.LerpUnclamped(contour[i - 1].Output, contour[i].Output, t);
            }
            return contour[contour.Length - 1].Output;
        }
        private static Color CompensateTMPAdditiveEffectColor(Color baseColor, Color effectColor, string blendModeKey)
        {
            string mode = (blendModeKey ?? string.Empty).Trim();
            if (string.Equals(mode, "Scrn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Screen", StringComparison.OrdinalIgnoreCase))
            {
                effectColor.r *= 1f - baseColor.r;
                effectColor.g *= 1f - baseColor.g;
                effectColor.b *= 1f - baseColor.b;
            }
            return effectColor;
        }
        private static float ConvertPSDBevelAngleToTMPLightAngle(float psdAngle)
        {
            float normalizedAngle = Mathf.Repeat(90f - psdAngle, 360f);
            return normalizedAngle * Mathf.Deg2Rad;
        }
        private static bool IsPSDSmoothBevelTechnique(string techniqueKey)
        {
            string technique = (techniqueKey ?? string.Empty).Trim();
            return technique.Length == 0
                || string.Equals(technique, "SfBL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(technique, "Smooth", StringComparison.OrdinalIgnoreCase)
                || string.Equals(technique, "softMatte", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsPSDBevelDirectionDown(string directionKey)
        {
            string direction = (directionKey ?? string.Empty).Trim();
            return string.Equals(direction, "Out", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "Down", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "stampOut", StringComparison.OrdinalIgnoreCase);
        }
        private static string BuildTMPGradientSignature(Color baseColor, Vector2 effectSize, in TextLayerInfo textInfo)
        {
            if (!HasUsableTMPGradientStops(in textInfo))
                return "0";

            Color32 baseColor32 = baseColor;
            var builder = new StringBuilder(192);
            builder.Append(Mathf.RoundToInt(textInfo.GradientAngle * 1000f)).Append('|')
                .Append(textInfo.GradientReverse ? 1 : 0).Append('|')
                .Append(Mathf.RoundToInt(textInfo.GradientOpacity * 10000f)).Append('|')
                .Append(Mathf.RoundToInt(textInfo.GradientScale * 10000f)).Append('|')
                .Append(Mathf.RoundToInt(textInfo.GradientOffset.x * 10000f)).Append(',')
                .Append(Mathf.RoundToInt(textInfo.GradientOffset.y * 10000f)).Append('|')
                .Append(textInfo.GradientAlignWithLayer ? 1 : 0).Append('|')
                .Append(textInfo.GradientDither ? 1 : 0).Append('|')
                .Append(textInfo.GradientStyleKey).Append('|')
                .Append(textInfo.GradientBlendModeKey).Append('|')
                .Append(textInfo.GradientInterpolationKey).Append('|')
                .Append(Mathf.RoundToInt(Mathf.Abs(effectSize.x) * 100f)).Append(',')
                .Append(Mathf.RoundToInt(Mathf.Abs(effectSize.y) * 100f)).Append('|')
                .Append(Mathf.RoundToInt(textInfo.LayerOpacity * 10000f)).Append('|')
                .Append(baseColor32.r).Append(',').Append(baseColor32.g).Append(',')
                .Append(baseColor32.b).Append(',').Append(baseColor32.a);

            var stops = textInfo.GradientStops;
            for (int i = 0; i < stops.Length; i++)
            {
                Color32 color = stops[i].Color;
                builder.Append('|').Append(Mathf.RoundToInt(stops[i].Location * 100000f)).Append(':')
                    .Append(color.r).Append(',').Append(color.g).Append(',').Append(color.b).Append(',').Append(color.a);
            }
            return builder.ToString();
        }

        private static Texture2D GetOrCreateTMPGradientTexture(
            TMP_FontAsset fontAsset,
            string gradientSignature,
            Color baseColor,
            Vector2 effectSize,
            in TextLayerInfo textInfo)
        {
            string hash = Hash128.Compute(gradientSignature).ToString();
            if (tmpGradientTextureCache.TryGetValue(hash, out var cachedTexture) && cachedTexture != null)
                return cachedTexture;

            string folderPath = ResolveTMPMaterialFolderPath(fontAsset, null);
            string texturePath = string.IsNullOrWhiteSpace(folderPath)
                ? null
                : $"{folderPath}/PsdGradient_{hash}.png";
            if (!string.IsNullOrWhiteSpace(texturePath))
            {
                var textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (textureAsset != null)
                {
                    tmpGradientTextureCache[hash] = textureAsset;
                    return textureAsset;
                }
            }

            const int textureSize = 128;
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false, false)
            {
                name = $"PsdGradient_{hash}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            var pixels = new Color32[textureSize * textureSize];
            float width = Mathf.Max(1f, Mathf.Abs(effectSize.x));
            float height = Mathf.Max(1f, Mathf.Abs(effectSize.y));
            for (int y = 0; y < textureSize; y++)
            {
                float v = (y + 0.5f) / textureSize;
                int row = y * textureSize;
                for (int x = 0; x < textureSize; x++)
                {
                    float u = (x + 0.5f) / textureSize;
                    float t = EvaluateTMPGradientCoordinate(u, v, width, height, in textInfo);
                    if (textInfo.GradientDither)
                    {
                        t = Mathf.Clamp01(t + GetTMPGradientDither(x, y) / 255f);
                    }
                    pixels[row + x] = CompositeTMPGradient(baseColor, EvaluateTMPGradientColor(t, in textInfo), in textInfo);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            if (!string.IsNullOrWhiteSpace(texturePath))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absoluteTexturePath = Path.GetFullPath(Path.Combine(projectRoot, texturePath));
                File.WriteAllBytes(absoluteTexturePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 0;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }
            tmpGradientTextureCache[hash] = texture;
            return texture;
        }

        private static float EvaluateTMPGradientCoordinate(float u, float v, float width, float height, in TextLayerInfo textInfo)
        {
            float radians = textInfo.GradientAngle * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            var perpendicular = new Vector2(-direction.y, direction.x);
            var center = new Vector2(
                textInfo.GradientOffset.x * width * 0.5f,
                -textInfo.GradientOffset.y * height * 0.5f);
            var point = new Vector2((u - 0.5f) * width, (v - 0.5f) * height) - center;
            float scale = Mathf.Max(0.0001f, textInfo.GradientScale);
            string style = (textInfo.GradientStyleKey ?? string.Empty).Trim();
            float t;
            switch (style)
            {
                case "Rdl":
                case "Radial":
                    t = point.magnitude / (Mathf.Sqrt(width * width + height * height) * 0.5f * scale);
                    break;
                case "Angl":
                case "Angle":
                    t = Mathf.Repeat((Mathf.Atan2(point.y, point.x) - radians) / (Mathf.PI * 2f) + 0.5f, 1f);
                    break;
                case "Rflc":
                case "Reflected":
                    t = Mathf.Abs(Vector2.Dot(point, direction)) /
                        ((Mathf.Abs(direction.x) * width + Mathf.Abs(direction.y) * height) * 0.5f * scale);
                    break;
                case "Dmnd":
                case "Diamond":
                    float diamondExtent = (Mathf.Abs(direction.x) + Mathf.Abs(perpendicular.x)) * width * 0.5f
                        + (Mathf.Abs(direction.y) + Mathf.Abs(perpendicular.y)) * height * 0.5f;
                    t = (Mathf.Abs(Vector2.Dot(point, direction)) + Mathf.Abs(Vector2.Dot(point, perpendicular))) /
                        (diamondExtent * scale);
                    break;
                default:
                    float linearExtent = (Mathf.Abs(direction.x) * width + Mathf.Abs(direction.y) * height) * 0.5f;
                    t = 0.5f + Vector2.Dot(point, direction) / (2f * linearExtent * scale);
                    break;
            }

            t = Mathf.Clamp01(t);
            return textInfo.GradientReverse ? 1f - t : t;
        }

        private static Color EvaluateTMPGradientColor(float t, in TextLayerInfo textInfo)
        {
            var stops = textInfo.GradientStops;
            if (t <= stops[0].Location)
                return stops[0].Color;

            int last = stops.Length - 1;
            if (t >= stops[last].Location)
                return stops[last].Color;

            for (int i = 1; i <= last; i++)
            {
                if (t > stops[i].Location)
                    continue;

                float range = stops[i].Location - stops[i - 1].Location;
                return range > 0.00001f
                    ? InterpolateTMPGradientColor(
                        stops[i - 1].Color,
                        stops[i].Color,
                        (t - stops[i - 1].Location) / range,
                        textInfo.GradientInterpolationKey)
                    : stops[i].Color;
            }
            return stops[last].Color;
        }

        private static Color InterpolateTMPGradientColor(Color start, Color end, float t, string interpolationKey)
        {
            if (string.Equals(interpolationKey, "Perc", StringComparison.OrdinalIgnoreCase))
            {
                Vector3 startLab = LinearRgbToOklab(start.linear);
                Vector3 endLab = LinearRgbToOklab(end.linear);
                Vector3 lab = Vector3.LerpUnclamped(startLab, endLab, t);
                Color color = OklabToLinearRgb(lab).gamma;
                color.a = Mathf.LerpUnclamped(start.a, end.a, t);
                return color;
            }

            if (string.Equals(interpolationKey, "Smoo", StringComparison.OrdinalIgnoreCase))
            {
                Color color = Color.LerpUnclamped(start.linear, end.linear, t).gamma;
                color.a = Mathf.LerpUnclamped(start.a, end.a, t);
                return color;
            }

            return Color.LerpUnclamped(start, end, t);
        }

        private static Vector3 LinearRgbToOklab(Color color)
        {
            float l = Mathf.Pow(Mathf.Max(0f, 0.4122214708f * color.r + 0.5363325363f * color.g + 0.0514459929f * color.b), 1f / 3f);
            float m = Mathf.Pow(Mathf.Max(0f, 0.2119034982f * color.r + 0.6806995451f * color.g + 0.1073969566f * color.b), 1f / 3f);
            float s = Mathf.Pow(Mathf.Max(0f, 0.0883024619f * color.r + 0.2817188376f * color.g + 0.6299787005f * color.b), 1f / 3f);
            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        private static Color OklabToLinearRgb(Vector3 lab)
        {
            float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            l *= l * l;
            m *= m * m;
            s *= s * s;
            return new Color(
                Mathf.Clamp01(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
                Mathf.Clamp01(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
                Mathf.Clamp01(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s),
                1f);
        }

        private static Color32 CompositeTMPGradient(Color baseColor, Color gradientColor, in TextLayerInfo textInfo)
        {
            float layerOpacity = Mathf.Clamp01(textInfo.LayerOpacity);
            float baseAlpha = layerOpacity > 0.00001f ? Mathf.Clamp01(baseColor.a / layerOpacity) : 0f;
            float overlayAlpha = Mathf.Clamp01(gradientColor.a * textInfo.GradientOpacity);
            Color blended = BlendTMPGradientColor(baseColor, gradientColor, textInfo.GradientBlendModeKey);
            float outputAlpha = overlayAlpha + baseAlpha * (1f - overlayAlpha);
            float inverseAlpha = outputAlpha > 0.00001f ? 1f / outputAlpha : 0f;
            var output = new Color(
                (blended.r * overlayAlpha + baseColor.r * baseAlpha * (1f - overlayAlpha)) * inverseAlpha,
                (blended.g * overlayAlpha + baseColor.g * baseAlpha * (1f - overlayAlpha)) * inverseAlpha,
                (blended.b * overlayAlpha + baseColor.b * baseAlpha * (1f - overlayAlpha)) * inverseAlpha,
                outputAlpha * layerOpacity);
            return output;
        }

        private static Color BlendTMPGradientColor(Color baseColor, Color overlayColor, string blendModeKey)
        {
            string mode = (blendModeKey ?? string.Empty).Trim();
            switch (mode)
            {
                case "Mltp":
                    return new Color(baseColor.r * overlayColor.r, baseColor.g * overlayColor.g, baseColor.b * overlayColor.b, 1f);
                case "Scrn":
                    return new Color(1f - (1f - baseColor.r) * (1f - overlayColor.r), 1f - (1f - baseColor.g) * (1f - overlayColor.g), 1f - (1f - baseColor.b) * (1f - overlayColor.b), 1f);
                case "Ovrl":
                    return new Color(BlendTMPOverlay(baseColor.r, overlayColor.r), BlendTMPOverlay(baseColor.g, overlayColor.g), BlendTMPOverlay(baseColor.b, overlayColor.b), 1f);
                case "SftL":
                    return new Color(BlendTMPSoftLight(baseColor.r, overlayColor.r), BlendTMPSoftLight(baseColor.g, overlayColor.g), BlendTMPSoftLight(baseColor.b, overlayColor.b), 1f);
                case "HrdL":
                    return new Color(BlendTMPOverlay(overlayColor.r, baseColor.r), BlendTMPOverlay(overlayColor.g, baseColor.g), BlendTMPOverlay(overlayColor.b, baseColor.b), 1f);
                case "Drkn":
                    return new Color(Mathf.Min(baseColor.r, overlayColor.r), Mathf.Min(baseColor.g, overlayColor.g), Mathf.Min(baseColor.b, overlayColor.b), 1f);
                case "Lghn":
                    return new Color(Mathf.Max(baseColor.r, overlayColor.r), Mathf.Max(baseColor.g, overlayColor.g), Mathf.Max(baseColor.b, overlayColor.b), 1f);
                default:
                    return new Color(overlayColor.r, overlayColor.g, overlayColor.b, 1f);
            }
        }

        private static float BlendTMPOverlay(float baseValue, float overlayValue)
        {
            return baseValue <= 0.5f
                ? 2f * baseValue * overlayValue
                : 1f - 2f * (1f - baseValue) * (1f - overlayValue);
        }

        private static float BlendTMPSoftLight(float baseValue, float overlayValue)
        {
            return overlayValue <= 0.5f
                ? baseValue - (1f - 2f * overlayValue) * baseValue * (1f - baseValue)
                : baseValue + (2f * overlayValue - 1f) * (Mathf.Sqrt(baseValue) - baseValue);
        }

        private static float GetTMPGradientDither(int x, int y)
        {
            int index = (x & 3) | ((y & 3) << 2);
            return (TmpGradientBayer4x4[index] - 7.5f) / 16f;
        }

        private static void ApplyTMPEffectMaterialProperties(Material mat,
            float outlineWidth, float faceDilate, float faceSoftness, Color32 outlineColor, bool hasOutline,
            float shadowOffsetX, float shadowOffsetY, float shadowDilate, float shadowSoftness, Color32 shadowColor, bool hasShadow, bool shadowIsInner,
            Color32 glowColor, float glowOffset, float glowInner, float glowOuter, float glowPower, bool hasGlow,
            float bevelAmount, float bevelOffset, float bevelWidth, float bevelClamp, float bevelRoundness, float lightAngle,
            Color bevelSpecularColor, Color bevelReflectFaceColor, Color bevelReflectOutlineColor, float bevelSpecularPower, float bevelReflectivity, float bevelDiffuse, float bevelAmbient,
            float bevelShaderFlags, bool hasBevel, Texture2D gradientTexture, bool hasGradient)
        {
            if (mat == null) return;

            // Text color belongs to TMP's Vertex Color. Keeping the material face
            // white prevents the shared material from tinting it a second time.
            if (mat.HasProperty(ShaderUtilities.ID_FaceColor))
                mat.SetColor(ShaderUtilities.ID_FaceColor, Color.white);

            if (hasGradient)
            {
                if (mat.HasProperty(TmpFaceTexturePropertyId))
                    mat.SetTexture(TmpFaceTexturePropertyId, gradientTexture);
                if (mat.HasProperty(TmpFaceTextureTransformPropertyId))
                    mat.SetVector(TmpFaceTextureTransformPropertyId, new Vector4(1f, 1f, 0f, 0f));
            }

            if (mat.HasProperty(TmpFaceDilatePropertyId))
            {
                mat.SetFloat(TmpFaceDilatePropertyId, faceDilate);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineSoftness))
            {
                mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, faceSoftness);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                mat.SetColor(ShaderUtilities.ID_OutlineColor, hasOutline ? (Color)outlineColor : Color.clear);
            }
            if (hasOutline)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Outline);
            }

            if (mat.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, hasShadow ? (Color)shadowColor : Color.clear);
            }
            if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
            {
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, shadowOffsetX);
            }
            if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetY))
            {
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, shadowOffsetY);
            }
            if (mat.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
            {
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, shadowSoftness);
            }
            if (mat.HasProperty(ShaderUtilities.ID_UnderlayDilate))
            {
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, shadowDilate);
            }
            if (hasShadow)
            {
                if (shadowIsInner)
                {
                    mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
                    mat.EnableKeyword(TmpUnderlayInnerKeyword);
                }
                else
                {
                    mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    mat.DisableKeyword(TmpUnderlayInnerKeyword);
                }
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.DisableKeyword(TmpUnderlayInnerKeyword);
            }

            if (mat.HasProperty(ShaderUtilities.ID_GlowColor))
            {
                mat.SetColor(ShaderUtilities.ID_GlowColor, hasGlow ? (Color)glowColor : Color.clear);
            }
            if (mat.HasProperty(ShaderUtilities.ID_GlowOffset))
            {
                mat.SetFloat(ShaderUtilities.ID_GlowOffset, glowOffset);
            }
            if (mat.HasProperty(ShaderUtilities.ID_GlowInner))
            {
                mat.SetFloat(ShaderUtilities.ID_GlowInner, glowInner);
            }
            if (mat.HasProperty(ShaderUtilities.ID_GlowOuter))
            {
                mat.SetFloat(ShaderUtilities.ID_GlowOuter, glowOuter);
            }
            if (mat.HasProperty(ShaderUtilities.ID_GlowPower))
            {
                mat.SetFloat(ShaderUtilities.ID_GlowPower, glowPower);
            }
            if (hasGlow)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Glow);
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Glow);
            }

            if (mat.HasProperty(ShaderUtilities.ID_BevelAmount))
            {
                mat.SetFloat(ShaderUtilities.ID_BevelAmount, bevelAmount);
            }
            if (mat.HasProperty(TmpShaderFlagsPropertyId))
            {
                mat.SetFloat(TmpShaderFlagsPropertyId, hasBevel ? bevelShaderFlags : 0f);
            }
            if (mat.HasProperty(TmpBevelOffsetPropertyId))
            {
                mat.SetFloat(TmpBevelOffsetPropertyId, bevelOffset);
            }
            if (mat.HasProperty(TmpBevelWidthPropertyId))
            {
                mat.SetFloat(TmpBevelWidthPropertyId, bevelWidth);
            }
            if (mat.HasProperty(TmpBevelClampPropertyId))
            {
                mat.SetFloat(TmpBevelClampPropertyId, bevelClamp);
            }
            if (mat.HasProperty(TmpBevelRoundnessPropertyId))
            {
                mat.SetFloat(TmpBevelRoundnessPropertyId, bevelRoundness);
            }
            if (mat.HasProperty(ShaderUtilities.ID_LightAngle))
            {
                mat.SetFloat(ShaderUtilities.ID_LightAngle, lightAngle);
            }
            if (mat.HasProperty(TmpSpecularColorPropertyId))
            {
                mat.SetColor(TmpSpecularColorPropertyId, hasBevel ? bevelSpecularColor : Color.clear);
            }
            if (mat.HasProperty(TmpReflectFaceColorPropertyId))
            {
                mat.SetColor(TmpReflectFaceColorPropertyId, hasBevel ? bevelReflectFaceColor : Color.black);
            }
            if (mat.HasProperty(TmpReflectOutlineColorPropertyId))
            {
                mat.SetColor(TmpReflectOutlineColorPropertyId, hasBevel ? bevelReflectOutlineColor : Color.black);
            }
            if (mat.HasProperty(TmpSpecularPowerPropertyId))
            {
                mat.SetFloat(TmpSpecularPowerPropertyId, hasBevel ? bevelSpecularPower : 0f);
            }
            if (mat.HasProperty(TmpReflectivityPropertyId))
            {
                mat.SetFloat(TmpReflectivityPropertyId, hasBevel ? bevelReflectivity : 10f);
            }
            if (mat.HasProperty(TmpDiffusePropertyId))
            {
                mat.SetFloat(TmpDiffusePropertyId, hasBevel ? bevelDiffuse : 0f);
            }
            if (mat.HasProperty(TmpAmbientPropertyId))
            {
                mat.SetFloat(TmpAmbientPropertyId, hasBevel ? bevelAmbient : 1f);
            }
            if (hasBevel)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Bevel);
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Bevel);
            }

            ShaderUtilities.UpdateShaderRatios(mat);
        }
        private static string BuildTMPEffectMaterialName(TMP_FontAsset fontAsset, Material baseMaterial, in TextLayerInfo textInfo)
        {
            string effectTypeName = GetTMPEffectMaterialTypeName(in textInfo);
            return BuildTMPMaterialName(fontAsset, baseMaterial, effectTypeName);
        }
        private static string BuildTMPMaterialName(TMP_FontAsset fontAsset, Material baseMaterial, string effectTypeName)
        {
            string folderPath = ResolveTMPMaterialFolderPath(fontAsset, baseMaterial);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return $"{effectTypeName}_1";
            }

            int maxIndex = 0;
            var materialGuids = AssetDatabase.FindAssets("t:Material", new string[] { folderPath });
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                if (string.IsNullOrWhiteSpace(materialPath))
                {
                    continue;
                }

                string existingName = Path.GetFileNameWithoutExtension(materialPath);
                if (TryGetTMPEffectMaterialIndex(existingName, effectTypeName, out int existingIndex) && existingIndex > maxIndex)
                {
                    maxIndex = existingIndex;
                }
            }

            return $"{effectTypeName}_{maxIndex + 1}";
        }
        private static Material LoadPersistentTMPEffectMaterial(TMP_FontAsset fontAsset, string effectSignature)
        {
            if (fontAsset == null || string.IsNullOrWhiteSpace(effectSignature)) return null;

            if (!TryFindPersistentTMPEffectMaterial(fontAsset, effectSignature, out var material, out _))
            {
                return null;
            }

            EnsureTMPMaterialHasAtlas(material, fontAsset);
            return material;
        }
        private static void PersistTMPEffectMaterial(TMP_FontAsset fontAsset, Material material, string effectSignature)
        {
            if (fontAsset == null || material == null || string.IsNullOrWhiteSpace(effectSignature)) return;

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            bool fontIsProjectAsset = !string.IsNullOrWhiteSpace(assetPath)
                && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
            // 字体在 UPM 包里时改存共享输出目录；早退会让特效材质只留在内存，prefab 丢 fontSharedMaterial 引用。
            string storageFolder = ResolveTMPMaterialFolderPath(fontAsset, null);
            if (string.IsNullOrWhiteSpace(storageFolder))
            {
                Debug.LogWarning("未配置共享资源输出目录(SharedAssetsOutput)，TMP 特效材质无法落盘，描边/发光/渐变不会保留在 Prefab 中。");
                return;
            }

            EnsureTMPMaterialHasAtlas(material, fontAsset);

            string materialPath;
            Material existingMaterial;
            if (TryFindPersistentTMPEffectMaterial(fontAsset, effectSignature, out existingMaterial, out materialPath))
            {
                if (existingMaterial != material)
                {
                    EditorUtility.CopySerialized(material, existingMaterial);
                    UnityEngine.Object.DestroyImmediate(material);
                    material = existingMaterial;
                }
            }
            else
            {
                materialPath = $"{storageFolder}/{material.name}.mat";
                AssetDatabase.CreateAsset(material, materialPath);
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath) ?? material;
            }

            EnsureTMPMaterialHasAtlas(material, fontAsset);
            SetTMPEffectMaterialSignature(fontAsset, materialPath, effectSignature);
            EditorUtility.SetDirty(material);
            if (fontIsProjectAsset) EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
        }
        private static string GetSiblingMaterialAssetPath(string ownerAssetPath, string materialName, bool generateUnique)
        {
            if (string.IsNullOrWhiteSpace(ownerAssetPath) || string.IsNullOrWhiteSpace(materialName))
            {
                return null;
            }

            string folderPath = Path.GetDirectoryName(ownerAssetPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            string targetPath = $"{folderPath}/{materialName}.mat";
            return generateUnique ? AssetDatabase.GenerateUniqueAssetPath(targetPath) : targetPath;
        }
        private static string GetTMPEffectMaterialTypeName(in TextLayerInfo textInfo)
        {
            var parts = new List<string>(5);
            if (ResolveTMPGradientOutput(in textInfo) == TMPGradientOutput.Texture)
            {
                parts.Add("Gradient");
            }
            if (textInfo.HasOutline)
            {
                parts.Add("Outline");
            }
            if (textInfo.HasShadow)
            {
                parts.Add(textInfo.ShadowIsInner ? "InnerShadow" : "Shadow");
            }
            if (textInfo.HasGlow)
            {
                if (textInfo.OuterGlow.Enabled)
                    parts.Add("Glow");
                if (textInfo.InnerGlow.Enabled)
                    parts.Add("InnerGlow");
            }
            if (textInfo.HasBevel)
            {
                parts.Add("Bevel");
            }
            return parts.Count > 0 ? string.Join("_", parts) : "Text";
        }
        private static bool TryGetTMPEffectMaterialIndex(string materialName, string effectTypeName, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(materialName) || string.IsNullOrWhiteSpace(effectTypeName))
            {
                return false;
            }

            string prefix = effectTypeName + "_";
            if (!materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(materialName.Substring(prefix.Length), out index);
        }
        private static string ResolveTMPMaterialFolderPath(TMP_FontAsset fontAsset, Material baseMaterial)
        {
            string ownerAssetPath = fontAsset != null ? AssetDatabase.GetAssetPath(fontAsset) : baseMaterial != null ? AssetDatabase.GetAssetPath(baseMaterial) : null;
            if (!string.IsNullOrWhiteSpace(ownerAssetPath) && ownerAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(ownerAssetPath)?.Replace("\\", "/");
            }

            // UPM 包里的字体（TMP 自带 LiberationSans 等）不能往里写。改存到共享输出目录，
            // 否则特效材质只留在内存，存 prefab 时 fontSharedMaterial 引用会静默丢失。
            var parser = Instance;
            string shared = parser != null ? parser.SharedAssetsOutput : null;
            if (string.IsNullOrWhiteSpace(shared)) return null;
            shared = shared.Replace("\\", "/").TrimEnd('/');
            string fontKey = LayerNameUtility.Sanitize(fontAsset != null ? fontAsset.name : "Default");
            string folder = $"{shared}/TMP Effects/{fontKey}";
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            return folder;
        }
        private static string GetTMPFontAssetSignature(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return "TMP_FONT_NULL";
            }

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrWhiteSpace(assetGuid))
                {
                    return assetGuid;
                }
            }

            return fontAsset.name;
        }
        private static string GetTMPEffectMaterialSignatureUserData(TMP_FontAsset fontAsset, string effectSignature)
        {
            return $"{TmpEffectMaterialSignatureUserDataPrefix}{GetTMPFontAssetSignature(fontAsset)}|{effectSignature}";
        }
        private static string BuildLegacyTMPEffectMaterialName(TMP_FontAsset fontAsset, string effectSignature)
        {
            uint hash = unchecked((uint)Animator.StringToHash(effectSignature));
            string ownerName = fontAsset != null ? fontAsset.name : "TMP Font";
            return $"{ownerName}{LegacyTmpEffectMaterialNameToken}{hash:X8}";
        }
        private static bool TryFindPersistentTMPEffectMaterial(TMP_FontAsset fontAsset, string effectSignature, out Material material, out string materialPath)
        {
            material = null;
            materialPath = null;
            if (fontAsset == null || string.IsNullOrWhiteSpace(effectSignature))
            {
                return false;
            }

            string folderPath = ResolveTMPMaterialFolderPath(fontAsset, null);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            string expectedUserData = GetTMPEffectMaterialSignatureUserData(fontAsset, effectSignature);
            var materialGuids = AssetDatabase.FindAssets("t:Material", new string[] { folderPath });
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null || !string.Equals(importer.userData, expectedUserData, StringComparison.Ordinal))
                {
                    continue;
                }

                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    continue;
                }

                materialPath = path;
                return true;
            }

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            string legacyMaterialName = BuildLegacyTMPEffectMaterialName(fontAsset, effectSignature);
            string legacyMaterialPath = GetSiblingMaterialAssetPath(assetPath, legacyMaterialName, false);
            if (string.IsNullOrWhiteSpace(legacyMaterialPath))
            {
                return false;
            }

            material = AssetDatabase.LoadAssetAtPath<Material>(legacyMaterialPath);
            if (material == null)
            {
                return false;
            }

            materialPath = legacyMaterialPath;
            SetTMPEffectMaterialSignature(fontAsset, materialPath, effectSignature);
            return true;
        }
        private static void SetTMPEffectMaterialSignature(TMP_FontAsset fontAsset, string materialPath, string effectSignature)
        {
            if (string.IsNullOrWhiteSpace(materialPath) || string.IsNullOrWhiteSpace(effectSignature))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(materialPath);
            if (importer == null)
            {
                return;
            }

            string userData = GetTMPEffectMaterialSignatureUserData(fontAsset, effectSignature);
            if (string.Equals(importer.userData, userData, StringComparison.Ordinal))
            {
                return;
            }

            importer.userData = userData;
            importer.SaveAndReimport();
        }
        private static string GetTMPFontBaseName(TMP_FontAsset fontAsset, string assetPath)
        {
            string name = !string.IsNullOrWhiteSpace(assetPath) ? Path.GetFileNameWithoutExtension(assetPath) : fontAsset != null ? fontAsset.name : "TMP Font";
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "TMP Font";
            }

            const string sdfSuffix = " SDF";
            return name.EndsWith(sdfSuffix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - sdfSuffix.Length)
                : name;
        }
        private static Texture2D ResolveTMPFontAtlasTexture(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return null;
            }

            var atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures != null)
            {
                for (int i = 0; i < atlasTextures.Length; i++)
                {
                    if (atlasTextures[i] != null)
                    {
                        return atlasTextures[i];
                    }
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            Texture2D fallback = null;
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (subAssets == null)
            {
                return null;
            }

            for (int i = 0; i < subAssets.Length; i++)
            {
                if (!(subAssets[i] is Texture2D texture))
                {
                    continue;
                }

                if (fallback == null) fallback = texture;
                if (texture.name.IndexOf("Atlas", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return texture;
                }
            }

            return fallback;
        }
        private static bool EnsureTMPMaterialHasAtlas(Material material, TMP_FontAsset fontAsset)
        {
            if (material == null || fontAsset == null)
            {
                return false;
            }

#if !UNITY_6000_6_OR_NEWER
            ShaderUtilities.GetShaderPropertyIDs();
#endif
            var atlasTexture = ResolveTMPFontAtlasTexture(fontAsset);
            if (atlasTexture == null)
            {
                return false;
            }

            bool changed = false;
            if (material.HasProperty(ShaderUtilities.ID_MainTex) && material.GetTexture(ShaderUtilities.ID_MainTex) != atlasTexture)
            {
                material.SetTexture(ShaderUtilities.ID_MainTex, atlasTexture);
                changed = true;
            }

            float textureWidth = atlasTexture.width > 0 ? atlasTexture.width : Mathf.Max(1, fontAsset.atlasWidth);
            float textureHeight = atlasTexture.height > 0 ? atlasTexture.height : Mathf.Max(1, fontAsset.atlasHeight);
            float gradientScale = Mathf.Max(1f, fontAsset.atlasPadding + 1f);

            if (material.HasProperty(ShaderUtilities.ID_TextureWidth) && !Mathf.Approximately(material.GetFloat(ShaderUtilities.ID_TextureWidth), textureWidth))
            {
                material.SetFloat(ShaderUtilities.ID_TextureWidth, textureWidth);
                changed = true;
            }
            if (material.HasProperty(ShaderUtilities.ID_TextureHeight) && !Mathf.Approximately(material.GetFloat(ShaderUtilities.ID_TextureHeight), textureHeight))
            {
                material.SetFloat(ShaderUtilities.ID_TextureHeight, textureHeight);
                changed = true;
            }
            if (material.HasProperty(ShaderUtilities.ID_GradientScale) && !Mathf.Approximately(material.GetFloat(ShaderUtilities.ID_GradientScale), gradientScale))
            {
                material.SetFloat(ShaderUtilities.ID_GradientScale, gradientScale);
                changed = true;
            }
            if (material.HasProperty(ShaderUtilities.ID_WeightNormal) && !Mathf.Approximately(material.GetFloat(ShaderUtilities.ID_WeightNormal), fontAsset.normalStyle))
            {
                material.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                changed = true;
            }
            if (material.HasProperty(ShaderUtilities.ID_WeightBold) && !Mathf.Approximately(material.GetFloat(ShaderUtilities.ID_WeightBold), fontAsset.boldStyle))
            {
                material.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
                changed = true;
            }

            ShaderUtilities.UpdateShaderRatios(material);
            if (changed)
            {
                EditorUtility.SetDirty(material);
            }
            return changed;
        }
        private static TMP_FontAsset EnsureTMPRichTextFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            assetPath = assetPath.Replace("\\", "/");
            string resourcePath = TMP_Settings.instance != null
                ? TMP_Settings.defaultFontAssetPath
                : "Fonts & Materials/";
            resourcePath = string.IsNullOrWhiteSpace(resourcePath)
                ? "Fonts & Materials"
                : resourcePath.Replace("\\", "/").Trim('/');
            string resourceMarker = "/Resources/" + resourcePath + "/";
            if (assetPath.IndexOf(resourceMarker, StringComparison.OrdinalIgnoreCase) >= 0
                && string.Equals(
                    Path.GetFileNameWithoutExtension(assetPath),
                    fontAsset.name,
                    StringComparison.Ordinal))
            {
                return fontAsset;
            }

            if (fontAsset.name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            EnsureTMPFontAssetResources(fontAsset);
            AssetDatabase.SaveAssets();

            string targetFolder = "Assets/TextMesh Pro/Resources/" + resourcePath;
            EnsureAssetFolderExists(targetFolder);
            string targetName = fontAsset.name;
            string targetPath = targetFolder + "/" + targetName + ".asset";
            var resourceFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetPath);
            if (resourceFont != null && HasSameTMPFontSource(resourceFont, fontAsset))
                return resourceFont;
            if (resourceFont != null)
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return null;
                targetName += "_" + guid.Substring(0, 8);
                targetPath = targetFolder + "/" + targetName + ".asset";
                resourceFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetPath);
                if (resourceFont != null && HasSameTMPFontSource(resourceFont, fontAsset))
                    return resourceFont;
                if (resourceFont != null)
                    return null;
            }

            if (!AssetDatabase.CopyAsset(assetPath, targetPath))
                return null;

            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
            resourceFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetPath);
            if (resourceFont != null && !string.Equals(resourceFont.name, targetName, StringComparison.Ordinal))
            {
                resourceFont.name = targetName;
                EditorUtility.SetDirty(resourceFont);
                AssetDatabase.SaveAssets();
            }
            return resourceFont;
        }

        private static bool HasSameTMPFontSource(TMP_FontAsset left, TMP_FontAsset right)
        {
            if (left == right)
                return true;
            if (left.sourceFontFile != null && left.sourceFontFile == right.sourceFontFile)
                return true;

            string leftGuid = left.creationSettings.sourceFontFileGUID;
            string rightGuid = right.creationSettings.sourceFontFileGUID;
            return !string.IsNullOrEmpty(leftGuid)
                && string.Equals(leftGuid, rightGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            string[] parts = assetFolderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static Material EnsureTMPFontAssetResources(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return null;
            }

            string assetPath = AssetDatabase.GetAssetPath(fontAsset);
            var atlasTexture = ResolveTMPFontAtlasTexture(fontAsset);
            bool fontDirty = false;

            if (atlasTexture != null)
            {
                if (fontAsset.atlasTextures == null || fontAsset.atlasTextures.Length == 0)
                {
                    fontAsset.atlasTextures = new Texture2D[] { atlasTexture };
                    fontDirty = true;
                }
                else if (fontAsset.atlasTextures[0] == null)
                {
                    fontAsset.atlasTextures[0] = atlasTexture;
                    fontDirty = true;
                }
            }

            var fontMaterial = fontAsset.material;
            string fontMaterialPath = fontMaterial != null ? AssetDatabase.GetAssetPath(fontMaterial) : null;
            bool needsMaterial = fontMaterial == null || string.IsNullOrWhiteSpace(fontMaterialPath);
            if (needsMaterial && !string.IsNullOrWhiteSpace(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string materialName = $"{GetTMPFontBaseName(fontAsset, assetPath)} Atlas Material";
                string targetPath = GetSiblingMaterialAssetPath(assetPath, materialName, false);
                var existingMaterial = string.IsNullOrWhiteSpace(targetPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (existingMaterial != null)
                {
                    fontMaterial = existingMaterial;
                }
                else
                {
                    Material templateMaterial = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.material : null;
                    if (fontMaterial == null)
                    {
                        fontMaterial = CreateTMPMaterialCopy(templateMaterial, true);
                        if (fontMaterial == null)
                        {
                            var shader = Shader.Find("TextMeshPro/Distance Field");
                            if (shader != null)
                            {
                                fontMaterial = new Material(shader);
                            }
                        }
                    }

                    if (fontMaterial != null)
                    {
                        fontMaterial.name = materialName;
                        AssetDatabase.CreateAsset(fontMaterial, targetPath);
                        fontMaterial = AssetDatabase.LoadAssetAtPath<Material>(targetPath) ?? fontMaterial;
                    }
                }

                if (fontMaterial != null && fontAsset.material != fontMaterial)
                {
                    fontAsset.material = fontMaterial;
                    fontDirty = true;
                }
            }

            if (fontMaterial != null && EnsureTMPMaterialHasAtlas(fontMaterial, fontAsset))
            {
                fontDirty = true;
            }

            if (fontDirty)
            {
                EditorUtility.SetDirty(fontAsset);
                if (atlasTexture != null)
                {
                    EditorUtility.SetDirty(atlasTexture);
                }
                if (fontMaterial != null)
                {
                    EditorUtility.SetDirty(fontMaterial);
                }
                AssetDatabase.SaveAssets();
            }

            return fontAsset.material;
        }
        private static Material CreateTMPMaterialCopy(Material baseMaterial, bool preferDistanceFieldShader)
        {
            if (baseMaterial == null) return null;

#if !UNITY_6000_6_OR_NEWER
            ShaderUtilities.GetShaderPropertyIDs();
#endif
            var material = new Material(baseMaterial);
            var distanceFieldShader = preferDistanceFieldShader ? Shader.Find("TextMeshPro/Distance Field") : null;
            if (distanceFieldShader != null && baseMaterial.shader != distanceFieldShader)
            {
                // Unity 6.0 can discard destination-only shader properties when
                // CopyPropertiesFromMaterial copies a Mobile SDF material. Clone
                // first, then switch shader so FaceTex / glow / bevel stay writable.
                material.shader = distanceFieldShader;
            }

            var mainTexture = baseMaterial.GetTexture(ShaderUtilities.ID_MainTex);
            if (mainTexture != null && material.HasProperty(ShaderUtilities.ID_MainTex))
            {
                material.SetTexture(ShaderUtilities.ID_MainTex, mainTexture);
            }

            return material;
        }

        /// <summary>
        /// Warning: Unity may replace special characters in imported font FamilyName with spaces.
        /// Normalize the font name before lookup so the PSD font can still be resolved.
        /// </summary>
        /// <param name="fontName"></param>
        /// <returns></returns>
        internal static string GetFixedFontName(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName)) return string.Empty;
            string fixedFontName = Regex.Replace(fontName, "[^A-Za-z0-9]+", " ");
            return Regex.Replace(fixedFontName, "\\s+", " ").Trim();
        }

        private const int ExactFontNameMatchScore = 3;

        private static string GetCompactFontName(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName)) return string.Empty;
            return Regex.Replace(GetFixedFontName(fontName), "\\s+", string.Empty);
        }

        private static int GetFontNameMatchScore(string candidate, string fontName, string fixedFontName, string compactFontName)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return 0;

            string trimmedCandidate = candidate.Trim();
            if (string.Equals(trimmedCandidate, fontName, StringComparison.OrdinalIgnoreCase))
            {
                return ExactFontNameMatchScore;
            }

            string fixedCandidate = GetFixedFontName(trimmedCandidate);
            if (string.Equals(trimmedCandidate, fixedFontName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fixedCandidate, fontName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fixedCandidate, fixedFontName, StringComparison.OrdinalIgnoreCase))
            {
                return ExactFontNameMatchScore - 1;
            }

            string compactCandidate = GetCompactFontName(fixedCandidate);
            if (!string.IsNullOrEmpty(compactCandidate)
                && string.Equals(compactCandidate, compactFontName, StringComparison.OrdinalIgnoreCase))
            {
                return ExactFontNameMatchScore - 2;
            }

            return 0;
        }

        private static int GetBestFontNameMatchScore(string fontName, string fixedFontName, string compactFontName, params string[] candidates)
        {
            int bestScore = 0;
            foreach (var candidate in candidates)
            {
                int matchScore = GetFontNameMatchScore(candidate, fontName, fixedFontName, compactFontName);
                if (matchScore > bestScore)
                {
                    bestScore = matchScore;
                    if (bestScore >= ExactFontNameMatchScore)
                    {
                        break;
                    }
                }
            }

            return bestScore;
        }

        // Unity exposes the imported family name, but the editable Font Names aliases live in the .meta file.
        private static string[] GetFontLookupNames(string fontPath, TrueTypeFontImporter fontImporter, UnityEngine.Font fontAsset)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (fontImporter != null && !string.IsNullOrWhiteSpace(fontImporter.fontTTFName))
            {
                candidates.Add(fontImporter.fontTTFName.Trim());
            }

            if (fontAsset != null && !string.IsNullOrWhiteSpace(fontAsset.name))
            {
                candidates.Add(fontAsset.name.Trim());
            }

            string fileName = Path.GetFileNameWithoutExtension(fontPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                candidates.Add(fileName.Trim());
            }

            foreach (var metaFontName in ReadFontNamesFromMeta(fontPath))
            {
                candidates.Add(metaFontName);
            }

            return candidates.ToArray();
        }

        private static IEnumerable<string> ReadFontNamesFromMeta(string fontPath)
        {
            if (string.IsNullOrWhiteSpace(fontPath))
            {
                yield break;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                yield break;
            }

            string relativeMetaPath = $"{fontPath}.meta".Replace('/', Path.DirectorySeparatorChar);
            string metaPath = Path.Combine(projectRoot, relativeMetaPath);
            if (!File.Exists(metaPath))
            {
                yield break;
            }

            bool readingFontNames = false;
            foreach (var rawLine in File.ReadLines(metaPath))
            {
                string trimmedLine = rawLine.Trim();
                if (!readingFontNames)
                {
                    if (string.Equals(trimmedLine, "fontNames:", StringComparison.Ordinal))
                    {
                        readingFontNames = true;
                        continue;
                    }

                    if (string.Equals(trimmedLine, "fontNames: []", StringComparison.Ordinal))
                    {
                        yield break;
                    }

                    continue;
                }

                if (trimmedLine.Length == 0)
                {
                    continue;
                }

                if (!trimmedLine.StartsWith("-", StringComparison.Ordinal))
                {
                    yield break;
                }

                string fontName = trimmedLine.Substring(1).Trim();
                if (fontName.Length >= 2)
                {
                    bool isDoubleQuoted = fontName.StartsWith("\"", StringComparison.Ordinal) && fontName.EndsWith("\"", StringComparison.Ordinal);
                    bool isSingleQuoted = fontName.StartsWith("'", StringComparison.Ordinal) && fontName.EndsWith("'", StringComparison.Ordinal);
                    if (isDoubleQuoted || isSingleQuoted)
                    {
                        fontName = fontName.Substring(1, fontName.Length - 2).Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(fontName))
                {
                    yield return fontName;
                }
            }
        }
        /// <summary>
        /// Find TMP_FontAsset by font name.
        /// </summary>
        /// <param name="fontName"></param>
        /// <returns></returns>
        internal static TMP_FontAsset FindTMPFontAsset(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName)) return null;
            string fixedFontName = GetFixedFontName(fontName);
            string compactFontName = GetCompactFontName(fontName);
            var sourceFont = FindFontAsset(fontName);
            string sourceFontGuid = null;
            if (sourceFont != null)
            {
                var sourceFontPath = AssetDatabase.GetAssetPath(sourceFont);
                if (!string.IsNullOrWhiteSpace(sourceFontPath))
                {
                    sourceFontGuid = AssetDatabase.AssetPathToGUID(sourceFontPath);
                }
            }

            TMP_FontAsset familyMatch = null;
            int familyMatchScore = 0;
            var fontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var guid in fontGuids)
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (font == null)
                {
                    continue;
                }

                if (sourceFont != null && IsTMPFontFromSource(font, sourceFont, sourceFontGuid))
                {
                    EnsureTMPFontAssetResources(font);
                    return font;
                }

                int matchScore = GetBestFontNameMatchScore(
                    fontName,
                    fixedFontName,
                    compactFontName,
                    font.faceInfo.familyName,
                    font.name,
                    font.sourceFontFile != null ? font.sourceFontFile.name : null);

                if (matchScore > familyMatchScore)
                {
                    familyMatch = font;
                    familyMatchScore = matchScore;
                }
            }

            if (familyMatch != null)
            {
                EnsureTMPFontAssetResources(familyMatch);
                return familyMatch;
            }

            return sourceFont != null ? CreateTMPFontAsset(sourceFont) : null;
        }
        private static bool IsTMPFontFromSource(TMP_FontAsset font, UnityEngine.Font sourceFont, string sourceFontGuid)
        {
            if (font == null || sourceFont == null) return false;
            if (font.sourceFontFile == sourceFont) return true;

            if (!string.IsNullOrWhiteSpace(sourceFontGuid))
            {
                if (string.Equals(font.creationSettings.sourceFontFileGUID, sourceFontGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (font.sourceFontFile != null)
                {
                    var existingSourcePath = AssetDatabase.GetAssetPath(font.sourceFontFile);
                    if (!string.IsNullOrWhiteSpace(existingSourcePath) &&
                        string.Equals(AssetDatabase.AssetPathToGUID(existingSourcePath), sourceFontGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        private static TMP_FontAsset CreateTMPFontAsset(
            UnityEngine.Font sourceFont,
            int minimumAtlasPadding = 9,
            int samplingPointSize = 90)
        {
            if (sourceFont == null) return null;
            if (TMP_Settings.instance == null)
            {
                Debug.LogWarning("Unable to create TMP font asset because TMP Essential Resources are missing.");
                return null;
            }

#if !UNITY_6000_6_OR_NEWER
            ShaderUtilities.GetShaderPropertyIDs();
#endif
            string sourceFontPath = AssetDatabase.GetAssetPath(sourceFont);
            if (string.IsNullOrWhiteSpace(sourceFontPath))
            {
                return null;
            }

            var fontImporter = AssetImporter.GetAtPath(sourceFontPath) as TrueTypeFontImporter;
            if (fontImporter != null && !fontImporter.includeFontData)
            {
                fontImporter.includeFontData = true;
                fontImporter.SaveAndReimport();
                sourceFont = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(sourceFontPath);
                if (sourceFont == null)
                {
                    return null;
                }
            }

            string sourceFontGuid = AssetDatabase.AssetPathToGUID(sourceFontPath);
            minimumAtlasPadding = Mathf.Max(9, minimumAtlasPadding);
            samplingPointSize = Mathf.Max(16, samplingPointSize);
            var existingFontAsset = FindExistingTMPFontBySource(sourceFont, sourceFontGuid, minimumAtlasPadding);
            if (existingFontAsset != null)
            {
                EnsureTMPFontAssetResources(existingFontAsset);
                return existingFontAsset;
            }

            string folderPath = Path.GetDirectoryName(sourceFontPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            string assetBaseName = Path.GetFileNameWithoutExtension(sourceFontPath);
            string fontAssetName = minimumAtlasPadding > 9
                ? $"{assetBaseName} PSD SDF P{minimumAtlasPadding}"
                : $"{assetBaseName} SDF";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{fontAssetName}.asset");
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                samplingPointSize,
                minimumAtlasPadding,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                return null;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            var atlasTexture = fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 ? fontAsset.atlasTextures[0] : null;
            if (atlasTexture != null)
            {
                atlasTexture.name = $"{assetBaseName} Atlas";
            }

            var originalMaterial = fontAsset.material;
            var fontMaterial = CreateTMPMaterialCopy(originalMaterial, true);
            if (fontMaterial != null)
            {
                fontMaterial.name = $"{assetBaseName} Atlas Material";
                if (atlasTexture != null && fontMaterial.HasProperty(ShaderUtilities.ID_MainTex))
                {
                    fontMaterial.SetTexture(ShaderUtilities.ID_MainTex, atlasTexture);
                }
                if (fontMaterial.HasProperty(ShaderUtilities.ID_TextureWidth))
                {
                    fontMaterial.SetFloat(ShaderUtilities.ID_TextureWidth, fontAsset.atlasWidth);
                }
                if (fontMaterial.HasProperty(ShaderUtilities.ID_TextureHeight))
                {
                    fontMaterial.SetFloat(ShaderUtilities.ID_TextureHeight, fontAsset.atlasHeight);
                }
                if (fontMaterial.HasProperty(ShaderUtilities.ID_GradientScale))
                {
                    fontMaterial.SetFloat(ShaderUtilities.ID_GradientScale, fontAsset.atlasPadding + 1f);
                }
                if (fontMaterial.HasProperty(ShaderUtilities.ID_WeightNormal))
                {
                    fontMaterial.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                }
                if (fontMaterial.HasProperty(ShaderUtilities.ID_WeightBold))
                {
                    fontMaterial.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
                }
                fontAsset.material = fontMaterial;
            }

            AssetDatabase.CreateAsset(fontAsset, assetPath);
            if (atlasTexture != null)
            {
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }
            if (fontAsset.material != null)
            {
                string fontMaterialPath = GetSiblingMaterialAssetPath(assetPath, fontAsset.material.name, true);
                AssetDatabase.CreateAsset(fontAsset.material, fontMaterialPath);
            }
            if (originalMaterial != null && originalMaterial != fontAsset.material)
            {
                UnityEngine.Object.DestroyImmediate(originalMaterial);
            }

            var creationSettings = fontAsset.creationSettings;
            creationSettings.sourceFontFileName = sourceFont.name;
            creationSettings.sourceFontFileGUID = sourceFontGuid;
            creationSettings.pointSizeSamplingMode = 0;
            creationSettings.pointSize = Mathf.RoundToInt(fontAsset.faceInfo.pointSize);
            creationSettings.padding = fontAsset.atlasPadding;
            creationSettings.packingMode = 0;
            creationSettings.atlasWidth = fontAsset.atlasWidth;
            creationSettings.atlasHeight = fontAsset.atlasHeight;
            creationSettings.characterSetSelectionMode = 7;
            creationSettings.characterSequence = string.Empty;
            creationSettings.referencedFontAssetGUID = string.Empty;
            creationSettings.referencedTextAssetGUID = string.Empty;
            creationSettings.fontStyle = 0;
            creationSettings.fontStyleModifier = 0f;
            creationSettings.renderMode = (int)fontAsset.atlasRenderMode;
            creationSettings.includeFontFeatures = false;
            fontAsset.creationSettings = creationSettings;

            EditorUtility.SetDirty(fontAsset);
            if (atlasTexture != null)
            {
                EditorUtility.SetDirty(atlasTexture);
            }
            if (fontAsset.material != null)
            {
                EditorUtility.SetDirty(fontAsset.material);
            }

            AssetDatabase.SaveAssets();
            var persistedFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (persistedFontAsset == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                persistedFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            }
            if (persistedFontAsset == null) persistedFontAsset = fontAsset;
            EnsureTMPFontAssetResources(persistedFontAsset);
            return persistedFontAsset;
        }
        private static TMP_FontAsset FindExistingTMPFontBySource(
            UnityEngine.Font sourceFont,
            string sourceFontGuid,
            int minimumAtlasPadding = 0)
        {
            if (sourceFont == null) return null;

            TMP_FontAsset best = null;
            var fontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var guid in fontGuids)
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (!IsTMPFontFromSource(font, sourceFont, sourceFontGuid)
                    || font.atlasPadding < minimumAtlasPadding)
                    continue;
                if (best == null || font.atlasPadding < best.atlasPadding)
                    best = font;
            }

            return best;
        }
        private static TMP_FontAsset EnsureTMPEffectFontCapacity(
            TMP_FontAsset fontAsset,
            float fontSize,
            in TextLayerInfo textInfo)
        {
            if (fontAsset == null || !HasTMPDistanceFieldEffects(in textInfo))
                return fontAsset;

            int requiredPadding = CalculateRequiredTMPEffectAtlasPadding(fontAsset, fontSize, in textInfo);
            if (fontAsset.atlasPadding >= requiredPadding)
                return fontAsset;

            UnityEngine.Font sourceFont = fontAsset.sourceFontFile;
            if (sourceFont == null && !string.IsNullOrWhiteSpace(fontAsset.creationSettings.sourceFontFileGUID))
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(fontAsset.creationSettings.sourceFontFileGUID);
                sourceFont = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(sourcePath);
            }
            if (sourceFont == null)
                return fontAsset;

            string sourcePathForGuid = AssetDatabase.GetAssetPath(sourceFont);
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePathForGuid);
            var existing = FindExistingTMPFontBySource(sourceFont, sourceGuid, requiredPadding);
            if (existing != null)
                return existing;

            int pointSize = fontAsset.faceInfo.pointSize > 0f
                ? Mathf.RoundToInt(fontAsset.faceInfo.pointSize)
                : 90;
            return CreateTMPFontAsset(sourceFont, requiredPadding, pointSize) ?? fontAsset;
        }
        private static bool HasTMPDistanceFieldEffects(in TextLayerInfo textInfo)
        {
            return textInfo.HasOutline || textInfo.HasShadow || textInfo.HasGlow || textInfo.HasBevel;
        }
        internal static int CalculateRequiredTMPEffectAtlasPadding(
            TMP_FontAsset fontAsset,
            float fontSize,
            in TextLayerInfo textInfo)
        {
            if (!HasTMPDistanceFieldEffects(in textInfo))
                return fontAsset != null ? Mathf.Max(9, fontAsset.atlasPadding) : 9;

            float effectRadius = textInfo.HasOutline ? Mathf.Max(0f, textInfo.TMPOutlineSize) : 0f;
            if (textInfo.HasShadow)
            {
                effectRadius = Mathf.Max(
                    effectRadius,
                    Mathf.Max(Mathf.Abs(textInfo.ShadowOffset.x), Mathf.Abs(textInfo.ShadowOffset.y))
                        + Mathf.Max(0f, textInfo.ShadowSoftness));
            }
            if (textInfo.OuterGlow.Enabled)
                effectRadius = Mathf.Max(effectRadius, textInfo.OuterGlow.Size);
            if (textInfo.InnerGlow.Enabled)
                effectRadius = Mathf.Max(effectRadius, textInfo.InnerGlow.Size);
            if (textInfo.HasBevel)
            {
                effectRadius = Mathf.Max(
                    effectRadius,
                    Mathf.Sqrt(textInfo.BevelSize * textInfo.BevelSize + textInfo.BevelSoften * textInfo.BevelSoften));
            }

            float atlasPointSize = fontAsset != null && fontAsset.faceInfo.pointSize > 0f
                ? fontAsset.faceInfo.pointSize
                : Mathf.Max(16f, fontSize);
            float renderedScale = Mathf.Max(0.01f, fontSize / atlasPointSize);
            int rawPadding = Mathf.CeilToInt((2f * effectRadius + 1f) / renderedScale);
            int bucket = rawPadding <= 16 ? 16 : Mathf.NextPowerOfTwo(rawPadding);
            return Mathf.Clamp(bucket, 9, 128);
        }
        private static void EnsureTMPFontHasCharacters(TMP_FontAsset fontAsset, string text)
        {
            if (fontAsset == null || string.IsNullOrEmpty(text)) return;

            EnsureTMPFontAssetResources(fontAsset);
            if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
            if (fontAsset.HasCharacters(text)) return;

            fontAsset.TryAddCharacters(text, out _);
            EnsureTMPFontAssetResources(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            if (fontAsset.material != null)
            {
                EditorUtility.SetDirty(fontAsset.material);
            }
            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    if (fontAsset.atlasTextures[i] != null)
                    {
                        EditorUtility.SetDirty(fontAsset.atlasTextures[i]);
                    }
                }
            }
            AssetDatabase.SaveAssets();
        }
        /// <summary>
        /// Find Font Asset by font name.
        /// </summary>
        /// <param name="fontName"></param>
        /// <returns></returns>
        internal static UnityEngine.Font FindFontAsset(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName)) return null;
            string fixedFontName = GetFixedFontName(fontName);
            string compactFontName = GetCompactFontName(fontName);
            var fontGuids = AssetDatabase.FindAssets("t:font");
            UnityEngine.Font matchedFont = null;
            int matchedFontScore = 0;
            foreach (var guid in fontGuids)
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(guid);
                var fontImporter = AssetImporter.GetAtPath(fontPath) as TrueTypeFontImporter;
                var fontAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(fontPath);
                if (fontAsset == null)
                {
                    continue;
                }

                int matchScore = GetBestFontNameMatchScore(
                    fontName,
                    fixedFontName,
                    compactFontName,
                    GetFontLookupNames(fontPath, fontImporter, fontAsset));

                if (matchScore > matchedFontScore)
                {
                    matchedFont = fontAsset;
                    matchedFontScore = matchScore;
                    if (matchedFontScore >= ExactFontNameMatchScore)
                    {
                        break;
                    }
                }
            }
            return matchedFont;
        }

        internal static UnityEngine.Color LayerNode2Color(PsdLayerNode fillColor, Color defaultColor)
        {
            if (fillColor != null && fillColor.TryGetLayerColor(out var layerColor))
            {
                return layerColor;
            }
            return defaultColor;
        }
        /// <summary>
        /// Export the designer-facing rules document.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        internal void ExportReadmeDoc()
        {
            var exportDir = EditorUtility.SaveFolderPanel("选择文档导出路径", Application.dataPath, null);
            if (string.IsNullOrWhiteSpace(exportDir) || !Directory.Exists(exportDir))
            {
                return;
            }

            var docFile = Path.Combine(exportDir, "Psd2UGUI设计师使用文档.doc");
            var strBuilder = new StringBuilder();
            strBuilder.AppendLine("使用说明:");
            strBuilder.AppendLine(this.readmeDoc);
            strBuilder.AppendLine(Environment.NewLine + Environment.NewLine);
            strBuilder.AppendLine("UI类型标识: 图层/组命名以'.类型'结尾");
            strBuilder.AppendLine("UI类型标识列表:");

            foreach (var rule in rules)
            {
                if (rule.UIType == GUIType.Null) continue;

                strBuilder.AppendLine($"{rule.UIType}: {rule.Comment}");
                strBuilder.Append("类型标识: ");
                foreach (var tag in rule.TypeMatches)
                {
                    strBuilder.Append($".{tag}, ");
                }
                strBuilder.AppendLine();
                strBuilder.AppendLine();
            }

            try
            {
                File.WriteAllText(docFile, strBuilder.ToString(), Encoding.UTF8);
                EditorUtility.RevealInFinder(docFile);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

        }

        private sealed class PsTagMenuItem
        {
            public string CanonicalId;
            public string Suffix;
            public string Label;
        }

        private sealed class PsTagAliasRule
        {
            public string Main;
            public string Export;
            public string Role;
            public string ImageType;
            public string TextBackend;
        }

        private sealed class PsRasterizeAsImageConfig
        {
            public readonly HashSet<string> MainIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> ExportIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> RoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string GetPsFamilyLabel(string familyKey)
        {
            switch (familyKey)
            {
                case "main":
                    return "结构标签";
                case "export":
                    return "导出标签";
                case "textBackend":
                    return "文本后端";
                case "imageType":
                    return "Image Type";
                case "role":
                    return "角色标签";
                default:
                    return familyKey;
            }
        }

        private static string GetPsFallbackCanonicalToken(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Image:
                    return "img";
                case GUIType.RawImage:
                    return "rimg";
                case GUIType.Text:
                case GUIType.TMPText:
                    return "txt";
                case GUIType.Mask:
                    return "msk";
                case GUIType.FillColor:
                    return "col";
                case GUIType.Background:
                    return "bg";
                case GUIType.Button:
                case GUIType.TMPButton:
                    return "bt";
                case GUIType.Button_Highlight:
                    return "onover";
                case GUIType.Button_Press:
                    return "press";
                case GUIType.Button_Select:
                    return "select";
                case GUIType.Button_Disable:
                    return "disable";
                case GUIType.Button_Text:
                    return "bttxt";
                case GUIType.Dropdown:
                case GUIType.TMPDropdown:
                    return "dpd";
                case GUIType.Dropdown_Label:
                    return "dpdlb";
                case GUIType.Dropdown_Arrow:
                    return "dpdicon";
                case GUIType.InputField:
                case GUIType.TMPInputField:
                    return "ipt";
                case GUIType.InputField_Placeholder:
                    return "placeholder";
                case GUIType.InputField_Text:
                    return "ipttxt";
                case GUIType.Toggle:
                case GUIType.TMPToggle:
                    return "tg";
                case GUIType.Toggle_Checkmark:
                    return "mark";
                case GUIType.Toggle_Label:
                    return "tglb";
                case GUIType.Slider:
                    return "sld";
                case GUIType.Slider_Fill:
                    return "fill";
                case GUIType.Slider_Handle:
                    return "handle";
                case GUIType.ScrollView:
                    return "sv";
                case GUIType.Panel:
                    return "panel";
                case GUIType.ToggleGroup:
                    return "tgg";
                case GUIType.ScrollView_Viewport:
                    return "vpt";
                case GUIType.ScrollView_HorizontalBarBG:
                    return "hbarbg";
                case GUIType.ScrollView_HorizontalBar:
                    return "hbar";
                case GUIType.ScrollView_VerticalBarBG:
                    return "vbarbg";
                case GUIType.ScrollView_VerticalBar:
                    return "vbar";
                default:
                    return uiType.ToString().ToLowerInvariant();
            }
        }

        private string GetCanonicalRuleToken(GUIType uiType)
        {
            var rule = GetRule(uiType);
            if (rule?.TypeMatches != null)
            {
                for (int i = 0; i < rule.TypeMatches.Length; i++)
                {
                    var token = rule.TypeMatches[i];
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token.Trim().TrimStart('.').ToLowerInvariant();
                    }
                }
            }
            return GetPsFallbackCanonicalToken(uiType);
        }

        private static bool TryGetPsFamilyKey(GUIType uiType, out string familyKey)
        {
            switch (uiType)
            {
                case GUIType.Image:
                case GUIType.RawImage:
                case GUIType.Text:
                case GUIType.Mask:
                case GUIType.FillColor:
                case GUIType.Panel:
                case GUIType.ToggleGroup:
                case GUIType.Button:
                case GUIType.Dropdown:
                case GUIType.InputField:
                case GUIType.Toggle:
                case GUIType.Slider:
                case GUIType.ScrollView:
                    familyKey = "main";
                    return true;

                case GUIType.Background:
                case GUIType.Button_Highlight:
                case GUIType.Button_Press:
                case GUIType.Button_Select:
                case GUIType.Button_Disable:
                case GUIType.Button_Text:
                case GUIType.Dropdown_Label:
                case GUIType.Dropdown_Arrow:
                case GUIType.InputField_Placeholder:
                case GUIType.InputField_Text:
                case GUIType.Toggle_Checkmark:
                case GUIType.Toggle_Label:
                case GUIType.Slider_Fill:
                case GUIType.Slider_Handle:
                case GUIType.ScrollView_Viewport:
                case GUIType.ScrollView_HorizontalBarBG:
                case GUIType.ScrollView_HorizontalBar:
                case GUIType.ScrollView_VerticalBarBG:
                case GUIType.ScrollView_VerticalBar:
                    familyKey = "role";
                    return true;

                default:
                    familyKey = null;
                    return false;
            }
        }

        private static bool ShouldPsRasterizeAsImageMainType(GUIType uiType)
        {
            return uiType == GUIType.Image || uiType == GUIType.RawImage;
        }

        private static bool ShouldPsRasterizeAsImageSemanticType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Background:
                case GUIType.Button_Highlight:
                case GUIType.Button_Press:
                case GUIType.Button_Select:
                case GUIType.Button_Disable:
                case GUIType.Dropdown_Arrow:
                case GUIType.Toggle_Checkmark:
                case GUIType.Slider_Fill:
                case GUIType.Slider_Handle:
                case GUIType.ScrollView_HorizontalBarBG:
                case GUIType.ScrollView_HorizontalBar:
                case GUIType.ScrollView_VerticalBarBG:
                case GUIType.ScrollView_VerticalBar:
                    return true;

                default:
                    return false;
            }
        }

        private static GUIType ConvertTmpCompositeToBaseMainType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.TMPText:
                    return GUIType.Text;
                case GUIType.TMPButton:
                    return GUIType.Button;
                case GUIType.TMPDropdown:
                    return GUIType.Dropdown;
                case GUIType.TMPInputField:
                    return GUIType.InputField;
                case GUIType.TMPToggle:
                    return GUIType.Toggle;
                default:
                    return uiType;
            }
        }

        private static string EscapeJsString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string NormalizePsTooltipLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            return label
                .Replace("\\r", " ")
                .Replace("\\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static void AddPsMenuItem(Dictionary<string, List<PsTagMenuItem>> familyItems, string familyKey, string canonicalId, string label)
        {
            if (string.IsNullOrWhiteSpace(familyKey) || string.IsNullOrWhiteSpace(canonicalId)) return;

            if (!familyItems.TryGetValue(familyKey, out var items))
            {
                items = new List<PsTagMenuItem>();
                familyItems.Add(familyKey, items);
            }

            if (items.Any(item => string.Equals(item.CanonicalId, canonicalId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            items.Add(new PsTagMenuItem()
            {
                CanonicalId = canonicalId,
                Suffix = "." + canonicalId,
                Label = string.IsNullOrWhiteSpace(label) ? canonicalId : NormalizePsTooltipLabel(label)
            });
        }

        private static void AddPsAliasRule(
            Dictionary<string, PsTagAliasRule> aliasRules,
            string alias,
            string main = null,
            string export = null,
            string role = null,
            string imageType = null,
            string textBackend = null)
        {
            if (string.IsNullOrWhiteSpace(alias)) return;

            alias = alias.Trim().TrimStart('.').ToLowerInvariant();
            if (!aliasRules.TryGetValue(alias, out var aliasRule))
            {
                aliasRule = new PsTagAliasRule();
                aliasRules.Add(alias, aliasRule);
            }

            // 同一个 tag 配进多条规则时，Unity 解析取首条命中；这里也必须首条优先，
            // 否则 PS 菜单加的后缀在 Unity 里会被解析成别的控件。
            if (!string.IsNullOrWhiteSpace(main) && string.IsNullOrWhiteSpace(aliasRule.Main)) aliasRule.Main = main;
            if (!string.IsNullOrWhiteSpace(export) && string.IsNullOrWhiteSpace(aliasRule.Export)) aliasRule.Export = export;
            if (!string.IsNullOrWhiteSpace(role) && string.IsNullOrWhiteSpace(aliasRule.Role)) aliasRule.Role = role;
            if (!string.IsNullOrWhiteSpace(imageType) && string.IsNullOrWhiteSpace(aliasRule.ImageType)) aliasRule.ImageType = imageType;
            if (!string.IsNullOrWhiteSpace(textBackend) && string.IsNullOrWhiteSpace(aliasRule.TextBackend)) aliasRule.TextBackend = textBackend;
        }

        private void RegisterRuleForPsTagConfig(
            UGUIParseRule rule,
            Dictionary<string, List<PsTagMenuItem>> familyItems,
            Dictionary<string, PsTagAliasRule> aliasRules,
            PsRasterizeAsImageConfig rasterizeAsImageConfig)
        {
            if (rule == null || rule.UIType == GUIType.Null || rule.TypeMatches == null || rule.TypeMatches.Length == 0)
            {
                return;
            }

            var canonicalToken = GetCanonicalRuleToken(rule.UIType);
            var label = string.IsNullOrWhiteSpace(rule.UITypeDesc)
                ? rule.UIType.ToString()
                : $"{rule.UIType} {rule.UITypeDesc}";

            switch (rule.UIType)
            {
                case GUIType.TMPText:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                    var baseMainType = ConvertTmpCompositeToBaseMainType(rule.UIType);
                    var baseCanonicalToken = GetCanonicalRuleToken(baseMainType);
                    for (int i = 0; i < rule.TypeMatches.Length; i++)
                    {
                        AddPsAliasRule(aliasRules, rule.TypeMatches[i], main: baseCanonicalToken, textBackend: "tmp");
                    }
                    return;

                case GUIType.Image:
                    AddPsMenuItem(familyItems, "export", canonicalToken, label);
                    for (int i = 0; i < rule.TypeMatches.Length; i++)
                    {
                        AddPsAliasRule(aliasRules, rule.TypeMatches[i], export: canonicalToken);
                    }
                    rasterizeAsImageConfig.ExportIds.Add(canonicalToken);
                    return;
            }

            if (!TryGetPsFamilyKey(rule.UIType, out var familyKey))
            {
                return;
            }

            AddPsMenuItem(familyItems, familyKey, canonicalToken, label);
            for (int i = 0; i < rule.TypeMatches.Length; i++)
            {
                var alias = rule.TypeMatches[i];
                if (string.IsNullOrWhiteSpace(alias)) continue;

                if (string.Equals(familyKey, "main", StringComparison.OrdinalIgnoreCase))
                {
                    AddPsAliasRule(aliasRules, alias, main: canonicalToken);
                }
                else
                {
                    AddPsAliasRule(aliasRules, alias, role: canonicalToken);
                }
            }

            if (ShouldPsRasterizeAsImageMainType(rule.UIType))
            {
                rasterizeAsImageConfig.MainIds.Add(canonicalToken);
            }
            else if (ShouldPsRasterizeAsImageSemanticType(rule.UIType))
            {
                rasterizeAsImageConfig.RoleIds.Add(canonicalToken);
            }
        }

        private string BuildPsTagConfigBlock()
        {
            var familyItems = new Dictionary<string, List<PsTagMenuItem>>(StringComparer.OrdinalIgnoreCase);
            var aliasRules = new Dictionary<string, PsTagAliasRule>(StringComparer.OrdinalIgnoreCase);
            var rasterizeAsImageConfig = new PsRasterizeAsImageConfig();

            if (rules != null)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    RegisterRuleForPsTagConfig(rules[i], familyItems, aliasRules, rasterizeAsImageConfig);
                }
            }

            AddPsMenuItem(familyItems, "textBackend", "tmp", "TMP文本后端");
            AddPsMenuItem(familyItems, "textBackend", "ugui", "原生文本后端");
            AddPsAliasRule(aliasRules, "tmp", textBackend: "tmp");
            AddPsAliasRule(aliasRules, "ugui", textBackend: "ugui");

            AddPsMenuItem(familyItems, "imageType", "simple", "普通");
            AddPsMenuItem(familyItems, "imageType", "sliced", "九宫格");
            AddPsMenuItem(familyItems, "imageType", "tiled", "平铺");
            AddPsMenuItem(familyItems, "imageType", "filled", "填充");
            AddPsAliasRule(aliasRules, "simple", imageType: "simple");
            AddPsAliasRule(aliasRules, "sliced", imageType: "sliced");
            AddPsAliasRule(aliasRules, "tiled", imageType: "tiled");
            AddPsAliasRule(aliasRules, "filled", imageType: "filled");

            var familyOrder = new[] { "export", "main", "textBackend", "imageType", "role" };
            var builder = new StringBuilder();
            builder.AppendLine("var TAG_CONFIG = {");
            builder.AppendLine("    canonicalOrder: [\"export\", \"main\", \"textBackend\", \"imageType\", \"role\"],");
            builder.AppendLine("    reuseMarkers: [");
            builder.AppendLine("        { \"id\": \"ref\", \"prefix\": \"ref \", \"label\": \"ref 复用共享图片资源\" },");
            builder.AppendLine("        { \"id\": \"refp\", \"prefix\": \"refp \", \"label\": \"refp 复用共享预制体\" }");
            builder.AppendLine("    ],");
            builder.AppendLine("    familyLabels: {");
            for (int familyIndex = 0; familyIndex < familyOrder.Length; familyIndex++)
            {
                var familyKey = familyOrder[familyIndex];
                builder.Append("        \"").Append(familyKey).Append("\": \"").Append(EscapeJsString(GetPsFamilyLabel(familyKey))).Append("\"");
                builder.AppendLine(familyIndex < familyOrder.Length - 1 ? "," : string.Empty);
            }
            builder.AppendLine("    },");
            builder.AppendLine("    families: {");
            for (int familyIndex = 0; familyIndex < familyOrder.Length; familyIndex++)
            {
                var familyKey = familyOrder[familyIndex];
                familyItems.TryGetValue(familyKey, out var items);
                items = items ?? new List<PsTagMenuItem>();
                builder.Append("        \"").Append(familyKey).Append("\": [").AppendLine();
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    var item = items[itemIndex];
                    builder.Append("            { \"id\": \"")
                        .Append(EscapeJsString(item.CanonicalId))
                        .Append("\", \"suffix\": \"")
                        .Append(EscapeJsString(item.Suffix))
                        .Append("\", \"label\": \"")
                        .Append(EscapeJsString(item.Label))
                        .Append("\" }");
                    builder.AppendLine(itemIndex < items.Count - 1 ? "," : string.Empty);
                }
                builder.Append("        ]");
                builder.AppendLine(familyIndex < familyOrder.Length - 1 ? "," : string.Empty);
            }
            builder.AppendLine("    },");
            builder.AppendLine("    rasterizeAsImage: {");

            void AppendBooleanLookup(string key, IEnumerable<string> values, bool appendComma)
            {
                var orderedValues = values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                builder.Append("        \"").Append(key).Append("\": {");
                if (orderedValues.Length > 0)
                {
                    builder.AppendLine();
                    for (int valueIndex = 0; valueIndex < orderedValues.Length; valueIndex++)
                    {
                        builder.Append("            \"")
                            .Append(EscapeJsString(orderedValues[valueIndex]))
                            .Append("\": true");
                        builder.AppendLine(valueIndex < orderedValues.Length - 1 ? "," : string.Empty);
                    }
                    builder.Append("        }");
                }
                else
                {
                    builder.Append("}");
                }

                builder.AppendLine(appendComma ? "," : string.Empty);
            }

            AppendBooleanLookup("main", rasterizeAsImageConfig.MainIds, true);
            AppendBooleanLookup("export", rasterizeAsImageConfig.ExportIds, true);
            AppendBooleanLookup("role", rasterizeAsImageConfig.RoleIds, false);
            builder.AppendLine("    },");
            builder.AppendLine("    aliasMap: {");
            var orderedAliases = aliasRules.Keys.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int aliasIndex = 0; aliasIndex < orderedAliases.Length; aliasIndex++)
            {
                var alias = orderedAliases[aliasIndex];
                var aliasRule = aliasRules[alias];
                builder.Append("        \"").Append(EscapeJsString(alias)).Append("\": {");
                bool hasAnyField = false;
                void AppendField(string fieldName, string fieldValue)
                {
                    if (string.IsNullOrWhiteSpace(fieldValue)) return;
                    if (hasAnyField) builder.Append(", ");
                    builder.Append("\"").Append(fieldName).Append("\": \"").Append(EscapeJsString(fieldValue)).Append("\"");
                    hasAnyField = true;
                }

                AppendField("main", aliasRule.Main);
                AppendField("export", aliasRule.Export);
                AppendField("role", aliasRule.Role);
                AppendField("imageType", aliasRule.ImageType);
                AppendField("textBackend", aliasRule.TextBackend);
                builder.Append(" }");
                builder.AppendLine(aliasIndex < orderedAliases.Length - 1 ? "," : string.Empty);
            }
            builder.AppendLine("    }");
            builder.Append("};");
            return builder.ToString();
        }

        private string BuildPsReuseDefaultSettingsBlock()
        {
            var builder = new StringBuilder();
            builder.AppendLine("var REUSE_DEFAULT_SETTINGS = {");
            builder.Append("    imageRoot: \"")
                .Append(EscapeJsString(GetPhotoshopDefaultReuseDirectory(sharedAssetsOutput)))
                .AppendLine("\",");
            builder.Append("    prefabRoot: \"")
                .Append(EscapeJsString(GetPhotoshopDefaultReuseDirectory(sharedPrefabOutput)))
                .AppendLine("\"");
            builder.Append("};");
            return builder.ToString();
        }

        private static string GetPhotoshopDefaultReuseDirectory(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return string.Empty;
            }

            var normalizedPath = configuredPath.Trim();
            string fullPath;
            if (Path.IsPathRooted(normalizedPath))
            {
                fullPath = normalizedPath;
            }
            else
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    return string.Empty;
                }

                fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));
            }

            return fullPath.Replace('\\', '/');
        }

        private static bool ReplaceScriptBlock(string scriptPath, string blockPattern, string replacementBlock, string blockName, out string error)
        {
            error = null;
            string fullPath = Psd2UIFormPluginPathUtility.AssetPathToAbsolutePath(scriptPath);
            if (!File.Exists(fullPath))
            {
                error = $"PS脚本文件不存在：\n{scriptPath}";
                return false;
            }

            var scriptContent = File.ReadAllText(fullPath, Encoding.UTF8);
            var configPattern = new Regex(blockPattern, RegexOptions.Multiline);
            if (!configPattern.IsMatch(scriptContent))
            {
                error = $"PS脚本中未找到 {blockName}：\n{scriptPath}";
                return false;
            }

            var newContent = configPattern.Replace(scriptContent, replacementBlock, 1);
            File.WriteAllText(fullPath, newContent, Encoding.UTF8);
            AssetDatabase.ImportAsset(scriptPath);
            return true;
        }

        private static bool ReplacePsTagConfigBlock(string scriptPath, string tagConfigBlock, out string error)
        {
            return ReplaceScriptBlock(scriptPath, @"var\s+TAG_CONFIG\s*=\s*\{[\s\S]*?\};", tagConfigBlock, "TAG_CONFIG", out error);
        }

        private static bool ReplacePsReuseDefaultSettingsBlock(string scriptPath, string reuseDefaultsBlock, out string error)
        {
            return ReplaceScriptBlock(scriptPath, @"var\s+REUSE_DEFAULT_SETTINGS\s*=\s*\{[\s\S]*?\};", reuseDefaultsBlock, "REUSE_DEFAULT_SETTINGS", out error);
        }

        private static string NormalizeRegistryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Path.GetDirectoryName(normalized);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return null;
            }
        }

        private static void TryAddPhotoshopInstallDirectory(HashSet<string> installDirectories, string path)
        {
            var normalized = NormalizeRegistryPath(path);
            if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
            {
                return;
            }

            installDirectories.Add(normalized);
        }

        private static Type ResolveType(string fullName)
        {
            var type = Type.GetType(fullName);
            if (type != null)
            {
                return type;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static object ParseEnumValue(Type enumType, string name)
        {
            return enumType == null ? null : Enum.Parse(enumType, name);
        }

        private static object InvokeRegistryMethod(object target, string methodName, Type[] parameterTypes, params object[] args)
        {
            if (target == null)
            {
                return null;
            }

            var targetType = target as Type ?? target.GetType();
            var method = parameterTypes == null
                ? targetType.GetMethod(methodName)
                : targetType.GetMethod(methodName, parameterTypes);
            return method?.Invoke(target is Type ? null : target, args);
        }

        private static string GetRegistryValue(object key, string valueName)
        {
            return InvokeRegistryMethod(key, "GetValue", new[] { typeof(string) }, valueName) as string;
        }

        private static object OpenRegistrySubKey(object key, string subKeyName)
        {
            return InvokeRegistryMethod(key, "OpenSubKey", new[] { typeof(string) }, subKeyName);
        }

        private static string[] GetRegistrySubKeyNames(object key)
        {
            return InvokeRegistryMethod(key, "GetSubKeyNames", Type.EmptyTypes) as string[] ?? Array.Empty<string>();
        }

        private static object OpenRegistryBaseKey(string hiveName, string viewName)
        {
            var registryKeyType = ResolveType("Microsoft.Win32.RegistryKey");
            var registryHiveType = ResolveType("Microsoft.Win32.RegistryHive");
            var registryViewType = ResolveType("Microsoft.Win32.RegistryView");
            if (registryKeyType == null || registryHiveType == null || registryViewType == null)
            {
                return null;
            }

            var hive = ParseEnumValue(registryHiveType, hiveName);
            var view = ParseEnumValue(registryViewType, viewName);
            if (hive == null || view == null)
            {
                return null;
            }

            return InvokeRegistryMethod(
                registryKeyType,
                "OpenBaseKey",
                new[] { registryHiveType, registryViewType },
                hive,
                view);
        }

        private static void TryCollectPhotoshopInstallDirectory(object key, HashSet<string> installDirectories)
        {
            if (key == null)
            {
                return;
            }

            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(key, "ApplicationPath"));
            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(key, "InstallPath"));
            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(key, "Path"));
            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(key, null));
        }

        private static void CollectPhotoshopInstallsFromAdobeRegistry(string hiveName, string viewName, HashSet<string> installDirectories)
        {
            var baseKey = OpenRegistryBaseKey(hiveName, viewName);
            if (baseKey == null)
            {
                return;
            }

            using (baseKey as IDisposable)
            {
                var photoshopRoot = OpenRegistrySubKey(baseKey, @"SOFTWARE\Adobe\Photoshop");
                if (photoshopRoot == null)
                {
                    return;
                }

                using (photoshopRoot as IDisposable)
                {
                    var versionKeys = GetRegistrySubKeyNames(photoshopRoot);
                    for (int i = 0; i < versionKeys.Length; i++)
                    {
                        var versionKey = OpenRegistrySubKey(photoshopRoot, versionKeys[i]);
                        using (versionKey as IDisposable)
                        {
                            TryCollectPhotoshopInstallDirectory(versionKey, installDirectories);

                            var applicationPathKey = versionKey == null ? null : OpenRegistrySubKey(versionKey, "ApplicationPath");
                            using (applicationPathKey as IDisposable)
                            {
                                TryCollectPhotoshopInstallDirectory(applicationPathKey, installDirectories);
                            }
                        }
                    }
                }
            }
        }

        private static void CollectPhotoshopInstallsFromUninstallRegistry(string hiveName, string viewName, HashSet<string> installDirectories)
        {
            var baseKey = OpenRegistryBaseKey(hiveName, viewName);
            if (baseKey == null)
            {
                return;
            }

            using (baseKey as IDisposable)
            {
                var uninstallRoot = OpenRegistrySubKey(baseKey, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallRoot == null)
                {
                    return;
                }

                using (uninstallRoot as IDisposable)
                {
                    var subKeyNames = GetRegistrySubKeyNames(uninstallRoot);
                    for (int i = 0; i < subKeyNames.Length; i++)
                    {
                        var subKey = OpenRegistrySubKey(uninstallRoot, subKeyNames[i]);
                        using (subKey as IDisposable)
                        {
                            var displayName = GetRegistryValue(subKey, "DisplayName");
                            if (string.IsNullOrWhiteSpace(displayName) ||
                                displayName.IndexOf("Adobe Photoshop", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }

                            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(subKey, "InstallLocation"));
                            TryAddPhotoshopInstallDirectory(installDirectories, GetRegistryValue(subKey, "DisplayIcon"));
                        }
                    }
                }
            }
        }

        private static string[] FindPhotoshopScriptDirectories()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return FindPhotoshopScriptDirectoriesOnMac();
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return Array.Empty<string>();
            }

            var installDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hives = new[] { "LocalMachine", "CurrentUser" };
            var views = new[] { "Registry64", "Registry32" };

            for (int hiveIndex = 0; hiveIndex < hives.Length; hiveIndex++)
            {
                for (int viewIndex = 0; viewIndex < views.Length; viewIndex++)
                {
                    try
                    {
                        CollectPhotoshopInstallsFromAdobeRegistry(hives[hiveIndex], views[viewIndex], installDirectories);
                    }
                    catch
                    {
                    }

                    try
                    {
                        CollectPhotoshopInstallsFromUninstallRegistry(hives[hiveIndex], views[viewIndex], installDirectories);
                    }
                    catch
                    {
                    }
                }
            }

            return installDirectories
                .Select(path => Path.Combine(path, "Presets", "Scripts"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] FindPhotoshopScriptDirectoriesOnMac()
        {
            var scriptDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appDirectories = new List<string>();

            try
            {
                if (Directory.Exists("/Applications"))
                {
                    appDirectories.AddRange(Directory.GetDirectories("/Applications", "Adobe Photoshop*.app", SearchOption.TopDirectoryOnly));
                }
            }
            catch
            {
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                string userApplications = Path.Combine(home, "Applications");
                try
                {
                    if (Directory.Exists(userApplications))
                    {
                        appDirectories.AddRange(Directory.GetDirectories(userApplications, "Adobe Photoshop*.app", SearchOption.TopDirectoryOnly));
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < appDirectories.Count; i++)
            {
                TryAddMacPhotoshopScriptDirectory(scriptDirectories, appDirectories[i]);
            }

            return scriptDirectories
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void TryAddMacPhotoshopScriptDirectory(HashSet<string> scriptDirectories, string appBundlePath)
        {
            if (scriptDirectories == null || string.IsNullOrWhiteSpace(appBundlePath))
            {
                return;
            }

            var candidates = new[]
            {
                Path.Combine(appBundlePath, "Presets", "Scripts"),
                Path.Combine(appBundlePath, "Contents", "Required", "Presets", "Scripts"),
                Path.Combine(appBundlePath, "Contents", "Resources", "Presets", "Scripts")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                scriptDirectories.Add(candidate);
            }
        }

        private static void DeployScriptsToPhotoshop(out List<string> deployedScriptDirectories, out List<string> deployErrors)
        {
            deployedScriptDirectories = new List<string>();
            deployErrors = new List<string>();

            var scriptDirectories = FindPhotoshopScriptDirectories();
            if (scriptDirectories.Length == 0)
            {
                deployErrors.Add("未找到 Photoshop 安装目录，已只更新工程内 jsx 文件。");
                return;
            }

            var sourceScriptPaths = new[]
            {
                Psd2UIFormPluginPathUtility.GetPluginAbsolutePath(LayerTagMenuScriptRelativePath),
                Psd2UIFormPluginPathUtility.GetPluginAbsolutePath(LayerExportScriptRelativePath)
            };

            for (int i = 0; i < sourceScriptPaths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(sourceScriptPaths[i]) || !File.Exists(sourceScriptPaths[i]))
                {
                    deployErrors.Add($"本地脚本不存在，无法自动部署：\n{sourceScriptPaths[i] ?? "(null)"}");
                    return;
                }
            }

            for (int dirIndex = 0; dirIndex < scriptDirectories.Length; dirIndex++)
            {
                var scriptDirectory = scriptDirectories[dirIndex];

                try
                {
                    Directory.CreateDirectory(scriptDirectory);

                    for (int fileIndex = 0; fileIndex < sourceScriptPaths.Length; fileIndex++)
                    {
                        var sourcePath = sourceScriptPaths[fileIndex];
                        var targetPath = Path.Combine(scriptDirectory, Path.GetFileName(sourcePath));
                        File.Copy(sourcePath, targetPath, true);
                    }

                    deployedScriptDirectories.Add(scriptDirectory);
                }
                catch (Exception ex)
                {
                    deployErrors.Add($"覆盖 Photoshop 脚本目录失败：\n{scriptDirectory}\n{ex.Message}");
                }
            }
        }

        internal void ExportLayerTagMenuConfig()
        {
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            var tagConfigBlock = BuildPsTagConfigBlock();
            var reuseDefaultsBlock = BuildPsReuseDefaultSettingsBlock();
            var errors = new List<string>();
            var layerTagMenuScriptPath = Psd2UIFormPluginPathUtility.GetPluginAssetPath(LayerTagMenuScriptRelativePath);
            var layerExportScriptPath = Psd2UIFormPluginPathUtility.GetPluginAssetPath(LayerExportScriptRelativePath);
            var targetScripts = new[] { layerTagMenuScriptPath, layerExportScriptPath };
            for (int i = 0; i < targetScripts.Length; i++)
            {
                var scriptPath = targetScripts[i];
                if (!ReplacePsTagConfigBlock(scriptPath, tagConfigBlock, out var error))
                {
                    errors.Add(error);
                }
            }

            if (!ReplacePsReuseDefaultSettingsBlock(layerTagMenuScriptPath, reuseDefaultsBlock, out var reuseDefaultsError))
            {
                errors.Add(reuseDefaultsError);
            }

            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("导出PS脚本工具 失败", string.Join("\n\n", errors), "确定");
                return;
            }

            DeployScriptsToPhotoshop(out var deployedScriptDirectories, out var deployErrors);

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("工程内脚本已更新：");
            for (int i = 0; i < targetScripts.Length; i++)
            {
                messageBuilder.AppendLine(targetScripts[i]);
            }

            if (deployedScriptDirectories.Count > 0)
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("已覆盖 Photoshop 脚本目录：");
                for (int i = 0; i < deployedScriptDirectories.Count; i++)
                {
                    messageBuilder.AppendLine(deployedScriptDirectories[i]);
                }
            }

            if (deployErrors.Count > 0)
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("自动部署存在以下问题：");
                for (int i = 0; i < deployErrors.Count; i++)
                {
                    messageBuilder.AppendLine(deployErrors[i]);
                }
            }

            EditorUtility.DisplayDialog(
                deployErrors.Count > 0 ? "导出PS脚本工具 完成" : "导出PS脚本工具 成功",
                messageBuilder.ToString(),
                "确定");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(layerTagMenuScriptPath);
        }
    }
    internal struct TextGradientStop
    {
        public float Location;
        public Color Color;
    }
    internal struct TextStyleRunInfo
    {
        public int Start;
        public int Length;
        public string FontName;
        public float FontSize;
        public Color Color;
        public FontStyle FontStyle;
        public TMPro.FontStyles TMPFontStyle;
        public float CharacterSpacing;
        public float LineSpacing;
        public bool IsAutoLineSpacing;
        public float BaselineShift;
        public float HorizontalScale;
        public bool AutoKerning;
        public float Kerning;
        public bool Ligatures;
        public bool NoBreak;
        public cn.efunstudio.psdreader.PsdParser.PsdTextCapitalization Capitalization;
    }
    internal struct TextParagraphRunInfo
    {
        public int Start;
        public int Length;
        public cn.efunstudio.psdreader.PsdParser.PsdTextJustification Justification;
        public float FirstLineIndent;
        public float StartIndent;
        public float EndIndent;
        public float SpaceBefore;
        public float SpaceAfter;
        public bool AutoHyphenate;
    }
    internal struct TextEffectContourPoint
    {
        public float Input;
        public float Output;
    }
    internal struct TextGlowEffectInfo
    {
        public bool Enabled;
        public bool Inner;
        public Color Color;
        public float Size;
        public float Spread;
        public string BlendModeKey;
        public string TechniqueKey;
        public string SourceKey;
        public float Noise;
        public float Jitter;
        public float Range;
        public bool AntiAlias;
        public TextEffectContourPoint[] Contour;
    }
        internal struct TextLayerInfo
        {
            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
            internal enum TMPOutlineMode
            {
                Center = 0,
            Inside = 1,
            Outside = 2
        }
        public string Text;
        public bool IsParagraphText;
        public float LayerOpacity;
        public float FillOpacity;
        public TextStyleRunInfo[] StyleRuns;
        public TextParagraphRunInfo[] ParagraphRuns;
        public float FontSize;
        public bool IsAutoLineSpacing;
        public float LineSpacing;
        public float CharacterSpacing;
        public Color Color;
        public FontStyle FontStyle;
        public TMPro.FontStyles TMPFontStyle;
        public string FontName;
        public cn.efunstudio.psdreader.PsdParser.PsdTextJustification Justification;
        public bool AutoKerning;
        public bool HasOutline;
        public Color OutlineColor;
        public float OutlineSize;
        public float TMPOutlineSize;
        public TMPOutlineMode TMPOutlinePosition;
        public string OutlineBlendModeKey;
        public bool HasShadow;
        public bool ShadowIsInner;
        public Color ShadowColor;
        public Vector2 ShadowOffset;
        public float ShadowSpread;
        public float ShadowSoftness;
        public string ShadowBlendModeKey;
        public TextEffectContourPoint[] ShadowContour;
        public TextGlowEffectInfo OuterGlow;
        public TextGlowEffectInfo InnerGlow;
        public bool HasGlow => OuterGlow.Enabled || InnerGlow.Enabled;
        public bool HasBevel;
        public bool BevelIsInner;
        public float BevelSize;
        public float BevelDepth;
        public float BevelSoften;
        public float BevelAngle;
        public float BevelAltitude;
        public string BevelTechniqueKey;
        public string BevelDirectionKey;
        public string BevelHighlightBlendModeKey;
        public string BevelShadowBlendModeKey;
        public Color BevelHighlightColor;
        public float BevelHighlightOpacity;
        public Color BevelShadowColor;
        public float BevelShadowOpacity;
        public TextEffectContourPoint[] BevelGlossContour;
        public bool HasGradient;
        public float GradientAngle;
        public bool GradientReverse;
        public float GradientOpacity;
        public float GradientScale;
        public Vector2 GradientOffset;
        public bool GradientAlignWithLayer;
        public bool GradientDither;
        public string GradientStyleKey;
        public string GradientBlendModeKey;
        public string GradientInterpolationKey;
        public TextGradientStop[] GradientStops;
    }
    internal static class LayerNameUtility
    {
        private const int MaxNameLength = 64;
        private static readonly Regex MultiUnderscoreRegex = new Regex("_+", RegexOptions.Compiled);
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON","PRN","AUX","NUL",
            "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };
        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();
        private static readonly Encoding Gb2312Encoding;
        private static readonly int[] PinyinCodes = new int[]
        {
            -20319,-20317,-20304,-20295,-20292,-20283,-20265,-20257,-20242,-20230,
            -20051,-20036,-20032,-20026,-20002,-19990,-19986,-19982,-19976,-19805,
            -19784,-19775,-19774,-19763,-19756,-19751,-19746,-19741,-19739,-19728,
            -19725,-19715,-19540,-19531,-19525,-19515,-19500,-19484,-19479,-19467,
            -19289,-19288,-19281,-19275,-19270,-19263,-19261,-19249,-19243,-19242,
            -19238,-19235,-19227,-19224,-19218,-19212,-19038,-19023,-19018,-19006,
            -19003,-18996,-18977,-18961,-18952,-18783,-18774,-18773,-18763,-18756,
            -18741,-18735,-18731,-18722,-18710,-18697,-18696,-18526,-18518,-18501,
            -18490,-18478,-18463,-18448,-18447,-18446,-18239,-18237,-18231,-18220,
            -18211,-18201,-18184,-18183,-18181,-18012,-17997,-17988,-17970,-17964,
            -17961,-17950,-17947,-17931,-17928,-17922,-17759,-17752,-17733,-17730,
            -17721,-17703,-17701,-17697,-17692,-17683,-17676,-17496,-17487,-17482,
            -17468,-17454,-17433,-17427,-17417,-17202,-17185,-16983,-16970,-16942,
            -16915,-16733,-16708,-16706,-16689,-16664,-16657,-16647,-16474,-16470,
            -16465,-16459,-16452,-16448,-16433,-16429,-16427,-16423,-16419,-16412,
            -16407,-16403,-16401,-16393,-16220,-16216,-16212,-16205,-16202,-16187,
            -16180,-16171,-16169,-16158,-16155,-15959,-15958,-15944,-15933,-15920,
            -15915,-15903,-15889,-15878,-15707,-15701,-15681,-15667,-15661,-15659,
            -15652,-15640,-15631,-15625,-15454,-15448,-15436,-15435,-15419,-15416,
            -15408,-15394,-15385,-15377,-15375,-15369,-15363,-15362,-15183,-15180,
            -15165,-15158,-15153,-15150,-15149,-15144,-15143,-15141,-15140,-15139,
            -15128,-15121,-15119,-15117,-15110,-15109,-14941,-14937,-14933,-14930,
            -14929,-14928,-14926,-14922,-14921,-14914,-14908,-14902,-14894,-14889,
            -14882,-14873,-14871,-14857,-14678,-14674,-14670,-14668,-14663,-14654,
            -14645,-14630,-14594,-14429,-14407,-14399,-14384,-14379,-14368,-14355,
            -14353,-14345,-14170,-14159,-14151,-14149,-14145,-14140,-14137,-14135,
            -14125,-14123,-14122,-14112,-14109,-14099,-14097,-14094,-14092,-14090,
            -14087,-14083,-13917,-13914,-13910,-13907,-13906,-13905,-13896,-13894,
            -13878,-13870,-13859,-13847,-13831,-13658,-13611,-13601,-13406,-13404,
            -13400,-13398,-13395,-13391,-13387,-13383,-13367,-13359,-13356,-13343,
            -13340,-13329,-13326,-13318,-13147,-13138,-13120,-13107,-13096,-13095,
            -13091,-13076,-13068,-13063,-13060,-12888,-12875,-12871,-12860,-12858,
            -12852,-12849,-12838,-12831,-12829,-12812,-12802,-12607,-12597,-12594,
            -12585,-12556,-12359,-12346,-12320,-12300,-12120,-12099,-12089,-12074,
            -12067,-12058,-12039,-11867,-11861,-11847,-11831,-11798,-11781,-11604,
            -11589,-11536,-11358,-11340,-11339,-11324,-11303,-11097,-11077,-11067,
            -11055,-11052,-11045,-11041,-11038,-11024,-11020,-11019,-11018,-11014,
            -10838,-10832,-10815,-10800,-10790,-10780,-10764,-10587,-10544,-10533,
            -10519,-10331,-10329,-10328,-10322,-10315,-10309,-10307,-10296,-10281,
            -10274,-10270,-10262,-10260,-10256,-10254
        };
        private static readonly string[] PinyinValues = new string[]
        {
            "a","ai","an","ang","ao","ba","bai","ban","bang","bao","bei","ben","beng","bi","bian","biao",
            "bie","bin","bing","bo","bu","ca","cai","can","cang","cao","ce","ceng","cha","chai","chan",
            "chang","chao","che","chen","cheng","chi","chong","chou","chu","chuai","chuan","chuang",
            "chui","chun","chuo","ci","cong","cou","cu","cuan","cui","cun","cuo","da","dai","dan",
            "dang","dao","de","deng","di","dian","diao","die","ding","diu","dong","dou","du","duan",
            "dui","dun","duo","e","en","er","fa","fan","fang","fei","fen","feng","fo","fou","fu","ga",
            "gai","gan","gang","gao","ge","gei","gen","geng","gong","gou","gu","gua","guai","guan",
            "guang","gui","gun","guo","ha","hai","han","hang","hao","he","hei","hen","heng","hong",
            "hou","hu","hua","huai","huan","huang","hui","hun","huo","ji","jia","jian","jiang","jiao",
            "jie","jin","jing","jiong","jiu","ju","juan","jue","jun","ka","kai","kan","kang","kao",
            "ke","ken","keng","kong","kou","ku","kua","kuai","kuan","kuang","kui","kun","kuo","la",
            "lai","lan","lang","lao","le","lei","leng","li","lia","lian","liang","liao","lie","lin",
            "ling","liu","long","lou","lu","lv","luan","lue","lun","luo","ma","mai","man","mang","mao",
            "me","mei","men","meng","mi","mian","miao","mie","min","ming","miu","mo","mou","mu","na",
            "nai","nan","nang","nao","ne","nei","nen","neng","ni","nian","niang","niao","nie","nin",
            "ning","niu","nong","nu","nv","nuan","nue","nuo","o","ou","pa","pai","pan","pang","pao",
            "pei","pen","peng","pi","pian","piao","pie","pin","ping","po","pu","qi","qia","qian",
            "qiang","qiao","qie","qin","qing","qiong","qiu","qu","quan","que","qun","ran","rang",
            "rao","re","ren","reng","ri","rong","rou","ru","ruan","rui","run","ruo","sa","sai","san",
            "sang","sao","se","sen","seng","sha","shai","shan","shang","shao","she","shen","sheng",
            "shi","shou","shu","shua","shuai","shuan","shuang","shui","shun","shuo","si","song",
            "sou","su","suan","sui","sun","suo","ta","tai","tan","tang","tao","te","teng","ti","tian",
            "tiao","tie","ting","tong","tou","tu","tuan","tui","tun","tuo","wa","wai","wan","wang",
            "wei","wen","weng","wo","wu","xi","xia","xian","xiang","xiao","xie","xin","xing","xiong",
            "xiu","xu","xuan","xue","xun","ya","yan","yang","yao","ye","yi","yin","ying","yo","yong",
            "you","yu","yuan","yue","yun","za","zai","zan","zang","zao","ze","zei","zen","zeng","zha",
            "zhai","zhan","zhang","zhao","zhe","zhen","zheng","zhi","zhong","zhou","zhu","zhua",
            "zhuai","zhuan","zhuang","zhui","zhun","zhuo","zi","zong","zou","zu","zuan","zui","zun","zuo"
        };

        static LayerNameUtility()
        {
            try
            {
                Gb2312Encoding = Encoding.GetEncoding("GB2312");
            }
            catch
            {
                try
                {
                    Gb2312Encoding = Encoding.GetEncoding(936);
                }
                catch
                {
                    Gb2312Encoding = Encoding.UTF8;
                }
            }
        }

        internal static string ConvertChineseToLetters(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var builder = new StringBuilder(input.Length * 2);
            foreach (var ch in input)
            {
                if (IsChinese(ch))
                {
                    builder.Append(ConvertChineseChar(ch));
                }
                else
                {
                    builder.Append(ch);
                }
            }
            return builder.ToString();
        }

        internal static string Sanitize(string input)
        {
            return SanitizeInternal(input, stripRefTags: true);
        }

        internal static string SanitizeRelativePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var normalized = input.Replace("\\", "/").Trim();
            if (string.IsNullOrEmpty(normalized)) return string.Empty;

            var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments == null || segments.Length == 0) return string.Empty;

            var sanitizedSegments = new List<string>(segments.Length);
            foreach (var segment in segments)
            {
                var sanitizedSegment = SanitizeInternal(segment, stripRefTags: false);
                if (string.IsNullOrWhiteSpace(sanitizedSegment)) continue;
                sanitizedSegments.Add(sanitizedSegment);
            }

            return sanitizedSegments.Count > 0 ? string.Join("/", sanitizedSegments) : string.Empty;
        }

        internal static string SanitizePreserveRefTags(string input)
        {
            return SanitizeInternal(input, stripRefTags: false);
        }

        internal static string GetRelativePathLeafName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            var normalized = path.Replace("\\", "/").TrimEnd('/');
            if (string.IsNullOrEmpty(normalized)) return string.Empty;

            var leafName = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(leafName) ? normalized : leafName;
        }

        private static string SanitizeInternal(string input, bool stripRefTags)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var working = input.Trim();
            if (stripRefTags)
            {
                if (!string.IsNullOrEmpty(UGUIParser.REF_PREFAB_TAG) && working.StartsWith(UGUIParser.REF_PREFAB_TAG, StringComparison.OrdinalIgnoreCase))
                {
                    working = working.Substring(UGUIParser.REF_PREFAB_TAG.Length).TrimStart();
                }
                else if (!string.IsNullOrEmpty(UGUIParser.REF_TAG) && working.StartsWith(UGUIParser.REF_TAG, StringComparison.OrdinalIgnoreCase))
                {
                    working = working.Substring(UGUIParser.REF_TAG.Length).TrimStart();
                }
            }

            var builder = new StringBuilder(working.Length);
            foreach (var ch in working)
            {
                if (IsAllowedChar(ch))
                {
                    builder.Append(ch);
                }
                else if (char.IsWhiteSpace(ch) || char.IsControl(ch) || Array.IndexOf(InvalidFileChars, ch) >= 0)
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append('_');
                }
            }
            var sanitized = MultiUnderscoreRegex.Replace(builder.ToString(), "_");
            sanitized = sanitized.Trim('_', '.');
            if (sanitized.Length > MaxNameLength)
            {
                sanitized = sanitized.Substring(0, MaxNameLength);
            }
            // 截断点可能让结尾残留 '_' 或 '.'：既违反上面的收尾规则，Windows 也会去掉结尾 '.'，
            // 造成记录名与真实文件名不一致。
            sanitized = sanitized.TrimEnd('_', '.');
            if (ReservedNames.Contains(sanitized))
            {
                sanitized = "_" + sanitized;
            }
            return sanitized;
        }

        private static string ConvertChineseChar(char chineseChar)
        {
            if (Gb2312Encoding == null)
            {
                return ((int)chineseChar).ToString("x4");
            }
            var bytes = Gb2312Encoding.GetBytes(new[] { chineseChar });
            if (bytes.Length != 2)
            {
                return chineseChar.ToString();
            }
            var code = bytes[0] << 8 | bytes[1];
            code -= 65536;
            if (code > 0 && code < 160)
            {
                return chineseChar.ToString();
            }
            for (int i = PinyinCodes.Length - 1; i >= 0; i--)
            {
                if (code >= PinyinCodes[i])
                {
                    return PinyinValues[i];
                }
            }
            return "u" + ((int)chineseChar).ToString("x4");
        }

        private static bool IsChinese(char ch)
        {
            return ch >= 0x4e00 && ch <= 0x9fff;
        }

        private static bool IsAllowedChar(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.';
        }
    }

}
#endif
