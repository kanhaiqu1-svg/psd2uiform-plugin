#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    [Serializable]
    internal sealed class PsdPropertyRecord
    {
        public string targetId, parentId;
        public List<PsdPropertyValue> values = new List<PsdPropertyValue>();
    }

    [Serializable]
    internal sealed class PsdPropertyValue
    {
        public string path, value;
        public UnityEngine.Object asset;
    }

    // Serialized uGUI fields only; user scripts and persistent events are never copied from a template.
    internal sealed class PsdManagedProperties
    {
        private sealed class ObjectState
        {
            internal UnityEngine.Object target;
            internal List<PsdPropertyValue> current;
            internal PsdPropertyRecord baseline;
            internal Dictionary<string, PsdPropertyValue> userModified;
            internal bool writable;
            internal Transform parent;
            internal string parentKey;
        }

        private readonly List<ObjectState> before = new List<ObjectState>();
        private readonly Dictionary<UnityEngine.Object, List<PsdPropertyValue>> desired = new Dictionary<UnityEngine.Object, List<PsdPropertyValue>>();
        private readonly HashSet<UnityEngine.Object> untouched = new HashSet<UnityEngine.Object>();
        private readonly Transform root;
        private readonly Dictionary<UnityEngine.Object, string> referenceKeys = new Dictionary<UnityEngine.Object, string>();
        private readonly Dictionary<Transform, Transform> desiredParents = new Dictionary<Transform, Transform>();
        private readonly Dictionary<UnityEngine.Object, HashSet<string>> derivedFields = new Dictionary<UnityEngine.Object, HashSet<string>>();

        internal void AllowDerivedField(UnityEngine.Object target, string path)
        {
            if (!derivedFields.TryGetValue(target, out var fields)) derivedFields.Add(target, fields = new HashSet<string>());
            fields.Add(path);
        }

        internal PsdManagedProperties(GameObject contents, GameObject asset, List<PsdPropertyRecord> baseline, GameObject scope)
        {
            root = contents.transform;
            if (asset != null)
                foreach (var transform in contents.GetComponentsInChildren<Transform>(true))
                {
                    RegisterReference(transform.gameObject);
                    foreach (var component in transform.GetComponents<Component>()) if (component != null) RegisterReference(component);
                }
            void RegisterReference(UnityEngine.Object target)
            {
                var saved = ResolveSavedObject(target, root, asset.transform);
                if (saved != null) referenceKeys[target] = GlobalObjectId.GetGlobalObjectIdSlow(saved).ToString();
            }
            var index = new Dictionary<string, PsdPropertyRecord>(StringComparer.Ordinal);
            foreach (var record in baseline) index[record.targetId] = record;
            foreach (var target in ManagedObjects(contents))
            {
                var transform = TransformOf(target);
                var assetTarget = asset != null ? ResolveSavedObject(target, root, asset.transform) : null;
                PsdPropertyRecord record = null;
                if (assetTarget != null) index.TryGetValue(GlobalObjectId.GetGlobalObjectIdSlow(assetTarget).ToString(), out record);
                bool writable = scope == null || transform == scope.transform || transform.IsChildOf(scope.transform);
                var current = Read(target);
                var userModified = new Dictionary<string, PsdPropertyValue>(StringComparer.Ordinal);
                if (record != null)
                {
                    var prior = new Dictionary<string, PsdPropertyValue>(StringComparer.Ordinal);
                    foreach (var value in record.values) prior[value.path] = value;
                    foreach (var value in current)
                        if (prior.TryGetValue(value.path, out var old) && !Equal(value, old)) userModified.Add(value.path, old);
                }
                // Reimporting a deleted image can revive baseline asset references. Freeze user edits before imports.
                before.Add(new ObjectState { target = target, current = current, baseline = record, userModified = userModified, writable = writable,
                    parent = transform.parent, parentKey = ReferenceKey(transform.parent) });
                if (!writable) untouched.Add(target);
            }
        }

        internal void Merge(GenerationContext context)
        {
            foreach (var target in ManagedObjects(root.gameObject)) desired[target] = Read(target);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) desiredParents[transform] = transform.parent;
            var generatedRoots = new HashSet<GameObject>(context.DesiredToGenerated.Values);
            generatedRoots.UnionWith(context.FailedOutputRoots);
            foreach (var state in before)
            {
                if (state.target == null || !desired.TryGetValue(state.target, out var next)) continue;
                var transform = TransformOf(state.target);
                bool failed = false;
                for (var current = transform; current != null; current = current.parent)
                    if (generatedRoots.Contains(current.gameObject)) { failed = context.FailedOutputRoots.Contains(current.gameObject); break; }
                bool movedByUser = state.target is Transform && state.baseline != null && state.baseline.parentId != state.parentKey;
                if (state.target is Transform && (failed || !state.writable || movedByUser) && transform.parent != state.parent
                    && (state.parent == null || !state.parent.IsChildOf(transform)))
                {
                    transform.SetParent(state.parent, true);
                    if (movedByUser) context.GenerationIssues.Add(new AiRecognitionIssue { nodeId = transform.name,
                        code = "parent_conflict", action = "保留用户层级", reason = "生成父级与用户修改冲突" });
                }
                if (failed) { untouched.Add(state.target); desiredParents[transform] = state.parent; }
                derivedFields.TryGetValue(state.target, out var derived);
                var proposed = new Dictionary<string, PsdPropertyValue>(StringComparer.Ordinal);
                foreach (var value in next) proposed[value.path] = value;
                using (var serialized = new SerializedObject(state.target))
                {
                    foreach (var current in state.current)
                    {
                        if (!proposed.TryGetValue(current.path, out var value)) continue;
                        if (failed || movedByUser || !state.writable && (derived == null || !derived.Contains(current.path)))
                        {
                            // Canvas is temporarily disabled by the save pipeline and restored after saving.
                            if (state.target is Canvas && current.path == "m_Enabled") continue;
                            Write(serialized.FindProperty(current.path), current);
                            continue;
                        }
                        if (!state.userModified.TryGetValue(current.path, out var prior) || Equal(current, value)) continue;
                        Write(serialized.FindProperty(current.path), current);
                        if (!Equal(value, prior)) context.GenerationIssues.Add(new AiRecognitionIssue {
                            nodeId = state.target.name, code = "property_conflict", action = "保留用户修改", reason = current.path });
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        internal List<PsdPropertyRecord> Save(GameObject saved, List<PsdPropertyRecord> previous)
        {
            var old = new Dictionary<string, PsdPropertyRecord>(StringComparer.Ordinal);
            foreach (var record in previous) old[record.targetId] = record;
            var result = new List<PsdPropertyRecord>();
            foreach (var pair in desired)
            {
                if (pair.Key == null) continue;
                var target = ResolveSavedObject(pair.Key, root, saved.transform);
                if (target == null) continue;
                string id = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
                if (untouched.Contains(pair.Key) && old.TryGetValue(id, out var record)) result.Add(record);
                else
                {
                    var values = new List<PsdPropertyValue>(pair.Value.Count);
                    foreach (var value in pair.Value) values.Add(SavedValue(value, saved));
                    var transform = pair.Key as Transform;
                    var parent = transform != null && desiredParents.TryGetValue(transform, out var proposedParent) ? proposedParent : null;
                    var savedParent = parent != null ? ResolveSavedObject(parent, root, saved.transform) : null;
                    result.Add(new PsdPropertyRecord { targetId = id, values = values,
                        parentId = savedParent != null ? GlobalObjectId.GetGlobalObjectIdSlow(savedParent).ToString() : string.Empty });
                }
                if (untouched.Contains(pair.Key) && derivedFields.TryGetValue(pair.Key, out var fields))
                {
                    var output = result[result.Count - 1];
                    foreach (var value in pair.Value)
                    {
                        if (!fields.Contains(value.path)) continue;
                        output.values.RemoveAll(v => v.path == value.path);
                        output.values.Add(SavedValue(value, saved));
                    }
                }
            }
            return result;
        }

        private PsdPropertyValue SavedValue(PsdPropertyValue value, GameObject saved)
        {
            var asset = value.asset;
            string key = value.value;
            if (asset is GameObject || asset is Component)
            {
                if (!EditorUtility.IsPersistent(asset)) asset = ResolveSavedObject(asset, root, saved.transform);
                key = asset != null ? GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString() : string.Empty;
            }
            return new PsdPropertyValue { path = value.path, value = key, asset = asset };
        }

        private string ReferenceKey(UnityEngine.Object target)
        {
            if (target == null) return string.Empty;
            if (referenceKeys.TryGetValue(target, out var key)) return key;
            if (EditorUtility.IsPersistent(target)) return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
#if UNITY_6000_3_OR_NEWER
            return "new:" + target.GetEntityId();
#else
            return "new:" + target.GetInstanceID();
#endif
        }

        private static bool Equal(PsdPropertyValue a, PsdPropertyValue b) => a.value == b.value
            && (a.asset is GameObject || a.asset is Component || b.asset is GameObject || b.asset is Component
                || a.asset == b.asset || a.asset == null && b.asset == null);
        private static Transform TransformOf(UnityEngine.Object target) => target is GameObject go ? go.transform : ((Component)target).transform;
        private static IEnumerable<UnityEngine.Object> ManagedObjects(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                yield return transform.gameObject;
                foreach (var component in transform.GetComponents<Component>())
                    if (component != null && (component is Transform || component is CanvasGroup || component is Canvas
                        || component.GetType().Assembly == typeof(UnityEngine.UI.Graphic).Assembly)) yield return component;
            }
        }

        internal static UnityEngine.Object ResolveSavedObject(UnityEngine.Object target, Transform sourceRoot, Transform savedRoot)
        {
            var path = new List<int>();
            var transform = TransformOf(target);
            for (var current = transform; current != sourceRoot; current = current.parent)
            {
                if (current == null) return null;
                path.Add(current.GetSiblingIndex());
            }
            var saved = savedRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= saved.childCount) return null;
                saved = saved.GetChild(path[i]);
            }
            if (target is GameObject) return saved.gameObject;
            var components = transform.GetComponents(target.GetType());
            int slot = Array.IndexOf(components, target);
            var savedComponents = saved.GetComponents(target.GetType());
            return slot >= 0 && slot < savedComponents.Length ? savedComponents[slot] : null;
        }

        private List<PsdPropertyValue> Read(UnityEngine.Object target)
        {
            // Setting world position updates native layout before its serialized anchored position.
            // Capture the same values that PrefabUtility will save, or the next update sees a false user edit.
            if (target is RectTransform rectTransform) rectTransform.ForceUpdateRectTransforms();
            var values = new List<PsdPropertyValue>();
            using (var serialized = new SerializedObject(target))
            {
                var property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = property.propertyType == SerializedPropertyType.Generic;
                    string path = property.propertyPath;
                    if (target is Canvas && path == "m_Enabled" || path == "m_Script" || path == "m_RootOrder" || path == "m_Father" || path.StartsWith("m_Component", StringComparison.Ordinal)
                        || path == "m_Children" || path == "m_GameObject" || path.IndexOf("m_PersistentCalls", StringComparison.Ordinal) >= 0)
                    { enterChildren = false; continue; }
                    string value;
                    UnityEngine.Object asset = null;
                    switch (property.propertyType)
                    {
                        case SerializedPropertyType.Integer: value = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Boolean: value = property.boolValue ? "1" : "0"; break;
                        case SerializedPropertyType.Float: value = property.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.String: value = property.stringValue; break;
                        case SerializedPropertyType.Enum: value = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Color: value = JsonUtility.ToJson(property.colorValue); break;
                        case SerializedPropertyType.Vector2: value = JsonUtility.ToJson(property.vector2Value); break;
                        case SerializedPropertyType.Vector3: value = JsonUtility.ToJson(property.vector3Value); break;
                        case SerializedPropertyType.Vector4: value = JsonUtility.ToJson(property.vector4Value); break;
                        case SerializedPropertyType.Quaternion: value = JsonUtility.ToJson(property.quaternionValue); break;
                        case SerializedPropertyType.Rect: value = JsonUtility.ToJson(property.rectValue); break;
                        case SerializedPropertyType.ObjectReference:
                            asset = property.objectReferenceValue;
                            value = asset is GameObject || asset is Component ? ReferenceKey(asset) : string.Empty;
                            break;
                        default: continue;
                    }
                    values.Add(new PsdPropertyValue { path = path, value = value, asset = asset });
                }
            }
            return values;
        }

        private static void Write(SerializedProperty property, PsdPropertyValue value)
        {
            if (property == null) return;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: property.longValue = long.Parse(value.value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: property.boolValue = value.value == "1"; break;
                case SerializedPropertyType.Float: property.doubleValue = double.Parse(value.value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.String: property.stringValue = value.value; break;
                case SerializedPropertyType.Enum: property.intValue = int.Parse(value.value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Color: property.colorValue = JsonUtility.FromJson<Color>(value.value); break;
                case SerializedPropertyType.Vector2: property.vector2Value = JsonUtility.FromJson<Vector2>(value.value); break;
                case SerializedPropertyType.Vector3: property.vector3Value = JsonUtility.FromJson<Vector3>(value.value); break;
                case SerializedPropertyType.Vector4: property.vector4Value = JsonUtility.FromJson<Vector4>(value.value); break;
                case SerializedPropertyType.Quaternion: property.quaternionValue = JsonUtility.FromJson<Quaternion>(value.value); break;
                case SerializedPropertyType.Rect: property.rectValue = JsonUtility.FromJson<Rect>(value.value); break;
                case SerializedPropertyType.ObjectReference: property.objectReferenceValue = value.asset; break;
            }
        }
    }
}
#endif
