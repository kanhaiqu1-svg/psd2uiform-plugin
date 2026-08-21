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
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    public static class PsdLayerPreviewCache
    {
        private sealed class CacheEntry
        {
            public Texture2D Texture;
            public int RefCount;
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public static Texture2D Acquire(string cacheKey, Func<Texture2D> textureFactory)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || textureFactory == null)
            {
                return textureFactory?.Invoke();
            }

            lock (SyncRoot)
            {
                if (Cache.TryGetValue(cacheKey, out CacheEntry entry))
                {
                    if (entry != null && entry.Texture != null)
                    {
                        entry.RefCount++;
                        return entry.Texture;
                    }

                    Cache.Remove(cacheKey);
                }

                Texture2D texture = textureFactory();
                if (texture == null)
                {
                    return null;
                }

                Cache[cacheKey] = new CacheEntry
                {
                    Texture = texture,
                    RefCount = 1,
                };
                return texture;
            }
        }

        public static bool Release(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return false;
            }

            Texture2D textureToDestroy = null;
            lock (SyncRoot)
            {
                if (!Cache.TryGetValue(cacheKey, out CacheEntry entry) || entry == null)
                {
                    return false;
                }

                entry.RefCount--;
                if (entry.RefCount > 0)
                {
                    return true;
                }

                textureToDestroy = entry.Texture;
                Cache.Remove(cacheKey);
            }

            if (textureToDestroy != null)
            {
                UnityEngine.Object.DestroyImmediate(textureToDestroy);
            }

            return true;
        }

        public static void Clear()
        {
            Texture2D[] texturesToDestroy;
            lock (SyncRoot)
            {
                if (Cache.Count == 0)
                {
                    return;
                }

                var textures = new List<Texture2D>(Cache.Count);
                foreach (CacheEntry entry in Cache.Values)
                {
                    if (entry?.Texture != null)
                    {
                        textures.Add(entry.Texture);
                    }
                }

                Cache.Clear();
                texturesToDestroy = textures.ToArray();
            }

            for (int i = 0; i < texturesToDestroy.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(texturesToDestroy[i]);
            }
        }
    }
}
#endif
