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

namespace UGF.EditorTools.Psd2UGUI
{
    [CustomEditor(typeof(Psd2UIFormConverter))]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class Psd2UIFormConverterInspector : UnityEditor.Editor
    {
        Psd2UIFormConverter targetLogic;

        GUIContent parsePsd2NodesBt;
        GUIContent exportUISpritesBt;
        GUIContent generateUIFormBt;
        GUILayoutOption btHeight;
        bool metadataDebugFoldout;
        int metadataDebugSelection;
        bool metadataDebugShowJson;
        Vector2 metadataDebugScroll;
        private void OnEnable()
        {
            btHeight = GUILayout.Height(30);
            targetLogic = target as Psd2UIFormConverter;
            parsePsd2NodesBt = new GUIContent("解析psd图层", "把psd图层解析为可编辑节点树");
            exportUISpritesBt = new GUIContent("导出Images", "导出勾选的psd图层为碎图");
            generateUIFormBt = new GUIContent("生成UIForm", "根据解析后的节点树生成UIForm Prefab");
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
        private void OnDisable()
        {
            Psd2UIFormSettings.Save();
        }

        public override void OnInspectorGUI()
        {
            bool requestExitGUI = false;

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
                // ====== 插入目标引擎选择下拉框 ======
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("目标渲染引擎:", EditorStyles.boldLabel, GUILayout.Width(150));
                Psd2UIFormSettings.Instance.TargetEngine = (ExportTargetEngine)EditorGUILayout.EnumPopup(Psd2UIFormSettings.Instance.TargetEngine);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
                // ===================================

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

        public override bool HasPreviewGUI()
        {
            return targetLogic.BindPsdAsset != null;
        }
        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            GUI.DrawTexture(r, targetLogic.BindPsdAsset.texture, ScaleMode.ScaleToFit);
            //base.OnPreviewGUI(r, background);
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
        internal static Psd2UIFormConverter Instance { get; private set; }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] internal string psdAssetChangeTime;//文件修改时间标识
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("UIForm名字")][SerializeField] private string uiFormName;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Tooltip("关联的psd文件")][SerializeField] private UnityEngine.Sprite psdAsset;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private string psdAssetPath;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private string slotRootPath;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [Header("Debug:")][SerializeField] bool drawLayerRectGizmos = true;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] UnityEngine.Color drawLayerRectGizmosColor = UnityEngine.Color.gray;
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
            public GameObject InstanceObject;
            public string Key;
            public string TypeKey;
            public bool IsContainer;
        }
        private sealed class LayerUITypeSnapshot
        {
            public GUIType UIType;
            public GUIType RoleUIType;
        }
        internal bool Initiated => psdInstance != null;
        internal string PsdAssetName => psdAsset != null ? AssetDatabase.GetAssetPath(psdAsset) : psdAssetPath;
        internal UnityEngine.Sprite BindPsdAsset => psdAsset;
        internal Vector2Int UIFormCanvasSize => psdInstance != null ? new Vector2Int(psdInstance.Width, psdInstance.Height) : Vector2Int.zero;
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
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        private void Start()
        {
            if (!string.IsNullOrWhiteSpace(PsdAssetName))
            {
                RefreshNodesBindLayer();
            }
        }


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
                PsdLayerNode clickNode = FindTopMostPsdLayerNode(worldHitPos, transform);
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
        private void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            if (Event.current == null) return;
            var node = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
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
                        foreach (var item in selectObjs)
                        {
                            var psdLayerNode = item?.GetComponent<PsdLayerNode>();
                            if (psdLayerNode != null)
                            {
                                psdLayerNode.SetUIType(selectUIType);
                                EditorUtility.SetDirty(psdLayerNode);
                            }
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

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            if (this.psdInstance != null)
            {
                psdInstance.Dispose();
                psdInstance = null;
            }
        }

        private void RefreshNodesBindLayer(string psdFilePath = null)
        {
            var resolvedPsdPath = psdFilePath ?? PsdAssetName;
            if (psdInstance == null)
            {
                if (string.IsNullOrEmpty(resolvedPsdPath) || !File.Exists(resolvedPsdPath))
                {
                    Debug.LogError($"刷新节点绑定图层失败! psd文件不存在:{resolvedPsdPath}");
                    return;
                }
                try
                {
                    EditorUtility.DisplayProgressBar("文件加载中", string.Format("正在读取psd文件:{0}\n文件过大会影响读取速度,建议通过栅格化图层减小psd大小", resolvedPsdPath), 0.5f);
                    psdInstance = PsdDocument.Create(resolvedPsdPath);
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

            var spRender = gameObject.GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();
            if (this.psdAsset == null && !string.IsNullOrEmpty(resolvedPsdPath))
            {
                this.psdAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(resolvedPsdPath);
                if (this.psdAsset == null)
                {
                    var allSubAssets = AssetDatabase.LoadAllAssetsAtPath(resolvedPsdPath);
                    foreach (var asset in allSubAssets)
                    {
                        if (asset is UnityEngine.Sprite sprite) { this.psdAsset = sprite; break; }
                    }
                }
                if (this.psdAsset == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(resolvedPsdPath);
                    if (tex != null)
                    {
                        this.psdAsset = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        this.psdAsset.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
            }
            spRender.sprite = this.psdAsset;
        }

        [MenuItem("Assets/Psd2UIForm Editor", priority = 0)]
        static void Psd2UIFormPrefabMenu()
        {
            if (Selection.activeObject == null) return;
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (Path.GetExtension(assetPath).ToLower().CompareTo(".psd") != 0)
            {
                Debug.LogWarning($"选择的文件({assetPath})不是psd格式, 工具只支持psd转换为UIForm");
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
        static bool ValidatePsd2UIFormPrefabMenu()
        {
            if (Selection.activeObject != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(assetPath) && Path.GetExtension(assetPath).ToLower().CompareTo(".psd") == 0)
                {
                    return true;
                }
            }

            return false;
        }
        internal bool CheckPsdAssetHasChanged()
        {
            if (psdAsset == null) return false;
            var fileTag = GetAssetChangeTag(PsdAssetName);
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
            PrefabStageUtility.OpenPrefab(prefabName);
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
        internal static bool ParsePsd2LayerPrefab(string psdFile, Psd2UIFormConverter instanceRoot = null, bool keepExistingUIType = false)
        {
            if (!File.Exists(psdFile))
            {
                Debug.LogError($"Error: Psd文件不存在:{psdFile}");
                return false;
            }
            var texImporter = AssetImporter.GetAtPath(psdFile) as TextureImporter;
            if (texImporter == null)
            {
                Debug.LogError($"Error: 无法获取PSD导入设置:{psdFile}");
                return false;
            }
            if (texImporter.textureType != TextureImporterType.Sprite || texImporter.spriteImportMode != SpriteImportMode.Single)
            {
                texImporter.textureType = TextureImporterType.Sprite;
                texImporter.spriteImportMode = SpriteImportMode.Single;
                texImporter.mipmapEnabled = false;
                texImporter.alphaIsTransparency = true;
                texImporter.SaveAndReimport();
            }

            var prefabFile = GetPsdLayerPrefabPath(psdFile);
            var rootName = Path.GetFileNameWithoutExtension(prefabFile);

            bool needDestroyInstance = instanceRoot == null;
            if (instanceRoot != null)
            {
                instanceRoot.SetPsdAsset(psdFile);
                ParsePsdLayer2Root(psdFile, instanceRoot, keepExistingUIType);
                return true;
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
                ParsePsdLayer2Root(psdFile, rootLayer, keepExistingUIType);

                PrefabUtility.SaveAsPrefabAsset(rootLayer.gameObject, prefabFile, out bool savePrefabSuccess);
                if (needDestroyInstance) GameObject.DestroyImmediate(rootLayer.gameObject);
                AssetDatabase.Refresh();
                var currentStagePath = StageUtility.GetCurrentStage()?.assetPath;
                bool isSameStagePrefab = !string.IsNullOrWhiteSpace(currentStagePath)
                    && AssetDatabase.GUIDFromAssetPath(currentStagePath) == AssetDatabase.GUIDFromAssetPath(prefabFile);
                if (savePrefabSuccess && !isSameStagePrefab)
                {
                    OpenPrefab(prefabFile);
                }

                return savePrefabSuccess;
            }
        }
        private static void ParsePsdLayer2Root(string psdFile, Psd2UIFormConverter converter, bool keepExistingUIType = false)
        {
            EditorUtility.DisplayProgressBar("解析PSD", $"正在解析{psdFile}", 0);
            var preservedUITypes = keepExistingUIType ? converter.CaptureLayerUITypeSnapshot() : null;
            //清空已有节点重新解析
            for (int i = converter.transform.childCount - 1; i >= 0; i--)
            {
                GameObject.DestroyImmediate(converter.transform.GetChild(i).gameObject);
            }

            try
            {
                using (var psd = PsdDocument.Create(psdFile))
                {
                    int totalCount = psd.CountAllLayers();
                    int parsedCount = 0;
                    int nextBindIndex = 0;
                    for (int i = 0; i < psd.Childs.Length; i++)
                    {
                        var rootLayer = psd.Childs[i] as PsdLayer;
                        if (rootLayer == null) continue;
                        ParsePsdLayerRecursive(rootLayer, converter.transform, ref nextBindIndex, ref parsedCount, totalCount);
                    }
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                return;
            }

            converter.psdAssetChangeTime = GetAssetChangeTag(psdFile);
            if (converter.psdInstance != null)
            {
                converter.psdInstance.Dispose();
                converter.psdInstance = null;
            }
            converter.RefreshNodesBindLayer(psdFile);
            if (keepExistingUIType)
            {
                converter.RestoreLayerUITypeSnapshot(preservedUITypes);
            }
            var childrenNodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            foreach (var item in childrenNodes)
            {
                item.RefreshUIHelper(false);
            }
            EditorUtility.SetDirty(converter.gameObject);
            EditorUtility.ClearProgressBar();
        }
        private void SetPsdAsset(string psdFile)
        {
            this.psdAssetPath = psdFile;
            this.psdAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(psdFile);
            if (this.psdAsset == null)
            {
                var allSubAssets = AssetDatabase.LoadAllAssetsAtPath(psdFile);
                foreach (var asset in allSubAssets)
                {
                    if (asset is UnityEngine.Sprite sprite) { this.psdAsset = sprite; break; }
                }
            }

            if (string.IsNullOrWhiteSpace(this.uiFormName))
            {
                this.uiFormName = this.psdAsset != null ? this.psdAsset.name : System.IO.Path.GetFileNameWithoutExtension(psdFile);
            }

            // 自动检测 Slot 根目录（供 Prefab 导出路径使用）
            var psdDir = Path.GetDirectoryName(psdFile).Replace("\\", "/");
            this.slotRootPath = DetectSlotRoot(psdDir);

            if (string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.UIImagesOutputDir))
            {
                Psd2UIFormSettings.Instance.UIImagesOutputDir = psdDir;
            }
        }

        /// <summary>
        /// 从 PSD 所在目录向上查找 Slot_XXXX 根目录
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
        /// 从 PSD 文件名提取关键字，映射到 Image 子目录。
        /// slot_1431_board → board → Image/Board
        /// slot_6096_icon_high_1 → icon → Image/Icon
        /// </summary>
        private static string GetImageSubdirFromPsdName(string psdName)
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
        /// 获取解析好的psd layers文件
        /// </summary>
        /// <param name="psd"></param>
        /// <returns></returns>
        private static string GetPsdLayerPrefabPath(string psd)
        {
            return Path.Combine(Path.GetDirectoryName(psd), Path.GetFileNameWithoutExtension(psd) + "_psd_layers_parsed.prefab");
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
            if (UGUIParser.Instance.TryParse(layerNode, out var initRule, out var roleType))
            {
                layerNode.SetResolvedTypes(initRule.UIType, roleType, false);
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

        /// <summary>
        /// 导出psd图层为Sprites碎图
        /// </summary>
        /// <param name="psdAssetName"></param>
        internal void ExportSprites()
        {
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

            // ====== 拦截逻辑开始 (Cocos 导出分支) ======
            if (Psd2UIFormSettings.Instance.TargetEngine == ExportTargetEngine.CocosStudio)
            {
                if (Psd2UIFormSettings.Instance.UseUIFormOutputDir)
                {
                    CocosStudioExporter.ExportToCsd(this, Psd2UIFormSettings.Instance.UIFormOutputDir);
                }
                else
                {
                    var defaultDir = !string.IsNullOrWhiteSpace(slotRootPath) ? slotRootPath + "/Prefab/" : "Assets";
                    string lastSaveDir = string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.LastUIFormOutputDir) ? defaultDir : Psd2UIFormSettings.Instance.LastUIFormOutputDir;
                    string selectDir = EditorUtility.SaveFolderPanel("保存目录", lastSaveDir, null);
                    if (!string.IsNullOrWhiteSpace(selectDir))
                    {
                        if (!selectDir.StartsWith("Assets/"))
                            selectDir = PathExtensions.GetRelativePath(Directory.GetParent(Application.dataPath).FullName, selectDir);
                        Psd2UIFormSettings.Instance.LastUIFormOutputDir = selectDir;
                        CocosStudioExporter.ExportToCsd(this, selectDir);
                    }
                }
                return; 
            }
            // ====== 拦截逻辑结束 ======

            if (Psd2UIFormSettings.Instance.UseUIFormOutputDir)
            {
                ExportUIPrefab(rootNode, Psd2UIFormSettings.Instance.UIFormOutputDir);
            }
            else
            {
                var defaultDir = !string.IsNullOrWhiteSpace(slotRootPath) ? slotRootPath + "/Prefab/" : "Assets";
                string lastSaveDir = string.IsNullOrWhiteSpace(Psd2UIFormSettings.Instance.LastUIFormOutputDir) ? defaultDir : Psd2UIFormSettings.Instance.LastUIFormOutputDir;
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
            // Slot 根目录下的 Prefab/ 作为默认，但允许用户通过设置或选择器覆盖
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
            // 遵循 prefab_slotXXXX_xxx 命名规范: 去掉 slot_ 中的下划线，加 prefab_ 前缀
            var baseName = System.Text.RegularExpressions.Regex.Replace(
                uiFormName, @"^[Ss]lot_(\d+)", "slot$1");
            var prefabFileName = baseName.StartsWith("prefab_", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : "prefab_" + baseName;
            var prefabName = Path.Combine(outputDir, $"{prefabFileName}.prefab").Replace("\\", "/");
            // 标准化目录路径（目录已由图片导出创建，Directory.Exists 可验证），再拼接文件名
            var prefabDir = Path.GetDirectoryName(prefabName).Replace("\\", "/");
            var normalizedDir = NormalizeToAssetPath(prefabDir);
            prefabName = normalizedDir + "/" + Path.GetFileName(prefabName);
            if (root == this.transform && File.Exists(prefabName))
            {
                if (!EditorUtility.DisplayDialog("警告", $"prefab文件已存在, 是否覆盖:{prefabName}", "覆盖生成", "取消生成"))
                {
                    return false;
                }
                //AssetDatabase.DeleteAsset(prefabName);
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
                PrefabUtility.SaveAsPrefabAsset(uiFormRoot, prefabName);
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
                uiElement.transform.SetParent(parentNode.transform, true);
                uiElement.transform.position += canvasPosition;
                uiElement.transform.localScale = Vector3.one;
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
                    prefabInstance.transform.position += canvasPosition;
                    prefabInstance.transform.localScale = Vector3.one;
                }
            }

            CleanupLegacyUiStringKeys(uiFormRoot);
            CleanupStaleGeneratedNodes(uiFormRoot.transform, activeGeneratedKeys);
            FitGeneratedContainerHierarchy(uiFormRoot.transform);
            var generatedNodeSnapshots = CaptureGeneratedNodeSnapshots(uiFormRoot);
            CleanupTransientGeneratedKeys(uiFormRoot);

            var uiPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(uiFormRoot, prefabName, InteractionMode.UserAction);
            if (uiPrefab != null)
            {
                SaveGeneratedKeysMetadata(prefabName, generatedNodeSnapshots, incrementalScopePathKey);
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
                    bool useExistingUiNode = targetNode != null;
                    if (!useExistingUiNode)
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
                        SetGeneratedNodeKey(targetNode, nodeKey, GeneratedContainerTypeKey, true, activeGeneratedKeys);
                    }
                    result = targetNode;
                }
            }
            return result;
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
            foreach (var generatedKey in generatedKeys)
            {
                if (generatedKey == null) continue;
                snapshots.Add(new GeneratedNodeSnapshot
                {
                    InstanceObject = generatedKey.gameObject,
                    Key = generatedKey.Key,
                    TypeKey = generatedKey.TypeKey,
                    IsContainer = generatedKey.IsContainer,
                });
            }
            return snapshots;
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

        private void SaveGeneratedKeysMetadata(string prefabAssetPath, List<GeneratedNodeSnapshot> snapshots, string incrementalScopePathKey = null)
        {
            try
            {
                if (snapshots == null || snapshots.Count < 1)
                {
                    SaveGeneratedMetadataEntries(prefabAssetPath, null);
                    return;
                }

                var metadataEntries = new List<GeneratedMetadataEntry>();
                foreach (var snapshot in snapshots)
                {
                    if (snapshot == null || snapshot.InstanceObject == null) continue;

                    var sourceGo = PrefabUtility.GetCorrespondingObjectFromSource(snapshot.InstanceObject) as GameObject;
                    if (sourceGo == null) continue;

                    var globalId = GlobalObjectId.GetGlobalObjectIdSlow(sourceGo).ToString();
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
            foreach (var component in components)
            {
                if (component == null) continue;
                var type = component.GetType();
                if (type == typeof(Transform)
                    || type == typeof(RectTransform)
                    || type == typeof(CanvasRenderer)
                    || type == typeof(UIStringKey)
                    || type == typeof(PsdGeneratedKey))
                {
                    continue;
                }
                return type;
            }
            return null;
        }

        private static bool IsLikelyMatchingGeneratedRoot(GameObject existing, GUIType uiType)
        {
            var rule = UGUIParser.Instance?.GetRule(uiType);
            if (rule == null || rule.UIPrefab == null || existing == null) return false;

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
                    UIType = node.UIType,
                    RoleUIType = node.RoleUIType
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

                node.SetResolvedTypes(snapshot.UIType, snapshot.RoleUIType, false);
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
                DestroyImmediate(conflictingNode);
            }

            var generatedName = uiHelper.LayerNode.GetGeneratedObjectName();
            var reusableLegacyNode = FindLegacyChildByName(parent, generatedName, false, uiHelper.LayerNode.UIType);
            if (reusableLegacyNode != null)
            {
                return reusableLegacyNode;
            }

            if (!string.Equals(generatedName, uiHelper.name, StringComparison.Ordinal))
            {
                return FindLegacyChildByName(parent, uiHelper.name, false, uiHelper.LayerNode.UIType);
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
            if (!TryEnsureSharedOutputDirectory(out var sharedDir)) return;
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
                var exportedPath = targetNode.ExportImageAsset(true, sharedDir, key, false, true);
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
                if (useTargetAsPrefabRoot)
                {
                    if (prefabRoot != null)
                    {
                        if (IsLikelyMatchingGeneratedRoot(prefabRoot, rootHelper.LayerNode.UIType))
                        {
                            prefabRoot = rootHelper.CreateUI(prefabRoot);
                        }
                        else
                        {
                            DestroyImmediate(prefabRoot);
                            prefabRoot = null;
                        }
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
                        && prefabRoot.GetComponent<RectTransform>() != null
                        && GetPrimaryRootComponentType(prefabRoot) == null;
                    if (!canReuseContainerRoot)
                    {
                        if (prefabRoot != null)
                        {
                            DestroyImmediate(prefabRoot);
                        }
                        prefabRoot = new GameObject(prefabRootName, typeof(RectTransform));
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
                    prefabRoot.transform.localPosition = Vector3.zero;
                    prefabRoot.transform.localRotation = Quaternion.identity;
                    prefabRoot.transform.localScale = Vector3.one;
                }

                var activeGeneratedKeys = new HashSet<string>(StringComparer.Ordinal);
                int curIdx = 0;
                int totalCount = uiHelpers.Length;
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
                    uiElement.transform.SetParent(parentNode.transform, true);
                    if (useTargetAsPrefabRoot)
                    {
                        uiElement.transform.position -= rootWorldOffset;
                    }
                    uiElement.transform.localScale = Vector3.one;
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
                        if (useTargetAsPrefabRoot)
                        {
                            prefabInstance.transform.position -= rootWorldOffset;
                        }
                        prefabInstance.transform.localScale = Vector3.one;
                    }
                }

                CleanupLegacyUiStringKeys(prefabRoot);
                CleanupStaleGeneratedNodes(prefabRoot.transform, activeGeneratedKeys);
                FitGeneratedContainerHierarchy(prefabRoot.transform);
                var generatedNodeSnapshots = CaptureGeneratedNodeSnapshots(prefabRoot);
                CleanupTransientGeneratedKeys(prefabRoot);

                var uiPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(prefabRoot, prefabAssetPath, InteractionMode.UserAction);
                if (uiPrefab != null)
                {
                    SaveGeneratedKeysMetadata(prefabAssetPath, generatedNodeSnapshots);
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
        internal string GetOrCreateSharedSprite(PsdLayerNode requester, bool auto9Slice)
        {
            if (requester == null || string.IsNullOrEmpty(requester.ReuseTargetKey))
            {
                return null;
            }

            if (sharedSpriteAssets.TryGetValue(requester.ReuseTargetKey, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath.Replace("\\", "/");
            }
            if (!TryEnsureSharedOutputDirectory(out var sharedDir))
            {
                Debug.LogWarning($"SharedAssetsOutput未配置, 无法复用图层:{requester.name}");
                return null;
            }
            var sharedAssetPath = Path.Combine(sharedDir, requester.ReuseTargetKey + ".png").Replace("\\", "/");
            if (File.Exists(sharedAssetPath))
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
            if (!TryEnsureSharedOutputDirectory(out var sharedDir)) return false;
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

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ApplyWorldRect(rectTransform, worldRect);
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

            var dependInstIds = new HashSet<int>();
            foreach (var item in uiHelpers)
            {
                var dependencies = item.GetDependencies();
                if (dependencies == null) continue;

                foreach (var depend in dependencies)
                {
                    if (depend == null) continue;

                    int dependId = depend.gameObject.GetInstanceID();
                    dependInstIds.Add(dependId);
                }
            }
            for (int i = uiHelpers.Length - 1; i >= 0; i--)
            {
                var uiHelper = uiHelpers[i];
                if (dependInstIds.Contains(uiHelper.gameObject.GetInstanceID()))
                {
                    ArrayUtility.RemoveAt(ref uiHelpers, i);
                }
            }
            return uiHelpers;
        }
        /// <summary>
        /// 将文件或目录路径标准化为 Assets/ 相对路径。处理三种情况：
        /// 1. 已经是 Assets/ 路径 → 直接返回
        /// 2. 在 Assets 目录下的绝对路径 → 反推 Assets/... 相对路径
        /// 3. 通过软链接（如 Assets/UsrAssets/Res → ../../ResFatcat）访问的工程外路径
        ///    → 拆分路径逐级匹配，找到 Assets 目录下映射到的真实路径
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
        /// 把图片设置为为Sprite或Texture类型
        /// </summary>
        /// <param name="dir"></param>
        internal static void ConvertTexturesType(string[] texAssets, bool isImage = true)
        {
            foreach (var item in texAssets)
            {
                var assetPath = NormalizeToAssetPath(item);
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
                }

                texImporter.SaveAndReimport();
            }
        }
        internal static void ApplySpriteNineSlice(string spriteAssetPath)
        {
            var assetPath = NormalizeToAssetPath(spriteAssetPath);
            var texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (texImporter == null)
            {
                return;
            }
            bool revertReadable = !texImporter.isReadable;
            if (revertReadable)
            {
                texImporter.isReadable = true;
                texImporter.SaveAndReimport();
                texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            }
            if (texImporter == null)
            {
                return;
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null && texImporter.spriteBorder == Vector4.zero)
            {
                texImporter.spriteBorder = UGUIParser.CalculateTexture9SliceBorder(sprite.texture);
                texImporter.SaveAndReimport();
            }
            if (revertReadable)
            {
                texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
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
            // 按 PSD 文件名关键字自动分类到 Image/Board, Image/Icon 等子目录
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