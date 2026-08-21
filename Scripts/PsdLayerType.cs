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
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum PsdLayerType
    {
        Unknown = 0,
        Layer,
        BlackWhiteAdjustmentLayer,
        BrightnessContrastLayer,
        CmykChannelMixerLayer,
        ColorBalanceAdjustmentLayer,
        CurvesLayer,
        ExposureLayer,
        HueSaturationLayer,
        InvertAdjustmentLayer,
        LevelsLayer,
        PhotoFilterLayer,
        PosterizeLayer,
        RgbChannelMixerLayer,
        SelectiveColorLayer,
        ThresholdLayer,
        VibranceLayer,
        FillLayer,
        LayerGroup,
        SectionDividerLayer,
        ShapeLayer,
        SmartObjectLayer,
        TextLayer
    }
}
#endif
