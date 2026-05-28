using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
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
                    string imgFileName = cleanResName + ".png";
                    string subFolder = "";
                    string lowerName = cleanResName.ToLower();

                    if (lowerName.Contains("award")) subFolder = "award";
                    else if (lowerName.Contains("board")) subFolder = "board";
                    else if (lowerName.Contains("icon")) subFolder = "icon";

                    string targetDir = string.IsNullOrEmpty(subFolder) ? imagesBaseDir : Path.Combine(imagesBaseDir, subFolder);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    string relativeXmlPath = $"{slotFolderName}/images/{(string.IsNullOrEmpty(subFolder) ? "" : subFolder + "/")}{imgFileName}";

                    if (!node.HasReuseReference)
                    {
                        if (node.PreviewTexture == null) try { node.RefreshLayerTexture(true); } catch { }
                        if (node.PreviewTexture != null)
                        {
                            string fullExportPath = Path.Combine(targetDir, imgFileName).Replace("\\", "/");
                            if (Mathf.Approximately(resizeRatio, 1.0f))
                                File.WriteAllBytes(fullExportPath, node.PreviewTexture.EncodeToPNG());
                            else
                            {
                                int targetW = Mathf.Max(1, Mathf.RoundToInt(node.PreviewTexture.width * resizeRatio));
                                int targetH = Mathf.Max(1, Mathf.RoundToInt(node.PreviewTexture.height * resizeRatio));
                                var resized = ResizeTexture(node.PreviewTexture, targetW, targetH);
                                File.WriteAllBytes(fullExportPath, resized.EncodeToPNG());
                                UnityEngine.Object.DestroyImmediate(resized);
                            }
                        }
                    }

                    // ==========================================
                    // 2. 修正镜像缩放逻辑：保留 Unity localScale 的负号
                    // ==========================================
                    float texW = (node.PreviewTexture != null) ? node.PreviewTexture.width : width;
                    float texH = (node.PreviewTexture != null) ? node.PreviewTexture.height : height;
                    float finalResourceW = texW * resizeRatio;
                    float finalResourceH = texH * resizeRatio;

                    var sizeNode = nodeData.Element("Size");
                    var scaleNode = nodeData.Element("Scale");
                    // 与 Unity 统一：通过 PS 图层名 [sliced] / [tiled] 标签识别九宫格
                    string sourceName = !string.IsNullOrWhiteSpace(node.SourceLayerName) ? node.SourceLayerName : layerName;
                    bool is9Slice = sourceName.Contains("[sliced]", StringComparison.OrdinalIgnoreCase)
                                 || sourceName.Contains("[tiled]", StringComparison.OrdinalIgnoreCase);

                    sizeNode?.SetAttributeValue("X", finalResourceW.ToString("F4"));
                    sizeNode?.SetAttributeValue("Y", finalResourceH.ToString("F4"));

                    // 计算 Scale 数值并乘回 Unity 的正负号
                    float finalScaleX = (finalResourceW > 0) ? (width / finalResourceW) : 1f;
                    float finalScaleY = (finalResourceH > 0) ? (height / finalResourceH) : 1f;
                    scaleNode?.SetAttributeValue("ScaleX", (finalScaleX * flipX).ToString("F4"));
                    scaleNode?.SetAttributeValue("ScaleY", (finalScaleY * flipY).ToString("F4"));

                    string ctype = "SpriteObjectData";
                    string guiTypeStr = node.UIType.ToString();
                    if (guiTypeStr.Contains("Button")) ctype = "ButtonObjectData";
                    else if (guiTypeStr.Contains("LoadingBar") || guiTypeStr.Contains("ProgressBar") || guiTypeStr.Contains("Slider")) ctype = "LoadingBarObjectData";
                    else if (guiTypeStr.Contains("TextBMFont")) ctype = "TextBMFontObjectData";
                    else if (guiTypeStr.Contains("ImageView") || is9Slice) ctype = "ImageViewObjectData";

                    nodeData.Add(new XAttribute("ctype", ctype));

                    if (ctype == "ButtonObjectData")
                    {
                        nodeData.Add(new XAttribute("ButtonText", ""), new XAttribute("Scale9Enable", is9Slice ? "True" : "False"));
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
                        if (ctype == "ImageViewObjectData") nodeData.Add(new XAttribute("Scale9Enable", is9Slice ? "True" : "False"));
                        else nodeData.Add(new XElement("BlendFunc", new XAttribute("Src", "1"), new XAttribute("Dst", "771")));
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