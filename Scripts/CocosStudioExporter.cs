using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using cn.efunstudio.psdreader.PsdParser;

namespace UGF.EditorTools.Psd2UGUI
{
    public static class CocosStudioExporter
    {
        private static int _actionTagCounter = 10000;

        public static void ExportToCsd(Psd2UIFormConverter converter, string outputDir)
        {
            try
            {
                var formNameField = typeof(Psd2UIFormConverter).GetField("uiFormName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                string uiFormName = formNameField?.GetValue(converter) as string;
                if (string.IsNullOrEmpty(uiFormName)) uiFormName = converter.gameObject.name;

                int csdCanvasW = 1136;
                int csdCanvasH = 640;
                int psdCanvasW = 1386;
                int psdCanvasH = 852;
                float offsetX = (psdCanvasW - csdCanvasW) / 2f;
                float offsetY = (psdCanvasH - csdCanvasH) / 2f;

                string slotFolderName = new DirectoryInfo(outputDir).Name;
                string csdDir = Path.Combine(outputDir, "csd");
                if (!Directory.Exists(csdDir)) Directory.CreateDirectory(csdDir);
                string imagesBaseDir = Path.Combine(outputDir, "images");
                if (!Directory.Exists(imagesBaseDir)) Directory.CreateDirectory(imagesBaseDir);

                var childNodes = GetChildLayerNodes(converter.transform);
                var xmlChildren = new List<XElement>();

                foreach (var n in childNodes)
                {
                    var xmlNode = ParseLayerNode(n, imagesBaseDir, slotFolderName, offsetX, offsetY, psdCanvasW, psdCanvasH);
                    if (xmlNode != null) xmlChildren.Add(xmlNode);
                }

                int tagRoot = _actionTagCounter++;
                int tagAll = _actionTagCounter++;

                var panelAll = CreateBasePanel("Panel_All", tagAll, xmlChildren, -568, -320);
                var panelRoot = CreateBasePanel("Panel_Root", tagRoot, new List<XElement> { panelAll }, 568, 320);
                panelRoot.Add(new XElement("PrePosition", new XAttribute("X", "0.5000"), new XAttribute("Y", "0.5000")));

                var rootXml = new XElement("GameFile",
                    new XElement("PropertyGroup",
                        new XAttribute("Name", uiFormName),
                        new XAttribute("Type", "Layer"),
                        new XAttribute("ID", Guid.NewGuid().ToString()),
                        new XAttribute("Version", "3.10.0.0")
                    ),
                    new XElement("Content", new XAttribute("ctype", "GameProjectContent"),
                        new XElement("Content",
                            new XElement("Animation", new XAttribute("Duration", "0"), new XAttribute("Speed", "1.0000")),
                            new XElement("ObjectData",
                                new XAttribute("Name", "Layer"),
                                new XAttribute("Tag", "1"),
                                new XAttribute("ctype", "GameLayerObjectData"),
                                new XElement("Size", new XAttribute("X", csdCanvasW), new XAttribute("Y", csdCanvasH)),
                                new XElement("Children", panelRoot)
                            )
                        )
                    )
                );

                string savePath = Path.Combine(csdDir, uiFormName + ".csd");
                rootXml.Save(savePath);

                AssetDatabase.Refresh();
                Debug.Log($"[Cocos Studio] 导出成功！已处理镜像翻转与节点更名。路径: {savePath}");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Cocos Studio] 导出流程崩溃: {ex.Message}");
            }
        }

        private static XElement CreateBasePanel(string name, int tag, IEnumerable<XElement> children, float x, float y)
        {
            return new XElement("AbstractNodeData",
                new XAttribute("Name", name),
                new XAttribute("ActionTag", tag.ToString()),
                new XAttribute("Tag", tag.ToString()),
                new XAttribute("IconVisible", "False"),
                new XAttribute("TouchEnable", "False"),
                new XAttribute("ctype", "PanelObjectData"),
                new XAttribute("ComboBoxIndex", "0"),
                new XElement("Size", new XAttribute("X", "0.0000"), new XAttribute("Y", "0.0000")),
                new XElement("Children", children),
                new XElement("AnchorPoint"),
                new XElement("Position", new XAttribute("X", x.ToString("F4")), new XAttribute("Y", y.ToString("F4"))),
                new XElement("Scale", new XAttribute("ScaleX", "1.0000"), new XAttribute("ScaleY", "1.0000")),
                new XElement("CColor", new XAttribute("A", "255"), new XAttribute("R", "255"), new XAttribute("G", "255"), new XAttribute("B", "255"))
            );
        }

