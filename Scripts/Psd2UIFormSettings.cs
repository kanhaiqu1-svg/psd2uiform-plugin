/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 */

#if UNITY_EDITOR
using UnityEditor;

namespace UGF.EditorTools.Psd2UGUI
{
    // 定义我们支持的导出引擎
    public enum ExportTargetEngine 
    { 
        UGUI, 
        CocosStudio 
    }

    [FilePath("ProjectSettings/Psd2UIFormSettings.asset")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class Psd2UIFormSettings : ScriptableSingleton<Psd2UIFormSettings>
    {
        public ExportTargetEngine TargetEngine = ExportTargetEngine.UGUI;

        // 补回原版丢失的压缩图片配置字段
        public bool CompressImage = false;

        public string UIImagesOutputDir;
        public string UIFormOutputDir = "Assets";
        public bool UseUIFormOutputDir = false;
        public string LastUIFormOutputDir = "Assets";
    }
}
#endif