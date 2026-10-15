#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiPersistentIdentityMatcher
    {
        // Match declarations already made by AI. Never assign or infer a UI type here.
        internal static Dictionary<AiUiPlan.Node, PsdLayerNode> Match(AiAnalysisPackageDocument package,
            AiUiPlan plan, List<PsdLayerNode> previous)
        {
            var result = new Dictionary<AiUiPlan.Node, PsdLayerNode>();
            var claimed = new HashSet<PsdLayerNode>();
            var sources = new Dictionary<string, AiAnalysisNodeEntry>(StringComparer.Ordinal);
            foreach (var source in package.nodes) sources.Add(source.id, source);
            var oldKeys = new Dictionary<string, PsdLayerNode>(StringComparer.Ordinal);
            foreach (var node in previous)
            {
                var ids = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var child in node.GetComponentsInChildren<PsdLayerNode>(true))
                    for (int i = 0; i < child.AiSourceIndices.Length; i++)
                    {
                        int id = i < child.AiSourceLayerIds.Length ? child.AiSourceLayerIds[i] : -1;
                        if (id > 0) ids.Add("l" + id);
                        else if (child.GeneratedNodeId.StartsWith("gen:ai:" + package.treeHash + ":", StringComparison.Ordinal))
                            ids.Add("i" + child.AiSourceIndices[i]);
                    }
                if (ids.Count == 0) continue;
                string key = node.UIType + ":" + string.Join(",", ids);
                if (oldKeys.ContainsKey(key)) oldKeys[key] = null;
                else oldKeys.Add(key, node);
            }
            var descendants = new Dictionary<AiUiPlan.Node, SortedSet<string>>();
            var children = new Dictionary<AiUiPlan.Node, List<AiUiPlan.Node>>();
            foreach (var node in plan.nodes)
            {
                descendants.Add(node, new SortedSet<string>(StringComparer.Ordinal));
                if (node.parent == null) continue;
                if (!children.TryGetValue(node.parent, out var siblings)) children.Add(node.parent, siblings = new List<AiUiPlan.Node>());
                siblings.Add(node);
            }
            // Propagate each declared source once along its ancestor chain, without rescanning the entire plan per node.
            foreach (var node in plan.nodes)
            {
                if (node.rejected) continue;
                foreach (string leaf in node.leaves)
                {
                    var source = sources[leaf];
                    string id = source.sourceLayerId > 0 ? "l" + source.sourceLayerId : "i" + source.sourceIndex;
                    for (var ancestor = node; ancestor != null; ancestor = ancestor.parent) descendants[ancestor].Add(id);
                }
            }
            var newKeys = new Dictionary<string, AiUiPlan.Node>(StringComparer.Ordinal);
            foreach (var node in plan.nodes)
            {
                if (node.rejected) continue;
                var ids = descendants[node];
                if (ids.Count == 0) continue;
                string key = node.type + ":" + string.Join(",", ids);
                if (newKeys.ContainsKey(key)) newKeys[key] = null;
                else newKeys.Add(key, node);
            }
            foreach (var pair in newKeys)
                if (pair.Value != null && oldKeys.TryGetValue(pair.Key, out var old) && old != null && claimed.Add(old))
                    result.Add(pair.Value, old);

            // Existing children identify their owning container even when another child was added.
            // Accept only a unique one-to-one proposal in both directions.
            bool progress;
            do
            {
                progress = false;
                var proposals = new Dictionary<PsdLayerNode, AiUiPlan.Node>();
                foreach (var node in plan.nodes)
                {
                    if (node.rejected || result.ContainsKey(node) || !AiUiContract.IsContainer(node.type)
                        || !children.TryGetValue(node, out var directChildren)) continue;
                    PsdLayerNode candidate = null;
                    bool ambiguous = false;
                    foreach (var child in directChildren)
                    {
                        if (!result.TryGetValue(child, out var oldChild)) continue;
                        var oldParent = oldChild.transform.parent.GetComponent<PsdLayerNode>();
                        if (oldParent == null || oldParent.UIType != node.type || claimed.Contains(oldParent)) continue;
                        if (candidate != null && candidate != oldParent) { ambiguous = true; break; }
                        candidate = oldParent;
                    }
                    if (candidate == null || ambiguous) continue;
                    if (proposals.ContainsKey(candidate)) proposals[candidate] = null;
                    else proposals.Add(candidate, node);
                }
                foreach (var pair in proposals)
                    if (pair.Value != null && claimed.Add(pair.Key)) { result.Add(pair.Value, pair.Key); progress = true; }
            } while (progress);
            // Source-free authored text has no pixel identity. Match only an unambiguous semantic
            // name within the already-matched owner, never its value, borrowed style or position.
            var oldText = new Dictionary<string, PsdLayerNode>(StringComparer.Ordinal);
            foreach (var old in previous)
            {
                if (!old.HasAuthoredAiText || old.HasAiSources || claimed.Contains(old)) continue;
                var parent = old.transform.parent.GetComponent<PsdLayerNode>();
                string key = (parent != null ? parent.PersistentId : "root") + ":" + old.UIType + ":" + old.name;
                if (oldText.ContainsKey(key)) oldText[key] = null;
                else oldText.Add(key, old);
            }
            var newText = new Dictionary<string, AiUiPlan.Node>(StringComparer.Ordinal);
            foreach (var node in plan.nodes)
            {
                if (node.rejected || result.ContainsKey(node) || node.leaves.Length != 0 || node.source.text?.mode != "value"
                    || string.IsNullOrEmpty(node.source.name)) continue;
                PsdLayerNode parent = null;
                if (node.parent != null && !result.TryGetValue(node.parent, out parent)) continue;
                string key = (parent != null ? parent.PersistentId : "root") + ":" + node.type + ":" + node.source.name;
                if (newText.ContainsKey(key)) newText[key] = null;
                else newText.Add(key, node);
            }
            foreach (var pair in newText)
                if (pair.Value != null && oldText.TryGetValue(pair.Key, out var old) && old != null && claimed.Add(old))
                    result.Add(pair.Value, old);
            return result;
        }
    }
}
#endif
