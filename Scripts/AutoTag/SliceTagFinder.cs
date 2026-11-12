#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// Fatcat定制: 一条九宫格建议。
    /// </summary>
    internal sealed class SliceSuggestion
    {
        internal PsdLayerNode Node;
        internal string NewRawName;
        internal int Width;
        internal int Height;

        /// <summary>边框 (left, bottom, right, top)，由插件自己的算法算出，仅用于展示/判断。</summary>
        internal Vector4 Border;

        internal int CenterWidth;
        internal int CenterHeight;
        internal bool Selected = true;
        internal string Note;
    }

    /// <summary>
    /// Fatcat定制: 九宫格自动判定（纯规则，不涉及 AI）。
    ///
    /// 只决定一件事：这个图层该不该打 .sliced 标签。边框值完全交给插件现有算法
    /// （生成时 ApplySpriteNineSlice 只在 spriteBorder==0 时计算，不会覆盖手调值）。
    ///
    /// 判定 = 算法找得到可拉伸带 + 边框"讲得通"：
    ///   1. 四边都 > 1（四边全 ≤1 基本是纯色块，打了也等于没打）
    ///   2. 中心区至少占 10% 且不小于 4px（否则拉伸时窄带被撑开，画面会毁）
    ///   3. 左右、上下大致对称（真实底板是对称的；不对称的多半是巧合的稳定带）
    ///
    /// 这些阈值来自 Slot_6130 实测：64 层里算法报 22 个"两轴可拉伸"，加这三条过滤后
    /// 剩 4 个干净底板（227~272x72，边框 22/20/20/26 高度一致）；award 70 层过滤后为 0，
    /// 与设计师手工 0 标签一致。
    /// </summary>
    internal static class SliceTagFinder
    {
        // 中心区占原图的最小比例，低于此值拉伸会明显变形。
        internal static float MinCenterRatio = 0.10f;
        internal static int MinCenterPixels = 4;
        // 对称性容忍：|左-右| 不超过较大者的这个比例。
        internal static float SymmetryTolerance = 0.5f;

        internal static List<SliceSuggestion> Find(Psd2UIFormConverter converter, out string error)
        {
            error = null;
            var result = new List<SliceSuggestion>();
            if (converter == null)
            {
                error = "转换器不可用。";
                return result;
            }

            var candidates = new List<PsdLayerNode>();
            foreach (var node in converter.GetComponentsInChildren<PsdLayerNode>(true))
            {
                if (node == null) continue;
                // 已复用的图层边框取自共享图，打 .sliced 会去改共享图标(见复用功能的同类坑)。
                if (node.HasReuseReference || node.HasReusePrefabReference) continue;
                if (node.IsAiTarget) continue;
                if (node.BindPsdLayer == null) continue;
                if (node.UIType == GUIType.RawImage) continue;   // .rimg 按约定不做九宫格
                if (!node.NeedExportImage()) continue;
                if (HasExplicitImageType(node)) continue;        // 已经有 sliced/tiled/simple/filled，尊重原选择
                var layer = node.BindPsdLayer;
                if (layer.Right - layer.Left < 8 || layer.Bottom - layer.Top < 8) continue;
                candidates.Add(node);
            }

            if (candidates.Count == 0)
            {
                error = "没有可判定的图层。";
                return result;
            }

            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var node = candidates[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "判定九宫格",
                            $"正在分析 {node.name}（{i + 1}/{candidates.Count}）",
                            (i + 1) / (float)candidates.Count))
                    {
                        error = "已取消。";
                        return result;
                    }

                    Texture2D texture = null;
                    int w = 0, h = 0;
                    Vector4 border;
                    try
                    {
                        texture = node.CreateFullResolutionTexture();
                        if (texture == null) continue;
                        w = texture.width;
                        h = texture.height;
                        if (w < 8 || h < 8) continue;
                        border = UGUIParser.CalculateTexture9SliceBorder(texture);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Psd2UIForm] 九宫格分析失败，跳过 {node.name}: {ex.Message}");
                        continue;
                    }
                    finally
                    {
                        if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    }

                    int left = Mathf.RoundToInt(border.x);
                    int bottom = Mathf.RoundToInt(border.y);
                    int right = Mathf.RoundToInt(border.z);
                    int top = Mathf.RoundToInt(border.w);

                    // 算法可能返回 -1 之类的异常值(带宽贴满整轴)，直接排除。
                    if (left < 0 || bottom < 0 || right < 0 || top < 0) continue;
                    // 四边全 ≤1 基本是纯色块：打了 .sliced 也等价 Simple，没有意义。
                    if (left <= 1 && bottom <= 1 && right <= 1 && top <= 1) continue;

                    int centerW = w - left - right;
                    int centerH = h - bottom - top;
                    if (centerW < Mathf.Max(MinCenterPixels, Mathf.FloorToInt(w * MinCenterRatio))) continue;
                    if (centerH < Mathf.Max(MinCenterPixels, Mathf.FloorToInt(h * MinCenterRatio))) continue;
                    if (Mathf.Abs(left - right) > SymmetryTolerance * Mathf.Max(left, right)) continue;
                    if (Mathf.Abs(bottom - top) > SymmetryTolerance * Mathf.Max(bottom, top)) continue;

                    result.Add(new SliceSuggestion
                    {
                        Node = node,
                        NewRawName = AppendSlicedTag(node.SourceLayerName),
                        Width = w,
                        Height = h,
                        Border = border,
                        CenterWidth = centerW,
                        CenterHeight = centerH
                    });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            result.Sort((a, b) =>
            {
                int order = a.Node.RenderOrderSourceIndex.CompareTo(b.Node.RenderOrderSourceIndex);
                return order != 0 ? order : string.CompareOrdinal(a.Node.name, b.Node.name);
            });

            if (result.Count == 0) error = "没有找到边框合理的九宫格候选。";
            return result;
        }

        private static bool HasExplicitImageType(PsdLayerNode node)
        {
            string raw = node.SourceLayerName;
            if (string.IsNullOrEmpty(raw)) return false;
            return Contains(raw, ".sliced") || Contains(raw, ".tiled")
                   || Contains(raw, ".simple") || Contains(raw, ".filled");
        }

        private static bool Contains(string value, string token)
            => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 追加 .sliced。标签家族解析不看顺序，所以直接追加即可；
        /// RemoveLayerTypeFlags 会剥掉标签，导出文件名不受影响。
        /// </summary>
        internal static string AppendSlicedTag(string rawName)
        {
            string name = string.IsNullOrEmpty(rawName) ? string.Empty : rawName.Trim();
            if (name.Length == 0) return name;
            if (Contains(name, ".sliced")) return name;
            return name + ".sliced";
        }
    }
}
#endif
