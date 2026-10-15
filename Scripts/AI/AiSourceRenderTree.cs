#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using cn.efunstudio.psdreader.PsdParser;

namespace UGF.EditorTools.Psd2UGUI
{
    // Selection changes output membership, never the original PSD compositing ancestry.
    internal static class AiSourceRenderTree
    {
        internal static PsdLayerRenderNode Build(PsdDocument document, int[] indices)
        {
            if (indices == null || indices.Length == 0) return null;
            var selected = new HashSet<PsdLayer>();
            foreach (int index in indices)
            {
                var layer = document.GetLayerByFlatIndex(index);
                if (layer != null) selected.Add(layer);
            }
            return Build(document, selected);
        }

        internal static PsdLayerRenderNode Build(PsdDocument document, HashSet<PsdLayer> selected)
            => new PsdLayerRenderNode(null, Select(document.Childs, selected));

        private static PsdLayerRenderNode[] Select(PsdLayer[] layers, HashSet<PsdLayer> selected)
        {
            var result = new List<PsdLayerRenderNode>();
            PsdLayer clippingBase = null;
            foreach (var layer in layers)
            {
                PsdLayerRenderNode node = null;
                if (selected.Contains(layer)) node = new PsdLayerRenderNode(layer);
                else if (layer.IsGroup)
                {
                    var children = Select(layer.Childs, selected);
                    if (children.Length > 0) node = new PsdLayerRenderNode(layer, children) { UseSelectedBounds = true };
                }
                if (node != null)
                {
                    if (layer.IsClipping) node.ClippingBase = clippingBase;
                    result.Add(node);
                }
                if (!layer.IsClipping) clippingBase = layer;
            }
            return result.ToArray();
        }

        internal static string[] FindCompositionIssues(PsdDocument document, int[] indices)
        {
            var selected = new HashSet<PsdLayer>();
            foreach (int index in indices) selected.Add(document.GetLayerByFlatIndex(index));
            var issues = new List<string>();
            Visit(document.Childs);
            return issues.ToArray();

            bool Visit(PsdLayer[] layers)
            {
                bool any = false;
                foreach (var layer in layers)
                {
                    bool contained = selected.Contains(layer);
                    if (layer.IsGroup)
                    {
                        contained |= Visit(layer.Childs);
                        if (contained && !layer.CanRegroupChildren() && !AllSelected(layer))
                            issues.Add("素材跨越需要整体合成的源组: " + layer.Name + "；保留结构，需复核该图像的混合效果。");
                    }
                    else if (contained && layer.RequiresBackdropComposition)
                        issues.Add("素材使用依赖底图的混合模式: " + layer.Name + "；独立 uGUI 图像需复核混合效果。");
                    any |= contained;
                }
                return any;
            }
            bool AllSelected(PsdLayer layer)
            {
                if (selected.Contains(layer)) return true;
                if (!layer.IsGroup) return false;
                foreach (var child in layer.Childs) if (!AllSelected(child)) return false;
                return true;
            }
        }
    }
}
#endif
