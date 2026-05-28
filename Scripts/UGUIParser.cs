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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using cn.efunstudio.psdreader;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
        FillColor, // Solid color fill
        TMPText,
        TMPButton,
        TMPDropdown,
        TMPInputField,
        TMPToggle,

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
        private SerializedProperty readmeProperty;
        SerializedProperty defaultTextType;
        SerializedProperty defaultImageType;
        SerializedProperty forceUseTMP;
        SerializedProperty convertZh2En;
        SerializedProperty nineSliceBorderTolerance;
        SerializedProperty sharedAssetsOutput;
        SerializedProperty sharedPrefabOutput;
        private string[] textTypesDisplay;
        private int[] textTypes;
        private string[] imageTypesDisplay;
        private int[] imageTypes;
        private void OnEnable()
        {
            readmeProperty = serializedObject.FindProperty("readmeDoc");
            defaultTextType = serializedObject.FindProperty("defaultTextType");
            defaultImageType = serializedObject.FindProperty("defaultImageType");
            forceUseTMP = serializedObject.FindProperty("forceUseTMP");
            convertZh2En = serializedObject.FindProperty("convertZh2En");
            nineSliceBorderTolerance = serializedObject.FindProperty("nineSliceBorderTolerance");
            sharedAssetsOutput = serializedObject.FindProperty("sharedAssetsOutput");
            sharedPrefabOutput = serializedObject.FindProperty("sharedPrefabOutput");
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
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (GUILayout.Button("使用教程"))
            {
                Application.OpenURL("https://efunstudio.cn");
            }
            if (GUILayout.Button("导出使用文档"))
            {
                (target as UGUIParser).ExportReadmeDoc();
            }
            if (GUILayout.Button("导出Rules标签"))
            {
                (target as UGUIParser).ExportLayerTagMenuConfig();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出配置json"))
                {
                    ExportConfigJson(serializedObject);
                }

                if (GUILayout.Button("从json导入配置"))
                {
                    ImportConfigFromJson(serializedObject);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.LabelField("使用说明:");
            readmeProperty.stringValue = EditorGUILayout.TextArea(readmeProperty.stringValue, GUILayout.Height(100));

            using (new EditorGUILayout.HorizontalScope())
            {
                nineSliceBorderTolerance.intValue = EditorGUILayout.IntSlider("九宫识别容错(默认:5)", nineSliceBorderTolerance.intValue, 1, 10);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                defaultTextType.enumValueIndex = EditorGUILayout.IntPopup("默认文本类型:", defaultTextType.enumValueIndex, textTypesDisplay, textTypes);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                defaultImageType.enumValueIndex = EditorGUILayout.IntPopup("默认图片类型:", defaultImageType.enumValueIndex, imageTypesDisplay, imageTypes);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                forceUseTMP.boolValue = EditorGUILayout.ToggleLeft("强制优先使用TMP", forceUseTMP.boolValue);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                convertZh2En.boolValue = EditorGUILayout.ToggleLeft("中文转拼音", convertZh2En.boolValue);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                sharedAssetsOutput.stringValue = EditorGUILayout.TextField("复用图片导出路径", sharedAssetsOutput.stringValue);
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("选择导出路径", Application.dataPath, "");

                    if (!string.IsNullOrEmpty(path))
                    {
                        sharedAssetsOutput.stringValue = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, path); ;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                sharedPrefabOutput.stringValue = EditorGUILayout.TextField("复用prefab导出路径", sharedPrefabOutput.stringValue);
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("选择导出路径", Application.dataPath, "");

                    if (!string.IsNullOrEmpty(path))
                    {
                        sharedPrefabOutput.stringValue = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, path); ;
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
            base.OnInspectorGUI();
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
                serializedObject.FindProperty("defaultTextType").enumValueIndex = (int)snapshot.defaultTextType;
                serializedObject.FindProperty("defaultImageType").enumValueIndex = (int)snapshot.defaultImageType;
                serializedObject.FindProperty("forceUseTMP").boolValue = snapshot.forceUseTMP;
                serializedObject.FindProperty("readmeDoc").stringValue = snapshot.readmeDoc;
                serializedObject.FindProperty("convertZh2En").boolValue = snapshot.convertZh2En;
                if (json.IndexOf("\"nineSliceBorderTolerance\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    serializedObject.FindProperty("nineSliceBorderTolerance").intValue = Mathf.Clamp(snapshot.nineSliceBorderTolerance, 0, 255);
                }
                serializedObject.FindProperty("sharedAssetsOutput").stringValue = snapshot.sharedAssetsOutput;
                serializedObject.FindProperty("sharedPrefabOutput").stringValue = snapshot.sharedPrefabOutput;

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

                        ruleProp.FindPropertyRelative("UIType").enumValueIndex = (int)ruleSnapshot.UIType;
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

            snapshot.defaultTextType = (GUIType)serializedObject.FindProperty("defaultTextType").enumValueIndex;
            snapshot.defaultImageType = (GUIType)serializedObject.FindProperty("defaultImageType").enumValueIndex;
            snapshot.forceUseTMP = serializedObject.FindProperty("forceUseTMP").boolValue;
            snapshot.readmeDoc = serializedObject.FindProperty("readmeDoc").stringValue;
            snapshot.convertZh2En = serializedObject.FindProperty("convertZh2En").boolValue;
            snapshot.nineSliceBorderTolerance = Mathf.Clamp(serializedObject.FindProperty("nineSliceBorderTolerance").intValue, 0, 255);
            snapshot.sharedAssetsOutput = serializedObject.FindProperty("sharedAssetsOutput").stringValue;
            snapshot.sharedPrefabOutput = serializedObject.FindProperty("sharedPrefabOutput").stringValue;

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
                    ruleSnapshot.UIType = (GUIType)ruleProp.FindPropertyRelative("UIType").enumValueIndex;
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

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class UGUIParserSnapshot
        {
            public GUIType defaultTextType;
            public GUIType defaultImageType;
            public bool forceUseTMP;
            public string uiFormTemplateGuid;
            public UGUIParseRuleSnapshot[] rules;
            public string readmeDoc;
            public bool convertZh2En;
            public int nineSliceBorderTolerance;
            public string sharedAssetsOutput;
            public string sharedPrefabOutput;
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
    }
    [CanEditMultipleObjects]
    [CreateAssetMenu(fileName = "Psd2UIFormConfig", menuName = "ScriptableObject/Psd2UIForm Config")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class UGUIParser : ScriptableObject
    {
        internal const char UITYPE_SPLIT_CHAR = '.';
        internal const int UITYPE_MAX = 100;
        private const string LayerTagMenuScriptPath = "Assets/Plugins/PSD2UIForm/PSScript/PSD2UGUI-LayerTagMenu.jsx";
        private const string LayerExportScriptPath = "Assets/Plugins/PSD2UIForm/PSScript/PSD2UIForm-导出PSD.jsx";
        internal const string REF_TAG = "ref ";
        internal const string REF_PREFAB_TAG = "refp ";
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] GUIType defaultTextType = GUIType.Text;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] GUIType defaultImageType = GUIType.Image;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField, HideInInspector] bool forceUseTMP = false;
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
        /// <summary>
        /// Shared export directory for reused assets.
        /// </summary>
        internal string SharedAssetsOutput => sharedAssetsOutput;
        internal string SharedPrefabOutput => sharedPrefabOutput;
        internal int NineSliceBorderTolerance => Mathf.Clamp(nineSliceBorderTolerance, 0, 255);
        internal GUIType DefaultText => ResolvePreferredUIType(defaultTextType);
        internal GUIType DefaultImage => defaultImageType;
        internal GameObject UIFormTemplate => uiFormTemplate;
        internal bool ConvertZh2En => convertZh2En;
        internal bool ForceUseTMP => forceUseTMP;
        private static UGUIParser mInstance = null;
        private const string TmpEffectMaterialSignatureUserDataPrefix = "PSD2UI_TMPFX_SIG:";
        private const string LegacyTmpEffectMaterialNameToken = "__PSD2UI_TMPFX__";
        private static readonly Dictionary<string, Material> tmpEffectMaterialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<int, Material> tmpEffectMaterialBaseLookup = new Dictionary<int, Material>();
        private static readonly int TmpFaceDilatePropertyId = Shader.PropertyToID("_FaceDilate");
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
        private const float TmpOutlineThicknessScale = 0.5f;
        private const float TmpBevelWidthScale = 0.5f;
        private const float TmpShaderClamp = 1f;
        private const float TmpDefaultGlowPower = 0.75f;
        internal static UGUIParser Instance
        {
            get
            {
                if (mInstance == null)
                {
                    var guid = AssetDatabase.FindAssets("t:UGUIParser").FirstOrDefault();
                    mInstance = AssetDatabase.LoadAssetAtPath<UGUIParser>(AssetDatabase.GUIDToAssetPath(guid));
                }
                return mInstance;
            }
        }
        internal static bool IsMainUIType(GUIType tp)
        {
            return (int)tp <= UITYPE_MAX;
        }
        internal GUIType ResolvePreferredUIType(GUIType uiType)
        {
            if (!forceUseTMP) return uiType;

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
            public GUIType RoleType = GUIType.Null;
            public bool HasExplicitMainType;
            public bool HasExplicitImageType;
            public Image.Type ExplicitImageType = Image.Type.Simple;
            public bool HasExplicitTextBackend;
            public bool ForceTMP;
            public bool ForceUGUI;
        }
        private static bool IsRoleUIType(GUIType uiType)
        {
            return (int)uiType > UITYPE_MAX;
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
                        Family = IsRoleUIType(rule.UIType) ? LayerTagFamily.Role : LayerTagFamily.Main,
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
                if (splitIndex <= 0 || splitIndex >= workingName.Length - 1)
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
            if (emitWarnings && parsedInfo.Warnings.Count > 0)
            {
                for (int i = 0; i < parsedInfo.Warnings.Count; i++)
                {
                    Debug.LogWarning($"Layer tag warning [{rawName}]: {parsedInfo.Warnings[i]}");
                }
            }
            return true;
        }
        private void ResolveParsedTypes(ParsedLayerTagInfo parsedInfo, PsdLayerType layerType)
        {
            if (parsedInfo == null) return;

            if (parsedInfo.FamilyWinners.TryGetValue(LayerTagFamily.Role, out var roleWinner))
            {
                parsedInfo.RoleType = roleWinner.UIType;
            }

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
                else
                {
                    mainType = requestedMainType;
                    parsedInfo.HasExplicitMainType = true;
                }
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

            return Type.GetType(rule.UIHelper);
        }
        internal UGUIParseRule GetRule(GUIType uiType)
        {
            foreach (var rule in rules)
            {
                if (rule.UIType == uiType) return rule;
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
        internal bool TryParse(PsdLayerNode layer, out UGUIParseRule result, out GUIType roleType)
        {
            result = null;
            roleType = GUIType.Null;
            if (layer == null)
            {
                return false;
            }

            var layerName = layer.BindPsdLayer != null ? layer.BindPsdLayer.GetDisplayName() : layer.SourceLayerName;
            TryParseLayerTags(layerName, layer.LayerType, out var parsedInfo, emitWarnings: true);
            roleType = parsedInfo.RoleType;

            if (parsedInfo.MainType != GUIType.Null)
            {
                result = ResolvePreferredRule(GetRule(parsedInfo.MainType));
            }

            return result != null;
        }
        internal bool TryParse(PsdLayerNode layer, out UGUIParseRule result)
        {
            return TryParse(layer, out result, out _);
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

            var rect = layerNode.LayerRect;
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

        /// <summary>
        /// Convert the layer node image to a Texture2D asset and return it.
        /// </summary>
        /// <param name="layerNode"></param>
        /// <returns></returns>
        internal static Texture2D LayerNode2Texture(PsdLayerNode layerNode)
        {
            if (layerNode != null)
            {
                var spAssetName = layerNode.ExportImageAsset(false);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spAssetName);
                return texture;
            }
            return null;
        }
        internal Image.Type ResolveFinalImageType(PsdLayerNode layerNode, Image.Type templateImageType)
        {
            if (layerNode == null)
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
            if (layerNode != null)
            {
                var spAssetName = layerNode.ExportImageAsset(true);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spAssetName);
                if (sprite != null)
                {
                    if (auto9Slice)
                    {
                        var spImpt = AssetImporter.GetAtPath(spAssetName) as TextureImporter;
                        var rawReadable = spImpt.isReadable;
                        if (!rawReadable)
                        {
                            spImpt.isReadable = true;
                            spImpt.SaveAndReimport();
                        }
                        if (spImpt.spriteBorder == Vector4.zero)
                        {
                            spImpt.spriteBorder = CalculateTexture9SliceBorder(sprite.texture);
                            spImpt.isReadable = rawReadable;
                            spImpt.SaveAndReimport();
                        }
                    }
                    return sprite;
                }
            }
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

            int contentLeft = contentRect.xMin;
            int contentRight = contentRect.xMax - 1;
            int contentBottom = contentRect.yMin;
            int contentTop = contentRect.yMax - 1;

            RangeInt horizontalStretch = FindAxisStretchBand(
                pixels, width, contentRect,
                true, effectiveAlphaThreshold, effectiveVariationTolerance);
            RangeInt verticalStretch = FindAxisStretchBand(
                pixels, width, contentRect,
                false, effectiveAlphaThreshold, effectiveVariationTolerance);
            bool hasHorizontalStretch = horizontalStretch.length > 1;
            bool hasVerticalStretch = verticalStretch.length > 1;

            int leftBorder = hasHorizontalStretch
                ? Mathf.Clamp(horizontalStretch.start - contentLeft, 0, Mathf.Max(0, width - 1))
                : 0;
            int rightBorder = hasHorizontalStretch
                ? Mathf.Clamp(contentRight - (horizontalStretch.start + horizontalStretch.length - 1), 0, Mathf.Max(0, width - 1))
                : 0;
            int bottomBorder = hasVerticalStretch
                ? Mathf.Clamp(verticalStretch.start - contentBottom, 0, Mathf.Max(0, height - 1))
                : 0;
            int topBorder = hasVerticalStretch
                ? Mathf.Clamp(contentTop - (verticalStretch.start + verticalStretch.length - 1), 0, Mathf.Max(0, height - 1))
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
        /// <summary>
        /// Apply parsed PSD text data to a legacy uGUI Text component.
        /// </summary>
        /// <param name="txtLayer"></param>
        /// <param name="text"></param>
        internal static TextLayerInfo SetTextStyle(PsdLayerNode txtLayer, UnityEngine.UI.Text text)
        {
            if (text == null) return default;
            text.gameObject.SetActive(txtLayer != null);
            if (txtLayer != null && txtLayer.ParseTextLayerInfo(out var textInfo))
            {
                bool isMultiLine = IsLikelyMultiLineText(txtLayer, in textInfo);
                var tFont = FindFontAsset(textInfo.FontName);
                if (tFont != null) text.font = tFont;
                text.text = textInfo.Text;
                text.fontSize = textInfo.FontSize;
                text.fontStyle = textInfo.FontStyle;
                text.color = textInfo.Color;
                text.resizeTextForBestFit = false;
                text.lineSpacing = ConvertPsdLeadingToUGUILineSpacing(text, in textInfo);
                text.horizontalOverflow = isMultiLine ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                ApplyUGUITextEffects(text, in textInfo);
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
            text.gameObject.SetActive(txtLayer != null);
            if (txtLayer != null && txtLayer.ParseTextLayerInfo(out var textInfo))
            {
                bool isMultiLine = IsLikelyMultiLineText(txtLayer, in textInfo);
                var tFont = FindTMPFontAsset(textInfo.FontName) ?? text.font;
                if (tFont != null)
                {
                    var fontMaterial = EnsureTMPFontAssetResources(tFont);
                    EnsureTMPFontHasCharacters(tFont, textInfo.Text);
                    fontMaterial = EnsureTMPFontAssetResources(tFont) ?? fontMaterial;
                    text.font = tFont;
                    if (fontMaterial != null)
                    {
                        text.fontSharedMaterial = fontMaterial;
                    }
                }
                text.text = textInfo.Text ?? string.Empty;
                text.fontSize = textInfo.FontSize;
                text.fontStyle = textInfo.TMPFontStyle;
                text.characterSpacing = textInfo.CharacterSpacing;
                text.lineSpacing = ConvertPsdLeadingToTMPLineSpacing(text, in textInfo);
                text.enableAutoSizing = false;
                text.enableWordWrapping = isMultiLine;
                text.overflowMode = TextOverflowModes.Overflow;
                text.margin = Vector4.zero;
                text.color = HasUsableTMPGradientStops(in textInfo) ? Color.white : textInfo.Color;
                ApplyTMPTextEffects(text, in textInfo);
                text.ForceMeshUpdate();
                ApplyTMPTextGradient(txtLayer, text, in textInfo);

                // TODO: Revisit TMP layout parity after more PSD samples are verified.
            }
        }
        private static float ConvertPsdLeadingToUGUILineSpacing(UnityEngine.UI.Text text, in TextLayerInfo textInfo)
        {
            if (textInfo.IsAutoLineSpacing || textInfo.LineSpacing <= 0f)
            {
                return 1f;
            }

            float baseLineHeight = Mathf.Max(1f, textInfo.FontSize);
            var font = text != null ? text.font : null;
            if (font != null && font.fontSize > 0 && font.lineHeight > 0)
            {
                baseLineHeight = font.lineHeight * (textInfo.FontSize / (float)font.fontSize);
            }

            float spacingFactor = textInfo.LineSpacing / Mathf.Max(1f, baseLineHeight);
            if (float.IsNaN(spacingFactor) || float.IsInfinity(spacingFactor))
            {
                return 1f;
            }

            return Mathf.Max(0.01f, spacingFactor);
        }
        private static bool IsLikelyMultiLineText(PsdLayerNode layerNode, in TextLayerInfo textInfo)
        {
            if (layerNode == null) return false;
            if (!string.IsNullOrEmpty(textInfo.Text) && (textInfo.Text.IndexOf('\n') >= 0 || textInfo.Text.IndexOf('\r') >= 0))
            {
                return true;
            }

            float singleLineHeight = Mathf.Max(textInfo.FontSize * 1.35f, 1f);
            return layerNode.LayerRect.height > singleLineHeight;
        }
        private static float ConvertPsdLeadingToTMPLineSpacing(TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            if (textInfo.IsAutoLineSpacing || textInfo.LineSpacing <= 0f)
            {
                return 0f;
            }

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

            float additionalSpacing = textInfo.LineSpacing - baseLineHeight;
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
        private static void ApplyTMPTextGradient(PsdLayerNode layerNode, TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            if (text == null)
                return;

            text.enableVertexGradient = false;
            text.colorGradientPreset = null;
            text.colorGradient = new VertexGradient(textInfo.Color);

            // Always sync a serialized TMP gradient approximation when PSD provides usable stops.
            // This keeps the result editable in the TMP inspector even when the effect cannot
            // be reproduced exactly (for example, multi-stop or non-linear Photoshop gradients).
            if (TryApplyTMPSerializedGradient(text, in textInfo))
            {
                text.color = Color.white;
                return;
            }

            text.color = textInfo.Color;
        }
        private static bool HasUsableTMPGradientStops(in TextLayerInfo textInfo)
        {
            return textInfo.HasGradient && textInfo.GradientStops != null && textInfo.GradientStops.Length >= 2;
        }
        private static bool TryApplyTMPSerializedGradient(TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            if (text == null || !HasUsableTMPGradientStops(in textInfo))
            {
                return false;
            }

            text.enableVertexGradient = true;
            text.colorGradientPreset = null;
            text.colorGradient = CreateTMPApproximateVertexGradient(in textInfo);
            text.color = Color.white;
            text.havePropertiesChanged = true;
            text.SetVerticesDirty();
            text.ForceMeshUpdate();
            return true;
        }
        private static VertexGradient CreateTMPApproximateVertexGradient(in TextLayerInfo textInfo)
        {
            Vector2 direction = GetTMPGradientDirection(textInfo.GradientAngle);
            var corners = new Vector2[4]
            {
                new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-0.5f, -0.5f),
                new Vector2(0.5f, -0.5f)
            };

            float minDot = float.PositiveInfinity;
            float maxDot = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                float dot = Vector2.Dot(corners[i], direction);
                if (dot < minDot) minDot = dot;
                if (dot > maxDot) maxDot = dot;
            }

            float dotRange = maxDot - minDot;
            if (Mathf.Abs(dotRange) < 0.0001f)
            {
                dotRange = 1f;
            }

            return new VertexGradient()
            {
                topLeft = EvaluateTMPGradientColor(in textInfo, (Vector2.Dot(corners[0], direction) - minDot) / dotRange),
                topRight = EvaluateTMPGradientColor(in textInfo, (Vector2.Dot(corners[1], direction) - minDot) / dotRange),
                bottomLeft = EvaluateTMPGradientColor(in textInfo, (Vector2.Dot(corners[2], direction) - minDot) / dotRange),
                bottomRight = EvaluateTMPGradientColor(in textInfo, (Vector2.Dot(corners[3], direction) - minDot) / dotRange)
            };
        }
        private static Vector2 GetTMPGradientDirection(float angle)
        {
            float normalizedAngle = NormalizeTMPGradientAngle(angle);
            float rad = normalizedAngle * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector2.right;
            }

            return direction.normalized;
        }
        private static float NormalizeTMPGradientAngle(float angle)
        {
            if (float.IsNaN(angle) || float.IsInfinity(angle))
            {
                return 0f;
            }

            angle %= 360f;
            if (angle < 0f)
            {
                angle += 360f;
            }

            return angle;
        }
        private static Color32 EvaluateTMPGradientColor(in TextLayerInfo textInfo, float t)
        {
            var stops = textInfo.GradientStops;
            if (stops == null || stops.Length == 0)
            {
                return textInfo.Color;
            }

            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                t = 0f;
            }
            t = Mathf.Clamp01(t);
            if (textInfo.GradientReverse)
            {
                t = 1f - t;
            }

            if (t <= stops[0].Location)
            {
                return stops[0].Color;
            }
            if (t >= stops[stops.Length - 1].Location)
            {
                return stops[stops.Length - 1].Color;
            }

            for (int i = 0; i < stops.Length - 1; i++)
            {
                var current = stops[i];
                var next = stops[i + 1];
                if (t <= next.Location)
                {
                    float range = next.Location - current.Location;
                    if (range <= 0.0001f)
                    {
                        return next.Color;
                    }

                    float localT = Mathf.InverseLerp(current.Location, next.Location, t);
                    return Color.Lerp(current.Color, next.Color, localT);
                }
            }

            return stops[stops.Length - 1].Color;
        }
        private static void ApplyTMPTextEffects(TextMeshProUGUI text, in TextLayerInfo textInfo)
        {
            if (text == null) return;
            RemoveLegacyTextEffects(text);

            var baseMaterial = ResolveTMPBaseMaterial(text);
            if (baseMaterial == null)
            {
                return;
            }

            if (!textInfo.HasOutline && !textInfo.HasShadow && !textInfo.HasGlow && !textInfo.HasBevel)
            {
                if (text.fontSharedMaterial != baseMaterial)
                {
                    text.fontSharedMaterial = baseMaterial;
                    text.UpdateMeshPadding();
                    text.SetMaterialDirty();
                }
                return;
            }

            var effectMaterial = GetOrCreateTMPEffectMaterial(text.font, baseMaterial, text.fontSize, in textInfo);
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
            if (current != null && tmpEffectMaterialBaseLookup.TryGetValue(current.GetInstanceID(), out var baseMaterial) && baseMaterial != null)
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
            return texA.GetInstanceID() == texB.GetInstanceID();
        }
        private static Material GetOrCreateTMPEffectMaterial(TMP_FontAsset fontAsset, Material baseMaterial, float fontSize, in TextLayerInfo textInfo)
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
            ShaderUtilities.GetShaderPropertyIDs();

            float gradientScale = GetTMPMaterialGradientScale(baseMaterial);
            float normalizedOutlineSize = textInfo.HasOutline ? Mathf.Clamp01(textInfo.TMPOutlineSize / gradientScale) : 0f;
            float outlineWidth = normalizedOutlineSize * TmpOutlineThicknessScale;
            float faceDilate = 0f;
            if (textInfo.HasOutline)
            {
                switch (textInfo.TMPOutlinePosition)
                {
                    case TextLayerInfo.TMPOutlineMode.Inside:
                        faceDilate = 0f;
                        break;
                    case TextLayerInfo.TMPOutlineMode.Center:
                        faceDilate = normalizedOutlineSize * 0.5f;
                        break;
                    case TextLayerInfo.TMPOutlineMode.Outside:
                        faceDilate = normalizedOutlineSize;
                        break;
                    default:
                        faceDilate = normalizedOutlineSize * 0.5f;
                        break;
                }
            }
            float outlineSoftness = 0f;
            float shadowOffsetX = 0f;
            float shadowOffsetY = 0f;
            float shadowDilate = 0f;
            float shadowSoftness = 0f;
            bool shadowIsInner = textInfo.HasShadow && textInfo.ShadowIsInner;
            if (textInfo.HasShadow)
            {
                float desiredShadowOffsetX = Mathf.Clamp(textInfo.ShadowOffset.x / gradientScale, -1f, 1f);
                float desiredShadowOffsetY = Mathf.Clamp(textInfo.ShadowOffset.y / gradientScale, -1f, 1f);
                float shadowSpread = Mathf.Clamp01(textInfo.ShadowSpread);
                float normalizedShadowSize = Mathf.Clamp01(textInfo.ShadowSoftness / gradientScale);
                float desiredShadowDilateMagnitude = Mathf.Clamp01(normalizedShadowSize * shadowSpread);
                float desiredShadowDilate = shadowIsInner ? -desiredShadowDilateMagnitude : desiredShadowDilateMagnitude;
                float desiredShadowSoftness = Mathf.Clamp01(normalizedShadowSize - desiredShadowDilateMagnitude);
                float underlayBudget = GetTMPAvailableEffectBudget(baseMaterial, faceDilate);

                if (shadowIsInner)
                {
                    desiredShadowOffsetX = -desiredShadowOffsetX;
                    desiredShadowOffsetY = -desiredShadowOffsetY;
                }

                FitTMPUnderlayToBudget(underlayBudget, ref desiredShadowOffsetX, ref desiredShadowOffsetY, ref desiredShadowDilate, ref desiredShadowSoftness, shadowIsInner);

                shadowOffsetX = NormalizeTMPBudgetedValue(desiredShadowOffsetX, underlayBudget);
                shadowOffsetY = NormalizeTMPBudgetedValue(desiredShadowOffsetY, underlayBudget);
                shadowDilate = NormalizeTMPBudgetedValue(desiredShadowDilate, underlayBudget);
                shadowSoftness = NormalizeTMPBudgetedValue(desiredShadowSoftness, underlayBudget);
            }
            float glowInner = 0f;
            float glowOuter = 0f;
            float glowOffset = 0f;
            float glowPower = 0f;
            Color tmpGlowColor = textInfo.GlowColor;
            if (textInfo.HasGlow)
            {
                float glowSize = Mathf.Clamp01(textInfo.GlowSize / gradientScale);
                float glowSpread = Mathf.Clamp01(textInfo.GlowSpread);
                float baseGlowPower = Mathf.Clamp01(textInfo.GlowPower > 0f ? textInfo.GlowPower : TmpDefaultGlowPower);

                if (textInfo.GlowIsInner)
                {
                    float innerGlowBasePower = Mathf.Clamp01(baseGlowPower * 0.1f);
                    glowInner = glowSize;
                    glowOuter = 0f;
                    glowOffset = 0f;
                    glowPower = Mathf.Lerp(innerGlowBasePower, Mathf.Min(1f, innerGlowBasePower * 2f), glowSpread);
                }
                else
                {
                    glowInner = 0f;
                    // TMP glow outer radius needs a larger scale factor than PSD size to
                    // visually match Photoshop outer glow on text. Empirically, a PSD size
                    // of 5px with 0 spread should land close to TMP Outer = 1.
                    glowOuter = Mathf.Clamp01(glowSize * Mathf.Lerp(2f, 1.35f, glowSpread));
                    glowOffset = 0f;
                    // Lower TMP glow power produces the softer, fuller falloff that matches
                    // Photoshop outer glow better. Higher spread should tighten the falloff.
                    glowPower = Mathf.Lerp(0.2f, 0.45f, glowSpread);
                }
            }
            float normalizedBevelSize = textInfo.HasBevel ? Mathf.Clamp01(textInfo.BevelSize / gradientScale) : 0f;
            float bevelSoftnessRatio = textInfo.HasBevel && textInfo.BevelSize > 0f ? Mathf.Clamp01(textInfo.BevelSoften / textInfo.BevelSize) : 0f;
            float targetBevelWidth = textInfo.HasBevel ? Mathf.Clamp(normalizedBevelSize * Mathf.Lerp(TmpBevelWidthScale, TmpBevelWidthScale * 0.75f, bevelSoftnessRatio), 0f, 0.5f) : 0f;
            float bevelWidth = textInfo.HasBevel ? Mathf.Clamp(targetBevelWidth - outlineWidth * Mathf.Lerp(1f, 0.65f, bevelSoftnessRatio), -0.5f, 0.5f) : 0f;
            float bevelAmount = textInfo.HasBevel ? (textInfo.BevelDepth > 1f ? Mathf.Clamp01(textInfo.BevelDepth / 100f) : Mathf.Clamp01(textInfo.BevelDepth)) * Mathf.Lerp(1f, 0.82f, bevelSoftnessRatio) : 0f;
            float bevelOffset = textInfo.HasBevel ? Mathf.Clamp((textInfo.BevelIsInner ? -1f : 1f) * normalizedBevelSize * Mathf.Lerp(0.12f, 0.04f, bevelSoftnessRatio), -0.5f, 0.5f) : 0f;
            float bevelClamp = textInfo.HasBevel ? Mathf.Clamp01(bevelSoftnessRatio * Mathf.Lerp(0.45f, 0.75f, normalizedBevelSize)) : 0f;
            float bevelRoundness = textInfo.HasBevel ? Mathf.Lerp(0.08f, 0.92f, bevelSoftnessRatio) : 0f;
            float lightAngle = textInfo.HasBevel ? ConvertPSDBevelAngleToTMPLightAngle(textInfo.BevelAngle) : 0f;
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
                float altitude = Mathf.Clamp01(textInfo.BevelAltitude / 90f);
                float highlightOpacity = Mathf.Clamp01(textInfo.BevelHighlightOpacity);
                float shadowOpacity = Mathf.Clamp01(textInfo.BevelShadowOpacity);
                float shadowStrength = shadowOpacity * (1f - GetColorLuminance(textInfo.BevelShadowColor));
                float highlightStrength = highlightOpacity * Mathf.Lerp(0.65f, 1f, bevelAmount);

                float specularStrength = Mathf.Clamp01(Mathf.Lerp(0.35f, 1f, shadowStrength));
                bevelSpecularColor = new Color(
                    textInfo.BevelShadowColor.r * specularStrength,
                    textInfo.BevelShadowColor.g * specularStrength,
                    textInfo.BevelShadowColor.b * specularStrength,
                    1f);
                float reflectionStrength = Mathf.Clamp01(Mathf.Lerp(0.22f, 0.72f, highlightStrength) * Mathf.Lerp(0.85f, 1.05f, altitude));
                bevelReflectFaceColor = new Color(
                    textInfo.BevelHighlightColor.r * reflectionStrength,
                    textInfo.BevelHighlightColor.g * reflectionStrength,
                    textInfo.BevelHighlightColor.b * reflectionStrength,
                    1f);
                Color outlineReflectionSource = textInfo.HasOutline
                    ? Color.Lerp(textInfo.OutlineColor, textInfo.BevelHighlightColor, 0.65f)
                    : textInfo.BevelHighlightColor;
                float outlineReflectionStrength = Mathf.Clamp01(reflectionStrength * (textInfo.HasOutline ? 1f : 0.9f));
                bevelReflectOutlineColor = new Color(
                    outlineReflectionSource.r * outlineReflectionStrength,
                    outlineReflectionSource.g * outlineReflectionStrength,
                    outlineReflectionSource.b * outlineReflectionStrength,
                    1f);
                bevelSpecularPower = highlightStrength > 0f ? Mathf.Clamp(Mathf.Lerp(0.2f, 2.2f, highlightStrength) * Mathf.Lerp(0.8f, 1.1f, altitude), 0f, 4f) : 0f;
                bevelReflectivity = Mathf.Lerp(14f, 6f, Mathf.Clamp01(bevelRoundness + (1f - altitude) * 0.25f));
                bevelDiffuse = shadowStrength > 0f ? Mathf.Clamp01(Mathf.Lerp(0.15f, 0.8f, shadowStrength) * Mathf.Lerp(1.1f, 0.75f, altitude)) : 0f;
                bevelAmbient = shadowStrength > 0f ? Mathf.Clamp01(1f - shadowStrength * Mathf.Lerp(0.75f, 0.45f, altitude)) : 1f;
            }
            Color32 outlineColor = textInfo.OutlineColor;
            Color32 shadowColor = textInfo.ShadowColor;
            Color32 glowColor = tmpGlowColor;

            int ow = Mathf.RoundToInt(outlineWidth * 10000f);
            int fd = Mathf.RoundToInt(faceDilate * 10000f);
            int sx = Mathf.RoundToInt(shadowOffsetX * 10000f);
            int sy = Mathf.RoundToInt(shadowOffsetY * 10000f);
            int od = Mathf.RoundToInt(outlineSoftness * 10000f);
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
            string effectSignature =
                $"o:{(textInfo.HasOutline ? 1 : 0)}|op:{(int)textInfo.TMPOutlinePosition}|ow:{ow}|fd:{fd}|os:{od}|oc:{outlineColor.r},{outlineColor.g},{outlineColor.b},{outlineColor.a}" +
                $"|s:{(textInfo.HasShadow ? 1 : 0)}|si:{(shadowIsInner ? 1 : 0)}|so:{sx},{sy}|sd:{sd}|ss:{ss}|sc:{shadowColor.r},{shadowColor.g},{shadowColor.b},{shadowColor.a}" +
                $"|g:{(textInfo.HasGlow ? 1 : 0)}|gi:{(textInfo.GlowIsInner ? 1 : 0)}|go:{go}|giw:{gi}|gof:{gOfs}|gp:{gp}|gc:{glowColor.r},{glowColor.g},{glowColor.b},{glowColor.a}" +
                $"|b:{(textInfo.HasBevel ? 1 : 0)}|bi:{(textInfo.BevelIsInner ? 1 : 0)}|ba:{ba}|bo:{bo}|bw:{bw}|bc:{bc}|br:{br}|la:{la}|bsc:{bevelSpecularColor32.r},{bevelSpecularColor32.g},{bevelSpecularColor32.b},{bevelSpecularColor32.a}|bsp:{bsp}|brv:{brv}|bdf:{bdf}|bam:{bam}|bf:{bf}";
            string key = $"{(fontAsset != null ? fontAsset.GetInstanceID() : 0)}|{effectSignature}";

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
                ApplyTMPEffectMaterialProperties(materialAsset, outlineWidth, faceDilate, outlineSoftness, outlineColor, textInfo.HasOutline,
                    shadowOffsetX, shadowOffsetY, shadowDilate, shadowSoftness, shadowColor, textInfo.HasShadow, shadowIsInner,
                    glowColor, glowOffset, glowInner, glowOuter, glowPower, textInfo.HasGlow,
                    bevelAmount, bevelOffset, bevelWidth, bevelClamp, bevelRoundness, lightAngle,
                    bevelSpecularColor, bevelReflectFaceColor, bevelReflectOutlineColor, bevelSpecularPower, bevelReflectivity, bevelDiffuse, bevelAmbient,
                    bevelShaderFlags, textInfo.HasBevel);
                EditorUtility.SetDirty(materialAsset);
                tmpEffectMaterialCache[key] = materialAsset;
                tmpEffectMaterialBaseLookup[materialAsset.GetInstanceID()] = baseMaterial;
                return materialAsset;
            }

            var mat = CreateTMPMaterialCopy(baseMaterial, true);
            if (mat == null)
            {
                return null;
            }
            mat.name = materialName;
            EnsureTMPMaterialHasAtlas(mat, fontAsset);
            ApplyTMPEffectMaterialProperties(mat, outlineWidth, faceDilate, outlineSoftness, outlineColor, textInfo.HasOutline,
                shadowOffsetX, shadowOffsetY, shadowDilate, shadowSoftness, shadowColor, textInfo.HasShadow, shadowIsInner,
                glowColor, glowOffset, glowInner, glowOuter, glowPower, textInfo.HasGlow,
                bevelAmount, bevelOffset, bevelWidth, bevelClamp, bevelRoundness, lightAngle,
                bevelSpecularColor, bevelReflectFaceColor, bevelReflectOutlineColor, bevelSpecularPower, bevelReflectivity, bevelDiffuse, bevelAmbient,
                bevelShaderFlags, textInfo.HasBevel);

            if (fontAsset != null)
            {
                PersistTMPEffectMaterial(fontAsset, mat, effectSignature);
            }

            tmpEffectMaterialCache[key] = mat;
            tmpEffectMaterialBaseLookup[mat.GetInstanceID()] = baseMaterial;
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
        private static float GetTMPMaterialWeight(Material mat)
        {
            if (mat == null)
            {
                return 0f;
            }

            float normalWeight = mat.HasProperty(ShaderUtilities.ID_WeightNormal) ? mat.GetFloat(ShaderUtilities.ID_WeightNormal) : 0f;
            float boldWeight = mat.HasProperty(ShaderUtilities.ID_WeightBold) ? mat.GetFloat(ShaderUtilities.ID_WeightBold) : 0f;
            return Mathf.Max(normalWeight, boldWeight) / 4f;
        }
        private static float GetTMPAvailableEffectBudget(Material mat, float faceDilate)
        {
            if (mat == null || !mat.HasProperty(ShaderUtilities.ID_GradientScale))
            {
                return 0f;
            }

            float scale = Mathf.Max(1f, mat.GetFloat(ShaderUtilities.ID_GradientScale));
            float weight = GetTMPMaterialWeight(mat);
            float range = (weight + faceDilate) * (scale - TmpShaderClamp);
            return Mathf.Max(0f, scale - TmpShaderClamp - range) / scale;
        }
        private static void FitTMPUnderlayToBudget(float budget, ref float offsetX, ref float offsetY, ref float dilate, ref float softness, bool allowNegativeDilate = false)
        {
            if (budget <= 0f)
            {
                offsetX = 0f;
                offsetY = 0f;
                dilate = 0f;
                softness = 0f;
                return;
            }

            offsetX = Mathf.Clamp(offsetX, -1f, 1f);
            offsetY = Mathf.Clamp(offsetY, -1f, 1f);
            dilate = Mathf.Clamp(dilate, allowNegativeDilate ? -1f : 0f, 1f);
            softness = Mathf.Clamp(softness, 0f, 1f);

            float offsetBudget = Mathf.Max(Mathf.Abs(offsetX), Mathf.Abs(offsetY));
            if (offsetBudget > budget && offsetBudget > 0f)
            {
                float scale = budget / offsetBudget;
                offsetX *= scale;
                offsetY *= scale;
                dilate = 0f;
                softness = 0f;
                return;
            }

            float remaining = budget - offsetBudget;
            float dilateMagnitude = Mathf.Min(Mathf.Abs(dilate), remaining);
            dilate = Mathf.Sign(dilate) * dilateMagnitude;
            remaining -= dilateMagnitude;
            softness = Mathf.Min(softness, remaining);
        }
        private static float NormalizeTMPBudgetedValue(float actualValue, float budget)
        {
            if (budget <= 0f)
            {
                return 0f;
            }

            return actualValue / budget;
        }
        private static float ConvertPSDBevelAngleToTMPLightAngle(float psdAngle)
        {
            float normalizedAngle = Mathf.Repeat(90f - psdAngle, 360f);
            return normalizedAngle * Mathf.Deg2Rad;
        }
        private static float GetColorLuminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }
        private static void ApplyTMPEffectMaterialProperties(Material mat,
            float outlineWidth, float faceDilate, float outlineSoftness, Color32 outlineColor, bool hasOutline,
            float shadowOffsetX, float shadowOffsetY, float shadowDilate, float shadowSoftness, Color32 shadowColor, bool hasShadow, bool shadowIsInner,
            Color32 glowColor, float glowOffset, float glowInner, float glowOuter, float glowPower, bool hasGlow,
            float bevelAmount, float bevelOffset, float bevelWidth, float bevelClamp, float bevelRoundness, float lightAngle,
            Color bevelSpecularColor, Color bevelReflectFaceColor, Color bevelReflectOutlineColor, float bevelSpecularPower, float bevelReflectivity, float bevelDiffuse, float bevelAmbient,
            float bevelShaderFlags, bool hasBevel)
        {
            if (mat == null) return;

            if (mat.HasProperty(TmpFaceDilatePropertyId))
            {
                mat.SetFloat(TmpFaceDilatePropertyId, hasOutline ? faceDilate : 0f);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineSoftness))
            {
                mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, outlineSoftness);
            }
            if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                mat.SetColor(ShaderUtilities.ID_OutlineColor, hasOutline ? outlineColor : Color.clear);
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
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, hasShadow ? shadowColor : Color.clear);
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
                mat.SetColor(ShaderUtilities.ID_GlowColor, hasGlow ? glowColor : Color.clear);
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
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
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
                materialPath = GetSiblingMaterialAssetPath(assetPath, material.name, false);
                if (string.IsNullOrWhiteSpace(materialPath))
                {
                    return;
                }

                AssetDatabase.CreateAsset(material, materialPath);
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath) ?? material;
            }

            EnsureTMPMaterialHasAtlas(material, fontAsset);
            SetTMPEffectMaterialSignature(fontAsset, materialPath, effectSignature);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(fontAsset);
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
            var parts = new List<string>(4);
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
                parts.Add(textInfo.GlowIsInner ? "InnerGlow" : "Glow");
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
            if (string.IsNullOrWhiteSpace(ownerAssetPath) || !ownerAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.GetDirectoryName(ownerAssetPath)?.Replace("\\", "/");
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
                if (subAssets[i] is not Texture2D texture)
                {
                    continue;
                }

                fallback ??= texture;
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

            ShaderUtilities.GetShaderPropertyIDs();
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

            ShaderUtilities.GetShaderPropertyIDs();
            Material material;
            var distanceFieldShader = preferDistanceFieldShader ? Shader.Find("TextMeshPro/Distance Field") : null;
            if (distanceFieldShader != null && baseMaterial.shader != distanceFieldShader)
            {
                material = new Material(distanceFieldShader);
                material.CopyPropertiesFromMaterial(baseMaterial);
            }
            else
            {
                material = new Material(baseMaterial);
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
        private static TMP_FontAsset CreateTMPFontAsset(UnityEngine.Font sourceFont)
        {
            if (sourceFont == null) return null;
            if (TMP_Settings.instance == null)
            {
                Debug.LogWarning("Unable to create TMP font asset because TMP Essential Resources are missing.");
                return null;
            }

            ShaderUtilities.GetShaderPropertyIDs();
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
            var existingFontAsset = FindExistingTMPFontBySource(sourceFont, sourceFontGuid);
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
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{assetBaseName} SDF.asset");
            var fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
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
            persistedFontAsset ??= fontAsset;
            EnsureTMPFontAssetResources(persistedFontAsset);
            return persistedFontAsset;
        }
        private static TMP_FontAsset FindExistingTMPFontBySource(UnityEngine.Font sourceFont, string sourceFontGuid)
        {
            if (sourceFont == null) return null;

            var fontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var guid in fontGuids)
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (IsTMPFontFromSource(font, sourceFont, sourceFontGuid))
                {
                    return font;
                }
            }

            return null;
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
            public string Role;
            public string ImageType;
            public string TextBackend;
        }

        private sealed class PsRasterizeAsImageConfig
        {
            public readonly HashSet<string> MainIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> RoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string GetPsFamilyLabel(string familyKey)
        {
            switch (familyKey)
            {
                case "main":
                    return "结构标签";
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

        private static bool ShouldPsRasterizeAsImageRoleType(GUIType uiType)
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
                Label = string.IsNullOrWhiteSpace(label) ? canonicalId : label
            });
        }

        private static void AddPsAliasRule(
            Dictionary<string, PsTagAliasRule> aliasRules,
            string alias,
            string main = null,
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

            if (!string.IsNullOrWhiteSpace(main)) aliasRule.Main = main;
            if (!string.IsNullOrWhiteSpace(role)) aliasRule.Role = role;
            if (!string.IsNullOrWhiteSpace(imageType)) aliasRule.ImageType = imageType;
            if (!string.IsNullOrWhiteSpace(textBackend)) aliasRule.TextBackend = textBackend;
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
                : $"{rule.UIType}\\n{rule.UITypeDesc}";

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
            else if (ShouldPsRasterizeAsImageRoleType(rule.UIType))
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

            AddPsMenuItem(familyItems, "textBackend", "tmp", "TMP\\nTMP文本后端");
            AddPsMenuItem(familyItems, "textBackend", "ugui", "UGUI\\n原生文本后端");
            AddPsAliasRule(aliasRules, "tmp", textBackend: "tmp");
            AddPsAliasRule(aliasRules, "ugui", textBackend: "ugui");

            AddPsMenuItem(familyItems, "imageType", "simple", "Simple\\n普通");
            AddPsMenuItem(familyItems, "imageType", "sliced", "Sliced\\n九宫格");
            AddPsMenuItem(familyItems, "imageType", "tiled", "Tiled\\n平铺");
            AddPsMenuItem(familyItems, "imageType", "filled", "Filled\\n填充");
            AddPsAliasRule(aliasRules, "simple", imageType: "simple");
            AddPsAliasRule(aliasRules, "sliced", imageType: "sliced");
            AddPsAliasRule(aliasRules, "tiled", imageType: "tiled");
            AddPsAliasRule(aliasRules, "filled", imageType: "filled");

            var familyOrder = new[] { "main", "textBackend", "imageType", "role" };
            var builder = new StringBuilder();
            builder.AppendLine("var TAG_CONFIG = {");
            builder.AppendLine("    canonicalOrder: [\"main\", \"textBackend\", \"imageType\", \"role\"],");
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

        private static bool ReplacePsTagConfigBlock(string scriptPath, string tagConfigBlock, out string error)
        {
            error = null;
            if (!File.Exists(scriptPath))
            {
                error = $"PS script file does not exist:\n{scriptPath}";
                return false;
            }

            var scriptContent = File.ReadAllText(scriptPath, Encoding.UTF8);
            var configPattern = new Regex(@"var\s+TAG_CONFIG\s*=\s*\{[\s\S]*?\};", RegexOptions.Multiline);
            if (!configPattern.IsMatch(scriptContent))
            {
                error = $"TAG_CONFIG was not found in PS script:\n{scriptPath}";
                return false;
            }

            var newContent = configPattern.Replace(scriptContent, tagConfigBlock, 1);
            File.WriteAllText(scriptPath, newContent, Encoding.UTF8);
            AssetDatabase.ImportAsset(scriptPath);
            return true;
        }

        internal void ExportLayerTagMenuConfig()
        {
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            var tagConfigBlock = BuildPsTagConfigBlock();
            var errors = new List<string>();
            var targetScripts = new[] { LayerTagMenuScriptPath, LayerExportScriptPath };
            for (int i = 0; i < targetScripts.Length; i++)
            {
                var scriptPath = targetScripts[i];
                if (!ReplacePsTagConfigBlock(scriptPath, tagConfigBlock, out var error))
                {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Export Rules Failed", string.Join("\n\n", errors), "OK");
                return;
            }

            EditorUtility.DisplayDialog("Export Rules Succeeded", string.Join("\n", targetScripts), "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LayerTagMenuScriptPath);
        }
    }
    internal struct TextGradientStop
    {
        public float Location;
        public Color Color;
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
        public int FontSize;
        public bool IsAutoLineSpacing;
        public float LineSpacing;
        public float CharacterSpacing;
        public Color Color;
        public FontStyle FontStyle;
        public TMPro.FontStyles TMPFontStyle;
        public string FontName;
        public bool HasOutline;
        public Color OutlineColor;
        public float OutlineSize;
        public float TMPOutlineSize;
        public TMPOutlineMode TMPOutlinePosition;
        public bool HasShadow;
        public bool ShadowIsInner;
        public Color ShadowColor;
        public Vector2 ShadowOffset;
        public float ShadowSpread;
        public float ShadowSoftness;
        public bool HasGlow;
        public bool GlowIsInner;
        public Color GlowColor;
        public float GlowSize;
        public float GlowSpread;
        public float GlowOffset;
        public float GlowPower;
        public bool HasBevel;
        public bool BevelIsInner;
        public float BevelSize;
        public float BevelDepth;
        public float BevelSoften;
        public float BevelAngle;
        public float BevelAltitude;
        public Color BevelHighlightColor;
        public float BevelHighlightOpacity;
        public Color BevelShadowColor;
        public float BevelShadowOpacity;
        public bool HasGradient;
        public float GradientAngle;
        public bool GradientReverse;
        public string GradientStyleKey;
        public string GradientBlendModeKey;
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
