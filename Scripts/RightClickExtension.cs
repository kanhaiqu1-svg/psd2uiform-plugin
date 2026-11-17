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
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal static class RightClickExtension
    {
        private const string CropMinimalNineSliceMenuPath = "Assets/Psd2UIForm/Crop Minimal 9-Slice";

        [MenuItem("Assets/Psd2UIForm/Auto Sprite Border", priority = 1004)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void AutoSpriteSliceBorder()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                // Multiple/图集按整张图算 border 会污染全部子精灵，只处理 Single Sprite。
                if (texImporter == null || texImporter.textureType != TextureImporterType.Sprite
                    || texImporter.spriteImportMode != SpriteImportMode.Single)
                {
                    continue;
                }

                var rawReadable = texImporter.isReadable;
                try
                {
                    if (!rawReadable)
                    {
                        texImporter.isReadable = true;
                        texImporter.SaveAndReimport();
                    }

                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    if (sprite != null && sprite.texture != null)
                    {
                        texImporter.spriteBorder = UGUIParser.CalculateTexture9SliceBorder(sprite.texture);
                    }
                }
                finally
                {
                    // 中途抛异常也不能把资源的可读性留在改过的状态。
                    texImporter.isReadable = rawReadable;
                    texImporter.SaveAndReimport();
                }
            }
        }

        [MenuItem(CropMinimalNineSliceMenuPath, priority = 1005)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static void CropMinimalNineSlice()
        {
            int successCount = 0;
            int skipCount = 0;

            foreach (var guid in Selection.assetGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    foreach (var subGuid in AssetDatabase.FindAssets("t:Texture2D", new[] { assetPath }))
                    {
                        var subPath = AssetDatabase.GUIDToAssetPath(subGuid);
                        if (TryCropMinimalNineSlice(subPath)) successCount++;
                        else skipCount++;
                    }
                }
                else
                {
                    if (TryCropMinimalNineSlice(assetPath)) successCount++;
                    else skipCount++;
                }
            }

            if (successCount > 0)
            {
                Debug.Log($"Crop Minimal 9-Slice finished. Success:{successCount} Skip:{skipCount}");
            }
        }

        [MenuItem(CropMinimalNineSliceMenuPath, true)]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        private static bool ValidateCropMinimalNineSlice()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    return true;
                }

                var texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (texImporter != null &&
                    texImporter.textureType == TextureImporterType.Sprite &&
                    texImporter.spriteImportMode == SpriteImportMode.Single)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TryCropMinimalNineSlice(string assetPath)
        {
            assetPath = Psd2UIFormConverter.NormalizeToAssetPath(assetPath); // Fatcat定制: 路径标准化(软链接映射)
            var texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (texImporter == null)
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 不是可导入的贴图，已跳过: {assetPath}");
                return false;
            }

            // Fatcat定制: 原来这条直接 return，完全没有日志，用户只会看到"点了没反应"。
            if (texImporter.textureType != TextureImporterType.Sprite)
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 该贴图不是 Sprite 类型(textureType={texImporter.textureType})，"
                                 + $"无法做九宫格。请先把 Texture Type 改成 Sprite (2D and UI)。: {assetPath}");
                return false;
            }

            if (texImporter.spriteImportMode != SpriteImportMode.Single)
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 只支持 Single Sprite，当前是 {texImporter.spriteImportMode}: {assetPath}");
                return false;
            }

            Vector4 border = texImporter.spriteBorder;
            int left = Mathf.RoundToInt(border.x);
            int bottom = Mathf.RoundToInt(border.y);
            int right = Mathf.RoundToInt(border.z);
            int top = Mathf.RoundToInt(border.w);
            if (border == Vector4.zero || left < 0 || bottom < 0 || right < 0 || top < 0)
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 该贴图还没有九宫格边框(spriteBorder={border})，没有可裁的东西。"
                                 + $"先用【Assets/Psd2UIForm/Auto Sprite Border】算边框，或给图层打 .sliced 后重新生成UIForm。: {assetPath}");
                return false;
            }

            if (border.x != left || border.y != bottom || border.z != right || border.w != top)
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 边框必须是整数像素，当前是 {border}: {assetPath}");
                return false;
            }

            string fullPath = GetAssetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 找不到贴图文件: {assetPath}");
                return false;
            }

            // 裁剪固定走 RGBA32+EncodeToPNG，会把 16bit 导出量化成 8bit 而 userData 标记不变，直接跳过。
            if (IsSixteenBitPng(fullPath))
            {
                Debug.LogWarning($"[Crop Minimal 9-Slice] 会把 16bit PNG 量化成 8bit，已跳过: {assetPath}");
                return false;
            }

            Texture2D sourceTexture = null;
            Texture2D croppedTexture = null;
            try
            {
                sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(sourceTexture, File.ReadAllBytes(fullPath), false))
                {
                    Debug.LogWarning($"Load sprite source failed: {assetPath}");
                    return false;
                }

                int width = sourceTexture.width;
                int height = sourceTexture.height;
                int centerWidth = width - left - right;
                int centerHeight = height - bottom - top;
                if (centerWidth <= 0 || centerHeight <= 0)
                {
                    Debug.LogWarning($"[Crop Minimal 9-Slice] 边框({border})超过贴图尺寸({width}x{height}): {assetPath}");
                    return false;
                }

                if (centerWidth <= 1 && centerHeight <= 1)
                {
                    Debug.Log($"[Crop Minimal 9-Slice] 已经是最小九宫格（中心区 {centerWidth}x{centerHeight}），无需再裁: {assetPath}");
                    return false;
                }

                Color32[] sourcePixels = sourceTexture.GetPixels32();
                // Fatcat定制: 折叠容差与边框算法(NineSliceBorderTolerance)保持一致，
                // 否则「算得出边框却裁不了」——中心带只差 1~5/255 就会被判为不可折叠。
                int tolerance = UGUIParser.Instance != null ? UGUIParser.Instance.NineSliceBorderTolerance : 5;
                int deltaH = 0, deltaV = 0;
                bool cropHorizontal = centerWidth > 1
                    && PsdImageComparer.CanCollapse(sourcePixels, width, height, border, true, tolerance, out deltaH);
                bool cropVertical = centerHeight > 1
                    && PsdImageComparer.CanCollapse(sourcePixels, width, height, border, false, tolerance, out deltaV);
                if (!cropHorizontal && !cropVertical)
                {
                    int exceed = centerWidth > 1 ? deltaH : deltaV;
                    // Fatcat定制: 区分「差一点点」和「根本不均匀」。
                    // 手拉的边框常常画在渐变/图案上，那种情况压成 1 像素会改变外观，
                    // 属于「不该裁」而不是「裁失败」——Minimal 裁剪只是省贴图尺寸的可选步骤。
                    if (exceed <= 32)
                    {
                        Debug.LogWarning($"[Crop Minimal 9-Slice] 中心带差异 {exceed}/255 超过当前容差 {tolerance}，无法折叠。"
                                         + $"把配置里的「九宫格容差」调到 ≥{exceed} 再裁: {assetPath}");
                    }
                    else
                    {
                        Debug.Log($"[Crop Minimal 9-Slice] 跳过（不是错误）：中心区是渐变/图案，像素差异 {exceed}/255，"
                                  + $"压成 1 像素会改变外观，本来就不该用 Minimal 裁剪。"
                                  + $"手拉的边框已经生效，九宫格拉伸不受影响: {assetPath}");
                    }
                    return false;
                }

                int newWidth = cropHorizontal ? left + 1 + right : width;
                int newHeight = cropVertical ? bottom + 1 + top : height;
                int centerX = left + ((centerWidth - 1) / 2);
                int centerY = bottom + ((centerHeight - 1) / 2);

                Color32[] targetPixels = new Color32[newWidth * newHeight];
                for (int y = 0; y < newHeight; y++)
                {
                    int sourceY = MapCollapsedAxisIndex(y, cropVertical, bottom, centerHeight, centerY);
                    int sourceRowStart = sourceY * width;
                    int targetRowStart = y * newWidth;
                    for (int x = 0; x < newWidth; x++)
                    {
                        int sourceX = MapCollapsedAxisIndex(x, cropHorizontal, left, centerWidth, centerX);
                        targetPixels[targetRowStart + x] = sourcePixels[sourceRowStart + sourceX];
                    }
                }

                croppedTexture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false)
                {
                    alphaIsTransparency = sourceTexture.alphaIsTransparency,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = sourceTexture.anisoLevel
                };
                croppedTexture.SetPixels32(targetPixels);
                croppedTexture.Apply();

                byte[] encodedBytes = EncodeTextureBytes(assetPath, croppedTexture);
                if (encodedBytes == null || encodedBytes.Length == 0)
                {
                    Debug.LogWarning($"Unsupported texture format for Crop Minimal 9-Slice: {assetPath}");
                    return false;
                }

                File.WriteAllBytes(fullPath, encodedBytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                texImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (texImporter == null)
                {
                    Debug.LogWarning($"TextureImporter reload failed: {assetPath}");
                    return false;
                }

                Vector4 newBorder = new Vector4(left, bottom, right, top);
                if (texImporter.spriteBorder != newBorder)
                {
                    texImporter.spriteBorder = newBorder;
                    texImporter.SaveAndReimport();
                }

#if EFUN_DEBUG
                Debug.Log($"Crop Minimal 9-Slice success: {assetPath} ({width}x{height} -> {newWidth}x{newHeight})");
#endif
                return true;
            }
            finally
            {
                if (sourceTexture != null)
                {
                    Object.DestroyImmediate(sourceTexture);
                }

                if (croppedTexture != null)
                {
                    Object.DestroyImmediate(croppedTexture);
                }
            }
        }

        private static int MapCollapsedAxisIndex(int targetIndex, bool cropAxis, int stretchStart, int stretchLength, int centerIndex)
        {
            if (!cropAxis)
            {
                return targetIndex;
            }

            if (targetIndex < stretchStart)
            {
                return targetIndex;
            }

            if (targetIndex == stretchStart)
            {
                return centerIndex;
            }

            int stretchEndExclusive = stretchStart + stretchLength;
            return stretchEndExclusive + (targetIndex - stretchStart - 1);
        }

        private static bool IsSixteenBitPng(string fullPath)
        {
            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var header = new byte[25];
                    if (stream.Read(header, 0, 25) != 25) return false;
                    if (header[0] != 0x89 || header[1] != (byte)'P' || header[2] != (byte)'N' || header[3] != (byte)'G') return false;
                    if (header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R') return false;
                    return header[24] == 16;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static byte[] EncodeTextureBytes(string assetPath, Texture2D texture)
        {
            string extension = Path.GetExtension(assetPath)?.ToLowerInvariant();
            switch (extension)
            {
                case ".png":
                    return texture.EncodeToPNG();
                case ".jpg":
                case ".jpeg":
                    return texture.EncodeToJPG(100);
                default:
                    return null;
            }
        }

        private static string GetAssetFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return assetPath;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
#endif
