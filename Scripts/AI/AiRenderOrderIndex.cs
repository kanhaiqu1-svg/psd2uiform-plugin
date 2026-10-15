#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    // Original PSD leaf order and deterministic expansion of explicitly referenced groups.
    internal sealed class AiRenderOrderIndex
    {
        internal readonly Dictionary<string, AiAnalysisNodeEntry> Nodes = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<AiAnalysisNodeEntry> Leaves = new List<AiAnalysisNodeEntry>();
        private readonly Dictionary<string, List<AiAnalysisNodeEntry>> children = new Dictionary<string, List<AiAnalysisNodeEntry>>(StringComparer.OrdinalIgnoreCase);

        internal AiRenderOrderIndex(List<AiAnalysisNodeEntry> nodes)
        {
            foreach (var node in nodes)
            {
                Nodes.Add(node.id, node);
                string parent = node.parentId ?? string.Empty;
                if (!children.TryGetValue(parent, out var list)) children.Add(parent, list = new List<AiAnalysisNodeEntry>());
                list.Add(node);
            }
            foreach (var list in children.Values) list.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
            var roots = new List<AiAnalysisNodeEntry>();
            foreach (var node in nodes)
                if (string.IsNullOrEmpty(node.parentId) || !Nodes.ContainsKey(node.parentId)) roots.Add(node);
            roots.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));
            foreach (var node in roots) Visit(node);
        }

        private void Visit(AiAnalysisNodeEntry node)
        {
            node.drawIndex = -1;
            node.leafStart = Leaves.Count;
            if (children.TryGetValue(node.id, out var list) && list.Count > 0)
            {
                foreach (var child in list) Visit(child);
            }
            else if (!node.isGroupLayer || node.renderLeafCount > 0)
            {
                node.drawIndex = Leaves.Count;
                Leaves.Add(node);
            }
            node.leafEnd = Leaves.Count - 1;
            if (node.leafEnd < node.leafStart) node.leafStart = node.leafEnd = -1;
        }

        internal string[] Expand(string[] ids)
        {
            var indices = new SortedSet<int>();
            if (ids != null) foreach (string id in ids)
                if (id != null && Nodes.TryGetValue(id, out var node) && node.leafStart >= 0)
                    for (int i = node.leafStart; i <= node.leafEnd; i++) indices.Add(i);
            var result = new string[indices.Count];
            int n = 0;
            foreach (int i in indices) result[n++] = Leaves[i].id;
            return result;
        }

        // Geometry/order constraints only: no names, pixel interpretation or UI classification.
        internal List<AiMergeConflict> FindMergeConflicts(List<AiUiPlan.Node> targets)
        {
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
                if (!target.rejected && target.type == GUIType.Null)
                    foreach (string id in target.leaves) ignored.Add(id);
            var conflicts = new List<AiMergeConflict>();
            foreach (var target in targets)
            {
                if (target.rejected || !target.ownerValid || target.leaves.Length < 2
                    || target.type == GUIType.Null || target.type == GUIType.Mask || target.type == GUIType.ScrollView_Viewport) continue;
                var selected = new HashSet<string>(target.leaves, StringComparer.Ordinal);
                int first = Nodes[target.leaves[0]].drawIndex, last = Nodes[target.leaves[target.leaves.Length - 1]].drawIndex;
                for (int i = first + 1; i < last; i++)
                {
                    var between = Leaves[i];
                    if (!between.active || selected.Contains(between.id) || ignored.Contains(between.id)) continue;
                    string above = null;
                    foreach (string id in target.leaves)
                    {
                        var source = Nodes[id];
                        if (source.drawIndex <= i || !source.active || !Overlaps(source.rect, between.rect)) continue;
                        above = source.shortId;
                        break;
                    }
                    if (above == null) continue;
                    conflicts.Add(new AiMergeConflict { targetId = target.source.id,
                        below = Nodes[target.leaves[0]].shortId, between = between.shortId, above = above });
                    break;
                }
            }
            return conflicts;
        }

        private static bool Overlaps(RectData a, RectData b)
            => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

    }

    [Serializable]
    internal sealed class AiMergeConflict
    {
        public string targetId;
        public string below;
        public string between;
        public string above;
        internal string Description => below + " < " + between + " < " + above
            + "；上层素材与中间素材的矩形范围相交，合并到首层可能改变遮挡顺序。";
    }
}
#endif
