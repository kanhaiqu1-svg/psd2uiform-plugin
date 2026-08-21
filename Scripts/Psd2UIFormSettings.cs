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

/*
插件获取:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

#if UNITY_EDITOR

namespace UGF.EditorTools.Psd2UGUI
{
    [FilePath("ProjectSettings/Psd2UIFormSettings.asset")]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public sealed class Psd2UIFormSettings : ScriptableSingleton<Psd2UIFormSettings>
    {
        /// <summary>
        /// UI图片导出根目录
        /// </summary>
        public string UIImagesOutputDir;
        /// <summary>
        /// UI预制体静默导出目录
        /// </summary>
        public string UIFormOutputDir = "Assets";
        /// <summary>
        /// 使用静默导出路径(点击导出UIForm后不弹出路径选择)
        /// </summary>
        public bool UseUIFormOutputDir = true;
        /// <summary>
        /// 导出图片后自动压缩图片文件
        /// </summary>
        public bool CompressImage = false;
        /// <summary>
        /// 导出图片后自动裁剪九宫格中心区域至最小尺寸
        /// </summary>
        public bool AutoCropMinimalNineSlice = false;
        public string LastUIFormOutputDir = "Assets";
    }
}
#endif
