#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    public sealed partial class Psd2UIFormConverter
    {
        [SerializeField, HideInInspector] private PsdUiDocument uiDocument;

        internal PsdUiDocument Document
        {
            get
            {
                if (uiDocument != null) return uiDocument;
                string sourcePath = PsdAssetName;
                if (!string.IsNullOrEmpty(sourcePath) && sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    uiDocument = AssetDatabase.LoadAssetAtPath<PsdUiDocument>(DocumentPath(sourcePath));
                    string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
                    if (uiDocument == null && !string.IsNullOrEmpty(sourceGuid))
                        foreach (string guid in AssetDatabase.FindAssets("t:PsdUiDocument"))
                        {
                            var candidate = AssetDatabase.LoadAssetAtPath<PsdUiDocument>(AssetDatabase.GUIDToAssetPath(guid));
                            if (candidate.sourceGuid == sourceGuid) { uiDocument = candidate; break; }
                        }
                }
                if (uiDocument == null)
                {
                    uiDocument = ScriptableObject.CreateInstance<PsdUiDocument>();
                    uiDocument.documentId = Guid.NewGuid().ToString("N");
                    uiDocument.sourceGuid = string.IsNullOrEmpty(sourcePath) ? string.Empty : AssetDatabase.AssetPathToGUID(sourcePath);
                }
                return uiDocument;
            }
        }

        private static string DocumentPath(string source)
            => Path.GetDirectoryName(source).Replace('\\', '/') + "/Editor/" + Path.GetFileNameWithoutExtension(source) + ".ui.asset";

        private void SaveAuthoringDocument(GameObject root)
        {
            if (root == gameObject) SaveUiDocument();
        }

        internal void SaveUiDocument(string outputPath = null)
        {
            var document = Document;
            document.nodes.Clear();
            foreach (var node in GetComponentsInChildren<PsdLayerNode>(true))
            {
                var parent = node.transform.parent.GetComponent<PsdLayerNode>();
                document.nodes.Add(new PsdUiNodeRecord { id = node.PersistentId, parent = parent != null ? parent.PersistentId : null,
                    name = node.name, data = JsonUtility.ToJson(node), rect = node.LayerRect, active = node.gameObject.activeSelf });
            }
            document.sourceRevision = psdAssetChangeTime;
            string path = AssetDatabase.GetAssetPath(document);
            if (string.IsNullOrEmpty(path) && (document.hideFlags & HideFlags.DontSave) == 0)
            {
                string source = PsdAssetName;
                if (string.IsNullOrEmpty(source) || !source.StartsWith("Assets/", StringComparison.Ordinal)) source = outputPath;
                if (!string.IsNullOrEmpty(source) && source.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    path = DocumentPath(source);
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); AssetDatabase.Refresh(); }
                    AssetDatabase.CreateAsset(document, AssetDatabase.GenerateUniqueAssetPath(path));
                    EditorUtility.SetDirty(this);
                }
            }
            EditorUtility.SetDirty(document);
            if (!string.IsNullOrEmpty(path)) PsdEditorAssetUtility.SaveAssetIfDirty(document);
        }

        internal void RestoreUiDocument()
        {
            if (transform.childCount != 0) throw new InvalidOperationException("Restore requires an empty authoring view.");
            var document = Document;
            var nodes = new Dictionary<string, PsdLayerNode>(StringComparer.Ordinal);
            foreach (var record in document.nodes)
            {
                var go = new GameObject(record.name, typeof(RectTransform));
                go.SetActive(false);
                var node = go.AddComponent<PsdLayerNode>();
                JsonUtility.FromJsonOverwrite(record.data, node);
                node.UpdateLayerRectData(record.rect);
                nodes.Add(record.id, node);
            }
            foreach (var record in document.nodes)
            {
                var node = nodes[record.id];
                node.transform.SetParent(!string.IsNullOrEmpty(record.parent) && nodes.TryGetValue(record.parent, out var parent)
                    ? parent.transform : transform, false);
                node.gameObject.SetActive(record.active);
            }
            RefreshNodesBindLayer();
            RefreshAllLayerNodeHelpers();
        }

        private void SynchronizeSourceNodes(Transform staging)
        {
            var previous = GetComponentsInChildren<PsdLayerNode>(true);
            var incoming = staging.GetComponentsInChildren<PsdLayerNode>(true);
            var sourceNodes = new Dictionary<int, PsdLayerNode>();
            var consumed = new HashSet<int>();
            bool ai = false;
            foreach (var node in previous)
            {
                if (node.IsAiTarget)
                {
                    ai = true;
                    foreach (int sourceId in node.AiSourceLayerIds) if (sourceId > 0) consumed.Add(sourceId);
                }
                else if (node.bindPsdLayerId > 0) sourceNodes[node.bindPsdLayerId] = node;
            }
            var resolved = new Dictionary<PsdLayerNode, PsdLayerNode>();
            var surviving = new HashSet<PsdLayerNode>();
            foreach (var fresh in incoming)
            {
                if (ai && (fresh.LayerType == PsdLayerType.LayerGroup || consumed.Contains(fresh.bindPsdLayerId))) continue;
                PsdLayerNode node;
                if (fresh.bindPsdLayerId > 0 && sourceNodes.TryGetValue(fresh.bindPsdLayerId, out node))
                {
                    Undo.RecordObject(node, "Synchronize PSD source");
                    node.SynchronizeSource(fresh);
                }
                else
                {
                    node = fresh;
                    if (ai) node.MarkUnassignedSource();
                }
                resolved.Add(fresh, node);
                surviving.Add(node);
            }
            foreach (var fresh in incoming)
            {
                if (!resolved.TryGetValue(fresh, out var node)) continue;
                var parentSource = fresh.transform.parent.GetComponent<PsdLayerNode>();
                var targetParent = !ai && parentSource != null && resolved.TryGetValue(parentSource, out var parent) ? parent.transform : transform;
                if (!ai) node.SynchronizeSourceParent(targetParent, parentSource != null ? parentSource.bindPsdLayerId : 0);
                else if (node == fresh) Undo.SetTransformParent(node.transform, targetParent, "Add unassigned PSD source");
            }
            foreach (var node in previous)
            {
                if (node.IsAiTarget || node.bindPsdLayerId <= 0 && node.BindPsdLayerIndex < 0 || surviving.Contains(node)) continue;
                Undo.RecordObject(node, "Mark removed PSD source");
                node.MarkMissingSource();
            }
        }
    }
}
#endif
