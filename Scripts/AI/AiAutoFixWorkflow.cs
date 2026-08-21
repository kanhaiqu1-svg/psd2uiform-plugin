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
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiAutoFixWorkflow
    {
        private sealed class InactiveSourceNodePreservationScope : IDisposable
        {
            private sealed class Entry
            {
                public Transform transform;
                public Transform originalParent;
                public string originalParentNodeId;
                public int originalSiblingIndex;
                public int originalDepth;
            }

            private readonly Psd2UIFormConverter converter;
            private readonly AiJobContext context;
            private readonly GameObject stashRoot;
            private readonly List<Entry> entries;
            private bool restored;

            private InactiveSourceNodePreservationScope(
                Psd2UIFormConverter converter,
                AiJobContext context,
                GameObject stashRoot,
                List<Entry> entries)
            {
                this.converter = converter;
                this.context = context;
                this.stashRoot = stashRoot;
                this.entries = entries;
            }

            internal static InactiveSourceNodePreservationScope Create(Psd2UIFormConverter converter, AiJobContext context)
            {
                if (converter == null)
                {
                    return null;
                }

                var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                if (nodes == null || nodes.Length < 1)
                {
                    return null;
                }

                List<Entry> entries = null;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node == null
                        || node.transform == null
                        || node.gameObject == null
                        || node.gameObject.activeSelf
                        || node.BindPsdLayerIndex < 0
                        || HasInactiveSourceNodeAncestor(node.transform, converter.transform))
                    {
                        continue;
                    }

                    if (entries == null)
                    {
                        entries = new List<Entry>(8);
                    }

                    entries.Add(new Entry
                    {
                        transform = node.transform,
                        originalParent = node.transform.parent,
                        originalParentNodeId = ResolveNodeId(converter, node.transform.parent),
                        originalSiblingIndex = node.transform.GetSiblingIndex(),
                        originalDepth = GetTransformDepth(node.transform)
                    });
                }

                if (entries == null || entries.Count < 1)
                {
                    return null;
                }

                entries.Sort((left, right) =>
                {
                    int depthCompare = left.originalDepth.CompareTo(right.originalDepth);
                    if (depthCompare != 0)
                    {
                        return depthCompare;
                    }

                    return left.originalSiblingIndex.CompareTo(right.originalSiblingIndex);
                });

                var stashRoot = new GameObject("__PSD2UIForm_AI_InactiveNodeStash");
                stashRoot.hideFlags = HideFlags.HideAndDontSave;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry?.transform == null)
                    {
                        continue;
                    }

                    entry.transform.SetParent(stashRoot.transform, true);
                }

                AiJobFileUtility.AppendDebugLog(context, $"Temporarily detached {entries.Count} inactive source-bound node roots before local structure normalization.");
                return new InactiveSourceNodePreservationScope(converter, context, stashRoot, entries);
            }

            public void Dispose()
            {
                Restore();
            }

            internal void Restore()
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                if (entries != null)
                {
                    entries.Sort((left, right) =>
                    {
                        int depthCompare = left.originalDepth.CompareTo(right.originalDepth);
                        if (depthCompare != 0)
                        {
                            return depthCompare;
                        }

                        return left.originalSiblingIndex.CompareTo(right.originalSiblingIndex);
                    });

                    int restoredCount = 0;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (entry?.transform == null)
                        {
                            continue;
                        }

                        var targetParent = ResolveRestoreParent(entry);
                        if (targetParent == null)
                        {
                            continue;
                        }

                        entry.transform.SetParent(targetParent, true);
                        entry.transform.SetSiblingIndex(Mathf.Clamp(entry.originalSiblingIndex, 0, Mathf.Max(0, targetParent.childCount - 1)));
                        if (entry.transform.gameObject != null && entry.transform.gameObject.activeSelf)
                        {
                            entry.transform.gameObject.SetActive(false);
                        }

                        restoredCount++;
                    }

                    if (restoredCount > 0)
                    {
                        AiJobFileUtility.AppendDebugLog(context, $"Restored {restoredCount} inactive source-bound node roots after local structure normalization.");
                    }
                }

                if (stashRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(stashRoot);
                }
            }

            private Transform ResolveRestoreParent(Entry entry)
            {
                if (entry == null)
                {
                    return converter != null ? converter.transform : null;
                }

                if (entry.originalParent != null)
                {
                    return entry.originalParent;
                }

                var resolvedParent = TryResolveNodeTransform(converter, entry.originalParentNodeId);
                return resolvedParent != null ? resolvedParent : (converter != null ? converter.transform : null);
            }

            private static bool HasInactiveSourceNodeAncestor(Transform transform, Transform stopAt)
            {
                if (transform == null)
                {
                    return false;
                }

                var current = transform.parent;
                while (current != null && current != stopAt)
                {
                    var node = current.GetComponent<PsdLayerNode>();
                    if (node != null && !node.gameObject.activeSelf && node.BindPsdLayerIndex >= 0)
                    {
                        return true;
                    }

                    current = current.parent;
                }

                return false;
            }

            private static int GetTransformDepth(Transform transform)
            {
                int depth = 0;
                var current = transform;
                while (current != null)
                {
                    depth++;
                    current = current.parent;
                }

                return depth;
            }

            private static string ResolveNodeId(Psd2UIFormConverter converter, Transform transform)
            {
                if (converter == null || transform == null)
                {
                    return string.Empty;
                }

                var node = transform.GetComponent<PsdLayerNode>();
                return node != null ? AiNodeIdUtility.GetNodeId(converter, node) : string.Empty;
            }

            private static Transform TryResolveNodeTransform(Psd2UIFormConverter converter, string nodeId)
            {
                if (converter == null || string.IsNullOrWhiteSpace(nodeId))
                {
                    return null;
                }

                var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node == null || node.transform == null)
                    {
                        continue;
                    }

                    if (string.Equals(AiNodeIdUtility.GetNodeId(converter, node), nodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return node.transform;
                    }
                }

                return null;
            }
        }

        private sealed class Listener : IAiJobListener
        {
            private readonly Psd2UIFormConverter converter;

            internal Listener(Psd2UIFormConverter converter)
            {
                this.converter = converter;
            }

            public void OnAiJobCompleted(AiJobContext context)
            {
                if (converter == null)
                {
                    return;
                }

                if (!TryLoadPatchForConverter(converter, context, out var patch, out var loadError))
                {
                    EditorUtility.DisplayDialog("AI修正失败", loadError, "确定");
                    return;
                }

                string summary = BuildPatchSummary(patch, context);
                if (patch == null || patch.operations == null || patch.operations.Count < 1)
                {
                    EditorUtility.DisplayDialog("AI识别完成", summary, "确定");
                    return;
                }

                if (!EditorUtility.DisplayDialog("AI识别完成", summary, "应用识别结果", "取消"))
                {
                    return;
                }

                if (!TryApplyPatch(converter, context, patch, true, out var applyError))
                {
                    EditorUtility.DisplayDialog("AI修正失败", applyError, "确定");
                    return;
                }
            }

            public void OnAiJobFailed(AiJobContext context, string errorMessage)
            {
                if (!string.IsNullOrWhiteSpace(errorMessage)
                    && errorMessage.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EditorUtility.DisplayDialog("AI任务已取消", "当前 AI 自动识别修正任务已取消。", "确定");
                    return;
                }

                EditorUtility.DisplayDialog("AI修正失败", string.IsNullOrWhiteSpace(errorMessage) ? "未知错误" : errorMessage, "确定");
            }

            private static string BuildPatchSummary(AiPatchDocument patch, AiJobContext context)
            {
                int analysisCount = patch?.analysis?.Count ?? 0;
                int operationCount = patch?.operations?.Count ?? 0;
                float sumConfidence = 0f;
                float minConfidence = 1f;
                int counted = 0;

                if (patch?.analysis != null)
                {
                    for (int i = 0; i < patch.analysis.Count; i++)
                    {
                        var entry = patch.analysis[i];
                        if (entry == null)
                        {
                            continue;
                        }

                        if (!string.Equals(entry.semanticKind, "owner", StringComparison.Ordinal)
                            && !string.Equals(entry.semanticKind, "role", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        sumConfidence += entry.confidence;
                        minConfidence = Math.Min(minConfidence, entry.confidence);
                        counted++;
                    }
                }

                float avgConfidence = counted > 0 ? (sumConfidence / counted) : 0f;
                if (counted <= 0)
                {
                    minConfidence = 0f;
                }

                return
                    $"AI 已完成识别。\n\n" +
                    $"识别节点数: {analysisCount}\n" +
                    $"修正操作数: {operationCount}\n" +
                    $"平均置信度: {(avgConfidence * 100f):0.#}%\n" +
                    $"最低置信度: {(minConfidence * 100f):0.#}%\n" +
                    $"以上置信度由 AI 自行评估，仅供参考。\n\n" +
                    $"应用结果时将自动执行本地结构归一化。\n\n" +
                    $"结果文件:\n{context.PatchPath}\n\n" +
                    (operationCount > 0
                        ? "是否应用本次 AI 修正结果？"
                        : "AI 未发现需要应用的结构修正。");
            }
        }

        private static readonly AiJobManager JobManager = new AiJobManager();

        internal static bool Start(Psd2UIFormConverter converter, out string error)
        {
            error = null;
            if (converter == null)
            {
                error = "Converter is null.";
                return false;
            }

            var parser = UGUIParser.Instance;
            if (parser == null)
            {
                error = "Psd2UIFormConfig 未加载。";
                return false;
            }

            if (JobManager.HasRunningJobs)
            {
                error = "已有 AI 任务正在执行，请等待当前任务完成后再试。";
                return false;
            }

            var provider = AiProviderFactory.Create(parser);
            if (provider == null)
            {
                error = "当前 AI Provider 配置无效。";
                return false;
            }

            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string jobsRoot = ResolveJobRoot(projectRoot);
            var context = JobManager.CreateJob(provider.ProviderId, jobsRoot, converter.PsdAssetName);

            var builder = new AiAnalysisBundleBuilder();
            if (!builder.Build(converter, context, out var treeHash, out error))
            {
                return false;
            }

            var meta = new AiJobMetaDocument
            {
                jobId = context.JobId,
                providerId = provider.ProviderId,
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                projectPath = projectRoot.Replace("\\", "/"),
                psdAssetPath = converter.PsdAssetName ?? string.Empty,
                converterPath = AssetDatabase.GetAssetPath(converter.gameObject) ?? string.Empty,
                analysisPackageVersion = AiProtocolVersions.AnalysisPackage,
                recognitionCombinedVersion = AiProtocolVersions.RecognitionCombined,
                mainTypeVersion = AiProtocolVersions.MainType,
                childRelationVersion = AiProtocolVersions.ChildRelation,
                structuralVersion = AiProtocolVersions.Structural,
                patchVersion = AiProtocolVersions.Patch,
                treeHash = treeHash,
                previewHash = string.Empty
            };
            string combinedRecognitionPromptTemplatePath = ResolveCombinedRecognitionPromptTemplatePath(projectRoot);
            bool allowVisibleCliExecution = ShouldAllowVisibleCliExecution(provider, parser.AiProviderConfig);

            AiJobFileUtility.WriteJson(context.MetaPath, meta);
                AiJobFileUtility.AppendDebugLog(context, $"Workflow prepared. provider={provider.ProviderId}, projectRoot={projectRoot}, recognitionPrompt={combinedRecognitionPromptTemplatePath}");
            JobManager.StartJob(context, provider, new Listener(converter), (jobContext, cancellationToken) =>
            {
                ExecuteAiWorkflow(jobContext, provider, projectRoot, cancellationToken, combinedRecognitionPromptTemplatePath, allowVisibleCliExecution);
            });
            return true;
        }

        internal static bool ApplyCurrentResult(Psd2UIFormConverter converter, out string error)
        {
            return ApplyCurrentResult(converter, true, out error);
        }

        internal static bool ApplyCurrentResult(Psd2UIFormConverter converter, bool normalizeAfterApply, out string error)
        {
            error = null;
            if (converter == null)
            {
                error = "Converter is null.";
                return false;
            }

            if (JobManager.HasRunningJobs)
            {
                error = "已有 AI 任务正在执行，请等待当前任务完成后再试。";
                return false;
            }

            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var context = CreateLatestJobContext(projectRoot, converter.PsdAssetName);
            if (!TryLoadPatchForConverter(converter, context, out var patch, out error))
            {
                return false;
            }

            if (patch == null || patch.operations == null || patch.operations.Count < 1)
            {
                error = "当前 AI 结果没有需要应用的修正操作。";
                return false;
            }

            return TryApplyPatch(converter, context, patch, normalizeAfterApply, out error);
        }

        internal static bool RunBlocking(Psd2UIFormConverter converter, bool applyPatch, bool normalizeAfterApply, out AiJobContext context, out string error, string providerIdOverride = null)
        {
            context = null;
            error = null;
            if (converter == null)
            {
                error = "Converter is null.";
                return false;
            }

            var parser = UGUIParser.Instance;
            if (parser == null)
            {
                error = "Psd2UIFormConfig 未加载。";
                return false;
            }

            if (JobManager.HasRunningJobs)
            {
                error = "已有 AI 任务正在执行，请等待当前任务完成后再试。";
                return false;
            }

            var provider = string.IsNullOrWhiteSpace(providerIdOverride)
                ? AiProviderFactory.Create(parser)
                : AiProviderFactory.CreateById(providerIdOverride);
            if (provider == null)
            {
                error = string.IsNullOrWhiteSpace(providerIdOverride)
                    ? "当前 AI Provider 配置无效。"
                    : $"AI Provider override 无效: {providerIdOverride}";
                return false;
            }

            if (!TryCreateBlockingContext(converter, provider, out context, out var projectRoot, out var package, out _, out error))
            {
                return false;
            }
            string combinedRecognitionPromptTemplatePath = ResolveCombinedRecognitionPromptTemplatePath(projectRoot);
            bool allowVisibleCliExecution = ShouldAllowVisibleCliExecution(provider, parser.AiProviderConfig);
            AiJobFileUtility.AppendDebugLog(context, $"Blocking workflow prepared. provider={provider.ProviderId}, projectRoot={projectRoot}, recognitionPrompt={combinedRecognitionPromptTemplatePath}");

            DateTime startUtc = DateTime.UtcNow;
            try
            {
                AiJobFileUtility.WriteStatus(context, AiJobState.Running, "Blocking AI workflow started.");
                ExecuteAiWorkflow(context, provider, projectRoot, System.Threading.CancellationToken.None, combinedRecognitionPromptTemplatePath, allowVisibleCliExecution, package);

                if (applyPatch)
                {
                    if (!TryLoadPatchForConverter(converter, context, out var patch, out error))
                    {
                        WriteBlockingFailureResult(context, startUtc, error);
                        return false;
                    }

                    if (!TryApplyPatch(converter, context, patch, normalizeAfterApply, out error))
                    {
                        WriteBlockingFailureResult(context, startUtc, error);
                        return false;
                    }
                }

                WriteBlockingSuccessResult(context, startUtc);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AiJobFileUtility.WriteText(context.ErrorPath, ex.ToString());
                WriteBlockingFailureResult(context, startUtc, error);
                AiJobFileUtility.AppendDebugLog(context, $"Blocking AI workflow failed. exception={ex}");
                return false;
            }
        }

        private static string ResolveJobRoot(string projectRoot)
        {
            return AiPathDefaults.ToAbsoluteProjectPath(projectRoot, AiPathDefaults.JobsRootRelative);
        }

        private static AiJobContext CreateLatestJobContext(string projectRoot, string psdAssetPath)
        {
            string jobDirectory = AiJobManager.ResolveLatestScopedJobDirectory(ResolveJobRoot(projectRoot), psdAssetPath);
            string requestDirectory = Path.Combine(jobDirectory, "request");
            string responseDirectory = Path.Combine(jobDirectory, "response");
            return new AiJobContext
            {
                JobDirectory = jobDirectory,
                RequestDirectory = requestDirectory,
                ResponseDirectory = responseDirectory,
                MetaPath = Path.Combine(jobDirectory, "meta.json"),
                AnalysisPackagePath = Path.Combine(requestDirectory, "analysis-package.json"),
                RecognitionCombinedPath = Path.Combine(responseDirectory, "combined-recognition.json"),
                RecognitionCombinedTempPath = Path.Combine(responseDirectory, "combined-recognition.json.tmp"),
                MainTypePath = Path.Combine(responseDirectory, "main-type.json"),
                MainTypeTempPath = Path.Combine(responseDirectory, "main-type.json.tmp"),
                ChildRelationPath = Path.Combine(responseDirectory, "child-relation.json"),
                ChildRelationTempPath = Path.Combine(responseDirectory, "child-relation.json.tmp"),
                StructuralPath = Path.Combine(responseDirectory, "structural.json"),
                StructuralTempPath = Path.Combine(responseDirectory, "structural.json.tmp"),
                OwnerScopeManifestPath = Path.Combine(requestDirectory, "owner-scope-manifest.json"),
                RecognitionInputManifestPath = Path.Combine(requestDirectory, "recognition-input", "manifest.json"),
                RecognitionNodeShardDirectory = Path.Combine(requestDirectory, "recognition-input", "nodes"),
                PromptPath = Path.Combine(requestDirectory, "prompt.md"),
                StatusPath = Path.Combine(responseDirectory, "status.json"),
                ResultPath = Path.Combine(responseDirectory, "result.json"),
                PatchPath = Path.Combine(responseDirectory, "patch.json"),
                PatchTempPath = Path.Combine(responseDirectory, "patch.json.tmp"),
                RawOutputPath = Path.Combine(responseDirectory, "raw-output.txt"),
                CompletedPath = Path.Combine(responseDirectory, "Completed"),
                StreamOutputPath = Path.Combine(responseDirectory, "stream-output.jsonl"),
                VisibleCliCompletionPath = Path.Combine(responseDirectory, "visible-cli-completed.json"),
                ErrorPath = Path.Combine(responseDirectory, "error.txt"),
                DebugLogPath = Path.Combine(responseDirectory, "debug-log.txt")
            };
        }

        private static string ResolveCombinedRecognitionPromptTemplatePath(string projectRoot)
        {
            return AiPathDefaults.ToAbsolutePluginPath(projectRoot, AiPathDefaults.CombinedRecognitionPromptTemplateRelative);
        }

        private static string ResolveMainTypePromptTemplatePath(string projectRoot)
        {
            return AiPathDefaults.ToAbsolutePluginPath(projectRoot, AiPathDefaults.MainTypePromptTemplateRelative);
        }

        private static string ResolveChildRelationPromptTemplatePath(string projectRoot)
        {
            return AiPathDefaults.ToAbsolutePluginPath(projectRoot, AiPathDefaults.ChildRelationPromptTemplateRelative);
        }

        private static string ResolveCombinedRecognitionPromptPath(AiJobContext context)
        {
            return Path.Combine(context.RequestDirectory, "prompt-combined-recognition.md");
        }

        private static string ResolveMainTypePromptPath(AiJobContext context)
        {
            return Path.Combine(context.RequestDirectory, "prompt-main-type.md");
        }

        private static string ResolveChildRelationPromptPath(AiJobContext context)
        {
            return Path.Combine(context.RequestDirectory, "prompt-child-relation.md");
        }

        private static string[] ResolveImageInputs(AiJobContext context, string projectRoot)
        {
            if (!TryLoadAnalysisPackage(context, out var package))
            {
                return Array.Empty<string>();
            }

            var imagePaths = new List<string>(2);
            AddImageInput(context, imagePaths, projectRoot, package.document.annotatedPreviewImagePath);
            if (imagePaths.Count == 0)
            {
                AddImageInput(context, imagePaths, projectRoot, package.document.previewImagePath);
            }

            return imagePaths.ToArray();
        }

        private static bool TryLoadAnalysisPackage(AiJobContext context, out AiAnalysisPackageDocument package)
        {
            package = null;
            return context != null
                && !string.IsNullOrWhiteSpace(context.AnalysisPackagePath)
                && AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out package)
                && package != null
                && package.document != null;
        }

        private static AiPromptTag[] BuildCommonPromptTags(
            AiJobContext context,
            string projectRoot,
            params AiPromptTag[] extraTags)
        {
            if (!TryLoadAnalysisPackage(context, out var package))
            {
                return extraTags ?? Array.Empty<AiPromptTag>();
            }

            var tags = new List<AiPromptTag>(8 + (extraTags != null ? extraTags.Length : 0))
            {
                new AiPromptTag { key = "[RECOGNITION_INPUT_MANIFEST_PATH]", value = NormalizePromptPath(context.RecognitionInputManifestPath) },
                new AiPromptTag { key = "[RECOGNITION_NODE_SHARD_GLOB_PATH]", value = NormalizePromptGlobPath(context.RecognitionNodeShardDirectory, "*.json") },
                new AiPromptTag { key = "[PREVIEW_IMAGE_PATH]", value = ResolvePromptPath(context, projectRoot, package.document.previewImagePath) },
                new AiPromptTag { key = "[ANNOTATED_PREVIEW_IMAGE_PATH]", value = ResolvePromptPath(context, projectRoot, package.document.annotatedPreviewImagePath) },
                new AiPromptTag { key = "[NODE_ATLAS_GLOB_PATH]", value = ResolvePromptGlobPath(context, projectRoot, package.document.nodeAtlasDirectoryPath) },
                new AiPromptTag { key = "[NODE_PREVIEW_GLOB_PATH]", value = ResolvePromptGlobPath(context, projectRoot, package.document.nodePreviewDirectoryPath) }
            };

            if (extraTags != null && extraTags.Length > 0)
            {
                for (int i = 0; i < extraTags.Length; i++)
                {
                    var tag = extraTags[i];
                    if (tag == null || string.IsNullOrWhiteSpace(tag.key))
                    {
                        continue;
                    }

                    tags.Add(tag);
                }
            }

            return tags.ToArray();
        }

        private static string ResolvePromptPath(AiJobContext context, string projectRoot, string packagePath)
        {
            string resolved = ResolveBundlePath(context != null ? context.JobDirectory : string.Empty, projectRoot, packagePath);
            return NormalizePromptPath(resolved);
        }

        private static string ResolvePromptGlobPath(AiJobContext context, string projectRoot, string packageDirectoryPath)
        {
            string resolved = ResolveBundlePath(context != null ? context.JobDirectory : string.Empty, projectRoot, packageDirectoryPath);
            return NormalizePromptGlobPath(resolved, "*.png");
        }

        private static string NormalizePromptGlobPath(string absoluteDirectoryPath, string pattern)
        {
            if (string.IsNullOrWhiteSpace(absoluteDirectoryPath))
            {
                return string.Empty;
            }

            string directoryPath = NormalizePromptPath(absoluteDirectoryPath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return string.Empty;
            }

            string suffix = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
            return directoryPath.TrimEnd('/', '\\') + "/" + suffix.TrimStart('/', '\\');
        }

        private static string NormalizePromptPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(fullPath).Replace("\\", "/");
            }
            catch
            {
                return fullPath.Replace("\\", "/");
            }
        }

        private static void AddImageInput(AiJobContext context, List<string> imagePaths, string projectRoot, string packagePath)
        {
            if (context == null || imagePaths == null || string.IsNullOrWhiteSpace(packagePath)) return;

            string resolved = ResolveBundlePath(context.JobDirectory, projectRoot, packagePath);
            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved)) return;

            for (int i = 0; i < imagePaths.Count; i++)
            {
                if (string.Equals(imagePaths[i], resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            imagePaths.Add(resolved);
        }

        private static string ResolveBundlePath(string jobDirectory, string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(projectRoot, path);
            }

            return Path.GetFullPath(Path.Combine(jobDirectory, path));
        }

        private static bool TryCreateBlockingContext(
            Psd2UIFormConverter converter,
            IAiProvider provider,
            out AiJobContext context,
            out string projectRoot,
            out AiAnalysisPackageDocument package,
            out string treeHash,
            out string error)
        {
            context = null;
            projectRoot = null;
            package = null;
            treeHash = string.Empty;
            error = null;
            try
            {
                projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                string jobsRoot = ResolveJobRoot(projectRoot);
                var localJobManager = new AiJobManager();
                context = localJobManager.CreateJob(provider.ProviderId, jobsRoot, converter.PsdAssetName);

                var builder = new AiAnalysisBundleBuilder();
                if (!builder.Build(converter, context, out treeHash, out error))
                {
                    return false;
                }

                var meta = new AiJobMetaDocument
                {
                    jobId = context.JobId,
                    providerId = provider.ProviderId,
                    createdAtUtc = DateTime.UtcNow.ToString("o"),
                    projectPath = projectRoot.Replace("\\", "/"),
                    psdAssetPath = converter.PsdAssetName ?? string.Empty,
                    converterPath = AssetDatabase.GetAssetPath(converter.gameObject) ?? string.Empty,
                    analysisPackageVersion = AiProtocolVersions.AnalysisPackage,
                    recognitionCombinedVersion = AiProtocolVersions.RecognitionCombined,
                    mainTypeVersion = AiProtocolVersions.MainType,
                    childRelationVersion = AiProtocolVersions.ChildRelation,
                    structuralVersion = AiProtocolVersions.Structural,
                    patchVersion = AiProtocolVersions.Patch,
                    treeHash = treeHash,
                    previewHash = string.Empty
                };
                AiJobFileUtility.WriteJson(context.MetaPath, meta);
                if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out package) || package == null)
                {
                    error = "AI 分析包读取失败。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to prepare AI workflow context: {ex.Message}";
                return false;
            }
        }

        private static void ExecuteAiWorkflow(
            AiJobContext context,
            IAiProvider provider,
            string projectRoot,
            System.Threading.CancellationToken cancellationToken,
            string combinedRecognitionPromptTemplatePath,
            bool allowVisibleCliExecution,
            AiAnalysisPackageDocument package = null)
        {
            if (package == null && (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out package) || package == null))
            {
                throw new InvalidOperationException("AI 分析包读取失败。");
            }

            string[] imageInputs = ResolveImageInputs(context, projectRoot);
            PrepareRecognitionArtifacts(context);
            BuildRecognitionInputs(context, package, projectRoot);

            WriteRenderedCombinedRecognitionPrompt(context, projectRoot, combinedRecognitionPromptTemplatePath);
            string recognitionRawOutputPath = ResolveRecognitionStageRawOutputPath(context, "recognition");
            string recognitionCompletedPath = ResolveRecognitionStageCompletedPath(context, "recognition");
            string recognitionStreamOutputPath = ResolveRecognitionStageStreamOutputPath(context, "recognition");
            string recognitionVisibleCliCompletionPath = ResolveRecognitionStageVisibleCliCompletionPath(context, "recognition");

            AiJobFileUtility.WriteStatusStage(context, "AI正在识别UI元素类型");
            AiJobFileUtility.WriteStatusDetail(context, "AI任务进程: UI元素识别阶段执行中");
            var recognitionRequest = new AiAnalysisRequest
            {
                StageName = "recognition-combined",
                StageDisplayName = "AI正在识别UI元素类型",
                ResultDocumentKind = AiResultDocumentKind.RecognitionCombined,
                AnalysisPackageJsonPath = context.RecognitionInputManifestPath,
                PromptTemplatePath = ResolveCombinedRecognitionPromptPath(context),
                PromptTags = null,
                OutputJsonPath = context.RecognitionCombinedPath,
                OutputTempJsonPath = context.RecognitionCombinedTempPath,
                OutputRawTextPath = recognitionRawOutputPath,
                OutputCompletedPath = recognitionCompletedPath,
                StreamOutputPath = recognitionStreamOutputPath,
                VisibleCliCompletionPath = recognitionVisibleCliCompletionPath,
                WorkingDirectory = projectRoot,
                ImageInputPaths = imageInputs,
                AllowVisibleCliExecution = allowVisibleCliExecution,
                RequireExplicitOutputJsonFile = true,
                DisableOutputRecovery = true,
                CancellationToken = cancellationToken
            };

            var recognitionLoader = new AiRecognitionResultLoader();
            AiJobFileUtility.AppendDebugLog(context, $"Stage combined-recognition begin. prompt={recognitionRequest.PromptTemplatePath}, output={recognitionRequest.OutputJsonPath}, visibleCli={recognitionRequest.AllowVisibleCliExecution}");
            provider.ExecuteJob(context, recognitionRequest);
            MirrorStageRawOutput(recognitionRawOutputPath, context.RawOutputPath);
            CommitRecognitionArtifact(context, AiResultDocumentKind.RecognitionCombined, context.RecognitionCombinedTempPath, context.RecognitionCombinedPath, "combined-recognition");

            AiJobFileUtility.WriteStatusDetail(context, "AI任务进程: 读取 owner-first 识别语义图");
            if (!recognitionLoader.TryLoadCombinedRecognition(context, package, out var combinedResult, out var combinedError))
            {
                throw new InvalidOperationException(combinedError);
            }

            if (!recognitionLoader.TrySplitCombinedRecognition(package, combinedResult, out var mainTypeResult, out var childRelationResult, out var splitError))
            {
                throw new InvalidOperationException(splitError);
            }

            AiJobFileUtility.WriteJson(context.MainTypePath, mainTypeResult);
            AiJobFileUtility.WriteJson(context.ChildRelationPath, childRelationResult);
            AiJobFileUtility.AppendDebugLog(context, $"Combined recognition split completed. nodeCount={(mainTypeResult.nodes != null ? mainTypeResult.nodes.Count : 0)}, relationCount={(childRelationResult.relations != null ? childRelationResult.relations.Count : 0)}");

            AiJobFileUtility.WriteStatusStage(context, "AI识别完成, 开始本地结构矫正与Patch合成");
            AiJobFileUtility.WriteStatusDetail(context, "AI识别完成, 开始本地结构矫正与Patch合成");
            var synthesizer = new AiRecognitionPatchSynthesizer();
            if (!synthesizer.TryBuildPatch(package, combinedResult, out var patch, out var synthError))
            {
                throw new InvalidOperationException(synthError);
            }

            if (patch == null || patch.analysis == null || patch.analysis.Count < 1)
            {
                throw new InvalidOperationException("AI 识别结果无效：未生成任何分析项，通常表示识别结果 JSON 协议不匹配或关键 owner/role 识别失败。");
            }

            AiJobFileUtility.WriteJson(context.PatchPath, patch);
            AiJobFileUtility.AppendDebugLog(context, $"Local patch synthesized. analysisCount={(patch.analysis != null ? patch.analysis.Count : 0)}, operationCount={(patch.operations != null ? patch.operations.Count : 0)}");
        }

        private static void BuildRecognitionInputs(AiJobContext context, AiAnalysisPackageDocument package, string projectRoot)
        {
            var inputBuilder = new AiRecognitionInputBuilder();
            if (!inputBuilder.Build(context, package, projectRoot, out var error))
            {
                throw new InvalidOperationException(error);
            }

            AiJobFileUtility.AppendDebugLog(context, $"Recognition input built. manifest={context.RecognitionInputManifestPath}, nodeShards={context.RecognitionNodeShardDirectory}");
        }

        private static void WriteRenderedCombinedRecognitionPrompt(AiJobContext context, string projectRoot, string promptTemplatePath)
        {
            WriteRenderedPrompt(
                promptTemplatePath,
                ResolveCombinedRecognitionPromptPath(context),
                BuildCommonPromptTags(
                    context,
                    projectRoot,
                    new AiPromptTag { key = "[RECOGNITION_COMBINED_VERSION]", value = AiProtocolVersions.RecognitionCombined }));
        }

        private static void WriteRenderedMainTypePrompt(AiJobContext context, string projectRoot)
        {
            WriteRenderedPrompt(
                ResolveMainTypePromptTemplatePath(projectRoot),
                ResolveMainTypePromptPath(context),
                BuildCommonPromptTags(
                    context,
                    projectRoot,
                    new AiPromptTag { key = "[MAIN_TYPE_VERSION]", value = AiProtocolVersions.MainType }));
        }

        private static void WriteRenderedChildRelationPrompt(AiJobContext context, string projectRoot)
        {
            WriteRenderedPrompt(
                ResolveChildRelationPromptTemplatePath(projectRoot),
                ResolveChildRelationPromptPath(context),
                BuildCommonPromptTags(
                    context,
                    projectRoot,
                    new AiPromptTag { key = "[MAIN_TYPE_RESULT_PATH]", value = NormalizePromptPath(context.MainTypePath) },
                    new AiPromptTag { key = "[OWNER_SCOPE_MANIFEST_PATH]", value = NormalizePromptPath(context.OwnerScopeManifestPath) },
                    new AiPromptTag { key = "[CHILD_RELATION_VERSION]", value = AiProtocolVersions.ChildRelation }));
        }

        private static void WriteRenderedPrompt(string templatePath, string outputPath, AiPromptTag[] promptTags)
        {
            var request = new AiAnalysisRequest
            {
                PromptTags = promptTags
            };
            string rendered = AiProviderUtility.BuildCliPrompt(templatePath, request);
            AiJobFileUtility.WriteText(outputPath, rendered);
        }

        private static void PrepareRecognitionArtifacts(AiJobContext context)
        {
            if (context == null)
            {
                return;
            }

            TryDeleteFile(context.MainTypePath);
            TryDeleteFile(context.MainTypeTempPath);
            TryDeleteFile(context.ChildRelationPath);
            TryDeleteFile(context.ChildRelationTempPath);
            TryDeleteFile(context.RecognitionCombinedPath);
            TryDeleteFile(context.RecognitionCombinedTempPath);
            TryDeleteFile(context.StructuralPath);
            TryDeleteFile(context.StructuralTempPath);
            TryDeleteFile(context.PatchPath);
            TryDeleteFile(context.PatchTempPath);
            TryDeleteFile(context.OwnerScopeManifestPath);
            TryDeleteFile(context.RecognitionInputManifestPath);
            TryDeleteFile(context.RawOutputPath);
            TryDeleteFile(context.CompletedPath);
            TryDeleteFile(context.StreamOutputPath);
            TryDeleteFile(context.VisibleCliCompletionPath);
            TryDeleteFile(ResolveRecognitionStageRawOutputPath(context, "recognition"));
            TryDeleteFile(ResolveRecognitionStageCompletedPath(context, "recognition"));
            TryDeleteFile(ResolveRecognitionStageStreamOutputPath(context, "recognition"));
            TryDeleteFile(ResolveRecognitionStageVisibleCliCompletionPath(context, "recognition"));
            TryDeleteFile(ResolveRecognitionStageRawOutputPath(context, "main-type"));
            TryDeleteFile(ResolveRecognitionStageCompletedPath(context, "main-type"));
            TryDeleteFile(ResolveRecognitionStageStreamOutputPath(context, "main-type"));
            TryDeleteFile(ResolveRecognitionStageVisibleCliCompletionPath(context, "main-type"));
            TryDeleteFile(ResolveRecognitionStageRawOutputPath(context, "child-relation"));
            TryDeleteFile(ResolveRecognitionStageCompletedPath(context, "child-relation"));
            TryDeleteFile(ResolveRecognitionStageStreamOutputPath(context, "child-relation"));
            TryDeleteFile(ResolveRecognitionStageVisibleCliCompletionPath(context, "child-relation"));
            TryDeleteFile(context.PromptPath);
            TryDeleteFile(ResolveCombinedRecognitionPromptPath(context));
            TryDeleteFile(ResolveMainTypePromptPath(context));
            TryDeleteFile(ResolveChildRelationPromptPath(context));
            TryDeleteDirectory(context.RecognitionNodeShardDirectory);
        }

        private static bool ShouldAllowVisibleCliExecution(IAiProvider provider, AiProviderConfig config)
        {
            return provider != null
                && (config == null || config.showCliWindow)
                && provider.Capabilities != null
                && provider.Capabilities.UsesVisibleCliExecution
                && Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        private static void CommitRecognitionArtifact(AiJobContext context, AiResultDocumentKind kind, string tempPath, string finalPath, string label)
        {
            var request = new AiAnalysisRequest
            {
                ResultDocumentKind = kind,
                OutputJsonPath = finalPath,
                OutputTempJsonPath = tempPath,
                RequireExplicitOutputJsonFile = true,
                DisableOutputRecovery = true
            };

            if (!AiProviderUtility.TryCommitValidatedJsonDocument(request, out var error))
            {
                throw new InvalidOperationException($"Failed to finalize {label} result: {error}");
            }

            AiJobFileUtility.AppendDebugLog(context, $"Unified recognition artifact committed. kind={label}, temp={tempPath}, final={finalPath}");
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                AiJobFileUtility.DeleteFileIfExists(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static void MirrorStageRawOutput(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(destinationPath)
                || !File.Exists(sourcePath))
            {
                return;
            }

            try
            {
                AiJobFileUtility.EnsureDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourcePath, destinationPath, true);
            }
            catch
            {
            }
        }

        private static string ResolveRecognitionStageRawOutputPath(AiJobContext context, string stageSuffix)
        {
            return ResolveRecognitionStageResponsePath(context, "raw-output.", stageSuffix, ".txt");
        }

        private static string ResolveRecognitionStageCompletedPath(AiJobContext context, string stageSuffix)
        {
            return ResolveRecognitionStageResponsePath(context, "completed.", stageSuffix, string.Empty);
        }

        private static string ResolveRecognitionStageStreamOutputPath(AiJobContext context, string stageSuffix)
        {
            return ResolveRecognitionStageResponsePath(context, "stream-output.", stageSuffix, ".jsonl");
        }

        private static string ResolveRecognitionStageVisibleCliCompletionPath(AiJobContext context, string stageSuffix)
        {
            return ResolveRecognitionStageResponsePath(context, "visible-cli-completed.", stageSuffix, ".json");
        }

        private static string ResolveRecognitionStageResponsePath(AiJobContext context, string prefix, string stageSuffix, string extension)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.ResponseDirectory))
            {
                return string.Empty;
            }

            string fileName = string.Concat(prefix, stageSuffix ?? string.Empty, extension ?? string.Empty);
            return Path.Combine(context.ResponseDirectory, fileName);
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetRelativePath(rootPath, fullPath).Replace("\\", "/");
            }
            catch
            {
                return fullPath.Replace("\\", "/");
            }
        }

        private static bool TryLoadPatchForConverter(Psd2UIFormConverter converter, AiJobContext context, out AiPatchDocument patch, out string error)
        {
            patch = null;
            error = null;

            if (HasRecognitionArtifacts(context))
            {
                if (!TryRebuildPatchFromRecognition(context, out patch, out error))
                {
                    return false;
                }
            }

            var resultLoader = new AiResultLoader();
            if (!resultLoader.TryLoadPatch(context, out patch, out error))
            {
                return false;
            }

            if (patch == null)
            {
                error = "Patch 文件为空。";
                return false;
            }

            if (patch.analysis == null || patch.analysis.Count < 1)
            {
                error = "AI 识别结果无效：Patch 未生成任何分析项，通常表示识别结果 JSON 协议不匹配。";
                patch = null;
                return false;
            }

            string requestTreeHash = string.Empty;
            string requestPsdAssetPath = string.Empty;
            if (AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package)
                && package != null)
            {
                requestTreeHash = package.treeHash;
            }

            if (AiJobFileUtility.TryReadJson(context.MetaPath, out AiJobMetaDocument meta)
                && meta != null)
            {
                if (string.IsNullOrWhiteSpace(requestTreeHash))
                {
                    requestTreeHash = meta.treeHash;
                }
                if (string.IsNullOrWhiteSpace(requestPsdAssetPath))
                {
                    requestPsdAssetPath = meta.psdAssetPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(requestTreeHash)
                && !string.Equals(requestTreeHash, patch.treeHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "AI 结果与当前任务请求不匹配，请重新执行 AI 自动识别修正。";
                patch = null;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requestPsdAssetPath)
                && !SameAssetPath(requestPsdAssetPath, converter.PsdAssetName))
            {
                error = $"AI 结果属于 PSD '{requestPsdAssetPath}'，当前面板绑定 PSD 为 '{converter.PsdAssetName}'。请切换到对应面板或重新执行 AI 自动识别修正。";
                patch = null;
                return false;
            }

            return true;
        }

        private static bool HasRecognitionArtifacts(AiJobContext context)
        {
            return context != null
                && !string.IsNullOrWhiteSpace(context.AnalysisPackagePath)
                && File.Exists(context.AnalysisPackagePath)
                && ((!string.IsNullOrWhiteSpace(context.RecognitionCombinedPath) && File.Exists(context.RecognitionCombinedPath))
                    || (!string.IsNullOrWhiteSpace(context.MainTypePath)
                        && File.Exists(context.MainTypePath)
                        && !string.IsNullOrWhiteSpace(context.ChildRelationPath)
                        && File.Exists(context.ChildRelationPath)));
        }

        private static bool TryRebuildPatchFromRecognition(AiJobContext context, out AiPatchDocument patch, out string error)
        {
            patch = null;
            error = null;
            if (context == null)
            {
                error = "AI job context is null.";
                return false;
            }

            if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package) || package == null)
            {
                error = $"AI analysis package not found: {context.AnalysisPackagePath}";
                return false;
            }

            var recognitionLoader = new AiRecognitionResultLoader();
            var synthesizer = new AiRecognitionPatchSynthesizer();
            if (!string.IsNullOrWhiteSpace(context.RecognitionCombinedPath) && File.Exists(context.RecognitionCombinedPath))
            {
                if (!recognitionLoader.TryLoadCombinedRecognition(context, package, out var combinedResult, out error))
                {
                    return false;
                }

                if (!synthesizer.TryBuildPatch(package, combinedResult, out patch, out error))
                {
                    return false;
                }

                AiJobFileUtility.WriteJson(context.PatchPath, patch);
                AiJobFileUtility.AppendDebugLog(context, "Patch rebuilt from latest combined-recognition.json before apply.");
                return true;
            }

            if (!recognitionLoader.TryLoadMainType(context, package, out var mainTypeResult, out error))
            {
                return false;
            }

            if (!recognitionLoader.TryLoadChildRelations(context, package, out var childRelationResult, out error))
            {
                return false;
            }

            if (!synthesizer.TryBuildPatch(package, mainTypeResult, childRelationResult, out patch, out error))
            {
                return false;
            }

            AiJobFileUtility.WriteJson(context.PatchPath, patch);
            AiJobFileUtility.AppendDebugLog(context, "Patch rebuilt from latest main-type.json and child-relation.json before apply.");
            return true;
        }

        private static bool SameAssetPath(string left, string right)
        {
            return string.Equals(NormalizeAssetPath(left), NormalizeAssetPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace("\\", "/");
        }

        private static bool TryApplyPatch(Psd2UIFormConverter converter, AiJobContext context, AiPatchDocument patch, bool normalizeAfterApply, out string error)
        {
            if (!ValidateCurrentTreeMatchesRequest(converter, context, out error))
            {
                return false;
            }

            bool requiredNormalized = AiPatchSemanticNormalizer.NormalizeRequiredFields(patch);
            if (context != null
                && !string.IsNullOrWhiteSpace(context.AnalysisPackagePath)
                && AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package)
                && package != null)
            {
                AiPatchSemanticNormalizer.NormalizeAnalysisEntries(patch, package);

                var validator = new AiPatchValidator();
                if (!validator.Validate(patch, package, out error))
                {
                    string validationError = error;
                    AiJobFileUtility.AppendDebugLog(context, $"Patch package validation warning; applying valid operations anyway. error={validationError}");
                    Debug.LogWarning("[PSD2UIForm.AI] Patch 语义校验未完全通过，将继续应用可执行的正确项。error=" + validationError);
                    error = null;
                }
            }
            else if (requiredNormalized && context != null)
            {
                AiJobFileUtility.WriteJson(context.PatchPath, patch);
            }

            AiPatchSemanticNormalizer.Normalize(converter, patch);
            var applier = new AiPatchApplier();
            if (!applier.Apply(converter, patch, out error))
            {
                return false;
            }

            if (!normalizeAfterApply)
            {
                return true;
            }

            using (var inactiveNodeScope = InactiveSourceNodePreservationScope.Create(converter, context))
            {
                if (!converter.NormalizeLocalStructure(true, out var normalizeSummary))
                {
                    error = normalizeSummary;
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(normalizeSummary))
                {
                    Debug.Log("[PSD2UIForm.AI] " + normalizeSummary.Replace("\n", " | "));
                }
            }
            return true;
        }

        private static bool TryApplyPatch(Psd2UIFormConverter converter, AiPatchDocument patch, out string error)
        {
            return TryApplyPatch(converter, null, patch, true, out error);
        }

        private static void WriteBlockingSuccessResult(AiJobContext context, DateTime startUtc)
        {
            if (context == null)
            {
                return;
            }

            var result = new AiJobResultDocument
            {
                jobId = context.JobId,
                providerId = context.ProviderId,
                success = true,
                completedAtUtc = DateTime.UtcNow.ToString("o"),
                durationMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds,
                rawOutputFile = MakeRelativePath(context.JobDirectory, context.RawOutputPath),
                patchFile = MakeRelativePath(context.JobDirectory, context.PatchPath),
                errorFile = string.Empty
            };
            AiJobFileUtility.WriteJson(context.ResultPath, result);
            AiJobFileUtility.WriteStatus(context, AiJobState.Completed, "Blocking AI workflow completed successfully.");
            AiJobFileUtility.AppendDebugLog(context, $"Blocking AI workflow completed. durationMs={result.durationMs}, patchPath={context.PatchPath}");
        }

        private static void WriteBlockingFailureResult(AiJobContext context, DateTime startUtc, string message)
        {
            if (context == null)
            {
                return;
            }

            var result = new AiJobResultDocument
            {
                jobId = context.JobId,
                providerId = context.ProviderId,
                success = false,
                completedAtUtc = DateTime.UtcNow.ToString("o"),
                durationMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds,
                rawOutputFile = MakeRelativePath(context.JobDirectory, context.RawOutputPath),
                patchFile = string.Empty,
                errorFile = File.Exists(context.ErrorPath) ? MakeRelativePath(context.JobDirectory, context.ErrorPath) : string.Empty
            };
            AiJobFileUtility.WriteJson(context.ResultPath, result);
            AiJobFileUtility.WriteStatus(context, AiJobState.Failed, string.IsNullOrWhiteSpace(message) ? "Blocking AI workflow failed." : message);
        }

        /// <summary>
        /// 校验当前树与 AI 请求时的树是否一致。有差异时 warning 但不阻断。
        /// 个别节点变化不影响其余正确操作的应用。操作引用不存在的节点时 applier 会自行跳过。
        /// </summary>
        private static bool ValidateCurrentTreeMatchesRequest(Psd2UIFormConverter converter, AiJobContext context, out string error)
        {
            error = null;
            if (converter == null || context == null || string.IsNullOrWhiteSpace(context.AnalysisPackagePath))
            {
                return true;
            }
            if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package)
                || package == null
                || package.nodes == null)
            {
                return true;
            }

            var nodes = converter.GetComponentsInChildren<PsdLayerNode>(true);
            var current = new Dictionary<string, PsdLayerNode>(StringComparer.OrdinalIgnoreCase);
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    string id = AiNodeIdUtility.GetNodeId(converter, node);
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        current[id] = node;
                    }
                }
            }

            var mismatches = new List<string>();
            for (int i = 0; i < package.nodes.Count; i++)
            {
                var expected = package.nodes[i];
                if (expected == null || string.IsNullOrWhiteSpace(expected.id))
                {
                    continue;
                }
                if (!current.TryGetValue(expected.id, out var node) || node == null)
                {
                    mismatches.Add($"'{expected.id}' 已不存在");
                    continue;
                }

                string currentParentId = GetCurrentParentId(converter, node.transform.parent);
                string expectedUiType = !string.IsNullOrWhiteSpace(expected.uiType)
                    ? expected.uiType
                    : GUIType.Null.ToString();
                string expectedName = expected.name ?? string.Empty;
                if (!string.Equals(currentParentId, expected.parentId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(node.UIType.ToString(), expectedUiType, StringComparison.Ordinal)
                    || !string.Equals(node.gameObject.name ?? string.Empty, expectedName, StringComparison.Ordinal))
                {
                    mismatches.Add($"'{expected.id}' (name={expectedName}→{node.name}, parent={expected.parentId}→{currentParentId}, uiType={expectedUiType}→{node.UIType})");
                }
            }

            if (mismatches.Count > 0)
            {
                error = $"节点树已变化 ({mismatches.Count} 个节点差异，应用仍继续): {string.Join("; ", mismatches.Take(5))}";
            }
            return true;
        }

        private static string GetCurrentParentId(Psd2UIFormConverter converter, Transform parent)
        {
            if (converter == null || parent == null || parent == converter.transform)
            {
                return "root";
            }

            var parentNode = parent.GetComponent<PsdLayerNode>();
            return parentNode != null ? AiNodeIdUtility.GetNodeId(converter, parentNode) : "root";
        }
    }
}
#endif
