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
using UnityEngine;
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
        GUIContent normalizeStructureBt;
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
            parsePsd2NodesBt = new GUIContent("解析psd图层", "把psd图层解析为可编辑节点树");
            exportUISpritesBt = new GUIContent("导出Images", "导出勾选的psd图层为碎图");
            aiAutoFixBt = new GUIContent("AI自动识别UI类型", "导出当前节点树和预览图，调用AI自动识别并修正UI类型与结构");
            applyAiResultBt = new GUIContent("应用AI结果", "应用当前 AI 识别数据 到节点树");
            normalizeStructureBt = new GUIContent("修正树结构", "按本地 owner 规则修正层级，并刷新主控件对子控件的引用");
            generateUIFormBt = new GUIContent("生成UIForm", "根据解析后的节点树生成UIForm Prefab");
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
                // ====== Fatcat定制: 目标引擎选择下拉框(保留CocosStudio导出) ======
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("目标渲染引擎:", EditorStyles.boldLabel, GUILayout.Width(150));
                Psd2UIFormSettings.Instance.TargetEngine = (ExportTargetEngine)EditorGUILayout.EnumPopup(Psd2UIFormSettings.Instance.TargetEngine);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
                // =================================================================
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
                    if (TryGetKeepExistingUITypeSelection(out var keepExistingUIType))
                    {
                        Psd2UIFormConverter.ParsePsd2LayerPrefab(targetLogic.PsdAssetName, targetLogic, keepExistingUIType);
                    }
                }
                if (GUILayout.Button(exportUISpritesBt, btHeight))
                {
                    targetLogic.ExportSprites();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(aiAutoFixBt, btHeight))
                {
                    if (!AiAutoFixWorkflow.Start(targetLogic, out var aiError))
                    {
                        EditorUtility.DisplayDialog("AI修正启动失败", aiError, "确定");
                    }
                }
                if (GUILayout.Button(applyAiResultBt, btHeight))
                {
                    if (!AiAutoFixWorkflow.ApplyCurrentResult(targetLogic, out var applyError))
                    {
                        EditorUtility.DisplayDialog("应用AI结果失败", applyError, "确定");
                    }
                }
                if (GUILayout.Button(normalizeStructureBt, btHeight))
                {
                    if (!targetLogic.NormalizeLocalStructure(true, out var summary))
                    {
                        EditorUtility.DisplayDialog("本地归一化失败", summary, "确定");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("本地归一化完成", summary, "确定");
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

        private static bool TryGetKeepExistingUITypeSelection(out bool keepExistingUIType)
        {
            const string title = "重新解析PSD图层";
            const string message = "重新解析会重建当前节点树。\n\n是否保留现有UI类型？\n\n选择“是”会按当前节点层级尽量回填已有 UI Type，新图层仍按默认规则初始化。\n选择“否”会按PSD图层标记重新初始化 UI Type。";
            int option = EditorUtility.DisplayDialogComplex(title, message, "是，保留现有UI类型", "否，重新解析UI类型", "取消");
            keepExistingUIType = option == 0;
            return option != 2;
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
            float innerWidth = Mathf.Max(80f, width - style.padding.horizontal);
            float labelWidth = Mathf.Max(60f, innerWidth - buttonWidth - WarningIconSize - WarningIconSpacing - 8f);
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

            Rect iconRect = new Rect(
                contentRect.x,
                contentRect.y + Mathf.Max(0f, (contentRect.height - WarningIconSize) * 0.5f),
                WarningIconSize,
                WarningIconSize);

            Rect labelRect = new Rect(
                iconRect.xMax + WarningIconSpacing,
                contentRect.y,
                Mathf.Max(1f, buttonRect.x - iconRect.xMax - WarningIconSpacing - 8f),
                contentRect.height);

            GUI.Label(iconRect, warningIconContent);
            GUI.Label(labelRect, content, style);
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
    public sealed class Psd2UIFormConverter : MonoBehaviour
    {
        const string RecordLayerOperation = "Change Export Image";
        const string GeneratedContainerTypeKey = "__Container";
        const string GeneratedPrefabReferenceTypeKey = "__PrefabRef";
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
        [HideInInspector][SerializeField] private string slotRootPath; // Fatcat定制: Slot根目录(供Prefab导出默认路径)
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Header("Debug:")][SerializeField] bool drawLayerRectGizmos = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] UnityEngine.Color drawLayerRectGizmosColor = UnityEngine.Color.gray;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("Scene点选时，优先选中命中区域内面积最小的图层节点")][SerializeField] bool preferSmallestLayerOnScenePick = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private List<GeneratedMetadataSerializedEntry> generatedMetadataEntries = new List<GeneratedMetadataSerializedEntry>();
        private PsdDocument psdInstance;//psd文件解析实例
        private GUIStyle uiTypeLabelStyle;
        private readonly Dictionary<string, PsdLayerNode> layerLookupByName = new Dictionary<string, PsdLayerNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sharedSpriteAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        private sealed class GeneratedMetadataEntry
        {
            public string GlobalObjectId;
            public string Key;
            public string TypeKey;
            public bool IsContainer;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class GeneratedMetadataSerializedEntry
        {
            public string PrefabAssetPath;
            [HideInInspector] public string Json;
            public List<GeneratedMetadataEntry> Entries = new List<GeneratedMetadataEntry>();
        }

        private sealed class GeneratedNodeSnapshot
        {
            public string Key;
            public string TypeKey;
            public bool IsContainer;
            public List<int> TransformPath;
        }
        private sealed class LayerUITypeSnapshot
        {
            public GUIType UIType;
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
            SceneView.duringSceneGui += OnSceneGUI;
#if UNITY_6000_0_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
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
#if UNITY_6000_0_OR_NEWER
            EntityId entityId
#else
            int instanceId
#endif
        )
        {
#if UNITY_6000_0_OR_NEWER
            return EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
        }

        private static
#if UNITY_6000_0_OR_NEWER
            EntityId
#else
            int
#endif
            GetObjectId(UnityEngine.Object obj)
        {
#if UNITY_6000_0_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        private void OnHierarchyGUI(
#if UNITY_6000_0_OR_NEWER
            EntityId entityId,
#else
            int instanceID,
#endif
            Rect selectionRect)
        {
            if (Event.current == null) return;
            var node = HierarchyIdToGameObject(
#if UNITY_6000_0_OR_NEWER
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
            var selectLayerNodes = selects.Where(item => item?.GetComponent<PsdLayerNode>() != null).ToArray();
            foreach (var layer in selectLayerNodes)
            {
                layer.GetComponent<PsdLayerNode>().markToExport = exportImg;
            }
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
#if UNITY_6000_0_OR_NEWER
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

            // Fatcat定制: 打开/刷新时若显示图丢失则重新加载(内存Sprite无法序列化进prefab, 沿用旧版行为)
            if (this.psdAsset == null && !string.IsNullOrWhiteSpace(PsdAssetName))
            {
                this.psdAsset = LoadPsdSpriteAsset(PsdAssetName);
                if (!IsPsbSourceDocument(PsdAssetName))
                {
                    this.previewSprite = null;
                }
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
        internal bool CheckPsdAssetHasChanged()
        {
            var assetPath = PsdAssetName;
            if (string.IsNullOrWhiteSpace(assetPath)) return false;
            var fileTag = GetAssetChangeTag(assetPath);
            return psdAssetChangeTime.CompareTo(fileTag) != 0;
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
#if UNITY_2021_1_OR_NEWER
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
        internal static bool ParsePsd2LayerPrefab(string psdFile, Psd2UIFormConverter instanceRoot = null, bool keepExistingUIType = false, bool openPrefabAfterSave = true)
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
                instanceRoot.SetPsdAsset(psdFile); // Fatcat定制: re-parse也刷新PSD绑定与slotRootPath
                return ParsePsdLayer2Root(psdFile, instanceRoot, keepExistingUIType);
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
                if (!ParsePsdLayer2Root(psdFile, rootLayer, keepExistingUIType))
                {
                    if (needDestroyInstance) GameObject.DestroyImmediate(rootLayer.gameObject);
                    return false;
                }

                rootLayer.gameObject.name = System.IO.Path.GetFileNameWithoutExtension(prefabFile);
                PrefabUtility.SaveAsPrefabAsset(rootLayer.gameObject, prefabFile, out bool savePrefabSuccess);
                if (needDestroyInstance) GameObject.DestroyImmediate(rootLayer.gameObject);
                AssetDatabase.Refresh();
                var currentStagePath = StageUtility.GetCurrentStage()?.assetPath;
                bool isSameStagePrefab = !string.IsNullOrWhiteSpace(currentStagePath)
                    && AssetDatabase.GUIDFromAssetPath(currentStagePath) == AssetDatabase.GUIDFromAssetPath(prefabFile);
                if (savePrefabSuccess && openPrefabAfterSave && !isSameStagePrefab)
                {
                    OpenPrefab(prefabFile);
                }

                return savePrefabSuccess;
            }
        }
        private static bool ParsePsdLayer2Root(string psdFile, Psd2UIFormConverter converter, bool keepExistingUIType = false)
        {
            EditorUtility.DisplayProgressBar("解析PSD", $"正在解析{psdFile}", 0);
            var preservedUITypes = keepExistingUIType ? converter.CaptureLayerUITypeSnapshot() : null;

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

                    //清空已有节点重新解析
                    for (int i = converter.transform.childCount - 1; i >= 0; i--)
                    {
                        GameObject.DestroyImmediate(converter.transform.GetChild(i).gameObject);
                    }

                    int totalCount = psd.CountAllLayers();
                    int parsedCount = 0;
                    int nextBindIndex = 0;
                    for (int i = 0; i < rootLayers.Length; i++)
                    {
                        var rootLayer = rootLayers[i] as PsdLayer;
                        if (rootLayer == null) continue;
                        ParsePsdLayerRecursive(rootLayer, converter.transform, ref nextBindIndex, ref parsedCount, totalCount);
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
                if (keepExistingUIType)
                {
                    converter.RestoreLayerUITypeSnapshot(preservedUITypes);
                }
                converter.ApplyGroupDerivedStates();
                var childrenNodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                foreach (var item in childrenNodes)
                {
                    item.RefreshUIHelper(false);
                }
                EditorUtility.SetDirty(converter.gameObject);
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
            // Fatcat定制: 自动检测 Slot 根目录(供 Prefab 导出默认路径)
            var psdDir = Path.GetDirectoryName(psdAssetPath)?.Replace("\\", "/");
            this.slotRootPath = DetectSlotRoot(psdDir);
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
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is UnityEngine.Sprite subSprite) return subSprite;
                }
            }

            // Fatcat定制: 回退——PSD未按Sprite导入时, 用拼合纹理临时创建Sprite供场景显示(沿用旧版行为)
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(psdFile);
            if (tex != null)
            {
                var fallbackSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                fallbackSprite.name = tex.name;
                return fallbackSprite;
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
                var texture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA64, false, false);
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
            nodeName = UGUIParser.Instance.GetSafeLayerName(nodeName, bindLayerIdx);
            var node = new GameObject(nodeName, typeof(RectTransform));
            node.gameObject.tag = "EditorOnly";
            node.transform.localPosition = Vector3.zero;
            node.transform.localRotation = Quaternion.identity;
            node.transform.localScale = Vector3.one;
            var layerNode = node.AddComponent<PsdLayerNode>();
            layerNode.BindPsdLayerIndex = bindLayerIdx;
            InitLayerNodeData(layerNode, layer);
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
            if (UGUIParser.Instance.TryParse(layerNode, out var initRule))
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
            if (nodes == null || nodes.Length == 0) return;

            foreach (var node in nodes)
            {
                if (node == null) continue;

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
                        node.SetGroupGenerationState(true, textSource.BindPsdLayerIndex);
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

                if (descendant.HasReusePrefabReference || IsBehavioralGroupOpacityType(descendant.UIType))
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

        internal bool NormalizeLocalStructure(bool registerUndo, out string summary)
        {
            summary = "未执行本地归一化。";
            if (!PsdStructureNormalizer.Normalize(this, registerUndo, out var report))
            {
                summary = report != null ? report.BuildSummary() : "本地归一化失败。";
                return false;
            }

            summary = report != null ? report.BuildSummary() : "本地归一化完成。";
            if (report != null)
            {
                for (int i = 0; i < report.Operations.Count; i++)
                {
                    Debug.Log($"[PSD2UIForm.Normalize] {report.Operations[i]}");
                }
                for (int i = 0; i < report.Warnings.Count; i++)
                {
                    Debug.LogWarning("[PSD2UIForm.Normalize] " + report.Warnings[i]);
                }
            }

            return true;
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
            ResolveLayerNameLookup();
            var exportLayers = this.GetComponentsInChildren<PsdLayerNode>().Where(node => node.NeedExportImage());
            var exportDir = GetUIFormImagesOutputDir();
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }
            int exportIdx = 0;
            int totalCount = exportLayers.Count();
            foreach (var layer in exportLayers)
            {
                var assetName = layer.ExportImageAsset();
                if (assetName == null)
                {
                    Debug.LogWarning($"导出图层[name:{layer.name}, layerIdx:{layer.BindPsdLayerIndex}]图片失败!");
                }
                ++exportIdx;
                EditorUtility.DisplayProgressBar($"导出进度({exportIdx}/{totalCount})", $"导出UI图片:{assetName}", exportIdx / (float)totalCount);
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
        /// <summary>
        /// 根据解析后的节点树生成UIForm Prefab
        /// </summary>
        internal void GenerateUIForm(PsdLayerNode exportNode = null)
        {
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
            // ====== Fatcat定制: CocosStudio导出分支 ======
            if (Psd2UIFormSettings.Instance.TargetEngine == ExportTargetEngine.CocosStudio)
            {
                if (Psd2UIFormSettings.Instance.UseUIFormOutputDir)
                {
                    CocosStudioExporter.ExportToCsd(this, Psd2UIFormSettings.Instance.UIFormOutputDir);
                }
                else
                {
                    var cocosDefaultDir = !string.IsNullOrWhiteSpace(slotRootPath) ? slotRootPath + "/Prefab/" : "Assets";
                    string cocosLastDir = string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.LastUIFormOutputDir) ? cocosDefaultDir : Psd2UIFormSettings.Instance.LastUIFormOutputDir;
                    string cocosSelectDir = EditorUtility.SaveFolderPanel("保存目录", cocosLastDir, null);
                    if (!string.IsNullOrWhiteSpace(cocosSelectDir))
                    {
                        if (!cocosSelectDir.StartsWith("Assets/"))
                            cocosSelectDir = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, cocosSelectDir);
                        Psd2UIFormSettings.Instance.LastUIFormOutputDir = cocosSelectDir;
                        CocosStudioExporter.ExportToCsd(this, cocosSelectDir);
                    }
                }
                return;
            }
            // ==============================================
            if (Psd2UIFormSettings.Instance.UseUIFormOutputDir)
            {
                ExportUIPrefab(rootNode, Psd2UIFormSettings.Instance.UIFormOutputDir);
            }
            else
            {
                var uguiDefaultDir = !string.IsNullOrWhiteSpace(slotRootPath) ? slotRootPath + "/Prefab/" : "Assets"; // Fatcat定制: 默认Slot根目录/Prefab/
                string lastSaveDir = string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.LastUIFormOutputDir) ? uguiDefaultDir : Psd2UIFormSettings.Instance.LastUIFormOutputDir;
                string selectDir = EditorUtility.SaveFolderPanel("保存目录", lastSaveDir, null);
                if (!string.IsNullOrWhiteSpace(selectDir))
                {
                    if (!selectDir.StartsWith("Assets/"))
                        selectDir = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, selectDir);
                    Psd2UIFormSettings.Instance.LastUIFormOutputDir = selectDir;
                    ExportUIPrefab(rootNode, selectDir);
                }
            }
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
        private bool ExportUIPrefab(Transform root, string outputDir)
        {
            // Fatcat定制: Slot 根目录下的 Prefab/ 作为默认输出目录(允许设置或选择器覆盖)
            if (string.IsNullOrWhiteSpace(outputDir) && !string.IsNullOrWhiteSpace(slotRootPath))
            {
                outputDir = slotRootPath + "/Prefab/";
            }
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
            // Fatcat定制: 遵循 prefab_slotXXXX_xxx 命名规范(去 slot_ 下划线, 加 prefab_ 前缀)
            var baseName = System.Text.RegularExpressions.Regex.Replace(uiFormName, @"^[Ss]lot_(\d+)", "slot$1");
            var prefabFileName = baseName.StartsWith("prefab_", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : "prefab_" + baseName;
            var prefabName = Path.Combine(outputDir, $"{prefabFileName}.prefab").Replace("\\", "/");
            // Fatcat定制: 标准化目录路径(兼容软链接映射), 再拼接文件名
            var prefabDir = Path.GetDirectoryName(prefabName).Replace("\\", "/");
            prefabName = NormalizeToAssetPath(prefabDir) + "/" + Path.GetFileName(prefabName);
            if (root == this.transform && File.Exists(prefabName))
            {
                int option = EditorUtility.DisplayDialogComplex(
                    "警告",
                    $"prefab文件已存在, 请选择生成方式:{prefabName}",
                    "覆盖生成(不丢失引用)",
                    "取消",
                    "重新生成");
                if (option == 2)
                {
                    if (!AssetDatabase.DeleteAsset(prefabName))
                    {
                        Debug.LogError($"重新生成UIForm失败: 删除旧prefab失败:{prefabName}");
                        return false;
                    }
                    AssetDatabase.Refresh();
                }
                else if (option == 1)
                {
                    return false;
                }
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
                        if (parentNode != null && parentNode.HasReusePrefabReference)
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
                    if (transform == prefabRef.transform || transform.IsChildOf(prefabRef.transform)) return true;
                }
                return false;
            }

            var nodesForBuild = layerNodes.Where(node => node != null && !IsInsidePrefabRef(node.transform)).ToArray();
            NormalizePreferredUIHelpersForGeneration(nodesForBuild);
            ResolveLayerNameLookup();
            referencedLayerKeys.Clear();
            foreach (var node in nodesForBuild)
            {
                if (!node.HasReuseReference || string.IsNullOrEmpty(node.ReuseTargetKey)) continue;
                referencedLayerKeys.Add(node.ReuseTargetKey);
            }
            ExportSharedReferenceSprites(nodesForBuild);

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
            if ((uiHelpers == null || uiHelpers.Length < 1) && prefabRefRoots.Count < 1)
            {
                return false;
            }
            GameObject uiFormRoot;
            var existingUiFormPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabName);
            if (existingUiFormPrefab == null)
            {
                if (UGUIParser.Instance.UIFormTemplate == null)
                {
                    Debug.LogError("生成UIForm失败: UIFormTemplate为空");
                    return false;
                }

                uiFormRoot = GameObject.Instantiate(UGUIParser.Instance.UIFormTemplate, Vector3.zero, Quaternion.identity);
                uiFormRoot.transform.localScale = Vector3.one;
            }
            else
            {
                uiFormRoot = GameObject.Instantiate(existingUiFormPrefab);// PrefabUtility.LoadPrefabContents(prefabName);
                RestoreGeneratedKeysFromMetadata(prefabName, uiFormRoot);
            }
            uiFormRoot.name = uiFormName;
            var activeGeneratedKeys = new HashSet<string>(StringComparer.Ordinal);
            string incrementalScopePathKey = null;
            if (root != this.transform)
            {
                incrementalScopePathKey = GetGameObjectHierarchyPathKey(root.gameObject, this.transform);
                if (!string.IsNullOrEmpty(incrementalScopePathKey))
                {
                    CollectExistingGeneratedKeys(uiFormRoot.transform, activeGeneratedKeys,
                        generatedKey => !IsPathInScope(generatedKey.Key, incrementalScopePathKey));
                }
            }
            Vector3 canvasPosition = Vector3.zero;
            var uiFormRect = uiFormRoot.GetComponent<RectTransform>();
            if (uiFormRect != null)
            {
                canvasPosition = uiFormRect.anchoredPosition;
            }
            int curIdx = 0;
            int totalCount = uiHelpers.Length;
            var generatedUiHelpers = new List<UIHelperBase>(totalCount);
            var generatedUiElements = new List<GameObject>(totalCount);
            foreach (var uiHelper in uiHelpers)
            {
                if (uiHelper == null || uiHelper.LayerNode == null) continue;

                EditorUtility.DisplayProgressBar($"生成UIFrom:({curIdx++}/{totalCount})", $"正在生成UI元素:{uiHelper.name}", curIdx /
                (float)totalCount);
                var elementPathKey = GetGameObjectHierarchyPathKey(uiHelper.gameObject, this.transform);
                var goPath = GetGameObjectHierarchyParentPath(uiHelper.gameObject, this.transform, out var goNames);
                var parentNode = GetOrCreateNodeByHierarchyPath(uiFormRoot, goPath, goNames, activeGeneratedKeys);
                var reusableNode = TryGetReusableGeneratedNode(parentNode.transform, uiHelper, elementPathKey);
                var uiElement = uiHelper.CreateUI(reusableNode);
                if (uiElement == null) continue;

                SetGeneratedNodeKey(uiElement, elementPathKey, uiHelper.LayerNode.UIType.ToString(), false, activeGeneratedKeys);
                RemoveConsumedGeneratedDependencyNodes(uiElement, uiHelper, this.transform);
                PreserveGeneratedDependencyNodes(uiElement, uiHelper, this.transform, activeGeneratedKeys);
                uiElement.transform.SetParent(parentNode.transform, true);
                ApplyGeneratedSiblingIndex(uiElement.transform, uiHelper.transform);
                uiElement.transform.position += canvasPosition;
                uiElement.transform.localScale = Vector3.one;
                uiHelper.OnUIParented(uiElement);
                generatedUiHelpers.Add(uiHelper);
                generatedUiElements.Add(uiElement);
            }
            if (prefabRefRoots.Count > 0)
            {
                int prefabIdx = 0;
                int prefabTotal = prefabRefRoots.Count;
                foreach (var prefabRefNode in prefabRefRoots)
                {
                    if (prefabRefNode == null || !prefabRefNode.HasReusePrefabReference) continue;

                    EditorUtility.DisplayProgressBar($"生成UIFrom-引用prefab:({prefabIdx++}/{prefabTotal})", $"正在实例化prefab:{prefabRefNode.ReusePrefabDisplayName}", prefabIdx / (float)prefabTotal);
                    if (!TryGetOrCreateSharedPrefabAsset(prefabRefNode, out var prefabAsset) || prefabAsset == null)
                    {
                        Debug.LogWarning($"引用prefab未找到且导出失败: {prefabRefNode.ReusePrefabDisplayName}");
                        continue;
                    }

                    var pathKey = GetGameObjectHierarchyPathKey(prefabRefNode.gameObject, this.transform);
                    var goPath = GetGameObjectHierarchyParentPath(prefabRefNode.gameObject, this.transform, out var goNames);
                    var parentNode = GetOrCreateNodeByHierarchyPath(uiFormRoot, goPath, goNames, activeGeneratedKeys);
                    var prefabInstance = TryGetReusableGeneratedPrefabNode(parentNode.transform, prefabRefNode, pathKey, prefabAsset);
                    if (prefabInstance == null)
                    {
                        prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                        if (prefabInstance == null)
                        {
                            prefabInstance = GameObject.Instantiate(prefabAsset);
                        }
                    }
                    prefabInstance.name = string.IsNullOrEmpty(prefabRefNode.ReusePrefabKey)
                        ? prefabAsset.name
                        : PsdLayerNode.GetReferenceLeafName(prefabRefNode.ReusePrefabKey, prefabAsset.name);
                    prefabInstance.transform.localRotation = Quaternion.identity;
                    prefabInstance.transform.localScale = Vector3.one;
                    var prefabRect = prefabInstance.GetComponent<RectTransform>();
                    if (prefabRect != null)
                    {
                        UGUIParser.SetRectTransform(prefabRefNode, prefabRect);
                    }
                    else
                    {
                        Debug.LogWarning($"引用prefab缺少RectTransform: {prefabAsset.name}");
                    }

                    SetGeneratedNodeKey(prefabInstance, pathKey, GeneratedPrefabReferenceTypeKey, false, activeGeneratedKeys);
                    prefabInstance.transform.SetParent(parentNode.transform, true);
                    ApplyGeneratedSiblingIndex(prefabInstance.transform, prefabRefNode.transform);
                    prefabInstance.transform.position += canvasPosition;
                    prefabInstance.transform.localScale = Vector3.one;
                }
            }

            SynchronizeGeneratedGroupOpacities(this.transform, uiFormRoot, nodesForBuild);
            NotifyGeneratedHierarchyReady(generatedUiHelpers, generatedUiElements);
            CleanupLegacyUiStringKeys(uiFormRoot);
            CleanupStaleGeneratedNodes(uiFormRoot.transform, activeGeneratedKeys);
            FitGeneratedContainerHierarchy(uiFormRoot.transform);
            var generatedNodeSnapshots = CaptureGeneratedNodeSnapshots(uiFormRoot);
            CleanupTransientGeneratedKeys(uiFormRoot);

            uiFormRoot.name = System.IO.Path.GetFileNameWithoutExtension(prefabName);
            var uiPrefab = PrefabUtility.SaveAsPrefabAsset(uiFormRoot, prefabName);
            if (uiPrefab != null)
            {
                SaveGeneratedKeysMetadata(prefabName, uiPrefab, generatedNodeSnapshots, incrementalScopePathKey);
                DestroyImmediate(uiFormRoot);
                Selection.activeGameObject = uiPrefab;
            }

            EditorUtility.ClearProgressBar();
            return true;
        }

        private GameObject GetOrCreateNodeByHierarchyPath(GameObject uiFormRoot, string[] goPath, string[] goNames, HashSet<string> activeGeneratedKeys)
        {
            GameObject result = uiFormRoot;
            if (goPath != null && goNames != null)
            {
                for (int i = 0; i < goPath.Length; i++)
                {
                    var nodeKey = goPath[i];
                    var legacyNodeName = goNames[i];
                    var nodeName = PsdLayerNode.GetGeneratedObjectName(legacyNodeName);
                    var targetNode = FindGeneratedChildByPath(result.transform, nodeKey, false);
                    bool useExistingUiNode = targetNode != null
                        && IsActiveGeneratedNode(activeGeneratedKeys, targetNode.GetComponent<PsdGeneratedKey>());
                    if (targetNode == null)
                    {
                        targetNode = FindGeneratedChild(result.transform, nodeKey, GeneratedContainerTypeKey, true);
                    }
                    if (targetNode == null)
                    {
                        targetNode = FindLegacyChildByName(result.transform, nodeName, true);
                    }
                    if (targetNode == null && !string.Equals(legacyNodeName, nodeName, StringComparison.Ordinal))
                    {
                        targetNode = FindLegacyChildByName(result.transform, legacyNodeName, true);
                    }
                    if (targetNode == null)
                    {
                        targetNode = new GameObject(nodeName, typeof(RectTransform));
                        targetNode.layer = UnityEngine.LayerMask.NameToLayer("UI");
                        targetNode.transform.SetParent(result.transform, false);
                        targetNode.transform.localPosition = Vector3.zero;
                        targetNode.transform.localRotation = Quaternion.identity;
                        targetNode.transform.localScale = Vector3.one;
                    }
                    targetNode.name = nodeName;
                    if (!useExistingUiNode)
                    {
                        if (targetNode.GetComponent<PsdGeneratedKey>() != null)
                        {
                            StripManagedGeneratedRootComponents(targetNode);
                        }
                        SetGeneratedNodeKey(targetNode, nodeKey, GeneratedContainerTypeKey, true, activeGeneratedKeys);
                    }
                    result = targetNode;
                }
            }
            return result;
        }

        private static bool IsActiveGeneratedNode(HashSet<string> activeGeneratedKeys, PsdGeneratedKey generatedKey)
        {
            if (activeGeneratedKeys == null || generatedKey == null) return false;
            return activeGeneratedKeys.Contains(BuildGeneratedCompositeKey(generatedKey.Key, generatedKey.TypeKey, generatedKey.IsContainer));
        }

        private static void ApplyGeneratedSiblingIndex(Transform generatedTransform, Transform sourceTransform)
        {
            if (generatedTransform == null || sourceTransform == null || generatedTransform.parent == null) return;
            generatedTransform.SetSiblingIndex(sourceTransform.GetSiblingIndex());
        }

        private static void NotifyGeneratedHierarchyReady(List<UIHelperBase> helpers, List<GameObject> elements)
        {
            if (helpers == null || elements == null) return;

            int count = Mathf.Min(helpers.Count, elements.Count);
            for (int i = 0; i < count; i++)
            {
                var helper = helpers[i];
                var element = elements[i];
                if (helper == null || element == null) continue;

                helper.OnGeneratedHierarchyReady(element);
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
                    string pathKey = GetGameObjectHierarchyPathKey(sourceNode.gameObject, sourcePathRoot);
                    generatedNode = FindGeneratedDescendantByPath(generatedRoot.transform, pathKey, false)
                        ?? FindGeneratedDescendantByPath(generatedRoot.transform, pathKey, true);
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

        private static string BuildGeneratedCompositeKey(string key, string typeKey, bool isContainer)
        {
            return $"{(isContainer ? "C" : "N")}:{typeKey}:{key}";
        }

        private static string GetLegacyGeneratedMetadataPath(string prefabAssetPath)
        {
            if (string.IsNullOrWhiteSpace(prefabAssetPath)) return null;

            var dir = Path.GetDirectoryName(prefabAssetPath);
            var fileName = Path.GetFileNameWithoutExtension(prefabAssetPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName)) return null;

            return Path.Combine(dir, $"{fileName}.psd2uiform.generated.json").Replace("\\", "/");
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

        private GeneratedMetadataSerializedEntry FindGeneratedMetadataEntry(string targetPrefabAssetPath)
        {
            if (generatedMetadataEntries == null || generatedMetadataEntries.Count < 1) return null;

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
                    || string.IsNullOrWhiteSpace(item.Key)
                    || string.IsNullOrWhiteSpace(item.TypeKey))
                {
                    continue;
                }

                result.Add(new GeneratedMetadataEntry
                {
                    GlobalObjectId = item.GlobalObjectId,
                    Key = item.Key,
                    TypeKey = item.TypeKey,
                    IsContainer = item.IsContainer,
                });
            }

            return result;
        }

        private static List<GeneratedMetadataEntry> ParseGeneratedMetadataJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<GeneratedMetadataEntry>();
            }

            try
            {
                var metadata = JsonUtility.FromJson<GeneratedMetadataCollection>(json);
                return CloneGeneratedMetadataEntries(metadata?.Entries);
            }
            catch
            {
                return new List<GeneratedMetadataEntry>();
            }
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
                if (normalizedEntries.Count < 1)
                {
                    normalizedEntries = ParseGeneratedMetadataJson(entry.Json);
                }
                if (normalizedEntries.Count < 1) continue;

                map[normalizedPath] = new GeneratedMetadataSerializedEntry
                {
                    PrefabAssetPath = normalizedPath,
                    Json = null,
                    Entries = normalizedEntries,
                };
            }

            generatedMetadataEntries = map.Values.OrderBy(item => item.PrefabAssetPath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void MarkGeneratedMetadataDirty()
        {
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            if (gameObject != null && gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
            AssetDatabase.SaveAssets();
        }

        internal string GetGeneratedMetadataHostAssetPathForDebug()
        {
            var assetPath = AssetDatabase.GetAssetPath(gameObject);
            return string.IsNullOrWhiteSpace(assetPath) ? "<Scene Object>" : assetPath;
        }

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

        private List<GeneratedMetadataEntry> LoadGeneratedMetadataEntries(string targetPrefabAssetPath)
        {
            var entry = FindGeneratedMetadataEntry(targetPrefabAssetPath);
            if (entry != null && entry.Entries != null && entry.Entries.Count > 0)
            {
                return CloneGeneratedMetadataEntries(entry.Entries);
            }
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Json))
            {
                var migratedEntries = ParseGeneratedMetadataJson(entry.Json);
                if (migratedEntries.Count > 0)
                {
                    SaveGeneratedMetadataEntries(targetPrefabAssetPath, migratedEntries);
                    return migratedEntries;
                }
            }

            var legacyMetadataPath = GetLegacyGeneratedMetadataPath(targetPrefabAssetPath);
            if (!string.IsNullOrWhiteSpace(legacyMetadataPath) && File.Exists(legacyMetadataPath))
            {
                var legacyJson = File.ReadAllText(legacyMetadataPath);
                if (!string.IsNullOrWhiteSpace(legacyJson))
                {
                    var migratedEntries = ParseGeneratedMetadataJson(legacyJson);
                    if (migratedEntries.Count > 0)
                    {
                        SaveGeneratedMetadataEntries(targetPrefabAssetPath, migratedEntries);
                        return migratedEntries;
                    }
                }
            }

            return null;
        }

        private void SaveGeneratedMetadataEntries(string targetPrefabAssetPath, List<GeneratedMetadataEntry> entries)
        {
            var normalizedPath = NormalizeMetadataPrefabPath(targetPrefabAssetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                Debug.LogWarning($"保存生成节点元数据失败: target为空, target={targetPrefabAssetPath}");
                return;
            }

            if (generatedMetadataEntries == null)
            {
                generatedMetadataEntries = new List<GeneratedMetadataSerializedEntry>();
            }

            var entry = FindGeneratedMetadataEntry(normalizedPath);
            var normalizedEntries = CloneGeneratedMetadataEntries(entries);
            if (normalizedEntries.Count < 1)
            {
                if (entry != null)
                {
                    generatedMetadataEntries.Remove(entry);
                }
            }
            else
            {
                if (entry == null)
                {
                    entry = new GeneratedMetadataSerializedEntry();
                    generatedMetadataEntries.Add(entry);
                }

                entry.PrefabAssetPath = normalizedPath;
                entry.Entries = normalizedEntries;
            }

            CleanupGeneratedMetadataEntries();
            MarkGeneratedMetadataDirty();

            var legacyMetadataPath = GetLegacyGeneratedMetadataPath(targetPrefabAssetPath);
            if (!string.IsNullOrWhiteSpace(legacyMetadataPath) && File.Exists(legacyMetadataPath))
            {
                AssetDatabase.DeleteAsset(legacyMetadataPath);
            }
        }

        private List<GeneratedNodeSnapshot> CaptureGeneratedNodeSnapshots(GameObject root)
        {
            var snapshots = new List<GeneratedNodeSnapshot>();
            if (root == null) return snapshots;

            var generatedKeys = root.GetComponentsInChildren<PsdGeneratedKey>(true);
            var rootTransform = root.transform;
            foreach (var generatedKey in generatedKeys)
            {
                if (generatedKey == null) continue;

                var transformPath = BuildTransformIndexPath(generatedKey.transform, rootTransform);
                if (transformPath == null) continue;

                snapshots.Add(new GeneratedNodeSnapshot
                {
                    Key = generatedKey.Key,
                    TypeKey = generatedKey.TypeKey,
                    IsContainer = generatedKey.IsContainer,
                    TransformPath = transformPath,
                });
            }
            return snapshots;
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
            return current != null ? current.gameObject : null;
        }

        private static void CleanupTransientGeneratedKeys(GameObject root)
        {
            if (root == null) return;

            var generatedKeys = root.GetComponentsInChildren<PsdGeneratedKey>(true);
            for (int i = generatedKeys.Length - 1; i >= 0; i--)
            {
                if (generatedKeys[i] != null)
                {
                    DestroyImmediate(generatedKeys[i]);
                }
            }
        }

        private void RestoreGeneratedKeysFromMetadata(string prefabAssetPath, GameObject prefabInstance)
        {
            if (prefabInstance == null) return;

            var metadataEntries = LoadGeneratedMetadataEntries(prefabAssetPath);
            if (metadataEntries == null || metadataEntries.Count < 1) return;

            try
            {
                var sourceToInstance = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                var transforms = prefabInstance.GetComponentsInChildren<Transform>(true);
                foreach (var item in transforms)
                {
                    if (item == null) continue;
                    var sourceGo = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject) as GameObject;
                    if (sourceGo == null) continue;

                    var globalId = GlobalObjectId.GetGlobalObjectIdSlow(sourceGo).ToString();
                    if (string.IsNullOrEmpty(globalId)) continue;
                    sourceToInstance[globalId] = item.gameObject;
                }

                foreach (var entry in metadataEntries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.GlobalObjectId)) continue;
                    if (!sourceToInstance.TryGetValue(entry.GlobalObjectId, out var instanceGo) || instanceGo == null) continue;

                    SetGeneratedNodeKey(instanceGo, entry.Key, entry.TypeKey, entry.IsContainer);
                }
            }
            catch (Exception err)
            {
                Debug.LogWarning($"恢复生成节点元数据失败: {prefabAssetPath}\n{err.Message}");
            }
        }

        private void SaveGeneratedKeysMetadata(string prefabAssetPath, GameObject prefabAssetRoot, List<GeneratedNodeSnapshot> snapshots, string incrementalScopePathKey = null)
        {
            try
            {
                if (prefabAssetRoot == null || snapshots == null || snapshots.Count < 1)
                {
                    SaveGeneratedMetadataEntries(prefabAssetPath, null);
                    return;
                }

                var metadataEntries = new List<GeneratedMetadataEntry>();
                foreach (var snapshot in snapshots)
                {
                    if (snapshot == null || snapshot.TransformPath == null) continue;

                    var savedGo = ResolveTransformIndexPath(prefabAssetRoot, snapshot.TransformPath);
                    if (savedGo == null) continue;

                    var globalId = GlobalObjectId.GetGlobalObjectIdSlow(savedGo).ToString();
                    if (string.IsNullOrEmpty(globalId)) continue;

                    metadataEntries.Add(new GeneratedMetadataEntry
                    {
                        GlobalObjectId = globalId,
                        Key = snapshot.Key,
                        TypeKey = snapshot.TypeKey,
                        IsContainer = snapshot.IsContainer,
                    });
                }

                if (metadataEntries.Count < 1)
                {
                    SaveGeneratedMetadataEntries(prefabAssetPath, null);
                    return;
                }

                if (!string.IsNullOrEmpty(incrementalScopePathKey))
                {
                    var oldMetadataEntries = LoadGeneratedMetadataEntries(prefabAssetPath);
                    if (oldMetadataEntries != null && oldMetadataEntries.Count > 0)
                    {
                        var compositeKeys = new HashSet<string>(metadataEntries.Select(entry => BuildGeneratedCompositeKey(entry.Key, entry.TypeKey, entry.IsContainer)), StringComparer.Ordinal);
                        foreach (var entry in oldMetadataEntries)
                        {
                            if (entry == null || IsPathInScope(entry.Key, incrementalScopePathKey)) continue;

                            var compositeKey = BuildGeneratedCompositeKey(entry.Key, entry.TypeKey, entry.IsContainer);
                            if (compositeKeys.Add(compositeKey))
                            {
                                metadataEntries.Add(entry);
                            }
                        }
                    }
                }

                SaveGeneratedMetadataEntries(prefabAssetPath, metadataEntries);
            }
            catch (Exception err)
            {
                Debug.LogWarning($"保存生成节点元数据失败: {prefabAssetPath}\n{err.Message}");
            }
        }

        private static bool IsPathInScope(string pathKey, string scopePathKey)
        {
            if (string.IsNullOrEmpty(pathKey) || string.IsNullOrEmpty(scopePathKey)) return false;

            return string.Equals(pathKey, scopePathKey, StringComparison.Ordinal)
                || pathKey.StartsWith(scopePathKey + "/", StringComparison.Ordinal);
        }

        private void CollectExistingGeneratedKeys(Transform root, HashSet<string> activeGeneratedKeys, Func<PsdGeneratedKey, bool> includePredicate)
        {
            if (root == null || activeGeneratedKeys == null) return;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null) continue;

                var generatedKey = child.GetComponent<PsdGeneratedKey>();
                if (generatedKey != null && (includePredicate == null || includePredicate(generatedKey)))
                {
                    activeGeneratedKeys.Add(BuildGeneratedCompositeKey(generatedKey.Key, generatedKey.TypeKey, generatedKey.IsContainer));
                }

                CollectExistingGeneratedKeys(child, activeGeneratedKeys, includePredicate);
            }
        }

        private static Type GetPrimaryRootComponentType(GameObject target)
        {
            if (target == null) return null;

            var components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || IsIgnoredRootComponent(component.GetType())) continue;

                if (component is ScrollRect || component is Selectable || component is Mask)
                {
                    return component.GetType();
                }
            }

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || IsIgnoredRootComponent(component.GetType())) continue;

                if (component is Graphic)
                {
                    return component.GetType();
                }
            }

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                var type = component.GetType();
                if (!IsIgnoredRootComponent(type)) return type;
            }
            return null;
        }

        private static bool IsIgnoredRootComponent(Type type)
        {
            return type == typeof(Transform)
                || type == typeof(RectTransform)
                || type == typeof(CanvasRenderer)
                || type == typeof(UIStringKey)
                || type == typeof(PsdGeneratedKey);
        }

        private static bool IsLikelyMatchingGeneratedRoot(GameObject existing, GUIType uiType)
        {
            var rule = UGUIParser.Instance?.GetRule(uiType);
            if (rule == null || existing == null) return false;

            if (rule.UIPrefab == null)
            {
                return existing.GetComponent<RectTransform>() != null;
            }

            var expectedType = GetPrimaryRootComponentType(rule.UIPrefab);
            var existingType = GetPrimaryRootComponentType(existing);
            if (expectedType == null || existingType == null)
            {
                return false;
            }
            return expectedType == existingType;
        }

        private GameObject FindGeneratedChild(Transform parent, string pathKey, string typeKey, bool isContainer)
        {
            if (parent == null || string.IsNullOrEmpty(pathKey) || string.IsNullOrEmpty(typeKey)) return null;

            foreach (Transform child in parent)
            {
                if (child == null) continue;
                var key = child.GetComponent<PsdGeneratedKey>();
                if (key == null) continue;
                if (key.IsContainer != isContainer) continue;
                if (!string.Equals(key.TypeKey, typeKey, StringComparison.Ordinal)) continue;
                if (!string.Equals(key.Key, pathKey, StringComparison.Ordinal)) continue;
                return child.gameObject;
            }
            return null;
        }

        private GameObject FindGeneratedChildByPath(Transform parent, string pathKey, bool isContainer)
        {
            if (parent == null || string.IsNullOrEmpty(pathKey)) return null;

            foreach (Transform child in parent)
            {
                if (child == null) continue;
                var key = child.GetComponent<PsdGeneratedKey>();
                if (key == null) continue;
                if (key.IsContainer != isContainer) continue;
                if (!string.Equals(key.Key, pathKey, StringComparison.Ordinal)) continue;
                return child.gameObject;
            }
            return null;
        }

        private GameObject FindLegacyChildByName(Transform parent, string nodeName, bool isContainer, GUIType? expectedUiType = null)
        {
            if (parent == null || string.IsNullOrWhiteSpace(nodeName)) return null;

            GameObject result = null;
            int matchCount = 0;
            foreach (Transform child in parent)
            {
                if (child == null || !string.Equals(child.name, nodeName, StringComparison.Ordinal)) continue;
                if (child.GetComponent<PsdGeneratedKey>() != null) continue;

                if (isContainer)
                {
                    if (GetPrimaryRootComponentType(child.gameObject) != null) continue;
                }
                else if (expectedUiType.HasValue && !IsLikelyMatchingGeneratedRoot(child.gameObject, expectedUiType.Value))
                {
                    continue;
                }

                result = child.gameObject;
                matchCount++;
                if (matchCount > 1)
                {
                    return null;
                }
            }
            return result;
        }

        private void SetGeneratedNodeKey(GameObject go, string pathKey, string typeKey, bool isContainer, HashSet<string> activeGeneratedKeys = null)
        {
            if (go == null || string.IsNullOrEmpty(pathKey) || string.IsNullOrEmpty(typeKey)) return;

            var generatedKey = go.GetComponent<PsdGeneratedKey>() ?? go.AddComponent<PsdGeneratedKey>();
            generatedKey.Key = pathKey;
            generatedKey.TypeKey = typeKey;
            generatedKey.IsContainer = isContainer;
            activeGeneratedKeys?.Add(BuildGeneratedCompositeKey(pathKey, typeKey, isContainer));
        }

        private static void CleanupLegacyUiStringKeys(GameObject root)
        {
            if (root == null) return;

            var uiStrKeys = root.GetComponentsInChildren<UIStringKey>(true);
            for (int i = uiStrKeys.Length - 1; i >= 0; i--)
            {
                if (uiStrKeys[i] != null)
                {
                    DestroyImmediate(uiStrKeys[i]);
                }
            }
        }

        private void CleanupStaleGeneratedNodes(Transform root, HashSet<string> activeGeneratedKeys)
        {
            if (root == null || activeGeneratedKeys == null) return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null) continue;

                var generatedKey = child.GetComponent<PsdGeneratedKey>();
                if (generatedKey != null)
                {
                    var compositeKey = BuildGeneratedCompositeKey(generatedKey.Key, generatedKey.TypeKey, generatedKey.IsContainer);
                    if (!activeGeneratedKeys.Contains(compositeKey))
                    {
                        DestroyImmediate(child.gameObject);
                        continue;
                    }
                }

                CleanupStaleGeneratedNodes(child, activeGeneratedKeys);
            }
        }

        private string GetGameObjectHierarchyPathKey(GameObject go, Transform stopRoot)
        {
            if (go == null) return null;

            var node = go.GetComponent<PsdLayerNode>();
            if (node == null) return null;

            var segments = new List<string>();
            var current = go.transform;
            while (current != null && current != stopRoot)
            {
                var currentNode = current.GetComponent<PsdLayerNode>();
                if (currentNode != null)
                {
                    var segment = GetHierarchySegmentKey(currentNode);
                    if (!string.IsNullOrEmpty(segment))
                    {
                        segments.Insert(0, segment);
                    }
                }
                current = current.parent;
            }
            return segments.Count > 0 ? string.Join("/", segments) : null;
        }

        private string[] GetGameObjectHierarchyParentPath(GameObject go, Transform stopRoot, out string[] names)
        {
            names = null;
            if (go == null || stopRoot == null) return null;
            if (go.transform == stopRoot) return null;
            if (go.transform.parent == null) return null;
            if (!go.transform.IsChildOf(stopRoot)) return null;
            if (go.transform.parent == stopRoot) return null;

            var parents = new List<PsdLayerNode>();
            var current = go.transform.parent;
            while (current != null && current != stopRoot)
            {
                var currentNode = current.GetComponent<PsdLayerNode>();
                if (currentNode != null)
                {
                    parents.Insert(0, currentNode);
                }
                current = current.parent;
            }
            if (parents.Count < 1) return null;

            names = parents.Select(item => item.gameObject.name).ToArray();
            return parents.Select(item => GetGameObjectHierarchyPathKey(item.gameObject, stopRoot)).ToArray();
        }

        private Dictionary<string, LayerUITypeSnapshot> CaptureLayerUITypeSnapshot()
        {
            var snapshots = new Dictionary<string, LayerUITypeSnapshot>(StringComparer.OrdinalIgnoreCase);
            var nodes = GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var node in nodes)
            {
                var pathKey = GetLayerUITypePathKey(node);
                if (string.IsNullOrEmpty(pathKey) || snapshots.ContainsKey(pathKey)) continue;

                snapshots.Add(pathKey, new LayerUITypeSnapshot
                {
                    UIType = node.UIType
                });
            }
            return snapshots;
        }

        private void RestoreLayerUITypeSnapshot(Dictionary<string, LayerUITypeSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count < 1) return;

            var nodes = GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var node in nodes)
            {
                var pathKey = GetLayerUITypePathKey(node);
                if (string.IsNullOrEmpty(pathKey)) continue;
                if (!snapshots.TryGetValue(pathKey, out var snapshot)) continue;

                node.SetUIType(snapshot.UIType, false);
            }
        }

        private string GetLayerUITypePathKey(PsdLayerNode node)
        {
            if (node == null) return null;

            var segments = new List<string>();
            var current = node.transform;
            while (current != null && current != transform)
            {
                var currentNode = current.GetComponent<PsdLayerNode>();
                if (currentNode != null)
                {
                    var segment = GetLayerUITypeSegmentKey(currentNode);
                    if (!string.IsNullOrEmpty(segment))
                    {
                        segments.Insert(0, segment);
                    }
                }
                current = current.parent;
            }
            return segments.Count > 0 ? string.Join("/", segments) : null;
        }

        private string GetLayerUITypeSegmentKey(PsdLayerNode node)
        {
            if (node == null) return null;

            var baseKey = GetLayerUITypeLookupKey(node);
            if (string.IsNullOrEmpty(baseKey)) return null;

            int duplicateIndex = 0;
            var parent = node.transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    var siblingNode = sibling != null ? sibling.GetComponent<PsdLayerNode>() : null;
                    if (siblingNode == null) continue;
                    if (ReferenceEquals(siblingNode, node)) break;
                    if (string.Equals(GetLayerUITypeLookupKey(siblingNode), baseKey, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateIndex++;
                    }
                }
            }

            return duplicateIndex > 0 ? $"{baseKey}[{duplicateIndex}]" : baseKey;
        }

        private string GetLayerUITypeLookupKey(PsdLayerNode node)
        {
            if (node == null) return null;

            var rawName = string.IsNullOrWhiteSpace(node.SourceLayerName) ? node.gameObject?.name : node.SourceLayerName;
            var parser = UGUIParser.Instance;
            if (parser != null)
            {
                rawName = parser.RemoveLayerTypeFlags(rawName);
            }
            return PsdLayerNode.NormalizeLayerName(rawName, preserveRefTags: true);
        }

        private string GetHierarchySegmentKey(PsdLayerNode node)
        {
            if (node == null) return null;

            var baseKey = node.LayerNameLookupKey;
            if (string.IsNullOrEmpty(baseKey)) return null;

            int duplicateIndex = 0;
            var parent = node.transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    var siblingNode = sibling != null ? sibling.GetComponent<PsdLayerNode>() : null;
                    if (siblingNode == null) continue;
                    if (ReferenceEquals(siblingNode, node)) break;
                    if (string.Equals(siblingNode.LayerNameLookupKey, baseKey, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateIndex++;
                    }
                }
            }

            return duplicateIndex > 0 ? $"{baseKey}[{duplicateIndex}]" : baseKey;
        }

        private GameObject TryGetReusableGeneratedNode(Transform parent, UIHelperBase uiHelper, string pathKey)
        {
            if (parent == null || uiHelper == null || uiHelper.LayerNode == null || string.IsNullOrEmpty(pathKey)) return null;

            string typeKey = uiHelper.LayerNode.UIType.ToString();
            var reusableNode = FindGeneratedChild(parent, pathKey, typeKey, false);
            if (reusableNode != null)
            {
                return reusableNode;
            }

            var conflictingNode = FindGeneratedChildByPath(parent, pathKey, false);
            if (conflictingNode != null)
            {
                return conflictingNode;
            }

            var reusableContainer = FindGeneratedChild(parent, pathKey, GeneratedContainerTypeKey, true);
            if (reusableContainer != null)
            {
                return reusableContainer;
            }

            return null;
        }

        private void PreserveGeneratedDependencyNodes(GameObject uiElement, UIHelperBase uiHelper, Transform pathRoot, HashSet<string> activeGeneratedKeys)
        {
            if (uiElement == null || uiHelper == null || pathRoot == null || activeGeneratedKeys == null) return;
            if (!ShouldPreserveGeneratedDependencyNodes(uiHelper)) return;

            var dependencies = uiHelper.GetDependencies();
            if (dependencies == null || dependencies.Length < 1) return;

            for (int i = 0; i < dependencies.Length; i++)
            {
                var dependency = dependencies[i];
                if (dependency == null || !IsButtonTextDependency(dependency.UIType)) continue;

                var dependencyPathKey = GetGameObjectHierarchyPathKey(dependency.gameObject, pathRoot);
                var generatedDependency = FindGeneratedDescendantByPath(uiElement.transform, dependencyPathKey, false);
                if (generatedDependency == null) continue;

                SetGeneratedNodeKey(generatedDependency, dependencyPathKey, dependency.UIType.ToString(), false, activeGeneratedKeys);
            }
        }

        private void RemoveConsumedGeneratedDependencyNodes(GameObject uiElement, UIHelperBase uiHelper, Transform pathRoot)
        {
            if (uiElement == null || uiHelper == null || pathRoot == null) return;
            if (!ShouldRemoveConsumedDependencyNodes(uiHelper)) return;

            var dependencies = uiHelper.GetDependencies();
            if (dependencies == null || dependencies.Length < 1) return;

            for (int i = 0; i < dependencies.Length; i++)
            {
                var dependency = dependencies[i];
                if (dependency == null || !IsButtonImageDependency(dependency.UIType)) continue;

                var dependencyPathKey = GetGameObjectHierarchyPathKey(dependency.gameObject, pathRoot);
                var generatedDependency = FindGeneratedDescendantByPath(uiElement.transform, dependencyPathKey, false);
                if (generatedDependency == null)
                {
                    generatedDependency = FindDescendantByName(uiElement.transform, dependency.GetGeneratedObjectName());
                }
                if (generatedDependency == null || generatedDependency == uiElement) continue;

                DestroyImmediate(generatedDependency);
            }
        }

        private static bool ShouldPreserveGeneratedDependencyNodes(UIHelperBase uiHelper)
        {
            return uiHelper is ButtonHelper || uiHelper is TMPButtonHelper;
        }

        private static bool ShouldRemoveConsumedDependencyNodes(UIHelperBase uiHelper)
        {
            return uiHelper is ButtonHelper || uiHelper is TMPButtonHelper;
        }

        private static bool IsButtonTextDependency(GUIType uiType)
        {
            return uiType == GUIType.Button_Text
                || uiType == GUIType.Text
                || uiType == GUIType.TMPText;
        }

        private static bool IsButtonImageDependency(GUIType uiType)
        {
            return uiType == GUIType.Background
                || uiType == GUIType.Image
                || uiType == GUIType.RawImage
                || uiType == GUIType.Button_Highlight
                || uiType == GUIType.Button_Press
                || uiType == GUIType.Button_Select
                || uiType == GUIType.Button_Disable;
        }

        private static GameObject FindGeneratedDescendantByPath(Transform root, string pathKey, bool isContainer)
        {
            if (root == null || string.IsNullOrEmpty(pathKey)) return null;

            var generatedKeys = root.GetComponentsInChildren<PsdGeneratedKey>(true);
            for (int i = 0; i < generatedKeys.Length; i++)
            {
                var key = generatedKeys[i];
                if (key == null || key.IsContainer != isContainer) continue;
                if (string.Equals(key.Key, pathKey, StringComparison.Ordinal))
                {
                    return key.gameObject;
                }
            }
            return null;
        }

        private static GameObject FindDescendantByName(Transform root, string generatedName)
        {
            if (root == null || string.IsNullOrEmpty(generatedName)) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null) continue;

                if (string.Equals(child.name, generatedName, StringComparison.Ordinal))
                {
                    return child.gameObject;
                }

                var nested = FindDescendantByName(child, generatedName);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private GameObject TryGetReusableGeneratedPrefabNode(Transform parent, PsdLayerNode prefabRefNode, string pathKey, GameObject prefabAsset)
        {
            if (parent == null || prefabRefNode == null || string.IsNullOrEmpty(pathKey)) return null;

            var reusableNode = FindGeneratedChild(parent, pathKey, GeneratedPrefabReferenceTypeKey, false);
            if (reusableNode != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(reusableNode);
                if (source == prefabAsset)
                {
                    return reusableNode;
                }
                DestroyImmediate(reusableNode);
            }

            var conflictingNode = FindGeneratedChildByPath(parent, pathKey, false);
            if (conflictingNode != null)
            {
                DestroyImmediate(conflictingNode);
            }

            var legacyName = string.IsNullOrEmpty(prefabRefNode.ReusePrefabKey)
                ? prefabRefNode.gameObject.name
                : PsdLayerNode.GetReferenceLeafName(prefabRefNode.ReusePrefabKey, prefabRefNode.gameObject.name);
            var legacyNode = FindLegacyChildByName(parent, legacyName, false);
            if (legacyNode == null) return null;

            var legacySource = PrefabUtility.GetCorrespondingObjectFromSource(legacyNode);
            return legacySource == prefabAsset ? legacyNode : null;
        }
        private void ResolveLayerNameLookup(PsdLayerNode[] nodes = null)
        {
            layerLookupByName.Clear();
            referencedLayerKeys.Clear();
            if (nodes == null || nodes.Length == 0)
            {
                nodes = this.GetComponentsInChildren<PsdLayerNode>(true);
            }
            foreach (var node in nodes)
            {
                var key = node.LayerNameLookupKey;
                if (string.IsNullOrEmpty(key) || layerLookupByName.ContainsKey(key)) continue;
                layerLookupByName.Add(key, node);
            }
            foreach (var node in nodes)
            {
                if (!node.HasReuseReference || string.IsNullOrEmpty(node.ReuseTargetKey)) continue;
                referencedLayerKeys.Add(node.ReuseTargetKey);
                //if (!layerLookupByName.ContainsKey(node.ReuseTargetKey))
                //{
                //    Debug.LogWarning($"复用图层未找到: {node.name} -> {node.ReuseTargetDisplayName}");
                //}
            }
        }
        private void ExportSharedReferenceSprites(PsdLayerNode[] nodes)
        {
            sharedSpriteAssets.Clear();
            if (nodes == null || nodes.Length == 0) return;
            // Fatcat定制: 不再统一共享目录, 传null由目标节点按名称分类导出
            var referencedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (!node.HasReuseReference || string.IsNullOrEmpty(node.ReuseTargetKey)) continue;
                referencedKeys.Add(node.ReuseTargetKey);
            }
            foreach (var key in referencedKeys)
            {
                if (!layerLookupByName.TryGetValue(key, out var targetNode))
                {
                    continue;
                }
                var exportedPath = targetNode.ExportImageAsset(true, null, key, false, true);
                if (!string.IsNullOrEmpty(exportedPath))
                {
                    RegisterSharedSprite(key, exportedPath);
                }
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
        private bool TryEnsureSharedOutputDirectory(string classifyName, out string sharedDir)
        {
            sharedDir = null;
            // Fatcat定制: 共享图集按PSD/图层名分类到 Image/Board 等子目录(基于UI图片导出根目录)
            var baseDir = Psd2UIFormSettings.Instance.UIImagesOutputDir;
            if (string.IsNullOrWhiteSpace(baseDir)) return false;

            var subDir = GetImageSubdirFromPsdName(classifyName ?? string.Empty);
            sharedDir = Path.Combine(baseDir, subDir).Replace("\\", "/");

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

            if (!TryEnsureSharedPrefabOutputDirectory(out var sharedDir))
            {
                Debug.LogWarning($"SharedPrefabOutput未配置, 无法复用prefab:{requester.name}");
                return false;
            }

            var expectedPrefabPath = Path.Combine(sharedDir, $"{key}.prefab").Replace("\\", "/");
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
                        if (parentNode != null && parentNode.HasReusePrefabReference)
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
                    if (transform == prefabRef.transform || transform.IsChildOf(prefabRef.transform)) return true;
                }
                return false;
            }

            var nodesForBuild = layerNodes.Where(node => node != null && !IsInsidePrefabRef(node.transform)).ToArray();
            NormalizePreferredUIHelpersForGeneration(nodesForBuild);
            ResolveLayerNameLookup();
            referencedLayerKeys.Clear();
            foreach (var node in nodesForBuild)
            {
                if (!node.HasReuseReference || string.IsNullOrEmpty(node.ReuseTargetKey)) continue;
                referencedLayerKeys.Add(node.ReuseTargetKey);
            }
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

            GameObject prefabRoot = null;
            var existingPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
            var rootHelper = exportRoot.GetComponent<UIHelperBase>();
            bool useTargetAsPrefabRoot = rootHelper != null
                && rootHelper.LayerNode != null
                && rootHelper.LayerNode.IsMainUIType
                && rootHelper.LayerNode.UIType != GUIType.Null;
            try
            {
                if (existingPrefabAsset != null)
                {
                    prefabRoot = GameObject.Instantiate(existingPrefabAsset);
                    RestoreGeneratedKeysFromMetadata(prefabAssetPath, prefabRoot);
                }

                Vector3 rootWorldOffset = Vector3.zero;
                Quaternion rootLocalRotation = Quaternion.identity;
                if (useTargetAsPrefabRoot)
                {
                    if (prefabRoot != null)
                    {
                        prefabRoot = rootHelper.CreateUI(prefabRoot);
                    }
                    if (prefabRoot == null)
                    {
                        prefabRoot = rootHelper.CreateUI();
                    }
                    if (prefabRoot == null)
                    {
                        useTargetAsPrefabRoot = false;
                    }
                }
                if (!useTargetAsPrefabRoot)
                {
                    bool canReuseContainerRoot = prefabRoot != null
                        && prefabRoot.GetComponent<RectTransform>() != null;
                    if (!canReuseContainerRoot)
                    {
                        if (prefabRoot != null)
                        {
                            DestroyImmediate(prefabRoot);
                        }
                        prefabRoot = new GameObject(prefabRootName, typeof(RectTransform));
                    }
                    else
                    {
                        StripManagedGeneratedRootComponents(prefabRoot);
                    }

                    prefabRoot.name = prefabRootName;
                    prefabRoot.layer = UnityEngine.LayerMask.NameToLayer("UI");
                    prefabRoot.transform.localPosition = Vector3.zero;
                    prefabRoot.transform.localRotation = Quaternion.identity;
                    prefabRoot.transform.localScale = Vector3.one;
                    UGUIParser.SetRectTransform(targetLogic, prefabRoot.transform);
                }
                else
                {
                    prefabRoot.name = prefabRootName;
                    prefabRoot.layer = UnityEngine.LayerMask.NameToLayer("UI");
                    rootWorldOffset = prefabRoot.transform.position;
                    if (rootHelper.LayerNode.TryGetTextUnityRotation(out _))
                    {
                        rootLocalRotation = prefabRoot.transform.localRotation;
                    }
                    prefabRoot.transform.localPosition = Vector3.zero;
                    prefabRoot.transform.localRotation = Quaternion.identity;
                    prefabRoot.transform.localScale = Vector3.one;
                }

                var activeGeneratedKeys = new HashSet<string>(StringComparer.Ordinal);
                int curIdx = 0;
                int totalCount = uiHelpers.Length;
                var generatedUiHelpers = new List<UIHelperBase>(totalCount);
                var generatedUiElements = new List<GameObject>(totalCount);
                foreach (var uiHelper in uiHelpers)
                {
                    if (uiHelper == null || uiHelper.LayerNode == null) continue;
                    if (useTargetAsPrefabRoot && uiHelper == rootHelper) continue;

                    EditorUtility.DisplayProgressBar($"生成prefab:({curIdx++}/{totalCount})", $"正在生成UI元素:{uiHelper.name}", curIdx / (float)totalCount);
                    var elementPathKey = GetGameObjectHierarchyPathKey(uiHelper.gameObject, exportRoot);
                    var goPath = GetGameObjectHierarchyParentPath(uiHelper.gameObject, exportRoot, out var goNames);
                    var parentNode = GetOrCreateNodeByHierarchyPath(prefabRoot, goPath, goNames, activeGeneratedKeys);
                    var reusableNode = TryGetReusableGeneratedNode(parentNode.transform, uiHelper, elementPathKey);
                    var uiElement = uiHelper.CreateUI(reusableNode);
                    if (uiElement == null) continue;

                    SetGeneratedNodeKey(uiElement, elementPathKey, uiHelper.LayerNode.UIType.ToString(), false, activeGeneratedKeys);
                    RemoveConsumedGeneratedDependencyNodes(uiElement, uiHelper, exportRoot);
                    PreserveGeneratedDependencyNodes(uiElement, uiHelper, exportRoot, activeGeneratedKeys);
                    uiElement.transform.SetParent(parentNode.transform, true);
                    ApplyGeneratedSiblingIndex(uiElement.transform, uiHelper.transform);
                    if (useTargetAsPrefabRoot)
                    {
                        uiElement.transform.position -= rootWorldOffset;
                    }
                    uiElement.transform.localScale = Vector3.one;
                    uiHelper.OnUIParented(uiElement);
                    generatedUiHelpers.Add(uiHelper);
                    generatedUiElements.Add(uiElement);
                }

                if (prefabRefRoots.Count > 0)
                {
                    int prefabIdx = 0;
                    int prefabTotal = prefabRefRoots.Count;
                    foreach (var prefabRefNode in prefabRefRoots)
                    {
                        if (prefabRefNode == null || !prefabRefNode.HasReusePrefabReference) continue;

                        EditorUtility.DisplayProgressBar($"生成prefab-引用prefab:({prefabIdx++}/{prefabTotal})", $"正在实例化prefab:{prefabRefNode.ReusePrefabDisplayName}", prefabIdx / (float)prefabTotal);
                        if (!TryGetOrCreateSharedPrefabAsset(prefabRefNode, out var prefabAsset) || prefabAsset == null)
                        {
                            Debug.LogWarning($"引用prefab未找到且导出失败: {prefabRefNode.ReusePrefabDisplayName}");
                            continue;
                        }

                        var pathKey = GetGameObjectHierarchyPathKey(prefabRefNode.gameObject, exportRoot);
                        var goPath = GetGameObjectHierarchyParentPath(prefabRefNode.gameObject, exportRoot, out var goNames);
                        var parentNode = GetOrCreateNodeByHierarchyPath(prefabRoot, goPath, goNames, activeGeneratedKeys);
                        var prefabInstance = TryGetReusableGeneratedPrefabNode(parentNode.transform, prefabRefNode, pathKey, prefabAsset);
                        if (prefabInstance == null)
                        {
                            prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                            if (prefabInstance == null)
                            {
                                prefabInstance = GameObject.Instantiate(prefabAsset);
                            }
                        }
                        prefabInstance.name = string.IsNullOrEmpty(prefabRefNode.ReusePrefabKey)
                            ? prefabAsset.name
                            : PsdLayerNode.GetReferenceLeafName(prefabRefNode.ReusePrefabKey, prefabAsset.name);
                        prefabInstance.transform.localRotation = Quaternion.identity;
                        prefabInstance.transform.localScale = Vector3.one;
                        var prefabRect = prefabInstance.GetComponent<RectTransform>();
                        if (prefabRect != null)
                        {
                            UGUIParser.SetRectTransform(prefabRefNode, prefabRect);
                        }
                        else
                        {
                            Debug.LogWarning($"引用prefab缺少RectTransform: {prefabAsset.name}");
                        }

                        SetGeneratedNodeKey(prefabInstance, pathKey, GeneratedPrefabReferenceTypeKey, false, activeGeneratedKeys);
                        prefabInstance.transform.SetParent(parentNode.transform, true);
                        ApplyGeneratedSiblingIndex(prefabInstance.transform, prefabRefNode.transform);
                        if (useTargetAsPrefabRoot)
                        {
                            prefabInstance.transform.position -= rootWorldOffset;
                        }
                        prefabInstance.transform.localScale = Vector3.one;
                    }
                }

                SynchronizeGeneratedGroupOpacities(exportRoot, prefabRoot, nodesForBuild);
                NotifyGeneratedHierarchyReady(generatedUiHelpers, generatedUiElements);
                CleanupLegacyUiStringKeys(prefabRoot);
                CleanupStaleGeneratedNodes(prefabRoot.transform, activeGeneratedKeys);
                if (useTargetAsPrefabRoot)
                {
                    // A root Text/TMP prefab owns its RectTransform, so its
                    // rotation must survive the root-normalization step above.
                    prefabRoot.transform.localRotation = rootLocalRotation;
                }
                FitGeneratedContainerHierarchy(prefabRoot.transform);
                var generatedNodeSnapshots = CaptureGeneratedNodeSnapshots(prefabRoot);
                CleanupTransientGeneratedKeys(prefabRoot);

                prefabRoot.name = System.IO.Path.GetFileNameWithoutExtension(prefabAssetPath);
                var uiPrefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabAssetPath);
                if (uiPrefab != null)
                {
                    SaveGeneratedKeysMetadata(prefabAssetPath, uiPrefab, generatedNodeSnapshots);
                }
                return uiPrefab;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (prefabRoot != null)
                {
                    DestroyImmediate(prefabRoot);
                }
            }
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
                if (IsIgnoredRootComponent(type)) continue;
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
            if (requester == null || string.IsNullOrEmpty(requester.ReuseTargetKey))
            {
                return null;
            }

            if (TryGetSharedSpritePath(requester, out var cachedPath))
            {
                return cachedPath.Replace("\\", "/");
            }
            if (!TryEnsureSharedOutputDirectory(!string.IsNullOrWhiteSpace(requester.SourceLayerName) ? requester.SourceLayerName : requester.name, out var sharedDir))
            {
                Debug.LogWarning($"UIImagesOutputDir未配置, 无法复用图层:{requester.name}");
                return null;
            }
            bool preferHighBitDepthAsset = requester.PreferHighBitDepthAsset;
            var sharedAssetPath = PsdLayerNode.ResolveExportedImageAssetPath(sharedDir, requester.ReuseTargetKey, preferHighBitDepthAsset);
            if (!string.IsNullOrWhiteSpace(sharedAssetPath))
            {
                RegisterSharedSprite(requester.ReuseTargetKey, sharedAssetPath);
                return sharedAssetPath;
            }

            ResolveLayerNameLookup();
            if (!layerLookupByName.TryGetValue(requester.ReuseTargetKey, out var targetNode) || targetNode == null)
            {
                var exportedPath = requester.ExportImageAsset(true, sharedDir, requester.ReuseTargetKey, false, auto9Slice, ignoreReference: true);
                if (!string.IsNullOrEmpty(exportedPath))
                {
                    RegisterSharedSprite(requester.ReuseTargetKey, exportedPath);
                    return exportedPath;
                }
                Debug.LogWarning($"引用图片未找到且导出失败: {requester.name} -> {requester.ReuseTargetDisplayName}");
                return null;
            }
            else
            {
                var exportedPath = targetNode.ExportImageAsset(true, sharedDir, requester.ReuseTargetKey, false, auto9Slice);
                if (!string.IsNullOrEmpty(exportedPath))
                {
                    RegisterSharedSprite(requester.ReuseTargetKey, exportedPath);
                    return exportedPath;
                }
            }
            return null;
        }
        internal bool TryGetSharedSpritePath(PsdLayerNode node, out string assetPath)
        {
            assetPath = null;
            if (node == null) return false;
            var key = node.LayerNameLookupKey;
            if (string.IsNullOrEmpty(key)) return false;
            if (sharedSpriteAssets.TryGetValue(key, out var cachedPath))
            {
                if (!PsdTextureAssetUtility.MatchesExportMode(cachedPath, node.PreferHighBitDepthAsset))
                {
                    sharedSpriteAssets.Remove(key);
                    return false;
                }
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
            ResolveLayerNameLookup();
            if (node == null) return false;
            var key = node.LayerNameLookupKey;
            if (string.IsNullOrEmpty(key) || !referencedLayerKeys.Contains(key)) return false;
            if (!TryEnsureSharedOutputDirectory(!string.IsNullOrWhiteSpace(node.SourceLayerName) ? node.SourceLayerName : node.name, out var sharedDir)) return false;
            exportDir = sharedDir;
            fileName = key;
            return true;
        }
        internal void RegisterSharedSprite(string key, string assetPath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(assetPath)) return;
            sharedSpriteAssets[key] = assetPath.Replace("\\", "/");
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

            var generatedKey = target.GetComponent<PsdGeneratedKey>();
            return generatedKey != null
                && generatedKey.IsContainer
                && string.Equals(generatedKey.TypeKey, GeneratedContainerTypeKey, StringComparison.Ordinal);
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
                    uiHelpers[i].ParseAndAttachUIElements();
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
#if UNITY_6000_0_OR_NEWER
                EntityId
#else
                int
#endif
            >();
            foreach (var item in uiHelpers)
            {
                if (item == null) continue;

                var dependencies = item.GetDependencies();
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
            var parser = UGUIParser.Instance;
            if (parser == null || layerNodes == null || layerNodes.Length == 0) return;

            for (int i = 0; i < layerNodes.Length; i++)
            {
                var node = layerNodes[i];
                if (node == null) continue;

                var preferredType = parser.ResolvePreferredUIType(node.UIType);
                if (preferredType == node.UIType) continue;

                node.SetUIType(preferredType, false);
            }

            ApplyGroupDerivedStates(logOpacityConflicts: true);

            for (int i = 0; i < layerNodes.Length; i++)
            {
                var node = layerNodes[i];
                if (node == null) continue;
                node.RefreshUIHelper(false);
            }
        }

        private static void ResolveDependencyClaims(UIHelperBase[] uiHelpers)
        {
            if (uiHelpers == null || uiHelpers.Length < 1) return;

            var dependencyOwnerIds = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < uiHelpers.Length; i++)
            {
                var uiHelper = uiHelpers[i];
                if (uiHelper == null) continue;

                var dependencies = uiHelper.GetDependencies();
                if (dependencies == null) continue;

                int ownerId = uiHelper.GetInstanceID();
                for (int j = 0; j < dependencies.Length; j++)
                {
                    var dependency = dependencies[j];
                    if (dependency == null) continue;

                    int dependencyId = UIHelperBase.GetDependencyClaimId(dependency);
                    if (dependencyId == 0) continue;

                    if (!dependencyOwnerIds.TryGetValue(dependencyId, out var ownerIds))
                    {
                        ownerIds = new HashSet<int>();
                        dependencyOwnerIds.Add(dependencyId, ownerIds);
                    }
                    ownerIds.Add(ownerId);
                }
            }

            for (int i = 0; i < uiHelpers.Length; i++)
            {
                uiHelpers[i]?.ResolveDependencyClaims(dependencyOwnerIds);
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
                var assetPath = NormalizeToAssetPath(item); // Fatcat定制: 路径标准化(软链接映射)
                var texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
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

                texImporter.sRGBTexture = true;
                texImporter.textureCompression = TextureImporterCompression.Uncompressed;
                PsdTextureAssetUtility.ApplyPrecisionImportSettings(texImporter, preferHighBitDepth);
                texImporter.SaveAndReimport();
            }
        }
        internal static void ApplySpriteNineSlice(string spriteAssetPath)
        {
            spriteAssetPath = NormalizeToAssetPath(spriteAssetPath); // Fatcat定制: 路径标准化(软链接映射)
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
        /// 从 PSD 所在目录向上查找 Slot_XXXX 根目录(Fatcat定制)
        /// </summary>
        private static string DetectSlotRoot(string psdDir)
        {
            var dir = psdDir;
            while (!string.IsNullOrEmpty(dir))
            {
                var dirName = Path.GetFileName(dir);
                if (System.Text.RegularExpressions.Regex.IsMatch(dirName, @"^[Ss]lot[_-]?\d+$"))
                {
                    return dir;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir || string.IsNullOrEmpty(parent)) break;
                dir = parent;
            }
            return psdDir; // fallback: 使用 PSD 所在目录
        }

        /// <summary>
        /// 将工程外路径(软链接)标准化为 Assets 相对路径(Fatcat定制)。
        /// 例如 ../ResFatcat/... → Assets/UsrAssets/Res/...
        /// </summary>
        internal static string NormalizeToAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith("Assets/") || path.StartsWith("Assets\\")) return path;

            var fullPath = Path.GetFullPath(path);
            var assetsFullPath = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;

            // 情况1: 直接在 Assets 目录下
            if (fullPath.StartsWith(assetsFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets/" + fullPath.Substring(assetsFullPath.Length)
                    .Replace("\\", "/").TrimEnd('/');
            }

            // 情况2: 工程外路径（通过软链接访问）。
            // 收集 Assets 下最多3层的子目录，用相对路径前缀试匹配。
            // 3层足够覆盖 Assets/UsrAssets/Res 这种两级软链接。
            var parts = fullPath.Replace("\\", "/").TrimEnd('/').Split('/');
            var assetPrefixes = new List<string>();
            CollectAssetPrefixes(Application.dataPath, "Assets", 0, assetPrefixes);

            foreach (var prefix in assetPrefixes)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == ".." || parts[i] == ".") continue;
                    var suffix = string.Join("/", parts, i, parts.Length - i);
                    var candidate = prefix + "/" + suffix;
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return path;
        }

        private static void CollectAssetPrefixes(string dirFullPath, string dirRelativePath, int depth, List<string> results)
        {
            if (depth >= 3) return;
            try
            {
                foreach (var subDir in Directory.GetDirectories(dirFullPath))
                {
                    var dirName = Path.GetFileName(subDir);
                    var relPath = dirRelativePath + "/" + dirName;
                    results.Add(relPath);
                    CollectAssetPrefixes(subDir, relPath, depth + 1, results);
                }
            }
            catch (System.Exception)
            {
                // 跳过无权限访问的目录
            }
        }

        /// <summary>
        /// 从 PSD 文件名提取关键字，映射到 Image 子目录。(Fatcat定制)
        /// slot_1431_board → board → Image/Board
        /// slot_6096_icon_high_1 → icon → Image/Icon
        /// </summary>
        internal static string GetImageSubdirFromPsdName(string psdName)
        {
            // 去掉 img_ / img9_ 前缀（项目纹理命名规范），再匹配 slot_XXXX_keyword
            var cleanName = System.Text.RegularExpressions.Regex.Replace(psdName, @"^img9?_", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = System.Text.RegularExpressions.Regex.Match(cleanName, @"^[Ss]lot[_-]?\d+[_-](.+)$");
            if (match.Success)
            {
                var suffix = match.Groups[1].Value;
                var keyword = suffix.Split('_')[0].ToLower();
                return KeywordToImageSubdir(keyword);
            }
            // Fallback: 直接按名称关键字匹配（兼容非 slot_ 前缀的命名，如 Cocos 导出风格）
            var lowerName = cleanName.ToLower();
            if (lowerName.Contains("board")) return "Image/Board";
            if (lowerName.Contains("icon")) return "Image/Icon";
            if (lowerName.Contains("award")) return "Image/Award";
            if (lowerName.Contains("tex")) return "Image/Tex";
            if (lowerName.Contains("paytable")) return "Image/Paytable";
            if (lowerName.Contains("material")) return "Image/Material";
            return "Image";
        }

        private static string KeywordToImageSubdir(string keyword)
        {
            return keyword switch
            {
                "board" => "Image/Board",
                "icon" => "Image/Icon",
                "award" => "Image/Award",
                "tex" => "Image/Tex",
                "paytable" => "Image/Paytable",
                "material" => "Image/Material",
                _ => "Image/" + char.ToUpper(keyword[0]) + keyword.Substring(1)
            };
        }

        /// <summary>
        /// 获取UIForm对应的图片导出目录
        /// </summary>
        /// <returns></returns>
        internal string GetUIFormImagesOutputDir()
        {
            // Fatcat定制: 按 PSD 文件名关键字自动分类到 Image/Board, Image/Icon 等子目录
            var subDir = GetImageSubdirFromPsdName(uiFormName);
            return Path.Combine(Psd2UIFormSettings.Instance.UIImagesOutputDir, subDir).Replace("\\", "/");
        }

    }
    internal static class PathExtensions
    {
        internal static string GetRelativePath(string relativeTo, string path)
        {
#if UNITY_2021_1_OR_NEWER
            return Path.GetRelativePath(relativeTo, path);
#else
            relativeTo = Path.GetFullPath(relativeTo.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            path = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            Uri fromUri = new Uri(Path.GetFullPath(relativeTo));
            Uri toUri = new Uri(Path.GetFullPath(path));

            if (fromUri.Scheme != toUri.Scheme)
            {
                return path;
            }

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
#endif
        }
    }
}
#endif





