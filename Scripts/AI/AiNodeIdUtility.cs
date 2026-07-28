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
using System.Text;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiNodeIdUtility
    {
        internal static string GetNodeId(Psd2UIFormConverter converter, PsdLayerNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node.BindPsdLayerIndex >= 0)
            {
                return $"psd:{node.BindPsdLayerIndex}";
            }

            if (!string.IsNullOrWhiteSpace(node.GeneratedNodeId))
            {
                return node.GeneratedNodeId;
            }

            return BuildGeneratedNodeId(converter, node.transform);
        }

        internal static bool IsGeneratedGroupNode(PsdLayerNode node)
        {
            return node != null
                && node.BindPsdLayerIndex < 0
                && node.LayerType == PsdLayerType.LayerGroup;
        }

        private static string BuildGeneratedNodeId(Psd2UIFormConverter converter, Transform transform)
        {
            var builder = new StringBuilder(128);
            builder.Append("gen:");
            AppendTransformPath(builder, converter, transform);
            return builder.ToString();
        }

        private static void AppendTransformPath(StringBuilder builder, Psd2UIFormConverter converter, Transform transform)
        {
            if (builder == null || transform == null)
            {
                return;
            }

            if (transform.parent != null && (converter == null || transform.parent != converter.transform))
            {
                AppendTransformPath(builder, converter, transform.parent);
                builder.Append('/');
            }

            builder.Append(SanitizeSegment(transform.name));
            builder.Append('@');
            builder.Append(transform.GetSiblingIndex());
        }

        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "node";
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            }

            return builder.Length > 0 ? builder.ToString() : "node";
        }
    }
}
#endif
