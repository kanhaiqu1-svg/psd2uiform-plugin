#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// Fatcat定制: 一条复用建议 —— 把 Node 标记成复用 Source。
    /// </summary>
    internal sealed class ReuseSuggestion
    {
        internal PsdLayerNode Node;
        internal PsdLayerNode Source;

        /// <summary>写回 SourceLayerName 的原文，可能带标签后缀（如 ref xxx.bt）。</summary>
        internal string NewRawName;

        /// <summary>写回 GameObject 名字的安全名（与插件解析时一致）。</summary>
        internal string NewObjectName;

        internal int Width;
        internal int Height;
        internal bool Selected = true;
        internal string Warning;
    }

    /// <summary>
    /// Fatcat定制: 像素完全一致的一组图层。
    /// </summary>
    internal sealed class ReuseGroup
    {
        internal int Width;
        internal int Height;
        internal readonly List<PsdLayerNode> Members = new List<PsdLayerNode>();
    }

    /// <summary>
    /// Fatcat定制: 重复素材（复用）检测。
    ///
    /// 只做一件事：找出「渲染后像素完全一致」的图层，给出 ref 复用建议。
    /// 不涉及 AI —— 像素相同是确定性事实，哈希比对比视觉模型快且不会有假阳性。
    ///
    /// 判定范围：已勾选导出、非 ref/refp、非 AI 生成节点、有 PSD 绑定且可见的图层。
    /// 预筛：先按 PSD 图层尺寸分桶（像素一致必然尺寸相同），只渲染同尺寸的桶。
    /// 已知局限：像素不一致但视觉相同的（改了色/描边/压过）不会被识别，那是后续 AI 兜底的事。
    /// </summary>
    internal static class ReuseTagFinder
    {
        internal static List<ReuseSuggestion> Find(Psd2UIFormConverter converter, out string error)
        {
            error = null;
            var result = new List<ReuseSuggestion>();
            if (converter == null)
            {
                error = "转换器不可用。";
                return result;
            }

            var existingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<PsdLayerNode>();
            foreach (var node in converter.GetComponentsInChildren<PsdLayerNode>(true))
            {
                if (node == null) continue;

                if (node.HasReuseReference || node.HasReusePrefabReference)
                {
                    // 已经是复用的图层不能再被复用，否则会把别的 ref 链断掉；但它的目标要记下来优先当源。
                    if (node.HasReuseReference && !string.IsNullOrEmpty(node.ReuseTargetKey))
                    {
                        existingTargets.Add(node.ReuseTargetKey);
                    }
                    continue;
                }
                if (node.IsAiTarget) continue;
                if (node.BindPsdLayer == null) continue;
                if (!node.NeedExportImage()) continue;
                candidates.Add(node);
            }

            // 按 PSD 图层尺寸预分桶，避免把所有图层都渲染一遍。
            var buckets = new Dictionary<long, List<PsdLayerNode>>();
            foreach (var node in candidates)
            {
                var layer = node.BindPsdLayer;
                int w = layer.Right - layer.Left;
                int h = layer.Bottom - layer.Top;
                if (w <= 0 || h <= 0) continue;
                long key = ((long)w << 32) | (uint)h;
                if (!buckets.TryGetValue(key, out var list))
                {
                    buckets.Add(key, list = new List<PsdLayerNode>());
                }
                list.Add(node);
            }

            var toCompare = new List<PsdLayerNode>();
            foreach (var bucket in buckets)
            {
                if (bucket.Value.Count > 1) toCompare.AddRange(bucket.Value);
            }
            if (toCompare.Count == 0)
            {
                error = "没有尺寸相同的图层，无需比对。";
                return result;
            }

            var groups = new Dictionary<string, ReuseGroup>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < toCompare.Count; i++)
                {
                    var node = toCompare[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "查找重复素材",
                            $"正在比对 {node.name}（{i + 1}/{toCompare.Count}）",
                            (i + 1) / (float)toCompare.Count))
                    {
                        error = "已取消。";
                        return result;
                    }

                    Texture2D texture = null;
                    int texW = 0, texH = 0;
                    string hash;
                    bool hasVisiblePixels;
                    try
                    {
                        texture = node.CreateFullResolutionTexture();
                        if (texture == null) continue;
                        texW = texture.width;
                        texH = texture.height;
                        if (texW <= 0 || texH <= 0) continue;
                        hash = HashRawBytes(texture, out hasVisiblePixels);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Psd2UIForm] 渲染图层失败，跳过 {node.name}: {ex.Message}");
                        continue;
                    }
                    finally
                    {
                        if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    }

                    // 渲染失败的图层会得到一张全透明的图；两张空白图的哈希一样，
                    // 不排除掉就会把「都渲染失败」误判成「像素一致」。
                    if (!hasVisiblePixels)
                    {
                        Debug.LogWarning($"[Psd2UIForm] 图层渲染结果为全透明，跳过重复判定: {node.name}");
                        continue;
                    }

                    string key = texW + "x" + texH + ":" + hash;
                    if (!groups.TryGetValue(key, out var group))
                    {
                        groups.Add(key, group = new ReuseGroup { Width = texW, Height = texH });
                    }
                    group.Members.Add(node);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (var group in groups.Values)
            {
                if (group.Members.Count < 2) continue;

                // 源图层：优先挑「已经被别的 ref 指着」的那个，避免把现有引用链改向；
                // 否则取绘制顺序最靠下的，保证重复运行结果稳定。
                var source = group.Members.FirstOrDefault(m => existingTargets.Contains(m.ResourceName))
                             ?? group.Members
                                 .OrderBy(m => m.RenderOrderSourceIndex)
                                 .ThenBy(m => m.name, StringComparer.Ordinal)
                                 .First();

                foreach (var member in group.Members)
                {
                    if (member == source) continue;
                    // 它已经是别人的复用目标，改它会把那些 ref 打断。
                    if (existingTargets.Contains(member.ResourceName)) continue;

                    string tags = GetTagSuffix(member);
                    string rawName = "ref " + source.ResourceName + tags;
                    var parser = UGUIParser.Instance;
                    string objectName = parser != null
                        ? parser.GetSafeLayerName(rawName, member.BindPsdLayerIndex)
                        : rawName;

                    string warning = null;
                    if (member.LayerType == PsdLayerType.LayerGroup)
                    {
                        warning = "该图层是组，复用后按整图生成，子层级不再产生";
                    }
                    else if (!string.IsNullOrEmpty(tags)
                        && (tags.IndexOf("sliced", StringComparison.OrdinalIgnoreCase) >= 0
                            || tags.IndexOf("tiled", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        warning = "该图层带九宫格标签，复用后边框取自共享图，需确认";
                    }

                    result.Add(new ReuseSuggestion
                    {
                        Node = member,
                        Source = source,
                        NewRawName = rawName,
                        NewObjectName = objectName,
                        Width = group.Width,
                        Height = group.Height,
                        Warning = warning
                    });
                }
            }

            result.Sort((a, b) =>
            {
                int order = a.Node.RenderOrderSourceIndex.CompareTo(b.Node.RenderOrderSourceIndex);
                return order != 0 ? order : string.CompareOrdinal(a.Node.name, b.Node.name);
            });

            if (result.Count == 0) error = "没有发现像素完全一致的图层。";
            return result;
        }

        /// <summary>
        /// 取图层名尾部的标签后缀（如 img_x.bt -> .bt）。源名里没有标签时返回空串。
        /// 标签必须保留：ref 节点仍然靠标签决定 UIType。
        /// </summary>
        private static string GetTagSuffix(PsdLayerNode node)
        {
            string raw = node.SourceLayerName;
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var parser = UGUIParser.Instance;
            if (parser == null) return string.Empty;

            string baseName = parser.RemoveLayerTypeFlags(raw);
            if (!string.IsNullOrEmpty(baseName)
                && raw.Length > baseName.Length
                && raw.StartsWith(baseName, StringComparison.Ordinal))
            {
                return raw.Substring(baseName.Length);
            }
            return string.Empty;
        }

        private static string HashRawBytes(Texture2D texture, out bool hasVisiblePixels)
        {
            var raw = texture.GetRawTextureData<byte>();
            // 只有两种格式：RGBA32 与 R16G16B16A16_UNorm。alpha 都落在每像素的最后一个字节。
            int stride = texture.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB ? 4 : 8;
            int alphaMask = stride - 1;
            bool visible = false;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < raw.Length; i++)
            {
                byte value = raw[i];
                if ((i & alphaMask) == alphaMask && value != 0) visible = true;
                hash ^= value;
                hash *= 1099511628211UL;
            }
            hasVisiblePixels = visible;
            return hash.ToString("x16");
        }
    }

    /// <summary>
    /// Fatcat定制: 重命名后让按名字建的资源查找表重建，否则 ref 目标查不到。
    /// </summary>
    public sealed partial class Psd2UIFormConverter
    {
        internal void InvalidateResourceCatalog()
        {
            resourceCatalogReady = false;
            layerLookupByName.Clear();
        }
    }
}
#endif
