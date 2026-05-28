#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace UGF.EditorTools.Psd2UGUI
{
    [InitializeOnLoad]
    internal static class Psd2UIFormLicenseSetup
    {
        static Psd2UIFormLicenseSetup()
        {
            var targetDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "eFunStudioProtector", "Licenses", "psd2ugui");
            var targetFile = Path.Combine(targetDir, "license.cache.bin");

            if (!File.Exists(targetFile))
            {
                var sourceFile = Path.Combine("Assets", "Plugins", "PSD2UIForm", "license.cache.bin");
                var fullSource = Path.GetFullPath(sourceFile);

                if (File.Exists(fullSource))
                {
                    Directory.CreateDirectory(targetDir);
                    File.Copy(fullSource, targetFile, overwrite: false);
                }
            }
        }
    }
}
#endif
