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
插件获取:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cn.efunstudio.psdreader;
using cn.efunstudio.psdreader.PsdParser;
using UnityEditor;
using UnityEditor.SceneManagement;
#if !UNITY_2021_2_OR_NEWER
using PrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
#if UNITY_6000_3_OR_NEWER
using PsdObjectId = UnityEngine.EntityId;
#else
using PsdObjectId = System.Int32;
#endif
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [CustomEditor(typeof(Psd2UIFormConverter))]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class Psd2UIFormConverterInspector : UnityEditor.Editor
    {
        Psd2UIFormConverter targetLogic;

        GUIContent parsePsd2NodesBt;
        GUIContent exportUISpritesBt;
        GUIContent aiAutoFixBt;
        GUIContent applyAiResultBt;
        GUIContent generateUIFormBt;
        GUIContent openParserConfigBt;
        GUILayoutOption btHeight;
        bool metadataDebugFoldout;
        int metadataDebugSelection;
        bool metadataDebugShowJson;
        Vector2 metadataDebugScroll;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            btHeight = GUILayout.Height(30);
            targetLogic = target as Psd2UIFormConverter;
            parsePsd2NodesBt = new GUIContent("同步最新 PSD", "更新素材，保留已确认类型、节点身份和人工调整");
            exportUISpritesBt = new GUIContent("导出Images", "导出勾选的psd图层为碎图");
            aiAutoFixBt = new GUIContent("AI自动识别UI类型", "从原始 PSD 素材快照识别 UI 类型与结构");
            applyAiResultBt = new GUIContent("应用AI结果", "应用当前 AI 识别数据 到节点树");
            generateUIFormBt = new GUIContent("生成 / 更新 UIForm", "首次生成，或在已关联 prefab 上更新；保留对象引用和人工字段修改");
            openParserConfigBt = new GUIContent("打开设置", "选中并定位PSD2UIForm全局解析和生成规则配置");
#if !EFUN_PRIVATE
            if (Psd2UIFormSettings.Instance.CompressImage)
            {
                Psd2UIFormSettings.Instance.CompressImage = false;
                Psd2UIFormSettings.Save();
            }
#endif
            if (string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.UIFormOutputDir))
            {
                Debug.LogWarning($"UIForm输出路径为空!");
            }
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDisable()
        {
            Psd2UIFormSettings.Save();
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override void OnInspectorGUI()
        {
            bool requestExitGUI = false;
            PsdReaderProductAccess.EnsureDailyUpdateCheck();
            DrawPendingUpdateTip();

            if (!targetLogic.Initiated)
            {
                EditorGUILayout.HelpBox("请打开Prefab,进入Psd2UIForm编辑界面后才能操作!", MessageType.Error);
                if (GUILayout.Button("打开编辑界面"))
                {
                    var assetPath = AssetDatabase.GetAssetPath(target);
                    Psd2UIFormConverter.OpenPsdLayerEditor(assetPath);
                }
                return;
            }
            EditorGUILayout.BeginVertical("box");
            {
#if EFUN_PRIVATE
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("自动压缩图片:", GUILayout.Width(150));
                    Psd2UIFormSettings.Instance.CompressImage = EditorGUILayout.Toggle(Psd2UIFormSettings.Instance.CompressImage);
                    EditorGUILayout.EndHorizontal();
                }
#endif
                if (GUILayout.Button("查看使用文档"))
                {
                    Application.OpenURL("https://efunstudio.cn");
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(openParserConfigBt))
                {
                    SelectUGUIParserConfig();
                    requestExitGUI = true;
                }
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("UI图片导出路径:", GUILayout.Width(150));
                    Psd2UIFormSettings.Instance.UIImagesOutputDir = EditorGUILayout.TextField(Psd2UIFormSettings.Instance.UIImagesOutputDir);
                    if (GUILayout.Button("选择路径", GUILayout.Width(80)))
                    {
                        var retPath = EditorUtility.OpenFolderPanel("选择导出路径", Psd2UIFormSettings.Instance.UIImagesOutputDir, null);
                        if (!string.IsNullOrWhiteSpace(retPath))
                        {
                            if (!retPath.StartsWith("Assets/"))
                            {
                                retPath = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, retPath);
                            }
                            Psd2UIFormSettings.Instance.UIImagesOutputDir = retPath;
                            Psd2UIFormSettings.Save();
                        }
                        requestExitGUI = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                {
                    Psd2UIFormSettings.Instance.UseUIFormOutputDir = EditorGUILayout.ToggleLeft("使用UIForm导出路径:", Psd2UIFormSettings.Instance.UseUIFormOutputDir, GUILayout.Width(150));
                    EditorGUI.BeginDisabledGroup(!Psd2UIFormSettings.Instance.UseUIFormOutputDir);
                    {
                        Psd2UIFormSettings.Instance.UIFormOutputDir = EditorGUILayout.TextField(Psd2UIFormSettings.Instance.UIFormOutputDir);
                        if (GUILayout.Button("选择路径", GUILayout.Width(80)))
                        {
                            var retPath = EditorUtility.OpenFolderPanel("选择导出路径", Psd2UIFormSettings.Instance.UIFormOutputDir, null);
                            if (!string.IsNullOrWhiteSpace(retPath))
                            {
                                if (!retPath.StartsWith("Assets/"))
                                {
                                    retPath = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, retPath);
                                }
                                // 面板选出来的是 OS 路径（Windows 带 \），存进设置前统一成资产路径。
                                retPath = retPath.Replace("\\", "/");
                                Psd2UIFormSettings.Instance.UIFormOutputDir = retPath;
                                Psd2UIFormSettings.Save();
                            }
                            requestExitGUI = true;
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(parsePsd2NodesBt, btHeight))
                {
                    Psd2UIFormConverter.ParsePsd2LayerPrefab(targetLogic.PsdAssetName, targetLogic);
                }
                if (GUILayout.Button(exportUISpritesBt, btHeight))
                {
                    targetLogic.ExportSprites();
                }
                if (GUILayout.Button(new GUIContent("整理复用图片", "比较当前文档已导出的图片，迁移生成结果中的绑定，并清理没有引用的重复图片。"), btHeight))
                {
                    EditorUtility.DisplayDialog("图片复用", targetLogic.ConsolidateImages(), "确定");
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(aiAutoFixBt, btHeight))
                {
                    if (!AiAutoFixWorkflow.Start(targetLogic, out var aiError))
                    {
                        EditorUtility.DisplayDialog("AI 识别无法启动", aiError, "确定");
                    }
                }
                if (GUILayout.Button(applyAiResultBt, btHeight))
                {
                    if (!AiAutoFixWorkflow.ApplyCurrentResult(targetLogic, out var applyError))
                    {
                        EditorUtility.DisplayDialog("应用AI结果失败", applyError, "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button(generateUIFormBt, btHeight))
            {
                targetLogic.GenerateUIForm();
            }
            DrawMetadataDebugPanel();
            base.OnInspectorGUI();

            if (requestExitGUI)
            {
                GUIUtility.ExitGUI();
            }
        }

        private static void SelectUGUIParserConfig()
        {
            var parser = UGUIParser.Instance;
            if (parser == null)
            {
                EditorUtility.DisplayDialog("UGUIParser配置未找到", "项目中未找到 UGUIParser 配置实例。", "确定");
                return;
            }

            Selection.activeObject = parser;
            EditorGUIUtility.PingObject(parser);
        }

        private static void DrawPendingUpdateTip()
        {
            if (!PsdReaderProductAccess.HasPendingUpdateTip())
            {
                return;
            }

            if (Psd2UIFormEditorNoticeUtility.DrawVersionUpdateNotice(PsdReaderProductAccess.GetPendingUpdateTipMessage(), "下载"))
            {
                if (PsdReaderProductAccess.TryOpenPendingUpdateDownloadUrl())
                {
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawMetadataDebugPanel()
        {
            metadataDebugFoldout = EditorGUILayout.Foldout(metadataDebugFoldout, "生成元数据调试", true);
            if (!metadataDebugFoldout) return;

            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("数据宿主:", targetLogic.GetGeneratedMetadataHostAssetPathForDebug() ?? "<None>");

                var metadataTargets = targetLogic.GetGeneratedMetadataTargetsForDebug();
                if (metadataTargets == null || metadataTargets.Length < 1)
                {
                    metadataDebugShowJson = false;
                    EditorGUILayout.HelpBox("当前未找到生成元数据。先执行一次生成UIForm或增量导出后再查看。", MessageType.Info);
                }
                else
                {
                    metadataDebugSelection = Mathf.Clamp(metadataDebugSelection, 0, metadataTargets.Length - 1);
                    metadataDebugSelection = EditorGUILayout.Popup("目标Prefab", metadataDebugSelection, metadataTargets);

                    var selectedTarget = metadataTargets[metadataDebugSelection];
                    EditorGUILayout.LabelField("路径:", selectedTarget);

                    var json = targetLogic.GetGeneratedMetadataJsonForDebug(selectedTarget);
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button(metadataDebugShowJson ? "隐藏内容" : "查看元数据", GUILayout.Width(100)))
                        {
                            metadataDebugShowJson = !metadataDebugShowJson;
                        }
                        if (GUILayout.Button("复制JSON", GUILayout.Width(100)))
                        {
                            EditorGUIUtility.systemCopyBuffer = json;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    if (metadataDebugShowJson)
                    {
                        metadataDebugScroll = EditorGUILayout.BeginScrollView(metadataDebugScroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(320));
                        EditorGUILayout.TextArea(json ?? string.Empty, GUILayout.ExpandHeight(true));
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override bool HasPreviewGUI()
        {
            return targetLogic.BindPsdAsset != null;
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            GUI.DrawTexture(r, targetLogic.BindPsdAsset.texture, ScaleMode.ScaleToFit);
            //base.OnPreviewGUI(r, background);
        }
    }

    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal static class Psd2UIFormEditorNoticeUtility
    {
        private static readonly Color VersionUpdateBackgroundColor = new Color(0.36f, 0.08f, 0.08f, 1f);
        private static readonly Color VersionUpdateTextColor = new Color(1f, 0.93f, 0.93f, 1f);
        private const float WarningIconSize = 20f;
        private const float WarningIconSpacing = 8f;
        private static GUIStyle s_versionUpdateStyle;
        private static GUIStyle s_versionUpdateButtonStyle;
        private static GUIContent s_warningIconContent;
        private static readonly GUIContent ReleaseNotesButtonContent = new GUIContent("更新日志");

        internal static bool DrawVersionUpdateNotice(string message, string buttonLabel)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            GUIStyle style = GetVersionUpdateStyle();
            GUIStyle buttonStyle = GetVersionUpdateButtonStyle();
            GUIContent content = new GUIContent(message);
            GUIContent buttonContent = new GUIContent(string.IsNullOrWhiteSpace(buttonLabel) ? "下载" : buttonLabel);
            GUIContent warningIconContent = GetWarningIconContent();
            float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 56f);
            float buttonWidth = 68f;
            float buttonSpacing = 6f;
            float innerWidth = Mathf.Max(80f, width - style.padding.horizontal);
            float labelWidth = Mathf.Max(60f, innerWidth - buttonWidth * 2f - buttonSpacing - WarningIconSize - WarningIconSpacing - 8f);
            float labelHeight = style.CalcHeight(content, labelWidth);
            float buttonHeight = Mathf.Max(EditorGUIUtility.singleLineHeight + 2f, buttonStyle.CalcHeight(buttonContent, buttonWidth));
            float height = Mathf.Max(Mathf.Max(labelHeight, buttonHeight), WarningIconSize) + style.padding.vertical;

            Rect rect = EditorGUILayout.GetControlRect(false, height);
            rect = EditorGUI.IndentedRect(rect);
            EditorGUI.DrawRect(rect, VersionUpdateBackgroundColor);

            Rect contentRect = new Rect(
                rect.x + style.padding.left,
                rect.y + style.padding.top,
                Mathf.Max(1f, rect.width - style.padding.horizontal),
                Mathf.Max(1f, rect.height - style.padding.vertical));

            Rect buttonRect = new Rect(
                contentRect.xMax - buttonWidth,
                contentRect.y + Mathf.Max(0f, (contentRect.height - buttonHeight) * 0.5f),
                buttonWidth,
                buttonHeight);

            Rect releaseNotesButtonRect = new Rect(
                buttonRect.x - buttonSpacing - buttonWidth,
                buttonRect.y,
                buttonWidth,
                buttonHeight);

            Rect iconRect = new Rect(
                contentRect.x,
                contentRect.y + Mathf.Max(0f, (contentRect.height - WarningIconSize) * 0.5f),
                WarningIconSize,
                WarningIconSize);

            Rect labelRect = new Rect(
                iconRect.xMax + WarningIconSpacing,
                contentRect.y,
                Mathf.Max(1f, releaseNotesButtonRect.x - iconRect.xMax - WarningIconSpacing - 8f),
                contentRect.height);

            GUI.Label(iconRect, warningIconContent);
            GUI.Label(labelRect, content, style);
            if (GUI.Button(releaseNotesButtonRect, ReleaseNotesButtonContent, buttonStyle))
            {
                Application.OpenURL(PsdReaderProductAccess.ReleaseNotesUrl);
            }
            return GUI.Button(buttonRect, buttonContent, buttonStyle);
        }

        private static GUIStyle GetVersionUpdateStyle()
        {
            if (s_versionUpdateStyle != null)
            {
                return s_versionUpdateStyle;
            }

            s_versionUpdateStyle = new GUIStyle(EditorStyles.helpBox)
            {
                wordWrap = true,
                richText = false,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8),
            };

            s_versionUpdateStyle.normal.textColor = VersionUpdateTextColor;
            s_versionUpdateStyle.hover.textColor = VersionUpdateTextColor;
            s_versionUpdateStyle.active.textColor = VersionUpdateTextColor;
            s_versionUpdateStyle.focused.textColor = VersionUpdateTextColor;
            return s_versionUpdateStyle;
        }

        private static GUIStyle GetVersionUpdateButtonStyle()
        {
            if (s_versionUpdateButtonStyle != null)
            {
                return s_versionUpdateButtonStyle;
            }

            s_versionUpdateButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 22f,
            };

            return s_versionUpdateButtonStyle;
        }

        private static GUIContent GetWarningIconContent()
        {
            if (s_warningIconContent != null)
            {
                return s_warningIconContent;
            }

            s_warningIconContent = EditorGUIUtility.IconContent("console.warnicon");
            if (s_warningIconContent == null || s_warningIconContent.image == null)
            {
                s_warningIconContent = EditorGUIUtility.IconContent("console.warnicon.sml");
            }

            return s_warningIconContent ?? GUIContent.none;
        }
    }
    /// <summary>
    /// Psd文件转成UIForm prefab
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(SpriteRenderer))]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed partial class Psd2UIFormConverter : MonoBehaviour
    {
        const string RecordLayerOperation = "Change Export Image";
        const string GeneratedOrphansRootName = "__PSD2UIForm_Orphans";
        const string PreviewSpriteTagKey = "Psd2UIFormPreview";
        const string PreviewSpriteTagVersion = "v2";
        const float GroupOpacityOpaqueThreshold = 0.99999f;
        const float GroupOpacityTransparentThreshold = 0.00001f;
        internal static Psd2UIFormConverter Instance { get; private set; }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] internal string psdAssetChangeTime;//文件修改时间标识
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("UIForm名字")][SerializeField] private string uiFormName;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("关联的psd文件")][SerializeField] private UnityEngine.Sprite psdAsset;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private UnityEngine.Sprite previewSprite;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private string psdAssetPath;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Header("Debug:")][SerializeField] bool drawLayerRectGizmos = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] UnityEngine.Color drawLayerRectGizmosColor = UnityEngine.Color.gray;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("Scene点选时，优先选中命中区域内面积最小的图层节点")][SerializeField] bool preferSmallestLayerOnScenePick = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private List<GeneratedMetadataSerializedEntry> generatedMetadataEntries { get => Document.outputs; set => Document.outputs = value; }
        private PsdDocument psdInstance;//psd文件解析实例
        private GUIStyle uiTypeLabelStyle;
        private readonly Dictionary<string, PsdLayerNode> layerLookupByName = new Dictionary<string, PsdLayerNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sharedSpriteAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 本轮真正写出/复用的图片路径，只用于生成后提示“目录里还留着哪些历史文件”，插件不据此删除任何东西。
        private readonly HashSet<string> exportedImagePathsThisRound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal void RegisterExportedImagePath(string assetPath)
        {
            if (!string.IsNullOrEmpty(assetPath)) exportedImagePathsThisRound.Add(assetPath.Replace("\\", "/"));
        }
        internal List<string> CollectStaleGeneratedImages(string dir)
        {
            var stale = new List<string>();
            if (string.IsNullOrEmpty(dir) || exportedImagePathsThisRound.Count == 0 || !Directory.Exists(dir)) return stale;
            foreach (string file in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
            {
                string asset = file.Replace("\\", "/");
                int cut = asset.IndexOf("Assets/", StringComparison.Ordinal);
                if (cut > 0) asset = asset.Substring(cut);
                if (!exportedImagePathsThisRound.Contains(asset)) stale.Add(Path.GetFileName(asset));
            }
            return stale;
        }
        private void ReportStaleGeneratedImages()
        {
            var stale = CollectStaleGeneratedImages(GetUIFormImagesOutputDir());
            if (stale.Count == 0) return;
            Debug.LogWarning($"出图目录里有 {stale.Count} 张本轮没有写出的图片（多为历次生成留下的历史文件，插件不会自动删除）: "
                + string.Join(", ", stale.Take(20)) + (stale.Count > 20 ? " …" : ""));
        }
        private readonly Dictionary<string, GameObject> sharedPrefabAssets = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> referencedLayerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> sharedPrefabExportInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class GeneratedMetadataCollection
        {
            public List<GeneratedMetadataEntry> Entries = new List<GeneratedMetadataEntry>();
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        internal sealed class GeneratedMetadataEntry
        {
            public string ParentIdentity;
            public string GlobalObjectId;
            public string NodeId;
            public string SourceIdentity;
            public string Key;
            public string TypeKey;
            public bool IsContainer;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        internal sealed class GeneratedMetadataSerializedEntry
        {
            public List<PsdPropertyRecord> Properties = new List<PsdPropertyRecord>();
            public string PrefabAssetPath;
            public string PrefabAssetGuid;
            public string PsdAssetGuid;
            public string RootTypeKey;
            public List<GeneratedMetadataEntry> Entries = new List<GeneratedMetadataEntry>();
        }

        private sealed class GeneratedNodeSnapshot
        {
            public string ParentIdentity;
            public string SourceIdentity;
            public string Key;
            public string TypeKey;
            public bool IsContainer;
            public List<int> TransformPath;
        }

        internal bool Initiated => psdInstance != null;
        /// <summary>
        /// 已经解析的 PSD 文档实例。AI 分析包导出时需要它访问图层的原生 PSD 像素坐标
        /// (PsdLayer.Left/Top/Right/Bottom) 以便与预览图像素精确对齐。
        /// </summary>
        internal PsdDocument PsdInstance => psdInstance;
        internal string PsdAssetName
        {
            get
            {
                if (psdAsset != null)
                {
                    var assetPath = AssetDatabase.GetAssetPath(psdAsset);
                    if (!string.IsNullOrWhiteSpace(assetPath)) return assetPath;
                }
                return string.IsNullOrWhiteSpace(psdAssetPath) ? null : psdAssetPath;
            }
        }
        internal string UIFormName => uiFormName;
        internal UnityEngine.Sprite BindPsdAsset => psdAsset != null ? psdAsset : previewSprite;
        internal Vector2Int UIFormCanvasSize => psdInstance != null ? new Vector2Int(psdInstance.Width, psdInstance.Height) : Vector2Int.zero;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            Instance = this;
            uiTypeLabelStyle = new GUIStyle();
            uiTypeLabelStyle.fontSize = 13;
            uiTypeLabelStyle.fontStyle = UnityEngine.FontStyle.BoldAndItalic;
            UnityEngine.ColorUtility.TryParseHtmlString("#7ED994", out var color);
            uiTypeLabelStyle.normal.textColor = color;

            if (psdInstance == null && !string.IsNullOrWhiteSpace(PsdAssetName))
            {
                RefreshNodesBindLayer();
            }
            Undo.undoRedoPerformed -= RefreshAiBindingsAfterUndo;
            Undo.undoRedoPerformed += RefreshAiBindingsAfterUndo;
            PrefabStage.prefabSaving -= SaveAuthoringDocument;
            PrefabStage.prefabSaving += SaveAuthoringDocument;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
#if UNITY_6000_3_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void Start()
        {
            RefreshNodesBindLayer();
        }


        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDrawGizmos()
        {
            if (drawLayerRectGizmos)
            {
                var nodes = this.GetComponentsInChildren<PsdLayerNode>();
                PsdLayerNode nodeSelected = null;
                Gizmos.color = drawLayerRectGizmosColor;
                var currentSelect = Selection.activeGameObject;
                foreach (var item in nodes)
                {
                    if (item.gameObject == currentSelect)
                    {
                        nodeSelected = item;
                        continue;
                    }
                    if (item.NeedExportImage())
                    {
                        Gizmos.DrawWireCube(item.LayerRect.position * 0.01f, item.LayerRect.size * 0.01f);
                    }
                }
                if (nodeSelected != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireCube(nodeSelected.LayerRect.position * 0.01f, nodeSelected.LayerRect.size * 0.01f);
                }
            }
        }


        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e != null && e.type == EventType.MouseUp && e.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Plane plane = new Plane(Vector3.forward, Vector3.zero);
                if (!plane.Raycast(ray, out float enter))
                    return;

                Vector3 worldHitPos = ray.GetPoint(enter);
                PsdLayerNode clickNode = preferSmallestLayerOnScenePick
                    ? FindSmallestHitPsdLayerNode(worldHitPos, transform)
                    : FindTopMostPsdLayerNode(worldHitPos, transform);
                if (clickNode != null)
                {
                    Selection.activeGameObject = clickNode.gameObject;
                    EditorGUIUtility.PingObject(clickNode.gameObject);
                    Event.current.Use();
                }
            }
        }
        private static PsdLayerNode FindTopMostPsdLayerNode(Vector3 worldPosition, Transform root)
        {
            if (root == null) return null;
            if (!IsScenePickable(root.gameObject)) return null;

            int childCount = root.childCount;

            // 1. 先从子节点（从上到下）开始，保证上层优先
            for (int i = childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                PsdLayerNode hitInChild = FindTopMostPsdLayerNode(worldPosition, child);
                if (hitInChild != null)
                    return hitInChild;
            }

            // 再检测自己
            PsdLayerNode selfNode = root.GetComponent<PsdLayerNode>();
            if (selfNode != null)
            {
                if (IsHitWorldAABB(worldPosition, selfNode.LayerRect))
                    return selfNode;
            }

            return null;
        }

        private static PsdLayerNode FindSmallestHitPsdLayerNode(Vector3 worldPosition, Transform root)
        {
            PsdLayerNode bestNode = null;
            float bestArea = float.MaxValue;
            FindSmallestHitPsdLayerNode(worldPosition, root, ref bestNode, ref bestArea);
            return bestNode;
        }

        private static void FindSmallestHitPsdLayerNode(Vector3 worldPosition, Transform root, ref PsdLayerNode bestNode, ref float bestArea)
        {
            if (root == null) return;
            if (!IsScenePickable(root.gameObject)) return;

            int childCount = root.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                FindSmallestHitPsdLayerNode(worldPosition, root.GetChild(i), ref bestNode, ref bestArea);
            }

            PsdLayerNode selfNode = root.GetComponent<PsdLayerNode>();
            if (selfNode == null || !IsHitWorldAABB(worldPosition, selfNode.LayerRect))
            {
                return;
            }

            float selfArea = GetLayerArea(selfNode.LayerRect);
            if (selfArea < bestArea)
            {
                bestArea = selfArea;
                bestNode = selfNode;
            }
        }

        private static float GetLayerArea(Rect layerRect)
        {
            Vector2 size = layerRect.size;
            return size.x * size.y;
        }

        private static bool IsScenePickable(GameObject go)
        {
            if (go == null || !go.activeInHierarchy)
            {
                return false;
            }

            HideFlags hideFlags = go.hideFlags;
            if ((hideFlags & HideFlags.HideInHierarchy) != 0)
            {
                return false;
            }

            return !SceneVisibilityManager.instance.IsHidden(go);
        }

        private static bool IsHitWorldAABB(Vector3 worldPos, Rect layerRect)
        {
            Vector2 worldCenter = layerRect.position * 0.01f;
            Vector2 worldSize = layerRect.size * 0.01f;

            Vector2 halfSize = worldSize * 0.5f;
            Vector2 min = worldCenter - halfSize;
            Vector2 max = worldCenter + halfSize;

            return worldPos.x >= min.x && worldPos.x <= max.x &&
                   worldPos.y >= min.y && worldPos.y <= max.y;
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static GameObject HierarchyIdToGameObject(
#if UNITY_6000_3_OR_NEWER
            EntityId entityId
#else
            int instanceId
#endif
        )
        {
#if UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
        }

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

        private void OnHierarchyGUI(
#if UNITY_6000_3_OR_NEWER
            EntityId entityId,
#else
            int instanceID,
#endif
            Rect selectionRect)
        {
            if (Event.current == null) return;
            var node = HierarchyIdToGameObject(
#if UNITY_6000_3_OR_NEWER
                entityId
#else
                instanceID
#endif
            );
            if (node == null || node == this.gameObject) return;
            if (!node.TryGetComponent<PsdLayerNode>(out var layer)) return;

            Rect tmpRect = selectionRect;
            tmpRect.x = 35;
            tmpRect.width = 10;
            Undo.RecordObject(layer, RecordLayerOperation);
            EditorGUI.BeginChangeCheck();
            {
                layer.markToExport = EditorGUI.Toggle(tmpRect, layer.markToExport);
                if (EditorGUI.EndChangeCheck())
                {
                    if (Selection.gameObjects.Length > 1) SetExportImageTg(Selection.gameObjects, layer.markToExport);
                    EditorUtility.SetDirty(layer);
                }
            }
            tmpRect.width = Mathf.Clamp(selectionRect.xMax * 0.2f, 100, 200);
            tmpRect.x = selectionRect.xMax - tmpRect.width;
            if (EditorGUI.DropdownButton(tmpRect, new GUIContent(layer.UIType.ToString()), FocusType.Passive))
            {
                var dropdownMenu = PopUITypesMenu(layer, selectUIType =>
                {
                    var selectObjs = Selection.gameObjects;
                    if (selectObjs.Length > 1)
                    {
                        bool changed = false;
                        foreach (var item in selectObjs)
                        {
                            var psdLayerNode = item?.GetComponent<PsdLayerNode>();
                            if (psdLayerNode != null)
                            {
                                psdLayerNode.SetUIType(selectUIType, false);
                                EditorUtility.SetDirty(psdLayerNode);
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            RefreshAllLayerNodeHelpers();
                        }
                    }
                    else
                    {
                        layer.SetUIType(selectUIType);
                    }
                });

                dropdownMenu.ShowAsContext();
            }
            if (layer.HasReuseReference || layer.HasReusePrefabReference)
            {
                var content = new GUIContent(layer.name);
                var calcSize = GUI.skin.button.CalcSize(content);
                var buttonWidth = Mathf.Min(calcSize.x + 8f, 100f);
                var buttonRect = new Rect(selectionRect.xMax - tmpRect.width - buttonWidth, selectionRect.y, buttonWidth, selectionRect.height);
                if (GUI.Button(buttonRect, content))
                {
                    if (layer.HasReuseReference && layer.TryGetReuseTarget(out PsdLayerNode targetNode))
                    {
                        Selection.activeGameObject = targetNode.gameObject;
                    }
                    else if (layer.HasReuseReference && layer.TryGetReuseTargetAsset(out UnityEngine.Object targetAsset))
                    {
                        Selection.activeObject = targetAsset;
                    }
                    else if (layer.HasReusePrefabReference && layer.TryGetReusePrefabAsset(out var targetPrefab))
                    {
                        Selection.activeObject = targetPrefab;
                    }
                }
            }
        }
        private GenericMenu PopUITypesMenu(PsdLayerNode layer, Action<GUIType> onSelectEnum)
        {
            var names = Enum.GetValues(typeof(GUIType));
            var dropdownMenu = new GenericMenu();
            foreach (GUIType item in names)
            {
                string itemName = UGUIParser.IsMainUIType(item) ? item.ToString() : item.ToString().Replace('_', '/');
                dropdownMenu.AddItem(new GUIContent(itemName), item.Equals(layer.UIType), () => { onSelectEnum(item); });
            }
            return dropdownMenu;
        }

        /// <summary>
        /// 批量勾选导出图片
        /// </summary>
        /// <param name="selects"></param>
        /// <param name="exportImg"></param>
        private void SetExportImageTg(GameObject[] selects, bool exportImg)
        {
            // 只改属于本编辑树的选择，避免多选里混进别的文档时串改。
            var selectLayerNodes = selects
                .Where(item => item != null && item.transform != null && item.transform.IsChildOf(transform))
                .Select(item => item.GetComponent<PsdLayerNode>())
                .Where(node => node != null)
                .ToArray();
            Undo.SetCurrentGroupName("批量设置导出图片");
            int undoGroup = Undo.GetCurrentGroup();
            foreach (var node in selectLayerNodes)
            {
                // 逐个 Record+SetDirty：只记被点那个节点的话，其余改动撤销不掉、关掉 prefab 就丢。
                Undo.RecordObject(node, "批量设置导出图片");
                node.markToExport = exportImg;
                EditorUtility.SetDirty(node);
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshAiBindingsAfterUndo;
            PrefabStage.prefabSaving -= SaveAuthoringDocument;
            SceneView.duringSceneGui -= OnSceneGUI;
#if UNITY_6000_3_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
#endif
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Undo.undoRedoPerformed -= RefreshAiBindingsAfterUndo;
            PrefabStage.prefabSaving -= SaveAuthoringDocument;
            SceneView.duringSceneGui -= OnSceneGUI;
#if UNITY_6000_3_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
#endif
            if (this.psdInstance != null)
            {
                psdInstance.Dispose();
                psdInstance = null;
            }
        }

        private void RefreshAiBindingsAfterUndo()
        {
            if (psdInstance == null) return;
            foreach (var node in GetComponentsInChildren<PsdLayerNode>(true))
                if (node.IsAiTarget) node.InitPsdLayers(psdInstance);
            if (uiDocument != null) SaveUiDocument();
        }

        private void RefreshNodesBindLayer()
        {
            if (psdInstance == null)
            {
                if (!File.Exists(PsdAssetName))
                {
                    Debug.LogError($"刷新节点绑定图层失败! 源文档不存在:{PsdAssetName}");
                    return;
                }
                try
                {
                    EditorUtility.DisplayProgressBar("文件加载中", string.Format("正在读取源文档:{0}\n文件过大会影响读取速度,建议通过栅格化图层减小文档大小", PsdAssetName), 0.5f);
                    psdInstance = PsdDocument.Create(PsdAssetName);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return;
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            var layers = GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var layer in layers)
            {
                layer.InitPsdLayers(psdInstance);
            }

            RefreshDocumentPreviewSprite(psdInstance);
            ApplyDisplaySprite();
        }

        [MenuItem("Assets/Psd2UIForm Editor", priority = 0)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        static void Psd2UIFormPrefabMenu()
        {
            if (Selection.activeObject == null) return;
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!IsSupportedSourceDocument(assetPath))
            {
                Debug.LogWarning($"选择的文件({assetPath})不是PSD/PSB格式, 工具只支持PSD/PSB转换为UIForm");
                return;
            }
            string psdLayerPrefab = GetPsdLayerPrefabPath(assetPath);
            if (!File.Exists(psdLayerPrefab))
            {
                if (ParsePsd2LayerPrefab(assetPath))
                {
                    OpenPsdLayerEditor(psdLayerPrefab);
                }
            }
            else
            {
                OpenPsdLayerEditor(psdLayerPrefab);
            }
        }
        [MenuItem("Assets/Psd2UIForm Editor", true)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        static bool ValidatePsd2UIFormPrefabMenu()
        {
            if (Selection.activeObject != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (IsSupportedSourceDocument(assetPath))
                {
                    return true;
                }
            }

            return false;
        }
        private static string GetAssetChangeTag(string fileName)
        {
            return new FileInfo(fileName).LastWriteTimeUtc.ToString("yyyyMMddHHmmss");
        }
        /// <summary>
        /// 打开psd图层信息prefab
        /// </summary>
        /// <param name="psdLayerPrefab"></param>
        internal static void OpenPsdLayerEditor(string psdLayerPrefab)
        {
            OpenPrefab(psdLayerPrefab);
        }
        private static void OpenPrefab(string prefabName)
        {
#if UNITY_2021_2_OR_NEWER
            var guid = AssetDatabase.GUIDFromAssetPath(prefabName);
            var canonicalPath = guid.Empty() ? prefabName : AssetDatabase.GUIDToAssetPath(guid);
            PrefabStageUtility.OpenPrefab(canonicalPath);
#else
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabName);
            AssetDatabase.OpenAsset(prefab);
#endif
        }
        /// <summary>
        /// 把Psd图层解析成节点prefab
        /// </summary>
        /// <param name="psdPath"></param>
        /// <returns></returns>
        internal static bool ParsePsd2LayerPrefab(string psdFile, Psd2UIFormConverter instanceRoot = null, bool openPrefabAfterSave = true)
        {
            psdFile = NormalizeAssetPath(psdFile);
            if (string.IsNullOrWhiteSpace(psdFile) || !File.Exists(psdFile))
            {
                Debug.LogError($"Error: 源文档不存在:{psdFile}");
                return false;
            }
            var texImporter = AssetImporter.GetAtPath(psdFile) as TextureImporter;
            if (texImporter != null)
            {
                if (texImporter.textureType != TextureImporterType.Sprite || texImporter.spriteImportMode != SpriteImportMode.Single)
                {
                    texImporter.textureType = TextureImporterType.Sprite;
                    texImporter.spriteImportMode = SpriteImportMode.Single;
                    texImporter.mipmapEnabled = false;
                    texImporter.alphaIsTransparency = true;
                    texImporter.SaveAndReimport();
                }
                else
                {
                    AssetDatabase.ImportAsset(psdFile, ImportAssetOptions.ForceSynchronousImport);
                }
            }

            var prefabFile = GetPsdLayerPrefabPath(psdFile);
            var rootName = Path.GetFileNameWithoutExtension(prefabFile);

            bool needDestroyInstance = instanceRoot == null;
            if (instanceRoot != null)
            {
                return ParsePsdLayer2Root(psdFile, instanceRoot);
            }
            else
            {
                Psd2UIFormConverter rootLayer = CreatePsdLayerRoot(rootName);
                if (rootLayer == null)
                {
                    Debug.LogError($"解析PSD失败: 无法创建根节点组件 {nameof(Psd2UIFormConverter)}");
                    return false;
                }

                rootLayer.psdAssetChangeTime = GetAssetChangeTag(psdFile);
                rootLayer.SetPsdAsset(psdFile);
                if (!ParsePsdLayer2Root(psdFile, rootLayer))
                {
                    if (needDestroyInstance) GameObject.DestroyImmediate(rootLayer.gameObject);
                    return false;
                }

                rootLayer.gameObject.name = System.IO.Path.GetFileNameWithoutExtension(prefabFile);
                PrefabUtility.SaveAsPrefabAsset(rootLayer.gameObject, prefabFile, out bool savePrefabSuccess);
                if (!savePrefabSuccess) Debug.LogError($"保存图层编辑 prefab 失败: {prefabFile}");
                if (needDestroyInstance) GameObject.DestroyImmediate(rootLayer.gameObject);
                AssetDatabase.Refresh();
                var currentStagePath = StageUtility.GetCurrentStage()?.assetPath;
                bool isSameStagePrefab = !string.IsNullOrWhiteSpace(currentStagePath)
                    && AssetDatabase.AssetPathToGUID(currentStagePath) == AssetDatabase.AssetPathToGUID(prefabFile);
                if (savePrefabSuccess && openPrefabAfterSave && !isSameStagePrefab)
                {
                    OpenPrefab(prefabFile);
                }

                return savePrefabSuccess;
            }
        }
        private static bool ParsePsdLayer2Root(string psdFile, Psd2UIFormConverter converter)
        {
            EditorUtility.DisplayProgressBar("解析PSD", $"正在解析{psdFile}", 0);

            try
            {
                using (var psd = PsdDocument.Create(psdFile))
                {
                    var rootLayers = psd.Childs ?? Array.Empty<PsdLayer>();
                    if (rootLayers.Length == 0)
                    {
                        Debug.LogError($"解析PSD失败: PSD未包含可解析的图层树。文件: {psdFile}");
                        return false;
                    }

                    int totalCount = psd.CountAllLayers();
                    int parsedCount = 0;
                    int nextBindIndex = 0;
                    // 先在暂存容器里解析：中途抛异常时原来那份 _UIFormEditor 节点树不会被清空覆盖掉。
                    // 暂存容器带上与编辑根同尺寸的 RectTransform，解析期算 rect/轴心才是同一套数。
                    var staging = new GameObject("PSD2UIForm_ParseStaging", typeof(RectTransform));
                    staging.hideFlags = HideFlags.HideAndDontSave;
                    if (converter.transform is RectTransform converterRect)
                    {
                        var stagingRect = (RectTransform)staging.transform;
                        stagingRect.anchorMin = Vector2.zero;
                        stagingRect.anchorMax = Vector2.zero;
                        stagingRect.pivot = converterRect.pivot;
                        stagingRect.sizeDelta = converterRect.rect.size;
                        stagingRect.anchoredPosition = Vector2.zero;
                        stagingRect.localScale = converterRect.localScale;
                    }
                    try
                    {
                        for (int i = 0; i < rootLayers.Length; i++)
                        {
                            var rootLayer = rootLayers[i] as PsdLayer;
                            if (rootLayer == null) continue;
                            ParsePsdLayerRecursive(rootLayer, staging.transform, ref nextBindIndex, ref parsedCount, totalCount);
                        }

                        if (parsedCount <= 0 || staging.transform.childCount == 0)
                        {
                            throw new InvalidOperationException($"PSD 未产出任何节点，保留原编辑树: {psdFile}");
                        }

                        converter.SynchronizeSourceNodes(staging.transform);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(staging);
                    }

                    converter.RefreshDocumentPreviewSprite(psd, psdFile);
                }

                converter.psdAssetChangeTime = GetAssetChangeTag(psdFile);
                if (converter.psdInstance != null)
                {
                    converter.psdInstance.Dispose();
                    converter.psdInstance = null;
                }
                converter.RefreshNodesBindLayer();
                converter.ApplyGroupDerivedStates();
                var childrenNodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                foreach (var item in childrenNodes)
                {
                    item.RefreshUIHelper(false);
                }
                EditorUtility.SetDirty(converter.gameObject);
                converter.SaveUiDocument();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        private void SetPsdAsset(string psdFile)
        {
            psdAssetPath = NormalizeAssetPath(psdFile);
            this.psdAsset = LoadPsdSpriteAsset(psdAssetPath);
            this.previewSprite = this.psdAsset != null || !IsPsbSourceDocument(psdAssetPath)
                ? null
                : LoadDocumentPreviewSpriteAsset(psdAssetPath);
            if (string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.UIImagesOutputDir))
            {
                Psd2UIFormSettings.Instance.UIImagesOutputDir = Path.GetDirectoryName(psdAssetPath);
            }
            if (string.IsNullOrWhiteSpace(this.uiFormName))
            {
                this.uiFormName = this.psdAsset != null ? this.psdAsset.name : Path.GetFileNameWithoutExtension(psdAssetPath);
            }
        }

        /// <summary>
        /// 获取解析好的psd layers文件
        /// </summary>
        /// <param name="psd"></param>
        /// <returns></returns>
        private static string GetPsdLayerPrefabPath(string psd)
        {
            return Path.Combine(Path.GetDirectoryName(psd), Path.GetFileNameWithoutExtension(psd) + "_UIFormEditor.prefab");
        }
        private static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;

            var normalized = assetPath.Replace("\\", "/").Trim();
            if (Path.IsPathRooted(normalized))
            {
                normalized = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, normalized).Replace("\\", "/");
            }
            return normalized;
        }
        private static bool IsSupportedSourceDocument(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return false;

            var extension = Path.GetExtension(assetPath);
            return extension.Equals(".psd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".psb", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsPsbSourceDocument(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return false;

            return Path.GetExtension(assetPath).Equals(".psb", StringComparison.OrdinalIgnoreCase);
        }
        private static UnityEngine.Sprite LoadPsdSpriteAsset(string psdFile)
        {
            if (string.IsNullOrWhiteSpace(psdFile)) return null;

            var sprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(psdFile);
            if (sprite != null) return sprite;

            var assets = AssetDatabase.LoadAllAssetsAtPath(psdFile);
            if (assets == null) return null;
            foreach (var asset in assets)
            {
                if (asset is UnityEngine.Sprite subSprite) return subSprite;
            }

            return null;
        }
        private static string GetDocumentPreviewAssetPath(string sourceAssetPath)
        {
            if (string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                return null;
            }

            string dir = Path.GetDirectoryName(sourceAssetPath);
            string fileName = Path.GetFileNameWithoutExtension(sourceAssetPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            return Path.Combine(dir, $"{fileName}_Preview.png").Replace("\\", "/");
        }
        private static string BuildPreviewSpriteTag(string sourceAssetPath, string sourceChangeTag)
        {
            return $"{PreviewSpriteTagKey}:{PreviewSpriteTagVersion}:{sourceChangeTag}:{sourceAssetPath}";
        }
        private static string ResolveAbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string normalized = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, normalized.Replace("/", "\\"));
        }
        private static UnityEngine.Sprite LoadDocumentPreviewSpriteAsset(string sourceAssetPath)
        {
            return PsdLayerNode.LoadSpriteAssetAtPath(GetDocumentPreviewAssetPath(sourceAssetPath));
        }
        private static bool PreviewSpriteMatchesSource(string previewAssetPath, string sourceAssetPath, string sourceChangeTag)
        {
            var importer = AssetImporter.GetAtPath(previewAssetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            return string.Equals(importer.userData, BuildPreviewSpriteTag(sourceAssetPath, sourceChangeTag), StringComparison.Ordinal);
        }
        private sealed class DocumentPreviewRenderItem
        {
            internal PsdLayer Layer;
            internal PsdRenderedImage Image;
        }

        private static byte[] BuildDocumentPreviewPng(PsdDocument document)
        {
            return BuildAnalysisPreviewPng(document, out _, out _, out _, out _);
        }

        internal static byte[] BuildAnalysisPreviewPng(string psdAssetPath, out int canvasLeft, out int canvasTop, out int canvasWidth, out int canvasHeight)
        {
            canvasLeft = 0;
            canvasTop = 0;
            canvasWidth = 0;
            canvasHeight = 0;

            psdAssetPath = NormalizeAssetPath(psdAssetPath);
            if (string.IsNullOrWhiteSpace(psdAssetPath))
            {
                return null;
            }

            using (var document = PsdDocument.Create(psdAssetPath))
            {
                return BuildAnalysisPreviewPng(document, out canvasLeft, out canvasTop, out canvasWidth, out canvasHeight);
            }
        }

        /// <summary>
        /// 用正式 Render (非 RenderPreview) 合成 PSD 完整预览 PNG，并返回输出画布相对
        /// PSD 原点 (0,0) 的偏移 + 实际画布尺寸。AI 分析包用这些偏移把 PsdLayer 的原生
        /// 像素坐标精确映射到预览图像素 (top-left origin, Y-down)。
        /// </summary>
        /// <param name="document">已经解析的 PsdDocument。</param>
        /// <param name="canvasLeft">输出 PNG 中像素 (0,0) 对应的 PSD x 坐标。可能为负 (当图层越出画布时)。</param>
        /// <param name="canvasTop">输出 PNG 中像素 (0,0) 对应的 PSD y 坐标。可能为负。</param>
        /// <param name="canvasWidth">PNG 像素宽。</param>
        /// <param name="canvasHeight">PNG 像素高。</param>
        internal static byte[] BuildAnalysisPreviewPng(PsdDocument document, out int canvasLeft, out int canvasTop, out int canvasWidth, out int canvasHeight)
        {
            canvasLeft = 0;
            canvasTop = 0;
            canvasWidth = 0;
            canvasHeight = 0;
            if (document == null)
            {
                return null;
            }

            var renderedItems = CollectDocumentPreviewRenderItems(document);
            ResolveDocumentPreviewCanvasBounds(document, renderedItems, out canvasLeft, out canvasTop, out canvasWidth, out canvasHeight);

            bool preferHighBitDepth = false;
            for (int i = 0; i < renderedItems.Count; i++)
            {
                var image = renderedItems[i] != null ? renderedItems[i].Image : null;
                if (image != null && image.IsHighBitDepth)
                {
                    preferHighBitDepth = true;
                    break;
                }
            }
            Texture2D composedTexture = null;
            try
            {
                composedTexture = ComposeDocumentPreviewTexture(renderedItems, canvasLeft, canvasTop, canvasWidth, canvasHeight, preferHighBitDepth);
                return PsdTextureAssetUtility.EncodePng(composedTexture);
            }
            finally
            {
                if (composedTexture != null)
                {
                    DestroyImmediate(composedTexture);
                }
            }
        }

        private static List<DocumentPreviewRenderItem> CollectDocumentPreviewRenderItems(PsdDocument document)
        {
            var renderedItems = new List<DocumentPreviewRenderItem>();
            var layers = document.Childs;
            if (layers == null || layers.Length == 0)
            {
                return renderedItems;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i] as PsdLayer;
                if (layer == null || !layer.IsVisible)
                {
                    continue;
                }

                var rendered = layer.Render();
                if (rendered == null || rendered.IsEmpty)
                {
                    continue;
                }

                renderedItems.Add(new DocumentPreviewRenderItem
                {
                    Layer = layer,
                    Image = rendered
                });
            }

            return renderedItems;
        }

        private static void ResolveDocumentPreviewCanvasBounds(PsdDocument document, List<DocumentPreviewRenderItem> renderedItems, out int left, out int top, out int width, out int height)
        {
            int minLeft = 0;
            int minTop = 0;
            int maxRight = document != null ? document.Width : 0;
            int maxBottom = document != null ? document.Height : 0;

            if (renderedItems != null)
            {
                for (int i = 0; i < renderedItems.Count; i++)
                {
                    var image = renderedItems[i]?.Image;
                    if (image == null || image.IsEmpty)
                    {
                        continue;
                    }

                    if (image.Left < minLeft)
                    {
                        minLeft = image.Left;
                    }

                    if (image.Top < minTop)
                    {
                        minTop = image.Top;
                    }

                    if (image.Right > maxRight)
                    {
                        maxRight = image.Right;
                    }

                    if (image.Bottom > maxBottom)
                    {
                        maxBottom = image.Bottom;
                    }
                }
            }

            int baseWidth = Math.Max(0, document != null ? document.Width : 0);
            int baseHeight = Math.Max(0, document != null ? document.Height : 0);
            int padX = Math.Max(Math.Max(0, -minLeft), Math.Max(0, maxRight - baseWidth));
            int padY = Math.Max(Math.Max(0, -minTop), Math.Max(0, maxBottom - baseHeight));
            left = -padX;
            top = -padY;
            width = baseWidth + (padX << 1);
            height = baseHeight + (padY << 1);
        }

        private static Texture2D ComposeDocumentPreviewTexture(List<DocumentPreviewRenderItem> renderedItems, int canvasLeft, int canvasTop, int canvasWidth, int canvasHeight, bool preferHighBitDepth)
        {
            canvasWidth = Math.Max(1, canvasWidth);
            canvasHeight = Math.Max(1, canvasHeight);

            if (preferHighBitDepth)
            {
                ushort[] rgba64 = new ushort[canvasWidth * canvasHeight * 4];
                ushort[] currentClippingMask = null;
                if (renderedItems != null)
                {
                    for (int i = 0; i < renderedItems.Count; i++)
                    {
                        var item = renderedItems[i];
                        var image = item?.Image;
                        if (image == null || image.IsEmpty)
                        {
                            continue;
                        }

                        bool isClippingLayer = item.Layer != null && item.Layer.IsClipping;
                        CompositePreviewImage16(rgba64, canvasLeft, canvasTop, canvasWidth, canvasHeight, image, isClippingLayer ? currentClippingMask : null);
                        if (!isClippingLayer)
                        {
                            currentClippingMask = BuildPreviewAlphaMask16(image, canvasLeft, canvasTop, canvasWidth, canvasHeight);
                        }
                    }
                }

                byte[] rawData = new byte[rgba64.Length * sizeof(ushort)];
                Buffer.BlockCopy(rgba64, 0, rawData, 0, rawData.Length);
                var texture = new Texture2D(canvasWidth, canvasHeight, GraphicsFormat.R16G16B16A16_UNorm, TextureCreationFlags.None);
                texture.LoadRawTextureData(rawData);
                texture.Apply(false, false);
                return texture;
            }

            byte[] rgba32 = new byte[canvasWidth * canvasHeight * 4];
            byte[] clippingMask8 = null;
            if (renderedItems != null)
            {
                for (int i = 0; i < renderedItems.Count; i++)
                {
                    var item = renderedItems[i];
                    var image = item?.Image;
                    if (image == null || image.IsEmpty)
                    {
                        continue;
                    }

                    bool isClippingLayer = item.Layer != null && item.Layer.IsClipping;
                    CompositePreviewImage8(rgba32, canvasLeft, canvasTop, canvasWidth, canvasHeight, image, isClippingLayer ? clippingMask8 : null);
                    if (!isClippingLayer)
                    {
                        clippingMask8 = BuildPreviewAlphaMask8(image, canvasLeft, canvasTop, canvasWidth, canvasHeight);
                    }
                }
            }

            var texture32 = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false, false);
            texture32.LoadRawTextureData(rgba32);
            texture32.Apply(false, false);
            return texture32;
        }

        private static void CompositePreviewImage8(byte[] destination, int destinationLeft, int destinationTop, int destinationWidth, int destinationHeight, PsdRenderedImage source, byte[] clippingMask)
        {
            if (destination == null || source == null || source.IsEmpty)
            {
                return;
            }

            byte[] sourcePixels = source.Rgba32;
            int offsetX = source.Left - destinationLeft;
            int offsetY = source.Top - destinationTop;
            for (int y = 0; y < source.Height; y++)
            {
                int dstY = offsetY + y;
                if (dstY < 0 || dstY >= destinationHeight)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int dstX = offsetX + x;
                    if (dstX < 0 || dstX >= destinationWidth)
                    {
                        continue;
                    }

                    int srcOffset = GetPreviewPixelOffset(source.Width, source.Height, x, y);
                    byte srcA = sourcePixels[srcOffset + 3];
                    if (srcA <= 0)
                    {
                        continue;
                    }

                    if (clippingMask != null)
                    {
                        srcA = PsdLayerRenderer.MultiplyAlpha(srcA, clippingMask[(dstY * destinationWidth) + dstX]);
                        if (srcA <= 0)
                        {
                            continue;
                        }
                    }

                    CompositePreviewPixel8(
                        destination,
                        destinationWidth,
                        destinationHeight,
                        dstX,
                        dstY,
                        sourcePixels[srcOffset],
                        sourcePixels[srcOffset + 1],
                        sourcePixels[srcOffset + 2],
                        srcA);
                }
            }
        }

        private static void CompositePreviewImage16(ushort[] destination, int destinationLeft, int destinationTop, int destinationWidth, int destinationHeight, PsdRenderedImage source, ushort[] clippingMask)
        {
            if (destination == null || source == null || source.IsEmpty)
            {
                return;
            }

            ushort[] sourcePixels = source.Rgba64;
            int offsetX = source.Left - destinationLeft;
            int offsetY = source.Top - destinationTop;
            for (int y = 0; y < source.Height; y++)
            {
                int dstY = offsetY + y;
                if (dstY < 0 || dstY >= destinationHeight)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int dstX = offsetX + x;
                    if (dstX < 0 || dstX >= destinationWidth)
                    {
                        continue;
                    }

                    int srcOffset = GetPreviewPixelOffset(source.Width, source.Height, x, y);
                    ushort srcA = sourcePixels[srcOffset + 3];
                    if (srcA <= 0)
                    {
                        continue;
                    }

                    if (clippingMask != null)
                    {
                        srcA = PsdLayerRenderer.MultiplyAlpha(srcA, clippingMask[(dstY * destinationWidth) + dstX]);
                        if (srcA <= 0)
                        {
                            continue;
                        }
                    }

                    CompositePreviewPixel16(
                        destination,
                        destinationWidth,
                        destinationHeight,
                        dstX,
                        dstY,
                        sourcePixels[srcOffset],
                        sourcePixels[srcOffset + 1],
                        sourcePixels[srcOffset + 2],
                        srcA);
                }
            }
        }

        private static byte[] BuildPreviewAlphaMask8(PsdRenderedImage source, int destinationLeft, int destinationTop, int destinationWidth, int destinationHeight)
        {
            var alphaMask = new byte[destinationWidth * destinationHeight];
            if (source == null || source.IsEmpty)
            {
                return alphaMask;
            }

            byte[] sourcePixels = source.Rgba32;
            int offsetX = source.Left - destinationLeft;
            int offsetY = source.Top - destinationTop;
            for (int y = 0; y < source.Height; y++)
            {
                int dstY = offsetY + y;
                if (dstY < 0 || dstY >= destinationHeight)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int dstX = offsetX + x;
                    if (dstX < 0 || dstX >= destinationWidth)
                    {
                        continue;
                    }

                    int srcOffset = GetPreviewPixelOffset(source.Width, source.Height, x, y);
                    alphaMask[(dstY * destinationWidth) + dstX] = sourcePixels[srcOffset + 3];
                }
            }

            return alphaMask;
        }

        private static ushort[] BuildPreviewAlphaMask16(PsdRenderedImage source, int destinationLeft, int destinationTop, int destinationWidth, int destinationHeight)
        {
            var alphaMask = new ushort[destinationWidth * destinationHeight];
            if (source == null || source.IsEmpty)
            {
                return alphaMask;
            }

            ushort[] sourcePixels = source.Rgba64;
            int offsetX = source.Left - destinationLeft;
            int offsetY = source.Top - destinationTop;
            for (int y = 0; y < source.Height; y++)
            {
                int dstY = offsetY + y;
                if (dstY < 0 || dstY >= destinationHeight)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int dstX = offsetX + x;
                    if (dstX < 0 || dstX >= destinationWidth)
                    {
                        continue;
                    }

                    int srcOffset = GetPreviewPixelOffset(source.Width, source.Height, x, y);
                    alphaMask[(dstY * destinationWidth) + dstX] = sourcePixels[srcOffset + 3];
                }
            }

            return alphaMask;
        }

        private static void CompositePreviewPixel8(byte[] destination, int width, int height, int x, int yTopDown, byte srcR, byte srcG, byte srcB, byte srcA)
        {
            int offset = GetPreviewPixelOffset(width, height, x, yTopDown);
            byte dstR = destination[offset];
            byte dstG = destination[offset + 1];
            byte dstB = destination[offset + 2];
            byte dstA = destination[offset + 3];

            float srcAlpha = srcA / 255f;
            float dstAlpha = dstA / 255f;
            float outAlpha = srcAlpha + (dstAlpha * (1f - srcAlpha));
            if (outAlpha <= 0f)
            {
                destination[offset] = 0;
                destination[offset + 1] = 0;
                destination[offset + 2] = 0;
                destination[offset + 3] = 0;
                return;
            }

            float outR = ((srcR / 255f) * srcAlpha) + ((dstR / 255f) * dstAlpha * (1f - srcAlpha));
            float outG = ((srcG / 255f) * srcAlpha) + ((dstG / 255f) * dstAlpha * (1f - srcAlpha));
            float outB = ((srcB / 255f) * srcAlpha) + ((dstB / 255f) * dstAlpha * (1f - srcAlpha));
            destination[offset] = ClampPreviewByte(outR / outAlpha);
            destination[offset + 1] = ClampPreviewByte(outG / outAlpha);
            destination[offset + 2] = ClampPreviewByte(outB / outAlpha);
            destination[offset + 3] = ClampPreviewByte(outAlpha);
        }

        private static void CompositePreviewPixel16(ushort[] destination, int width, int height, int x, int yTopDown, ushort srcR, ushort srcG, ushort srcB, ushort srcA)
        {
            int offset = GetPreviewPixelOffset(width, height, x, yTopDown);
            ushort dstR = destination[offset];
            ushort dstG = destination[offset + 1];
            ushort dstB = destination[offset + 2];
            ushort dstA = destination[offset + 3];

            float srcAlpha = srcA / 65535f;
            float dstAlpha = dstA / 65535f;
            float outAlpha = srcAlpha + (dstAlpha * (1f - srcAlpha));
            if (outAlpha <= 0f)
            {
                destination[offset] = 0;
                destination[offset + 1] = 0;
                destination[offset + 2] = 0;
                destination[offset + 3] = 0;
                return;
            }

            float outR = ((srcR / 65535f) * srcAlpha) + ((dstR / 65535f) * dstAlpha * (1f - srcAlpha));
            float outG = ((srcG / 65535f) * srcAlpha) + ((dstG / 65535f) * dstAlpha * (1f - srcAlpha));
            float outB = ((srcB / 65535f) * srcAlpha) + ((dstB / 65535f) * dstAlpha * (1f - srcAlpha));
            destination[offset] = ClampPreviewUInt16(outR / outAlpha);
            destination[offset + 1] = ClampPreviewUInt16(outG / outAlpha);
            destination[offset + 2] = ClampPreviewUInt16(outB / outAlpha);
            destination[offset + 3] = ClampPreviewUInt16(outAlpha);
        }

        private static int GetPreviewPixelOffset(int width, int height, int x, int yTopDown)
        {
            return (((height - 1 - yTopDown) * width) + x) * 4;
        }

        private static byte ClampPreviewByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }

        private static ushort ClampPreviewUInt16(float value)
        {
            return (ushort)Mathf.Clamp(Mathf.RoundToInt(value * 65535f), 0, 65535);
        }
        private static UnityEngine.Sprite EnsureDocumentPreviewSpriteAsset(string sourceAssetPath, PsdDocument document = null)
        {
            sourceAssetPath = NormalizeAssetPath(sourceAssetPath);
            string absoluteSourcePath = ResolveAbsoluteAssetPath(sourceAssetPath);
            if (string.IsNullOrWhiteSpace(sourceAssetPath) || string.IsNullOrWhiteSpace(absoluteSourcePath) || !File.Exists(absoluteSourcePath))
            {
                return null;
            }

            string sourceChangeTag = GetAssetChangeTag(sourceAssetPath);
            string previewAssetPath = GetDocumentPreviewAssetPath(sourceAssetPath);
            if (string.IsNullOrWhiteSpace(previewAssetPath))
            {
                return null;
            }

            string absolutePreviewPath = ResolveAbsoluteAssetPath(previewAssetPath);
            if (!string.IsNullOrWhiteSpace(absolutePreviewPath)
                && File.Exists(absolutePreviewPath)
                && PreviewSpriteMatchesSource(previewAssetPath, sourceAssetPath, sourceChangeTag))
            {
                return PsdLayerNode.LoadSpriteAssetAtPath(previewAssetPath);
            }

            bool ownsDocument = document == null;
            try
            {
                if (document == null)
                {
                    document = PsdDocument.Create(sourceAssetPath);
                }

                byte[] pngBytes = BuildDocumentPreviewPng(document);
                if (pngBytes == null || pngBytes.Length == 0)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(absolutePreviewPath))
                {
                    return null;
                }

                File.WriteAllBytes(absolutePreviewPath, pngBytes);
                AssetDatabase.ImportAsset(previewAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(previewAssetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.userData = BuildPreviewSpriteTag(sourceAssetPath, sourceChangeTag);
                    importer.SaveAndReimport();
                }

                return PsdLayerNode.LoadSpriteAssetAtPath(previewAssetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"生成源文档预览图失败: {sourceAssetPath}\n{ex}");
                return null;
            }
            finally
            {
                if (ownsDocument && document != null)
                {
                    document.Dispose();
                }
            }
        }
        private void RefreshDocumentPreviewSprite(PsdDocument document, string sourceAssetPath = null)
        {
            sourceAssetPath = string.IsNullOrWhiteSpace(sourceAssetPath) ? PsdAssetName : NormalizeAssetPath(sourceAssetPath);
            var previousPreview = previewSprite;

            if (psdAsset != null || !IsPsbSourceDocument(sourceAssetPath))
            {
                previewSprite = null;
            }
            else
            {
                previewSprite = EnsureDocumentPreviewSpriteAsset(sourceAssetPath, document);
            }

            if (previousPreview != previewSprite)
            {
                EditorUtility.SetDirty(this);
            }
        }
        private void ApplyDisplaySprite()
        {
            var spRender = gameObject.GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();
            spRender.sprite = BindPsdAsset;
        }

        private static Psd2UIFormConverter CreatePsdLayerRoot(string rootName)
        {
            var node = new GameObject(rootName, typeof(RectTransform));
            node.gameObject.tag = "EditorOnly";
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;
            var layerRoot = node.AddComponent<Psd2UIFormConverter>();
            if (layerRoot == null)
            {
                GameObject.DestroyImmediate(node);
            }
            return layerRoot;
        }
        private static void ParsePsdLayerRecursive(PsdLayer layer, Transform parentNode, ref int nextBindIndex, ref int parsedCount, int totalCount)
        {
            if (layer == null || parentNode == null) return;

            parsedCount++;
            EditorUtility.DisplayProgressBar($"解析PSD({parsedCount}/{Mathf.Max(1, totalCount)})", $"正在解析图层:{layer.GetDisplayName()}", totalCount > 0 ? parsedCount / (float)totalCount : 1f);

            int bindLayerIdx = layer.IsGroup ? nextBindIndex + layer.CountFlatIndexSlots() - 1 : nextBindIndex;
            var newLayerNode = CreatePsdLayerNode(layer, bindLayerIdx);
            newLayerNode.transform.SetParent(parentNode);
            newLayerNode.transform.localPosition = Vector3.zero;

            if (layer.Childs != null && layer.Childs.Length > 0)
            {
                int childBindIndex = nextBindIndex + 1;
                for (int i = 0; i < layer.Childs.Length; i++)
                {
                    var childLayer = layer.Childs[i];
                    if (childLayer == null) continue;
                    ParsePsdLayerRecursive(childLayer, newLayerNode.transform, ref childBindIndex, ref parsedCount, totalCount);
                }
            }

            nextBindIndex += layer.CountFlatIndexSlots();
        }
        private static PsdLayerNode CreatePsdLayerNode(PsdLayer layer, int bindLayerIdx)
        {
            string nodeName = layer.GetDisplayName();
            var parser = UGUIParser.Instance;
            nodeName = parser != null ? parser.GetSafeLayerName(nodeName, bindLayerIdx) : nodeName;
            var node = new GameObject(nodeName, typeof(RectTransform));
            node.gameObject.tag = "EditorOnly";
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;
            var layerNode = node.AddComponent<PsdLayerNode>();
            layerNode.BindPsdLayerIndex = bindLayerIdx;
            if (layer.TryGetLayerId(out int sourceLayerId) && sourceLayerId > 0) layerNode.bindPsdLayerId = sourceLayerId;
            InitLayerNodeData(layerNode, layer);
            layerNode.CaptureSourcePresentation();
            return layerNode;
        }

        /// <summary>
        /// 根据psd图层信息解析并初始化图层UI类型、是否导出等信息
        /// </summary>
        /// <param name="layerNode"></param>
        /// <param name="layer"></param>
        private static void InitLayerNodeData(PsdLayerNode layerNode, PsdLayer layer)
        {
            if (layer == null) return;
            var layerTp = layer.GetLayerType();
            layerNode.SetSourceLayerName(layer.GetDisplayName());
            layerNode.BindPsdLayer = layer;
            layerNode.SetGroupGenerationState(false, -1);
            var parser = UGUIParser.Instance;
            if (parser != null && parser.TryParse(layerNode, out var initRule))
            {
                layerNode.SetUIType(initRule.UIType, false);
            }
            bool isTextBackedUI = layerTp == PsdLayerType.TextLayer && layerNode.UIType.ToString().EndsWith("Text") && layerNode.UIType != GUIType.FillColor;
            bool isFillColorUI = layerTp == PsdLayerType.FillLayer || layerNode.UIType == GUIType.FillColor;
            layerNode.markToExport = layerTp != PsdLayerType.LayerGroup && !isTextBackedUI && !isFillColorUI;
            layerNode.gameObject.SetActive(layer.IsVisible);
            if (layerNode.HasReuseReference || layerNode.HasReusePrefabReference)
            {
                layerNode.markToExport = false;
            }
        }

        internal void ApplyGroupDerivedStates(bool logOpacityConflicts = false)
        {
            var nodes = GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var node in nodes) if (node.IsAiTarget) return;
            if (nodes == null || nodes.Length == 0) return;

            foreach (var node in nodes)
            {
                if (node == null || node.IsAiAuthoritative) continue;

                node.SetGroupOpacityRasterization(false);
                node.SetGroupGenerationState(false, -1);
                if (node.LayerType != PsdLayerType.LayerGroup)
                {
                    continue;
                }

                if (IsGroupTextSourceType(node.UIType) || IsGroupTextSourceSemanticType(node.UIType))
                {
                    var textSource = FindGroupTextSource(node);
                    if (textSource != null)
                    {
                        node.SetGroupGenerationState(true, textSource.BindPsdLayerIndex, textSource.bindPsdLayerId);
                        node.markToExport = false;
                    }
                    else
                    {
                        node.SetUIType(GUIType.Panel, false);
                        node.markToExport = false;
                    }
                    EditorUtility.SetDirty(node);
                    continue;
                }

                if (IsGroupImageSourceType(node.UIType) || IsGroupImageSourceSemanticType(node.UIType))
                {
                    node.SetGroupGenerationState(true, -1);
                    node.markToExport = !node.HasReuseReference && !node.HasReusePrefabReference;
                    EditorUtility.SetDirty(node);
                    continue;
                }

                node.markToExport = false;
                EditorUtility.SetDirty(node);
            }

            ResolveGroupOpacityGenerationStates(nodes, logOpacityConflicts);
        }

        private static void ResolveGroupOpacityGenerationStates(PsdLayerNode[] nodes, bool logConflicts)
        {
            Array.Sort(nodes, CompareGroupOpacityResolutionOrder);
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null
                    || node.IsAiAuthoritative
                    || node.LayerType != PsdLayerType.LayerGroup
                    || node.BindPsdLayer == null
                    || node.BakesGroupOpacityIntoGeneratedImage)
                {
                    continue;
                }

                float opacity = node.SourceOpacity;
                if (opacity >= GroupOpacityOpaqueThreshold || opacity <= GroupOpacityTransparentThreshold)
                {
                    continue;
                }

                if (!node.HasOverlappingGeneratedGraphics())
                {
                    continue;
                }

                if (CanRasterizeGroupForOpacity(node))
                {
                    node.SetGroupOpacityRasterization(true);
                    node.markToExport = true;
                    EditorUtility.SetDirty(node);
                    continue;
                }

                if (logConflicts)
                {
                    Debug.LogWarning(
                        $"[PSD2UIForm.GroupOpacity] 组 '{node.name}' 的半透明子节点存在像素重叠，" +
                        "但该组包含交互、遮罩或 prefab 引用，无法在保持结构的同时逐像素还原 Photoshop 组透明度。" +
                        "当前保留结构并同步 CanvasGroup.alpha；如需像素完全一致，请将该组改为纯视觉 Panel/Null 组。");
                }
            }
        }

        private static int CompareGroupOpacityResolutionOrder(PsdLayerNode left, PsdLayerNode right)
        {
            int leftDepth = GetTransformDepth(left != null ? left.transform : null);
            int rightDepth = GetTransformDepth(right != null ? right.transform : null);
            return rightDepth.CompareTo(leftDepth);
        }

        private static int GetTransformDepth(Transform transform)
        {
            int depth = 0;
            while (transform != null)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        private static bool CanRasterizeGroupForOpacity(PsdLayerNode groupNode)
        {
            if (groupNode == null
                || (groupNode.UIType != GUIType.Null && groupNode.UIType != GUIType.Panel)
                || groupNode.HasReuseReference
                || groupNode.HasReusePrefabReference)
            {
                return false;
            }

            var ancestor = groupNode.transform.parent;
            while (ancestor != null)
            {
                var ancestorNode = ancestor.GetComponent<PsdLayerNode>();
                if (ancestorNode != null
                    && (ancestorNode.HasReusePrefabReference || IsBehavioralGroupOpacityType(ancestorNode.UIType)))
                {
                    return false;
                }
                ancestor = ancestor.parent;
            }

            var descendants = groupNode.GetComponentsInChildren<PsdLayerNode>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                var descendant = descendants[i];
                if (descendant == null || descendant == groupNode || !descendant.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (descendant.HasReuseReference || descendant.HasReusePrefabReference
                    || (descendant.UIType != GUIType.Null && descendant.UIType != GUIType.Panel))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBehavioralGroupOpacityType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Button:
                case GUIType.Dropdown:
                case GUIType.InputField:
                case GUIType.Toggle:
                case GUIType.Slider:
                case GUIType.ScrollView:
                case GUIType.Mask:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                case GUIType.ToggleGroup:
                    return true;
                default:
                    return false;
            }
        }

        internal void RefreshAllLayerNodeHelpers()
        {
            ApplyGroupDerivedStates();
            var nodes = GetComponentsInChildren<PsdLayerNode>(true);
            if (nodes == null || nodes.Length == 0)
            {
                return;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    continue;
                }
                node.RefreshUIHelper(false);
            }
        }

        private static PsdLayerNode FindGroupTextSource(PsdLayerNode groupNode)
        {
            if (groupNode == null) return null;

            var childNodes = groupNode.GetComponentsInChildren<PsdLayerNode>(true)
                .Where(node => node != null
                    && node != groupNode
                    && node.LayerType == PsdLayerType.TextLayer
                    && node.gameObject.activeSelf)
                .OrderBy(node => node.BindPsdLayerIndex)
                .ToArray();

            if (childNodes.Length == 0) return null;

            var taggedText = childNodes.FirstOrDefault(node => IsGroupTextSourceType(node.UIType) || IsGroupTextSourceSemanticType(node.UIType));
            return taggedText ?? childNodes[0];
        }

        private static bool IsGroupImageSourceType(GUIType uiType)
        {
            return uiType == GUIType.Image
                || uiType == GUIType.RawImage
                || uiType == GUIType.Mask;
        }

        private static bool IsGroupTextSourceType(GUIType uiType)
        {
            return uiType == GUIType.Text || uiType == GUIType.TMPText;
        }

        private static bool IsGroupTextSourceSemanticType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.Button_Text:
                case GUIType.Dropdown_Label:
                case GUIType.InputField_Placeholder:
                case GUIType.InputField_Text:
                case GUIType.Toggle_Label:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGroupImageSourceSemanticType(GUIType uiType)
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
                case GUIType.ScrollView_Viewport:
                case GUIType.ScrollView_HorizontalBarBG:
                case GUIType.ScrollView_HorizontalBar:
                case GUIType.ScrollView_VerticalBarBG:
                case GUIType.ScrollView_VerticalBar:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 导出psd图层为Sprites碎图
        /// </summary>
        /// <param name="psdAssetName"></param>
        internal void ExportSprites()
        {
            ApplyGroupDerivedStates();
            var nodes = GetComponentsInChildren<PsdLayerNode>();
            var collapsed = new List<Transform>();
            foreach (var node in nodes)
                if (node.CollapseChildrenForGeneration || !node.IsAiAuthoritative && (node.HasReuseReference || node.HasReusePrefabReference))
                    collapsed.Add(node.transform);
            nodes = nodes.Where(node => !IsChildOfCollapsedRoot(node.transform, collapsed)).ToArray();
            var exportLayers = nodes.Where(node => node.NeedExportImage()).ToArray();
            var exportDir = GetUIFormImagesOutputDir();
            if (string.IsNullOrWhiteSpace(exportDir))
            {
                Debug.LogError("导出图片中止：图片输出目录无效（原因见上一条错误）。");
                return;
            }
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }
            ExportSharedReferenceSprites(nodes);
            int exportIdx = 0;
            int totalCount = exportLayers.Length;
            using (var imageBatch = new PsdImageExportBatch(this))
            {
                try
                {
                    foreach (var layer in exportLayers)
                    {
                        string assetName = null;
                        try { assetName = layer.ExportImageAsset(); }
                        catch (Exception ex) { RecordGenerationIssue(layer, "image_export_failed", ex.Message); }
                        if (assetName == null)
                        {
                            Debug.LogWarning($"导出图层[name:{layer.name}, layerIdx:{layer.BindPsdLayerIndex}]图片失败!");
                        }
                        ++exportIdx;
                        EditorUtility.DisplayProgressBar($"导出进度({exportIdx}/{totalCount})", $"处理UI图片:{layer.name}", exportIdx / (float)totalCount);
                    }
                    imageBatch.Complete(null);
                }
                finally
                {
                    // 单张图抛异常也不能把模态进度条留在屏幕上，后续按钮看着像整个失效了。
                    EditorUtility.ClearProgressBar();
                }
                AssetDatabase.Refresh();
            }
        }
        /// <summary>
        /// 根据解析后的节点树生成UIForm Prefab
        /// </summary>
        internal void GenerateUIForm(PsdLayerNode exportNode = null)
        {
            if (exportNode != null) { UpdateUIFormNode(exportNode); return; }
            if (Psd2UIFormSettings.Instance.UseUIFormOutputDir && string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.UIFormOutputDir))
            {
                Debug.LogError($"生成UIForm失败! UIForm导出路径为空:{Psd2UIFormSettings.Instance.UIFormOutputDir}");
                return;
            }
            Transform rootNode = this.transform;
            if (exportNode != null)
            {
                rootNode = exportNode.transform;
            }
            if (Psd2UIFormSettings.Instance.UseUIFormOutputDir)
            {
                ExportUIPrefab(rootNode, Psd2UIFormSettings.Instance.UIFormOutputDir);
            }
            else
            {
                string lastSaveDir = string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.LastUIFormOutputDir) ? "Assets" : Psd2UIFormSettings.Instance.LastUIFormOutputDir;
                string selectDir = EditorUtility.SaveFolderPanel("保存目录", lastSaveDir, null);
                if (!string.IsNullOrWhiteSpace(selectDir))
                {
                    if (!selectDir.StartsWith("Assets/"))
                        selectDir = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, selectDir);
                    Psd2UIFormSettings.Instance.LastUIFormOutputDir = selectDir;
                    ExportUIPrefab(rootNode, selectDir);
                }
            }
            ReportStaleGeneratedImages();
        }
        internal void ExportAsPrefab(PsdLayerNode targetLogic)
        {
            if (targetLogic == null)
            {
                Debug.LogError("导出prefab失败: 目标节点为空");
                return;
            }
            if (UGUIParser.Instance == null)
            {
                Debug.LogError("导出prefab失败: UGUIParser配置未找到");
                return;
            }
            if (string.IsNullOrWhiteSpace(UGUIParser.Instance.SharedPrefabOutput))
            {
                Debug.LogError($"导出prefab失败! 复用prefab导出路径为空:{UGUIParser.Instance.SharedPrefabOutput}");
                return;
            }

            var outputDir = NormalizeAssetDirectory(UGUIParser.Instance.SharedPrefabOutput);
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                Debug.LogError($"导出prefab失败! 复用prefab导出路径无效:{UGUIParser.Instance.SharedPrefabOutput}");
                return;
            }
            if (!Directory.Exists(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                    AssetDatabase.Refresh();
                }
                catch (Exception err)
                {
                    Debug.LogError($"创建复用prefab导出目录失败:{err.Message}");
                    return;
                }
            }

            var prefabBaseName = LayerNameUtility.Sanitize(targetLogic.name);
            if (string.IsNullOrWhiteSpace(prefabBaseName))
            {
                prefabBaseName = "PsdLayerPrefab";
            }
            var prefabName = Path.Combine(outputDir, $"{prefabBaseName}.prefab").Replace("\\", "/");
            bool prefabExists = File.Exists(prefabName);
            if (prefabExists)
            {
                if (!EditorUtility.DisplayDialog("警告", $"prefab文件已存在, 是否覆盖:{prefabName}", "覆盖生成", "取消生成"))
                {
                    return;
                }
                // 不删除现有prefab,让SaveAsPrefabAssetAndConnect保留GUID
            }

            var rootLayerNode = targetLogic.GetComponent<PsdLayerNode>();
            bool ignoreRootPrefabReference = rootLayerNode != null
                && rootLayerNode.HasReusePrefabReference
                && string.Equals(rootLayerNode.ReusePrefabKey, prefabBaseName, StringComparison.OrdinalIgnoreCase);

            var uiPrefab = ExportLayerTreeAsPrefab(targetLogic, prefabName, prefabBaseName, ignoreRootPrefabReference);
            if (uiPrefab != null)
            {
                Selection.activeGameObject = uiPrefab;
            }
        }
        private bool ExportUIPrefab(Transform root, string outputDir, PsdLayerNode updateScope = null)
        {
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                if (!Directory.Exists(outputDir))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDir);
                        AssetDatabase.Refresh();
                    }
                    catch (Exception err)
                    {
                        Debug.LogError($"导出UI prefab失败:{err.Message}");
                        return false;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(uiFormName))
            {
                Debug.LogError("导出UI Prefab失败: UI Form Name为空, 请填写UI Form Name.");
                return false;
            }
            RefreshNodesBindLayer();
            outputDir = NormalizeAssetDirectory(outputDir);
            var prefabName = Path.Combine(outputDir, $"{uiFormName}.prefab");
            string trackedOutput = AssetDatabase.GUIDToAssetPath(Document.uiFormGuid);
            if (root == transform && !string.IsNullOrEmpty(trackedOutput)) prefabName = trackedOutput;
            if (File.Exists(prefabName) && FindGeneratedMetadataEntry(prefabName) == null)
            {
                // An unregistered prefab may be user-authored. Never claim its objects by name.
                if (updateScope != null) return false;
                prefabName = AssetDatabase.GenerateUniqueAssetPath(prefabName);
                RecordGenerationIssue(null, "unregistered_output", "原文件没有节点身份记录，生成独立资产: " + prefabName);
            }
            var layerNodes = root.GetComponentsInChildren<PsdLayerNode>(true);
            var prefabRefRoots = new List<PsdLayerNode>();
            var prefabRefCandidates = layerNodes.Where(node => node != null && node.HasReusePrefabReference).ToArray();
            var rootLayerNode = root.GetComponent<PsdLayerNode>();
            if (rootLayerNode != null && rootLayerNode.HasReusePrefabReference)
            {
                prefabRefRoots.Add(rootLayerNode);
            }
            else if (prefabRefCandidates.Length > 0)
            {
                foreach (var node in prefabRefCandidates)
                {
                    if (node == null) continue;
                    bool hasPrefabAncestor = false;
                    var parent = node.transform.parent;
                    while (parent != null && parent != root)
                    {
                        var parentNode = parent.GetComponent<PsdLayerNode>();
                        if (parentNode != null && parentNode.HasReusePrefabReference && !parentNode.IsAiAuthoritative)
                        {
                            hasPrefabAncestor = true;
                            break;
                        }
                        parent = parent.parent;
                    }
                    if (!hasPrefabAncestor)
                    {
                        prefabRefRoots.Add(node);
                    }
                }
            }
            prefabRefRoots.Sort(CompareHierarchyOrder);

            bool IsInsidePrefabRef(Transform transform)
            {
                if (transform == null || prefabRefRoots.Count == 0) return false;
                foreach (var prefabRef in prefabRefRoots)
                {
                    if (prefabRef == null) continue;
                    if (transform == prefabRef.transform || !prefabRef.IsAiAuthoritative && transform.IsChildOf(prefabRef.transform)) return true;
                }
                return false;
            }

            var nodesForBuild = layerNodes.Where(node => node != null && !IsInsidePrefabRef(node.transform)).ToArray();
            NormalizePreferredUIHelpersForGeneration(nodesForBuild);
            ExportSharedReferenceSprites(updateScope == null ? nodesForBuild : updateScope.GetComponentsInChildren<PsdLayerNode>(true));

            var uiHelpers = GetAvailableUIHelpers(root);
            if (uiHelpers != null && uiHelpers.Length > 0 && prefabRefRoots.Count > 0)
            {
                for (int i = uiHelpers.Length - 1; i >= 0; i--)
                {
                    var helper = uiHelpers[i];
                    if (helper == null || helper.LayerNode == null) continue;
                    if (IsInsidePrefabRef(helper.transform))
                    {
                        ArrayUtility.RemoveAt(ref uiHelpers, i);
                    }
                }
            }
            if ((uiHelpers == null || uiHelpers.Length < 1) && prefabRefRoots.Count < 1 && FindGeneratedMetadataEntry(prefabName) == null)
            {
                return false;
            }

            var uiPrefab = RunPrefabGeneration(
                root, rootLayerNode, nodesForBuild, uiHelpers, prefabRefRoots,
                rootHelper: null, prefabAssetPath: prefabName, prefabRootName: uiFormName,
                isFullFormExport: root == this.transform, updateScope: updateScope);
            if (uiPrefab != null)
            {
                Selection.activeGameObject = uiPrefab;
            }
            return uiPrefab != null;
        }

        private readonly HashSet<UIHelperBase> failedGenerationHelpers = new HashSet<UIHelperBase>();
        private readonly List<AiRecognitionIssue> pendingGenerationIssues = new List<AiRecognitionIssue>();

        internal void RecordGenerationIssue(PsdLayerNode node, string code, string reason)
        {
            var issues = GenerationContext.Current != null ? GenerationContext.Current.GenerationIssues : pendingGenerationIssues;
            string id = node != null ? AiNodeIdUtility.GetNodeId(this, node) : string.Empty;
            if (!issues.Exists(issue => issue.nodeId == id && issue.code == code && issue.reason == reason))
                issues.Add(new AiRecognitionIssue { nodeId = id, code = code, action = "保留独立元素并继续其他单元", reason = reason });
#if EFUN_DEBUG
            Debug.LogWarning("[PSD2UIForm] " + code + ": " + reason, node);
#endif
        }

        private sealed class BuildProgress
        {
            public int Current;
            public int Total;
        }

        /// <summary>
        /// 覆盖生成核心:contents 原地编辑(LoadPrefabContents → 就地修改 → SaveAsPrefabAsset → finally UnloadPrefabContents)。
        /// 未被销毁的对象 fileID 全部保留 —— 引用保持的唯一根基。流程:Analyze(调用方完成) → Match(树对齐) → Build → Save+Capture。
        /// </summary>
        private GameObject RunPrefabGeneration(
            Transform exportRoot,
            PsdLayerNode exportRootNode,
            PsdLayerNode[] nodesForBuild,
            UIHelperBase[] uiHelpers,
            List<PsdLayerNode> prefabRefRoots,
            UIHelperBase rootHelper,
            string prefabAssetPath,
            string prefabRootName,
            bool isFullFormExport,
            PsdLayerNode updateScope = null)
        {
            var existingPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
            bool editingExisting = existingPrefabAsset != null;
            if (updateScope != null && !editingExisting) throw new InvalidOperationException("局部更新需要已关联的 UIForm prefab。");

            // 目标 prefab 正打开在 Prefab Mode 时 LoadPrefabContents 会抛 InvalidOperationException,提前拦截
            if (editingExisting)
            {
                var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage != null && !string.IsNullOrWhiteSpace(currentStage.assetPath)
                    && AssetDatabase.AssetPathToGUID(currentStage.assetPath) == AssetDatabase.AssetPathToGUID(prefabAssetPath))
                {
                    Debug.LogError($"覆盖生成prefab失败: 目标prefab正打开在Prefab Mode, 请先关闭后再生成: {prefabAssetPath}");
                    return null;
                }
            }

            GameObject contentsRoot = null;
            bool contentsLoaded = false;
            bool contextStarted = true;
            var context = GenerationContext.Begin();
            context.GenerationIssues.AddRange(pendingGenerationIssues);
            var rootHelperForTree = rootHelper;
            var generationTimer = System.Diagnostics.Stopwatch.StartNew();
            using (var imageBatch = new PsdImageExportBatch(this))
            {
                try
                {
                    if (editingExisting)
                    {
                        contentsRoot = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                        contentsLoaded = true;
                    }
                    else if (rootHelper != null)
                    {
                        try
                        {
                            if (!failedGenerationHelpers.Contains(rootHelper)) contentsRoot = rootHelper.CreateUI(null);
                            if (contentsRoot == null) throw new InvalidOperationException("Root Helper has no usable output.");
                        }
                        catch (Exception ex)
                        {
                            RecordGenerationIssue(rootHelper.LayerNode, "root_helper_failed", ex.Message);
                            contentsRoot = new GameObject(prefabRootName, typeof(RectTransform));
                            rootHelper = null;
                        }
                    }
                    else if (isFullFormExport && UGUIParser.Instance.UIFormTemplate != null)
                    {
                        contentsRoot = GameObject.Instantiate(UGUIParser.Instance.UIFormTemplate, Vector3.zero, Quaternion.identity);
                        contentsRoot.transform.localScale = Vector3.one;
                    }
                    else
                    {
                        contentsRoot = new GameObject(prefabRootName, typeof(RectTransform));
                        contentsRoot.layer = UnityEngine.LayerMask.NameToLayer("UI");
                        contentsRoot.transform.localPosition = Vector3.zero;
                        contentsRoot.transform.localRotation = Quaternion.identity;
                        contentsRoot.transform.localScale = Vector3.one;
                        if (exportRootNode != null)
                        {
                            UGUIParser.SetRectTransform(exportRootNode, contentsRoot.transform);
                        }
                    }
                    if (contentsRoot == null)
                    {
                        if (isFullFormExport)
                        {
                            Debug.LogError("生成UIForm失败: UIFormTemplate为空");
                        }
                        return null;
                    }

                    var previousMetadata = FindGeneratedMetadataEntry(prefabAssetPath);
                    Dictionary<string, GameObject> previousGeneratedBySourceIdentity = null;
                    Dictionary<GameObject, string> previousTypeKeys = null;
                    HashSet<GameObject> knownGeneratedNodes = null;
                    GameObject propertyScope = null;
                    if (editingExisting)
                    {
                        previousGeneratedBySourceIdentity = RestoreGeneratedIdentityMap(prefabAssetPath, contentsRoot, out knownGeneratedNodes, out previousTypeKeys);
                        if (updateScope != null && !previousGeneratedBySourceIdentity.TryGetValue(updateScope.PersistentId, out propertyScope))
                            throw new InvalidOperationException("局部更新的目标对象不存在，保留原 prefab。");
                    }
                    var propertyBaseline = previousMetadata != null ? previousMetadata.Properties : new List<PsdPropertyRecord>();
                    var propertyState = new PsdManagedProperties(contentsRoot, existingPrefabAsset, propertyBaseline, propertyScope);
                    var existingScripts = new HashSet<MonoBehaviour>(editingExisting ? contentsRoot.GetComponentsInChildren<MonoBehaviour>(true) : Array.Empty<MonoBehaviour>());
                    if (editingExisting)
                        foreach (var current in contentsRoot.GetComponentsInChildren<Transform>(true))
                        {
                            context.ExistingOutputObjects.Add(current.gameObject);
                            foreach (var component in current.GetComponents<Component>()) if (component != null) context.ExistingOutputObjects.Add(component);
                        }
                    if (updateScope == null) NormalizeOverlayCanvasRootScaleForGeneration(contentsRoot);

                    // 生成期间禁用 Canvas:contents 场景里 Canvas 系统会持续重算 Overlay 根
                    // (scale 归零、root rect 变为窗口尺寸、root 世界坐标偏移到 rect 左下角),
                    // 导致"以图层中心为世界坐标"的定位写入错位(界面不在 Canvas 中心)。
                    // 禁用后根保持 scale=1、位于原点,行为与编辑器场景一致(HEAD 版生成路径)。
                    var rootCanvas = contentsRoot.GetComponent<Canvas>();
                    bool rootCanvasWasEnabled = rootCanvas != null && rootCanvas.enabled;
                    if (rootCanvas != null)
                    {
                        rootCanvas.enabled = false;
                        // 防御:禁用前 Canvas 系统可能已把根摆到 rect 左下角/缩放归零,统一复位
                        if (updateScope == null)
                        {
                            contentsRoot.transform.localPosition = Vector3.zero;
                            contentsRoot.transform.localRotation = Quaternion.identity;
                            contentsRoot.transform.localScale = Vector3.one;
                        }
                    }

                    // 根处理
                    Quaternion rootLocalRotation = Quaternion.identity;
                    bool retainedRoot = false;
                    if (rootHelper != null && editingExisting && previousMetadata != null
                        && !string.IsNullOrEmpty(previousMetadata.RootTypeKey) && previousMetadata.RootTypeKey != rootHelper.LayerNode.UIType.ToString())
                    {
                        RecordGenerationIssue(rootHelper.LayerNode, "component_migration_pending", "独立 prefab 根类型变化，保留已有组件引用。");
                        retainedRoot = true;
                        rootHelper = null;
                    }
                    if (rootHelper != null && contentsLoaded)
                    {
                        // 根是 UI 元素且覆盖现有 prefab:原地复用/重建根
                        var capturedEvents = UnityEventPreserver.Capture(contentsRoot);
                        GUIType? previousRootUIType = null;
                        var previousRootTypeKey = previousMetadata?.RootTypeKey ?? rootHelper.LayerNode.UIType.ToString();
                        if (Enum.TryParse(previousRootTypeKey, out GUIType parsedPreviousRootUIType))
                        {
                            previousRootUIType = parsedPreviousRootUIType;
                        }
                        GameObject rebuiltRoot = null;
                        try { rebuiltRoot = rootHelper.CreateUI(null, contentsRoot, previousRootUIType); }
                        catch (Exception ex) { RecordGenerationIssue(rootHelper.LayerNode, "root_helper_failed", ex.Message); }
                        UnityEventPreserver.Restore(rebuiltRoot != null ? rebuiltRoot : contentsRoot, capturedEvents);
                        if (rebuiltRoot == null)
                        {
                            rootHelper = null;
                            retainedRoot = true;
                            context.FailedOutputRoots.Add(contentsRoot);
                        }
                        else
                        {
                            contentsRoot = rebuiltRoot;
                        }
                    }
                    if (rootHelper != null)
                    {
                        contentsRoot.name = prefabRootName;
                        contentsRoot.layer = UnityEngine.LayerMask.NameToLayer("UI");
                        if (rootHelper.LayerNode != null && rootHelper.LayerNode.TryGetTextUnityRotation(out _))
                        {
                            rootLocalRotation = contentsRoot.transform.localRotation;
                        }
                        contentsRoot.transform.localPosition = Vector3.zero;
                        contentsRoot.transform.localRotation = Quaternion.identity;
                        contentsRoot.transform.localScale = Vector3.one;
                    }
                    else if (editingExisting && !isFullFormExport && !retainedRoot)
                    {
                        // 子树导出且根不是元素:覆盖旧容器根
                        if (contentsRoot.GetComponent<RectTransform>() == null)
                        {
                            Debug.LogError($"覆盖生成prefab失败: 旧prefab根节点不是RectTransform, 保留原资产避免丢失引用: {prefabAssetPath}");
                            return null;
                        }
                        StripManagedGeneratedRootComponents(contentsRoot);
                        contentsRoot.name = prefabRootName;
                        contentsRoot.layer = UnityEngine.LayerMask.NameToLayer("UI");
                        contentsRoot.transform.localPosition = Vector3.zero;
                        contentsRoot.transform.localRotation = Quaternion.identity;
                        contentsRoot.transform.localScale = Vector3.one;
                        if (exportRootNode != null)
                        {
                            UGUIParser.SetRectTransform(exportRootNode, contentsRoot.transform);
                        }
                    }
                    else
                    {
                        contentsRoot.name = prefabRootName;
                    }

                    var desiredRoots = BuildDesiredTree(exportRoot, rootHelperForTree, nodesForBuild, uiHelpers, prefabRefRoots, context);
                    if (rootHelper == null && rootHelperForTree != null && !retainedRoot)
                    {
                        var independent = new HashSet<Transform>();
                        foreach (var desired in desiredRoots) if (desired.LayerNode != null) independent.Add(desired.LayerNode.transform);
                        BuildBasicGenerationSources(rootHelperForTree.LayerNode, contentsRoot.transform, independent);
                    }

                    if (editingExisting)
                    {
                        GeneratedTreeMatcher.Match(
                            context, contentsRoot, desiredRoots, previousGeneratedBySourceIdentity, knownGeneratedNodes);
                        FillPreviousTypeKeys(desiredRoots, previousTypeKeys);
                    }
                    // 新资产的节点保持 Create 决策。
                    if (updateScope != null)
                        desiredRoots = SelectWriteScope(updateScope, desiredRoots, context, previousGeneratedBySourceIdentity, prefabAssetPath);

                    // Build
                    var generatedUiHelpers = new List<UIHelperBase>(uiHelpers != null ? uiHelpers.Length : 0);
                    var generatedUiElements = new List<GameObject>(uiHelpers != null ? uiHelpers.Length : 0);
                    var progress = new BuildProgress { Total = uiHelpers != null ? uiHelpers.Length : 0 };
                    foreach (var desiredRoot in desiredRoots)
                    {
                        BuildGeneratedNodeRecursive(desiredRoot, contentsRoot.transform, exportRoot, generatedUiHelpers, generatedUiElements, progress);
                    }

                    // 后处理
                    if (updateScope == null) SynchronizeGeneratedGroupOpacities(exportRoot, contentsRoot, nodesForBuild);
                    NotifyGeneratedHierarchyReady(generatedUiHelpers, generatedUiElements);
                    if (rootHelper != null)
                    {
                        try { rootHelper.OnGeneratedHierarchyReady(contentsRoot); }
                        catch (Exception ex)
                        {
                            context.FailedOutputRoots.Add(contentsRoot);
                            RecordGenerationIssue(rootHelper.LayerNode, "root_postprocess_failed", ex.Message);
                        }
                    }
                    int orderIssues = updateScope == null ? PsdGeneratedDrawOrder.Reconcile(context, exportRoot, contentsRoot.transform)
                        : ReconcileScopedOrder(updateScope, context);
                    if (orderIssues > 0) RecordGenerationIssue(exportRootNode, "template_draw_order", orderIssues + " 个模板范围存在顺序交错，已保留独立元素供调整。");
                    ParkOrphans(context.NodesToOrphan, contentsRoot.transform);
                    if (rootHelper != null)
                    {
                        // 根 Text/TMP 元素的旋转在根归一化后恢复(旧行为保持)
                        contentsRoot.transform.localRotation = rootLocalRotation;
                    }
                    if (updateScope == null) FitGeneratedContainerHierarchy(contentsRoot.transform);
                    else UpdateScopedScrollBounds(updateScope, context, propertyState);
                    RemoveGeneratedScripts(contentsRoot, existingScripts);
                    imageBatch.Complete(contentsRoot);
                    propertyState.Merge(context);
                    var generatedNodeSnapshots = CaptureGeneratedNodeSnapshots(
                        contentsRoot, context, previousGeneratedBySourceIdentity, previousTypeKeys);

                    // 保存:未被销毁的对象 fileID 全部保留,外部引用不丢。
                    // Canvas 保持禁用状态保存(contents 场景里一旦恢复,Canvas 系统会立即把根 scale 重算为 0)。
                    contentsRoot.name = System.IO.Path.GetFileNameWithoutExtension(prefabAssetPath);
                    var savedPrefab = PrefabUtility.SaveAsPrefabAsset(contentsRoot, prefabAssetPath);
                    if (savedPrefab == null)
                    {
                        // 保存失败必须报错：否则界面看着"生成完成"，prefab 其实还是旧的。
                        Debug.LogError($"生成 UIForm 失败：Prefab 保存返回空, 目标={prefabAssetPath}, 本次生成结果已丢弃", this);
                    }
                    if (savedPrefab != null)
                    {
                        SaveGeneratedMetadata(prefabAssetPath, savedPrefab, generatedNodeSnapshots);
                        var savedMetadata = FindGeneratedMetadataEntry(prefabAssetPath);
                        savedMetadata.Properties = propertyState.Save(savedPrefab, propertyBaseline);
                        savedMetadata.RootTypeKey = retainedRoot ? previousMetadata?.RootTypeKey : rootHelperForTree?.LayerNode.UIType.ToString();
                        if (isFullFormExport) Document.uiFormGuid = AssetDatabase.AssetPathToGUID(prefabAssetPath);
                        SaveUiDocument(prefabAssetPath);
                        string prefabGuid = AssetDatabase.AssetPathToGUID(prefabAssetPath);
                        // GUID 为空时所有报告都写进同一个 ".json"，彼此覆盖。
                        string reportKey = string.IsNullOrEmpty(prefabGuid)
                            ? "unimported-" + AiPathDefaults.GetStableToken(prefabAssetPath.ToLowerInvariant())
                            : prefabGuid;
                        string reportPath = Path.Combine("Library", "PSD2UIForm", "generation-reports", reportKey + ".json");
                        int generatedCount = 0, retainedCount = 0, unresolvedCount = 0;
                        foreach (var pair in context.DesiredToGenerated)
                        {
                            if (context.WriteScope != null && !context.WriteScope.Contains(pair.Key.SourceIdentity)) continue;
                            if (pair.Key.PreservePreviousType || context.FailedOutputRoots.Contains(pair.Value))
                            {
                                if (pair.Key.ReuseGo != null) retainedCount++;
                                else unresolvedCount++;
                            }
                            else generatedCount++;
                        }
                        if (rootHelperForTree != null)
                        {
                            if (retainedRoot || context.FailedOutputRoots.Contains(contentsRoot))
                            {
                                if (editingExisting) retainedCount++;
                                else unresolvedCount++;
                            }
                            else if (rootHelper != null) generatedCount++;
                            else unresolvedCount++;
                        }
                        AiJobFileUtility.WriteJson(reportPath, new PsdGenerationReport { prefabPath = prefabAssetPath,
                            status = context.GenerationIssues.Count == 0 ? "complete" : "partial_success", generatedUnitCount = generatedCount,
                            retainedUnitCount = retainedCount, unresolvedUnitCount = unresolvedCount,
                            basicElementCount = context.BasicElementCount, elapsedMilliseconds = generationTimer.ElapsedMilliseconds, issues = context.GenerationIssues });
                        if (context.GenerationIssues.Count > 0) Debug.LogWarning("[PSD2UIForm] 局部生成报告: " + Path.GetFullPath(reportPath), this);
                    }
                    if (savedPrefab != null && rootCanvas != null)
                    {
                        // 保存后在资产对象上恢复 Canvas 状态与根 scale(资产对象不在场景中,Canvas 系统不会重算,scale 保持 1)
                        var assetCanvas = savedPrefab.GetComponent<Canvas>();
                        if (assetCanvas != null)
                        {
                            assetCanvas.enabled = rootCanvasWasEnabled;
                            EditorUtility.SetDirty(assetCanvas);
                        }
                        if (updateScope == null) NormalizeOverlayCanvasRootScaleForGeneration(savedPrefab);
                        EditorUtility.SetDirty(savedPrefab.transform);
                        AssetDatabase.SaveAssets();
                    }
                    return savedPrefab;
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    if (contextStarted)
                    {
                        GenerationContext.End();
                    }
                    if (contentsRoot != null)
                    {
                        if (contentsLoaded)
                        {
                            PrefabUtility.UnloadPrefabContents(contentsRoot);
                        }
                        else
                        {
                            DestroyImmediate(contentsRoot);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 构建期望树:元素(helper 节点)+ prefabRef 根 + 容器(已覆盖节点的祖先链)。
        /// 模板子节点(如按钮文本)不属于期望树,由各 helper 在其元素内自行复用/更新。
        /// </summary>
        private List<GeneratedTreeNode> BuildDesiredTree(
            Transform exportRoot,
            UIHelperBase rootHelper,
            PsdLayerNode[] sources,
            UIHelperBase[] uiHelpers,
            List<PsdLayerNode> prefabRefRoots,
            GenerationContext context)
        {
            var nodeMap = new Dictionary<PsdLayerNode, GeneratedTreeNode>();
            var roots = new List<GeneratedTreeNode>();

            // 1) 元素节点(根元素除外)
            if (uiHelpers != null)
            {
                for (int i = 0; i < uiHelpers.Length; i++)
                {
                    var helper = uiHelpers[i];
                    if (helper == null || helper.LayerNode == null) continue;
                    if (rootHelper != null && helper == rootHelper) continue;
                    nodeMap[helper.LayerNode] = new GeneratedTreeNode
                    {
                        LayerNode = helper.LayerNode,
                        Helper = helper,
                    };
                }
            }

            var knownContainers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var output in Document.outputs)
                foreach (var entry in output.Entries) if (entry.IsContainer) knownContainers.Add(entry.SourceIdentity);
            foreach (var source in sources)
            {
                if (source.transform == exportRoot || source.HasMissingSource || source.IsUnassignedSource
                    || !source.gameObject.activeInHierarchy || source.HasReusePrefabReference || nodeMap.ContainsKey(source)) continue;
                bool container = source.UIType == GUIType.Null && knownContainers.Contains(source.PersistentId);
                if (container || source.UIType != GUIType.Null && source.IsMainUIType && source.GetComponent<UIHelperBase>() == null)
                    nodeMap.Add(source, new GeneratedTreeNode { LayerNode = source, IsContainer = container });
            }

            // 2) prefabRef 根
            if (prefabRefRoots != null)
            {
                for (int i = 0; i < prefabRefRoots.Count; i++)
                {
                    var prefabRefNode = prefabRefRoots[i];
                    if (prefabRefNode == null) continue;
                    if (!nodeMap.ContainsKey(prefabRefNode))
                    {
                        nodeMap[prefabRefNode] = new GeneratedTreeNode
                        {
                            LayerNode = prefabRefNode,
                            IsPrefabRef = true,
                        };
                    }
                }
            }

            // 3) 容器:为已建节点补祖先链
            var createdNodes = new List<GeneratedTreeNode>(nodeMap.Values);
            for (int i = 0; i < createdNodes.Count; i++)
            {
                var current = createdNodes[i].LayerNode.transform.parent;
                while (current != null && current != exportRoot)
                {
                    var ancestorNode = current.GetComponent<PsdLayerNode>();
                    if (ancestorNode != null && !nodeMap.ContainsKey(ancestorNode))
                    {
                        var ancestorHelper = ancestorNode.IsMainUIType ? ancestorNode.GetComponent<UIHelperBase>() : null;
                        nodeMap[ancestorNode] = new GeneratedTreeNode
                        {
                            LayerNode = ancestorNode,
                            Helper = ancestorHelper,
                            IsContainer = ancestorHelper == null,
                        };
                    }
                    current = current.parent;
                }
            }

            // 4) 父子链接 + 身份填充
            foreach (var treeNode in nodeMap.Values)
            {
                GeneratedTreeNode parent = null;
                var current = treeNode.LayerNode.transform.parent;
                while (current != null && current != exportRoot)
                {
                    var ancestorNode = current.GetComponent<PsdLayerNode>();
                    if (ancestorNode != null && nodeMap.TryGetValue(ancestorNode, out parent))
                    {
                        break;
                    }
                    current = current.parent;
                }

                treeNode.Parent = parent;
                if (parent != null)
                {
                    parent.Children.Add(treeNode);
                }
                else
                {
                    roots.Add(treeNode);
                }

                FillTreeNodeIdentity(treeNode, exportRoot);
                context.DesiredNodeByLayer[treeNode.LayerNode] = treeNode;
            }

            // 5) 子节点按源兄弟序排序
            SortDesiredChildren(roots);
            return roots;
        }

        private static void SortDesiredChildren(List<GeneratedTreeNode> nodes)
        {
            nodes.Sort((left, right) => CompareSiblingIndex(left, right));
            for (int i = 0; i < nodes.Count; i++)
            {
                SortDesiredChildren(nodes[i].Children);
            }
        }

        private static int CompareSiblingIndex(GeneratedTreeNode left, GeneratedTreeNode right)
        {
            int leftIndex = left != null && left.LayerNode != null ? left.LayerNode.transform.GetSiblingIndex() : -1;
            int rightIndex = right != null && right.LayerNode != null ? right.LayerNode.transform.GetSiblingIndex() : -1;
            return leftIndex.CompareTo(rightIndex);
        }

        private void FillTreeNodeIdentity(GeneratedTreeNode treeNode, Transform exportRoot)
        {
            treeNode.GeneratedName = PsdLayerNode.GetGeneratedObjectName(treeNode.LayerNode.name);
            treeNode.SourceIdentity = GetSourceIdentity(treeNode.LayerNode);
        }

        private static void FillPreviousTypeKeys(
            List<GeneratedTreeNode> nodes,
            Dictionary<GameObject, string> previousTypeKeys)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.ReuseGo != null)
                {
                    if (previousTypeKeys == null
                        || !previousTypeKeys.TryGetValue(node.ReuseGo, out node.PreviousTypeKey)
                        || string.IsNullOrEmpty(node.PreviousTypeKey))
                    {
                        node.PreviousTypeKey = GeneratedTreeMatcher.DeriveTypeKeyFromComponents(node.ReuseGo);
                    }
                }
                FillPreviousTypeKeys(node.Children, previousTypeKeys);
            }
        }

        private void BuildGeneratedNodeRecursive(
            GeneratedTreeNode node,
            Transform fallbackParent,
            Transform exportRoot,
            List<UIHelperBase> generatedUiHelpers,
            List<GameObject> generatedUiElements,
            BuildProgress progress)
        {
            var context = GenerationContext.Current;
            Transform parent = node.Parent != null
                && context.DesiredToGenerated.TryGetValue(node.Parent, out var parentGo)
                && parentGo != null
                ? parentGo.transform
                : fallbackParent;

            GameObject generated = null;
            if (context.WriteScope != null && node.Parent != null && parent != null)
            {
                var scroll = parent.GetComponent<ScrollRect>();
                if (scroll != null && scroll.content != null) parent = scroll.content;
            }
            if (!node.IsPrefabRef && context.HelperRoots.TryGetValue(node.LayerNode, out var embedded) && embedded != null)
            {
                generated = embedded;
                if (node.ReuseGo != null && node.ReuseGo != embedded) OrphanReuseGo(context, node.ReuseGo);
                context.NodesToOrphan.Remove(embedded);
                generatedUiHelpers.Add(node.Helper);
                generatedUiElements.Add(embedded);
            }
            else if (node.ReuseGo != null && !string.IsNullOrEmpty(node.PreviousTypeKey) && node.PreviousTypeKey != GetDesiredTypeKey(node))
            {
                generated = node.ReuseGo;
                node.PreservePreviousType = true;
                RecordGenerationIssue(node.LayerNode, "component_migration_pending", "UI 类型发生变化；保留已有组件引用，等待明确迁移。");
            }
            else if (node.IsContainer)
            {
                generated = BuildGeneratedContainer(node, parent);
            }
            else if (node.IsPrefabRef)
            {
                try { generated = BuildGeneratedPrefabRef(node, parent); }
                catch (Exception ex) { RecordGenerationIssue(node.LayerNode, "prefab_reference_failed", ex.Message); }
                if (generated == null)
                {
                    generated = node.ReuseGo != null ? node.ReuseGo : BuildBasicGenerationUnit(node, parent);
                    context.FailedOutputRoots.Add(generated);
                }
            }
            else
            {
                try
                {
                    if (node.Helper != null && failedGenerationHelpers.Contains(node.Helper)) throw new InvalidOperationException("Helper dependency parsing failed.");
                    generated = BuildGeneratedElement(node, parent, exportRoot, progress);
                    if (generated != null)
                    {
                        generatedUiHelpers.Add(node.Helper);
                        generatedUiElements.Add(generated);
                    }
                    else
                    {
                        RecordGenerationIssue(node.LayerNode, "helper_empty", "Helper has no usable output; retaining the node and independent children.");
                        // 旧节点是外部引用的 fileID 载体：销毁会让引用变 Missing，改为收容停用。
                        generated = node.ReuseGo != null ? node.ReuseGo : BuildBasicGenerationUnit(node, parent);
                        context.FailedOutputRoots.Add(generated);
                    }
                }
                catch (Exception ex)
                {
                    RecordGenerationIssue(node.LayerNode, "helper_build_failed", ex.Message);
                    generated = node.ReuseGo != null ? node.ReuseGo : BuildBasicGenerationUnit(node, parent);
                    context.FailedOutputRoots.Add(generated);
                }
            }
            if (generated == null) return;

            context.DesiredToGenerated[node] = generated;
            for (int i = 0; i < node.Children.Count; i++)
            {
                BuildGeneratedNodeRecursive(node.Children[i], generated.transform, exportRoot, generatedUiHelpers, generatedUiElements, progress);
            }
        }

        private GameObject BuildBasicGenerationUnit(GeneratedTreeNode node, Transform parent)
        {
            var root = new GameObject(node.LayerNode != null ? node.LayerNode.GetGeneratedObjectName() : "UnresolvedUI", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            if (node.LayerNode != null && node.LayerNode.IsAiAuthoritative)
            {
                // A failed Helper cannot turn an AI control or role into a guessed Image/Text.
                UGUIParser.SetRectTransform(node.LayerNode, root.transform);
                return root;
            }
            var independent = new HashSet<Transform>();
            foreach (var child in node.Children) if (child.LayerNode != null) independent.Add(child.LayerNode.transform);
            if (node.LayerNode != null) BuildBasicGenerationSources(node.LayerNode, root.transform, independent);
            return root;
        }

        private void BuildBasicGenerationSources(PsdLayerNode source, Transform parent, HashSet<Transform> independent)
        {
            if (source.IsAiAuthoritative) return;
            if (independent.Contains(source.transform)) return;
            if (source.HasReuseReference || source.HasReusePrefabReference) return;
            bool text = source.IsTextLayer(out _);
            if (text || source.LayerType != PsdLayerType.LayerGroup || source.CollapseChildrenForGeneration)
            {
                GameObject element = null;
                try
                {
                    element = new GameObject(source.GetGeneratedObjectName(), typeof(RectTransform));
                    element.transform.SetParent(parent, false);
                    if (text)
                    {
                        var label = element.AddComponent<UnityEngine.UI.Text>();
                        UGUIParser.SetTextStyle(source, label);
                        UGUIParser.SetTextRectTransform(source, label);
                        UGUIParser.SetTextRotation(source, label);
                    }
                    else
                    {
                        var image = element.AddComponent<UnityEngine.UI.Image>();
                        UGUIParser.SetRectTransform(source, image);
                        UGUIParser.Instance.BindImage(source, image);
                    }
                    if (GenerationContext.Current != null) GenerationContext.Current.BasicElementCount++;
                }
                catch (Exception ex)
                {
                    if (element != null) DestroyImmediate(element);
                    RecordGenerationIssue(source, "basic_element_failed", ex.Message);
                }
                return;
            }
            for (int i = 0; i < source.transform.childCount; i++)
            {
                var child = source.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (child != null) BuildBasicGenerationSources(child, parent, independent);
            }
        }

        private GameObject BuildGeneratedContainer(GeneratedTreeNode node, Transform parent)
        {
            var context = GenerationContext.Current;
            GameObject generated;
            if (node.ReuseGo != null)
            {
                generated = node.ReuseGo;
                if (node.Revived)
                {
                    generated.SetActive(true);
                }
                // 元素→容器:剥离旧托管组件(与旧版容器复用行为一致)
                if (GeneratedTreeMatcher.DeriveTypeKeyFromComponents(generated) != GeneratedTreeMatcher.ContainerTypeKey)
                {
                    StripManagedGeneratedRootComponents(generated);
                }
                generated.name = PsdLayerNode.GetGeneratedObjectName(node.LayerNode.name);
            }
            else
            {
                var nodeName = PsdLayerNode.GetGeneratedObjectName(node.LayerNode.name);
                generated = new GameObject(nodeName, typeof(RectTransform));
                generated.layer = UnityEngine.LayerMask.NameToLayer("UI");
                generated.transform.SetParent(parent, false);
                generated.transform.localPosition = Vector3.zero;
                generated.transform.localRotation = Quaternion.identity;
                generated.transform.localScale = Vector3.one;
            }

            generated.transform.SetParent(parent, false);
            // 构建顺序即显示顺序:按编辑器树相对顺序追加到末尾(嵌套节点位于模板子节点之后,渲染在其上方)
            generated.transform.SetAsLastSibling();
            generated.transform.localScale = Vector3.one;
            context.GeneratedContainerNodes.Add(generated);
            return generated;
        }

        private GameObject BuildGeneratedElement(GeneratedTreeNode node, Transform parent, Transform exportRoot, BuildProgress progress)
        {
            var context = GenerationContext.Current;
            if (node.Helper == null || node.Helper.LayerNode == null) return null;

            progress.Current++;
            if (progress.Total > 0)
            {
                EditorUtility.DisplayProgressBar(
                    $"生成UIFrom:({progress.Current}/{progress.Total})",
                    $"正在生成UI元素:{node.Helper.name}",
                    progress.Current / (float)progress.Total);
            }

            GameObject generated;
            GUIType? previousUIType = null;
            if (!string.IsNullOrEmpty(node.PreviousTypeKey)
                && Enum.TryParse(node.PreviousTypeKey, out GUIType parsedPreviousUIType))
            {
                previousUIType = parsedPreviousUIType;
            }
            if (node.ReuseGo != null)
            {
                generated = node.ReuseGo;
                if (node.Revived)
                {
                    generated.SetActive(true);
                }
                // 事件捕获/恢复:PasteComponentValues 会以模板空事件覆盖复用组件(组件重建时),必须显式保留。
                // 兼容性由 CreateUI 内部的 CreateOrReuseRoot/IsCompatibleRoot 判定(与原版语义一致),
                // 不可在此用组件推导类型预判换型 —— FillColor/TMPButton 等 UIType 名与组件名不一致,会误判导致组件被重建、引用丢失。
                var capturedEvents = UnityEventPreserver.Capture(generated);
                generated = node.Helper.CreateUI(parent, generated, previousUIType);
                UnityEventPreserver.Restore(generated, capturedEvents);
                if (generated == null) return null;
            }
            else
            {
                generated = node.Helper.CreateUI(parent);
                if (generated == null) return null;
            }

            try
            {
                generated.name = node.LayerNode.GetGeneratedObjectName();
                generated.transform.SetAsLastSibling();
                return generated;
            }
            catch
            {
                if (generated != null && generated != node.ReuseGo) DestroyImmediate(generated);
                throw;
            }
        }

        private GameObject BuildGeneratedPrefabRef(GeneratedTreeNode node, Transform parent)
        {
            var context = GenerationContext.Current;
            if (node.LayerNode == null || !TryGetOrCreateSharedPrefabAsset(node.LayerNode, out var prefabAsset) || prefabAsset == null)
            {
                if (node.LayerNode != null)
                {
                    RecordGenerationIssue(node.LayerNode, "prefab_reference_missing", "引用 prefab 未找到: " + node.LayerNode.ReusePrefabDisplayName);
                }
                return null;
            }

            GameObject instance = null;
            if (node.ReuseGo != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(node.ReuseGo);
                if (source == prefabAsset)
                {
                    instance = node.ReuseGo;
                    if (node.Revived)
                    {
                        instance.SetActive(true);
                    }
                }
                else
                {
                    RecordGenerationIssue(node.LayerNode, "prefab_migration_pending", "引用目标 prefab 变化，保留已有实例及组件引用。");
                    context.FailedOutputRoots.Add(node.ReuseGo);
                    return node.ReuseGo;
                }
            }
            if (instance == null)
            {
                instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                if (instance == null)
                {
                    instance = GameObject.Instantiate(prefabAsset);
                }
            }

            instance.name = string.IsNullOrEmpty(node.LayerNode.ReusePrefabKey)
                ? prefabAsset.name
                : PsdLayerNode.GetReferenceLeafName(node.LayerNode.ReusePrefabKey, prefabAsset.name);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            var prefabRect = instance.GetComponent<RectTransform>();
            if (prefabRect == null)
            {
                Debug.LogWarning($"引用prefab缺少RectTransform: {prefabAsset.name}");
            }

            instance.transform.SetParent(parent, false);
            // 构建顺序即显示顺序:按编辑器树相对顺序追加到末尾
            instance.transform.SetAsLastSibling();
            if (prefabRect != null)
            {
                UGUIParser.SetRectTransform(node.LayerNode, prefabRect);
            }
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        // 生成失败的旧节点是外部引用的 fileID 载体：销毁会让引用变 Missing，交给 ParkOrphans 停用收容。
        private static void OrphanReuseGo(GenerationContext context, GameObject reuseGo)
        {
            if (context == null || context.NodesToOrphan == null || reuseGo == null) return;
            if (!context.NodesToOrphan.Contains(reuseGo)) context.NodesToOrphan.Add(reuseGo);
        }

        /// <summary>
        /// 未匹配的旧受管节点移入停用孤儿区(fileID 保留,外部引用保持有效)。
        /// </summary>
        private void ParkOrphans(List<GameObject> nodesToOrphan, Transform generationRoot)
        {
            if (nodesToOrphan == null || nodesToOrphan.Count < 1) return;
            WarnOrphanReferenceImpacts(nodesToOrphan, generationRoot);

            var orphanRoot = GetOrCreateGeneratedOrphansRoot(generationRoot);
            for (int i = 0; i < nodesToOrphan.Count; i++)
            {
                var node = nodesToOrphan[i];
                if (node == null) continue;
                if (node.transform.parent == orphanRoot) continue;
                node.transform.SetParent(orphanRoot, true);
                node.SetActive(false);
            }
        }

        /// <summary>
        /// 会话内影响扫描:找出指向将被孤儿化子树的组件引用,提示目标会停用但引用仍保留。
        /// </summary>
        private static void WarnOrphanReferenceImpacts(List<GameObject> nodesToOrphan, Transform generationRoot)
        {
#if EFUN_DEBUG
            if (generationRoot == null) return;
            var orphanRoots = new HashSet<Transform>();
            for (int i = 0; i < nodesToOrphan.Count; i++)
            {
                if (nodesToOrphan[i] != null)
                {
                    orphanRoots.Add(nodesToOrphan[i].transform);
                }
            }
            if (orphanRoots.Count < 1) return;

            var components = generationRoot.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component.transform == null) continue;
                if (IsUnderAnyTransform(component.transform, orphanRoots)) continue;//孤儿子树内部

                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                while (prop.NextVisible(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var target = prop.objectReferenceValue;
                    if (target == null) continue;
                    var targetTransform = target is Component targetComponent
                        ? targetComponent.transform
                        : (target is GameObject targetGo ? targetGo.transform : null);
                    if (targetTransform != null && IsUnderAnyTransform(targetTransform, orphanRoots))
                    {
                        Debug.LogWarning($"孤儿化后引用仍保留,但目标会停用: {component.gameObject.name}.{prop.name} → {target.name}");
                    }
                }
            }
#endif
        }

        private static bool IsUnderAnyTransform(Transform node, HashSet<Transform> roots)
        {
            var current = node;
            while (current != null)
            {
                if (roots.Contains(current)) return true;
                current = current.parent;
            }
            return false;
        }

        private void NotifyGeneratedHierarchyReady(List<UIHelperBase> helpers, List<GameObject> elements)
        {
            if (helpers == null || elements == null) return;

            int count = Mathf.Min(helpers.Count, elements.Count);
            // Children finish sizing before an enclosing ScrollView measures its content.
            for (int i = count - 1; i >= 0; i--)
            {
                var helper = helpers[i];
                var element = elements[i];
                if (helper == null || element == null) continue;

                try { helper.OnGeneratedHierarchyReady(element); }
                catch (Exception ex)
                {
                    RecordGenerationIssue(helper.LayerNode, "helper_postprocess_failed", ex.Message);
                    GenerationContext.Current.FailedOutputRoots.Add(element);
                }
            }
        }

        private void SynchronizeGeneratedGroupOpacities(
            Transform sourcePathRoot,
            GameObject generatedRoot,
            PsdLayerNode[] sourceNodes)
        {
            if (sourcePathRoot == null || generatedRoot == null || sourceNodes == null)
            {
                return;
            }

            for (int i = 0; i < sourceNodes.Length; i++)
            {
                var sourceNode = sourceNodes[i];
                if (sourceNode == null || sourceNode.LayerType != PsdLayerType.LayerGroup)
                {
                    continue;
                }

                GameObject generatedNode;
                if (sourceNode.transform == sourcePathRoot)
                {
                    generatedNode = generatedRoot;
                }
                else
                {
                    var context = GenerationContext.Current;
                    generatedNode = context != null
                        && context.DesiredNodeByLayer.TryGetValue(sourceNode, out var treeNode)
                        && context.DesiredToGenerated.TryGetValue(treeNode, out var go)
                        ? go
                        : null;
                }

                if (generatedNode == null)
                {
                    continue;
                }

                bool shouldSynchronize = sourceNode.BindPsdLayer != null
                    && sourceNode.SourceOpacity < GroupOpacityOpaqueThreshold
                    && !sourceNode.BakesGroupOpacityIntoGeneratedImage;
                SynchronizeGeneratedGroupOpacity(generatedNode, sourceNode.SourceOpacity, shouldSynchronize);
            }
        }

        private static void SynchronizeGeneratedGroupOpacity(GameObject target, float sourceOpacity, bool shouldSynchronize)
        {
            var targetCanvasGroup = target.GetComponent<CanvasGroup>();
            if (targetCanvasGroup == null)
            {
                if (!shouldSynchronize)
                {
                    return;
                }
                targetCanvasGroup = target.AddComponent<CanvasGroup>();
            }
            targetCanvasGroup.alpha = shouldSynchronize ? Mathf.Clamp01(sourceOpacity) : 1f;
        }

        private static string NormalizeMetadataPrefabPath(string prefabAssetPath)
        {
            return string.IsNullOrWhiteSpace(prefabAssetPath) ? null : prefabAssetPath.Replace("\\", "/").Trim();
        }

        private static bool IsSameMetadataPrefabPath(string leftPath, string rightPath)
        {
            var leftNormalized = NormalizeMetadataPrefabPath(leftPath);
            var rightNormalized = NormalizeMetadataPrefabPath(rightPath);
            if (string.IsNullOrWhiteSpace(leftNormalized) || string.IsNullOrWhiteSpace(rightNormalized)) return false;
            return string.Equals(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetAssetGuid(string assetPath)
        {
            var normalizedPath = NormalizeMetadataPrefabPath(assetPath);
            return string.IsNullOrWhiteSpace(normalizedPath) ? string.Empty : AssetDatabase.AssetPathToGUID(normalizedPath);
        }

        private string GetPsdAssetGuid()
        {
            return GetAssetGuid(PsdAssetName);
        }

        private GeneratedMetadataSerializedEntry FindGeneratedMetadataEntry(string targetPrefabAssetPath)
        {
            if (generatedMetadataEntries == null || generatedMetadataEntries.Count < 1) return null;

            var targetPrefabGuid = GetAssetGuid(targetPrefabAssetPath);
            if (!string.IsNullOrEmpty(targetPrefabGuid))
            {
                for (int i = 0; i < generatedMetadataEntries.Count; i++)
                {
                    var entry = generatedMetadataEntries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabAssetGuid)) continue;
                    if (string.Equals(entry.PrefabAssetGuid, targetPrefabGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            for (int i = 0; i < generatedMetadataEntries.Count; i++)
            {
                var entry = generatedMetadataEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabAssetPath)) continue;
                if (IsSameMetadataPrefabPath(entry.PrefabAssetPath, targetPrefabAssetPath))
                {
                    return entry;
                }
            }
            return null;
        }

        private static List<GeneratedMetadataEntry> CloneGeneratedMetadataEntries(IEnumerable<GeneratedMetadataEntry> source)
        {
            var result = new List<GeneratedMetadataEntry>();
            if (source == null) return result;

            foreach (var item in source)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.GlobalObjectId)
                    || string.IsNullOrWhiteSpace(item.SourceIdentity))
                {
                    continue;
                }

                result.Add(new GeneratedMetadataEntry
                {
                    ParentIdentity = item.ParentIdentity,
                    GlobalObjectId = item.GlobalObjectId,
                    NodeId = item.NodeId,
                    SourceIdentity = item.SourceIdentity,
                    Key = item.Key,
                    TypeKey = item.TypeKey,
                    IsContainer = item.IsContainer,
                });
            }

            return result;
        }

        private void CleanupGeneratedMetadataEntries()
        {
            if (generatedMetadataEntries == null)
            {
                generatedMetadataEntries = new List<GeneratedMetadataSerializedEntry>();
                return;
            }

            var map = new Dictionary<string, GeneratedMetadataSerializedEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < generatedMetadataEntries.Count; i++)
            {
                var entry = generatedMetadataEntries[i];
                var normalizedPath = NormalizeMetadataPrefabPath(entry?.PrefabAssetPath);
                if (string.IsNullOrWhiteSpace(normalizedPath)) continue;
                var normalizedEntries = CloneGeneratedMetadataEntries(entry.Entries);

                var prefabGuid = !string.IsNullOrWhiteSpace(entry.PrefabAssetGuid)
                    ? entry.PrefabAssetGuid
                    : GetAssetGuid(normalizedPath);
                var mapKey = string.IsNullOrWhiteSpace(prefabGuid) ? normalizedPath : prefabGuid;
                map[mapKey] = new GeneratedMetadataSerializedEntry
                {
                    Properties = entry.Properties,
                    RootTypeKey = entry.RootTypeKey,
                    PrefabAssetPath = normalizedPath,
                    PrefabAssetGuid = prefabGuid,
                    PsdAssetGuid = entry.PsdAssetGuid,
                    Entries = normalizedEntries,
                };
            }

            generatedMetadataEntries = map.Values.OrderBy(item => item.PrefabAssetPath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void MarkGeneratedMetadataDirty()
        {
            EditorUtility.SetDirty(Document);
        }

        internal string GetGeneratedMetadataHostAssetPathForDebug()
            => AssetDatabase.GetAssetPath(Document);

        internal string[] GetGeneratedMetadataTargetsForDebug()
        {
            if (generatedMetadataEntries == null || generatedMetadataEntries.Count < 1)
            {
                return Array.Empty<string>();
            }

            return generatedMetadataEntries
                .Where(item => item != null
                    && !string.IsNullOrWhiteSpace(item.PrefabAssetPath)
                    && item.Entries != null
                    && item.Entries.Count > 0)
                .Select(item => NormalizeMetadataPrefabPath(item.PrefabAssetPath))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal string GetGeneratedMetadataJsonForDebug(string targetPrefabAssetPath)
        {
            var entry = FindGeneratedMetadataEntry(targetPrefabAssetPath);
            if (entry == null || entry.Entries == null || entry.Entries.Count < 1) return string.Empty;

            var metadata = new GeneratedMetadataCollection
            {
                Entries = CloneGeneratedMetadataEntries(entry.Entries),
            };
            return JsonUtility.ToJson(metadata, true);
        }

        private static string GetSourceIdentity(PsdLayerNode node)
        {
            return node != null ? node.PersistentId : null;
        }

        private Dictionary<string, GameObject> RestoreGeneratedIdentityMap(
            string prefabAssetPath,
            GameObject contentsRoot,
            out HashSet<GameObject> knownGeneratedNodes,
            out Dictionary<GameObject, string> previousTypeKeys)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            knownGeneratedNodes = new HashSet<GameObject>();
            previousTypeKeys = new Dictionary<GameObject, string>();
            var metadata = FindGeneratedMetadataEntry(prefabAssetPath);
            if (metadata == null || metadata.Entries == null || metadata.Entries.Count < 1 || contentsRoot == null)
            {
                return result;
            }

            var psdGuid = GetPsdAssetGuid();
            if (!string.IsNullOrEmpty(metadata.PsdAssetGuid)
                && !string.IsNullOrEmpty(psdGuid)
                && !string.Equals(metadata.PsdAssetGuid, psdGuid, StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            var prefabAssetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
            if (prefabAssetRoot == null) return result;

            var generatedByGlobalId = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var assetTransforms = prefabAssetRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < assetTransforms.Length; i++)
            {
                var transformPath = BuildTransformIndexPath(assetTransforms[i], prefabAssetRoot.transform);
                if (transformPath == null) continue;
                var contentsGo = ResolveTransformIndexPath(contentsRoot, transformPath);
                if (contentsGo == null) continue;

                var globalId = GlobalObjectId.GetGlobalObjectIdSlow(assetTransforms[i].gameObject).ToString();
                if (!string.IsNullOrEmpty(globalId))
                {
                    generatedByGlobalId[globalId] = contentsGo;
                }
            }

            for (int i = 0; i < metadata.Entries.Count; i++)
            {
                var entry = metadata.Entries[i];
                if (entry == null
                    || string.IsNullOrEmpty(entry.SourceIdentity)
                    || string.IsNullOrEmpty(entry.GlobalObjectId)
                    || !generatedByGlobalId.TryGetValue(entry.GlobalObjectId, out var generated))
                {
                    continue;
                }

                knownGeneratedNodes.Add(generated);
                if (!previousTypeKeys.ContainsKey(generated))
                {
                    previousTypeKeys.Add(generated, entry.TypeKey);
                }
                if (!result.ContainsKey(entry.SourceIdentity))
                {
                    result.Add(entry.SourceIdentity, generated);
                }
            }
            return result;
        }

        private static List<GeneratedNodeSnapshot> CaptureGeneratedNodeSnapshots(
            GameObject contentsRoot,
            GenerationContext context,
            Dictionary<string, GameObject> previousGeneratedBySourceIdentity,
            Dictionary<GameObject, string> previousTypeKeys)
        {
            var result = new List<GeneratedNodeSnapshot>();
            if (contentsRoot == null || context == null) return result;

            var capturedIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in context.DesiredToGenerated)
            {
                var desired = pair.Key;
                var generated = pair.Value;
                if (desired == null
                    || generated == null
                    || string.IsNullOrEmpty(desired.SourceIdentity)
                    || context.WriteScope != null && !context.WriteScope.Contains(desired.SourceIdentity)
                    || !capturedIdentities.Add(desired.SourceIdentity))
                {
                    continue;
                }

                var transformPath = BuildTransformIndexPath(generated.transform, contentsRoot.transform);
                if (transformPath == null) continue;
                var typeKey = GetDesiredTypeKey(desired);
                result.Add(new GeneratedNodeSnapshot
                {
                    ParentIdentity = desired.Parent?.SourceIdentity,
                    SourceIdentity = desired.SourceIdentity,
                    Key = desired.GeneratedName,
                    TypeKey = typeKey,
                    IsContainer = typeKey == GeneratedTreeMatcher.ContainerTypeKey,
                    TransformPath = transformPath,
                });
            }

            // 未被本轮期望树认领的旧生成节点已经进入孤儿区。继续保存其稳定身份，
            // 使图层在后续任意一轮恢复时仍可复活原 fileID，而非退回名称匹配并新建对象。
            if (previousGeneratedBySourceIdentity != null)
            {
                foreach (var pair in previousGeneratedBySourceIdentity)
                {
                    var sourceIdentity = pair.Key;
                    var generated = pair.Value;
                    if (generated == null
                        || string.IsNullOrEmpty(sourceIdentity)
                        || !capturedIdentities.Add(sourceIdentity))
                    {
                        continue;
                    }

                    var transformPath = BuildTransformIndexPath(generated.transform, contentsRoot.transform);
                    if (transformPath == null) continue;

                    // 未更新的节点沿用已记录的语义类型；组件无法区分 Panel 与普通 Image。
                    var typeKey = previousTypeKeys[generated];
                    result.Add(new GeneratedNodeSnapshot
                    {
                        ParentIdentity = FindGeneratedParentIdentity(generated.transform.parent, previousGeneratedBySourceIdentity),
                        SourceIdentity = sourceIdentity,
                        Key = generated.name,
                        TypeKey = typeKey,
                        IsContainer = string.Equals(
                            typeKey, GeneratedTreeMatcher.ContainerTypeKey, StringComparison.Ordinal),
                        TransformPath = transformPath,
                    });
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.SourceIdentity, right.SourceIdentity));
            return result;
        }

        private static string GetDesiredTypeKey(GeneratedTreeNode node)
        {
            if (node.PreservePreviousType) return node.PreviousTypeKey;
            if (node.IsContainer) return GeneratedTreeMatcher.ContainerTypeKey;
            if (node.IsPrefabRef) return "__PrefabRef";
            return node.LayerNode != null ? node.LayerNode.UIType.ToString() : string.Empty;
        }

        private void SaveGeneratedMetadata(
            string prefabAssetPath,
            GameObject prefabAssetRoot,
            List<GeneratedNodeSnapshot> snapshots)
        {
            var entries = new List<GeneratedMetadataEntry>(snapshots != null ? snapshots.Count : 0);
            if (prefabAssetRoot != null && snapshots != null)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    var snapshot = snapshots[i];
                    var savedGo = ResolveTransformIndexPath(prefabAssetRoot, snapshot.TransformPath);
                    if (savedGo == null) continue;

                    var globalId = GlobalObjectId.GetGlobalObjectIdSlow(savedGo).ToString();
                    if (string.IsNullOrEmpty(globalId)) continue;

                    entries.Add(new GeneratedMetadataEntry
                    {
                        ParentIdentity = snapshot.ParentIdentity,
                        GlobalObjectId = globalId,
                        SourceIdentity = snapshot.SourceIdentity,
                        Key = snapshot.Key,
                        TypeKey = snapshot.TypeKey,
                        IsContainer = snapshot.IsContainer,
                    });
                }
            }

            SaveGeneratedMetadataEntries(prefabAssetPath, entries);
        }

        private void SaveGeneratedMetadataEntries(string prefabAssetPath, List<GeneratedMetadataEntry> entries)
        {
            var normalizedPath = NormalizeMetadataPrefabPath(prefabAssetPath);
            if (string.IsNullOrEmpty(normalizedPath)) return;
            if (generatedMetadataEntries == null)
            {
                generatedMetadataEntries = new List<GeneratedMetadataSerializedEntry>();
            }

            var metadata = FindGeneratedMetadataEntry(normalizedPath);
            var normalizedEntries = CloneGeneratedMetadataEntries(entries);
            if (metadata == null)
            {
                metadata = new GeneratedMetadataSerializedEntry();
                generatedMetadataEntries.Add(metadata);
            }
            metadata.PrefabAssetPath = normalizedPath;
            metadata.PrefabAssetGuid = GetAssetGuid(normalizedPath);
            metadata.PsdAssetGuid = GetPsdAssetGuid();
            metadata.Entries = normalizedEntries;

            CleanupGeneratedMetadataEntries();
            MarkGeneratedMetadataDirty();

        }

        private static List<int> BuildTransformIndexPath(Transform target, Transform root)
        {
            if (target == null || root == null || !target.IsChildOf(root)) return null;

            var path = new List<int>(8);
            var current = target;
            while (current != null && current != root)
            {
                path.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }
            return current == root ? path : null;
        }

        private static GameObject ResolveTransformIndexPath(GameObject root, List<int> transformPath)
        {
            if (root == null || transformPath == null) return null;

            var current = root.transform;
            for (int i = 0; i < transformPath.Count; i++)
            {
                int childIndex = transformPath[i];
                if (childIndex < 0 || childIndex >= current.childCount) return null;
                current = current.GetChild(childIndex);
            }
            return current.gameObject;
        }

        private static bool IsGeneratedOrphansRoot(Transform transform)
        {
            return transform != null && string.Equals(transform.name, GeneratedOrphansRootName, StringComparison.Ordinal);
        }

        // ScreenSpaceOverlay Canvas 根在 LoadPrefabContents 的孤立场景里没有显示设备,
        // Canvas 会把根 localScale 置零,导致 SetRectTransform 的世界坐标写入被吞掉(子节点位置归零)。
        // 生成前把退化 scale 修正为 1,使世界坐标赋值生效;Overlay Canvas 运行时会自行重算 scale,序列化值 1 是正确基线。
        private static void NormalizeOverlayCanvasRootScaleForGeneration(GameObject root)
        {
            if (root == null) return;
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) return;

            var scale = root.transform.localScale;
            if (scale.x == 0f || scale.y == 0f)
            {
                root.transform.localScale = Vector3.one;
            }
        }

        private static Transform GetOrCreateGeneratedOrphansRoot(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (IsGeneratedOrphansRoot(child))
                {
                    child.gameObject.SetActive(false);
                    return child;
                }
            }

            var orphanRoot = new GameObject(GeneratedOrphansRootName, typeof(RectTransform));
            orphanRoot.transform.SetParent(root, false);
            orphanRoot.SetActive(false);
            return orphanRoot.transform;
        }

        private readonly Dictionary<PsdLayerNode, string> imageExportNames = new Dictionary<PsdLayerNode, string>();
        private readonly Dictionary<string, PsdLayer> originalResourceLayers = new Dictionary<string, PsdLayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PsdLayer> declaredResourceLayers = new Dictionary<string, PsdLayer>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ambiguousResourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool resourceCatalogReady;

        private void ResolveLayerNameLookup(PsdLayerNode[] nodes = null)
        {
            layerLookupByName.Clear();
            referencedLayerKeys.Clear();
            originalResourceLayers.Clear();
            declaredResourceLayers.Clear();
            ambiguousResourceNames.Clear();
            imageExportNames.Clear();
            if (nodes == null) nodes = GetComponentsInChildren<PsdLayerNode>(true);
            var exportNodes = new List<PsdLayerNode>();
            var names = new List<string>();
            foreach (var node in nodes)
            {
                if (node.HasReuseReference) referencedLayerKeys.Add(node.ReuseTargetKey);
                if (node.HasReuseReference || node.HasReusePrefabReference) continue;
                string key = node.ResourceName;
                if (layerLookupByName.ContainsKey(key)) layerLookupByName[key] = null;
                else layerLookupByName.Add(key, node);
                if (!node.NeedExportImage()) continue;
                exportNodes.Add(node);
            }
            // Source order is stable across save/reload, unlike Unity instance IDs.
            exportNodes.Sort((a, b) => {
                int order = a.RenderOrderSourceIndex.CompareTo(b.RenderOrderSourceIndex);
                return order != 0 ? order : CompareHierarchyOrder(a, b);
            });
            foreach (var node in exportNodes) names.Add(node.ResourceName);
            var allocated = PsdResourceNaming.AllocateNames(names);
            for (int i = 0; i < allocated.Length; i++) imageExportNames.Add(exportNodes[i], allocated[i]);
            var document = PsdInstance;
            if (document != null) CollectResourceLayers(document.Childs);
            resourceCatalogReady = true;
        }

        private void CollectResourceLayers(PsdLayer[] layers)
        {
            foreach (var layer in layers)
            {
                PsdResourceNaming.Parse(layer.Name, out var kind, out var key);
                if (kind == "ref")
                {
                    if (!string.IsNullOrEmpty(key) && !declaredResourceLayers.ContainsKey(key)) declaredResourceLayers.Add(key, layer);
                    continue;
                }
                if (kind == "refp") continue;
                key = PsdResourceNaming.LayerName(layer.Name);
                if (originalResourceLayers.ContainsKey(key)) ambiguousResourceNames.Add(key);
                else originalResourceLayers.Add(key, layer);
                if (layer.IsGroup) CollectResourceLayers(layer.Childs);
            }
        }

        internal string ResolveImageExportName(PsdLayerNode node)
        {
            if (!imageExportNames.TryGetValue(node, out var name))
            {
                ResolveLayerNameLookup();
                if (!imageExportNames.TryGetValue(node, out name)) name = node.ResourceName;
            }
            return name;
        }

        private void ExportSharedReferenceSprites(PsdLayerNode[] nodes)
        {
            sharedSpriteAssets.Clear();
            exportedImagePathsThisRound.Clear();
            ResolveLayerNameLookup(nodes);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (!node.HasReuseReference || !visited.Add(node.ReuseTargetKey)) continue;
                try { GetOrCreateSharedSprite(node, true); }
                catch (Exception ex) { RecordGenerationIssue(node, "shared_image_failed", ex.Message); }
            }
        }
        private static bool EnsureAssetDirectoryExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return false;

            var assetDir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(assetDir)) return false;
            if (Directory.Exists(assetDir)) return true;

            try
            {
                Directory.CreateDirectory(assetDir);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception err)
            {
                Debug.LogError($"创建资源目录失败:{err.Message}\n{assetDir}");
                return false;
            }
        }
        private bool TryEnsureSharedOutputDirectory(out string sharedDir)
        {
            sharedDir = null;
            var configuredPath = UGUIParser.Instance?.SharedAssetsOutput;
            if (string.IsNullOrWhiteSpace(configuredPath)) return false;

            sharedDir = NormalizeAssetDirectory(configuredPath);
            if (string.IsNullOrWhiteSpace(sharedDir)) return false;

            if (!Directory.Exists(sharedDir))
            {
                try
                {
                    Directory.CreateDirectory(sharedDir);
                    AssetDatabase.Refresh();
                }
                catch (Exception err)
                {
                    Debug.LogError($"创建共享资源目录失败:{err.Message}");
                    return false;
                }
            }
            return true;
        }
        private bool TryEnsureSharedPrefabOutputDirectory(out string sharedDir)
        {
            sharedDir = null;
            var configuredPath = UGUIParser.Instance?.SharedPrefabOutput;
            if (string.IsNullOrWhiteSpace(configuredPath)) return false;

            sharedDir = NormalizeAssetDirectory(configuredPath);
            if (string.IsNullOrWhiteSpace(sharedDir)) return false;

            if (!Directory.Exists(sharedDir))
            {
                try
                {
                    Directory.CreateDirectory(sharedDir);
                    AssetDatabase.Refresh();
                }
                catch (Exception err)
                {
                    Debug.LogError($"创建共享Prefab目录失败:{err.Message}");
                    return false;
                }
            }
            return true;
        }
        private string NormalizeAssetDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            var normalized = dir.Replace("\\", "/").Trim();
            if (Path.IsPathRooted(normalized))
            {
                normalized = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, normalized).Replace("\\", "/");
            }
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return normalized.Replace("\\", "/");
        }
        private bool TryGetOrCreateSharedPrefabAsset(PsdLayerNode requester, out GameObject prefabAsset)
        {
            prefabAsset = null;
            if (requester == null || string.IsNullOrWhiteSpace(requester.ReusePrefabKey)) return false;

            var key = requester.ReusePrefabKey;
            if (sharedPrefabAssets.TryGetValue(key, out var cachedPrefab) && cachedPrefab != null)
            {
                prefabAsset = cachedPrefab;
                return true;
            }

            if (requester.TryGetReusePrefabAsset(out prefabAsset) && prefabAsset != null)
            {
                sharedPrefabAssets[key] = prefabAsset;
                return true;
            }

            if (requester.IsAiAuthoritative) return false;

            if (!TryEnsureSharedPrefabOutputDirectory(out var sharedDir))
            {
                Debug.LogWarning($"SharedPrefabOutput未配置, 无法复用prefab:{requester.name}");
                return false;
            }

            var expectedPrefabPath = Path.Combine(sharedDir, key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? key : key + ".prefab").Replace("\\", "/");
            if (!EnsureAssetDirectoryExists(expectedPrefabPath))
            {
                return false;
            }
            if (File.Exists(expectedPrefabPath))
            {
                AssetDatabase.ImportAsset(expectedPrefabPath);
                prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPrefabPath);
                if (prefabAsset != null)
                {
                    sharedPrefabAssets[key] = prefabAsset;
                    return true;
                }

                Debug.LogWarning($"引用prefab文件存在但无法加载: {expectedPrefabPath}");
            }

            if (sharedPrefabExportInProgress.Contains(key))
            {
                Debug.LogWarning($"检测到循环引用prefab, 已跳过导出: {key}");
                return false;
            }

            sharedPrefabExportInProgress.Add(key);
            try
            {
                var prefabRootName = PsdLayerNode.GetReferenceLeafName(key, requester.name);
                prefabAsset = ExportLayerTreeAsPrefab(requester, expectedPrefabPath, prefabRootName, ignoreRootPrefabReference: true);
                if (prefabAsset != null)
                {
                    sharedPrefabAssets[key] = prefabAsset;
                    return true;
                }
                return false;
            }
            finally
            {
                sharedPrefabExportInProgress.Remove(key);
            }
        }

        private GameObject ExportLayerTreeAsPrefab(PsdLayerNode targetLogic, string prefabAssetPath, string prefabRootName, bool ignoreRootPrefabReference)
        {
            if (targetLogic == null) return null;
            if (string.IsNullOrWhiteSpace(prefabAssetPath) || string.IsNullOrWhiteSpace(prefabRootName)) return null;

            RefreshNodesBindLayer();
            Transform exportRoot = targetLogic.transform;
            var layerNodes = exportRoot.GetComponentsInChildren<PsdLayerNode>(true);
            var prefabRefRoots = new List<PsdLayerNode>();
            var rootLayerNode = exportRoot.GetComponent<PsdLayerNode>();
            var prefabRefCandidates = layerNodes.Where(node => node != null && node.HasReusePrefabReference).ToArray();
            if (!ignoreRootPrefabReference && rootLayerNode != null && rootLayerNode.HasReusePrefabReference)
            {
                prefabRefRoots.Add(rootLayerNode);
            }
            else if (prefabRefCandidates.Length > 0)
            {
                foreach (var node in prefabRefCandidates)
                {
                    if (node == null) continue;
                    if (ignoreRootPrefabReference && node == rootLayerNode) continue;
                    bool hasPrefabAncestor = false;
                    var parent = node.transform.parent;
                    while (parent != null && parent != exportRoot)
                    {
                        var parentNode = parent.GetComponent<PsdLayerNode>();
                        if (parentNode != null && parentNode.HasReusePrefabReference && !parentNode.IsAiAuthoritative)
                        {
                            if (!ignoreRootPrefabReference || parentNode != rootLayerNode)
                            {
                                hasPrefabAncestor = true;
                                break;
                            }
                        }
                        parent = parent.parent;
                    }
                    if (!hasPrefabAncestor)
                    {
                        prefabRefRoots.Add(node);
                    }
                }
            }
            prefabRefRoots.Sort(CompareHierarchyOrder);

            bool IsInsidePrefabRef(Transform transform)
            {
                if (transform == null || prefabRefRoots.Count == 0) return false;
                foreach (var prefabRef in prefabRefRoots)
                {
                    if (prefabRef == null) continue;
                    if (transform == prefabRef.transform || !prefabRef.IsAiAuthoritative && transform.IsChildOf(prefabRef.transform)) return true;
                }
                return false;
            }

            var nodesForBuild = layerNodes.Where(node => node != null && !IsInsidePrefabRef(node.transform)).ToArray();
            NormalizePreferredUIHelpersForGeneration(nodesForBuild);
            ExportSharedReferenceSprites(nodesForBuild);

            var uiHelpers = GetAvailableUIHelpers(exportRoot);
            if (uiHelpers != null && uiHelpers.Length > 0 && prefabRefRoots.Count > 0)
            {
                for (int i = uiHelpers.Length - 1; i >= 0; i--)
                {
                    var helper = uiHelpers[i];
                    if (helper == null || helper.LayerNode == null) continue;
                    if (IsInsidePrefabRef(helper.transform))
                    {
                        ArrayUtility.RemoveAt(ref uiHelpers, i);
                    }
                }
            }
            if ((uiHelpers == null || uiHelpers.Length < 1) && prefabRefRoots.Count < 1)
            {
                Debug.LogWarning($"导出prefab失败: 未找到可生成的UI节点:{targetLogic.name}");
                return null;
            }

            var rootHelper = exportRoot.GetComponent<UIHelperBase>();
            bool useTargetAsPrefabRoot = rootHelper != null
                && rootHelper.LayerNode != null
                && rootHelper.LayerNode.IsMainUIType
                && rootHelper.LayerNode.UIType != GUIType.Null;

            return RunPrefabGeneration(
                exportRoot, rootLayerNode, nodesForBuild, uiHelpers, prefabRefRoots,
                rootHelper: useTargetAsPrefabRoot ? rootHelper : null,
                prefabAssetPath: prefabAssetPath,
                prefabRootName: prefabRootName,
                isFullFormExport: false);
        }

        private static void StripManagedGeneratedRootComponents(GameObject target)
        {
            if (target == null) return;

            var components = target.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component == null) continue;

                var type = component.GetType();
                if (type == typeof(Transform)
                    || type == typeof(RectTransform)
                    || type == typeof(CanvasRenderer)) continue;
                if (!IsManagedGeneratedRootComponent(type)) continue;

                DestroyImmediate(component);
            }
        }

        private static bool IsManagedGeneratedRootComponent(Type type)
        {
            return typeof(Selectable).IsAssignableFrom(type)
                || typeof(Graphic).IsAssignableFrom(type)
                || type == typeof(Mask)
                || type == typeof(ScrollRect)
                || type == typeof(CanvasRenderer);
        }
        internal string GetOrCreateSharedSprite(PsdLayerNode requester, bool auto9Slice)
        {
            string key = requester.ReuseTargetKey;
            if (string.IsNullOrEmpty(key)) return null;
            if (!resourceCatalogReady) ResolveLayerNameLookup();
            if (TryGetSharedSpritePath(requester, out var cachedPath)) return cachedPath;
            if (!TryEnsureSharedOutputDirectory(out var sharedDir)) return null;
            var registry = PsdResourceRegistry.Open(sharedDir);
            string existing = registry.Resolve(sharedDir, key, false);
            var resource = registry.Find(key, false);
            if (existing != null && (resource.ownerDocument != Document.documentId || imageUpdateScope != null))
            {
                if (imageUpdateScope != null && resource.ownerDocument == Document.documentId)
                    RecordGenerationIssue(requester, "shared_resource_not_updated", "局部更新保留共享资源。请完整生成拥有此资源的 PSD 以同步所有引用: " + key);
                RegisterSharedSprite(key, existing);
                return existing;
            }
            // An explicit local filename identifies that asset. Never write PNG bytes under
            // another format's extension or silently resolve a different resource.
            if (Array.Exists(PsdResourceNaming.ImageExtensions, extension => key.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                && !key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("引用图片不存在: " + key);
            if (ambiguousResourceNames.Contains(key))
                throw new InvalidOperationException("PSD 中有多个同名资源，无法确定引用目标: " + key);
            if (!originalResourceLayers.TryGetValue(key, out var source)) declaredResourceLayers.TryGetValue(key, out source);
            if (resource != null && resource.sourceLayerId > 0)
            {
                source = null;
                foreach (var candidate in originalResourceLayers.Values)
                    if (candidate.TryGetLayerId(out int id) && id == resource.sourceLayerId) { source = candidate; break; }
                if (source == null)
                    foreach (var candidate in declaredResourceLayers.Values)
                        if (candidate.TryGetLayerId(out int id) && id == resource.sourceLayerId) { source = candidate; break; }
                if (source == null)
                {
                    RecordGenerationIssue(requester, "shared_source_missing", "共享图片的原始图层已移除，保留已生成的资源: " + key);
                    RegisterSharedSprite(key, existing);
                    return existing;
                }
            }
            string exportedPath;
            string contentHash = null;
            int sourceLayerId = 0;
            if (source != null)
            {
                var tree = AiSourceRenderTree.Build(source.Document, new HashSet<PsdLayer> { source });
                var rendered = PsdLayerRenderer.RenderTree(tree, includeHiddenLayers: true, isPreviewRender: false);
                if (rendered == null || rendered.IsEmpty) return null;
                var bytes = PsdTextureAssetUtility.EncodePng(rendered);
                contentHash = ImageHash(bytes);
                source.TryGetLayerId(out sourceLayerId);
                if (existing != null && resource.contentHash == contentHash)
                {
                    RegisterSharedSprite(key, existing);
                    return existing;
                }
                exportedPath = existing ?? sharedDir.TrimEnd('/') + "/" + key + (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "" : ".png");
                if (!EnsureAssetDirectoryExists(exportedPath)) return null;
                File.WriteAllBytes(exportedPath, bytes);
                if (Psd2UIFormSettings.Instance.CompressImage) CompressImageFile(exportedPath);
                AssetDatabase.ImportAsset(exportedPath);
                ConvertTexturesType(new[] { exportedPath }, true, rendered.IsHighBitDepth);
                if (auto9Slice) ApplySpriteNineSlice(exportedPath);
            }
            else
            {
                // A ref marker is an explicit shared-resource declaration. With no existing asset,
                // export one canonical occurrence, even when every occurrence carries the marker.
                var target = layerLookupByName.TryGetValue(key, out var named) && named != null ? named : requester;
                exportedPath = target.ExportImageAsset(true, sharedDir, key, false, auto9Slice, ignoreReference: true);
            }
            if (!string.IsNullOrEmpty(exportedPath))
            {
                registry.Bind(key, false, exportedPath, Document.documentId, sourceLayerId, contentHash);
                RegisterSharedSprite(key, exportedPath);
            }
            return exportedPath;
        }
        internal bool TryGetSharedSpritePath(PsdLayerNode node, out string assetPath)
        {
            assetPath = null;
            if (node == null) return false;
            string key = node.HasReuseReference ? node.ReuseTargetKey : node.ResourceName;
            if (!node.HasReuseReference && (!referencedLayerKeys.Contains(key)
                || !layerLookupByName.TryGetValue(key, out var owner) || owner != node)) return false;
            if (sharedSpriteAssets.TryGetValue(key, out var cachedPath))
            {
                if (File.Exists(cachedPath))
                {
                    assetPath = cachedPath.Replace("\\", "/");
                    return true;
                }
                sharedSpriteAssets.Remove(key);
            }
            return false;
        }
        internal bool TryOverrideSharedOutput(PsdLayerNode node, out string exportDir, out string fileName)
        {
            exportDir = null;
            fileName = null;
            if (node == null) return false;
            // 键与 referencedLayerKeys 的写入侧(ReuseTargetKey)保持一致,否则引用节点必判空
            var key = node.HasReuseReference && !string.IsNullOrWhiteSpace(node.ReuseTargetKey)
                ? node.ReuseTargetKey
                : node.LayerNameLookupKey;
            if (string.IsNullOrEmpty(key) || !referencedLayerKeys.Contains(key)
                || !layerLookupByName.TryGetValue(key, out var owner) || owner != node) return false;
            if (!TryEnsureSharedOutputDirectory(out var sharedDir)) return false;
            exportDir = sharedDir;
            fileName = key;
            return true;
        }
        internal void RegisterSharedSprite(string key, string assetPath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(assetPath)) return;
            sharedSpriteAssets[key] = assetPath.Replace("\\", "/");
            RegisterExportedImagePath(assetPath);
        }
        internal PsdLayerNode FindLayerNodeByKey(string key)
        {
            ResolveLayerNameLookup();
            if (string.IsNullOrEmpty(key)) return null;
            layerLookupByName.TryGetValue(key, out var node);
            return node;
        }

        private void FitGeneratedContainerHierarchy(Transform root)
        {
            if (root == null) return;
            if (IsGeneratedOrphansRoot(root)) return;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null) continue;

                FitGeneratedContainerHierarchy(child);
            }

            if (!IsGeneratedContainer(root)) return;

            FitGeneratedContainerToDirectChildren(root as RectTransform);
        }

        private static bool IsGeneratedContainer(Transform target)
        {
            if (target == null) return false;
            var context = GenerationContext.Current;
            return context != null && context.GeneratedContainerNodes.Contains(target.gameObject);
        }

        private static void FitGeneratedContainerToDirectChildren(RectTransform container)
        {
            if (container == null) return;

            var childRects = new List<RectTransform>();
            for (int i = 0; i < container.childCount; i++)
            {
                var childRect = container.GetChild(i) as RectTransform;
                if (childRect == null) continue;

                NormalizeAbsoluteChildRectTransform(childRect);
                childRects.Add(childRect);
            }

            if (childRects.Count < 1) return;
            if (!TryGetCombinedWorldRect(childRects, out var worldRect)) return;

            var childWorldPositions = new Vector3[childRects.Count];
            for (int i = 0; i < childRects.Count; i++)
            {
                childWorldPositions[i] = childRects[i].position;
            }

            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0.5f, 0.5f);
            ApplyWorldRect(container, worldRect);

            for (int i = 0; i < childRects.Count; i++)
            {
                childRects[i].position = childWorldPositions[i];
            }
        }

        private static void NormalizeAbsoluteChildRectTransform(RectTransform rectTransform)
        {
            if (rectTransform == null) return;
            if (!TryGetWorldRect(rectTransform, out var worldRect)) return;

            if (Quaternion.Angle(rectTransform.rotation, Quaternion.identity) <= 0.001f)
            {
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ApplyWorldRect(rectTransform, worldRect);
                return;
            }

            // Keep the actual local size and world rotation. Applying the AABB
            // dimensions here would enlarge a rotated text element and make the
            // next container-fit pass permanently diverge from the PSD layout.
            Vector2 localSize = rectTransform.rect.size;
            Vector3 worldPosition = rectTransform.position;
            Quaternion worldRotation = rectTransform.rotation;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, localSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, localSize.y);
            rectTransform.position = worldPosition;
            rectTransform.rotation = worldRotation;
        }

        private static bool TryGetCombinedWorldRect(List<RectTransform> rectTransforms, out Rect worldRect)
        {
            worldRect = default;
            if (rectTransforms == null || rectTransforms.Count < 1) return false;

            bool hasRect = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            for (int i = 0; i < rectTransforms.Count; i++)
            {
                if (!TryGetWorldRect(rectTransforms[i], out var rect)) continue;

                if (!hasRect)
                {
                    minX = rect.xMin;
                    minY = rect.yMin;
                    maxX = rect.xMax;
                    maxY = rect.yMax;
                    hasRect = true;
                    continue;
                }

                minX = Mathf.Min(minX, rect.xMin);
                minY = Mathf.Min(minY, rect.yMin);
                maxX = Mathf.Max(maxX, rect.xMax);
                maxY = Mathf.Max(maxY, rect.yMax);
            }

            if (!hasRect) return false;

            worldRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryGetWorldRect(RectTransform rectTransform, out Rect worldRect)
        {
            worldRect = default;
            if (rectTransform == null) return false;
            // Match source bounds: unresolved/empty children must not enlarge generated containers.
            var localRect = rectTransform.rect;
            if (localRect.width <= 0f || localRect.height <= 0f) return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            float minX = corners[0].x;
            float minY = corners[0].y;
            float maxX = corners[0].x;
            float maxY = corners[0].y;
            for (int i = 1; i < corners.Length; i++)
            {
                var corner = corners[i];
                minX = Mathf.Min(minX, corner.x);
                minY = Mathf.Min(minY, corner.y);
                maxX = Mathf.Max(maxX, corner.x);
                maxY = Mathf.Max(maxY, corner.y);
            }

            worldRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private static void ApplyWorldRect(RectTransform rectTransform, Rect worldRect)
        {
            if (rectTransform == null) return;

            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, worldRect.width);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, worldRect.height);

            Vector2 actualSize = rectTransform.rect.size;
            Vector2 pivotOffset = (rectTransform.pivot - Vector2.one * 0.5f) * actualSize;
            rectTransform.position = new Vector3(
                worldRect.center.x + pivotOffset.x,
                worldRect.center.y + pivotOffset.y,
                rectTransform.position.z);
        }
        private UIHelperBase[] GetAvailableUIHelpers(Transform root)
        {
            var uiHelpers = root.GetComponentsInChildren<UIHelperBase>();
            uiHelpers = uiHelpers.Where(ui => ui != null && ui.LayerNode != null && ui.LayerNode.IsMainUIType).ToArray();
            for (int i = 0; i < uiHelpers.Length; i++)
            {
                if (uiHelpers[i] != null)
                {
                    try { uiHelpers[i].ParseAndAttachUIElements(); }
                    catch (Exception ex) { failedGenerationHelpers.Add(uiHelpers[i]); RecordGenerationIssue(uiHelpers[i].LayerNode, "helper_parse_failed", ex.Message); }
                }
            }
            uiHelpers = root.GetComponentsInChildren<UIHelperBase>();
            uiHelpers = uiHelpers.Where(ui => ui != null && ui.LayerNode != null && ui.LayerNode.IsMainUIType).ToArray();
            ResolveDependencyClaims(uiHelpers);

            var collapsedRoots = new List<Transform>();
            var layerNodes = root.GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var layerNode in layerNodes)
            {
                if (layerNode != null && layerNode.CollapseChildrenForGeneration)
                {
                    collapsedRoots.Add(layerNode.transform);
                }
            }

            var dependInstIds = new HashSet<
