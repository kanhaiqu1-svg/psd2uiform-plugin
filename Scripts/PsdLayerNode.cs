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
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using PsdObjectId = UnityEngine.EntityId;
#else
using PsdObjectId = System.Int32;
#endif
using UnityEngine.Experimental.Rendering;
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Linq;
using cn.efunstudio.psdreader.PsdParser;

namespace UGF.EditorTools.Psd2UGUI
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PsdLayerNode))]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class PsdLayerNodeInspector : Editor
    {
        private const float CopyButtonWidth = 52f;
        //private static readonly GUIStyle ReadOnlyTextAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        PsdLayerNode targetLogic;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnEnable()
        {
            targetLogic = target as PsdLayerNode;
            targetLogic.RefreshLayerTexture();
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            var newUIType = (GUIType)EditorGUILayout.EnumPopup("UI Type", targetLogic.UIType);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(targets, "Change UI Type");
                bool changed = false;

                foreach (var item in targets)
                {
                    if (item == null) continue;
                    var node = item as PsdLayerNode;
                    if (node == null) continue;
                    node.SetUIType(newUIType, false);
                    changed = true;
                }

                if (changed)
                {
                    Psd2UIFormConverter.Instance?.RefreshAllLayerNodeHelpers();
                }
            }

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("导出图片资源"))
                {
                    foreach (var item in targets)
                    {
                        if (item == null) continue;

                        (item as PsdLayerNode)?.ExportImageAsset();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("更新此节点到已生成 UIForm"))
            {
                Psd2UIFormConverter.Instance.GenerateUIForm(targetLogic);
            }
            if (GUILayout.Button("导出为独立 prefab"))
            {
                Psd2UIFormConverter.Instance.ExportAsPrefab(targetLogic);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layer Data", EditorStyles.boldLabel);
            var rect = targetLogic.UnityRect;
            DrawVector2FieldReadOnly("Position", rect.position);
            DrawVector2FieldReadOnly("Size", rect.size);
            var rotation = targetLogic.UnityRotationEulerAngles;
            DrawVector3FieldReadOnly("Rotation", rotation);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(CopyButtonWidth)))
            {
                CopyRectTransformClipboard(rect.position, rect.size, rotation);
            }
            EditorGUILayout.EndHorizontal();

            if (targetLogic.ParseTextLayerInfo(out var textInfo))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Text Data", EditorStyles.boldLabel);
                DrawReadOnlyField("Text Content", textInfo.Text ?? string.Empty);
                DrawReadOnlyField("Text Type", textInfo.IsParagraphText ? "Paragraph" : "Point");
                DrawReadOnlyField("Alignment", textInfo.Justification.ToString());
                DrawReadOnlyField("Style Runs", (textInfo.StyleRuns?.Length ?? 0).ToString(CultureInfo.InvariantCulture));
                DrawReadOnlyField("Paragraph Runs", (textInfo.ParagraphRuns?.Length ?? 0).ToString(CultureInfo.InvariantCulture));
                DrawReadOnlyField("Font Name", textInfo.FontName ?? string.Empty);
                DrawReadOnlyField("Font Size", FormatFloat(textInfo.FontSize));
                DrawReadOnlyField("Font Style", textInfo.FontStyle.ToString());
                DrawReadOnlyField("TMP Font Style", textInfo.TMPFontStyle.ToString());
                DrawReadOnlyField("Character Spacing", FormatFloat(textInfo.CharacterSpacing));
                DrawReadOnlyField("Line Spacing", textInfo.IsAutoLineSpacing ? "Auto" : FormatFloat(textInfo.LineSpacing));
                DrawReadOnlyField("Auto Line Spacing", textInfo.IsAutoLineSpacing ? "True" : "False");
                DrawReadOnlyField("Paragraph Spacing", FormatParagraphSpacing(textInfo.ParagraphRuns));
                DrawColorFieldWithCopy("Color", textInfo.Color);
                DrawTextEffectsInfo(in textInfo);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawVector2FieldReadOnly(string label, Vector2 value)
        {
            DrawSelectableReadOnlyField(label, FormatVector2(value));
        }

        private void DrawVector3FieldReadOnly(string label, Vector3 value)
        {
            DrawSelectableReadOnlyField(label, FormatVector3(value));
        }

        private static void DrawSelectableReadOnlyField(string label, string value)
        {
            var controlRect = EditorGUILayout.GetControlRect();
            var valueRect = EditorGUI.PrefixLabel(controlRect, new GUIContent(label));
            EditorGUI.SelectableLabel(valueRect, value ?? string.Empty, EditorStyles.textField);
        }

        private void CopyRectTransformClipboard(Vector2 positionValue, Vector2 sizeValue, Vector3 rotationValue)
        {
            var temp = new GameObject("RectTransformClipboard", typeof(RectTransform));
            temp.hideFlags = HideFlags.HideAndDontSave;
            var rectTransform = temp.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = positionValue;
            rectTransform.sizeDelta = sizeValue;
            rectTransform.localEulerAngles = rotationValue;
            rectTransform.localScale = Vector3.one;
            ComponentUtility.CopyComponent(rectTransform);
            DestroyImmediate(temp);
        }


        private void DrawReadOnlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            var controlRect = EditorGUILayout.GetControlRect();
            var valueRect = EditorGUI.PrefixLabel(controlRect, new GUIContent(label));
            EditorGUI.SelectableLabel(valueRect, value ?? string.Empty, EditorStyles.textField);
            if (GUILayout.Button("Copy", GUILayout.Width(CopyButtonWidth)))
            {
                GUIUtility.systemCopyBuffer = value;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawColorFieldWithCopy(string label, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ColorField(label, color);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("RGB", GUILayout.Width(CopyButtonWidth)))
            {
                GUIUtility.systemCopyBuffer = ColorUtility.ToHtmlStringRGB(color);
            }
            if (GUILayout.Button("A", GUILayout.Width(CopyButtonWidth)))
            {
                GUIUtility.systemCopyBuffer = color.a.ToString("0.###", CultureInfo.InvariantCulture);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatParagraphSpacing(TextParagraphRunInfo[] runs)
        {
            if (runs == null || runs.Length == 0)
            {
                return "0 / 0";
            }

            string result = string.Empty;
            for (int i = 0; i < runs.Length; i++)
            {
                if (i > 0)
                {
                    result += ", ";
                }

                result += FormatFloat(runs[i].SpaceBefore) + " / " + FormatFloat(runs[i].SpaceAfter);
            }

            return result;
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)})";
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)})";
        }

        private void DrawTextEffectsInfo(in TextLayerInfo textInfo)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Text Effects", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawTextOutlineInfo(in textInfo);
            DrawTextShadowInfo(in textInfo);
            DrawTextGlowInfo("Outer Glow", in textInfo.OuterGlow);
            DrawTextGlowInfo("Inner Glow", in textInfo.InnerGlow);
            DrawTextBevelInfo(in textInfo);
            DrawTextGradientInfo(in textInfo);
            EditorGUI.indentLevel--;
        }

        private void DrawTextOutlineInfo(in TextLayerInfo textInfo)
        {
            if (!textInfo.HasOutline)
            {
                DrawReadOnlyField("Outline", "None");
                return;
            }

            DrawReadOnlyField("Outline", "Enabled");
            DrawColorFieldWithCopy("Outline Color", textInfo.OutlineColor);
            DrawReadOnlyField("Outline Size (UGUI)", FormatFloat(textInfo.OutlineSize));
            DrawReadOnlyField("Outline Size (TMP)", FormatFloat(textInfo.TMPOutlineSize));
            DrawReadOnlyField("Outline Position", textInfo.TMPOutlinePosition.ToString());
        }

        private void DrawTextShadowInfo(in TextLayerInfo textInfo)
        {
            if (!textInfo.HasShadow)
            {
                DrawReadOnlyField("Shadow", "None");
                return;
            }

            DrawReadOnlyField("Shadow", textInfo.ShadowIsInner ? "Inner" : "Outer");
            DrawColorFieldWithCopy("Shadow Color", textInfo.ShadowColor);
            DrawReadOnlyField("Shadow Offset", FormatVector2(textInfo.ShadowOffset));
            DrawReadOnlyField("Shadow Spread", FormatFloat(textInfo.ShadowSpread));
            DrawReadOnlyField("Shadow Softness", FormatFloat(textInfo.ShadowSoftness));
        }

        private void DrawTextGlowInfo(string label, in TextGlowEffectInfo glow)
        {
            if (!glow.Enabled)
            {
                DrawReadOnlyField(label, "None");
                return;
            }

            DrawReadOnlyField(label, "Enabled");
            DrawColorFieldWithCopy(label + " Color", glow.Color);
            DrawReadOnlyField(label + " Size", FormatFloat(glow.Size));
            DrawReadOnlyField(label + " Spread", FormatFloat(glow.Spread));
            DrawReadOnlyField(label + " Technique", glow.TechniqueKey ?? string.Empty);
            DrawReadOnlyField(label + " Source", glow.SourceKey ?? string.Empty);
            DrawReadOnlyField(label + " Range", FormatFloat(glow.Range));
            DrawReadOnlyField(label + " Noise", FormatFloat(glow.Noise));
            DrawReadOnlyField(label + " Jitter", FormatFloat(glow.Jitter));
            DrawReadOnlyField(label + " Anti Alias", glow.AntiAlias ? "True" : "False");
        }

        private void DrawTextBevelInfo(in TextLayerInfo textInfo)
        {
            if (!textInfo.HasBevel)
            {
                DrawReadOnlyField("Bevel", "None");
                return;
            }

            DrawReadOnlyField("Bevel", textInfo.BevelIsInner ? "Inner" : "Outer");
            DrawReadOnlyField("Bevel Size", FormatFloat(textInfo.BevelSize));
            DrawReadOnlyField("Bevel Depth", FormatFloat(textInfo.BevelDepth));
            DrawReadOnlyField("Bevel Soften", FormatFloat(textInfo.BevelSoften));
            DrawReadOnlyField("Bevel Angle", FormatFloat(textInfo.BevelAngle));
            DrawReadOnlyField("Bevel Altitude", FormatFloat(textInfo.BevelAltitude));
            DrawColorFieldWithCopy("Bevel Highlight Color", textInfo.BevelHighlightColor);
            DrawReadOnlyField("Bevel Highlight Opacity", FormatFloat(textInfo.BevelHighlightOpacity));
            DrawColorFieldWithCopy("Bevel Shadow Color", textInfo.BevelShadowColor);
            DrawReadOnlyField("Bevel Shadow Opacity", FormatFloat(textInfo.BevelShadowOpacity));
        }

        private void DrawTextGradientInfo(in TextLayerInfo textInfo)
        {
            if (textInfo.HasGradient && textInfo.GradientStops != null && textInfo.GradientStops.Length > 0)
            {
                DrawReadOnlyField("Gradient", "Enabled");
                DrawReadOnlyField("Gradient Angle", FormatFloat(textInfo.GradientAngle));
                DrawReadOnlyField("Gradient Reverse", textInfo.GradientReverse ? "True" : "False");
                DrawReadOnlyField("Gradient Stops", textInfo.GradientStops.Length.ToString());
                for (int i = 0; i < textInfo.GradientStops.Length; i++)
                {
                    var stop = textInfo.GradientStops[i];
                    DrawReadOnlyField($"Stop {i} Pos", FormatFloat(stop.Location));
                    DrawColorFieldWithCopy($"Stop {i} Color", stop.Color);
                }
            }
            else
            {
                DrawReadOnlyField("Gradient", "None");
            }
        }


        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override bool HasPreviewGUI()
        {
            var layerNode = (target as PsdLayerNode);
            return layerNode != null && layerNode.PreviewTexture != null;
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            var layerNode = (target as PsdLayerNode);
            GUI.DrawTexture(r, layerNode.PreviewTexture, ScaleMode.ScaleToFit);
            //base.OnPreviewGUI(r, background);
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        public override string GetInfoString()
        {
            var layerNode = (target as PsdLayerNode);
            return layerNode.LayerInfo;
        }
    }
    [CanEditMultipleObjects]
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed partial class PsdLayerNode : MonoBehaviour
    {
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

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] internal int BindPsdLayerIndex = -1;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] PsdLayerType mLayerType = PsdLayerType.Unknown;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] string sourceLayerName;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] internal bool markToExport;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] string generatedNodeId;
        [HideInInspector][SerializeField] private string persistentId;
        [HideInInspector][SerializeField] private bool missingSource;
        [HideInInspector][SerializeField] private bool unassignedSource;
        [HideInInspector][SerializeField] private string sourceGeneratedName;
        [HideInInspector][SerializeField] private int sourceParentLayerId;
        [HideInInspector][SerializeField] private bool sourceParentKnown, sourceVisible;
        internal string PersistentId
        {
            get
            {
                if (string.IsNullOrEmpty(persistentId)) { persistentId = Guid.NewGuid().ToString("N"); EditorUtility.SetDirty(this); }
                return persistentId;
            }
        }
        internal bool HasMissingSource => missingSource;
        internal bool IsUnassignedSource => unassignedSource;
        internal int[] AiSourceLayerIds => aiSourceLayerIds;

        internal void SynchronizeSource(PsdLayerNode source)
        {
            BindPsdLayerIndex = source.BindPsdLayerIndex;
            bindPsdLayerId = source.bindPsdLayerId;
            mLayerType = source.mLayerType;
            if (name == sourceGeneratedName) name = source.name;
            sourceGeneratedName = source.name;
            if (gameObject.activeSelf == sourceVisible) gameObject.SetActive(source.gameObject.activeSelf);
            sourceVisible = source.gameObject.activeSelf;
            sourceLayerName = source.sourceLayerName;
            BindPsdLayer = source.BindPsdLayer;
            missingSource = false;
            UpdateLayerRectData(source.LayerRect);
        }

        internal void MarkUnassignedSource() { unassignedSource = true; UIType = GUIType.Null; markToExport = false; }
        internal void MarkMissingSource() { missingSource = true; BindPsdLayerIndex = -1; BindPsdLayer = null; markToExport = false; RemoveUIHelper(); }
        internal void CaptureSourcePresentation() { sourceGeneratedName = name; sourceVisible = gameObject.activeSelf; }
        internal void SynchronizeSourceParent(Transform parent, int parentLayerId)
        {
            var currentParent = transform.parent != null ? transform.parent.GetComponent<PsdLayerNode>() : null;
            int currentId = currentParent != null ? currentParent.bindPsdLayerId : 0;
            if (!sourceParentKnown || currentId == sourceParentLayerId)
                Undo.SetTransformParent(transform, parent, "Synchronize PSD hierarchy");
            sourceParentLayerId = parentLayerId;
            sourceParentKnown = true;
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] internal GUIType UIType;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] internal bool collapseChildrenForGeneration;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] internal int textSourceBindPsdLayerIndex = -1;
        [HideInInspector][SerializeField] internal int bindPsdLayerId = -1;
        [HideInInspector][SerializeField] internal int textSourceBindPsdLayerId = -1;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] private bool rasterizeGroupForOpacity;
        internal Texture2D PreviewTexture { get; private set; }
        internal string LayerInfo => LayerRect.ToString();
        private Rect mLayerRect;
        internal Rect LayerRect
        {
            get
            {
                if (IsAiTarget && (aiExplicitRect || HasAiSources)) return aiBounds;
                if (ShouldUseDerivedLayerRect() && TryCalculateDerivedLayerRect(out var derivedRect))
                {
                    return derivedRect;
                }

                return mLayerRect;
            }
            private set => mLayerRect = value;
        }
        internal PsdLayerType LayerType { get => mLayerType; }
        internal string SourceLayerName => sourceLayerName;
        internal string GeneratedNodeId => generatedNodeId;
        internal void SetAiDeliveryId(string id) { generatedNodeId = id; }
        internal bool IsMainUIType => rasterizeGroupForOpacity || UGUIParser.IsMainUIType(UIType);
        internal bool HasReuseReference => !string.IsNullOrEmpty(ReuseTargetKey);
        internal bool HasReusePrefabReference => !string.IsNullOrEmpty(ReusePrefabKey);
        internal string ReuseTargetKey => TryGetReuseTargetName(out var targetName) ? NormalizeReferencePath(targetName) : null;
        internal string ReuseTargetDisplayName => TryGetReuseTargetName(out var targetName) ? targetName : null;
        internal string ReusePrefabKey => TryGetReusePrefabName(out var targetName) ? NormalizeReferencePath(targetName) : null;
        internal string ReusePrefabDisplayName => TryGetReusePrefabName(out var targetName) ? targetName : null;
        internal string LayerNameLookupKey => ResourceName;
        internal string ResourceName => IsAiTarget && !string.IsNullOrEmpty(aiResourceName)
            ? aiResourceName : PsdResourceNaming.LayerName(IsAiTarget ? name : GetReferenceSourceName());
        internal bool CollapseChildrenForGeneration => collapseChildrenForGeneration;
        internal bool RasterizeGroupForOpacity => rasterizeGroupForOpacity;
        [HideInInspector][SerializeField] private bool aiAuthoritative;
        [HideInInspector][SerializeField] private bool aiExplicitRect;
        [HideInInspector][SerializeField] private int[] aiSourceIndices = Array.Empty<int>();
        [HideInInspector][SerializeField] private int[] aiSourceLayerIds = Array.Empty<int>();
        [HideInInspector][SerializeField] private string aiResourceName;
        [HideInInspector][SerializeField] private string aiReferenceKind;
        [HideInInspector][SerializeField] private string aiReferenceKey;
        [HideInInspector][SerializeField] private Rect aiBounds;
        [HideInInspector][SerializeField] private bool aiOwnerReady = true;
        [HideInInspector][SerializeField] private float aiTextAncestorOpacity = 1f;
        internal float AiTextAncestorOpacity => aiTextAncestorOpacity;
        [HideInInspector][SerializeField] private bool aiSourceVisible = true;
        internal bool AiSourceVisible => aiSourceVisible;
        private PsdLayerRenderNode aiRenderTree;
        internal bool IsAiTarget => aiAuthoritative && BindPsdLayerIndex < 0;
        internal bool HasAiSources
        {
            get { foreach (int index in aiSourceIndices) if (index >= 0) return true; return false; }
        }
        internal int[] AiSourceIndices => aiSourceIndices;
        internal bool HasAiSource(int index) => Array.IndexOf(aiSourceIndices, index) >= 0;
        internal int RenderOrderSourceIndex
        {
            get {
                if (!IsAiTarget) return BindPsdLayerIndex >= 0 ? BindPsdLayerIndex : int.MaxValue;
                int first = int.MaxValue;
                foreach (int index in aiSourceIndices) if (index >= 0 && index < first) first = index;
                return first;
            }
        }
        internal bool IsAiReady => !IsAiTarget || aiOwnerReady && (HasReusePrefabReference ? HasAiSources : AiUiContract.IsText(UIType) ? aiTextReady
            && LayerRect.width > 0 && LayerRect.height > 0 : HasAiSources
            || (AiUiContract.IsContainer(UIType) || UIType == GUIType.ScrollView_Viewport && aiExplicitRect)
            && LayerRect.width > 0 && LayerRect.height > 0);
        internal bool IsAiAuthoritative => aiAuthoritative;
        internal bool BakesGroupOpacityIntoGeneratedImage => LayerType == PsdLayerType.LayerGroup
            && collapseChildrenForGeneration
            && textSourceBindPsdLayerIndex < 0;
        internal float SourceOpacity => BindPsdLayer != null ? BindPsdLayer.Opacity : 1f;
        internal PsdLayer GroupTextSourcePsdLayer => mTextSourcePsdLayer;

        internal bool TryGetSourceLayerId(out int layerId)
        {
            layerId = 0;
            return BindPsdLayer != null && BindPsdLayer.TryGetLayerId(out layerId);
        }
        private WeakReference<PsdLayerNode> reuseTargetRef = null;
        private string previewCacheKey;
        /// <summary>
        /// Cached bound PSD layer.
        /// </summary>
        private PsdLayer mBindPsdLayer;
        private PsdLayer mTextSourcePsdLayer;

        internal PsdLayer BindPsdLayer
        {
            get => mBindPsdLayer;
            set
            {
                mBindPsdLayer = value;
                if (mBindPsdLayer == null) return;
                mLayerType = mBindPsdLayer.GetLayerType();
                //if (IsTextLayer(out var txtLayer) && !txtLayer.TextBoundBox.IsEmpty)
                //{
                //    var txtRect = txtLayer.TextBoundBox;
                //    //txtRect.Width *= (float)txtLayer.TransformMatrix[3];
                //    //txtRect.Height *= (float)txtLayer.TransformMatrix[3];
                //    LayerRect = PsdParserExtensions.PsdRect2UnityRect(txtRect.Left, txtRect.Top, txtRect.Right, txtRect.Bottom, new Vector2Int(BindPsdLayer.Container.Width, BindPsdLayer.Container.Height));
                //}
                //else
                //{
                //    LayerRect = mBindPsdLayer.GetLayerRect();
                //}
                LayerRect = mBindPsdLayer.GetLayerRect();
            }
        }
        internal static string NormalizeLayerName(string rawName, bool preserveRefTags = false)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;
            var sanitized = preserveRefTags
                ? LayerNameUtility.SanitizePreserveRefTags(rawName.Trim())
                : LayerNameUtility.Sanitize(rawName.Trim());
            return sanitized.ToLowerInvariant();
        }

        internal static string GetGeneratedObjectName(string rawName, int fallbackIndex = -1)
        {
            var workingName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
            var parser = UGUIParser.Instance;
            if (parser != null && parser.ConvertZh2En && !string.IsNullOrEmpty(workingName))
            {
                workingName = LayerNameUtility.ConvertChineseToLetters(workingName);
            }

            if (parser != null)
            {
                workingName = parser.RemoveLayerTypeFlags(workingName);
            }

            workingName = LayerNameUtility.Sanitize(workingName);
            if (string.IsNullOrEmpty(workingName))
            {
                return fallbackIndex >= 0 ? $"PsdLayer-{fallbackIndex}" : "PsdLayer";
            }

            return workingName;
        }

        internal string GetGeneratedObjectName()
        {
            return GetGeneratedObjectName(GetReferenceSourceName(), BindPsdLayerIndex);
        }

        internal static string NormalizePrefabName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;
            return LayerNameUtility.Sanitize(rawName.Trim());
        }
        internal static string NormalizeReferencePath(string rawPath)
        {
            return PsdResourceNaming.ReferenceKey(rawPath);
        }

        internal static string GetReferenceLeafName(string referencePath, string fallbackName = null)
        {
            var leafName = LayerNameUtility.GetRelativePathLeafName(referencePath);
            if (!string.IsNullOrWhiteSpace(leafName))
            {
                return leafName;
            }

            return string.IsNullOrWhiteSpace(fallbackName) ? "PsdLayer" : fallbackName;
        }

        internal void SetSourceLayerName(string layerName)
        {
            sourceLayerName = layerName;
        }

        internal void ConfigureGeneratedGroupNode(string layerName, Rect layerRect, string nodeId = null)
        {
            BindPsdLayerIndex = -1;
            bindPsdLayerId = -1;
            markToExport = false;
            textSourceBindPsdLayerIndex = -1;
            textSourceBindPsdLayerId = -1;
            SetGroupGenerationState(false, -1);

            mLayerType = PsdLayerType.LayerGroup;
            sourceLayerName = layerName ?? string.Empty;
            generatedNodeId = nodeId ?? string.Empty;
            UpdateLayerRectData(layerRect);
        }

        internal void ConfigureAiDeclaration(GUIType type, Rect? explicitRect, bool ownerReady)
        {
            aiAuthoritative = true;
            aiOwnerReady = ownerReady;
            aiExplicitRect = explicitRect.HasValue;
            if (explicitRect.HasValue) aiBounds = explicitRect.Value;
            rasterizeGroupForOpacity = false;
            SetUIType(type, false);
            UpdateAiBinding(null);
        }

        internal void AddAiSource(int sourceIndex, PsdDocument document)
        {
            if (!HasAiSource(sourceIndex))
            {
                AlignAiSourceLayerIds(document);
                Array.Resize(ref aiSourceIndices, aiSourceIndices.Length + 1);
                aiSourceIndices[aiSourceIndices.Length - 1] = sourceIndex;
                Array.Resize(ref aiSourceLayerIds, aiSourceLayerIds.Length + 1);
                aiSourceLayerIds[aiSourceLayerIds.Length - 1] = ResolveLayerId(document, sourceIndex);
            }
            UpdateAiBinding(document);
        }

        internal void RemoveAiSource(int index, PsdDocument document)
        {
            int position = Array.IndexOf(aiSourceIndices, index);
            if (position < 0) return;
            AlignAiSourceLayerIds(document);
            var remaining = new int[aiSourceIndices.Length - 1];
            var remainingIds = new int[aiSourceLayerIds.Length - 1];
            Array.Copy(aiSourceIndices, 0, remaining, 0, position);
            Array.Copy(aiSourceIndices, position + 1, remaining, position, remaining.Length - position);
            Array.Copy(aiSourceLayerIds, 0, remainingIds, 0, position);
            Array.Copy(aiSourceLayerIds, position + 1, remainingIds, position, remainingIds.Length - position);
            aiSourceIndices = remaining;
            aiSourceLayerIds = remainingIds;
            UpdateAiBinding(document);
        }

        private static int ResolveLayerId(PsdDocument document, int sourceIndex)
        {
            var layer = document != null ? document.GetLayerByFlatIndex(sourceIndex) : null;
            return layer != null && layer.TryGetLayerId(out int layerId) && layerId > 0 ? layerId : -1;
        }

        private void AlignAiSourceLayerIds(PsdDocument document)
        {
            if (aiSourceLayerIds.Length == aiSourceIndices.Length) return;
            var ids = new int[aiSourceIndices.Length];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = i < aiSourceLayerIds.Length ? aiSourceLayerIds[i] : ResolveLayerId(document, aiSourceIndices[i]);
            aiSourceLayerIds = ids;
        }

        // Sources outlive their position: after the PSD gains, loses or reorders layers the stored
        // flat indexes point at whoever happens to sit there now, so follow lyid instead.
        private void RealignSourceLayerIndexes(PsdDocument document)
        {
            var indexById = document.GetLayerIndexByIdMap();
            if (indexById == null) return;
            if (bindPsdLayerId > 0)
                BindPsdLayerIndex = indexById.TryGetValue(bindPsdLayerId, out int bound) ? bound : -1;
            if (textSourceBindPsdLayerId > 0)
                textSourceBindPsdLayerIndex = indexById.TryGetValue(textSourceBindPsdLayerId, out int textIndex) ? textIndex : -1;
            if (aiTextStyleLayerId > 0)
                aiTextStyleIndex = indexById.TryGetValue(aiTextStyleLayerId, out int styleIndex) ? styleIndex : -1;
            if (aiSourceLayerIds.Length != aiSourceIndices.Length) return;
            for (int i = 0; i < aiSourceIndices.Length; i++)
            {
                if (aiSourceLayerIds[i] > 0)
                    aiSourceIndices[i] = indexById.TryGetValue(aiSourceLayerIds[i], out int sourceIndex) ? sourceIndex : -1;
            }
        }

        private void UpdateAiBinding(PsdDocument document)
        {
            ReleasePreviewTexture();
            if (aiSourceIndices.Length != 1) SetAiResourceMetadata(null, null, null);
            else if (document != null)
            {
                string sourceName = document.GetLayerByFlatIndex(aiSourceIndices[0])?.Name;
                PsdResourceNaming.Parse(sourceName, out var kind, out var key);
                SetAiResourceMetadata(sourceName, kind, key);
            }
            bool text = AiUiContract.IsText(UIType);
            bool nativeText = text && aiText?.mode == "source";
            textSourceBindPsdLayerIndex = nativeText && HasAiSources ? aiSourceIndices[0]
                : text && aiText?.mode == "value" ? aiTextStyleIndex : -1;
            // 只有两份数组等长时才可信；旧资产的 id 数组可能缺失，错配会把文本源指到别的图层。
            textSourceBindPsdLayerId = nativeText && HasAiSources && aiSourceLayerIds.Length == aiSourceIndices.Length
                ? aiSourceLayerIds[0] : text && aiText?.mode == "value" ? aiTextStyleLayerId : -1;
            mTextSourcePsdLayer = textSourceBindPsdLayerIndex >= 0 ? document?.GetLayerByFlatIndex(textSourceBindPsdLayerIndex) : null;
            if (document != null)
            {
                aiTextAncestorOpacity = nativeText ? mTextSourcePsdLayer?.AncestorOpacity ?? 1f : 1f;
            }
            bool styleAvailable = mTextSourcePsdLayer != null && mTextSourcePsdLayer.TryGetTextLayerInfo(out var info)
                && info.StyleRuns != null && info.StyleRuns.Length > 0;
            aiTextReady = text && (nativeText ? styleAvailable : aiText?.mode == "value"
                && (styleAvailable || aiText.style?.IsComplete == true));
            // AI children are independent target elements, not the PSD layers being rendered.
            collapseChildrenForGeneration = false;
            markToExport = aiOwnerReady && HasAiSources && !text && UIType != GUIType.Null
                && !HasReuseReference && !HasReusePrefabReference;
            aiRenderTree = document == null ? null : AiSourceRenderTree.Build(document, aiSourceIndices);
            if (document != null)
            {
                aiSourceVisible = !HasAiSources;
                foreach (int index in aiSourceIndices)
                    if (document.GetLayerByFlatIndex(index)?.IsVisibleInHierarchy == true) { aiSourceVisible = true; break; }
            }
            if (!aiExplicitRect && document != null && HasAiSources)
            {
                float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                foreach (int index in aiSourceIndices)
                {
                    var layer = document.GetLayerByFlatIndex(index);
                    if (layer == null) continue;
                    left = Mathf.Min(left, layer.Left); top = Mathf.Min(top, layer.Top);
                    right = Mathf.Max(right, layer.Right); bottom = Mathf.Max(bottom, layer.Bottom);
                }
                if (!float.IsInfinity(left)) aiBounds = new Rect((left + right - document.Width) * .5f, (document.Height - top - bottom) * .5f, right - left, bottom - top);
            }
        }

        internal void SetAiResourceMetadata(string sourceName, string kind, string key)
        {
            aiResourceName = string.IsNullOrEmpty(sourceName) ? null : PsdResourceNaming.LayerName(sourceName);
            aiReferenceKind = kind;
            aiReferenceKey = key;
            if (!string.IsNullOrEmpty(key)) markToExport = false;
        }

        internal string[] GetAiRenderIssues(PsdDocument document)
            => document == null || !HasAiSources ? Array.Empty<string>() : AiSourceRenderTree.FindCompositionIssues(document, aiSourceIndices);

        internal void SetGeneratedNodeId(string nodeId)
        {
            generatedNodeId = nodeId ?? string.Empty;
        }

        internal void UpdateLayerRectData(Rect layerRect)
        {
            LayerRect = layerRect;
        }

        internal static void GetLayerRectVisualBounds(Rect rect, out float xMin, out float yMin, out float xMax, out float yMax)
        {
            float halfWidth = rect.width * 0.5f;
            float halfHeight = rect.height * 0.5f;
            xMin = rect.position.x - halfWidth;
            yMin = rect.position.y - halfHeight;
            xMax = rect.position.x + halfWidth;
            yMax = rect.position.y + halfHeight;
        }

        internal static Rect CreateLayerRectFromVisualBounds(float xMin, float yMin, float xMax, float yMax)
        {
            return new Rect(
                (xMin + xMax) * 0.5f,
                (yMin + yMax) * 0.5f,
                xMax - xMin,
                yMax - yMin);
        }

        private bool ShouldUseDerivedLayerRect()
        {
            return !aiExplicitRect && BindPsdLayer == null
                && BindPsdLayerIndex < 0
                && LayerType == PsdLayerType.LayerGroup;
        }

        private bool TryCalculateDerivedLayerRect(out Rect bounds)
        {
            bounds = Rect.zero;

            bool hasAny = false;
            float xMin = 0f;
            float yMin = 0f;
            float xMax = 0f;
            float yMax = 0f;

            for (int i = 0, childCount = transform.childCount; i < childCount; i++)
            {
                var childNode = transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                var rect = childNode.LayerRect;
                // Empty geometry (including unbound AI declarations) contributes no visual bounds.
                if (rect.width <= 0f || rect.height <= 0f) continue;

                GetLayerRectVisualBounds(rect, out float minX, out float minY, out float maxX, out float maxY);
                if (!hasAny)
                {
                    xMin = minX;
                    yMin = minY;
                    xMax = maxX;
                    yMax = maxY;
                    hasAny = true;
                    continue;
                }

                xMin = Mathf.Min(xMin, minX);
                yMin = Mathf.Min(yMin, minY);
                xMax = Mathf.Max(xMax, maxX);
                yMax = Mathf.Max(yMax, maxY);
            }

            if (!hasAny)
            {
                return false;
            }

            bounds = CreateLayerRectFromVisualBounds(xMin, yMin, xMax, yMax);
            return true;
        }

        private string GetReferenceSourceName()
        {
            var currentName = gameObject?.name;
            var parser = UGUIParser.Instance;
            if (!string.IsNullOrWhiteSpace(currentName) && parser != null)
            {
                if (!string.IsNullOrWhiteSpace(sourceLayerName))
                {
                    var expectedName = parser.GetSafeLayerName(sourceLayerName, BindPsdLayerIndex);
                    if (!string.Equals(currentName, expectedName, StringComparison.Ordinal))
                    {
                        return currentName;
                    }
                }
                else
                {
                    var currentBindDisplayName = BindPsdLayer?.GetDisplayName();
                    if (!string.IsNullOrWhiteSpace(currentBindDisplayName))
                    {
                        var expectedName = parser.GetSafeLayerName(currentBindDisplayName, BindPsdLayerIndex);
                        if (!string.Equals(currentName, expectedName, StringComparison.Ordinal))
                        {
                            return currentName;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(sourceLayerName))
            {
                return sourceLayerName;
            }

            var bindDisplayName = BindPsdLayer?.GetDisplayName();
            if (!string.IsNullOrWhiteSpace(bindDisplayName))
            {
                return bindDisplayName;
            }

            return currentName;
        }

        private bool TryGetReuseTargetName(out string targetName)
        {
            targetName = null;
            if (aiAuthoritative) { targetName = aiReferenceKind == "ref" ? aiReferenceKey : null; return !string.IsNullOrEmpty(targetName); }
            var workingName = GetReferenceSourceName();
            if (string.IsNullOrWhiteSpace(workingName)) return false;

            workingName = workingName.Trim();
            if (!workingName.StartsWith(UGUIParser.REF_TAG, StringComparison.OrdinalIgnoreCase)) return false;

            targetName = workingName.Substring(UGUIParser.REF_TAG.Length).Trim();
            var parser = UGUIParser.Instance;
            if (parser != null)
            {
                targetName = parser.RemoveLayerTypeFlags(targetName);
            }
            return !string.IsNullOrEmpty(targetName);
        }
        private bool TryGetReusePrefabName(out string targetName)
        {
            targetName = null;
            if (aiAuthoritative) { targetName = aiReferenceKind == "refp" ? aiReferenceKey : null; return !string.IsNullOrEmpty(targetName); }
            var workingName = GetReferenceSourceName();
            if (string.IsNullOrWhiteSpace(workingName)) return false;

            workingName = workingName.Trim();
            if (!workingName.StartsWith(UGUIParser.REF_PREFAB_TAG, StringComparison.OrdinalIgnoreCase)) return false;

            targetName = workingName.Substring(UGUIParser.REF_PREFAB_TAG.Length).Trim();
            var parser = UGUIParser.Instance;
            if (parser != null)
            {
                targetName = parser.RemoveLayerTypeFlags(targetName);
            }
            return !string.IsNullOrEmpty(targetName);
        }
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private void OnDestroy()
        {
            ReleasePreviewTexture();
        }
        internal void SetUIType(GUIType uiType, bool triggerParseFunc = true)
        {
            unassignedSource = false;
            this.UIType = ResolvePreferredType(uiType);
            RemoveUIHelper();

            if (triggerParseFunc)
            {
                var converter = Psd2UIFormConverter.Instance;
                if (converter != null)
                {
                    converter.RefreshAllLayerNodeHelpers();
                }
                else
                {
                    RefreshUIHelper(true);
                }
            }
        }
        internal GUIType ResolvePreferredType(GUIType type)
        {
            var parser = UGUIParser.Instance;
            if (parser == null) return type;
            return parser.ResolvePreferredUIType(type, aiAuthoritative ? null : GetReferenceSourceName(), LayerType);
        }
        internal void SetGroupGenerationState(bool collapseChildren, int textSourceIndex, int textSourceLayerId = -1)
        {
            collapseChildrenForGeneration = collapseChildren;
            textSourceBindPsdLayerIndex = textSourceIndex;
            textSourceBindPsdLayerId = textSourceIndex >= 0 ? textSourceLayerId : -1;
            mTextSourcePsdLayer = null;
            if (textSourceBindPsdLayerIndex >= 0)
            {
                mTextSourcePsdLayer = BindPsdLayer?.Document?.GetLayerByFlatIndex(textSourceBindPsdLayerIndex);
            }
        }
        internal void SetGroupOpacityRasterization(bool value)
        {
            rasterizeGroupForOpacity = value;
            if (!value)
            {
                return;
            }

            collapseChildrenForGeneration = true;
            textSourceBindPsdLayerIndex = -1;
            textSourceBindPsdLayerId = -1;
            mTextSourcePsdLayer = null;
        }
        internal void RefreshUIHelper(bool refreshParent = false)
        {
            if (missingSource || unassignedSource) { RemoveUIHelper(); return; }
            if (IsAiAuthoritative && HasReusePrefabReference) { RemoveUIHelper(); return; }
            if (IsAiTarget && !IsAiReady) { RemoveUIHelper(); return; }
            var parser = UGUIParser.Instance;
            UIType = ResolvePreferredType(UIType);
            if (UIType == GUIType.Null && !rasterizeGroupForOpacity)
            {
                RemoveUIHelper();
                return;
            }

            var uiHelperTp = rasterizeGroupForOpacity
                ? typeof(PanelHelper)
                : parser == null ? null : parser.GetHelperType(UIType);
            RemoveIncompatibleUIHelpers(uiHelperTp);
            if (uiHelperTp != null)
            {
                var helper = (gameObject.GetComponent(uiHelperTp) ?? Undo.AddComponent(gameObject, uiHelperTp)) as UIHelperBase;
                if (helper != null)
                {
                    helper.ParseAndAttachUIElements();
                    // Conventional parsing can replace its own Helper when a PSD text source is absent.
                    if (helper != null) EditorUtility.SetDirty(helper);
                }
            }
            if (refreshParent)
            {
                var currentNode = transform.parent;
                while (currentNode != null)
                {
                    var parentHelper = currentNode.GetComponent<UIHelperBase>();
                    if (parentHelper != null)
                    {
                        parentHelper.ParseAndAttachUIElements();
                        EditorUtility.SetDirty(parentHelper);
                        break;
                    }
                    currentNode = currentNode.parent;
                }

            }
            EditorUtility.SetDirty(this);
        }
        private void RemoveIncompatibleUIHelpers(Type keepType)
        {
            var uiHelpers = this.GetComponents<UIHelperBase>();
            if (uiHelpers == null) return;

            bool keptWanted = false;
            foreach (var uiHelper in uiHelpers)
            {
                if (uiHelper == null) continue;
                if (keepType != null && uiHelper.GetType() == keepType && !keptWanted)
                {
                    keptWanted = true;
                    continue;
                }
                // 全部走 Undo：不入栈的话撤销后类型回退了，Helper 组件却已经被销毁或留着重复份。
                Undo.DestroyObjectImmediate(uiHelper);
            }
        }
        private void RemoveUIHelper()
        {
            var uiHelpers = this.GetComponents<UIHelperBase>();
            if (uiHelpers != null)
            {
                foreach (var uiHelper in uiHelpers)
                {
                    if (aiAuthoritative) Undo.DestroyObjectImmediate(uiHelper);
                    else DestroyImmediate(uiHelper);
                }
            }
            EditorUtility.SetDirty(this);
        }
        /// <summary>
        /// Determine whether this layer still needs an exported image asset.
        /// </summary>
        /// <returns></returns>
        internal bool NeedExportImage()
        {
            if (UIType == GUIType.FillColor || LayerType == PsdLayerType.FillLayer)
            {
                return false;
            }

            return gameObject.activeSelf && markToExport;
        }

        private static string GetSanitizedExportName(string rawName, bool convertFileNameToLower, string fallbackName)
        {
            var sanitized = PsdResourceNaming.FileName(rawName);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = LayerNameUtility.Sanitize(fallbackName);
            }
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "PsdLayer";
            }
            if (convertFileNameToLower)
            {
                sanitized = sanitized.ToLowerInvariant();
            }
            return sanitized;
        }

        private static string GetLayerExportBaseName(PsdLayerNode node, bool convertFileNameToLower)
        {
            if (node == null) return "PsdLayer";
            var rawName = node.ResourceName;
            return GetSanitizedExportName(rawName, convertFileNameToLower, node.UIType.ToString());
        }

        private static string ResolveOverrideExportPath(string baseDir, string overrideFileName, bool convertFileNameToLower, string fallbackName, out string targetDir)
        {
            targetDir = baseDir;
            if (string.IsNullOrWhiteSpace(overrideFileName))
            {
                return GetSanitizedExportName(fallbackName, convertFileNameToLower, fallbackName);
            }

            var relativePath = PsdResourceNaming.ReferenceKey(overrideFileName);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return GetSanitizedExportName(fallbackName, convertFileNameToLower, fallbackName);
            }

            if (convertFileNameToLower)
            {
                relativePath = relativePath.ToLowerInvariant();
            }

            var relativeDirectory = Path.GetDirectoryName(relativePath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(relativeDirectory))
            {
                targetDir = Path.Combine(baseDir, relativeDirectory).Replace("\\", "/");
            }

            var leafName = LayerNameUtility.GetRelativePathLeafName(relativePath);
            return GetSanitizedExportName(leafName, convertFileNameToLower, fallbackName);
        }
        /// <summary>
        /// Export the layer image asset and return its asset path.
        /// </summary>
        /// <param name="forceSpriteType">Force sprite import settings for the exported texture.</param>
        /// <returns></returns>
        internal string ExportImageAsset(bool forceSpriteType = false, string overrideExportDir = null, string overrideFileName = null, bool convertFileNameToLower = false, bool auto9Slice = false, bool ignoreReference = false)
        {
            var converter = Psd2UIFormConverter.Instance;
            if (!ignoreReference && converter != null && converter.TryGetSharedSpritePath(this, out var sharedPath))
            {
                converter.RegisterExportedImagePath(sharedPath);
                return sharedPath;
            }
            bool useSharedOverride = false;
            string sharedOverrideKey = null;
            string exportDir = overrideExportDir;
            string fileName = overrideFileName;
            if (!ignoreReference && converter != null && string.IsNullOrEmpty(exportDir) && !HasReuseReference)
            {
                if (converter.TryOverrideSharedOutput(this, out var sharedDir, out var sharedFile))
                {
                    exportDir = sharedDir;
                    fileName = sharedFile;
                    useSharedOverride = true;
                    sharedOverrideKey = sharedFile;
                    forceSpriteType = true;
                }
            }
            if (!ignoreReference && HasReuseReference && converter != null)
            {
                return converter.GetOrCreateSharedSprite(this, auto9Slice);
            }
            string assetName = null;
            if (UIType == GUIType.FillColor || LayerType == PsdLayerType.FillLayer)
            {
                return null;
            }
            var rendered = RenderNodeImage(previewRender: false);
            if (rendered != null && !rendered.IsEmpty)
            {
                bool isImage = !(this.UIType == GUIType.FillColor || this.UIType == GUIType.RawImage);
                exportDir = string.IsNullOrWhiteSpace(exportDir) ? Psd2UIFormConverter.Instance.GetUIFormImagesOutputDir() : exportDir;
                if (string.IsNullOrWhiteSpace(exportDir))
                {
                    return null;
                }
                if (!Directory.Exists(exportDir))
                {
                    try
                    {
                        Directory.CreateDirectory(exportDir);
                        AssetDatabase.Refresh();
                    }
                    catch (System.Exception)
                    {
                        return null;
                    }
                }
                var defaultImgName = string.IsNullOrWhiteSpace(fileName)
                    ? converter.ResolveImageExportName(this)
                    : GetLayerExportBaseName(this, convertFileNameToLower);
                var imgName = string.IsNullOrWhiteSpace(fileName)
                    ? defaultImgName
                    : ResolveOverrideExportPath(exportDir, fileName, convertFileNameToLower, defaultImgName, out exportDir);
                if (!Directory.Exists(exportDir))
                {
                    try
                    {
                        Directory.CreateDirectory(exportDir);
                        AssetDatabase.Refresh();
                    }
                    catch (System.Exception)
                    {
                        return null;
                    }
                }
                string assetExtension = ".png";
                var imgFileName = Path.Combine(exportDir, imgName.EndsWith(assetExtension, StringComparison.OrdinalIgnoreCase) ? imgName : imgName + assetExtension).Replace("\\", "/");
                var bytes = PsdTextureAssetUtility.EncodePng(rendered);
                if (bytes == null || bytes.Length <= 0)
                {
                    return null;
                }
                if (ignoreReference || useSharedOverride)
                {
                    File.WriteAllBytes(imgFileName, bytes);
                    if (Psd2UIFormSettings.Instance.CompressImage) Psd2UIFormConverter.CompressImageFile(imgFileName);
                    AssetDatabase.ImportAsset(imgFileName, ImportAssetOptions.ForceSynchronousImport);
                    Psd2UIFormConverter.ConvertTexturesType(new[] { imgFileName }, isImage || forceSpriteType, rendered.IsHighBitDepth);
                    if (auto9Slice) Psd2UIFormConverter.ApplySpriteNineSlice(imgFileName);
                    converter.RegisterExportedImagePath(imgFileName);
                    assetName = imgFileName;
                }
                else assetName = converter.WriteImage(this, imgFileName, bytes, isImage || forceSpriteType, rendered.IsHighBitDepth, auto9Slice);
                if (useSharedOverride && converter != null)
                {
                    converter.RegisterSharedSprite(string.IsNullOrEmpty(sharedOverrideKey) ? LayerNameLookupKey : sharedOverrideKey, imgFileName);
                }
            }

            return assetName;
        }
        internal bool RefreshLayerTexture(bool forceRefresh = false)
        {
            string currentCacheKey = ResolvePreviewCacheKey();
            if (string.IsNullOrEmpty(currentCacheKey))
            {
                ReleasePreviewTexture();
                return false;
            }

            bool hasPreviewTexture = PreviewTexture != null;
            bool cacheKeyMatches = hasPreviewTexture
                && !string.IsNullOrEmpty(previewCacheKey)
                && string.Equals(previewCacheKey, currentCacheKey, StringComparison.Ordinal);

            if (!forceRefresh && cacheKeyMatches)
            {
                return true;
            }

            if (!CanRenderNodeImage())
            {
                ReleasePreviewTexture();
                return false;
            }

            if (hasPreviewTexture && (!cacheKeyMatches || forceRefresh))
            {
                ReleasePreviewTexture();
            }

            PreviewTexture = PsdLayerPreviewCache.Acquire(currentCacheKey, ConvertNodePreviewToTexture2D);
            if (PreviewTexture != null)
            {
                previewCacheKey = currentCacheKey;
            }
            return PreviewTexture != null;
        }

        private string ResolvePreviewCacheKey()
        {
            if (IsAiTarget && HasAiSources)
                return "AiPreview|" + generatedNodeId + "|" + UIType + "|" + string.Join(",", aiSourceIndices)
                    + "|" + Psd2UIFormConverter.Instance?.psdAssetChangeTime;
            if (ShouldRenderPreviewFromCurrentChildTree())
            {
                var childKeys = new List<string>();
                for (int i = 0, childCount = transform.childCount; i < childCount; i++)
                {
                    var childNode = transform.GetChild(i).GetComponent<PsdLayerNode>();
                    if (childNode == null)
                    {
                        continue;
                    }

                    var childKey = childNode.ResolvePreviewCacheKey();
                    if (string.IsNullOrEmpty(childKey))
                    {
                        childKeys.Add($"child:{i}:missing");
                        continue;
                    }

                    childKeys.Add($"child:{i}:{childKey}");
                }

                if (childKeys.Count < 1)
                {
                    return string.Empty;
                }

                return string.Join(
                    "|",
                    "TreePreview",
                    NormalizePreviewCachePart(UIType.ToString()),
                    NormalizePreviewCachePart(SourceLayerName),
                    LayerRect.x.ToString(CultureInfo.InvariantCulture),
                    LayerRect.y.ToString(CultureInfo.InvariantCulture),
                    LayerRect.width.ToString(CultureInfo.InvariantCulture),
                    LayerRect.height.ToString(CultureInfo.InvariantCulture),
                    SourceOpacity.ToString("R", CultureInfo.InvariantCulture),
                    string.Join("|", childKeys));
            }

            if (BindPsdLayer != null)
            {
                try
                {
                    return BuildPreviewCacheKey();
                }
                catch
                {
                    return string.Empty;
                }
            }

            if (!CanRenderGeneratedCompositeImage())
            {
                return string.Empty;
            }

            var generatedChildKeys = new List<string>();
            for (int i = 0, childCount = transform.childCount; i < childCount; i++)
            {
                var childNode = transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                var childKey = childNode.ResolvePreviewCacheKey();
                if (string.IsNullOrEmpty(childKey))
                {
                    generatedChildKeys.Add($"child:{i}:missing");
                    continue;
                }

                generatedChildKeys.Add($"child:{i}:{childKey}");
            }

            if (generatedChildKeys.Count < 1)
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                "GeneratedPreview",
                NormalizePreviewCachePart(UIType.ToString()),
                NormalizePreviewCachePart(SourceLayerName),
                LayerRect.x.ToString(CultureInfo.InvariantCulture),
                LayerRect.y.ToString(CultureInfo.InvariantCulture),
                LayerRect.width.ToString(CultureInfo.InvariantCulture),
                LayerRect.height.ToString(CultureInfo.InvariantCulture),
                string.Join("|", generatedChildKeys));
        }

        private string BuildPreviewCacheKey()
        {
            string assetPath = NormalizePreviewCachePart(Psd2UIFormConverter.Instance?.PsdAssetName);
            string assetChangeTag = NormalizePreviewCachePart(Psd2UIFormConverter.Instance?.psdAssetChangeTime);
            if (string.IsNullOrWhiteSpace(assetChangeTag) && !string.IsNullOrWhiteSpace(assetPath))
            {
                assetChangeTag = ResolveAssetChangeTag(assetPath);
            }

            string protectionFingerprint = NormalizePreviewCachePart(BindPsdLayer.GetPreviewProtectionFingerprint());
            return string.Join(
                "|",
                "PsdLayerPreview",
                assetPath,
                assetChangeTag,
                protectionFingerprint,
                BindPsdLayerIndex.ToString(CultureInfo.InvariantCulture),
                NormalizePreviewCachePart(SourceLayerName),
                BindPsdLayer.Left.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Top.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Width.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Height.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.IsGroup ? "1" : "0",
                BindPsdLayer.IsVisible ? "1" : "0",
                BindPsdLayer.Opacity.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string NormalizePreviewCachePart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace("\\", "/").ToLowerInvariant();
        }

        private static string ResolveAssetChangeTag(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            {
                return string.Empty;
            }

            return new FileInfo(assetPath).LastWriteTimeUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        private void ReleasePreviewTexture()
        {
            if (!string.IsNullOrWhiteSpace(previewCacheKey))
            {
                bool wasManaged = PsdLayerPreviewCache.Release(previewCacheKey);
                if (!wasManaged && PreviewTexture != null)
                {
                    DestroyImmediate(PreviewTexture);
                }
            }
            else if (PreviewTexture != null)
            {
                DestroyImmediate(PreviewTexture);
            }

            PreviewTexture = null;
            previewCacheKey = string.Empty;
        }

        /// <summary>
        /// Convert the bound PSD layer to a Texture2D.
        /// </summary>
        /// <param name="psdLayer"></param>
        /// <returns>Texture2D</returns>
        internal Texture2D ConvertNodePreviewToTexture2D()
        {
            var rendered = RenderNodeImage(previewRender: true);
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            return CreateTextureFromRenderedImage(rendered, true);
        }

        private static Texture2D CreateTextureFromRenderedImage(PsdRenderedImage rendered, bool transient)
        {
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            GraphicsFormat textureFormat = rendered.IsHighBitDepth ? GraphicsFormat.R16G16B16A16_UNorm : GraphicsFormat.R8G8B8A8_SRGB;
            var texture = new Texture2D(rendered.Width, rendered.Height, textureFormat, TextureCreationFlags.None);
            texture.hideFlags = transient ? HideFlags.HideAndDontSave : HideFlags.None;
            texture.alphaIsTransparency = true;
            if (rendered.IsHighBitDepth)
            {
                ushort[] rgba64 = rendered.Rgba64;
                byte[] rawData = new byte[rgba64.Length * sizeof(ushort)];
                Buffer.BlockCopy(rgba64, 0, rawData, 0, rawData.Length);
                texture.LoadRawTextureData(rawData);
            }
            else
            {
                texture.LoadRawTextureData(rendered.Rgba32);
            }
            texture.Apply(false, false);
            return texture;
        }

        internal static Sprite LoadSpriteAssetAtPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                return sprite;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault();
        }

        internal static string ResolveExportedImageAssetPath(string baseDir, string relativeKey)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(relativeKey))
            {
                return null;
            }

            return PsdResourceNaming.FindAsset(baseDir, relativeKey, false);
        }

        internal static bool TryReplaceSpriteSubAsset(string assetPath, Texture2D texture, Vector4 border, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(assetPath) || texture == null)
            {
                return false;
            }

            var existingSprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().ToArray();
            for (int i = 0; i < existingSprites.Length; i++)
            {
                if (existingSprites[i] != null)
                {
                    DestroyImmediate(existingSprites[i], true);
                }
            }

            // PPU 跟随导入器，别固定 100；轴心仍保持中心约定（导入器的 spritePivot 默认值与"左下"无法区分）。
            var spriteImporter = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
            float fallbackPixelsPerUnit = spriteImporter != null && spriteImporter.spritePixelsPerUnit > 0f
                ? spriteImporter.spritePixelsPerUnit
                : 100f;
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                fallbackPixelsPerUnit,
                0u,
                SpriteMeshType.FullRect,
                border);
            sprite.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.AddObjectToAsset(sprite, assetPath);
            EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(sprite);
            AssetDatabase.SaveAssets();
            // ForceUpdate 重导入会销毁刚挂进来的 Sprite 子资产，导致兜底路径九宫边框被归零。
            var reloadedSprite = LoadSpriteAssetAtPath(assetPath);
            if (reloadedSprite != null) sprite = reloadedSprite;
            return sprite != null;
        }

        internal bool TryGetLayerColor(out Color color)
        {
            color = default;
            if (!CanRenderNodeImage())
            {
                return false;
            }

            if (BindPsdLayer != null && BindPsdLayer.TryGetStructuralLayerColor(out color))
            {
                return true;
            }

            var rendered = RenderNodeImage(previewRender: true);
            if (!TryGetDominantRenderedColor(rendered, out color))
            {
                return false;
            }

            return true;
        }

        private bool CanRenderNodeImage()
        {
            return ShouldRenderPreviewFromCurrentChildTree()
                || BindPsdLayer != null
                || CanRenderGeneratedCompositeImage();
        }

        private bool CanRenderGeneratedCompositeImage()
        {
            if (BindPsdLayer != null || BindPsdLayerIndex >= 0 || LayerType != PsdLayerType.LayerGroup)
            {
                return false;
            }

            if (UGUIParser.IsTransparentContainerType(UIType) || UGUIParser.IsCompositeControlType(UIType))
            {
                return false;
            }

            switch (UIType)
            {
                case GUIType.FillColor:
                case GUIType.Text:
                case GUIType.TMPText:
                case GUIType.Button_Text:
                case GUIType.Dropdown_Label:
                case GUIType.InputField_Placeholder:
                case GUIType.InputField_Text:
                case GUIType.Toggle_Label:
                    return false;
                default:
                    return true;
            }
        }

        private PsdRenderedImage RenderNodeImage(bool previewRender)
        {
            if (aiRenderTree != null) return PsdLayerRenderer.RenderTree(aiRenderTree, includeHiddenLayers: true, applyClippingMasks: true, isPreviewRender: previewRender);
            if (ShouldRenderPreviewFromCurrentChildTree())
            {
                return RenderGeneratedCompositeImage(previewRender);
            }

            if (BindPsdLayer != null)
            {
                return previewRender ? BindPsdLayer.RenderPreview() : BindPsdLayer.Render();
            }

            if (!CanRenderGeneratedCompositeImage())
            {
                return null;
            }

            return RenderGeneratedCompositeImage(previewRender);
        }

        private bool ShouldRenderPreviewFromCurrentChildTree()
        {
            // The Unity hierarchy is authoritative, including groups emptied by regrouping.
            return LayerType == PsdLayerType.LayerGroup;
        }

        private PsdRenderedImage RenderGeneratedCompositeImage(bool previewRender)
        {
            var renderTree = BuildCurrentRenderTree();
            return PsdLayerRenderer.RenderTree(
                renderTree,
                includeHiddenLayers: false,
                applyClippingMasks: true,
                isPreviewRender: previewRender);
        }

        internal PsdLayerRenderNode BuildCurrentRenderTree()
        {
            return BuildCurrentRenderTree(this, true);
        }

        internal bool HasOverlappingGeneratedGraphics()
        {
            var graphics = new List<PsdLayerRenderNode>(Math.Max(2, transform.childCount));
            CollectGeneratedGraphicRenderTrees(this, graphics, includeSelf: false);
            if (graphics.Count < 2)
            {
                return false;
            }

            var renderTree = new PsdLayerRenderNode(null, graphics.ToArray());
            return PsdLayerRenderer.HasOverlappingDirectChildren(
                renderTree,
                includeHiddenLayers: false,
                applyClippingMasks: true);
        }

        private static void CollectGeneratedGraphicRenderTrees(
            PsdLayerNode node,
            List<PsdLayerRenderNode> graphics,
            bool includeSelf)
        {
            if (node == null || graphics == null || !node.gameObject.activeSelf)
            {
                return;
            }

            if (includeSelf)
            {
                if (node.LayerType != PsdLayerType.LayerGroup)
                {
                    if (node.UIType != GUIType.Null && node.BindPsdLayer != null)
                    {
                        graphics.Add(new PsdLayerRenderNode(node.BindPsdLayer));
                    }
                    return;
                }

                if (node.BindPsdLayer != null && node.SourceOpacity <= 0f)
                {
                    return;
                }

                if (node.BakesGroupOpacityIntoGeneratedImage)
                {
                    var compositeTree = node.BuildCurrentRenderTree();
                    if (compositeTree != null)
                    {
                        graphics.Add(compositeTree);
                    }
                    return;
                }

                if (node.CollapseChildrenForGeneration && node.GroupTextSourcePsdLayer != null)
                {
                    graphics.Add(new PsdLayerRenderNode(node.GroupTextSourcePsdLayer));
                    return;
                }
            }

            for (int i = 0, childCount = node.transform.childCount; i < childCount; i++)
            {
                var childNode = node.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode != null)
                {
                    CollectGeneratedGraphicRenderTrees(childNode, graphics, includeSelf: true);
                }
            }
        }

        private static PsdLayerRenderNode BuildCurrentRenderTree(PsdLayerNode node, bool includeInactiveRoot)
        {
            if (node == null || (!includeInactiveRoot && !node.gameObject.activeSelf))
            {
                return null;
            }

            if (node.aiRenderTree != null) return node.aiRenderTree;
            var children = new List<PsdLayerRenderNode>(node.transform.childCount);
            for (int i = 0, childCount = node.transform.childCount; i < childCount; i++)
            {
                var childNode = node.transform.GetChild(i).GetComponent<PsdLayerNode>();
                if (childNode == null)
                {
                    continue;
                }

                var childTree = BuildCurrentRenderTree(childNode, false);
                if (childTree != null)
                {
                    children.Add(childTree);
                }
            }

            var sourceLayer = node.BindPsdLayer;
            if (sourceLayer != null && !sourceLayer.IsGroup)
            {
                return new PsdLayerRenderNode(sourceLayer);
            }

            if (children.Count == 0)
            {
                return null;
            }

            return new PsdLayerRenderNode(sourceLayer, children.ToArray());
        }

        // Sample around the center first so localized watermark noise does not decide the color.
        private static bool TryGetDominantRenderedColor(PsdRenderedImage rendered, out Color color)
        {
            color = default;
            if (rendered == null || rendered.IsEmpty || rendered.Rgba32 == null || rendered.Rgba32.Length < 4)
            {
                return false;
            }

            int offsetX = rendered.Width <= 1 ? 0 : Mathf.Max(1, Mathf.RoundToInt(rendered.Width * 0.25f));
            int offsetY = rendered.Height <= 1 ? 0 : Mathf.Max(1, Mathf.RoundToInt(rendered.Height * 0.25f));
            int centerX = rendered.Width / 2;
            int centerY = rendered.Height / 2;

            uint[] samples = new uint[4];
            int sampleCount = 0;

            if (TryReadRenderedPixel(rendered, centerX, Mathf.Clamp(centerY - offsetY, 0, rendered.Height - 1), out var sample))
            {
                samples[sampleCount++] = sample;
            }
            if (TryReadRenderedPixel(rendered, centerX, Mathf.Clamp(centerY + offsetY, 0, rendered.Height - 1), out sample))
            {
                samples[sampleCount++] = sample;
            }
            if (TryReadRenderedPixel(rendered, Mathf.Clamp(centerX - offsetX, 0, rendered.Width - 1), centerY, out sample))
            {
                samples[sampleCount++] = sample;
            }
            if (TryReadRenderedPixel(rendered, Mathf.Clamp(centerX + offsetX, 0, rendered.Width - 1), centerY, out sample))
            {
                samples[sampleCount++] = sample;
            }

            if (sampleCount <= 0)
            {
                return false;
            }

            uint bestSample = samples[0];
            int bestCount = 1;
            for (int i = 0; i < sampleCount; i++)
            {
                uint current = samples[i];
                int currentCount = 1;
                for (int j = i + 1; j < sampleCount; j++)
                {
                    if (samples[j] == current)
                    {
                        currentCount++;
                    }
                }

                if (currentCount > bestCount)
                {
                    bestCount = currentCount;
                    bestSample = current;
                }
            }

            color = ToUnityColor(bestSample);
            return true;
        }

        private static bool TryReadRenderedPixel(PsdRenderedImage rendered, int x, int y, out uint rgba)
        {
            rgba = 0;
            if (rendered == null || rendered.IsEmpty || rendered.Rgba32 == null || rendered.Rgba32.Length < 4)
            {
                return false;
            }

            if (x < 0 || y < 0 || x >= rendered.Width || y >= rendered.Height)
            {
                return false;
            }

            int dataIndex = (((rendered.Height - 1 - y) * rendered.Width) + x) * 4;
            if (dataIndex < 0 || dataIndex + 3 >= rendered.Rgba32.Length)
            {
                return false;
            }

            byte alpha = rendered.Rgba32[dataIndex + 3];
            if (alpha == 0)
            {
                return false;
            }

            rgba = PackRgba(rendered.Rgba32[dataIndex], rendered.Rgba32[dataIndex + 1], rendered.Rgba32[dataIndex + 2], alpha);
            return true;
        }

        private static uint PackRgba(byte r, byte g, byte b, byte a)
        {
            return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        }

        private static Color ToUnityColor(uint rgba)
        {
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        internal PsdLayerNode FindOwnedSubLayerNode(params GUIType[] uiTps)
        {
            if (uiTps == null || uiTps.Length == 0)
            {
                return null;
            }

            for (int i = 0, count = aiAuthoritative && AiUiContract.IsRole(uiTps[0]) ? 1 : uiTps.Length; i < count; i++)
            {
                var nodes = FindOwnedSubLayerNodes(uiTps[i]);
                if (nodes != null && nodes.Length > 0)
                {
                    return nodes[0];
                }
            }
            return null;
        }
        internal PsdLayerNode[] FindOwnedSubLayerNodes(params GUIType[] uiTps)
        {
            if (uiTps == null || uiTps.Length == 0)
            {
                return null;
            }

            var matches = new List<PsdLayerNode>(4);
            var matchedIds = new HashSet<PsdObjectId>();
            // AI 权威节点：首参是角色时只认角色，不能回退去认领通用 Image/Text（测试
            // ExplicitRoleLookupNeverClaimsGenericImage 钉住该契约）；需要回退由调用方自己分步查。
            for (int i = 0, count = aiAuthoritative && AiUiContract.IsRole(uiTps[0]) ? 1 : uiTps.Length; i < count; i++)
            {
                CollectOwnedSubLayerNodesRecursive(transform, uiTps[i], matches, matchedIds);
            }
            return matches.Count > 0 ? matches.ToArray() : null;
        }

        internal PsdLayerNode FindOwnedContainerBackground()
        {
            var background = FindOwnedSubLayerNode(GUIType.Background);
            if (aiAuthoritative) return background;
            if (background != null) return background;
            var images = GetComponentsInChildren<PsdLayerNode>(true);
            var ownerRect = LayerRect;
            foreach (var image in images)
            {
                if (image == this || (image.UIType != GUIType.Image && image.UIType != GUIType.RawImage)
                    || !image.gameObject.activeInHierarchy
                    || !IsContainerBackgroundCandidate(image.name, ownerRect, image.LayerRect)) continue;
                var parent = image.transform.parent;
                while (parent != null && parent != transform)
                {
                    var parentNode = parent.GetComponent<PsdLayerNode>();
                    if (parentNode != null && parentNode.UIType != GUIType.Null) break;
                    parent = parent.parent;
                }
                if (parent != transform) continue; // Do not cross another UI instance, including a Panel.
                if (background != null) return null; // Multiple candidates require an explicit role.
                background = image;
            }
            return background;
        }

        internal static bool IsContainerBackgroundCandidate(string name, Rect owner, Rect image)
        {
            if (owner.width <= 0 || owner.height <= 0 || image.width <= 0 || image.height <= 0) return false;
            bool namedBackground = false;
            foreach (string token in name.Split(new[] { '_', '-', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(token, "bg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "background", StringComparison.OrdinalIgnoreCase)
                    || token == "背景" || token == "底图")
                {
                    namedBackground = true;
                    break;
                }
            }
            // LayerRect stores a center, not Rect.xMin/yMin. Require coverage on both axes.
            return namedBackground && image.width >= owner.width * .8f && image.height >= owner.height * .8f
                && image.width <= owner.width * 1.25f && image.height <= owner.height * 1.25f
                && Mathf.Abs(image.x - owner.x) + owner.width * .4f <= image.width * .5f
                && Mathf.Abs(image.y - owner.y) + owner.height * .4f <= image.height * .5f;
        }
        internal PsdLayerNode FindNearestCompatibleOwner()
        {
            if (!UGUIParser.IsSemanticUIType(UIType))
            {
                return FindNearestCompatibleMainOwner();
            }

            var current = transform.parent;
            while (current != null)
            {
                var ownerNode = current.GetComponent<PsdLayerNode>();
                if (ownerNode == null)
                {
                    current = current.parent;
                    continue;
                }

                if (UGUIParser.CanOwnSemanticRole(ownerNode.UIType, UIType))
                {
                    return ownerNode;
                }

                if (UGUIParser.IsTransparentContainerType(ownerNode.UIType))
                {
                    current = current.parent;
                    continue;
                }

                current = current.parent;
            }

            return null;
        }
        internal bool IsOwnedBy(PsdLayerNode owner)
        {
            if (owner == null || owner == this)
            {
                return false;
            }

            if (UGUIParser.IsSemanticUIType(UIType))
            {
                return FindNearestCompatibleOwner() == owner;
            }

            if (!UGUIParser.IsMainUIType(UIType))
            {
                return false;
            }

            var mainOwner = FindNearestCompatibleMainOwner();
            if (mainOwner != null)
            {
                return mainOwner == owner;
            }

            return FindNearestStructuralOwner() == owner;
        }
        internal bool TryGetReuseTargetAsset(out UnityEngine.Object targetObj)
        {
            targetObj = null;
            if (!HasReuseReference) return false;
            var sharedOutput = UGUIParser.Instance?.SharedAssetsOutput;
            string targetPath = Psd2UIFormConverter.Instance.ResolveResource(sharedOutput, ReuseTargetKey, false);
            if (string.IsNullOrWhiteSpace(targetPath)) return false;
            targetObj = (UnityEngine.Object)LoadSpriteAssetAtPath(targetPath) ?? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
            return targetObj != null;
        }
        internal bool TryGetReusePrefabAsset(out GameObject targetPrefab)
        {
            targetPrefab = null;
            if (!HasReusePrefabReference) return false;
            var sharedDir = UGUIParser.Instance?.SharedPrefabOutput;
            if (string.IsNullOrWhiteSpace(sharedDir)) return false;

            var targetPath = Psd2UIFormConverter.Instance.ResolveResource(sharedDir, ReusePrefabKey, true);
            if (targetPath != null) targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            return targetPrefab != null;
        }
        internal bool TryGetReuseTarget(out PsdLayerNode target)
        {
            target = null;
            if (!HasReuseReference) return false;
            if (reuseTargetRef != null && reuseTargetRef.TryGetTarget(out target) && target != null)
            {
                return true;
            }
            var converter = Psd2UIFormConverter.Instance;
            if (converter == null) return false;
            target = converter.FindLayerNodeByKey(ReuseTargetKey);
            if (target != null)
            {
                reuseTargetRef = new WeakReference<PsdLayerNode>(target);
                return true;
            }
            return false;
        }
        internal PsdLayerNode FindLayerNodeInChildren(GUIType uiTp)
        {
            var layers = GetComponentsInChildren<PsdLayerNode>(true);
            if (layers != null && layers.Length > 0)
            {
                return layers.FirstOrDefault(layer => layer.UIType == uiTp);
            }
            return null;
        }

        private void CollectOwnedSubLayerNodesRecursive(Transform parent, GUIType target, List<PsdLayerNode> matches, HashSet<PsdObjectId> matchedIds)
        {
            if (parent == null || matches == null || matchedIds == null)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var childNode = child != null ? child.GetComponent<PsdLayerNode>() : null;
                if (childNode == null)
                {
                    continue;
                }

                if (childNode.UIType == target && childNode.IsAiReady && childNode.IsOwnedBy(this))
                {
#if UNITY_6000_3_OR_NEWER
                    var instanceId = childNode.GetEntityId();
#else
                    int instanceId = childNode.GetInstanceID();
#endif
                    if (matchedIds.Add(instanceId))
                    {
                        matches.Add(childNode);
                    }
                }

                if (childNode.ShouldHideDescendantsFromOwnerQuery())
                {
                    continue;
                }

                CollectOwnedSubLayerNodesRecursive(child, target, matches, matchedIds);
            }
        }

        private PsdLayerNode FindNearestStructuralOwner()
        {
            var current = transform.parent;
            while (current != null)
            {
                var ownerNode = current.GetComponent<PsdLayerNode>();
                if (ownerNode == null)
                {
                    current = current.parent;
                    continue;
                }

                if (UGUIParser.IsTransparentContainerType(ownerNode.UIType))
                {
                    current = current.parent;
                    continue;
                }

                return ownerNode;
            }

            return null;
        }

        private PsdLayerNode FindNearestCompatibleMainOwner()
        {
            for (var current = transform.parent; current != null; current = current.parent)
            {
                var owner = current.GetComponent<PsdLayerNode>();
                if (owner == null || owner.UIType == GUIType.Null) continue;
                // A nested control belongs to its nearest structural owner. Do not cross a
                // ScrollView/Item/Panel to let an outer Dropdown or ToggleGroup consume it.
                return owner;
            }
            return null;
        }

        private bool ShouldHideDescendantsFromOwnerQuery()
        {
            if (CollapseChildrenForGeneration || HasReuseReference || HasReusePrefabReference)
            {
                return true;
            }

            if (UGUIParser.IsStandaloneGraphicMainType(UIType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parse text-layer metadata from the bound PSD layer.
        /// </summary>
        /// <param name="layer"></param>
        /// <returns></returns>
        internal bool IsTextLayer(out PsdTextLayerInfo layer)
        {
            layer = null;
            var textSource = mTextSourcePsdLayer ?? BindPsdLayer;
            if (textSource == null) return false;

            return textSource.TryGetTextLayerInfo(out layer);
        }

        internal Vector3 UnityRotationEulerAngles
        {
            get
            {
                var sourceLayer = mTextSourcePsdLayer ?? BindPsdLayer;
                if (sourceLayer == null)
                {
                    return Vector3.zero;
                }

                if (sourceLayer.IsTextLayer()
                    && PsdLayerTransformUtility.TryGetTextUnityRotation(sourceLayer, out var textRotation))
                {
                    return textRotation;
                }

                if (sourceLayer.IsGroup)
                {
                    return Vector3.zero;
                }

                return PsdLayerTransformUtility.TryGetPlacedUnityRotation(sourceLayer, out var imageRotation)
                    ? imageRotation
                    : Vector3.zero;
            }
        }

        internal Rect UnityRect
        {
            get
            {
                return UsesStaticTextLayoutRect() && TryGetTextLayoutRect(out var textRect)
                    ? textRect
                    : LayerRect;
            }
        }

        private bool UsesStaticTextLayoutRect()
        {
            switch (UIType)
            {
                case GUIType.Text:
                case GUIType.TMPText:
                case GUIType.Button_Text:
                case GUIType.Dropdown_Label:
                case GUIType.Toggle_Label:
                    return true;
                default:
                    return false;
            }
        }

        internal bool TryGetTextLayoutRect(out Rect rect)
        {
            rect = default;
            if (IsAiTarget && (aiExplicitRect || aiText?.mode == "value"))
            {
                rect = LayerRect;
                return rect.width > 0 && rect.height > 0;
            }
            var sourceLayer = mTextSourcePsdLayer ?? BindPsdLayer;
            if (sourceLayer == null
                || !sourceLayer.TryGetTextLayerInfo(out var textInfo))
            {
                return false;
            }

            rect = sourceLayer.GetLayerRect();
            bool isRotated = PsdLayerTransformUtility.TryGetTextUnityRotation(sourceLayer, out var rotation)
                && Mathf.Abs(rotation.z) > 0.001f;
            bool needsLayoutSize = textInfo.TextType == PsdTextType.Paragraph || isRotated;
            if (!needsLayoutSize)
            {
                return true;
            }

            if (!sourceLayer.TryGetTextLayoutBounds(
                    out var centerX,
                    out var centerY,
                    out var width,
                    out var height))
            {
                return true;
            }

            var document = sourceLayer.Document;
            float layoutCenterX = (float)(centerX - document.Width * 0.5d);
            if (!isRotated)
            {
                // Photoshop's paragraph bounds describe the editable text box, whose
                // unused vertical area does not correspond to Unity's font baseline.
                // Keep its wrapping width while positioning the text by rendered pixels.
                rect = new Rect(layoutCenterX, rect.position.y, (float)width, rect.height);
                return true;
            }

            rect = new Rect(
                layoutCenterX,
                (float)(document.Height * 0.5d - centerY),
                (float)width,
                (float)height);
            return true;
        }

        internal bool TryGetTextUnityRotation(out Vector3 rotation)
        {
            rotation = Vector3.zero;
            if (IsAiTarget && aiText?.mode == "value")
            {
                rotation.z = aiText.rotation;
                return true;
            }
            var sourceLayer = mTextSourcePsdLayer ?? BindPsdLayer;
            return sourceLayer != null
                && sourceLayer.IsTextLayer()
                && PsdLayerTransformUtility.TryGetTextUnityRotation(sourceLayer, out rotation);
        }

        internal void InitPsdLayers(PsdDocument psdInstance)
        {
            if (psdInstance != null) RealignSourceLayerIndexes(psdInstance);
            if (IsAiTarget) UpdateAiBinding(psdInstance);
            mTextSourcePsdLayer = null;
            if (psdInstance == null)
            {
                BindPsdLayer = null;
                return;
            }
            BindPsdLayer = BindPsdLayerIndex >= 0 ? psdInstance.GetLayerByFlatIndex(BindPsdLayerIndex) : null;
            if (textSourceBindPsdLayerIndex >= 0)
            {
                mTextSourcePsdLayer = psdInstance.GetLayerByFlatIndex(textSourceBindPsdLayerIndex);
            }
        }
        internal bool ParseTextLayerInfo(out TextLayerInfo textInfo)
        {
            return IsAiTarget ? TryParseAiText(out textInfo) : ParseNativeTextLayerInfo(out textInfo);
        }

        private bool ParseNativeTextLayerInfo(out TextLayerInfo textInfo)
        {
            textInfo = default;
            if (!IsTextLayer(out var txtLayer)
                || txtLayer.StyleRuns == null
                || txtLayer.StyleRuns.Length == 0)
            {
                return false;
            }

            var styleRuns = new TextStyleRunInfo[txtLayer.StyleRuns.Length];
            for (int i = 0; i < styleRuns.Length; i++)
            {
                var sourceRun = txtLayer.StyleRuns[i];
                styleRuns[i] = ConvertTextStyleRun(sourceRun);
            }

            var paragraphRuns = new TextParagraphRunInfo[txtLayer.ParagraphRuns != null ? txtLayer.ParagraphRuns.Length : 0];
            for (int i = 0; i < paragraphRuns.Length; i++)
            {
                var sourceRun = txtLayer.ParagraphRuns[i];
                var paragraph = sourceRun.Paragraph;
                paragraphRuns[i] = new TextParagraphRunInfo()
                {
                    Start = sourceRun.Start,
                    Length = sourceRun.Length,
                    Justification = paragraph.Justification,
                    FirstLineIndent = paragraph.FirstLineIndent,
                    StartIndent = paragraph.StartIndent,
                    EndIndent = paragraph.EndIndent,
                    SpaceBefore = paragraph.SpaceBefore,
                    SpaceAfter = paragraph.SpaceAfter,
                    AutoHyphenate = paragraph.AutoHyphenate,
                };
            }

            var primaryStyle = styleRuns[0];
            textInfo = new TextLayerInfo()
            {
                Text = txtLayer.Text,
                IsParagraphText = txtLayer.TextType == PsdTextType.Paragraph,
                LayerOpacity = Mathf.Clamp01(txtLayer.LayerOpacity),
                FillOpacity = Mathf.Clamp01(txtLayer.FillOpacity),
                StyleRuns = styleRuns,
                ParagraphRuns = paragraphRuns,
                FontSize = primaryStyle.FontSize,
                IsAutoLineSpacing = primaryStyle.IsAutoLineSpacing,
                CharacterSpacing = primaryStyle.CharacterSpacing,
                LineSpacing = primaryStyle.LineSpacing,
                Color = primaryStyle.Color,
                FontStyle = primaryStyle.FontStyle,
                TMPFontStyle = primaryStyle.TMPFontStyle,
                FontName = primaryStyle.FontName,
                Justification = paragraphRuns.Length > 0
                    ? paragraphRuns[0].Justification
                    : PsdTextJustification.Left,
                AutoKerning = primaryStyle.AutoKerning,
                OutlineColor = Color.clear,
                TMPOutlinePosition = TextLayerInfo.TMPOutlineMode.Center,
                ShadowColor = Color.clear,
                OuterGlow = default,
                InnerGlow = default,
                BevelHighlightColor = Color.white,
                BevelHighlightOpacity = 1f,
                BevelShadowColor = Color.black,
                BevelShadowOpacity = 1f,
            };
            ParseTextLayerEffects(txtLayer, ref textInfo);
            return true;
        }

        internal static TextStyleRunInfo ConvertTextStyleRun(PsdTextStyleRun sourceRun)
        {
            var source = sourceRun.Style;
            float verticalScale = Mathf.Max(0.0001f, source.VerticalScale);
            var run = new TextStyleRunInfo()
            {
                Start = sourceRun.Start,
                Length = sourceRun.Length,
                FontName = source.FontName,
                FontSize = Mathf.Max(0.01f, source.FontSize * verticalScale),
                Color = ConvertPsdColor(source.Color),
                FontStyle = FontStyle.Normal,
                TMPFontStyle = TMPro.FontStyles.Normal,
                CharacterSpacing = source.Tracking * 0.1f,
                LineSpacing = source.Leading,
                IsAutoLineSpacing = source.AutoLeading || source.Leading <= 0f,
                BaselineShift = source.BaselineShift,
                HorizontalScale = source.HorizontalScale / verticalScale,
                AutoKerning = source.AutoKerning,
                Kerning = source.Kerning,
                Ligatures = source.Ligatures,
                NoBreak = source.NoBreak,
                Capitalization = source.Capitalization,
            };

            if (source.FauxBold && source.FauxItalic)
            {
                run.FontStyle = FontStyle.BoldAndItalic;
            }
            else if (source.FauxBold)
            {
                run.FontStyle = FontStyle.Bold;
            }
            else if (source.FauxItalic)
            {
                run.FontStyle = FontStyle.Italic;
            }

            if (source.FauxItalic)
                run.TMPFontStyle |= TMPro.FontStyles.Italic;
            if (source.FauxBold)
                run.TMPFontStyle |= TMPro.FontStyles.Bold;
            if (source.Underline)
                run.TMPFontStyle |= TMPro.FontStyles.Underline;
            if (source.Strikethrough)
                run.TMPFontStyle |= TMPro.FontStyles.Strikethrough;
            if (source.Capitalization == PsdTextCapitalization.AllCaps)
                run.TMPFontStyle |= TMPro.FontStyles.UpperCase;
            else if (source.Capitalization == PsdTextCapitalization.SmallCaps)
                run.TMPFontStyle |= TMPro.FontStyles.SmallCaps;

            return run;
        }

        private static void ParseTextLayerEffects(PsdTextLayerInfo txtLayer, ref TextLayerInfo textInfo)
        {
            if (txtLayer == null) return;

            if (txtLayer.Shadow != null && txtLayer.Shadow.Enabled)
            {
                textInfo.HasShadow = true;
                textInfo.ShadowIsInner = false;
                textInfo.ShadowColor = ConvertPsdColor(txtLayer.Shadow.Color);
                textInfo.ShadowOffset = Quaternion.Euler(0, 0, txtLayer.Shadow.Angle) * (Vector2.left * txtLayer.Shadow.Distance);
                textInfo.ShadowSpread = Mathf.Clamp01(txtLayer.Shadow.Spread);
                textInfo.ShadowSoftness = Mathf.Max(0f, txtLayer.Shadow.Blur);
                textInfo.ShadowBlendModeKey = txtLayer.Shadow.BlendModeKey;
                textInfo.ShadowContour = ConvertTextEffectContour(txtLayer.Shadow.Contour);
            }
            else if (txtLayer.InnerShadow != null && txtLayer.InnerShadow.Enabled)
            {
                textInfo.HasShadow = true;
                textInfo.ShadowIsInner = true;
                textInfo.ShadowColor = ConvertPsdColor(txtLayer.InnerShadow.Color);
                textInfo.ShadowOffset = Quaternion.Euler(0, 0, txtLayer.InnerShadow.Angle) * (Vector2.left * txtLayer.InnerShadow.Distance);
                textInfo.ShadowSpread = Mathf.Clamp01(txtLayer.InnerShadow.Spread);
                textInfo.ShadowSoftness = Mathf.Max(0f, txtLayer.InnerShadow.Blur);
                textInfo.ShadowBlendModeKey = txtLayer.InnerShadow.BlendModeKey;
                textInfo.ShadowContour = ConvertTextEffectContour(txtLayer.InnerShadow.Contour);
            }

            if (txtLayer.Stroke != null && txtLayer.Stroke.Enabled)
            {
                textInfo.HasOutline = true;
                textInfo.OutlineSize = CalculateUnityOutlineSize(txtLayer.Stroke);
                // Photoshop stores the stroke kernel diameter, while TMP's SDF
                // outline solve operates on the distance-field radius.
                textInfo.TMPOutlineSize = Mathf.Max(0f, txtLayer.Stroke.Size * 0.5f);
                textInfo.TMPOutlinePosition = ConvertTMPOutlineMode(txtLayer.Stroke.Position);
                textInfo.OutlineColor = ConvertPsdColor(txtLayer.Stroke.Color);
                textInfo.OutlineBlendModeKey = txtLayer.Stroke.BlendModeKey;
            }

            textInfo.OuterGlow = ConvertTextGlowEffect(txtLayer.OuterGlow, false);
            textInfo.InnerGlow = ConvertTextGlowEffect(txtLayer.InnerGlow, true);

            if (txtLayer.Bevel != null && txtLayer.Bevel.Enabled)
            {
                textInfo.HasBevel = true;
                textInfo.BevelIsInner = txtLayer.Bevel.Inner;
                textInfo.BevelSize = Mathf.Max(0f, txtLayer.Bevel.Size);
                textInfo.BevelDepth = Mathf.Max(0f, txtLayer.Bevel.Depth);
                textInfo.BevelSoften = Mathf.Max(0f, txtLayer.Bevel.Soften);
                textInfo.BevelAngle = txtLayer.Bevel.Angle;
                textInfo.BevelAltitude = Mathf.Clamp(txtLayer.Bevel.Altitude, 0f, 90f);
                textInfo.BevelTechniqueKey = txtLayer.Bevel.TechniqueKey;
                textInfo.BevelDirectionKey = txtLayer.Bevel.DirectionKey;
                textInfo.BevelHighlightBlendModeKey = txtLayer.Bevel.HighlightBlendModeKey;
                textInfo.BevelShadowBlendModeKey = txtLayer.Bevel.ShadowBlendModeKey;
                textInfo.BevelHighlightColor = ConvertPsdColor(txtLayer.Bevel.HighlightColor);
                textInfo.BevelHighlightOpacity = Mathf.Clamp01(txtLayer.Bevel.HighlightOpacity);
                textInfo.BevelShadowColor = ConvertPsdColor(txtLayer.Bevel.ShadowColor);
                textInfo.BevelShadowOpacity = Mathf.Clamp01(txtLayer.Bevel.ShadowOpacity);
                textInfo.BevelGlossContour = ConvertTextEffectContour(txtLayer.Bevel.GlossContour);
            }

            if (txtLayer.Gradient != null && txtLayer.Gradient.Enabled && txtLayer.Gradient.Stops != null && txtLayer.Gradient.Stops.Length >= 2)
            {
                var stops = txtLayer.Gradient.Stops;
                var convertedStops = new TextGradientStop[stops.Length];
                for (int i = 0; i < stops.Length; i++)
                {
                    var stop = stops[i];
                    convertedStops[i] = new TextGradientStop()
                    {
                        Location = Mathf.Clamp01(stop.Location),
                        Color = ConvertPsdColor(stop.Color)
                    };
                }
                Array.Sort(convertedStops, (a, b) => a.Location.CompareTo(b.Location));
                textInfo.HasGradient = true;
                textInfo.GradientAngle = txtLayer.Gradient.Angle;
                textInfo.GradientReverse = txtLayer.Gradient.Reverse;
                textInfo.GradientOpacity = Mathf.Clamp01(txtLayer.Gradient.Opacity);
                textInfo.GradientScale = Mathf.Max(0.0001f, txtLayer.Gradient.Scale);
                textInfo.GradientOffset = new Vector2(txtLayer.Gradient.OffsetX, txtLayer.Gradient.OffsetY);
                textInfo.GradientAlignWithLayer = txtLayer.Gradient.AlignWithLayer;
                textInfo.GradientDither = txtLayer.Gradient.Dither;
                textInfo.GradientStyleKey = txtLayer.Gradient.StyleKey;
                textInfo.GradientBlendModeKey = txtLayer.Gradient.BlendModeKey;
                textInfo.GradientInterpolationKey = txtLayer.Gradient.InterpolationKey;
                textInfo.GradientStops = convertedStops;
            }
        }

        private static TextGlowEffectInfo ConvertTextGlowEffect(PsdTextGlowInfo source, bool inner)
        {
            if (source == null || !source.Enabled)
                return default;

            return new TextGlowEffectInfo
            {
                Enabled = true,
                Inner = inner,
                Color = ConvertPsdColor(source.Color),
                Size = Mathf.Max(0f, source.Size),
                Spread = Mathf.Clamp01(source.Spread),
                BlendModeKey = source.BlendModeKey,
                TechniqueKey = source.TechniqueKey,
                SourceKey = source.SourceKey,
                Noise = Mathf.Clamp01(source.Noise),
                Jitter = Mathf.Clamp01(source.Jitter),
                Range = Mathf.Clamp01(source.Range),
                AntiAlias = source.AntiAlias,
                Contour = ConvertTextEffectContour(source.Contour),
            };
        }

        private static TextEffectContourPoint[] ConvertTextEffectContour(PsdTextContourPoint[] source)
        {
            if (source == null || source.Length == 0)
                return null;

            var result = new TextEffectContourPoint[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new TextEffectContourPoint
                {
                    Input = Mathf.Clamp01(source[i].Input),
                    Output = Mathf.Clamp01(source[i].Output)
                };
            }
            return result;
        }
        private static TextLayerInfo.TMPOutlineMode ConvertTMPOutlineMode(PsdTextStrokePosition position)
        {
            switch (position)
            {
                case PsdTextStrokePosition.Inside:
                    return TextLayerInfo.TMPOutlineMode.Inside;
                case PsdTextStrokePosition.Center:
                    return TextLayerInfo.TMPOutlineMode.Center;
                default:
                    return TextLayerInfo.TMPOutlineMode.Outside;
            }
        }
        private static float CalculateUnityOutlineSize(PsdTextStrokeInfo stroke)
        {
            if (stroke == null) return 0f;

            var rawSize = Mathf.Max(0f, stroke.Size);
            switch (stroke.Position)
            {
                case PsdTextStrokePosition.Inside:
                    return 0f;
                case PsdTextStrokePosition.Center:
                    return rawSize * 0.5f;
                default:
                    return rawSize;
            }
        }

        private static Color ConvertPsdColor(PsdColor color)
        {
            return new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }
    }
}

#endif
