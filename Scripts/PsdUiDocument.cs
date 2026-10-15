#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    // Authoring data only. Never referenced by a generated runtime prefab.
    internal sealed class PsdUiDocument : ScriptableObject
    {
        public string documentId;
        public string sourceGuid;
        public string sourceRevision;
        public string uiFormGuid;
        public List<PsdUiNodeRecord> nodes = new List<PsdUiNodeRecord>();
        public List<Psd2UIFormConverter.GeneratedMetadataSerializedEntry> outputs = new List<Psd2UIFormConverter.GeneratedMetadataSerializedEntry>();
        public List<PsdImageRecord> images = new List<PsdImageRecord>();
        public List<PsdImageAlias> retiredImages = new List<PsdImageAlias>();
        public string imageReuseSummary;
    }

    [Serializable]
    internal sealed class PsdUiNodeRecord
    {
        public string id, parent, name, data;
        public Rect rect;
        public bool active;
    }

    [Serializable]
    internal sealed class PsdImageRecord
    {
        public string nodeId, assetGuid, path, contentHash, settingsKey;
        public string assetContentHash, masterSourceHash;
        public Vector4 generatedBorder;
        public bool hasGeneratedBorder;
    }

    [Serializable]
    internal sealed class PsdImageAlias
    {
        public string fromGuid, toGuid;
    }
}
#endif
