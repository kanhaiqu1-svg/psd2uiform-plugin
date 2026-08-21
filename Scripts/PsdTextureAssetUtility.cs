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
using System;
using System.Collections.Generic;
using System.IO;
using cn.efunstudio.psdreader;
using cn.efunstudio.psdreader.PsdParser;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal static class PsdTextureAssetUtility
    {
        private const string ExportModeKey = "Psd2UIExportMode";
        private static readonly PlatformSpec[] ManagedPlatforms = CollectManagedPlatforms();

        internal static byte[] EncodePng(PsdRenderedImage rendered)
        {
            if (rendered == null || rendered.IsEmpty)
            {
                return null;
            }

            if (!rendered.IsHighBitDepth)
            {
                return ImageConversion.EncodeArrayToPNG(
                    rendered.Rgba32,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    (uint)rendered.Width,
                    (uint)rendered.Height);
            }

            ushort[] rgba64 = rendered.Rgba64;
            byte[] rawData = new byte[rgba64.Length * sizeof(ushort)];
            Buffer.BlockCopy(rgba64, 0, rawData, 0, rawData.Length);
            return ImageConversion.EncodeArrayToPNG(
                rawData,
                GraphicsFormat.R16G16B16A16_UNorm,
                (uint)rendered.Width,
                (uint)rendered.Height);
        }

        internal static byte[] EncodePng(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            if (texture.format != TextureFormat.RGBA64)
            {
                return texture.EncodeToPNG();
            }

            var rawData = texture.GetRawTextureData<byte>();
            return ImageConversion.EncodeArrayToPNG(
                rawData.ToArray(),
                GraphicsFormat.R16G16B16A16_UNorm,
                (uint)texture.width,
                (uint)texture.height);
        }

        internal static void ApplyPrecisionImportSettings(TextureImporter importer, bool preferHighBitDepth)
        {
            if (importer == null)
            {
                return;
            }

            if (preferHighBitDepth)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                for (int i = 0; i < ManagedPlatforms.Length; i++)
                {
                    ApplyHighBitDepthOverrideIfSupported(importer, ManagedPlatforms[i]);
                }
            }

            importer.userData = MergeExportModeTag(importer.userData, BuildExportModeTag(preferHighBitDepth));
        }

        internal static bool MatchesExportMode(string assetPath, bool preferHighBitDepth)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            {
                return false;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            string expectedTag = BuildExportModeTag(preferHighBitDepth);
            return TryExtractExportModeTag(importer.userData, out string currentTag)
                && string.Equals(currentTag, expectedTag, StringComparison.Ordinal);
        }

        private static void ApplyHighBitDepthOverrideIfSupported(TextureImporter importer, PlatformSpec platform)
        {
            if (!SupportsHighBitDepthFormat(importer.textureType, platform))
            {
                return;
            }

            ApplyPlatformOverride(importer, platform.Name, TextureImporterFormat.RGBA64, true);
        }

        private static bool SupportsHighBitDepthFormat(TextureImporterType textureType, PlatformSpec platform)
        {
            if (platform.IsDefaultPlatform)
            {
                return TextureImporter.IsDefaultPlatformTextureFormatValid(textureType, TextureImporterFormat.RGBA64);
            }

            if (!BuildPipeline.IsBuildTargetSupported(platform.Group, platform.Target))
            {
                return false;
            }

            return TextureImporter.IsPlatformTextureFormatValid(textureType, platform.Target, TextureImporterFormat.RGBA64);
        }

        private static void ApplyPlatformOverride(TextureImporter importer, string platformName, TextureImporterFormat format, bool overridden)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platformName);
            settings.name = platformName;
            settings.overridden = overridden;
            settings.format = format;
            settings.maxTextureSize = Math.Max(settings.maxTextureSize, 16384);
            settings.allowsAlphaSplitting = false;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }

        private static string BuildExportModeTag(bool preferHighBitDepth)
        {
            return $"{ExportModeKey}:{(preferHighBitDepth ? "16" : "8")}:{ResolveAuthorizationStamp()}";
        }

        private static int ResolveAuthorizationStamp()
        {
#if EFUN_PRIVATE
            return 1;
#else
            return PsdReaderProductAccess.IsAvailable ? 1 : 0;
#endif
        }

        private static string MergeExportModeTag(string userData, string tag)
        {
            if (string.IsNullOrWhiteSpace(userData))
            {
                return tag;
            }

            if (!TryExtractExportModeTag(userData, out string existingTag))
            {
                return $"{userData}\n{tag}";
            }

            return userData.Replace(existingTag, tag);
        }

        private static bool TryExtractExportModeTag(string userData, out string tag)
        {
            tag = null;
            if (string.IsNullOrWhiteSpace(userData))
            {
                return false;
            }

            string[] lines = userData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith(ExportModeKey + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                tag = line;
                return true;
            }

            return false;
        }

        private static PlatformSpec[] CollectManagedPlatforms()
        {
            var platforms = new List<PlatformSpec>(8) { PlatformSpec.Default };
            var platformNames = new HashSet<string>(StringComparer.Ordinal)
            {
                PlatformSpec.Default.Name,
            };

            Array targets = Enum.GetValues(typeof(BuildTarget));
            for (int i = 0; i < targets.Length; i++)
            {
                BuildTarget target = (BuildTarget)targets.GetValue(i);
                if (target == BuildTarget.NoTarget)
                {
                    continue;
                }

                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
                if (group == BuildTargetGroup.Unknown || !BuildPipeline.IsBuildTargetSupported(group, target))
                {
                    continue;
                }

                string platformName = BuildPipeline.GetBuildTargetName(target);
                if (string.IsNullOrWhiteSpace(platformName) || !platformNames.Add(platformName))
                {
                    continue;
                }

                platforms.Add(new PlatformSpec(platformName, group, target));
            }

            return platforms.ToArray();
        }

        private readonly struct PlatformSpec
        {
            internal static readonly PlatformSpec Default = new PlatformSpec("DefaultTexturePlatform");

            internal readonly string Name;
            internal readonly BuildTargetGroup Group;
            internal readonly BuildTarget Target;
            internal readonly bool IsDefaultPlatform;

            internal PlatformSpec(string name)
            {
                Name = name;
                Group = BuildTargetGroup.Unknown;
                Target = BuildTarget.NoTarget;
                IsDefaultPlatform = true;
            }

            internal PlatformSpec(string name, BuildTargetGroup group, BuildTarget target)
            {
                Name = name;
                Group = group;
                Target = target;
                IsDefaultPlatform = false;
            }
        }
    }
}
#endif
