#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    // Explicit ref/refp declarations bind once by name; subsequent lookups follow the asset GUID.
    internal sealed class PsdResourceRegistry : ScriptableObject
    {
        [Serializable] internal sealed class Entry
        {
            public string key, assetGuid, ownerDocument, contentHash;
            public bool prefab;
            public int sourceLayerId;
        }
        [SerializeField] private List<Entry> entries = new List<Entry>();

        internal static PsdResourceRegistry Open(string directory)
        {
            string path = directory.Replace('\\', '/').TrimEnd('/') + "/Editor/PsdResources.asset";
            var registry = AssetDatabase.LoadAssetAtPath<PsdResourceRegistry>(path);
            if (registry != null) return registry;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            registry = CreateInstance<PsdResourceRegistry>();
            AssetDatabase.CreateAsset(registry, path);
            return registry;
        }

        internal Entry Find(string key, bool prefab)
            => entries.Find(entry => entry.prefab == prefab && string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase));

        internal bool ContainsGuid(string guid) => entries.Exists(entry => entry.assetGuid == guid);

        internal static HashSet<string> ImageGuids()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:PsdResourceRegistry"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<PsdResourceRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var entry in registry.entries) if (!entry.prefab) result.Add(entry.assetGuid);
            }
            return result;
        }

        internal string Resolve(string directory, string key, bool prefab)
        {
            var entry = Find(key, prefab);
            if (entry != null)
            {
                string bound = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                if (!string.IsNullOrEmpty(bound)) return bound;
                throw new FileNotFoundException("已绑定的资源已被删除，请恢复资源或修改 ref/refp 声明: " + key);
            }
            string path = PsdResourceNaming.FindAsset(directory, key, prefab);
            if (path != null) Bind(key, prefab, path, null, 0, null);
            return path;
        }

        internal void Bind(string key, bool prefab, string path, string owner, int layerId, string hash)
        {
            var entry = Find(key, prefab);
            if (entry == null) { entry = new Entry { key = key, prefab = prefab }; entries.Add(entry); }
            entry.assetGuid = AssetDatabase.AssetPathToGUID(path);
            entry.ownerDocument = owner;
            entry.sourceLayerId = layerId;
            entry.contentHash = hash;
            EditorUtility.SetDirty(this);
            PsdEditorAssetUtility.SaveAssetIfDirty(this);
        }
    }
}
#endif
