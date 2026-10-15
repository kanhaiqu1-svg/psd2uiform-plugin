#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    public sealed partial class Psd2UIFormConverter
    {
        private static void RemoveGeneratedScripts(GameObject root, HashSet<MonoBehaviour> existing)
        {
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null || existing.Contains(script)) continue;
                var assembly = script.GetType().Assembly;
                if (assembly == typeof(Graphic).Assembly || assembly == typeof(TMPro.TMP_Text).Assembly) continue;
                DestroyImmediate(script);
            }
        }

        private static string FindGeneratedParentIdentity(Transform parent, Dictionary<string, GameObject> objects)
        {
            while (parent != null)
            {
                foreach (var pair in objects) if (pair.Value != null && pair.Value.transform == parent) return pair.Key;
                parent = parent.parent;
            }
            return null;
        }

        internal bool UpdateUIFormNode(PsdLayerNode selected)
        {
            var owner = selected;
            while (owner != null && AiUiContract.IsRole(owner.UIType) && owner.transform.parent != transform)
                owner = owner.transform.parent.GetComponent<PsdLayerNode>();
            if (owner == null || AiUiContract.IsRole(owner.UIType))
            {
                Debug.LogWarning("[PSD2UIForm] 此角色尚未绑定到所属控件，保留现有输出。", selected);
                return false;
            }
            string path = AssetDatabase.GUIDToAssetPath(Document.uiFormGuid);
            var metadata = FindGeneratedMetadataEntry(path);
            if (string.IsNullOrEmpty(path) || metadata == null || !metadata.Entries.Exists(e => e.SourceIdentity == owner.PersistentId))
            {
                Debug.LogWarning("[PSD2UIForm] 当前节点尚未关联已生成的 UIForm。请先完整生成，或将节点导出为独立 prefab。", selected);
                return false;
            }
            imageUpdateScope = owner;
            try { return ExportUIPrefab(transform, Path.GetDirectoryName(path), owner); }
            finally { imageUpdateScope = null; }
        }

        private List<GeneratedTreeNode> SelectWriteScope(PsdLayerNode selected, List<GeneratedTreeNode> roots,
            GenerationContext context, Dictionary<string, GameObject> old, string prefabPath)
        {
            context.DesiredNodeByLayer.TryGetValue(selected, out var target);
            if (target == null && !selected.HasMissingSource || !old.ContainsKey(selected.PersistentId))
                throw new InvalidOperationException("选中节点缺少确定的输出映射，未修改 prefab。");
            var scope = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in selected.GetComponentsInChildren<PsdLayerNode>(true)) scope.Add(node.PersistentId);
            var entries = FindGeneratedMetadataEntry(prefabPath).Entries;
            bool changed;
            do
            {
                changed = false;
                foreach (var entry in entries)
                    if (!string.IsNullOrEmpty(entry.ParentIdentity) && scope.Contains(entry.ParentIdentity)) changed |= scope.Add(entry.SourceIdentity);
            } while (changed);
            context.WriteScope = scope;
            foreach (var pair in context.DesiredNodeByLayer)
                if (pair.Value.ReuseGo != null) context.DesiredToGenerated[pair.Value] = pair.Value.ReuseGo;
            var writableObjects = new HashSet<GameObject>();
            foreach (var pair in old) if (scope.Contains(pair.Key)) writableObjects.Add(pair.Value);
            context.NodesToOrphan.RemoveAll(go => !writableObjects.Contains(go));
            return target != null ? new List<GeneratedTreeNode> { target } : new List<GeneratedTreeNode>();
        }

        private static int ReconcileScopedOrder(PsdLayerNode scope, GenerationContext context)
        {
            if (!context.DesiredNodeByLayer.TryGetValue(scope, out var desired)) return 0;
            return context.DesiredToGenerated.TryGetValue(desired, out var generated)
                ? PsdGeneratedDrawOrder.Reconcile(context, scope.transform, generated.transform) : 0;
        }

        private static void UpdateScopedScrollBounds(PsdLayerNode scope, GenerationContext context, PsdManagedProperties properties)
        {
            GeneratedTreeNode start = null;
            for (var ancestor = scope.transform.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.TryGetComponent<PsdLayerNode>(out var source) && context.DesiredNodeByLayer.TryGetValue(source, out start)) break;
            for (var parent = start; parent != null; parent = parent.Parent)
            {
                if (!context.DesiredToGenerated.TryGetValue(parent, out var go)) continue;
                var scroll = go.GetComponent<ScrollRect>();
                if (scroll == null || scroll.content == null) continue;
                var content = scroll.content;
                properties.AllowDerivedField(content, "m_SizeDelta");
                Vector2 extent = scroll.viewport != null ? scroll.viewport.rect.size : Vector2.zero;
                var corners = new Vector3[4];
                foreach (var child in content.GetComponentsInChildren<RectTransform>(false))
                {
                    if (child == content) continue;
                    bool clipped = false;
                    for (var ancestor = child.parent; ancestor != content && ancestor != null; ancestor = ancestor.parent)
                        if (ancestor.GetComponent<Mask>() != null || ancestor.GetComponent<RectMask2D>() != null) { clipped = true; break; }
                    if (clipped) continue;
                    child.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        Vector3 point = content.InverseTransformPoint(corner);
                        extent.x = Mathf.Max(extent.x, point.x - content.rect.xMin);
                        extent.y = Mathf.Max(extent.y, content.rect.yMax - point.y);
                    }
                }
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, extent.x);
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, extent.y);
            }
        }
    }
}
#endif
