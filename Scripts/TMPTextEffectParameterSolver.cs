#if UNITY_EDITOR
using System;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal struct TMPTextGlowFit
    {
        public bool IsInner;
        public Color MaterialColor;
        public float GlowOffsetPixels;
        public float GlowKernelPixels;
        public float GlowPower;
        public float UnderlayDilatePixels;
        public float UnderlaySoftnessPixels;
        public float Error;
        public float TargetEnergy;
    }

    internal struct TMPTextGlowSolution
    {
        public bool HasShaderGlow;
        public TMPTextGlowFit ShaderGlow;
        public bool HasUnderlayGlow;
        public TMPTextGlowFit UnderlayGlow;
        public bool HasInnerGlowComposite;
        public TMPTextInnerGlowCompositeFit InnerGlowComposite;
    }

    internal struct TMPTextInnerGlowCompositeFit
    {
        public Color UnderlayColor;
        public float FaceInsetPixels;
        public float FaceSoftnessPixels;
        public float UnderlayDilatePixels;
        public float UnderlaySoftnessPixels;
    }

    /// <summary>
    /// Converts Photoshop glow profiles to the two native TMP SDF effect channels.
    /// Distances remain in rendered pixels until UGUIParser applies the material's
    /// ScaleRatio values.
    /// </summary>
    internal static class TMPTextEffectParameterSolver
    {
        private const float Tolerance = 0.000001f;
        private const float SqrtTwo = 1.41421356237f;
        private const float TmpDistanceCalibration = 2f / 3f;
        private const float ProfileHigh = 0.9f;
        private const float ProfileMiddle = 0.5f;
        private const float ProfileLow = 0.1f;

        internal static TMPTextGlowSolution Solve(
            Color faceColor,
            float effectPixelRange,
            in TextGlowEffectInfo outerGlow,
            in TextGlowEffectInfo innerGlow,
            bool underlayAvailable)
        {
            bool hasInner = innerGlow.Enabled && innerGlow.Color.a > Tolerance;
            bool hasOuter = outerGlow.Enabled && outerGlow.Color.a > Tolerance;
            var solution = default(TMPTextGlowSolution);
            if (!hasInner && !hasOuter)
                return solution;

            float maximumGlowKernel = Mathf.Max(0.01f, effectPixelRange * 0.5f);
            if (hasInner)
            {
                if (underlayAvailable
                    && IsNormalBlendMode(innerGlow.BlendModeKey)
                    && !IsSourceCenter(innerGlow.SourceKey))
                {
                    solution.HasInnerGlowComposite = true;
                    solution.InnerGlowComposite = FitInnerGlowComposite(in innerGlow);

                    // The single TMP Underlay channel now belongs to the inner glow.
                    // Keep an outer glow on TMP's separate Glow channel when both effects
                    // are present rather than overwriting either effect's material data.
                    if (hasOuter)
                    {
                        TMPTextGlowFit outerFit = FitShaderGlow(faceColor, maximumGlowKernel, in outerGlow);
                        if (outerFit.MaterialColor.a > Tolerance)
                        {
                            solution.HasShaderGlow = true;
                            solution.ShaderGlow = outerFit;
                        }
                    }
                }
                else
                {
                    TMPTextGlowFit innerFit = FitShaderGlow(faceColor, maximumGlowKernel, in innerGlow);
                    if (innerFit.MaterialColor.a > Tolerance)
                    {
                        solution.HasShaderGlow = true;
                        solution.ShaderGlow = innerFit;
                    }
                }

                if (hasOuter && underlayAvailable)
                {
                    TMPTextGlowFit outerFit = FitUnderlayGlow(effectPixelRange, in outerGlow);
                    if (outerFit.MaterialColor.a > Tolerance)
                    {
                        solution.HasUnderlayGlow = true;
                        solution.UnderlayGlow = outerFit;
                    }
                }
                return solution;
            }

            if (underlayAvailable && IsNormalBlendMode(outerGlow.BlendModeKey))
            {
                TMPTextGlowFit outerFit = FitUnderlayGlow(effectPixelRange, in outerGlow);
                if (outerFit.MaterialColor.a > Tolerance)
                {
                    solution.HasUnderlayGlow = true;
                    solution.UnderlayGlow = outerFit;
                }
                return solution;
            }

            TMPTextGlowFit shaderFit = FitShaderGlow(faceColor, maximumGlowKernel, in outerGlow);
            if (shaderFit.MaterialColor.a > Tolerance)
            {
                solution.HasShaderGlow = true;
                solution.ShaderGlow = shaderFit;
            }
            return solution;
        }

        private static TMPTextInnerGlowCompositeFit FitInnerGlowComposite(in TextGlowEffectInfo glow)
        {
            float sourceSize = Mathf.Max(0.01f, glow.Size);
            float searchExtent = GetProfileSearchExtent(in glow, sourceSize);
            float highDistance = FindProfileDistance(in glow, ProfileHigh, searchExtent);
            float lowDistance = FindProfileDistance(in glow, ProfileLow, searchExtent);

            // TMP's normal Underlay fills only the opacity removed from Face. Seed the
            // joint material solve in pixel space: Face controls the PS falloff while
            // Underlay remains a sharp, opaque reservoir at the original glyph boundary.
            return new TMPTextInnerGlowCompositeFit
            {
                UnderlayColor = new Color(glow.Color.r, glow.Color.g, glow.Color.b, 1f),
                FaceInsetPixels = Mathf.Max(0.5f, highDistance + 0.5f),
                FaceSoftnessPixels = Mathf.Max(0f, lowDistance + 0.5f),
                UnderlayDilatePixels = Mathf.Max(0.5f, highDistance + 1f),
                UnderlaySoftnessPixels = 0f
            };
        }

        private static TMPTextGlowFit FitShaderGlow(
            Color faceColor,
            float maximumKernelPixels,
            in TextGlowEffectInfo glow)
        {
            float sourceSize = Mathf.Max(0.01f, glow.Size);
            float searchExtent = GetProfileSearchExtent(in glow, sourceSize);
            float middleDistance = FindProfileDistance(in glow, ProfileMiddle, searchExtent);
            float lowDistance = FindProfileDistance(in glow, ProfileLow, searchExtent);
            float tailDistance = lowDistance + (lowDistance - middleDistance) * 0.25f;
            float calibratedMiddle = middleDistance * TmpDistanceCalibration;
            float calibratedTail = tailDistance * TmpDistanceCalibration;

            float offset = 0.5f;
            float branchInset = glow.Inner ? offset : 1.5f;
            float kernel = Mathf.Clamp(calibratedTail - branchInset, 0.05f, maximumKernelPixels);
            float normalizedMiddle = glow.Inner
                ? (calibratedMiddle + offset) / Mathf.Max(0.0001f, kernel + 1f)
                : Mathf.Abs(calibratedMiddle - offset) / Mathf.Max(0.0001f, kernel + 1f);
            normalizedMiddle = Mathf.Clamp(normalizedMiddle, 0.01f, 0.99f);
            float power = Mathf.Clamp(
                Mathf.Log(1f - ProfileMiddle) / Mathf.Log(normalizedMiddle),
                0.05f,
                1f);

            Color materialColor;
            float colorError;
            if (glow.Inner)
                materialColor = SolveInnerAdditiveColor(faceColor, glow.Color, glow.BlendModeKey, out colorError);
            else
                materialColor = SolveOuterAdditiveColor(glow.Color, glow.BlendModeKey, out colorError);

            float peakShape = glow.Inner
                ? EvaluateTMPGlowShape(-offset, kernel, power)
                : 1f;
            float opacity = Mathf.Clamp01(glow.Color.a);
            float peakOpacity = glow.Inner && !IsPreciseTechnique(glow.TechniqueKey) ? 0.4f : 0.5f;
            materialColor.a = Mathf.Clamp01(opacity * peakOpacity / Mathf.Max(0.1f, peakShape));
            if (EvaluateColorMagnitude(materialColor) <= Tolerance)
                materialColor.a = 0f;

            return new TMPTextGlowFit
            {
                IsInner = glow.Inner,
                MaterialColor = materialColor,
                GlowOffsetPixels = offset,
                GlowKernelPixels = kernel,
                GlowPower = power,
                Error = colorError,
                TargetEnergy = opacity * opacity
            };
        }

        private static TMPTextGlowFit FitUnderlayGlow(float effectPixelRange, in TextGlowEffectInfo glow)
        {
            float sourceSize = Mathf.Max(0.01f, glow.Size);
            float searchExtent = GetProfileSearchExtent(in glow, sourceSize);
            float highDistance = FindProfileDistance(in glow, ProfileHigh, searchExtent);
            float lowDistance = FindProfileDistance(in glow, ProfileLow, searchExtent);
            float transition = Mathf.Max(0.01f, lowDistance - highDistance);

            // TMP Underlay is a clamped linear ramp. Extrapolating the 90% and
            // 10% PS crossings preserves both its bright core and visible radius.
            float extrapolation = ProfileLow / (ProfileHigh - ProfileLow);
            float fullDistance = Mathf.Max(0f, highDistance - transition * extrapolation);
            float zeroDistance = lowDistance + transition * extrapolation;
            fullDistance *= TmpDistanceCalibration;
            zeroDistance *= TmpDistanceCalibration;

            float maximumDistance = Mathf.Max(0.01f, effectPixelRange);
            zeroDistance = Mathf.Min(zeroDistance, maximumDistance);
            fullDistance = Mathf.Min(fullDistance, zeroDistance);
            float dilate = Mathf.Clamp(fullDistance + zeroDistance, -maximumDistance, maximumDistance);
            float softness = Mathf.Clamp(zeroDistance - fullDistance - 1f, 0f, maximumDistance);

            Color materialColor = SolveUnderlayColor(glow.Color, glow.BlendModeKey, out float colorError);
            float opacity = Mathf.Clamp01(glow.Color.a);
            float edgeCoverage = EvaluateTMPUnderlayCoverage(0f, dilate, softness);
            float targetEdge = opacity * EvaluatePSGlowProfile(in glow, 0f);
            materialColor.a = Mathf.Clamp01(targetEdge / Mathf.Max(0.1f, edgeCoverage));

            return new TMPTextGlowFit
            {
                IsInner = false,
                MaterialColor = materialColor,
                UnderlayDilatePixels = dilate,
                UnderlaySoftnessPixels = softness,
                Error = colorError,
                TargetEnergy = opacity * opacity
            };
        }

        private static float GetProfileSearchExtent(in TextGlowEffectInfo glow, float size)
        {
            return IsPreciseTechnique(glow.TechniqueKey)
                ? Mathf.Max(1f, size * 1.05f)
                : Mathf.Max(1f, size * 1.5f);
        }

        private static float FindProfileDistance(
            in TextGlowEffectInfo glow,
            float level,
            float maximumDistance)
        {
            float edgeValue = EvaluatePSGlowProfile(in glow, 0f);
            float target = edgeValue * level;
            if (target <= Tolerance)
                return 0f;

            float previousDistance = 0f;
            float previousValue = edgeValue;
            const int steps = 96;
            for (int i = 1; i <= steps; i++)
            {
                float distance = maximumDistance * i / steps;
                float value = EvaluatePSGlowProfile(in glow, distance);
                if (value > target)
                {
                    previousDistance = distance;
                    previousValue = value;
                    continue;
                }

                float range = previousValue - value;
                float t = range > Tolerance ? (previousValue - target) / range : 0f;
                return Mathf.Lerp(previousDistance, distance, t);
            }
            return maximumDistance;
        }

        internal static float EvaluatePSGlowProfile(in TextGlowEffectInfo glow, float distancePixels)
        {
            float size = Mathf.Max(0.0001f, glow.Size);
            float spread = Mathf.Clamp01(glow.Spread);
            float distance = Mathf.Max(0f, distancePixels);
            float profile;
            if (IsPreciseTechnique(glow.TechniqueKey))
            {
                float solid = size * spread;
                profile = distance <= solid
                    ? 1f
                    : 1f - Mathf.Clamp01((distance - solid) / Mathf.Max(0.0001f, size - solid));
            }
            else
            {
                float sigma = size / 3f;
                profile = EvaluateErfc(distance / (SqrtTwo * sigma));
                profile = Mathf.Clamp01(profile / Mathf.Max(0.01f, 1f - spread));
            }

            float rangeExponent = Mathf.Pow(2f, (0.5f - Mathf.Clamp01(glow.Range)) * 2f);
            profile = Mathf.Pow(Mathf.Clamp01(profile), rangeExponent);
            profile = EvaluateContour(glow.Contour, profile);
            if (glow.Inner && IsSourceCenter(glow.SourceKey))
                profile = 1f - profile;
            return Mathf.Clamp01(profile);
        }

        private static float EvaluateTMPGlowShape(float signedGlowDistance, float kernelPixels, float power)
        {
            float normalizedDistance = Mathf.Clamp01(
                Mathf.Abs(signedGlowDistance) / Mathf.Max(0.0001f, 1f + kernelPixels));
            return 1f - Mathf.Pow(normalizedDistance, power);
        }

        private static float EvaluateTMPUnderlayCoverage(float outsideDistance, float dilatePixels, float softnessPixels)
        {
            return Mathf.Clamp01(
                0.5f + (0.5f * dilatePixels - outsideDistance) / (1f + softnessPixels));
        }

        private static float EvaluateErfc(float value)
        {
            float sign = value < 0f ? -1f : 1f;
            float x = Mathf.Abs(value);
            float t = 1f / (1f + 0.3275911f * x);
            float polynomial = (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t
                - 0.284496736f) * t + 0.254829592f) * t;
            float erf = 1f - polynomial * Mathf.Exp(-x * x);
            return 1f - sign * erf;
        }

        private static float EvaluateContour(TextEffectContourPoint[] contour, float input)
        {
            input = Mathf.Clamp01(input);
            if (contour == null || contour.Length == 0)
                return input;
            if (input <= contour[0].Input)
                return contour[0].Output;

            for (int i = 1; i < contour.Length; i++)
            {
                if (input > contour[i].Input)
                    continue;
                float range = contour[i].Input - contour[i - 1].Input;
                if (range <= Tolerance)
                    return contour[i].Output;
                float t = (input - contour[i - 1].Input) / range;
                return Mathf.LerpUnclamped(contour[i - 1].Output, contour[i].Output, t);
            }
            return contour[contour.Length - 1].Output;
        }

        private static Color SolveInnerAdditiveColor(
            Color faceColor,
            Color effectColor,
            string blendMode,
            out float error)
        {
            Color target = Blend(faceColor, effectColor, blendMode);
            var result = new Color(
                Mathf.Clamp01(target.r - faceColor.r),
                Mathf.Clamp01(target.g - faceColor.g),
                Mathf.Clamp01(target.b - faceColor.b),
                1f);
            float dr = faceColor.r + result.r - target.r;
            float dg = faceColor.g + result.g - target.g;
            float db = faceColor.b + result.b - target.b;
            error = (dr * dr + dg * dg + db * db) / 3f;
            return result;
        }

        private static Color SolveOuterAdditiveColor(Color effectColor, string blendMode, out float error)
        {
            Color sum = Color.clear;
            for (int i = 0; i < 5; i++)
            {
                Color background = GetReferenceBackground(i);
                Color target = Blend(background, effectColor, blendMode);
                sum.r += Mathf.Max(0f, target.r - background.r);
                sum.g += Mathf.Max(0f, target.g - background.g);
                sum.b += Mathf.Max(0f, target.b - background.b);
            }

            var result = new Color(
                Mathf.Clamp01(sum.r / 5f),
                Mathf.Clamp01(sum.g / 5f),
                Mathf.Clamp01(sum.b / 5f),
                1f);
            error = 0f;
            for (int i = 0; i < 5; i++)
            {
                Color background = GetReferenceBackground(i);
                Color target = Blend(background, effectColor, blendMode);
                float dr = background.r + result.r - target.r;
                float dg = background.g + result.g - target.g;
                float db = background.b + result.b - target.b;
                error += (dr * dr + dg * dg + db * db) / 15f;
            }
            return result;
        }

        private static Color SolveUnderlayColor(Color effectColor, string blendMode, out float error)
        {
            if (IsNormalBlendMode(blendMode))
            {
                error = 0f;
                return new Color(effectColor.r, effectColor.g, effectColor.b, 1f);
            }

            Color sum = Color.clear;
            for (int i = 0; i < 5; i++)
                sum += Blend(GetReferenceBackground(i), effectColor, blendMode);
            var result = new Color(
                Mathf.Clamp01(sum.r / 5f),
                Mathf.Clamp01(sum.g / 5f),
                Mathf.Clamp01(sum.b / 5f),
                1f);
            error = 0f;
            for (int i = 0; i < 5; i++)
            {
                Color target = Blend(GetReferenceBackground(i), effectColor, blendMode);
                float dr = result.r - target.r;
                float dg = result.g - target.g;
                float db = result.b - target.b;
                error += (dr * dr + dg * dg + db * db) / 15f;
            }
            return result;
        }

        private static float EvaluateColorMagnitude(Color color)
        {
            return color.r * color.r + color.g * color.g + color.b * color.b;
        }

        private static Color GetReferenceBackground(int index)
        {
            switch (index)
            {
                case 0: return Color.black;
                case 1: return new Color(0.25f, 0.25f, 0.25f, 1f);
                case 2: return new Color(0.5f, 0.5f, 0.5f, 1f);
                case 3: return new Color(0.25f, 0.5f, 0.75f, 1f);
                default: return Color.white;
            }
        }

        private static Color Blend(Color baseColor, Color overlayColor, string blendMode)
        {
            string mode = (blendMode ?? string.Empty).Trim();
            if (EqualsMode(mode, "Mltp", "Multiply"))
                return new Color(baseColor.r * overlayColor.r, baseColor.g * overlayColor.g, baseColor.b * overlayColor.b, 1f);
            if (EqualsMode(mode, "Scrn", "Screen"))
                return new Color(1f - (1f - baseColor.r) * (1f - overlayColor.r), 1f - (1f - baseColor.g) * (1f - overlayColor.g), 1f - (1f - baseColor.b) * (1f - overlayColor.b), 1f);
            if (EqualsMode(mode, "Ovrl", "Overlay"))
                return new Color(Overlay(baseColor.r, overlayColor.r), Overlay(baseColor.g, overlayColor.g), Overlay(baseColor.b, overlayColor.b), 1f);
            if (EqualsMode(mode, "SftL", "SoftLight"))
                return new Color(SoftLight(baseColor.r, overlayColor.r), SoftLight(baseColor.g, overlayColor.g), SoftLight(baseColor.b, overlayColor.b), 1f);
            if (EqualsMode(mode, "HrdL", "HardLight"))
                return new Color(Overlay(overlayColor.r, baseColor.r), Overlay(overlayColor.g, baseColor.g), Overlay(overlayColor.b, baseColor.b), 1f);
            if (EqualsMode(mode, "Drkn", "Darken"))
                return new Color(Mathf.Min(baseColor.r, overlayColor.r), Mathf.Min(baseColor.g, overlayColor.g), Mathf.Min(baseColor.b, overlayColor.b), 1f);
            if (EqualsMode(mode, "Lghn", "Lighten"))
                return new Color(Mathf.Max(baseColor.r, overlayColor.r), Mathf.Max(baseColor.g, overlayColor.g), Mathf.Max(baseColor.b, overlayColor.b), 1f);
            return new Color(overlayColor.r, overlayColor.g, overlayColor.b, 1f);
        }

        private static float Overlay(float baseValue, float overlayValue)
        {
            return baseValue <= 0.5f
                ? 2f * baseValue * overlayValue
                : 1f - 2f * (1f - baseValue) * (1f - overlayValue);
        }

        private static float SoftLight(float baseValue, float overlayValue)
        {
            return overlayValue <= 0.5f
                ? baseValue - (1f - 2f * overlayValue) * baseValue * (1f - baseValue)
                : baseValue + (2f * overlayValue - 1f) * (Mathf.Sqrt(baseValue) - baseValue);
        }

        private static bool IsNormalBlendMode(string blendMode)
        {
            string mode = (blendMode ?? string.Empty).Trim();
            return mode.Length == 0 || EqualsMode(mode, "Nrml", "Normal");
        }

        private static bool EqualsMode(string value, string key, string name)
        {
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreciseTechnique(string techniqueKey)
        {
            string technique = (techniqueKey ?? string.Empty).Trim();
            return EqualsMode(technique, "PrBL", "Precise");
        }

        private static bool IsSourceCenter(string sourceKey)
        {
            string source = (sourceKey ?? string.Empty).Trim();
            return string.Equals(source, "SrcC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "Center", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "Ctr", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
