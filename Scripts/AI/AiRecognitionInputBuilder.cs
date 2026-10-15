/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 */

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class AiRecognitionInputBuilder
    {
        internal bool Build(AiJobContext context, AiAnalysisPackageDocument package, string projectRoot,
            out AiRecognitionInputManifestDocument manifest, out string error)
        {
            manifest = null;
            error = null;
            try
            {
                string inputDirectory = !string.IsNullOrWhiteSpace(context.JobDirectory)
                    ? context.JobDirectory
                    : context.RequestDirectory;
                AiJobFileUtility.EnsureDirectory(inputDirectory);
                string nodePath = Path.Combine(inputDirectory, "nodes.tsv");
                var atlasIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var atlasPaths = new List<string>();
                var text = new StringBuilder(package.nodes.Count * 96);
                var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
                _ = new AiRenderOrderIndex(package.nodes);
                foreach (var node in package.nodes) sourceIds[node.id] = node.shortId;
                foreach (var node in package.nodes)
                {
                    string atlas = string.Empty;
                    if (node.atlas != null && !string.IsNullOrEmpty(node.atlas.page))
                    {
                        string path = ResolveImagePath(context.JobDirectory, projectRoot, package.document.imageRoot, node.atlas.page);
                        if (!atlasIndices.TryGetValue(path, out int page))
                        {
                            page = atlasPaths.Count;
                            atlasIndices.Add(path, page);
                            atlasPaths.Add(Path.GetFileName(path));
                        }
                        atlas = page + ":" + node.atlas.cell;
                    }
                    text.Append(node.shortId).Append('\t').Append(sourceIds.TryGetValue(node.parentId ?? string.Empty, out var parent) ? parent : string.Empty).Append('\t');
                    AppendCell(text, node.name);
                    text.Append('\t');
                    // Most imported layers keep the node name. Emit the source name only
                    // when it differs so the compact table preserves evidence without doubling it.
                    if (!string.Equals(node.name, node.layerName, StringComparison.Ordinal))
                        AppendCell(text, node.layerName);
                    text.Append('\t')
                        .Append(node.isTextLayer ? "text" : node.isGroupLayer ? "group" : "image")
                        .Append('\t').Append(node.leafStart).Append(',').Append(node.leafEnd)
                        .Append('\t')
                        .Append(node.rect.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(node.rect.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(node.rect.w.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(node.rect.h.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(atlas).Append('\t');
                    AppendCell(text, Path.GetFileName(node.imageFile));
                    text.Append('\t').Append(node.active ? '1' : '0').Append('\t');
                    AppendCell(text, node.text);
                    text.Append('\t');
                    AppendCell(text, string.IsNullOrEmpty(node.referenceKind) ? string.Empty : node.referenceKind + " " + node.referenceKey);
                    text.Append('\t');
                    if (node.isTextLayer && node.textStyle != null && node.textStyle.IsComplete) AppendCell(text, UnityEngine.JsonUtility.ToJson(node.textStyle));
                    text.Append('\n');
                }
                AiJobFileUtility.WriteText(nodePath, text.ToString());
                string imageRoot = string.IsNullOrWhiteSpace(package.document.imageRoot) ? "img" : package.document.imageRoot;
                string cliPreviewPath = PrepareCliImage(package.document.previewImagePath, imageRoot, context.JobDirectory, projectRoot, "preview", out float previewScale);
                string cliAnnotatedPreviewPath = PrepareCliImage(package.document.annotatedPreviewImagePath, imageRoot, context.JobDirectory, projectRoot, "annotated", out _);
                manifest = new AiRecognitionInputManifestDocument
                {
                    version = AiProtocolVersions.RecognitionInput,
                    document = new AiRecognitionInputDocumentInfo
                    {
                        imageRoot = imageRoot,
                        previewImagePath = cliPreviewPath,
                        annotatedPreviewImagePath = cliAnnotatedPreviewPath,
                        width = package.document.width, height = package.document.height,
                        previewScale = previewScale
                    },
                    nodeCount = package.nodes.Count,
                    nodeFilePath = Path.GetFileName(nodePath),
                    atlasPaths = atlasPaths.ToArray(),
                    helperContracts = BuildContracts()
                };
                AiJobFileUtility.WriteText(context.RecognitionInputManifestPath, FormatManifestJson(UnityEngine.JsonUtility.ToJson(manifest)));
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to build recognition input: {ex.Message}";
                return false;
            }
        }

        // Keep records compact, but avoid single-line JSON being clipped by CLI read tools.
        internal static string FormatManifestJson(string json)
        {
            var text = new StringBuilder(json.Length + 128);
            int depth = 0;
            bool quoted = false, escaped = false;
            foreach (char c in json)
            {
                text.Append(c);
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth <= 2) text.Append('\n');
            }
            return text.ToString();
        }

        private static void AppendCell(StringBuilder text, string value)
        {
            if (value == null) return;
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': text.Append("\\\\"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\n': text.Append("\\n"); break;
                    default: text.Append(c); break;
                }
            }
        }

        private static string PrepareCliImage(string sourcePath, string imageRoot, string jobDirectory, string projectRoot, string purpose, out float scale)
        {
            scale = 1f;
            if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;
            string fullSourcePath = ResolveImagePath(jobDirectory, projectRoot, imageRoot, sourcePath);
            string sourceName = Path.GetFileName(sourcePath);
            if (!File.Exists(fullSourcePath)) return sourceName;
            string outputName = purpose == "preview" ? "preview-cli.png" : "annotated-cli.png";
            string imageDirectory = ResolveImageRoot(jobDirectory, projectRoot, imageRoot);
            string outputPath = Path.Combine(imageDirectory, outputName);
            try
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!source.LoadImage(File.ReadAllBytes(fullSourcePath), false) || source.width <= 2048 && source.height <= 2048)
                    {
                        AiJobFileUtility.DeleteFileIfExists(outputPath);
                        return sourceName;
                    }
                    float resizeScale = 2048f / Mathf.Max(source.width, source.height);
                    scale = resizeScale;
                    int width = Mathf.Max(1, Mathf.RoundToInt(source.width * resizeScale));
                    int height = Mathf.Max(1, Mathf.RoundToInt(source.height * resizeScale));
                    var pixels = new Color32[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        float v = (y + 0.5f) / height;
                        for (int x = 0; x < width; x++)
                            pixels[y * width + x] = source.GetPixelBilinear((x + 0.5f) / width, v);
                    }
                    var resized = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                    try
                    {
                        resized.SetPixels32(pixels);
                        resized.Apply(false, false);
                        File.WriteAllBytes(outputPath, resized.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(resized); }
                }
                finally { UnityEngine.Object.DestroyImmediate(source); }
                return outputName;
            }
            catch (Exception)
            {
                // The original image remains a valid input if an editor image codec is unavailable.
                return sourceName;
            }
        }

        internal static string[] ResolveImageInputs(AiRecognitionInputManifestDocument manifest, string workingDirectory)
        {
            if (manifest == null || manifest.document == null) return Array.Empty<string>();
            var result = new List<string>(1 + (manifest.atlasPaths != null ? manifest.atlasPaths.Length : 0));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddImage(manifest.document.previewImagePath);
            if (manifest.atlasPaths != null)
                foreach (string path in manifest.atlasPaths) AddImage(path);
            return result.ToArray();

            void AddImage(string path)
            {
                string fullPath = ResolveImagePath(workingDirectory,
                    Directory.GetParent(Application.dataPath).FullName,
                    manifest.document.imageRoot,
                    path);
                if (seen.Add(fullPath))
                {
                    if (!File.Exists(fullPath)) throw new FileNotFoundException("Recognition image is missing.", fullPath);
                    result.Add(fullPath);
                }
            }
        }

        private static string ResolveImageRoot(string workingDirectory, string projectRoot, string imageRoot)
        {
            if (string.IsNullOrWhiteSpace(imageRoot)) return workingDirectory;
            if (Path.IsPathRooted(imageRoot)) return imageRoot;
            if (imageRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || imageRoot.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(projectRoot, imageRoot);
            return Path.Combine(workingDirectory, imageRoot);
        }

        private static string ResolveImagePath(string workingDirectory, string projectRoot, string imageRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
            if (Path.IsPathRooted(relativePath)) return Path.GetFullPath(relativePath);
            if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            return Path.GetFullPath(Path.Combine(ResolveImageRoot(workingDirectory, projectRoot, imageRoot), relativePath));
        }

        private static List<AiRecognitionHelperContract> BuildContracts()
        {
            var result = new List<AiRecognitionHelperContract>();
            foreach (GUIType type in AiUiContract.MainTypes)
            {
                var allowed = AiUiContract.Roles(type);
                var textRoles = new List<string>();
                foreach (var role in allowed) if (AiUiContract.IsText(role)) textRoles.Add(role.ToString());
                result.Add(new AiRecognitionHelperContract
                {
                    ownerType = type.ToString(),
                    allowedRoles = Array.ConvertAll(allowed, x => x.ToString()),
                    textRoles = textRoles.ToArray()
                });
            }
            return result;
        }
    }
}
#endif
