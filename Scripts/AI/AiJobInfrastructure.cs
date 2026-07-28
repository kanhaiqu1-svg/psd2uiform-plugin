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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal enum AiJobState
    {
        Created = 0,
        Preparing = 1,
        Running = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiJobMetaDocument
    {
        public string jobId;
        public string providerId;
        public string createdAtUtc;
        public string projectPath;
        public string psdAssetPath;
        public string converterPath;
        public string analysisPackageVersion;
        public string recognitionCombinedVersion;
        public string mainTypeVersion;
        public string childRelationVersion;
        public string structuralVersion;
        public string patchVersion;
        public string treeHash;
        public string previewHash;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiJobStatusDocument
    {
        public string jobId;
        public string providerId;
        public string state;
        public string updatedAtUtc;
        public string message;
        public string stage;
        public string detail;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiJobResultDocument
    {
        public string jobId;
        public string providerId;
        public bool success;
        public string completedAtUtc;
        public long durationMs;
        public string rawOutputFile;
        public string patchFile;
        public string errorFile;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiVisibleCliCompletionDocument
    {
        public int exitCode;
        public string completedAtUtc;
        public string rawOutputPath;
        public string streamOutputPath;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiJobContext
    {
        public string JobId;
        public string ProviderId;
        public string JobDirectory;
        public string RequestDirectory;
        public string ResponseDirectory;
        public string MetaPath;
        public string AnalysisPackagePath;
        public string RecognitionCombinedPath;
        public string RecognitionCombinedTempPath;
        public string MainTypePath;
        public string MainTypeTempPath;
        public string ChildRelationPath;
        public string ChildRelationTempPath;
        public string StructuralPath;
        public string StructuralTempPath;
        public string OwnerScopeManifestPath;
        public string RecognitionInputManifestPath;
        public string RecognitionNodeShardDirectory;
        public string PromptPath;
        public string StatusPath;
        public string ResultPath;
        public string PatchPath;
        public string PatchTempPath;
        public string RawOutputPath;
        public string CompletedPath;
        public string StreamOutputPath;
        public string VisibleCliCompletionPath;
        public string ErrorPath;
        public string DebugLogPath;
        public string DebugConsoleCloseSignalPath;
    }

    internal interface IAiJobListener
    {
        void OnAiJobCompleted(AiJobContext context);
        void OnAiJobFailed(AiJobContext context, string errorMessage);
    }

    internal delegate void AiJobExecuteHandler(AiJobContext context, CancellationToken cancellationToken);

    internal static class AiJobFileUtility
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object DebugLogSyncRoot = new object();
        private static readonly object JsonFileSyncRoot = new object();
        private const int IoRetryCount = 20;
        private const int IoRetryDelayMs = 50;

        internal static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        internal static void WriteJson<T>(string path, T value)
        {
            lock (JsonFileSyncRoot)
            {
                WriteTextAtomic(path, JsonUtility.ToJson(value, true));
            }
        }

        internal static bool TryReadJson<T>(string path, out T value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                lock (JsonFileSyncRoot)
                {
                    string text = ReadTextShared(path);
                    value = JsonUtility.FromJson<T>(text);
                }
                return value != null;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        internal static void WriteText(string path, string content)
        {
            WriteTextAtomic(path, content ?? string.Empty);
        }

        internal static void AppendDebugLog(AiJobContext context, string message, bool mirrorToConsole = true)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.DebugLogPath) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (DebugLogSyncRoot)
            {
                AppendText(context.DebugLogPath, line + Environment.NewLine);
            }

            if (mirrorToConsole)
            {
                Debug.Log($"[PSD2UIForm.AI] {line}");
            }
        }

        internal static void WriteStatus(AiJobContext context, AiJobState state, string message, string detail = null)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.StatusPath)) return;

            lock (JsonFileSyncRoot)
            {
                string currentDetail = detail;
                string currentStage = null;
                if (currentDetail == null && TryReadJson(context.StatusPath, out AiJobStatusDocument existingStatus))
                {
                    currentDetail = existingStatus != null ? existingStatus.detail : string.Empty;
                    currentStage = existingStatus != null ? existingStatus.stage : string.Empty;
                }

                var status = new AiJobStatusDocument
                {
                    jobId = context.JobId,
                    providerId = context.ProviderId,
                    state = ToStateString(state),
                    updatedAtUtc = DateTime.UtcNow.ToString("o"),
                    message = message ?? string.Empty,
                    stage = currentStage ?? string.Empty,
                    detail = currentDetail ?? string.Empty
                };
                WriteJson(context.StatusPath, status);
            }
        }

        internal static void WriteStatusStage(AiJobContext context, string stage)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.StatusPath))
            {
                return;
            }

            lock (JsonFileSyncRoot)
            {
                AiJobStatusDocument existingStatus = null;
                if (!TryReadJson(context.StatusPath, out existingStatus) || existingStatus == null)
                {
                    existingStatus = new AiJobStatusDocument
                    {
                        jobId = context.JobId,
                        providerId = context.ProviderId,
                        state = ToStateString(AiJobState.Running),
                        message = string.Empty,
                        stage = string.Empty,
                        detail = string.Empty
                    };
                }

                existingStatus.updatedAtUtc = DateTime.UtcNow.ToString("o");
                existingStatus.stage = stage ?? string.Empty;
                WriteJson(context.StatusPath, existingStatus);
            }
        }

        internal static void WriteStatusDetail(AiJobContext context, string detail)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.StatusPath))
            {
                return;
            }

            lock (JsonFileSyncRoot)
            {
                AiJobStatusDocument existingStatus = null;
                if (!TryReadJson(context.StatusPath, out existingStatus) || existingStatus == null)
                {
                    existingStatus = new AiJobStatusDocument
                    {
                        jobId = context.JobId,
                        providerId = context.ProviderId,
                        state = ToStateString(AiJobState.Running),
                        message = string.Empty,
                        stage = string.Empty,
                        detail = string.Empty
                    };
                }

                existingStatus.updatedAtUtc = DateTime.UtcNow.ToString("o");
                existingStatus.detail = detail ?? string.Empty;
                WriteJson(context.StatusPath, existingStatus);
            }
        }

        internal static string ReadText(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            return ReadTextShared(path);
        }

        internal static void DeleteFileIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            DeleteWithRetry(path);
        }

        internal static string ToStateString(AiJobState state)
        {
            switch (state)
            {
                case AiJobState.Created: return "created";
                case AiJobState.Preparing: return "preparing";
                case AiJobState.Running: return "running";
                case AiJobState.Completed: return "completed";
                case AiJobState.Failed: return "failed";
                case AiJobState.Cancelled: return "cancelled";
                default: return "unknown";
            }
        }

        private static string ReadTextShared(string path)
        {
            for (int attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Utf8NoBom, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Utf8NoBom, true))
            {
                return reader.ReadToEnd();
            }
        }

        private static void WriteTextAtomic(string path, string content)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                using (var writer = new StreamWriter(stream, Utf8NoBom, 4096, false))
                {
                    writer.Write(content ?? string.Empty);
                    writer.Flush();
                }

                ReplaceFile(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    DeleteWithRetry(tempPath);
                }
            }
        }

        private static void AppendText(string path, string content)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            for (int attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    using (var writer = new StreamWriter(stream, Utf8NoBom, 4096, false))
                    {
                        writer.Write(content ?? string.Empty);
                        writer.Flush();
                    }
                    return;
                }
                catch (IOException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
            }

            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var writer = new StreamWriter(stream, Utf8NoBom, 4096, false))
            {
                writer.Write(content ?? string.Empty);
                writer.Flush();
            }
        }

        private static void ReplaceFile(string tempPath, string path)
        {
            for (int attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        DeleteWithRetry(path);
                    }

                    File.Move(tempPath, path);
                    return;
                }
                catch (IOException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
            }

            if (File.Exists(path))
            {
                DeleteWithRetry(path);
            }

            File.Move(tempPath, path);
        }

        private static void DeleteWithRetry(string path)
        {
            for (int attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < IoRetryCount)
                {
                    Thread.Sleep(IoRetryDelayMs);
                }
            }

            File.Delete(path);
        }
    }

    internal sealed class AiJobManager
    {
        private const string ProgressBarTitle = "AI 深度思考中...";

        private sealed class RunningJob
        {
            public AiJobContext Context;
            public IAiJobListener Listener;
            public Task Task;
            public bool CompletionNotified;
            public bool CancellationRequested;
            public CancellationTokenSource CancellationTokenSource;
            public DateTime StartedAtLocal;
            public AiCliDebugConsole.Handle DebugConsoleHandle;
        }

        private readonly Dictionary<string, RunningJob> runningJobs = new Dictionary<string, RunningJob>(StringComparer.OrdinalIgnoreCase);
        private bool hookedToEditorUpdate;
        internal bool HasRunningJobs => runningJobs.Count > 0;
        internal AiJobContext CreateJob(string providerId, string rootDir, string psdAssetPath)
        {
            var safeProvider = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Replace("/", "_").Replace("\\", "_");
            var jobId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeProvider}_{Guid.NewGuid():N}".Substring(0, 32);
            var scopeDirectory = ResolveScopedJobDirectory(rootDir, psdAssetPath);
            var jobDirectory = scopeDirectory;
            var requestDirectory = Path.Combine(jobDirectory, "request");
            var responseDirectory = Path.Combine(jobDirectory, "response");

            AiJobFileUtility.EnsureDirectory(requestDirectory);
            AiJobFileUtility.EnsureDirectory(responseDirectory);

            var context = new AiJobContext
            {
                JobId = jobId,
                ProviderId = providerId,
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
                DebugLogPath = Path.Combine(responseDirectory, "debug-log.txt"),
                DebugConsoleCloseSignalPath = Path.Combine(responseDirectory, $"debug-console-{jobId}.close")
            };

            PrepareJobDirectory(context);
            AiJobFileUtility.WriteStatus(context, AiJobState.Created, "Job created.");
            AiJobFileUtility.AppendDebugLog(context, $"Job created. provider={providerId}, jobDirectory={jobDirectory}");
            return context;
        }

        internal static string ResolveLatestScopedJobDirectory(string rootDir, string psdAssetPath)
        {
            return ResolveScopedJobDirectory(rootDir, psdAssetPath);
        }

        internal static string ResolveScopedJobDirectory(string rootDir, string psdAssetPath)
        {
            string scopeName = AiPathDefaults.GetPsdAssetFolderName(psdAssetPath);
            return Path.Combine(rootDir, scopeName);
        }

        internal void StartJob(AiJobContext context, IAiProvider provider, IAiJobListener listener, AiJobExecuteHandler execute)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            if (runningJobs.Count > 0) throw new InvalidOperationException("An AI job is already running.");

            AiJobFileUtility.WriteStatus(context, AiJobState.Preparing, "Preparing provider execution.");
            AiJobFileUtility.AppendDebugLog(
                context,
                $"StartJob requested. provider={provider.ProviderId}, analysisPackage={context.AnalysisPackagePath}, mainTypePath={context.MainTypePath}, childRelationPath={context.ChildRelationPath}, structuralPath={context.StructuralPath}, patchPath={context.PatchPath}");

            AiCliDebugConsole.Handle debugConsoleHandle = null;
            if (provider.Capabilities != null
                && !provider.Capabilities.UsesVisibleCliExecution)
            {
                debugConsoleHandle = AiCliDebugConsole.TryLaunch(context, provider.ProviderId);
            }

            var runningJob = new RunningJob
            {
                Context = context,
                Listener = listener,
                CancellationTokenSource = new CancellationTokenSource(),
                StartedAtLocal = DateTime.Now,
                DebugConsoleHandle = debugConsoleHandle
            };

            runningJob.Task = Task.Run(() =>
            {
                var startUtc = DateTime.UtcNow;
                AiJobFileUtility.WriteStatus(context, AiJobState.Running, "Provider execution started.");
                AiJobFileUtility.AppendDebugLog(context, "Background AI task entered running state.");

                try
                {
                    execute(context, runningJob.CancellationTokenSource.Token);
                    AiJobFileUtility.AppendDebugLog(context, $"Workflow execution returned. patchExists={File.Exists(context.PatchPath)}, rawOutputExists={File.Exists(context.RawOutputPath)}");

                    if (!File.Exists(context.PatchPath))
                    {
                        throw new FileNotFoundException("Provider finished without producing patch.json.", context.PatchPath);
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
                    AiJobFileUtility.WriteStatus(context, AiJobState.Completed, "Patch file generated successfully.");
                    AiJobFileUtility.AppendDebugLog(context, $"AI task completed successfully. durationMs={result.durationMs}, patchPath={context.PatchPath}");
                }
                catch (OperationCanceledException)
                {
                    var result = new AiJobResultDocument
                    {
                        jobId = context.JobId,
                        providerId = context.ProviderId,
                        success = false,
                        completedAtUtc = DateTime.UtcNow.ToString("o"),
                        durationMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds,
                        rawOutputFile = MakeRelativePath(context.JobDirectory, context.RawOutputPath),
                        patchFile = string.Empty,
                        errorFile = string.Empty
                    };
                    AiJobFileUtility.WriteJson(context.ResultPath, result);
                    AiJobFileUtility.WriteStatus(context, AiJobState.Cancelled, "AI task was cancelled by user.");
                    AiJobFileUtility.AppendDebugLog(context, $"AI task cancelled. durationMs={result.durationMs}");
                }
                catch (Exception ex)
                {
                    AiJobFileUtility.WriteText(context.ErrorPath, ex.ToString());
                    var result = new AiJobResultDocument
                    {
                        jobId = context.JobId,
                        providerId = context.ProviderId,
                        success = false,
                        completedAtUtc = DateTime.UtcNow.ToString("o"),
                        durationMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds,
                        rawOutputFile = MakeRelativePath(context.JobDirectory, context.RawOutputPath),
                        patchFile = string.Empty,
                        errorFile = MakeRelativePath(context.JobDirectory, context.ErrorPath)
                    };
                    AiJobFileUtility.WriteJson(context.ResultPath, result);
                    AiJobFileUtility.WriteStatus(context, AiJobState.Failed, ex.Message);
                    AiJobFileUtility.AppendDebugLog(context, $"AI task failed. exception={ex}");
                }
            });

            runningJobs[context.JobId] = runningJob;
            EnsureEditorUpdateHook();
        }

        internal void CancelJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return;
            if (!runningJobs.TryGetValue(jobId, out var runningJob)) return;
            if (runningJob.CancellationRequested) return;

            runningJob.CancellationRequested = true;
            AiJobFileUtility.WriteStatus(runningJob.Context, AiJobState.Cancelled, "AI task was cancelled by user.");
            AiJobFileUtility.AppendDebugLog(runningJob.Context, "Cancellation requested by user.");
            try
            {
                runningJob.CancellationTokenSource?.Cancel();
                CloseDebugConsole(runningJob);
            }
            catch
            {
            }
        }

        internal void PollRunningJobs()
        {
            if (runningJobs.Count < 1)
            {
                RemoveEditorUpdateHook();
                return;
            }

            UpdateProgressUi();

            var completedJobIds = new List<string>();
            foreach (var pair in runningJobs)
            {
                var runningJob = pair.Value;
                if (runningJob == null || runningJob.CompletionNotified) continue;
                if (!TryGetTerminalState(runningJob, out var state, out var message))
                {
                    continue;
                }

                runningJob.CompletionNotified = true;
                ClearProgressUi();
                CloseDebugConsole(runningJob);
                if (state == AiJobState.Completed)
                {
                    AiJobFileUtility.AppendDebugLog(runningJob.Context, "PollRunningJobs observed completed state.");
                    runningJob.Listener?.OnAiJobCompleted(runningJob.Context);
                }
                else
                {
                    AiJobFileUtility.AppendDebugLog(runningJob.Context, $"PollRunningJobs observed terminal state={state}, message={message}");
                    runningJob.Listener?.OnAiJobFailed(runningJob.Context, message);
                }
                completedJobIds.Add(pair.Key);
            }

            for (int i = 0; i < completedJobIds.Count; i++)
            {
                if (runningJobs.TryGetValue(completedJobIds[i], out var completedJob))
                {
                    try
                    {
                        completedJob.CancellationTokenSource?.Dispose();
                        CloseDebugConsole(completedJob);
                    }
                    catch
                    {
                    }
                }
                runningJobs.Remove(completedJobIds[i]);
            }

            if (runningJobs.Count < 1)
            {
                RemoveEditorUpdateHook();
            }
        }

        private void EnsureEditorUpdateHook()
        {
            if (hookedToEditorUpdate) return;
            EditorApplication.update += PollRunningJobs;
            hookedToEditorUpdate = true;
        }

        private void RemoveEditorUpdateHook()
        {
            if (!hookedToEditorUpdate) return;
            EditorApplication.update -= PollRunningJobs;
            hookedToEditorUpdate = false;
            ClearProgressUi();
        }

        private void UpdateProgressUi()
        {
            RunningJob activeJob = null;
            foreach (var pair in runningJobs)
            {
                var candidate = pair.Value;
                if (candidate != null && !candidate.CompletionNotified)
                {
                    activeJob = candidate;
                    break;
                }
            }

            if (activeJob == null)
            {
                ClearProgressUi();
                return;
            }

            string detailMessage = string.Empty;
            string stageMessage = string.Empty;
            if (AiJobFileUtility.TryReadJson(activeJob.Context.StatusPath, out AiJobStatusDocument status)
                && status != null)
            {
                stageMessage = status.stage ?? string.Empty;
                detailMessage = status.detail ?? string.Empty;
            }

            double elapsedSeconds = Math.Max(0d, (DateTime.Now - activeJob.StartedAtLocal).TotalSeconds);
            float progress = 0.08f + 0.87f * (1f - (float)Math.Exp(-elapsedSeconds / 12d));
            progress = Mathf.Clamp(progress, 0.08f, 0.95f);
            string info = TrimProgressDetail(stageMessage, detailMessage);
            if (EditorUtility.DisplayCancelableProgressBar(ProgressBarTitle, info, progress))
            {
                CancelJob(activeJob.Context.JobId);
                ClearProgressUi();
                return;
            }
        }

        private static bool TryGetTerminalState(RunningJob runningJob, out AiJobState state, out string message)
        {
            state = AiJobState.Running;
            message = null;

            if (runningJob == null || runningJob.Context == null)
            {
                state = AiJobState.Failed;
                message = "AI 任务上下文无效。";
                return true;
            }

            if (runningJob.CancellationRequested)
            {
                state = AiJobState.Cancelled;
                message = "AI task was cancelled by user.";
                return true;
            }

            if (AiJobFileUtility.TryReadJson(runningJob.Context.StatusPath, out AiJobStatusDocument status)
                && status != null)
            {
                if (string.Equals(status.state, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    state = AiJobState.Completed;
                    message = status.message;
                    return true;
                }

                if (string.Equals(status.state, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    state = AiJobState.Failed;
                    message = status.message;
                    return true;
                }

                if (string.Equals(status.state, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    state = AiJobState.Cancelled;
                    message = status.message;
                    return true;
                }
            }

            if (runningJob.Task == null || !runningJob.Task.IsCompleted)
            {
                return false;
            }

            if (runningJob.Task.IsFaulted)
            {
                message = runningJob.Task.Exception != null
                    ? runningJob.Task.Exception.GetBaseException().Message
                    : "AI task failed.";
            }
            else if (File.Exists(runningJob.Context.ErrorPath))
            {
                message = AiJobFileUtility.ReadText(runningJob.Context.ErrorPath);
            }
            else
            {
                message = "AI task finished unexpectedly.";
            }

            state = AiJobState.Failed;
            AiJobFileUtility.WriteStatus(runningJob.Context, state, message);
            return true;
        }

        private static string TrimProgressDetail(string stage, string detail)
        {
            string normalizedStage = NormalizeProgressText(stage);
            string normalizedDetail = NormalizeProgressText(detail);

            if (string.IsNullOrWhiteSpace(normalizedStage) && string.IsNullOrWhiteSpace(normalizedDetail))
            {
                return "AI 正在分析当前 PSD 节点树...";
            }

            string normalized = string.IsNullOrWhiteSpace(normalizedStage)
                ? normalizedDetail
                : string.IsNullOrWhiteSpace(normalizedDetail)
                    ? normalizedStage
                    : normalizedStage + " | " + normalizedDetail;
            if (normalized.Length > 120)
            {
                normalized = normalized.Substring(0, 117) + "...";
            }
            return normalized;
        }

        private static string NormalizeProgressText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }
            return normalized;
        }

        private static void ClearProgressUi()
        {
            EditorUtility.ClearProgressBar();
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                var relative = Path.GetRelativePath(rootPath, fullPath);
                return relative.Replace("\\", "/");
            }
            catch
            {
                return fullPath.Replace("\\", "/");
            }
        }

        private static void PrepareJobDirectory(AiJobContext context)
        {
            if (context == null)
            {
                return;
            }

            AiJobFileUtility.EnsureDirectory(context.JobDirectory);
            AiJobFileUtility.EnsureDirectory(context.RequestDirectory);
            AiJobFileUtility.EnsureDirectory(context.ResponseDirectory);

            DeleteDirectoryFiles(context.RequestDirectory);
            DeleteDirectoryFiles(context.ResponseDirectory);
            TryDeleteFile(context.MetaPath);
            TryDeleteFile(Path.Combine(context.JobDirectory, "latest-job.txt"));
            TryDeleteFile(context.DebugConsoleCloseSignalPath);
        }

        private static void CloseDebugConsole(RunningJob runningJob)
        {
            if (runningJob == null || runningJob.DebugConsoleHandle == null)
            {
                return;
            }

            runningJob.DebugConsoleHandle.Close();
            runningJob.DebugConsoleHandle = null;
        }

        private static void DeleteDirectoryFiles(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                TryDeleteFile(files[i]);
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                AiJobFileUtility.DeleteFileIfExists(filePath);
            }
            catch
            {
                try
                {
                    AiJobFileUtility.WriteText(filePath, string.Empty);
                }
                catch
                {
                }
            }
        }
    }
}
#endif
