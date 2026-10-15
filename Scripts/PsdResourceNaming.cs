#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI
{
    // Resource keys come from PSD declarations, never from AI-generated display names.
    internal static class PsdResourceNaming
    {
        internal static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".asset" };

        internal static void Parse(string layerName, out string kind, out string key)
        {
            string value = (layerName ?? string.Empty).Trim();
            kind = value.StartsWith(UGUIParser.REF_PREFAB_TAG, StringComparison.OrdinalIgnoreCase) ? "refp"
                : value.StartsWith(UGUIParser.REF_TAG, StringComparison.OrdinalIgnoreCase) ? "ref" : string.Empty;
            if (kind.Length == 0) { key = string.Empty; return; }
            value = value.Substring(kind == "refp" ? UGUIParser.REF_PREFAB_TAG.Length : UGUIParser.REF_TAG.Length);
            key = ReferenceKey(UGUIParser.Instance?.RemoveLayerTypeFlags(value) ?? value);
        }

        internal static string LayerName(string value)
            => FileName(UGUIParser.Instance?.RemoveLayerTypeFlags(value) ?? value);

        internal static string FileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "PsdLayer";
            var result = new StringBuilder(value.Length);
            foreach (char c in value)
                result.Append(c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c);
            string name = result.ToString().TrimEnd(' ', '.');
            if (name.Length == 0) name = "PsdLayer";
            string stem = name.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] >= '1' && stem[3] <= '9') name = "_" + name;
            // Leave space for an extension and a duplicate suffix, without cutting a surrogate pair.
            if (name.Length > 180) name = name.Substring(0, char.IsHighSurrogate(name[179]) ? 179 : 180).TrimEnd(' ', '.');
            return name;
        }

        internal static string ReferenceKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim().Replace('\\', '/');
            // References are relative to the configured shared directory, not arbitrary filesystem paths.
            if (value.StartsWith("/", StringComparison.Ordinal) || value.IndexOf(':') >= 0) return string.Empty;
            var parts = value.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]) || parts[i] == "." || parts[i] == "..") return string.Empty;
                parts[i] = FileName(parts[i]);
            }
            return string.Join("/", parts);
        }

        internal static string FindAsset(string directory, string key, bool prefab)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(key)) return null;
            string path = directory.Replace('\\', '/').TrimEnd('/') + "/" + key;
            if (prefab)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) path += ".prefab";
                return File.Exists(path) ? path : null;
            }
            // An explicit extension is accepted, otherwise require one exact stem match.
            if (Array.Exists(ImageExtensions, extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return File.Exists(path) ? path : null;
            string match = null;
            foreach (string extension in ImageExtensions)
            {
                string candidate = path + extension;
                if (!File.Exists(candidate)) continue;
                if (match != null) throw new InvalidOperationException("资源引用存在多个同名图片，请保留扩展名明确指定: " + key);
                match = candidate;
            }
            return match;
        }

        internal static string[] AllocateNames(IReadOnlyList<string> names)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                string name = FileName(names[i]);
                result[i] = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
                reserved.Add(result[i]);
            }
            for (int i = 0; i < result.Length; i++)
            {
                string name = result[i];
                if (allocated.Add(name)) continue;
                if (!nextSuffix.TryGetValue(name, out int suffix)) suffix = 1;
                while (reserved.Contains(name + "_" + suffix) || !allocated.Add(name + "_" + suffix)) suffix++;
                result[i] = name + "_" + suffix;
                nextSuffix[name] = suffix + 1;
            }
            return result;
        }
    }
}
#endif
