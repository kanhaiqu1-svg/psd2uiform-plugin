#if UNITY_EDITOR
using System;
using System.Security.Cryptography;

namespace UGF.EditorTools.Psd2UGUI
{
    public sealed partial class Psd2UIFormConverter
    {
        private PsdLayerNode imageUpdateScope;
        internal PsdImageExportBatch ImageExport { get; set; }

        internal static string ImageHash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        internal string WriteImage(PsdLayerNode node, string suggestedPath, byte[] bytes, bool sprite, bool highDepth, bool slice)
        {
            if (ImageExport != null) return ImageExport.Add(node, suggestedPath, bytes, sprite, highDepth, slice);
            using (var batch = new PsdImageExportBatch(this))
            {
                string token = batch.Add(node, suggestedPath, bytes, sprite, highDepth, slice);
                batch.Complete(null);
                return batch.ResolvedPath(token);
            }
        }

        internal string ResolveResource(string directory, string key, bool prefab)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(key)) return null;
            return PsdResourceRegistry.Open(directory).Resolve(directory, key, prefab);
        }
    }
}
#endif
