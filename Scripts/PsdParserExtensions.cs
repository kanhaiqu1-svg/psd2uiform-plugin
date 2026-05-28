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
using System.Collections.Generic;
using System.Linq;
using cn.efunstudio.psdreader.PsdParser;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class PsdParserExtensions
    {
        internal static string GetDisplayName(this PsdLayer layer)
        {
            return layer != null ? (layer.Name ?? string.Empty) : string.Empty;
        }

        internal static PsdLayerType GetLayerType(this PsdLayer layer)
        {
            if (layer == null) return PsdLayerType.Unknown;
            if (layer.IsTextLayer()) return PsdLayerType.TextLayer;
            if (layer.IsSolidFillLayer()) return PsdLayerType.FillLayer;
            if (layer.IsGroup) return PsdLayerType.LayerGroup;
            return PsdLayerType.Layer;
        }

        internal static Rect GetLayerRect(this PsdLayer layer)
        {
            if (layer == null || layer.Document == null)
            {
                return default;
            }

            var canvasSize = new Vector2Int(layer.Document.Width, layer.Document.Height);
            return PsdRect2UnityRect(layer.Left, layer.Top, layer.Right, layer.Bottom, canvasSize);
        }

        internal static Rect PsdRect2UnityRect(int left, int top, int right, int bottom, Vector2Int canvasSize)
        {
            float halfWidth = Mathf.Abs(right - left) * 0.5f;
            float halfHeight = Mathf.Abs(bottom - top) * 0.5f;
            return new Rect(left + halfWidth - canvasSize.x * 0.5f, canvasSize.y - (top + halfHeight) - canvasSize.y * 0.5f, right - left, bottom - top);
        }

        internal static PsdLayer GetLayerByFlatIndex(this PsdDocument document, int flatIndex)
        {
            if (document == null || flatIndex < 0) return null;

            int nextIndex = 0;
            return FindByFlatIndex(document.Childs.OfType<PsdLayer>(), flatIndex, ref nextIndex);
        }

        private static PsdLayer FindByFlatIndex(IEnumerable<PsdLayer> layers, int targetIndex, ref int nextIndex)
        {
            if (layers == null) return null;

            foreach (var layer in layers)
            {
                var foundLayer = FindByFlatIndex(layer, targetIndex, ref nextIndex);
                if (foundLayer != null)
                {
                    return foundLayer;
                }
            }

            return null;
        }

        private static PsdLayer FindByFlatIndex(PsdLayer layer, int targetIndex, ref int nextIndex)
        {
            if (layer == null) return null;

            if (layer.IsGroup)
            {
                nextIndex++;
                var childLayer = FindByFlatIndex(layer.Childs, targetIndex, ref nextIndex);
                if (childLayer != null)
                {
                    return childLayer;
                }
            }

            if (nextIndex == targetIndex)
            {
                nextIndex++;
                return layer;
            }

            nextIndex++;
            return null;
        }

        internal static int CountAllLayers(this PsdDocument document)
        {
            if (document == null || document.Childs == null) return 0;
            return CountAllLayers(document.Childs.OfType<PsdLayer>());
        }

        internal static int CountFlatIndexSlots(this PsdLayer layer)
        {
            if (layer == null) return 0;
            return layer.IsGroup ? CountFlatIndexSlots(layer.Childs) + 2 : 1;
        }

        internal static int CountFlatIndexSlots(this PsdDocument document)
        {
            if (document == null || document.Childs == null) return 0;
            return CountFlatIndexSlots(document.Childs.OfType<PsdLayer>());
        }

        private static int CountAllLayers(IEnumerable<PsdLayer> layers)
        {
            int count = 0;
            if (layers == null) return count;

            foreach (var layer in layers)
            {
                count++;
                if (layer != null && layer.Childs != null && layer.Childs.Length > 0)
                {
                    count += CountAllLayers(layer.Childs);
                }
            }
            return count;
        }

        private static int CountFlatIndexSlots(IEnumerable<PsdLayer> layers)
        {
            int count = 0;
            if (layers == null) return count;

            foreach (var layer in layers)
            {
                count += layer.CountFlatIndexSlots();
            }
            return count;
        }
    }
}
#endif
