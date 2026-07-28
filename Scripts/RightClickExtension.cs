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
                if (texImporter == null || texImporter.textureType != TextureImporterType.Sprite)
                {
                    continue;
                }

                var rawReadable = texImporter.isReadable;
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

                texImporter.isReadable = rawReadable;
                texImporter.SaveAndReimport();
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
            if (texImporter == null || texImporter.textureType != TextureImporterType.Sprite)
            {
                return false;
            }

            if (texImporter.spriteImportMode != SpriteImportMode.Single)
            {
                Debug.LogWarning($"Crop Minimal 9-Slice only supports Single Sprite: {assetPath}");
                return false;
            }

            Vector4 border = texImporter.spriteBorder;
            int left = Mathf.RoundToInt(border.x);
            int bottom = Mathf.RoundToInt(border.y);
            int right = Mathf.RoundToInt(border.z);
            int top = Mathf.RoundToInt(border.w);
            if (border == Vector4.zero || left < 0 || bottom < 0 || right < 0 || top < 0)
            {
                Debug.LogWarning($"Sprite Border is invalid or empty: {assetPath}");
                return false;
            }

            string fullPath = GetAssetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"Sprite file not found: {assetPath}");
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
                    Debug.LogWarning($"Sprite Border exceeds texture size: {assetPath}");
                    return false;
                }

                bool cropHorizontal = centerWidth > 1;
                bool cropVertical = centerHeight > 1;
                if (!cropHorizontal && !cropVertical)
                {
                    Debug.LogWarning($"Sprite center area is already minimal: {assetPath}");
                    return false;
                }

                int newWidth = cropHorizontal ? left + 1 + right : width;
                int newHeight = cropVertical ? bottom + 1 + top : height;
                int centerX = left + ((centerWidth - 1) / 2);
                int centerY = bottom + ((centerHeight - 1) / 2);

                Color32[] sourcePixels = sourceTexture.GetPixels32();
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

                Debug.Log($"Crop Minimal 9-Slice success: {assetPath} ({width}x{height} -> {newWidth}x{newHeight})");
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