        private static PsdLayerNode[] GetChildLayerNodes(Transform parent)
        {
            if (parent == null) return new PsdLayerNode[0];
            return parent.Cast<Transform>()
                         .Select(t => t.GetComponent<PsdLayerNode>())
                         .Where(n => n != null && n.BindPsdLayer != null)
                         .ToArray();
        }

        private static XElement ParseLayerNode(PsdLayerNode node, string imagesBaseDir, string slotFolderName, float parentBlX, float parentBlY, int canvasW, int canvasH)
        {
            try
            {
                var psdLayer = node.BindPsdLayer;
                if (psdLayer == null || !psdLayer.IsVisible) return null;

                string layerName = string.IsNullOrEmpty(node.name) ? "UnknownLayer" : node.name;
                float width = psdLayer.Right - psdLayer.Left;
                float height = psdLayer.Bottom - psdLayer.Top;

                float centerX = psdLayer.Left + width / 2f;
                float centerY = canvasH - (psdLayer.Top + height / 2f);

                float localX = centerX - parentBlX;
                float localY = centerY - parentBlY;

                int tagId = _actionTagCounter++;

                // ==========================================
                // 1. 修正显示名称：如果是复用节点，删除 ref 前缀并加编号
                // ==========================================
                string displayName = layerName;
                if (node.HasReuseReference)
                {
                    displayName = $"{node.ReuseTargetDisplayName}_{tagId}";
                }

                // 读取 Unity Transform 的 localScale 正负符号（处理镜像翻转）
                float flipX = node.transform.localScale.x < 0 ? -1f : 1f;
                float flipY = node.transform.localScale.y < 0 ? -1f : 1f;

                var nodeData = new XElement("AbstractNodeData",
                    new XAttribute("Name", displayName),
                    new XAttribute("ActionTag", tagId.ToString()),
                    new XAttribute("Tag", tagId.ToString()),
                    new XAttribute("IconVisible", "False"),
                    new XAttribute("Alpha", "255"),
                    new XElement("Size", new XAttribute("X", width.ToString("F4")), new XAttribute("Y", height.ToString("F4"))),
                    new XElement("AnchorPoint", new XAttribute("ScaleX", "0.5000"), new XAttribute("ScaleY", "0.5000")),
                    new XElement("Position", new XAttribute("X", localX.ToString("F4")), new XAttribute("Y", localY.ToString("F4"))),
                    new XElement("Scale", new XAttribute("ScaleX", flipX.ToString("F4")), new XAttribute("ScaleY", flipY.ToString("F4"))),
                    new XElement("CColor", new XAttribute("A", "255"), new XAttribute("R", "255"), new XAttribute("G", "255"), new XAttribute("B", "255"))
                );

                if (psdLayer.IsGroup)
                {
                    nodeData.Add(new XAttribute("ctype", "PanelObjectData"));
                    nodeData.Add(new XAttribute("TouchEnable", "False"));
                    nodeData.Add(new XAttribute("ComboBoxIndex", "0"));

                    nodeData.Element("Position")?.SetAttributeValue("X", "0.0000");
                    nodeData.Element("Position")?.SetAttributeValue("Y", "0.0000");
                    nodeData.Element("Size")?.SetAttributeValue("X", "0.0000");
                    nodeData.Element("Size")?.SetAttributeValue("Y", "0.0000");

                    var children = GetChildLayerNodes(node.transform);
                    if (children.Length > 0)
                    {
                        var childrenXml = new XElement("Children");
                        foreach (var child in children)
                        {
                            var childXml = ParseLayerNode(child, imagesBaseDir, slotFolderName, parentBlX, parentBlY, canvasW, canvasH);
                            if (childXml != null) childrenXml.Add(childXml);
                        }
                        nodeData.Add(childrenXml);
                    }
                }
                else if (psdLayer.IsTextLayer())
                {
                    nodeData.Add(new XAttribute("ctype", "TextObjectData"));
                    string textStr = layerName;
                    int fontSize = 24;
                    try
                    {
                        if (node.ParseTextLayerInfo(out var textInfo))
                        {
                            textStr = !string.IsNullOrEmpty(textInfo.Text) ? textInfo.Text : layerName;
                            fontSize = (int)textInfo.FontSize;
                        }
                    }
                    catch { }
                    nodeData.Add(new XAttribute("LabelText", textStr), new XAttribute("FontSize", fontSize));
                }
                else if (node.NeedExportImage() || node.HasReuseReference)
                {
                    // 解析缩放倍率 RXX
                    float resizeRatio = 1.0f;
                    var rMatch = Regex.Match(layerName, @"R(\d+)$", RegexOptions.IgnoreCase);
                    if (rMatch.Success) resizeRatio = int.Parse(rMatch.Groups[1].Value) / 100f;

                    string resName = node.HasReuseReference ? node.ReuseTargetDisplayName : layerName;
                    string cleanResName = Regex.Replace(resName, @"R(\d+)$", "", RegexOptions.IgnoreCase);
                    cleanResName = Regex.Replace(cleanResName, @"_s9$", "", RegexOptions.IgnoreCase);
                    // Fatcat定制: 去掉 R 后缀后可能残留尾随下划线(img_xxx_R50 -> img_xxx_)
                    cleanResName = cleanResName.TrimEnd('_');
                    string imgFileName = cleanResName + ".png";
                    string subFolder = "";
                    string lowerName = cleanResName.ToLower();

                    if (lowerName.Contains("award")) subFolder = "award";
                    else if (lowerName.Contains("board")) subFolder = "board";
                    else if (lowerName.Contains("icon")) subFolder = "icon";

                    string targetDir = string.IsNullOrEmpty(subFolder) ? imagesBaseDir : Path.Combine(imagesBaseDir, subFolder);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    string relativeXmlPath = $"{slotFolderName}/images/{(string.IsNullOrEmpty(subFolder) ? "" : subFolder + "/")}{imgFileName}";

                    // ==========================================
                    // 2. 九宫格识别 + 修正镜像缩放逻辑：保留 Unity localScale 的负号
                    // ==========================================
                    // Fatcat定制: 与 Unity 统一取节点最终 Image.Type(.sliced/.tiled/AI 模板类型)。
                    // 原实现判断的是 "[sliced]" 方括号写法，而插件真实标签是 ".sliced"，永远匹配不上。
                    bool is9Slice = ResolveIsNineSlice(node);

                    float texW = width;
                    float texH = height;
                    Vector4 nineSliceBorder = Vector4.zero;
                    if (!node.HasReuseReference)
                    {
                        // Fatcat定制: PreviewTexture 是 50% 分辨率的预览缓存，不能作为导出源。
                        // 必须走与 Unity 图片导出相同的全分辨率渲染(RenderNodeImage(false))。
                        Texture2D exportTexture = null;
                        try
                        {
                            exportTexture = node.CreateFullResolutionTexture();
                            if (exportTexture != null)
                            {
                                texW = exportTexture.width;
                                texH = exportTexture.height;
                                // Fatcat定制: 边框必须在原始分辨率上算，之后按 R 缩放倍率同步缩放。
                                if (is9Slice) nineSliceBorder = UGUIParser.CalculateTexture9SliceBorder(exportTexture);
                                string fullExportPath = Path.Combine(targetDir, imgFileName).Replace("\\", "/");
                                if (Mathf.Approximately(resizeRatio, 1.0f))
                                {
                                    File.WriteAllBytes(fullExportPath, PsdTextureAssetUtility.EncodePng(exportTexture));
                                }
                                else
                                {
                                    int targetW = Mathf.Max(1, Mathf.RoundToInt(texW * resizeRatio));
                                    int targetH = Mathf.Max(1, Mathf.RoundToInt(texH * resizeRatio));
                                    var resized = ResizeTexture(exportTexture, targetW, targetH);
                                    File.WriteAllBytes(fullExportPath, PsdTextureAssetUtility.EncodePng(resized));
                                    UnityEngine.Object.DestroyImmediate(resized);
                                }
                            }
                        }
                        finally
                        {
                            if (exportTexture != null) UnityEngine.Object.DestroyImmediate(exportTexture);
                        }
                    }
                    else if (is9Slice
                             && TryResolveReferenceNineSliceBorder(node, out var refBorder, out int refW, out int refH))
                    {
                        // Fatcat定制: 复用节点的像素在目标图层上，边框和尺寸都要按目标算。
                        nineSliceBorder = refBorder;
                        texW = refW;
                        texH = refH;
                    }

                    float finalResourceW = texW * resizeRatio;
                    float finalResourceH = texH * resizeRatio;

                    var sizeNode = nodeData.Element("Size");
                    var scaleNode = nodeData.Element("Scale");

                    // 边框算不出来时不能当九宫格用(Cocos 会按边框 0 处理，拉伸即变形)。
                    bool applyNineSlice = is9Slice && nineSliceBorder != Vector4.zero;

                    if (applyNineSlice)
                    {
                        // Fatcat定制: 九宫格必须「Size=显示尺寸, Scale=1」。
                        // 用「Size=资源尺寸, Scale=显示/资源」会把四个角一起放大。
                        sizeNode?.SetAttributeValue("X", width.ToString("F4"));
                        sizeNode?.SetAttributeValue("Y", height.ToString("F4"));
                        scaleNode?.SetAttributeValue("ScaleX", flipX.ToString("F4"));
                        scaleNode?.SetAttributeValue("ScaleY", flipY.ToString("F4"));
                    }
                    else
                    {
                        sizeNode?.SetAttributeValue("X", finalResourceW.ToString("F4"));
                        sizeNode?.SetAttributeValue("Y", finalResourceH.ToString("F4"));

                        // 计算 Scale 数值并乘回 Unity 的正负号
                        float finalScaleX = (finalResourceW > 0) ? (width / finalResourceW) : 1f;
                        float finalScaleY = (finalResourceH > 0) ? (height / finalResourceH) : 1f;
                        scaleNode?.SetAttributeValue("ScaleX", (finalScaleX * flipX).ToString("F4"));
                        scaleNode?.SetAttributeValue("ScaleY", (finalScaleY * flipY).ToString("F4"));
                    }

                    string ctype = "SpriteObjectData";
                    string guiTypeStr = node.UIType.ToString();
                    if (guiTypeStr.Contains("Button")) ctype = "ButtonObjectData";
                    else if (guiTypeStr.Contains("LoadingBar") || guiTypeStr.Contains("ProgressBar") || guiTypeStr.Contains("Slider")) ctype = "LoadingBarObjectData";
                    else if (guiTypeStr.Contains("TextBMFont")) ctype = "TextBMFontObjectData";
                    else if (guiTypeStr.Contains("ImageView") || is9Slice) ctype = "ImageViewObjectData";

                    nodeData.Add(new XAttribute("ctype", ctype));

                    // Fatcat定制: Cocos 的九宫格是节点属性，边宽来自贴图。
                    // 字段集合与公式经生产工程(zq_class, 13309 个九宫格节点)全量校验:
                    //   Scale9Width = 图宽 - 左 - 右, Scale9Height = 图高 - 下 - 上
                    //   Scale9OriginX/Y = 左/上边宽
                    // 生产里 Scale9Enable="False" 出现 0 次 —— 非九宫格节点直接省略该属性。
                    if (applyNineSlice && (ctype == "ButtonObjectData" || ctype == "ImageViewObjectData"))
                    {
                        nodeData.Add(new XAttribute("Scale9Enable", "True"));
                        AppendScale9Attributes(nodeData, nineSliceBorder, resizeRatio, finalResourceW, finalResourceH);
                    }
                    else if (is9Slice && (ctype == "ButtonObjectData" || ctype == "ImageViewObjectData"))
                    {
                        Debug.LogWarning($"[Cocos Studio] 图层 '{node.name}' 标了九宫格但算不出有效边框，已按普通图导出。");
                    }

                    if (ctype == "ButtonObjectData")
                    {
                        nodeData.Add(new XAttribute("ButtonText", ""));
                        nodeData.Add(new XElement("NormalFileData", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                        nodeData.Add(new XElement("PressedFileData", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                        nodeData.Add(new XElement("DisabledFileData", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                    }
                    else if (ctype == "LoadingBarObjectData")
                    {
                        nodeData.Add(new XAttribute("ProgressInfo", "100"), new XAttribute("ProgressType", "0"));
                        nodeData.Add(new XElement("ImageFileData", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                    }
                    else if (ctype == "TextBMFontObjectData")
                    {
                        nodeData.Add(new XAttribute("LabelText", layerName));
                        nodeData.Add(new XElement("LabelBMFontFile_CNB", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                    }
                    else
                    {
                        // ImageView 不带 BlendFunc；Scale9Enable 已在上方统一处理。
                        if (ctype != "ImageViewObjectData")
                        {
                            nodeData.Add(new XElement("BlendFunc", new XAttribute("Src", "1"), new XAttribute("Dst", "771")));
                        }
                        nodeData.Add(new XElement("FileData", new XAttribute("Type", "Normal"), new XAttribute("Path", relativeXmlPath), new XAttribute("Plist", "")));
                    }
                }
                else nodeData.Add(new XAttribute("ctype", "SingleNodeObjectData"));

                return nodeData;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cocos Studio] 忽略问题图层: '{node?.name}'。原因: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fatcat定制: 与 Unity 共用同一套判定——取节点最终 Image.Type。
        /// 这样 .sliced/.tiled 标签、AI 模板类型、九宫格自动标记全都一致生效。
        /// </summary>
        private static bool ResolveIsNineSlice(PsdLayerNode node)
        {
            if (node == null) return false;
            var parser = UGUIParser.Instance;
            if (parser == null) return false;
            var imageType = parser.ResolveFinalImageType(node, Image.Type.Simple);
            return imageType == Image.Type.Sliced || imageType == Image.Type.Tiled;
        }

        /// <summary>
        /// Fatcat定制: 复用节点自己不写图，像素在目标图层上，所以按目标取尺寸与边框。
        /// 目标不在当前 PSD 时退回读已导入的 Sprite.border。
        /// </summary>
        private static bool TryResolveReferenceNineSliceBorder(PsdLayerNode node, out Vector4 border, out int texW, out int texH)
        {
            border = Vector4.zero;
            texW = 0;
            texH = 0;
            if (node == null) return false;
            try
            {
                if (node.TryGetReuseTarget(out var target) && target != null)
                {
                    var texture = target.CreateFullResolutionTexture();
                    if (texture != null)
                    {
                        try
                        {
                            texW = texture.width;
                            texH = texture.height;
                            border = UGUIParser.CalculateTexture9SliceBorder(texture);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(texture);
                        }
                        if (border != Vector4.zero) return true;
                    }
                }

                if (node.TryGetReuseTargetAsset(out var asset) && asset is Sprite sprite && sprite.texture != null)
                {
                    texW = sprite.texture.width;
                    texH = sprite.texture.height;
                    if (sprite.border != Vector4.zero)
                    {
                        border = sprite.border;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cocos Studio] 解析复用图层 '{node.name}' 的九宫格边框失败: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Fatcat定制: 写入 Cocos 九宫格属性。边框按 R 缩放倍率同步缩放并夹紧，
        /// 避免中心区算出负数。字段顺序对齐生产 .csd。
        /// </summary>
        private static void AppendScale9Attributes(XElement nodeData, Vector4 border, float resizeRatio, float resourceW, float resourceH)
        {
            int resW = Mathf.Max(1, Mathf.RoundToInt(resourceW));
            int resH = Mathf.Max(1, Mathf.RoundToInt(resourceH));

            int left = Mathf.Clamp(Mathf.RoundToInt(border.x * resizeRatio), 0, resW);
            int right = Mathf.Clamp(Mathf.RoundToInt(border.z * resizeRatio), 0, Mathf.Max(0, resW - left));
            int bottom = Mathf.Clamp(Mathf.RoundToInt(border.y * resizeRatio), 0, resH);
            int top = Mathf.Clamp(Mathf.RoundToInt(border.w * resizeRatio), 0, Mathf.Max(0, resH - bottom));

            nodeData.Add(
                new XAttribute("LeftEage", left),
                new XAttribute("RightEage", right),
                new XAttribute("TopEage", top),
                new XAttribute("BottomEage", bottom),
                new XAttribute("Scale9OriginX", left),
                new XAttribute("Scale9OriginY", top),
                new XAttribute("Scale9Width", resW - left - right),
                new XAttribute("Scale9Height", resH - bottom - top));
        }

        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }
    }
}