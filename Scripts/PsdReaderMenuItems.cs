/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 *
 * The software and associated documentation (including but not limited to source code,
 * object code, documents, images, audio, and video files) are the intellectual property
 * of Beijing Yiqu Technology Co., Ltd.
 *
 * For commercial use or licensing inquiries, please contact:
 * Email: efunstudio@gmail.com
 * Website: https://efunstudio.cn
 */

#if UNITY_EDITOR
using cn.efunstudio.psdreader;
using UnityEditor;

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal static class PsdReaderMenuItems
    {
        [MenuItem(PsdReaderMenuActions.ForceResetMenuPath)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void ForceReset()
        {
            PsdReaderMenuActions.ForceReset();
        }

        [MenuItem(PsdReaderMenuActions.LicenseWindowMenuPath, priority = PsdReaderMenuActions.LicenseWindowPriority)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void OpenLicenseWindow()
        {
            PsdReaderMenuActions.OpenLicenseWindow();
        }

        [MenuItem(PsdReaderMenuActions.ClearLicenseMenuPath, priority = PsdReaderMenuActions.ClearLicensePriority)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void ClearLicense()
        {
            PsdReaderMenuActions.ClearLicense();
        }

        [MenuItem(PsdReaderMenuActions.CheckUpdateMenuPath, priority = PsdReaderMenuActions.CheckUpdatePriority)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void CheckUpdate()
        {
            PsdReaderMenuActions.CheckUpdate();
        }
    }
}
#endif
