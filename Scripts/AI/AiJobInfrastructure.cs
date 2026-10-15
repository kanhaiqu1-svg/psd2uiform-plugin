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
        public string recognitionVersion;
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
        public string startedAtUtc;
        public string lastActivityAtUtc;
        public long elapsedMs;
        public int heartbeat;
        public long stdoutBytes;
        public long streamBytes;
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
        public string recognitionFile;
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
        public string RecognitionPath;
        public string RecognitionTempPath;
        public string RecognitionInputManifestPath;
        public string PromptPath;
        public string StatusPath;
        public string ResultPath;
        public string RawOutputPath;
        public string CompletedPath;
        public string StreamOutputPath;
        public string VisibleCliCompletionPath;
        public string ErrorPath;
        public string DebugLogPath;
        public string HeartbeatPath;
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
                WriteTextAtomic(path, value is AiRecognitionDocument recognition
                    ? AiRecognitionResultLoader.Serialize(recognition) : JsonUtility.ToJson(value, true));
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
            try
            {
                lock (DebugLogSyncRoot) AppendText(context.DebugLogPath, line + Environment.NewLine);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

#if EFUN_DEBUG
            if (mirrorToConsole)
            {
                Debug.Log($"[PSD2UIForm.AI] {line}");
            }
#endif
        }

        internal static void WriteStatus(AiJobContext context, AiJobState state, string message, string detail = null)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.StatusPath)) return;

            lock (JsonFileSyncRoot)
            {
                string currentDetail = detail;
                string currentStage = null;
                AiJobStatusDocument existingStatus = null;
                if (currentDetail == null && TryReadJson(context.StatusPath, out existingStatus))
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
                if (existingStatus != null)
                {
                    status.startedAtUtc = existingStatus.startedAtUtc;
                    status.lastActivityAtUtc = existingStatus.lastActivityAtUtc;
                    status.elapsedMs = existingStatus.elapsedMs;
                    status.heartbeat = existingStatus.heartbeat;
                    status.stdoutBytes = existingStatus.stdoutBytes;
                    status.streamBytes = existingStatus.streamBytes;
                }
                if (string.IsNullOrWhiteSpace(status.startedAtUtc)) status.startedAtUtc = status.updatedAtUtc;
                status.lastActivityAtUtc = status.updatedAtUtc;
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
                    if (File.Exists(context.StatusPath)) return;
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
                TouchStatusActivity(existingStatus);
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
                    // 文件在但读不到（多半正被替换）：保留原状态，不能拿默认 Running 覆盖终态。
                    if (File.Exists(context.StatusPath)) return;
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
                TouchStatusActivity(existingStatus);
                WriteJson(context.StatusPath, existingStatus);
            }
        }

        internal static void TouchStatusActivity(AiJobContext context, long stdoutBytes = -1, long streamBytes = -1)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.StatusPath)) return;
            lock (JsonFileSyncRoot)
            {
                if (!TryReadJson(context.StatusPath, out AiJobStatusDocument status) || status == null)
                {
                    if (File.Exists(context.StatusPath)) return;
                    status = new AiJobStatusDocument { jobId = context.JobId, providerId = context.ProviderId, state = ToStateString(AiJobState.Running) };
                }
                // lastActivityAtUtc 要名副其实：只有真实产出增长才算"活动"。
                // 原来每 200ms 轮询都刷新它，等于把"CLI 卡住"和"模型在长时间推理"抹成同一个时间戳，
                // 任何活跃度判据都无从生效。updatedAtUtc / heartbeat 仍照旧表示轮询线程活着。
                long previousStdout = status.stdoutBytes;
                long previousStream = status.streamBytes;
                string previousActivity = status.lastActivityAtUtc;
                bool grew = (stdoutBytes >= 0 && stdoutBytes != previousStdout)
                    || (streamBytes >= 0 && streamBytes != previousStream);

                TouchStatusActivity(status);
                if (!grew) status.lastActivityAtUtc = previousActivity;
                if (stdoutBytes >= 0) status.stdoutBytes = stdoutBytes;
                if (streamBytes >= 0) status.streamBytes = streamBytes;
                WriteJson(context.StatusPath, status);
            }
        }

        private static void TouchStatusActivity(AiJobStatusDocument status)
        {
            if (status == null) return;
            string now = DateTime.UtcNow.ToString("o");
            status.updatedAtUtc = now;
            status.lastActivityAtUtc = now;
            if (string.IsNullOrWhiteSpace(status.startedAtUtc)) status.startedAtUtc = now;
            if (DateTime.TryParse(status.startedAtUtc, out var started)) status.elapsedMs = (long)Math.Max(0, (DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds);
            status.heartbeat++;
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

        internal static string ReadTextShared(string path)
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
                        // 先删再移会留下"目标不存在"的中间窗口，中途失败还会连原文件一起丢；Replace 是原子的。
                        try
                        {
                            File.Replace(tempPath, path, null);
                            return;
                        }
                        catch (PlatformNotSupportedException)
                        {
                            DeleteWithRetry(path);
                            File.Move(tempPath, path);
                            return;
                        }
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
            public double EstimatedDurationSeconds;
            public AiCliDebugConsole.Handle DebugConsoleHandle;
        }

        private readonly Dictionary<string, RunningJob> runningJobs = new Dictionary<string, RunningJob>(StringComparer.OrdinalIgnoreCase);
        private bool hookedToEditorUpdate;
        private bool progressVisible;
        private double nextProgressUpdateTime;
        internal bool HasRunningJobs => runningJobs.Count > 0;

        // 域重载会带走 worker 线程、CTS 和 update 钩子，但带不走 CLI 子进程；不杀就会在重载后继续写同一个作业目录。
        private static readonly List<AiJobManager> LiveManagers = new List<AiJobManager>();

        internal AiJobManager()
        {
            LiveManagers.Add(this);
        }

        [InitializeOnLoadMethod]
        private static void InstallEditorLifecycleCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= AbortAllRunningJobs;
            AssemblyReloadEvents.beforeAssemblyReload += AbortAllRunningJobs;
            EditorApplication.quitting -= AbortAllRunningJobs;
            EditorApplication.quitting += AbortAllRunningJobs;
        }

        private static void AbortAllRunningJobs()
        {
            for (int i = 0; i < LiveManagers.Count; i++) LiveManagers[i]?.AbortRunningJobs();
        }

        private void AbortRunningJobs()
        {

            foreach (var pair in runningJobs)
            {
                var runningJob = pair.Value;
                if (runningJob == null || runningJob.CancellationRequested) continue;
                runningJob.CancellationRequested = true;
                try
                {
                    AiJobFileUtility.WriteStatus(runningJob.Context, AiJobState.Cancelled, "AI task interrupted by script reload or editor exit.");
                    AiJobFileUtility.AppendDebugLog(runningJob.Context, "Cancelling because the editor domain is going down.");
                    runningJob.CancellationTokenSource?.Cancel();
                    CloseDebugConsole(runningJob);
                }
                catch
                {
                }
            }
            ClearProgressUi();
            RemoveEditorUpdateHook();
        }
        internal AiJobContext CreateJob(string providerId, string rootDir, string psdAssetPath)
        {
            var safeProvider = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Replace("/", "_").Replace("\\", "_");
            // GUID 在前且不截断:时间戳+provider 已占 31+ 字符时截断会只剩 1 位 GUID,同秒同 provider 必碰撞
            var jobId = $"{Guid.NewGuid():N}_{DateTime.Now:yyyyMMdd_HHmmss}_{safeProvider}";
            var scopeDirectory = ResolveScopedJobDirectory(rootDir, psdAssetPath);
            var jobDirectory = scopeDirectory;
            // The job directory is the CLI sandbox. Keep all AI inputs and outputs at
            // its root so the prompt can use short, stable relative paths.
            var requestDirectory = jobDirectory;
            var responseDirectory = jobDirectory;

            AiJobFileUtility.EnsureDirectory(jobDirectory);

            var context = new AiJobContext
            {
                JobId = jobId,
                ProviderId = providerId,
                JobDirectory = jobDirectory,
                RequestDirectory = requestDirectory,
                ResponseDirectory = responseDirectory,
                MetaPath = Path.Combine(jobDirectory, "meta.json"),
                AnalysisPackagePath = Path.Combine(jobDirectory, "analysis-package.json"),
                RecognitionPath = Path.Combine(jobDirectory, "recognition.json"),
                RecognitionTempPath = Path.Combine(jobDirectory, "recognition.json.tmp"),
                RecognitionInputManifestPath = Path.Combine(jobDirectory, "manifest.json"),
                PromptPath = Path.Combine(jobDirectory, "prompt.md"),
                StatusPath = Path.Combine(jobDirectory, "status.json"),
                ResultPath = Path.Combine(jobDirectory, "result.json"),
                RawOutputPath = Path.Combine(jobDirectory, "raw-output.txt"),
                CompletedPath = Path.Combine(jobDirectory, "Completed"),
                StreamOutputPath = Path.Combine(jobDirectory, "stream-output.jsonl"),
                VisibleCliCompletionPath = Path.Combine(jobDirectory, "visible-cli-completed.json"),
                ErrorPath = Path.Combine(jobDirectory, "error.txt"),
                DebugLogPath = Path.Combine(jobDirectory, "debug-log.txt"),
                HeartbeatPath = Path.Combine(jobDirectory, "heartbeat.json"),
                DebugConsoleCloseSignalPath = Path.Combine(jobDirectory, $"debug-console-{jobId}.close")
            };

            TryDeleteFile(context.DebugConsoleCloseSignalPath);
            // The scoped directory is reused; progress and console output belong to this run only.
            AiJobFileUtility.WriteText(context.DebugLogPath, string.Empty);
            File.Delete(context.StatusPath);
            File.Delete(context.HeartbeatPath);
            AiJobFileUtility.WriteStatus(context, AiJobState.Created, "Job created.");
            AiJobFileUtility.AppendDebugLog(context, $"Job created. provider={providerId}, jobDirectory={jobDirectory}");
            return context;
        }

        internal static AiJobContext ContextForDirectory(string directory)
        {
            return new AiJobContext {
                JobDirectory = directory, RequestDirectory = directory, ResponseDirectory = directory,
                MetaPath = Path.Combine(directory, "meta.json"), AnalysisPackagePath = Path.Combine(directory, "analysis-package.json"),
                RecognitionPath = Path.Combine(directory, "recognition.json"), RecognitionTempPath = Path.Combine(directory, "recognition.json.tmp"),
                StatusPath = Path.Combine(directory, "status.json"), DebugLogPath = Path.Combine(directory, "debug-log.txt")
            };
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


        private static void DeleteEmptyDirectory(string directoryPath)
        {
            if (!string.IsNullOrWhiteSpace(directoryPath)
                && Directory.Exists(directoryPath)
                && Directory.GetFileSystemEntries(directoryPath).Length == 0)
            {
                Directory.Delete(directoryPath);
            }
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
                $"StartJob requested. provider={provider.ProviderId}, analysisPackage={context.AnalysisPackagePath}, recognitionPath={context.RecognitionPath}");

            AiCliDebugConsole.Handle debugConsoleHandle = null;
            if (provider.Capabilities != null
                && !provider.Capabilities.UsesVisibleCliExecution
                && UGUIParser.Instance.AiProviderConfig.showCliWindow)
            {
                debugConsoleHandle = AiCliDebugConsole.TryLaunch(context, provider.ProviderId);
            }

            var runningJob = new RunningJob
            {
                Context = context,
                Listener = listener,
                CancellationTokenSource = new CancellationTokenSource(),
                StartedAtLocal = DateTime.Now,
                EstimatedDurationSeconds = UnityEngine.Random.Range(300f, 600f),
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
                    runningJob.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    AiJobFileUtility.AppendDebugLog(context, $"Workflow execution returned. recognitionExists={File.Exists(context.RecognitionPath)}, rawOutputExists={File.Exists(context.RawOutputPath)}");

                    if (!File.Exists(context.RecognitionPath))
                    {
                        throw new FileNotFoundException("Provider finished without producing recognition.json.", context.RecognitionPath);
                    }

                    var result = new AiJobResultDocument
                    {
                        jobId = context.JobId,
                        providerId = context.ProviderId,
                        success = true,
                        completedAtUtc = DateTime.UtcNow.ToString("o"),
                        durationMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds,
                        rawOutputFile = MakeRelativePath(context.JobDirectory, context.RawOutputPath),
                        recognitionFile = MakeRelativePath(context.JobDirectory, context.RecognitionPath),
                        errorFile = string.Empty
                    };
                    AiJobFileUtility.WriteJson(context.ResultPath, result);
                    AiJobFileUtility.WriteStatus(context, AiJobState.Completed, "UI recognition generated successfully.");
                    AiJobFileUtility.AppendDebugLog(context, $"AI task completed successfully. durationMs={result.durationMs}, recognitionPath={context.RecognitionPath}");
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
                        recognitionFile = string.Empty,
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
                        recognitionFile = string.Empty,
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
            double now = EditorApplication.timeSinceStartup;
            if (now < nextProgressUpdateTime) return;
            nextProgressUpdateTime = now + 1d;

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
            if (activeJob.CancellationRequested) return;

            string detailMessage = string.Empty;
            string stageMessage = string.Empty;
            if (AiJobFileUtility.TryReadJson(activeJob.Context.StatusPath, out AiJobStatusDocument status)
                && status != null)
            {
                stageMessage = status.stage ?? string.Empty;
                detailMessage = status.detail ?? string.Empty;
            }

            double elapsedSeconds = Math.Max(0d, (DateTime.Now - activeJob.StartedAtLocal).TotalSeconds);
            float progress = Mathf.Min(0.98f, (float)(0.98d * elapsedSeconds / activeJob.EstimatedDurationSeconds));
            string info = BuildProgressInfo(stageMessage, detailMessage, elapsedSeconds);
            progressVisible = true;
            if (EditorUtility.DisplayCancelableProgressBar(ProgressBarTitle, info, progress))
            {
                CancelJob(activeJob.Context.JobId);
                ClearProgressUi();
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

            // Keep ownership of the CLI sandbox until the worker has stopped writing.
            if (runningJob.Task == null || !runningJob.Task.IsCompleted) return false;

            if (runningJob.CancellationRequested || runningJob.Task.IsCanceled)
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

        // 展示阶段和已用时，不将 CLI 的静默时间当作完成进度。
        internal static string BuildProgressInfo(string stage, string detail, double elapsedSeconds)
        {
            string head = NormalizeProgressText(stage);
            if (string.IsNullOrWhiteSpace(head)) head = NormalizeProgressText(detail);
            if (string.IsNullOrWhiteSpace(head)) head = "识别中";
            return head + " · 已用时 " + FormatElapsedShort(elapsedSeconds);
        }

        private static string FormatElapsedShort(double seconds)
        {
            int total = (int)Math.Round(seconds);
            int hours = total / 3600, minutes = (total % 3600) / 60, secs = total % 60;
            return hours > 0
                ? string.Format("{0}:{1:00}:{2:00}", hours, minutes, secs)
                : string.Format("{0}:{1:00}", minutes, secs);
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

        private void ClearProgressUi()
        {
            if (!progressVisible) return;
            EditorUtility.ClearProgressBar();
            progressVisible = false;
            nextProgressUpdateTime = 0d;
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                var relative = PathExtensions.GetRelativePath(rootPath, fullPath);
                return relative.Replace("\\", "/");
            }
            catch
            {
                return fullPath.Replace("\\", "/");
            }
        }

        // 上一轮的识别产物必须在写本轮 meta 之前清掉，否则会留下"新 meta + 旧结果"的错配窗口。
        // 只清结果类文件：status/heartbeat/debug-log 已由 CreateJob 写过本轮值，删掉会让面板在
        // 本轮 CLI 起来之前读不到状态，调试日志也会丢掉本轮"Job created"这类起点记录。
        internal static void ClearStaleRecognitionResults(AiJobContext context)
        {
            if (context == null)
            {
                return;
            }

            TryDeleteFile(context.RecognitionPath);
            TryDeleteFile(context.RecognitionTempPath);
            TryDeleteFile(context.CompletedPath);
            TryDeleteFile(context.RawOutputPath);
            TryDeleteFile(context.StreamOutputPath);
            TryDeleteFile(context.ErrorPath);
            TryDeleteFile(context.ResultPath);
            TryDeleteFile(context.VisibleCliCompletionPath);
            TryDeleteFile(Path.Combine(context.JobDirectory, "apply-report.json"));
            TryDeleteFile(Path.Combine(context.JobDirectory, "recognition-metrics.json"));
            TryDeleteFile(Path.Combine(context.JobDirectory, "export-metrics.json"));
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
