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
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Linq;
using cn.efunstudio.psdreader;
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
        private void OnEnable()
        {
            targetLogic = target as PsdLayerNode;
            targetLogic.RefreshLayerTexture();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            var newUIType = (GUIType)EditorGUILayout.EnumPopup("UI Type", targetLogic.UIType);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(targets, "Change UI Type");

                foreach (var item in targets)
                {
                    if (item == null) continue;
                    (item as PsdLayerNode)?.SetUIType(newUIType);
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
            if (targetLogic.IsMainUIType && GUILayout.Button("生成当前节点UIForm"))
            {
                Psd2UIFormConverter.Instance.GenerateUIForm(targetLogic);
            }
            if (GUILayout.Button("导出Prefab"))
            {
                Psd2UIFormConverter.Instance.ExportAsPrefab(targetLogic);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layer Data", EditorStyles.boldLabel);
            var rect = targetLogic.LayerRect;
            DrawVector2FieldReadOnly("Position", rect.position);
            DrawVector2FieldReadOnly("Size", rect.size);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(CopyButtonWidth)))
            {
                CopyRectTransformClipboard(rect.position, rect.size);
            }
            EditorGUILayout.EndHorizontal();

            if (targetLogic.ParseTextLayerInfo(out var textInfo))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Text Data", EditorStyles.boldLabel);
                DrawReadOnlyField("Text Content", textInfo.Text ?? string.Empty);
                DrawReadOnlyField("Font Name", textInfo.FontName ?? string.Empty);
                DrawReadOnlyField("Font Size", FormatFloat(textInfo.FontSize));
                DrawReadOnlyField("Font Style", textInfo.FontStyle.ToString());
                DrawReadOnlyField("TMP Font Style", textInfo.TMPFontStyle.ToString());
                DrawReadOnlyField("Character Spacing", FormatFloat(textInfo.CharacterSpacing));
                DrawReadOnlyField("Line Spacing", textInfo.IsAutoLineSpacing ? "Auto" : FormatFloat(textInfo.LineSpacing));
                DrawReadOnlyField("Auto Line Spacing", textInfo.IsAutoLineSpacing ? "True" : "False");
                DrawColorFieldWithCopy("Color", textInfo.Color);
                DrawTextEffectsInfo(in textInfo);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawVector2FieldReadOnly(string label, Vector2 value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Vector2Field(label, value);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void CopyRectTransformClipboard(Vector2 positionValue, Vector2 sizeValue)
        {
            var temp = new GameObject("RectTransformClipboard", typeof(RectTransform));
            temp.hideFlags = HideFlags.HideAndDontSave;
            var rectTransform = temp.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = positionValue;
            rectTransform.sizeDelta = sizeValue;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
            ComponentUtility.CopyComponent(rectTransform);
            DestroyImmediate(temp);
        }


        private void DrawReadOnlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(label, value);
            EditorGUI.EndDisabledGroup();
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

        private static string FormatVector2(Vector2 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)})";
        }

        private void DrawTextEffectsInfo(in TextLayerInfo textInfo)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Text Effects", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            DrawTextOutlineInfo(in textInfo);
            DrawTextShadowInfo(in textInfo);
            DrawTextGlowInfo(in textInfo);
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

        private void DrawTextGlowInfo(in TextLayerInfo textInfo)
        {
            if (!textInfo.HasGlow)
            {
                DrawReadOnlyField("Glow", "None");
                return;
            }

            DrawReadOnlyField("Glow", textInfo.GlowIsInner ? "Inner" : "Outer");
            DrawColorFieldWithCopy("Glow Color", textInfo.GlowColor);
            DrawReadOnlyField("Glow Size", FormatFloat(textInfo.GlowSize));
            DrawReadOnlyField("Glow Spread", FormatFloat(textInfo.GlowSpread));
            DrawReadOnlyField("Glow Offset", FormatFloat(textInfo.GlowOffset));
            DrawReadOnlyField("Glow Power", FormatFloat(textInfo.GlowPower));
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


        public override bool HasPreviewGUI()
        {
            var layerNode = (target as PsdLayerNode);
            return layerNode != null && layerNode.PreviewTexture != null;
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            var layerNode = (target as PsdLayerNode);
            GUI.DrawTexture(r, layerNode.PreviewTexture, ScaleMode.ScaleToFit);
            //base.OnPreviewGUI(r, background);
        }
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
    public sealed class PsdLayerNode : MonoBehaviour
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] internal int BindPsdLayerIndex = -1;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] PsdLayerType mLayerType = PsdLayerType.Unknown;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [ReadOnlyField][SerializeField] string sourceLayerName;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] internal bool markToExport;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] internal GUIType UIType;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [HideInInspector][SerializeField] internal GUIType RoleUIType;
        internal Texture2D PreviewTexture { get; private set; }
        internal string LayerInfo { get; private set; }
        internal Rect LayerRect { get; private set; }
        internal PsdLayerType LayerType { get => mLayerType; }
        internal string SourceLayerName => sourceLayerName;
        private Psd2UIFormConverter _cachedConverter;
        internal Psd2UIFormConverter Converter
        {
            get
            {
                if (_cachedConverter == null)
                    _cachedConverter = GetComponentInParent<Psd2UIFormConverter>();
                return _cachedConverter;
            }
        }
        internal bool IsMainUIType => UGUIParser.IsMainUIType(UIType);
        internal bool HasReuseReference => !string.IsNullOrEmpty(ReuseTargetKey);
        internal bool HasReusePrefabReference => !string.IsNullOrEmpty(ReusePrefabKey);
        internal string ReuseTargetKey => TryGetReuseTargetName(out var targetName) ? NormalizeReferencePath(targetName) : null;
        internal string ReuseTargetDisplayName => TryGetReuseTargetName(out var targetName) ? targetName : null;
        internal string ReusePrefabKey => TryGetReusePrefabName(out var targetName) ? NormalizeReferencePath(targetName) : null;
        internal string ReusePrefabDisplayName => TryGetReusePrefabName(out var targetName) ? targetName : null;
        internal string LayerNameLookupKey => NormalizeLayerName(gameObject?.name, preserveRefTags: true);
        private WeakReference<PsdLayerNode> reuseTargetRef = null;
        private string previewCacheKey;
        /// <summary>
        /// Cached bound PSD layer.
        /// </summary>
        private PsdLayer mBindPsdLayer;

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
                LayerInfo = $"{LayerRect}";
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
            if (string.IsNullOrWhiteSpace(rawPath)) return null;
            var workingPath = rawPath.Trim();
            var parser = UGUIParser.Instance;
            if (parser != null && parser.ConvertZh2En)
            {
                workingPath = LayerNameUtility.ConvertChineseToLetters(workingPath);
            }

            var sanitizedPath = LayerNameUtility.SanitizeRelativePath(workingPath);
            return string.IsNullOrWhiteSpace(sanitizedPath) ? null : sanitizedPath;
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
        private void OnDestroy()
        {
            ReleasePreviewTexture();
        }
        internal void SetUIType(GUIType uiType, bool triggerParseFunc = true)
        {
            var parser = UGUIParser.Instance;
            this.UIType = parser != null ? parser.ResolvePreferredUIType(uiType) : uiType;
            RemoveUIHelper();

            if (triggerParseFunc)
            {
                RefreshUIHelper(true);
            }
        }
        internal void SetResolvedTypes(GUIType uiType, GUIType roleUIType, bool triggerParseFunc = true)
        {
            RoleUIType = roleUIType;
            SetUIType(uiType, triggerParseFunc);
        }
        internal void RefreshUIHelper(bool refreshParent = false)
        {
            var parser = UGUIParser.Instance;
            if (parser != null)
            {
                UIType = parser.ResolvePreferredUIType(UIType);
            }
            if (UIType == GUIType.Null) return;

            var uiHelperTp = UGUIParser.Instance.GetHelperType(UIType);
            if (uiHelperTp != null)
            {
                var helper = (gameObject.GetComponent(uiHelperTp) ?? gameObject.AddComponent(uiHelperTp)) as UIHelperBase;
                helper.ParseAndAttachUIElements();
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
                        break;
                    }
                    currentNode = currentNode.parent;
                }

            }
            EditorUtility.SetDirty(this);
        }
        private void RemoveUIHelper()
        {
            var uiHelpers = this.GetComponents<UIHelperBase>();
            if (uiHelpers != null)
            {
                foreach (var uiHelper in uiHelpers)
                {
                    DestroyImmediate(uiHelper);
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
            var sanitized = LayerNameUtility.Sanitize(rawName);
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
            var rawName = string.IsNullOrWhiteSpace(node.name) ? node.UIType.ToString() : node.name;
            return GetSanitizedExportName(rawName, convertFileNameToLower, node.UIType.ToString());
        }

        private string ResolveDuplicateExportName(Psd2UIFormConverter converter, string desiredName, bool convertFileNameToLower)
        {
            if (converter == null || string.IsNullOrWhiteSpace(desiredName)) return desiredName;

            var allNodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            if (allNodes == null || allNodes.Length == 0) return desiredName;

            var exportNodes = allNodes
                .Where(node => node != null && (node == this || node.NeedExportImage()))
                .ToArray();
            if (exportNodes.Length <= 1) return desiredName;

            var desiredNames = exportNodes
                .Select(node => new { Node = node, Name = GetLayerExportBaseName(node, convertFileNameToLower) })
                .ToArray();

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in desiredNames)
            {
                reservedNames.Add(entry.Name);
            }

            var duplicates = desiredNames
                .Where(entry => string.Equals(entry.Name, desiredName, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Node)
                .OrderBy(node => node.BindPsdLayerIndex)
                .ThenBy(node => node.GetInstanceID())
                .ToArray();

            if (duplicates.Length <= 1) return desiredName;

            var position = Array.IndexOf(duplicates, this);
            if (position <= 0) return desiredName;

            int suffixNumber = 1;
            for (int duplicateIndex = 1; duplicateIndex <= position; duplicateIndex++)
            {
                while (reservedNames.Contains($"{desiredName}_{suffixNumber}"))
                {
                    suffixNumber++;
                }
                if (duplicateIndex == position)
                {
                    return $"{desiredName}_{suffixNumber}";
                }
                suffixNumber++;
            }

            return desiredName;
        }

        private static string ResolveOverrideExportPath(string baseDir, string overrideFileName, bool convertFileNameToLower, string fallbackName, out string targetDir)
        {
            targetDir = baseDir;
            if (string.IsNullOrWhiteSpace(overrideFileName))
            {
                return GetSanitizedExportName(fallbackName, convertFileNameToLower, fallbackName);
            }

            var relativePath = Path.ChangeExtension(overrideFileName.Trim().Replace("\\", "/"), null);
            relativePath = LayerNameUtility.SanitizeRelativePath(relativePath);
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
        internal string ExportImageAsset(bool forceSpriteType = false, string overrideExportDir = null, string overrideFileName = null, bool convertFileNameToLower = true, bool auto9Slice = false, bool ignoreReference = false)
        {
            var converter = this.Converter;
            if (converter != null && converter.TryGetSharedSpritePath(this, out var sharedPath))
            {
                return sharedPath;
            }
            bool useSharedOverride = false;
            string sharedOverrideKey = null;
            string exportDir = overrideExportDir;
            string fileName = overrideFileName;
            if (converter != null && string.IsNullOrEmpty(exportDir) && !HasReuseReference)
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
            if (BindPsdLayer != null)
            {
                var rendered = BindPsdLayer.Render();
                if (rendered == null || rendered.IsEmpty)
                {
                    return null;
                }

                var exportTexture = CreateTextureFromRenderedImage(rendered);
                if (exportTexture == null)
                {
                    return null;
                }

                var bytes = exportTexture.EncodeToPNG();
                DestroyImmediate(exportTexture);
                exportDir = string.IsNullOrWhiteSpace(exportDir) ? this.Converter.GetUIFormImagesOutputDir() : exportDir;
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
                    ? ResolveDuplicateExportName(converter, GetLayerExportBaseName(this, convertFileNameToLower), convertFileNameToLower)
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
                var imgFileName = Path.Combine(exportDir, imgName + ".png").Replace("\\", "/");
                File.WriteAllBytes(imgFileName, bytes);
#if EFUN_PRIVATE
                if (Psd2UIFormSettings.Instance.CompressImage)
                {
                    bool compressResult = Psd2UIFormConverter.CompressImageFile(imgFileName);
                    if (compressResult)
                    {
                        Debug.Log($"Compress image success:{imgFileName}");
                    }
                    else
                    {
                        Debug.LogWarning($"Compress image failed:{imgFileName}");
                    }
                }
#endif
                assetName = imgFileName;
                bool isImage = !(this.UIType == GUIType.FillColor || this.UIType == GUIType.RawImage);
                AssetDatabase.Refresh();
                assetName = Psd2UIFormConverter.NormalizeToAssetPath(assetName);
                Psd2UIFormConverter.ConvertTexturesType(new string[] { assetName }, isImage || forceSpriteType);
                if (auto9Slice)
                {
                    Psd2UIFormConverter.ApplySpriteNineSlice(assetName);
                }
                if (useSharedOverride && converter != null)
                {
                    converter.RegisterSharedSprite(string.IsNullOrEmpty(sharedOverrideKey) ? LayerNameLookupKey : sharedOverrideKey, assetName);
                }
            }

            return assetName;
        }
        internal bool RefreshLayerTexture(bool forceRefresh = false)
        {
            string currentCacheKey = ResolvePreviewCacheKey();
            bool hasPreviewTexture = PreviewTexture != null;
            bool cacheKeyMatches = hasPreviewTexture
                && !string.IsNullOrEmpty(previewCacheKey)
                && string.Equals(previewCacheKey, currentCacheKey, StringComparison.Ordinal);

            if (!forceRefresh && cacheKeyMatches)
            {
                return true;
            }

            if (BindPsdLayer == null)
            {
                ReleasePreviewTexture();
                return false;
            }

            if (hasPreviewTexture && (!cacheKeyMatches || forceRefresh))
            {
                ReleasePreviewTexture();
            }

            PreviewTexture = PsdLayerPreviewCache.Acquire(currentCacheKey, ConvertPsdLayer2Texture2D);
            if (PreviewTexture != null)
            {
                previewCacheKey = currentCacheKey;
            }
            return PreviewTexture != null;
        }

        private string ResolvePreviewCacheKey()
        {
            if (BindPsdLayer == null)
            {
                return string.Empty;
            }

            try
            {
                return BuildPreviewCacheKey();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildPreviewCacheKey()
        {
            string assetPath = NormalizePreviewCachePart(Psd2UIFormConverter.Instance?.PsdAssetName);
            string assetChangeTag = NormalizePreviewCachePart(Psd2UIFormConverter.Instance?.psdAssetChangeTime);
            if (string.IsNullOrWhiteSpace(assetChangeTag) && !string.IsNullOrWhiteSpace(assetPath))
            {
                assetChangeTag = ResolveAssetChangeTag(assetPath);
            }

            return string.Join(
                "|",
                "PsdLayerPreview",
                assetPath,
                assetChangeTag,
                BindPsdLayerIndex.ToString(CultureInfo.InvariantCulture),
                NormalizePreviewCachePart(SourceLayerName),
                BindPsdLayer.Left.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Top.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Width.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.Height.ToString(CultureInfo.InvariantCulture),
                BindPsdLayer.IsGroup ? "1" : "0",
                BindPsdLayer.IsVisible ? "1" : "0");
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
        internal Texture2D ConvertPsdLayer2Texture2D()
        {
            if (BindPsdLayer == null) return null;

            var rendered = BindPsdLayer.RenderPreview();
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            return CreateTextureFromRenderedImage(rendered);
        }

        private static Texture2D CreateTextureFromRenderedImage(PsdRenderedImage rendered)
        {
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            var texture = new Texture2D(rendered.Width, rendered.Height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.alphaIsTransparency = true;
            var colors = new Color32[rendered.Width * rendered.Height];
            for (int i = 0, p = 0; i < colors.Length; i++, p += 4)
            {
                colors[i] = new Color32(
                    rendered.Rgba32[p],
                    rendered.Rgba32[p + 1],
                    rendered.Rgba32[p + 2],
                    rendered.Rgba32[p + 3]);
            }

            texture.SetPixels32(colors);
            texture.Apply();
            return texture;
        }

        internal bool TryGetLayerColor(out Color color)
        {
            color = default;
            if (BindPsdLayer == null)
            {
                return false;
            }

            if (BindPsdLayer.TryGetStructuralLayerColor(out color))
            {
                return true;
            }

            var rendered = BindPsdLayer.RenderPreview();
            if (!TryGetDominantRenderedColor(rendered, out color))
            {
                return false;
            }

            return true;
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

        /// <summary>
        /// Find the first child layer node that matches the requested UI type.
        /// </summary>
        /// <param name="uiTp"></param>
        /// <returns></returns>
        internal PsdLayerNode FindSubLayerNode(GUIType uiTp)
        {
            if (uiTp == GUIType.Null) return null;
            return FindSubLayerNodeRecursive(transform, uiTp);
        }
        /// <summary>
        /// Find the first child layer node that matches any requested UI type.
        /// </summary>
        /// <param name="uiTps"></param>
        /// <returns></returns>
        internal PsdLayerNode FindSubLayerNode(params GUIType[] uiTps)
        {
            foreach (var tp in uiTps)
            {
                var result = FindSubLayerNode(tp);
                if (result != null) return result;
            }
            return null;
        }
        internal bool TryGetReuseTargetAsset(out UnityEngine.Object targetObj)
        {
            targetObj = null;
            if (!HasReuseReference) return false;
            string targetPath = Path.Combine(UGUIParser.Instance.SharedAssetsOutput, $"{ReuseTargetKey}.png").Replace("\\", "/");
            targetObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
            return targetObj != null;
        }
        internal bool TryGetReusePrefabAsset(out GameObject targetPrefab)
        {
            targetPrefab = null;
            if (!HasReusePrefabReference) return false;
            var sharedDir = UGUIParser.Instance?.SharedPrefabOutput;
            if (string.IsNullOrWhiteSpace(sharedDir)) return false;

            sharedDir = sharedDir.Replace("\\", "/");
            var targetPath = Path.Combine(sharedDir, $"{ReusePrefabKey}.prefab").Replace("\\", "/");
            targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (targetPrefab != null) return true;

            var searchKey = LayerNameUtility.GetRelativePathLeafName(ReusePrefabKey);
            if (string.IsNullOrWhiteSpace(searchKey)) return false;

            var guids = AssetDatabase.FindAssets($"{searchKey} t:prefab", new string[] { sharedDir });
            if (guids == null || guids.Length == 0) return false;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path)) continue;
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(fileName, searchKey, StringComparison.OrdinalIgnoreCase)) continue;

                targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (targetPrefab != null) return true;
            }

            // fallback: try first match
            var firstPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (!string.IsNullOrWhiteSpace(firstPath))
            {
                targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(firstPath);
            }
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
                return layers.FirstOrDefault(layer => layer.UIType == uiTp || layer.RoleUIType == uiTp);
            }
            return null;
        }

        private PsdLayerNode FindSubLayerNodeRecursive(Transform current, GUIType uiTp)
        {
            if (current == null) return null;
            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                var childNode = child.GetComponent<PsdLayerNode>();
                if (childNode != null)
                {
                    if (childNode.UIType == uiTp || childNode.RoleUIType == uiTp)
                    {
                        return childNode;
                    }
                    if (childNode != this && childNode.IsMainUIType && childNode.UIType != GUIType.Null)
                    {
                        continue;
                    }
                }
                var result = FindSubLayerNodeRecursive(child, uiTp);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Parse text-layer metadata from the bound PSD layer.
        /// </summary>
        /// <param name="layer"></param>
        /// <returns></returns>
        internal bool IsTextLayer(out PsdTextLayerInfo layer)
        {
            layer = null;
            if (BindPsdLayer == null) return false;

            return BindPsdLayer.TryGetTextLayerInfo(out layer);
        }
        internal void InitPsdLayers(PsdDocument psdInstance)
        {
            if (psdInstance == null)
            {
                BindPsdLayer = null;
                return;
            }
            if (BindPsdLayerIndex >= 0)
            {
                BindPsdLayer = psdInstance.GetLayerByFlatIndex(BindPsdLayerIndex);
            }
        }
        internal bool ParseTextLayerInfo(out TextLayerInfo textInfo)
        {
            textInfo = default;
            if (IsTextLayer(out var txtLayer))
            {
                textInfo = new TextLayerInfo()
                {
                    Text = null,
                    FontSize = 0,
                    IsAutoLineSpacing = true,
                    CharacterSpacing = 0f,
                    LineSpacing = 0f,
                    Color = Color.white,
                    FontStyle = FontStyle.Normal,
                    TMPFontStyle = TMPro.FontStyles.Normal,
                    FontName = null,
                    HasOutline = false,
                    OutlineColor = Color.clear,
                    OutlineSize = 0f,
                    TMPOutlineSize = 0f,
                    TMPOutlinePosition = TextLayerInfo.TMPOutlineMode.Center,
                    HasShadow = false,
                    ShadowIsInner = false,
                    ShadowColor = Color.clear,
                    ShadowOffset = Vector2.zero,
                    ShadowSpread = 0f,
                    ShadowSoftness = 0f,
                    HasGlow = false,
                    GlowIsInner = false,
                    GlowColor = Color.clear,
                    GlowSize = 0f,
                    GlowSpread = 0f,
                    GlowOffset = 0f,
                    GlowPower = 0f,
                    HasBevel = false,
                    BevelIsInner = false,
                    BevelSize = 0f,
                    BevelDepth = 0f,
                    BevelSoften = 0f,
                    BevelAngle = 0f,
                    BevelAltitude = 0f,
                    BevelHighlightColor = Color.white,
                    BevelHighlightOpacity = 1f,
                    BevelShadowColor = Color.black,
                    BevelShadowOpacity = 1f,
                    HasGradient = false,
                    GradientAngle = 0f,
                    GradientReverse = false,
                    GradientStyleKey = null,
                    GradientBlendModeKey = null,
                    GradientStops = null
                };
                textInfo.Text = txtLayer.Text;
                textInfo.FontSize = Mathf.Max(1, Mathf.FloorToInt(txtLayer.FontSize));
                textInfo.IsAutoLineSpacing = txtLayer.AutoLeading || txtLayer.Leading <= 0f;
                textInfo.Color = ConvertPsdColor(txtLayer.Color);
                if (txtLayer.FauxBold && txtLayer.FauxItalic)
                {
                    textInfo.FontStyle = UnityEngine.FontStyle.BoldAndItalic;
                }
                else if (txtLayer.FauxBold)
                {
                    textInfo.FontStyle = UnityEngine.FontStyle.Bold;

                }
                else if (txtLayer.FauxItalic)
                {
                    textInfo.FontStyle = UnityEngine.FontStyle.Italic;
                }
                else
                {
                    textInfo.FontStyle = UnityEngine.FontStyle.Normal;
                }

                if (txtLayer.FauxItalic)
                {
                    textInfo.TMPFontStyle |= TMPro.FontStyles.Italic;
                }
                if (txtLayer.FauxBold)
                {
                    textInfo.TMPFontStyle |= TMPro.FontStyles.Bold;
                }
                if (txtLayer.Underline)
                {
                    textInfo.TMPFontStyle |= TMPro.FontStyles.Underline;
                }
                if (txtLayer.Strikethrough)
                {
                    textInfo.TMPFontStyle |= TMPro.FontStyles.Strikethrough;
                }
                textInfo.FontName = txtLayer.FontName;
                textInfo.CharacterSpacing = txtLayer.Tracking * 0.1f;
                // Keep PSD Leading as-is; each text component uses different spacing units.
                textInfo.LineSpacing = txtLayer.Leading;
                ParseTextLayerEffects(txtLayer, ref textInfo);
                return true;
            }
            return false;
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
            }
            else if (txtLayer.InnerShadow != null && txtLayer.InnerShadow.Enabled)
            {
                textInfo.HasShadow = true;
                textInfo.ShadowIsInner = true;
                textInfo.ShadowColor = ConvertPsdColor(txtLayer.InnerShadow.Color);
                textInfo.ShadowOffset = Quaternion.Euler(0, 0, txtLayer.InnerShadow.Angle) * (Vector2.left * txtLayer.InnerShadow.Distance);
                textInfo.ShadowSpread = Mathf.Clamp01(txtLayer.InnerShadow.Spread);
                textInfo.ShadowSoftness = Mathf.Max(0f, txtLayer.InnerShadow.Blur);
            }

            if (txtLayer.Stroke != null && txtLayer.Stroke.Enabled)
            {
                textInfo.HasOutline = true;
                textInfo.OutlineSize = CalculateUnityOutlineSize(txtLayer.Stroke);
                textInfo.TMPOutlineSize = Mathf.Max(0f, txtLayer.Stroke.Size);
                textInfo.TMPOutlinePosition = ConvertTMPOutlineMode(txtLayer.Stroke.Position);
                textInfo.OutlineColor = ConvertPsdColor(txtLayer.Stroke.Color);
            }

            var glowInfo = (txtLayer.OuterGlow != null && txtLayer.OuterGlow.Enabled) ? txtLayer.OuterGlow :
                (txtLayer.InnerGlow != null && txtLayer.InnerGlow.Enabled ? txtLayer.InnerGlow : null);
            if (glowInfo != null)
            {
                textInfo.HasGlow = true;
                textInfo.GlowIsInner = glowInfo.Inner;
                textInfo.GlowColor = ConvertPsdColor(glowInfo.Color);
                textInfo.GlowSize = Mathf.Max(0f, glowInfo.Size);
                textInfo.GlowSpread = Mathf.Clamp01(glowInfo.Spread);
                textInfo.GlowOffset = 0f;
                textInfo.GlowPower = 0.75f;
            }

            if (txtLayer.Bevel != null && txtLayer.Bevel.Enabled)
            {
                textInfo.HasBevel = true;
                textInfo.BevelIsInner = txtLayer.Bevel.Inner;
                textInfo.BevelSize = Mathf.Max(0f, txtLayer.Bevel.Size);
                textInfo.BevelDepth = Mathf.Max(0f, txtLayer.Bevel.Depth);
                textInfo.BevelSoften = Mathf.Max(0f, txtLayer.Bevel.Soften);
                textInfo.BevelAngle = txtLayer.Bevel.Angle;
                textInfo.BevelAltitude = Mathf.Clamp(txtLayer.Bevel.Altitude, 0f, 90f);
                textInfo.BevelHighlightColor = ConvertPsdColor(txtLayer.Bevel.HighlightColor);
                textInfo.BevelHighlightOpacity = Mathf.Clamp01(txtLayer.Bevel.HighlightOpacity);
                textInfo.BevelShadowColor = ConvertPsdColor(txtLayer.Bevel.ShadowColor);
                textInfo.BevelShadowOpacity = Mathf.Clamp01(txtLayer.Bevel.ShadowOpacity);
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
                textInfo.GradientStyleKey = txtLayer.Gradient.StyleKey;
                textInfo.GradientBlendModeKey = txtLayer.Gradient.BlendModeKey;
                textInfo.GradientStops = convertedStops;
            }
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
