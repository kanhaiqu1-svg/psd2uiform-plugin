#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class PsdGeneratedDrawOrder
    {
        private struct Range
        {
            internal int first;
            internal int last;
        }

        internal static void Register(PsdLayerNode source, Component graphic)
        {
            var context = GenerationContext.Current;
            if (context != null && source != null && graphic != null) context.GraphicSources[graphic.transform] = source;
        }

        internal static int Reconcile(GenerationContext context, Transform sourceRoot, Transform generatedRoot)
        {
            var sources = sourceRoot.GetComponentsInChildren<PsdLayerNode>(true);
            var orderedSources = new List<PsdLayerNode>(sources.Length);
            var walkOrder = new Dictionary<PsdLayerNode, int>(sources.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source == null) continue;
                walkOrder[source] = orderedSources.Count;
                orderedSources.Add(source);
            }
            orderedSources.Sort((a, b) =>
            {
                int ka = a.RenderOrderSourceIndex;
                int kb = b.RenderOrderSourceIndex;
                return ka != kb ? ka.CompareTo(kb) : walkOrder[a].CompareTo(walkOrder[b]);
            });
            var ranks = new Dictionary<PsdLayerNode, int>(orderedSources.Count);
            for (int i = 0; i < orderedSources.Count; i++) ranks[orderedSources[i]] = i;
            var ranges = new Dictionary<Transform, Range>();
            foreach (var pair in context.GraphicSources)
            {
                if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy || !ranks.TryGetValue(pair.Value, out int rank)
                    || (pair.Key != generatedRoot && !pair.Key.IsChildOf(generatedRoot))) continue;
                for (var current = pair.Key; current != null; current = current.parent)
                {
                    if (ranges.TryGetValue(current, out var range))
                        ranges[current] = new Range { first = Mathf.Min(range.first, rank), last = Mathf.Max(range.last, rank) };
                    else ranges[current] = new Range { first = rank, last = rank };
                    if (current == generatedRoot) break;
                }
            }
            return Reorder(generatedRoot, ranges, context);
        }

        private static int Reorder(Transform parent, Dictionary<Transform, Range> ranges, GenerationContext context)
        {
            int issues = 0;
            var children = new List<Transform>();
            var slots = new List<int>();
            var ordered = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                children.Add(child);
                issues += Reorder(child, ranges, context);
                if (ranges.ContainsKey(child)) { slots.Add(i); ordered.Add(child); }
            }
            ordered.Sort((a, b) => { int rank = ranges[a].first.CompareTo(ranges[b].first); return rank != 0 ? rank : a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()); });
            int previous = ranges.TryGetValue(parent, out var parentRange) ? parentRange.first : -1;
            foreach (var child in ordered)
            {
                var range = ranges[child];
                if (range.first < previous)
                {
                    issues++;
#if EFUN_DEBUG
                    context.GraphicSources.TryGetValue(parent, out var source);
                    Debug.LogWarning("[PSD2UIForm] 模板绘制槽位与源层顺序交错，已保留独立元素供局部调整: " + parent.name, source);
#endif
                    break;
                }
                previous = range.last;
            }
            for (int i = 0; i < ordered.Count; i++) children[slots[i]] = ordered[i];
            for (int i = 0; i < children.Count; i++) children[i].SetSiblingIndex(i);
            return issues;
        }
    }
}
#endif
