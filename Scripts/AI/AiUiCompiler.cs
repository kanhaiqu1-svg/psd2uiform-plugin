#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiUiPlan
    {
        internal sealed class Node
        {
            internal AiUiNode source;
            internal GUIType type;
            internal string[] leaves = Array.Empty<string>();
            internal Node parent;
            internal bool rejected;
            internal bool ownerValid = true;
            internal bool compositionPending;
            internal AiAnalysisNodeEntry textStyleSource;
        }
        internal readonly List<Node> nodes = new List<Node>();
        internal readonly List<AiRecognitionIssue> issues = new List<AiRecognitionIssue>();
        internal int sourceCount;
        internal string resultHash;
        internal List<AiMergeConflict> mergeConflicts;

        internal static AiUiPlan Build(AiAnalysisPackageDocument package, AiRecognitionDocument document)
        {
            var plan = new AiUiPlan();
            plan.issues.AddRange(document.issues);
            var index = new AiRenderOrderIndex(package.nodes);
            plan.sourceCount = index.Leaves.Count;
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in package.nodes) aliases.Add(source.shortId, source.id);
            var ids = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var item in document.ui)
            {
                var node = new Node { source = item };
                plan.nodes.Add(node);
                if (ids.TryGetValue(item.id, out var duplicate))
                { Reject(node, "duplicate_id", item.id); Reject(duplicate, "duplicate_id", item.id); }
                else ids.Add(item.id, node);
                if (!AiUiContract.TryParse(item.type, out node.type))
                { Reject(node, "invalid_type", "不支持的 AI 类型: " + item.type); continue; }
                var sources = new List<string>();
                foreach (string alias in item.sources)
                    if (aliases.TryGetValue(alias, out var source)) sources.Add(source);
                    else Issue(node, "unknown_source", alias + " 未绑定；保留类型和其他素材。");
                bool prefabBinding = sources.Count == 1 && index.Nodes[sources[0]].referenceKind == "refp";
                if (AiUiContract.IsText(node.type) && !prefabBinding)
                {
                    var text = item.text;
                    if (text == null) Issue(node, "missing_text", "文字需要明确的 text 声明；保留类型，不从位图或节点名猜测内容。");
                    else if (text.mode == "source")
                    {
                        if (sources.Count > 0) Issue(node, "text_sources", "原生文字仅由 text.source 指定；忽略额外 sources。");
                        sources.Clear();
                        if (aliases.TryGetValue(text.source, out var source) && index.Nodes[source].isTextLayer)
                            sources.Add(source);
                        else Issue(node, "text_source", "text.source 必须引用完整的原生文本图层。");
                    }
                    else if (text.mode == "value")
                    {
                        if (sources.Count > 0) Issue(node, "text_sources", "重建文字不占用图片素材；忽略 sources。");
                        sources.Clear();
                        if (!string.IsNullOrEmpty(text.styleSource))
                        {
                            if (aliases.TryGetValue(text.styleSource, out var source) && index.Nodes[source].isTextLayer)
                                node.textStyleSource = index.Nodes[source];
                            else Issue(node, "text_style_source", "样式源不是原生文本图层；仍保留明确的文字、样式字段与布局。");
                        }
                        if (node.textStyleSource == null && text.style?.IsComplete != true)
                            Issue(node, "text_style_pending", "文字缺少有效样式引用或明确的 fontSize/color。");
                        if (item.rect == null)
                            Issue(node, "text_layout_pending", "独立文字需要明确 rect；不借用样式源的位置。");
                    }
                }
                else if (item.text != null) Issue(node, "unexpected_text", "非文本类型忽略 text，不改变 AI 声明的类型。");
                bool hasReference = false;
                var leaves = index.Expand(sources.ToArray());
                foreach (string source in leaves)
                    if (!string.IsNullOrEmpty(index.Nodes[source].referenceKind)) hasReference = true;
                if (hasReference && node.type != GUIType.Null)
                {
                    var reference = leaves.Length == 1 ? index.Nodes[leaves[0]] : null;
                    bool prefab = reference != null && reference.referenceKind == "refp";
                    bool compatible = prefab ? AiUiContract.IsMain(node.type)
                        : !AiUiContract.IsContainer(node.type) && !AiUiContract.IsText(node.type);
                    if (reference == null || string.IsNullOrEmpty(reference.referenceKey) || !compatible)
                        Issue(node, "invalid_resource_binding", "引用资源必须独占一个兼容的元素；保留类型，等待 AI 修正素材绑定。");
                    else node.leaves = leaves;
                    continue;
                }
                if (AiUiContract.IsContainer(node.type))
                {
                    if (sources.Count > 0) Issue(node, "container_sources", "容器素材必须由 AI 声明具体子角色；未自动分配。");
                    continue;
                }
                var valid = new List<string>(leaves.Length);
                if (AiUiContract.IsText(node.type))
                {
                    if (item.text?.mode == "source" && leaves.Length == 1 && index.Nodes[leaves[0]].isTextLayer) valid.Add(leaves[0]);
                }
                else
                    foreach (string leaf in leaves)
                        if (node.type == GUIType.Null || !index.Nodes[leaf].isTextLayer) valid.Add(leaf);
                        else Issue(node, "image_source", "图片声明中的文本素材保持原状: " + index.Nodes[leaf].shortId);
                node.leaves = valid.ToArray();
                if (node.leaves.Length == 0 && !AiUiContract.IsText(node.type)
                    && !(node.type == GUIType.ScrollView_Viewport && item.rect != null && item.sources.Length == 0))
                    Issue(node, "missing_sources", "没有可用素材，保留 AI 类型与结构。");
            }
            // Resolve all edges before checking cycles; record order has no semantic meaning.
            foreach (var node in plan.nodes)
                if (!string.IsNullOrEmpty(node.source.parent))
                {
                    if (ids.TryGetValue(node.source.parent, out var parent) && !parent.rejected) node.parent = parent;
                    else Issue(node, "missing_parent", "所属节点不可用；保留类型，暂置根级。");
                }
            foreach (var node in plan.nodes)
            {
                var seen = new HashSet<Node>();
                for (var ancestor = node; ancestor != null; ancestor = ancestor.parent)
                    if (!seen.Add(ancestor))
                    {
                        var cycle = ancestor;
                        do {
                            var next = cycle.parent;
                            Issue(cycle, "parent_cycle", "仅断开成环父级连接，保留类型与素材。");
                            cycle.parent = null;
                            cycle = next;
                        } while (cycle != ancestor);
                        break;
                    }
            }
            var roles = new Dictionary<string, Node>(StringComparer.Ordinal);
            var claims = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
            foreach (var node in plan.nodes)
            {
                if (node.rejected) continue;
                if (AiUiContract.IsRole(node.type))
                {
                    node.ownerValid = node.parent != null && AiUiContract.Allows(node.parent.type, node.type);
                    if (!node.ownerValid) Issue(node, "invalid_role_owner", "保留角色声明，等待正确所属控件；不替换角色类型。");
                    else
                    {
                        string key = node.parent.source.id + ":" + node.type;
                        if (roles.TryGetValue(key, out var other))
                        {
                            other.ownerValid = node.ownerValid = false;
                            Issue(other, "duplicate_role", key); Issue(node, "duplicate_role", key);
                        }
                        else roles.Add(key, node);
                    }
                }
                else if (node.parent != null && !AiUiContract.CanContain(node.parent.type))
                {
                    // 父级（如 ScrollView_Viewport 这类"角色"节点）不能容纳独立 UI 时，摘掉节点会把它甩到
                    // 树外——列表条目就会跑到 ScrollView 之外。改为上溯到最近一个可容纳的祖先，保持嵌套。
                    var ancestor = node.parent;
                    while (ancestor != null && !AiUiContract.CanContain(ancestor.type))
                        ancestor = ancestor.parent;
                    Issue(node, "invalid_parent", ancestor != null
                        ? "父级不能包含独立 UI，已上移到最近可用父级: " + ancestor.type
                        : "父级不能包含独立 UI，且没有可用祖先，已断开连接；保留该元素。");
                    node.parent = ancestor;
                }
                foreach (string leaf in node.leaves)
                {
                    if (!claims.TryGetValue(leaf, out var owners)) claims.Add(leaf, owners = new List<Node>());
                    owners.Add(node);
                }
            }
            foreach (var node in plan.nodes)
            {
                if (node.rejected) continue;
                var valid = new List<string>();
                foreach (string leaf in node.leaves)
                    if (claims[leaf].Count == 1) valid.Add(leaf);
                    else Issue(node, "duplicate_source", index.Nodes[leaf].shortId + " 有多个 AI 归属，暂停该素材绑定。");
                node.leaves = valid.ToArray();
            }
            plan.mergeConflicts = index.FindMergeConflicts(plan.nodes);
            foreach (var conflict in plan.mergeConflicts)
                foreach (var node in plan.nodes)
                    if (node.source.id == conflict.targetId)
                    {
                        node.compositionPending = true;
                        Issue(node, "merge_order_conflict", conflict.Description + " 保留 AI 类型，等待 AI 拆分素材绑定。");
                        break;
                    }
            using (var hash = SHA256.Create())
                plan.resultHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    AiRecognitionResultLoader.Serialize(new AiRecognitionDocument { version = document.version, ui = document.ui })))).Replace("-", "").ToLowerInvariant();
            return plan;

            void Issue(Node node, string code, string reason) => AiRecognitionResultLoader.AddIssue(plan.issues, node.source.id, code, reason);
            void Reject(Node node, string code, string reason) { node.rejected = true; Issue(node, code, reason); }
        }
    }

    [Serializable]
    internal sealed class AiUiApplyReport
    {
        public string status;
        public int declaredNodes;
        public int appliedNodes;
        public int skippedNodes;
        public int appliedSources;
        public int declaredSources;
        public int sourceCount;
        public int readyControls;
        public int pendingNodes;
        public long applyMilliseconds;
        public List<AiRecognitionIssue> issues = new List<AiRecognitionIssue>();
    }

    internal static class AiUiCompiler
    {
        internal static AiUiApplyReport Apply(Psd2UIFormConverter converter, AiAnalysisPackageDocument package, AiUiPlan plan)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var report = new AiUiApplyReport { declaredNodes = plan.nodes.Count, sourceCount = plan.sourceCount };
            report.issues.AddRange(plan.issues);
            var existing = new Dictionary<string, PsdLayerNode>(StringComparer.Ordinal);
            var previousTargets = new List<PsdLayerNode>();
            var availableSources = new HashSet<int>();
            foreach (var node in converter.GetComponentsInChildren<PsdLayerNode>(true))
            {
                existing[AiNodeIdUtility.GetNodeId(converter, node)] = node;
                if (node.IsAiTarget) previousTargets.Add(node);
                if (node.BindPsdLayerIndex >= 0) availableSources.Add(node.BindPsdLayerIndex);
                foreach (int index in node.AiSourceIndices) availableSources.Add(index);
            }
            var created = new Dictionary<AiUiPlan.Node, PsdLayerNode>();
            var persistentMatches = AiPersistentIdentityMatcher.Match(package, plan, previousTargets);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in plan.nodes) foreach (string leaf in entry.leaves) claimed.Add(leaf);
            var pending = new HashSet<AiUiPlan.Node>();
            foreach (var node in plan.nodes) if (!node.rejected) pending.Add(node);
            string prefix = "gen:ai:" + package.treeHash + ":" + plan.resultHash + ":";
            var order = new AiRenderOrderIndex(package.nodes);
            var bound = new HashSet<string>(StringComparer.Ordinal);
            var ranks = new Dictionary<Transform, int>();
            var transferredTargets = new HashSet<PsdLayerNode>();
            while (pending.Count > 0)
            {
                foreach (var entry in plan.nodes)
                {
                    if (!pending.Contains(entry) || entry.parent != null && pending.Contains(entry.parent)) continue;
                    pending.Remove(entry);
                    PsdLayerNode parentNode = null;
                    if (entry.parent != null) created.TryGetValue(entry.parent, out parentNode);
                    bool ownerReady = entry.ownerValid && !entry.compositionPending && (!AiUiContract.IsRole(entry.type) || parentNode != null);
                    int undo = BeginUndo("Apply AI type");
                    PsdLayerNode target;
                    try
                    {
                        string id = prefix + entry.source.id;
                        string name = entry.source.name;
                        if ((!existing.TryGetValue(id, out target) || target == null)
                            && (!persistentMatches.TryGetValue(entry, out target) || target == null))
                        {
                            var go = new GameObject(name, typeof(RectTransform));
                            Undo.RegisterCreatedObjectUndo(go, "Apply AI type");
                            target = Undo.AddComponent<PsdLayerNode>(go);
                            target.ConfigureGeneratedGroupNode(name, Rect.zero, id);
                        }
                        else Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Apply AI type");
                        target.name = name;
                        target.SetAiDeliveryId(id);
                        Undo.SetTransformParent(target.transform, parentNode != null ? parentNode.transform : converter.transform, "Apply AI parent");
                        Rect? rect = entry.source.rect == null ? (Rect?)null : CanvasRect(entry.source.rect, package.document);
                        target.ConfigureAiDeclaration(entry.type, rect, ownerReady);
                        existing[id] = target;
                        created.Add(entry, target);
                        report.appliedNodes++;
                        Undo.CollapseUndoOperations(undo);
                    }
                    catch (Exception ex)
                    {
                        Undo.RevertAllDownToGroup(undo);
                        Issue(entry, "type_apply_failed", ex.Message);
                        continue;
                    }
                    if (!ownerReady)
                    {
                        if (!entry.compositionPending) Issue(entry, "owner_pending", "角色暂不绑定；其他节点继续应用。");
                        // order.Nodes 以素材 id 为键，模型节点 id 查不到；用本节点自己的首个素材绘制区间落位，
                        // 别让待绑定角色被顶到父级最上层。
                        ranks[target.transform] = entry.leaves.Length > 0
                            && order.Nodes.TryGetValue(entry.leaves[0], out var pendingSource) ? pendingSource.leafStart : int.MaxValue;
                        continue;
                    }
                    int rank = int.MaxValue;
                    foreach (string leaf in entry.leaves)
                    {
                        undo = BeginUndo("Bind AI source");
                        try
                        {
                            var evidence = order.Nodes[leaf];
                            if (converter.PsdInstance != null)
                            {
                                var layer = converter.PsdInstance.GetLayerByFlatIndex(evidence.sourceIndex);
                                bool sameIdentity = layer != null
                                    && (evidence.sourceLayerId < 0 || layer.TryGetLayerId(out int nativeId) && nativeId == evidence.sourceLayerId);
                                if (sameIdentity && evidence.sourceLayerId < 0)
                                    sameIdentity = layer.Name == evidence.layerName && layer.IsGroup == evidence.isGroupLayer
                                        && layer.Left - package.document.canvasLeft == evidence.rect.x
                                        && layer.Top - package.document.canvasTop == evidence.rect.y
                                        && layer.Width == evidence.rect.w && layer.Height == evidence.rect.h;
                                if (!sameIdentity) throw new InvalidOperationException("素材身份已改变: " + evidence.shortId);
                            }
                            else if (!availableSources.Contains(evidence.sourceIndex))
                                throw new InvalidOperationException("原始 PSD 素材不可用: " + evidence.shortId);
                            // Detach the transferred source from previous AI render bindings, never guess a new owner.
                            foreach (var previous in previousTargets)
                                if (previous != target && previous.HasAiSource(evidence.sourceIndex))
                                {
                                    Undo.RegisterFullObjectHierarchyUndo(previous.gameObject, "Transfer AI source");
                                    previous.RemoveAiSource(evidence.sourceIndex, converter.PsdInstance);
                                    transferredTargets.Add(previous);
                                }
                            Undo.RecordObject(target, "Bind AI source");
                            target.AddAiSource(evidence.sourceIndex, converter.PsdInstance);
                            if (entry.leaves.Length == 1)
                                target.SetAiResourceMetadata(evidence.layerName, evidence.referenceKind, evidence.referenceKey);
                            bound.Add(leaf);
                            rank = Math.Min(rank, evidence.drawIndex);
                            Undo.CollapseUndoOperations(undo);
                        }
                        catch (Exception ex)
                        {
                            Undo.RevertAllDownToGroup(undo);
                            Issue(entry, "source_binding_failed", ex.Message);
                        }
                    }
                    ranks[target.transform] = rank;
                    if (AiUiContract.IsText(entry.type) && entry.source.text != null)
                    {
                        undo = BeginUndo("Apply AI text");
                        try
                        {
                            Undo.RecordObject(target, "Apply AI text");
                            var style = entry.textStyleSource;
                            var styleLayer = style == null ? null : converter.PsdInstance?.GetLayerByFlatIndex(style.sourceIndex);
                            bool validStyle = styleLayer != null && (style.sourceLayerId < 0
                                || styleLayer.TryGetLayerId(out int styleId) && styleId == style.sourceLayerId);
                            if (style != null && !validStyle) Issue(entry, "text_style_binding_failed", "样式素材身份已变化；保留文字和显式样式。");
                            target.ConfigureAiText(entry.source.text, validStyle ? style.sourceIndex : -1, converter.PsdInstance);
                            Undo.CollapseUndoOperations(undo);
                        }
                        catch (Exception ex) { Undo.RevertAllDownToGroup(undo); Issue(entry, "text_apply_failed", ex.Message); }
                    }
                    try
                    {
                        foreach (string issue in target.GetAiRenderIssues(converter.PsdInstance))
                            Issue(entry, "render_dependency", issue);
                    }
                    catch (Exception ex) { Issue(entry, "render_diagnostic_failed", ex.Message); }
                }
            }
            if (report.appliedNodes > 0) RemoveOriginalHierarchy(converter, report.issues);


            int pruneUndo = BeginUndo("Prune superseded AI targets");
            PruneConsumedWrappers(converter, created, transferredTargets, report.issues);
            Undo.CollapseUndoOperations(pruneUndo);
            foreach (var pair in created)
            {
                if (!ranks.TryGetValue(pair.Value.transform, out int rank)) continue;
                for (var parent = pair.Value.transform.parent; parent != null && parent != converter.transform; parent = parent.parent)
                    if (!ranks.TryGetValue(parent, out int prior) || rank < prior) ranks[parent] = rank;
            }
            var parents = new List<Transform> { converter.transform };
            var seenParents = new HashSet<Transform> { converter.transform };
            foreach (var pair in created)
            {
                var createdTransform = pair.Value != null ? pair.Value.transform : null;
                if (createdTransform != null && seenParents.Add(createdTransform)) parents.Add(createdTransform);
            }
            // 原始节点被删后上抛到非 AI 祖先下的层级也要排序，否则那些层永远按旧顺序。
            foreach (var rankPair in ranks)
            {
                var rankParent = rankPair.Key != null ? rankPair.Key.parent : null;
                if (rankParent != null && seenParents.Add(rankParent)) parents.Add(rankParent);
            }
            foreach (var parent in parents)
            {
                int undo = BeginUndo("Apply UI draw order");
                try
                {
                    // 子节点顺序是父 Transform 的状态，只 Record 子对象撤销不回层序。
                    Undo.RegisterCompleteObjectUndo(parent, "Apply UI draw order");
                    var children = new List<Transform>();
                    var originalOrder = new Dictionary<Transform, int>();
                    for (int i = 0; i < parent.childCount; i++) { var child = parent.GetChild(i); children.Add(child); originalOrder.Add(child, i); }
                    children.Sort((a, b) => { int compare = Rank(a).CompareTo(Rank(b)); return compare != 0 ? compare : originalOrder[a].CompareTo(originalOrder[b]); });
                    foreach (var child in children) { Undo.RecordObject(child, "Apply UI draw order"); child.SetAsLastSibling(); }
                    Undo.CollapseUndoOperations(undo);
                }
                catch (Exception ex)
                {
                    Undo.RevertAllDownToGroup(undo);
                    AiRecognitionResultLoader.AddIssue(report.issues, parent.name, "draw_order_failed", ex.Message);
                }
            }
            int helperUndo = BeginUndo("Refresh AI helpers");
            foreach (var previous in transferredTargets)
            {
                if (previous == null) continue;
                try { previous.RefreshUIHelper(false); }
                catch (Exception ex) { AiRecognitionResultLoader.AddIssue(report.issues, previous.name, "helper_binding_failed", ex.Message); }
            }
            foreach (var pair in created)
            {
                try
                {
                    pair.Value.RefreshUIHelper(false);
                    if (!pair.Value.IsAiReady) { report.pendingNodes++; continue; }
                    if (pair.Key.type == GUIType.Null || AiUiContract.IsRole(pair.Key.type)) continue;
                    if (pair.Value.HasReusePrefabReference)
                    {
                        if (!pair.Value.TryGetReusePrefabAsset(out _))
                            throw new InvalidOperationException("引用 prefab 未找到: " + pair.Value.ReusePrefabKey);
                        report.readyControls++;
                        continue;
                    }
                    if (pair.Value.GetComponent<UIHelperBase>() == null) throw new InvalidOperationException("没有可用的 Helper/模板配置。");
                    if (pair.Key.type != GUIType.Panel && pair.Key.type != GUIType.ToggleGroup
                        && UGUIParser.Instance.GetRule(pair.Value.UIType)?.UIPrefab == null)
                        throw new InvalidOperationException("控件缺少对应模板 prefab。");
                    report.readyControls++;
                }
                catch (Exception ex) { report.pendingNodes++; Issue(pair.Key, "helper_binding_failed", ex.Message); }
            }
            Undo.CollapseUndoOperations(helperUndo);
            foreach (var leaf in order.Leaves)
                if (claimed.Contains(leaf.id) && !bound.Contains(leaf.id)) AiRecognitionResultLoader.AddIssue(report.issues, leaf.shortId, "unassigned_source", "本次没有成功接管该素材；原 PSD 素材仍可再次识别，已有 AI 绑定保留。");
            report.appliedSources = bound.Count;
            report.declaredSources = claimed.Count;
            report.skippedNodes = report.declaredNodes - report.appliedNodes;
            report.status = report.appliedNodes == 0 ? "no_application"
                : report.issues.Count == 0 && report.pendingNodes == 0 && report.skippedNodes == 0 ? "complete" : "partial_success";
            report.applyMilliseconds = timer.ElapsedMilliseconds;
            EditorUtility.SetDirty(converter);
            converter.SaveUiDocument();
            return report;

            int Rank(Transform t)
            {
                if (ranks.TryGetValue(t, out int rank)) return rank;
                var node = t.GetComponent<PsdLayerNode>();
                return node != null && order.Nodes.TryGetValue(AiNodeIdUtility.GetNodeId(converter, node), out var source) ? source.leafStart : int.MaxValue;
            }
            void Issue(AiUiPlan.Node node, string code, string reason) => AiRecognitionResultLoader.AddIssue(report.issues, node.source.id, code, reason);
        }

        private static void PruneConsumedWrappers(Psd2UIFormConverter converter,
            Dictionary<AiUiPlan.Node, PsdLayerNode> created, HashSet<PsdLayerNode> transferred, List<AiRecognitionIssue> issues)
        {
            var current = new HashSet<PsdLayerNode>(created.Values);
            var previous = new HashSet<PsdLayerNode>(transferred);
            foreach (var node in transferred)
                for (var parent = node.transform.parent; parent != null && parent != converter.transform; parent = parent.parent)
                {
                    var ancestor = parent.GetComponent<PsdLayerNode>();
                    if (ancestor != null && ancestor.IsAiTarget) previous.Add(ancestor);
                }
            // Delete only successfully superseded targets and their emptied containers.
            bool changed;
            do
            {
                changed = false;
                foreach (var node in previous)
                    if (node != null && !current.Contains(node) && node.transform.childCount == 0 && !node.HasAiSources)
                    {
                        try { Undo.DestroyObjectImmediate(node.gameObject); changed = true; }
                        catch (Exception ex) { AiRecognitionResultLoader.AddIssue(issues, node.name, "prune_failed", ex.Message); }
                    }
            } while (changed);
        }

        private static void RemoveOriginalHierarchy(Psd2UIFormConverter converter, List<AiRecognitionIssue> issues)
        {
            var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node.BindPsdLayerIndex < 0) continue;
                int undo = BeginUndo("Remove original PSD node");
                string name = node.name;
                try
                {
                    // Keep existing AI/custom children; only the original PSD representation is removed.
                    while (node.transform.childCount > 0)
                        Undo.SetTransformParent(node.transform.GetChild(0), node.transform.parent, "Preserve target hierarchy");
                    Undo.DestroyObjectImmediate(node.gameObject);
                    Undo.CollapseUndoOperations(undo);
                }
                catch (Exception ex)
                {
                    Undo.RevertAllDownToGroup(undo);
                    AiRecognitionResultLoader.AddIssue(issues, name, "source_cleanup_failed", ex.Message);
                }
            }
        }

        private static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
        private static Rect CanvasRect(RectData r, AiAnalysisDocumentInfo d)
            => new Rect(r.x + d.canvasLeft + r.w * .5f - d.psdWidth * .5f,
                d.psdHeight * .5f - d.canvasTop - r.y - r.h * .5f, r.w, r.h);
    }
}
#endif