#if UNITY_6000_3_OR_NEWER
                EntityId
#else
                int
#endif
            >();
            foreach (var item in uiHelpers)
            {
                if (item == null) continue;

                if (failedGenerationHelpers.Contains(item)) continue;
                PsdLayerNode[] dependencies;
                try { dependencies = item.GetDependencies(); }
                catch (Exception ex) { failedGenerationHelpers.Add(item); RecordGenerationIssue(item.LayerNode, "helper_dependencies_failed", ex.Message); continue; }
                if (dependencies == null) continue;

                foreach (var depend in dependencies)
                {
                    if (depend == null) continue;

                    var dependId = GetObjectId(depend.gameObject);
                    dependInstIds.Add(dependId);
                    if (depend.CollapseChildrenForGeneration)
                    {
                        collapsedRoots.Add(depend.transform);
                    }
                }
            }
            for (int i = uiHelpers.Length - 1; i >= 0; i--)
            {
                var uiHelper = uiHelpers[i];
                if (uiHelper == null)
                {
                    ArrayUtility.RemoveAt(ref uiHelpers, i);
                    continue;
                }

                if (dependInstIds.Contains(GetObjectId(uiHelper.gameObject))
                    || IsChildOfCollapsedRoot(uiHelper.transform, collapsedRoots))
                {
                    ArrayUtility.RemoveAt(ref uiHelpers, i);
                }
            }
            Array.Sort(uiHelpers, CompareHierarchyOrder);
            return uiHelpers;
        }

        private void NormalizePreferredUIHelpersForGeneration(PsdLayerNode[] layerNodes)
        {
            failedGenerationHelpers.Clear();
            pendingGenerationIssues.Clear();
            var parser = UGUIParser.Instance;
            if (parser == null || layerNodes == null || layerNodes.Length == 0) return;

            for (int i = 0; i < layerNodes.Length; i++)
            {
                var node = layerNodes[i];
                if (node == null) continue;

                if (node.IsAiAuthoritative) continue;
                var preferredType = node.ResolvePreferredType(node.UIType);
                if (preferredType == node.UIType) continue;

                node.SetUIType(preferredType, false);
            }

            ApplyGroupDerivedStates(logOpacityConflicts: true);

            for (int i = 0; i < layerNodes.Length; i++)
            {
                var node = layerNodes[i];
                if (node == null) continue;
                try { node.RefreshUIHelper(false); }
                catch (Exception ex)
                {
                    var helper = node.GetComponent<UIHelperBase>();
                    if (helper != null) failedGenerationHelpers.Add(helper);
                    RecordGenerationIssue(node, "helper_refresh_failed", ex.Message);
                }
            }
        }

        private void ResolveDependencyClaims(UIHelperBase[] uiHelpers)
        {
            if (uiHelpers == null || uiHelpers.Length < 1) return;

            var dependencyOwnerIds = new Dictionary<PsdObjectId, HashSet<PsdObjectId>>();
            for (int i = 0; i < uiHelpers.Length; i++)
            {
                var uiHelper = uiHelpers[i];
                if (uiHelper == null) continue;

                if (failedGenerationHelpers.Contains(uiHelper)) continue;
                PsdLayerNode[] dependencies;
                try { dependencies = uiHelper.GetDependencies(); }
                catch (Exception ex) { failedGenerationHelpers.Add(uiHelper); RecordGenerationIssue(uiHelper.LayerNode, "helper_dependencies_failed", ex.Message); continue; }
                if (dependencies == null) continue;

#if UNITY_6000_3_OR_NEWER
                var ownerId = uiHelper.GetEntityId();
#else
                int ownerId = uiHelper.GetInstanceID();
#endif
                for (int j = 0; j < dependencies.Length; j++)
                {
                    var dependency = dependencies[j];
                    if (dependency == null) continue;

                    var dependencyId = UIHelperBase.GetDependencyClaimId(dependency);
                    if (dependencyId == default(PsdObjectId)) continue;

                    if (!dependencyOwnerIds.TryGetValue(dependencyId, out var ownerIds))
                    {
                        ownerIds = new HashSet<PsdObjectId>();
                        dependencyOwnerIds.Add(dependencyId, ownerIds);
                    }
                    ownerIds.Add(ownerId);
                }
            }

            for (int i = 0; i < uiHelpers.Length; i++)
            {
                var helper = uiHelpers[i];
                if (helper == null || failedGenerationHelpers.Contains(helper)) continue;
                try { helper.ResolveDependencyClaims(dependencyOwnerIds); }
                catch (Exception ex) { failedGenerationHelpers.Add(helper); RecordGenerationIssue(helper.LayerNode, "helper_claims_failed", ex.Message); }
            }
        }

        private static int CompareHierarchyOrder(UIHelperBase left, UIHelperBase right)
        {
            return CompareHierarchyOrder(left != null ? left.transform : null, right != null ? right.transform : null);
        }

        private static int CompareHierarchyOrder(PsdLayerNode left, PsdLayerNode right)
        {
            return CompareHierarchyOrder(left != null ? left.transform : null, right != null ? right.transform : null);
        }

        private static int CompareHierarchyOrder(Transform left, Transform right)
        {
            if (left == right) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var leftPath = BuildHierarchySiblingPath(left);
            var rightPath = BuildHierarchySiblingPath(right);
            int count = Mathf.Min(leftPath.Count, rightPath.Count);
            for (int i = 0; i < count; i++)
            {
                int compare = leftPath[i].CompareTo(rightPath[i]);
                if (compare != 0) return compare;
            }
            return leftPath.Count.CompareTo(rightPath.Count);
        }

        private static List<int> BuildHierarchySiblingPath(Transform transform)
        {
            var result = new List<int>(8);
            while (transform != null)
            {
                result.Insert(0, transform.GetSiblingIndex());
                transform = transform.parent;
            }
            return result;
        }

        private static bool IsChildOfCollapsedRoot(Transform candidate, List<Transform> collapsedRoots)
        {
            if (candidate == null || collapsedRoots == null || collapsedRoots.Count == 0) return false;

            for (int i = 0; i < collapsedRoots.Count; i++)
            {
                var collapsedRoot = collapsedRoots[i];
                if (collapsedRoot == null || candidate == collapsedRoot) continue;
                if (candidate.IsChildOf(collapsedRoot)) return true;
            }

            return false;
        }
        /// <summary>
        /// 把图片设置为为Sprite或Texture类型
        /// </summary>
        /// <param name="dir"></param>
        internal static void ConvertTexturesType(string[] texAssets, bool isImage = true, bool preferHighBitDepth = false)
        {
            foreach (var item in texAssets)
            {
                var texImporter = AssetImporter.GetAtPath(item) as TextureImporter;
                if (texImporter == null)
                {
                    Debug.LogError($"TextureImporter为空:{item}");
                    continue;
                }
                if (isImage)
                {
                    texImporter.textureType = TextureImporterType.Sprite;
                    texImporter.spriteImportMode = SpriteImportMode.Single;
                    texImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                    texImporter.alphaIsTransparency = true;
                    texImporter.mipmapEnabled = false;
                    texImporter.npotScale = TextureImporterNPOTScale.None;
                }
                else
                {
                    texImporter.textureType = TextureImporterType.Default;
                    texImporter.textureShape = TextureImporterShape.Texture2D;
                    texImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                    texImporter.alphaIsTransparency = true;
                    texImporter.mipmapEnabled = false;
                    texImporter.npotScale = TextureImporterNPOTScale.None;
                }

                // 默认 maxTextureSize=2048 会把超过它的图层降采样，尺寸与九宫边框都跟 PSD 对不上。
                texImporter.maxTextureSize = Math.Max(texImporter.maxTextureSize, 16384);

                texImporter.sRGBTexture = true;
                texImporter.textureCompression = TextureImporterCompression.Uncompressed;
                PsdTextureAssetUtility.ApplyPrecisionImportSettings(texImporter, preferHighBitDepth);
                texImporter.SaveAndReimport();
            }
        }
        internal static void ApplySpriteNineSlice(string spriteAssetPath)
        {
            var texImporter = AssetImporter.GetAtPath(spriteAssetPath) as TextureImporter;
            if (texImporter == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spriteAssetPath);
                var spriteAsset = PsdLayerNode.LoadSpriteAssetAtPath(spriteAssetPath);
                if (texture == null || spriteAsset == null || spriteAsset.border != Vector4.zero)
                {
                    return;
                }

                var border = UGUIParser.CalculateTexture9SliceBorder(texture);
                if (border == Vector4.zero)
                {
                    return;
                }

                PsdLayerNode.TryReplaceSpriteSubAsset(spriteAssetPath, texture, border, out _);
                return;
            }
            bool revertReadable = !texImporter.isReadable;
            if (revertReadable)
            {
                texImporter.isReadable = true;
                texImporter.SaveAndReimport();
                texImporter = AssetImporter.GetAtPath(spriteAssetPath) as TextureImporter;
            }
            if (texImporter == null)
            {
                return;
            }
            var sprite = PsdLayerNode.LoadSpriteAssetAtPath(spriteAssetPath);
            if (sprite != null && texImporter.spriteBorder == Vector4.zero)
            {
                texImporter.spriteBorder = UGUIParser.CalculateTexture9SliceBorder(sprite.texture);
                texImporter.SaveAndReimport();
            }
            if (revertReadable)
            {
                texImporter = AssetImporter.GetAtPath(spriteAssetPath) as TextureImporter;
                if (texImporter != null)
                {
                    texImporter.isReadable = false;
                    texImporter.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// 压缩图片文件
        /// </summary>
        /// <param name="asset">文件名(相对路径Assets)</param>
        /// <returns></returns>
        internal static bool CompressImageFile(string asset)
        {
#if !EFUN_PRIVATE
            return false;
#else
            var assetPath = asset.StartsWith("Assets/") ? PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, asset) : asset;
            var compressTool = Utility.Assembly.GetType("UGF.EditorTools.CompressTool");
            if (compressTool == null) return false;

            var compressMethod = compressTool.GetMethod("CompressImageOffline", new Type[] { typeof(string), typeof(string) });
            if (compressMethod == null) return false;

            return (bool)compressMethod.Invoke(null, new object[] { assetPath, assetPath });
#endif
        }
        /// <summary>
        /// 获取UIForm对应的图片导出目录
        /// </summary>
        /// <returns></returns>
        internal string GetUIFormImagesOutputDir()
        {
            // 这里必须走 NormalizeAssetDirectory：图片写到 Assets 之外时 Unity 根本不导入，
            // 结果是整屏白图却没有一句错误。
            var imagesRoot = Psd2UIFormSettings.Instance.UIImagesOutputDir;
            if (string.IsNullOrWhiteSpace(imagesRoot))
            {
                Debug.LogError("图片输出目录未配置：Settings 里的 UIImagesOutputDir 为空。");
                return null;
            }

            string formName = uiFormName ?? string.Empty;
            string combined = imagesRoot.TrimEnd('/', '\\');
            if (!string.IsNullOrEmpty(formName)) combined += "/" + formName;
            string normalized = NormalizeAssetDirectory(combined);
            if (normalized == null)
                Debug.LogError($"图片输出目录必须位于工程 Assets 内，否则 Unity 不会导入这些贴图: {combined}");
            return normalized;
        }

    }
    internal static class PathExtensions
    {
        internal static string GetRelativePath(string relativeTo, string path)
        {
#if UNITY_2021_2_OR_NEWER
            return Path.GetRelativePath(relativeTo, path);
#else
            relativeTo = Path.GetFullPath(relativeTo.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            path = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            if (string.Equals(relativeTo.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar),
                Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return ".";
            }
            Uri fromUri = new Uri(Path.GetFullPath(relativeTo));
            Uri toUri = new Uri(Path.GetFullPath(path));

            if (fromUri.Scheme != toUri.Scheme)
            {
                return path;
            }

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            if (relativeUri.IsAbsoluteUri) return path;
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
#endif
        }
    }
}
#endif
