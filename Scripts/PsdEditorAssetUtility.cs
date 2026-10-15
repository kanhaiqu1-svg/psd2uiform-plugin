#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class PsdEditorAssetUtility
    {
        internal static void SaveAssetIfDirty(Object asset)
        {
#if UNITY_2021_2_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(asset);
#else
            // Earlier releases have no consistently available per-asset save API.
            if (EditorUtility.IsDirty(asset)) AssetDatabase.SaveAssets();
#endif
        }
    }
}
#endif
