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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal abstract class CliAiProviderBase : IAiProvider
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private sealed class StreamState
        {
            public AiStreamTerminalEventKind TerminalEventKind;
            public string TerminalEventMessage;
        }

        private sealed class OutputCaptureState : IDisposable
        {
            public StreamWriter RawStreamWriter;
            public StreamWriter DisplayWriter;
            public bool VisibleDisplayEnabled;
            public readonly Dictionary<string, string> Dedupe = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly StringBuilder ClaudeThinkingBuffer = new StringBuilder(512);
            public readonly StringBuilder ClaudeResponseBuffer = new StringBuilder(512);
            public string ClaudeContentBlockType = string.Empty;
            public bool ClaudeThinkingHeaderShown;
            public bool ClaudeResponseHeaderShown;
            public bool OpenCodeThinkingHeaderShown;
            public bool OpenCodeResponseHeaderShown;

            public void Dispose()
            {
                try
                {
                    if (DisplayWriter != null)
                    {
                        DisplayWriter.Flush();
                        DisplayWriter.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    if (RawStreamWriter != null)
                    {
                        RawStreamWriter.Flush();
                        RawStreamWriter.Dispose();
                    }
                }
                catch
                {
                }
            }
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleEnvelope
        {
            public string type;
            public string subtype;
            public string model;
            public string[] tools;
            public ClaudeVisibleEventPayload @event;
            public ClaudeVisibleMessage message;
            public long duration_ms;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleEventPayload
        {
            public string type;
            public ClaudeVisibleContentBlock content_block;
            public ClaudeVisibleDelta delta;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleContentBlock
        {
            public string type;
            public string name;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleDelta
        {
            public string type;
            public string thinking;
            public string text;
            public string partial_json;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleMessage
        {
            public ClaudeVisibleMessageContent[] content;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class ClaudeVisibleMessageContent
        {
            public string type;
            public string content;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class OpenCodeVisibleEnvelope
        {
            public string type;
            public OpenCodeVisiblePart part;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class OpenCodeVisiblePart
        {
            public string type;
            public string text;
            public string reason;
            public OpenCodeVisibleTokens tokens;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class OpenCodeVisibleTokens
        {
            public int output;
            public int reasoning;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleEnvelope
        {
            public string type;
            public CodexVisibleItem item;
            public CodexVisibleUsage usage;
            public string message;
            public string summary;
            public string title;
            public string content;
            public CodexVisibleError error;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleItem
        {
            public string type;
            public string status;
            public string command;
            public string tool;
            public string server;
            public CodexVisibleArguments arguments;
            public string text;
            public CodexVisibleItemResult result;
            public CodexVisibleError error;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleArguments
        {
            public string title;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleItemResult
        {
            public CodexVisibleResultContent[] content;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleResultContent
        {
            public string text;
            public string content;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleError
        {
            public string message;
            public string text;
        }

        [Serializable]
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        private sealed class CodexVisibleUsage
        {
            public int output_tokens;
            public int reasoning_output_tokens;
        }

        public abstract string ProviderId { get; }

        public virtual AiProviderCapabilities Capabilities => new AiProviderCapabilities
        {
            SupportsImages = true,
            SupportsStrictJson = true,
        };

        protected abstract string ExecutablePath { get; }
        protected abstract string BuildArguments(AiAnalysisRequest request);
        protected virtual string BuildVisibleArguments(AiAnalysisRequest request)
        {
            return BuildArguments(request);
        }

        /// <summary>
        /// 返回 runner 脚本中实际调用 CLI 的 PowerShell 语句。默认是位置参数模式 (Codex 用)：
        ///   & $cliPath &lt;args&gt; $bootstrapPrompt
        /// Provider 可重写以使用其它启动方式（如 stdin pipe + stream-json pretty-printer）。
        /// 返回的字符串可以是多行 PS 代码块，会被原样写入 runner.ps1。
        /// 需要保证最后 $LASTEXITCODE 反映真实 CLI 退出码 (供基类后续判定)。
        /// </summary>
        protected virtual string BuildVisibleCliInvocation(string commandArguments, AiJobContext context)
        {
            return "& $cliPath " + commandArguments + " $bootstrapPrompt";
        }

        protected string BuildSharedVisibleCliInvocation(
            string commandArguments,
            string providerStreamAdapterFunction,
            string adapterFunctionName,
            string preludeScript = null,
            string epilogueScript = null)
        {
            var sb = new StringBuilder();
            sb.Append(VisibleCliDisplayFrameworkFunction);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(providerStreamAdapterFunction))
            {
                sb.Append(providerStreamAdapterFunction);
                sb.AppendLine();
            }

            sb.AppendLine("$script:streamWriter = [System.IO.StreamWriter]::new($streamPath, $false, $utf8NoBom)");
            sb.AppendLine("$script:visibleCliEventDedupe = @{}");

            if (!string.IsNullOrWhiteSpace(preludeScript))
            {
                sb.AppendLine(preludeScript);
            }

            sb.AppendLine("try {");
            sb.AppendLine("    $bootstrapPrompt | & $cliPath " + commandArguments + " 2>&1 | ForEach-Object { Invoke-VisibleCliStreamAdapter $_ '" + EscapePowerShellSingleQuoted(adapterFunctionName) + "' }");
            sb.AppendLine("}");
            sb.AppendLine("finally {");
            sb.AppendLine("    if ($script:streamWriter -ne $null) { $script:streamWriter.Flush(); $script:streamWriter.Dispose() }");
            sb.AppendLine("}");

            if (!string.IsNullOrWhiteSpace(epilogueScript))
            {
                sb.AppendLine(epilogueScript);
            }

            return sb.ToString();
        }

        public void ExecuteJob(AiJobContext context, AiAnalysisRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null) throw new ArgumentNullException(nameof(request));

            AiJobFileUtility.AppendDebugLog(context, $"CLI ExecuteJob begin. provider={ProviderId}");
            ValidateCliPathScope(context, request);

            var prompt = AiProviderUtility.BuildCliPrompt(request.PromptTemplatePath, request);
            AiJobFileUtility.WriteText(context.PromptPath, prompt);
            AiJobFileUtility.AppendDebugLog(context, $"Prompt written. promptPath={context.PromptPath}, promptLength={(prompt != null ? prompt.Length : 0)}");
            var stdoutBuilder = new StringBuilder(4096);
            var stderrBuilder = new StringBuilder(2048);
            var syncRoot = new object();
            var streamState = new StreamState();
            string lastArtifactError = null;
            int lastStdoutValidationLength = -1;
            bool useVisibleDisplay = ShouldUseVisibleCliExecution(request);

            string commandArguments = useVisibleDisplay ? BuildVisibleArguments(request) : BuildArguments(request);
            string workingDirectory = ResolveWorkingDirectory(request);
            ResetTransientArtifacts(context, request);
            TryLaunchVisibleCliLogViewer(context, request, useVisibleDisplay);

            if (!CliCommandResolver.TryResolve(ExecutablePath, commandArguments, context.PromptPath, out var launchSpec, out var resolveError))
            {
                AiJobFileUtility.AppendDebugLog(context, $"CLI resolve failed. executable={ExecutablePath}, error={resolveError}");
                throw new InvalidOperationException(resolveError);
            }

            AiJobFileUtility.AppendDebugLog(
                context,
                $"CLI resolved. executable={ExecutablePath}, launchFile={launchSpec.FileName}, launchArguments={launchSpec.Arguments}, useStandardInput={launchSpec.UseStandardInput}");

            var startInfo = new ProcessStartInfo
            {
                FileName = launchSpec.FileName,
                Arguments = launchSpec.Arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = launchSpec.UseStandardInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom
            };

            AiJobFileUtility.AppendDebugLog(context, $"ProcessStartInfo prepared. workingDirectory={startInfo.WorkingDirectory}, redirectStdIn={startInfo.RedirectStandardInput}");

            try
            {
                using (var captureState = CreateOutputCaptureState(context, request, useVisibleDisplay))
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (_, args) => HandleOutputLine(context, request, args != null ? args.Data : null, false, stdoutBuilder, syncRoot, streamState, captureState);
                    process.ErrorDataReceived += (_, args) => HandleOutputLine(context, request, args != null ? args.Data : null, true, stderrBuilder, syncRoot, streamState, captureState);

                    if (!process.Start())
                    {
                        AiJobFileUtility.AppendDebugLog(context, $"Process.Start returned false. fileName={startInfo.FileName}");
                        throw new InvalidOperationException($"Failed to start CLI provider '{ProviderId}'.");
                    }

                    AiJobFileUtility.AppendDebugLog(context, $"Process started. pid={process.Id}, fileName={startInfo.FileName}");

                    using (request.CancellationToken.Register(() =>
                    {
                        KillProcessTree(process, context, "Cancellation token triggered");
                    }))
                    {
                        if (launchSpec.UseStandardInput)
                        {
                            process.StandardInput.Write(prompt);
                            process.StandardInput.Close();
                            AiJobFileUtility.AppendDebugLog(context, "Prompt piped to process stdin and stdin closed.");
                        }

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        AiJobFileUtility.AppendDebugLog(context, "BeginOutputReadLine and BeginErrorReadLine called.");

                        const int waitTimeoutMs = 200;
                        while (!process.HasExited)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                            {
                                throw new OperationCanceledException("AI job was cancelled by user.", request.CancellationToken);
                            }

                            UpdateUnifiedRecognitionProgress(context, request);

                            if (TryFinalizeRunningArtifacts(context, request, out lastArtifactError))
                            {
                                AiJobFileUtility.AppendDebugLog(context, "Valid staged JSON artifact observed while CLI is still running. Stopping process tree immediately.");
                                KillProcessTree(process, context, "Staged JSON artifact committed");
                                return;
                            }

                            if (streamState.TerminalEventKind == AiStreamTerminalEventKind.Failed)
                            {
                                string currentStderr;
                                lock (syncRoot)
                                {
                                    currentStderr = stderrBuilder.ToString();
                                }

                                KillProcessTree(process, context, "Terminal failure event observed");
                                throw new InvalidOperationException(BuildCliFailureMessage(
                                    context,
                                    request,
                                    currentStderr,
                                    null,
                                    string.IsNullOrWhiteSpace(streamState.TerminalEventMessage) ? "CLI provider reported a failure event." : streamState.TerminalEventMessage));
                            }

                            if (!RequiresExplicitOutputFile(request)
                                && streamState.TerminalEventKind == AiStreamTerminalEventKind.Completed)
                            {
                                try
                                {
                                    if (TryFinalizeCompletedArtifacts(context, request, allowStreamRecovery: true, out lastArtifactError))
                                    {
                                        AiJobFileUtility.AppendDebugLog(context, "CLI completion event observed and artifacts finalized. Stopping process tree.");
                                        KillProcessTree(process, context, "Completion artifacts ready");
                                        return;
                                    }

                                    if (TryFinalizeCapturedStdoutArtifacts(context, request, stdoutBuilder, syncRoot, ref lastStdoutValidationLength, out lastArtifactError))
                                    {
                                        AiJobFileUtility.AppendDebugLog(context, "CLI completion event observed and JSON synthesized from captured stdout. Stopping process tree.");
                                        KillProcessTree(process, context, "Completion artifacts ready from captured stdout");
                                        return;
                                    }
                                }
                                catch
                                {
                                    KillProcessTree(process, context, "Completion artifact validation failed");
                                    throw;
                                }
                            }

                            process.WaitForExit(waitTimeoutMs);
                        }

                        process.WaitForExit();
                        AiJobFileUtility.AppendDebugLog(context, $"Process exited. exitCode={process.ExitCode}");

                        string stdout;
                        string stderr;
                        lock (syncRoot)
                        {
                            stdout = stdoutBuilder.ToString();
                            stderr = stderrBuilder.ToString();
                        }

                        AiJobFileUtility.AppendDebugLog(context, $"Process output captured. stdoutLength={(stdout != null ? stdout.Length : 0)}, stderrLength={(stderr != null ? stderr.Length : 0)}");

                        if (!File.Exists(request.OutputRawTextPath))
                        {
                            var rawOutput = new StringBuilder();
                            if (!string.IsNullOrWhiteSpace(stdout))
                            {
                                rawOutput.AppendLine(stdout.Trim());
                            }

                            if (!string.IsNullOrWhiteSpace(stderr))
                            {
                                rawOutput.AppendLine(stderr.Trim());
                            }

                            string rawOutputPath = GetRawOutputPath(context, request);
                            if (!string.IsNullOrWhiteSpace(rawOutputPath))
                            {
                                AiJobFileUtility.WriteText(rawOutputPath, rawOutput.ToString());
                                AiJobFileUtility.AppendDebugLog(context, $"Final assistant message artifact created from captured streams. rawOutputPath={rawOutputPath}");
                            }
                        }

                        if (request.CancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("AI job was cancelled by user.", request.CancellationToken);
                        }

                        if (TryFinalizeCompletedArtifacts(context, request, allowStreamRecovery: true, out lastArtifactError))
                        {
                            AiJobFileUtility.AppendDebugLog(context, "CLI completion artifacts extracted successfully after process exit.");
                            return;
                        }

                        if (TryFinalizeCapturedStdoutArtifacts(context, request, stdoutBuilder, syncRoot, ref lastStdoutValidationLength, out lastArtifactError))
                        {
                            AiJobFileUtility.AppendDebugLog(context, "CLI completion artifacts synthesized from captured stdout after process exit.");
                            return;
                        }

                        if (streamState.TerminalEventKind == AiStreamTerminalEventKind.Failed)
                        {
                            throw new InvalidOperationException(BuildCliFailureMessage(
                                context,
                                request,
                                stderr,
                                null,
                                string.IsNullOrWhiteSpace(streamState.TerminalEventMessage) ? "CLI provider reported a failure event." : streamState.TerminalEventMessage));
                        }

                        if (process.ExitCode != 0)
                        {
                            AiJobFileUtility.AppendDebugLog(context, $"CLI process exited with non-zero code. exitCode={process.ExitCode}");
                            throw new InvalidOperationException(BuildCliFailureMessage(context, request, stderr, process.ExitCode));
                        }

                        if (!string.IsNullOrWhiteSpace(lastArtifactError))
                        {
                            throw new InvalidOperationException(lastArtifactError);
                        }

                        throw new InvalidOperationException("CLI process exited without producing a valid JSON result artifact.");
                    }
                }
            }
            finally
            {
                TrySignalVisibleCliLogViewerClose(context, useVisibleDisplay);
            }
        }

        private static bool TryFinalizeCapturedStdoutArtifacts(
            AiJobContext context,
            AiAnalysisRequest request,
            StringBuilder stdoutBuilder,
            object syncRoot,
            ref int lastStdoutValidationLength,
            out string error)
        {
            error = null;
            if (request == null || stdoutBuilder == null || syncRoot == null || RequiresExplicitOutputFile(request) || request.DisableOutputRecovery)
            {
                return false;
            }

            string capturedStdout;
            lock (syncRoot)
            {
                if (stdoutBuilder.Length < 1 || stdoutBuilder.Length == lastStdoutValidationLength)
                {
                    return false;
                }

                capturedStdout = stdoutBuilder.ToString();
                lastStdoutValidationLength = stdoutBuilder.Length;
            }

            if (!AiProviderUtility.TryPersistJsonDocumentFromRawOutput(capturedStdout, request, out error))
            {
                return false;
            }

            EnsureCompletedMarker(context, request);
            AiJobFileUtility.AppendDebugLog(context, $"JSON synthesized directly from captured stdout. outputPath={request.OutputJsonPath}", mirrorToConsole: false);
            return true;
        }

        private bool ShouldUseVisibleCliExecution(AiAnalysisRequest request)
        {
            var capabilities = Capabilities;
            return capabilities != null
                && request != null
                && request.AllowVisibleCliExecution
                && capabilities.UsesVisibleCliExecution
                && (IsWindows() || IsMacOS());
        }

        private void ExecuteVisibleCliJob(AiJobContext context, AiAnalysisRequest request, string commandArguments, string workingDirectory)
        {
            var startInfo = BuildVisibleCliStartInfo(context, request, workingDirectory);
            ResetTransientArtifacts(context, request);

            AiJobFileUtility.AppendDebugLog(context, $"Starting visible CLI terminal. fileName={startInfo.FileName}, arguments={startInfo.Arguments}");
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException($"Failed to start visible CLI provider '{ProviderId}'.");
                }

                AiJobFileUtility.WriteStatusDetail(context, "AI任务进程: CLI 运行中");
                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI process started. pid={process.Id}");
                string lastArtifactError = null;
                bool observedStreamFailure = false;
                const int waitTimeoutMs = 500;
                while (!process.HasExited)
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        KillProcessTree(process, context, "Cancellation token triggered");
                        throw new OperationCanceledException("AI job was cancelled by user.", request.CancellationToken);
                    }

                    UpdateUnifiedRecognitionProgress(context, request);

                    if (TryFinalizeRunningArtifacts(context, request, out lastArtifactError))
                    {
                        AiJobFileUtility.AppendDebugLog(context, "Valid staged JSON artifact observed while visible CLI is still running. Closing terminal window.");
                        KillProcessTree(process, context, "Staged JSON artifact committed");
                        return;
                    }

                    var streamEventKind = UpdateVisibleStreamProgress(context, request);
                    if (streamEventKind == AiStreamTerminalEventKind.Failed)
                    {
                        observedStreamFailure = true;
                    }

                    if (TryReadVisibleCliCompletion(context, request, out var completion))
                    {
                        AiJobFileUtility.AppendDebugLog(context, $"Visible CLI completion marker observed. exitCode={completion.exitCode}, completedAtUtc={completion.completedAtUtc}");
                        if (TryFinalizeCompletedArtifacts(context, request, allowStreamRecovery: true, out lastArtifactError))
                        {
                            AiJobFileUtility.AppendDebugLog(context, "Visible CLI artifacts finalized from completion marker. Closing terminal window.");
                            KillProcessTree(process, context, "Completion artifacts ready");
                            return;
                        }

                        if (completion.exitCode != 0)
                        {
                            throw new InvalidOperationException(BuildCliFailureMessage(context, request, string.Empty, completion.exitCode));
                        }

                        if (observedStreamFailure)
                        {
                            throw new InvalidOperationException(BuildCliFailureMessage(context, request, string.Empty, null, "CLI provider reported a failure event. See current stage stream output for details."));
                        }

                        throw new InvalidOperationException(!string.IsNullOrWhiteSpace(lastArtifactError)
                            ? lastArtifactError
                            : "CLI process completed without producing a valid JSON result artifact.");
                    }

                    process.WaitForExit(waitTimeoutMs);
                }

                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI process exited. exitCode={process.ExitCode}");
                if (TryFinalizeCompletedArtifacts(context, request, allowStreamRecovery: true, out lastArtifactError))
                {
                    AiJobFileUtility.AppendDebugLog(context, "Visible CLI artifacts finalized after process exit.");
                    return;
                }

                if (request.CancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("AI job was cancelled by user.", request.CancellationToken);
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(BuildCliFailureMessage(context, request, string.Empty, process.ExitCode));
                }

                if (observedStreamFailure)
                {
                    throw new InvalidOperationException(BuildCliFailureMessage(context, request, string.Empty, null, "CLI provider reported a failure event. See current stage stream output for details."));
                }

                if (!string.IsNullOrWhiteSpace(lastArtifactError))
                {
                    throw new InvalidOperationException(lastArtifactError);
                }

                throw new InvalidOperationException("CLI process exited without producing a valid JSON result artifact.");
            }
        }

        private static bool TryReadVisibleCliCompletion(AiJobContext context, AiAnalysisRequest request, out AiVisibleCliCompletionDocument completion)
        {
            completion = null;
            string completionPath = GetVisibleCliCompletionPath(context, request);
            if (string.IsNullOrWhiteSpace(completionPath))
            {
                return false;
            }

            return AiJobFileUtility.TryReadJson(completionPath, out completion) && completion != null;
        }

        private static AiStreamTerminalEventKind UpdateVisibleStreamProgress(AiJobContext context, AiAnalysisRequest request)
        {
            string latestLine = ReadLatestNonEmptyLine(GetStreamOutputPath(context, request));
            if (string.IsNullOrWhiteSpace(latestLine))
            {
                return AiStreamTerminalEventKind.None;
            }

            string detail = AiProviderUtility.ExtractProgressDetail(latestLine);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                AiJobFileUtility.WriteStatusDetail(context, detail);
            }

            if (AiProviderUtility.TryParseCodexTerminalEvent(latestLine, out var kind, out var message)
                && kind != AiStreamTerminalEventKind.None)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI stream event observed. kind={kind}, message={message}", mirrorToConsole: false);
                return kind;
            }

            return AiStreamTerminalEventKind.None;
        }

        private static void UpdateUnifiedRecognitionProgress(AiJobContext context, AiAnalysisRequest request)
        {
            if (context == null
                || request == null
                || !string.Equals(request.StageName, "recognition-orchestrator", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(context.MainTypeTempPath)
                || !File.Exists(context.MainTypeTempPath))
            {
                return;
            }

            if (AiJobFileUtility.TryReadJson(context.StatusPath, out AiJobStatusDocument status)
                && status != null
                && string.Equals(status.stage, "AI处理阶段 2/3：子控件归属识别", StringComparison.Ordinal))
            {
                return;
            }

            AiJobFileUtility.WriteStatusStage(context, "AI处理阶段 2/3：子控件归属识别");
            AiJobFileUtility.WriteStatusDetail(context, "AI任务进程: 第一阶段结果已写入，继续同会话进行子控件归属识别");
            AiJobFileUtility.AppendDebugLog(context, "Unified recognition progress advanced to child-relation stage.");
        }

        private static string ReadLatestNonEmptyLine(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                const int maxBytes = 16384;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int bytesToRead = (int)Math.Min(maxBytes, stream.Length);
                    if (bytesToRead < 1) return string.Empty;

                    stream.Seek(-bytesToRead, SeekOrigin.End);
                    var buffer = new byte[bytesToRead];
                    int read = stream.Read(buffer, 0, bytesToRead);
                    string text = Utf8NoBom.GetString(buffer, 0, read);
                    var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string line = lines[i].Trim();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            return line;
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private ProcessStartInfo BuildVisibleCliStartInfo(AiJobContext context, AiAnalysisRequest request, string workingDirectory)
        {
            if (IsWindows())
            {
                string commandPath = ResolveWindowsPowerShellCommandPath(ExecutablePath);
                if (string.IsNullOrWhiteSpace(commandPath))
                {
                    throw new InvalidOperationException($"未找到 CLI 命令: {ExecutablePath}.ps1");
                }

                string streamOutputPath = GetStreamOutputPath(context, request);
                AiJobFileUtility.EnsureDirectory(
                    !string.IsNullOrWhiteSpace(streamOutputPath)
                        ? Path.GetDirectoryName(streamOutputPath)
                        : context.ResponseDirectory);
                string runnerPath = Path.Combine(context.ResponseDirectory, "run-visible-cli.ps1");
                string bootstrapPromptPath = Path.Combine(context.ResponseDirectory, "visible-cli-bootstrap-prompt.md");
                string visibleArguments = BuildVisibleArguments(request);
                AiJobFileUtility.WriteText(bootstrapPromptPath, BuildVisibleBootstrapPrompt(context, request));
                string runnerScript = BuildWindowsVisibleRunnerScript(context, commandPath, visibleArguments, bootstrapPromptPath, request);
                AiJobFileUtility.WriteText(runnerPath, runnerScript);
                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI runner written. runnerPath={runnerPath}, commandPath={commandPath}, commandArguments={visibleArguments}");

                return new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + runnerPath + "\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
            }

            throw new InvalidOperationException("Visible CLI execution is only supported on WindowsEditor.");
        }

        private string BuildWindowsVisibleRunnerScript(AiJobContext context, string commandPath, string commandArguments, string bootstrapPromptPath, AiAnalysisRequest request)
        {
            var builder = new StringBuilder(2048);
            string stageDisplay = request != null && !string.IsNullOrWhiteSpace(request.StageDisplayName)
                ? request.StageDisplayName
                : "AI Stage";
            builder.AppendLine("$ErrorActionPreference = 'Continue'");
            builder.AppendLine("$utf8NoBom = New-Object System.Text.UTF8Encoding($false)");
            builder.AppendLine("[Console]::InputEncoding = $utf8NoBom");
            builder.AppendLine("[Console]::OutputEncoding = $utf8NoBom");
            builder.AppendLine("$OutputEncoding = $utf8NoBom");
            builder.AppendLine("try { chcp 65001 > $null } catch { }");
            builder.AppendLine("$Host.UI.RawUI.WindowTitle = 'PSD2UIForm AI - " + EscapePowerShellSingleQuoted(stageDisplay) + " - " + EscapePowerShellSingleQuoted(ProviderId) + " - " + EscapePowerShellSingleQuoted(context.JobId) + "'");
            builder.AppendLine("$promptPath = '" + EscapePowerShellSingleQuoted(context.PromptPath) + "'");
            builder.AppendLine("$streamPath = '" + EscapePowerShellSingleQuoted(GetStreamOutputPath(context, request)) + "'");
            builder.AppendLine("$rawOutputPath = '" + EscapePowerShellSingleQuoted(GetRawOutputPath(context, request)) + "'");
            builder.AppendLine("$completionPath = '" + EscapePowerShellSingleQuoted(GetVisibleCliCompletionPath(context, request)) + "'");
            builder.AppendLine("$bootstrapPromptPath = '" + EscapePowerShellSingleQuoted(bootstrapPromptPath) + "'");
            builder.AppendLine("$cliPath = '" + EscapePowerShellSingleQuoted(commandPath) + "'");
            builder.AppendLine("Write-Host '[PSD2UIForm.AI] Starting native CLI UI for " + EscapePowerShellSingleQuoted(ProviderId) + "...'");
            builder.AppendLine("Write-Host ('[PSD2UIForm.AI] CLI: ' + $cliPath)");
            builder.AppendLine("Write-Host ('[PSD2UIForm.AI] Raw output: ' + $rawOutputPath)");
            builder.AppendLine("Write-Host ('[PSD2UIForm.AI] Completion marker: ' + $completionPath)");
            builder.AppendLine("Write-Host ('[PSD2UIForm.AI] Bootstrap prompt: ' + $bootstrapPromptPath)");
            builder.AppendLine("Write-Host ''");
            builder.AppendLine("$exitCode = 1");
            builder.AppendLine("if (Test-Path -LiteralPath $streamPath) { Remove-Item -LiteralPath $streamPath -Force }");
            builder.AppendLine("if (Test-Path -LiteralPath $completionPath) { Remove-Item -LiteralPath $completionPath -Force }");
            builder.AppendLine("$bootstrapPrompt = Get-Content -Raw -Encoding UTF8 -LiteralPath $bootstrapPromptPath");
            builder.AppendLine(BuildVisibleCliInvocation(commandArguments, context));
            builder.AppendLine("$exitCode = $LASTEXITCODE");
            builder.AppendLine("$completion = [ordered]@{");
            builder.AppendLine("    exitCode = [int]$exitCode");
            builder.AppendLine("    completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')");
            builder.AppendLine("    rawOutputPath = $rawOutputPath");
            builder.AppendLine("    streamOutputPath = $streamPath");
            builder.AppendLine("}");
            builder.AppendLine("$completion | ConvertTo-Json -Compress | Set-Content -LiteralPath $completionPath -Encoding UTF8");
            builder.AppendLine("Write-Host ''");
            builder.AppendLine("Write-Host ('[PSD2UIForm.AI] CLI process exited. exitCode=' + $exitCode)");
            builder.AppendLine("Write-Host '[PSD2UIForm.AI] Unity is reading the result files now. This window will close automatically.'");
            builder.AppendLine("exit $exitCode");
            return builder.ToString();
        }

        private string BuildVisibleBootstrapPrompt(AiJobContext context, AiAnalysisRequest request)
        {
            var builder = new StringBuilder(2048);
            string stageDisplay = request != null && !string.IsNullOrWhiteSpace(request.StageDisplayName)
                ? request.StageDisplayName
                : "AI Stage";
            builder.AppendLine($"You are running inside the native visible CLI for PSD2UIForm AI ({ProviderId}).");
            builder.AppendLine("Current task: " + stageDisplay);
            builder.AppendLine("This visible CLI window belongs to the current PSD2UIForm AI task. It is not an automatic retry.");
            builder.AppendLine();
            builder.AppendLine("Integration task:");
            builder.AppendLine("1. Read this UTF-8 task prompt file:");
            builder.AppendLine(context.PromptPath);
            builder.AppendLine("2. Follow the task prompt as the source of truth for PSD2UIForm analysis.");
            builder.AppendLine("3. The task prompt may say not to read local files or write files; for this visible CLI integration, this bootstrap prompt overrides that only as follows:");
            builder.AppendLine("   - You must read the task prompt file above.");
            builder.AppendLine("   - You may read local files explicitly referenced by that prompt.");
            builder.AppendLine("   - You must write the final " + AiProviderUtility.GetVisibleBootstrapResultLabel(request.ResultDocumentKind) + " to the staging result file below.");
            builder.AppendLine("   - The staging result file is the only success artifact Unity accepts. Assistant text and stream output are for display only.");
            builder.AppendLine("   - If this CLI exposes Bash, shell, or command execution tools, they remain forbidden unless the task prompt explicitly allows them.");
            builder.AppendLine("   - Do not write any other files unless the task prompt explicitly requires them.");
            builder.AppendLine("4. Before finishing, overwrite this UTF-8 staging result file with exactly one JSON object and nothing else:");
            builder.AppendLine(!string.IsNullOrWhiteSpace(request.OutputTempJsonPath) ? request.OutputTempJsonPath : request.OutputJsonPath);
            builder.AppendLine("5. Your final assistant response must repeat the same JSON object exactly, with no markdown fences and no surrounding prose.");
            builder.AppendLine("6. Do not emit planning/status text in assistant text responses. Keep intermediate progress in thinking/tool events only.");
            builder.AppendLine("7. Keep using the visible CLI normally so the user can see your live reasoning, tool calls, and progress in this terminal.");
            builder.AppendLine();
            builder.AppendLine("The Unity editor treats the validated staging result file as the success signal and may stop this CLI as soon as that file is ready.");
            return builder.ToString();
        }

        private static string ResolveWindowsPowerShellCommandPath(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return null;
            }

            if (Path.IsPathRooted(commandName))
            {
                return File.Exists(commandName) ? commandName : null;
            }

            var directories = BuildWindowsCliSearchDirectories();
            for (int i = 0; i < directories.Count; i++)
            {
                string directory = directories[i];
                if (string.IsNullOrWhiteSpace(directory)) continue;

                string ps1 = Path.Combine(directory, commandName + ".ps1");
                if (File.Exists(ps1))
                {
                    return ps1;
                }
            }

            for (int i = 0; i < directories.Count; i++)
            {
                string directory = directories[i];
                if (string.IsNullOrWhiteSpace(directory)) continue;

                string cmd = Path.Combine(directory, commandName + ".cmd");
                if (File.Exists(cmd))
                {
                    return cmd;
                }
            }

            return null;
        }

        private static List<string> BuildWindowsCliSearchDirectories()
        {
            var result = new List<string>(32);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddPathDirectories(result, seen, Environment.GetEnvironmentVariable("PATH"));
            AddPathDirectories(result, seen, TryGetEnvironmentPath(EnvironmentVariableTarget.User));
            AddPathDirectories(result, seen, TryGetEnvironmentPath(EnvironmentVariableTarget.Machine));
            return result;
        }

        private static void AddPathDirectories(List<string> result, HashSet<string> seen, string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return;
            }

            var parts = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string directory = parts[i].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(directory) && seen.Add(directory))
                {
                    result.Add(directory);
                }
            }
        }

        private static string TryGetEnvironmentPath(EnvironmentVariableTarget target)
        {
            try
            {
                return Environment.GetEnvironmentVariable("PATH", target);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        protected static string EscapePowerShellSingleQuoted(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }

        protected virtual string ResolveWorkingDirectory(AiAnalysisRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                return request.WorkingDirectory;
            }

            return Directory.GetCurrentDirectory();
        }

        protected static void AppendQuoted(List<string> args, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            args.Add($"\"{value.Replace("\"", "\\\"")}\"");
        }

        protected static string JoinArguments(List<string> args)
        {
            return args == null || args.Count < 1
                ? string.Empty
                : string.Join(" ", args);
        }

        private static void ValidateCliPathScope(AiJobContext context, AiAnalysisRequest request)
        {
            string workingDirectory = request.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new InvalidOperationException("CLI working directory is empty.");
            }

            ValidatePathWithinWorkingDirectory(context, workingDirectory, request.PromptTemplatePath, nameof(request.PromptTemplatePath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, request.AnalysisPackageJsonPath, nameof(request.AnalysisPackageJsonPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, request.OutputTempJsonPath, nameof(request.OutputTempJsonPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, request.OutputJsonPath, nameof(request.OutputJsonPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, GetRawOutputPath(context, request), nameof(request.OutputRawTextPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, GetCompletedPath(context, request), nameof(request.OutputCompletedPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, GetStreamOutputPath(context, request), nameof(request.StreamOutputPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, GetVisibleCliCompletionPath(context, request), nameof(request.VisibleCliCompletionPath));
            ValidatePathWithinWorkingDirectory(context, workingDirectory, context.PromptPath, nameof(context.PromptPath));

            if (request.ImageInputPaths == null)
            {
                return;
            }

            for (int i = 0; i < request.ImageInputPaths.Length; i++)
            {
                ValidatePathWithinWorkingDirectory(context, workingDirectory, request.ImageInputPaths[i], $"request.ImageInputPaths[{i}]");
            }

            AiJobFileUtility.AppendDebugLog(context, $"CLI path scope validation passed. workingDirectory={Path.GetFullPath(workingDirectory)}");
        }

        private static void ValidatePathWithinWorkingDirectory(AiJobContext context, string workingDirectory, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullWorkingDirectory = Path.GetFullPath(workingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(fullWorkingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AiJobFileUtility.AppendDebugLog(context, $"CLI path scope validation failed. label={label}, path={fullPath}, workingDirectory={fullWorkingDirectory}");
            throw new InvalidOperationException($"CLI path is outside working directory: {label} -> {fullPath}");
        }

        private static void KillProcessTree(Process process, AiJobContext context, string reason)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                AiJobFileUtility.AppendDebugLog(context, $"{reason}. Killing process tree pid={process.Id}");

                int pid = process.Id;
                TryKillChildProcesses(pid, context);
                process.Kill();
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"KillProcessTree failed. pid={(process != null ? process.Id : 0)}, error={ex.Message}");
            }
        }

        private static void TryKillChildProcesses(int pid, AiJobContext context)
        {
            if (pid <= 0)
            {
                return;
            }

            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using (var taskKill = new Process())
                    {
                        taskKill.StartInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/PID {pid} /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        taskKill.Start();
                        taskKill.WaitForExit(3000);
                    }
                    return;
                }

                using (var pkill = new Process())
                {
                    pkill.StartInfo = new ProcessStartInfo
                    {
                        FileName = "pkill",
                        Arguments = $"-TERM -P {pid}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    pkill.Start();
                    pkill.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"TryKillChildProcesses failed. pid={pid}, error={ex.Message}");
            }
        }

        private static bool TryFinalizeCompletedArtifacts(AiJobContext context, AiAnalysisRequest request, bool allowStreamRecovery, out string error)
        {
            error = null;
            if (context == null || request == null)
            {
                return false;
            }

            if (AiProviderUtility.TryCommitValidatedJsonDocument(request, allowRecoveredExplicitArtifact: true, out error))
            {
                EnsureCompletedMarker(context, request);
                AiJobFileUtility.AppendDebugLog(context, $"Detected valid JSON artifact. outputPath={request.OutputJsonPath}, kind={request.ResultDocumentKind}", mirrorToConsole: false);
                return true;
            }

            if (RequiresExplicitOutputFile(request) || request.DisableOutputRecovery)
            {
                return false;
            }

            string jsonSource = TryReadUtf8TextRelaxed(request.OutputRawTextPath);
            string rawOutputError = null;
            string streamRecoverError = null;
            if (!string.IsNullOrWhiteSpace(jsonSource))
            {
                if (AiProviderUtility.TryPersistJsonDocumentFromRawOutput(jsonSource, request, out error))
                {
                    EnsureCompletedMarker(context, request);
                    AiJobFileUtility.AppendDebugLog(context, $"JSON extracted successfully from final assistant message artifact. outputPath={request.OutputJsonPath}", mirrorToConsole: false);
                    return true;
                }

                rawOutputError = error;
            }

            if (allowStreamRecovery
                && AiProviderUtility.TryExtractAssistantTextFromStreamOutput(GetStreamOutputPath(context, request), out var recoveredText, out streamRecoverError)
                && !string.IsNullOrWhiteSpace(recoveredText))
            {
                if (!AiProviderUtility.CouldContainFinalJsonDocument(request.ResultDocumentKind, recoveredText))
                {
                    error = !string.IsNullOrWhiteSpace(rawOutputError) ? rawOutputError : null;
                    return false;
                }

                if (AiProviderUtility.TryPersistJsonDocumentFromRawOutput(recoveredText, request, out error))
                {
                    if (!string.IsNullOrWhiteSpace(request.OutputRawTextPath))
                    {
                        AiJobFileUtility.WriteText(request.OutputRawTextPath, recoveredText);
                    }

                    EnsureCompletedMarker(context, request);
                    AiJobFileUtility.AppendDebugLog(context, $"JSON recovered successfully from stream output. outputPath={request.OutputJsonPath}", mirrorToConsole: false);
                    return true;
                }
            }

            error = !string.IsNullOrWhiteSpace(rawOutputError)
                ? rawOutputError
                : (!string.IsNullOrWhiteSpace(error) ? error : streamRecoverError);
            return false;
        }

        private static bool TryFinalizeRunningArtifacts(AiJobContext context, AiAnalysisRequest request, out string error)
        {
            error = null;
            if (context == null || request == null)
            {
                return false;
            }

            if (!AiProviderUtility.TryCommitValidatedJsonDocument(request, allowRecoveredExplicitArtifact: false, out error))
            {
                return false;
            }

            EnsureCompletedMarker(context, request);
            AiJobFileUtility.AppendDebugLog(context, $"Detected valid staged JSON artifact while CLI is still running. outputPath={request.OutputJsonPath}, kind={request.ResultDocumentKind}", mirrorToConsole: false);
            return true;
        }

        private static bool RequiresExplicitOutputFile(AiAnalysisRequest request)
        {
            return request != null && request.RequireExplicitOutputJsonFile;
        }

        private static void EnsureCompletedMarker(AiJobContext context, AiAnalysisRequest request)
        {
            string completedPath = !string.IsNullOrWhiteSpace(request.OutputCompletedPath)
                ? request.OutputCompletedPath
                : context.CompletedPath;
            if (string.IsNullOrWhiteSpace(completedPath) || File.Exists(completedPath))
            {
                return;
            }

            AiJobFileUtility.WriteText(completedPath, string.Empty);
        }

        private static string TryReadUtf8TextRelaxed(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Utf8NoBom, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildCliFailureMessage(AiJobContext context, AiAnalysisRequest request, string stderr, int? exitCode, string fallback = null)
        {
            if (AiProviderUtility.TryResolveCliFailureMessage(
                    GetStreamOutputPath(context, request),
                    GetRawOutputPath(context, request),
                    stderr,
                    out var resolved)
                && !string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return exitCode.HasValue
                ? $"CLI provider '{ProviderId}' exited with code {exitCode.Value}."
                : $"CLI provider '{ProviderId}' failed.";
        }

        private static string GetRawOutputPath(AiJobContext context, AiAnalysisRequest request)
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.OutputRawTextPath))
            {
                return request.OutputRawTextPath;
            }

            return context != null ? context.RawOutputPath : null;
        }

        private static string GetStreamOutputPath(AiJobContext context, AiAnalysisRequest request)
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.StreamOutputPath))
            {
                return request.StreamOutputPath;
            }

            return context != null ? context.StreamOutputPath : null;
        }

        private static string GetCompletedPath(AiJobContext context, AiAnalysisRequest request)
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.OutputCompletedPath))
            {
                return request.OutputCompletedPath;
            }

            return context != null ? context.CompletedPath : null;
        }

        private static string GetVisibleCliCompletionPath(AiJobContext context, AiAnalysisRequest request)
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.VisibleCliCompletionPath))
            {
                return request.VisibleCliCompletionPath;
            }

            return context != null ? context.VisibleCliCompletionPath : null;
        }

        private static void ResetTransientArtifacts(AiJobContext context, AiAnalysisRequest request)
        {
            DeleteFileIfExists(GetRawOutputPath(context, request));
            DeleteFileIfExists(GetStreamOutputPath(context, request));
            DeleteFileIfExists(GetVisibleCliCompletionPath(context, request));
            DeleteFileIfExists(GetCompletedPath(context, request));
        }

        private static void DeleteFileIfExists(string path)
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

        private void HandleOutputLine(
            AiJobContext context,
            AiAnalysisRequest request,
            string line,
            bool isError,
            StringBuilder builder,
            object syncRoot,
            StreamState streamState,
            OutputCaptureState captureState)
        {
            if (line == null)
            {
                return;
            }

            lock (syncRoot)
            {
                builder.AppendLine(line);
                AppendCapturedOutputLine(captureState, line);
                AppendVisibleDisplayLines(captureState, ConvertVisibleCliLineToDisplayLine(line, captureState));
            }

            string detail = AiProviderUtility.ExtractProgressDetail(line);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                AiJobFileUtility.WriteStatusDetail(context, detail);
            }

            if (!isError
                && streamState != null
                && AiProviderUtility.TryParseCodexTerminalEvent(line, out var terminalKind, out var terminalMessage)
                && terminalKind != AiStreamTerminalEventKind.None)
            {
                streamState.TerminalEventKind = terminalKind;
                streamState.TerminalEventMessage = terminalMessage ?? string.Empty;
                AiJobFileUtility.AppendDebugLog(context, $"Terminal stream event observed. kind={terminalKind}, message={streamState.TerminalEventMessage}", mirrorToConsole: false);
            }

            LogCapturedProcessLine(context, captureState, line, isError);
        }

        private static void LogCapturedProcessLine(AiJobContext context, OutputCaptureState captureState, string line, bool isError)
        {
            if (context == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            bool rawStreamCaptured = captureState != null && captureState.RawStreamWriter != null;
            if (rawStreamCaptured && !isError)
            {
                return;
            }

            string normalizedLine = line.Trim();
            const int maxDebugLineLength = 1200;
            if (normalizedLine.Length > maxDebugLineLength)
            {
                normalizedLine = normalizedLine.Substring(0, maxDebugLineLength) + "...";
            }

            AiJobFileUtility.AppendDebugLog(context, $"{(isError ? "STDERR" : "STDOUT")}: {normalizedLine}", mirrorToConsole: isError);
        }

        private OutputCaptureState CreateOutputCaptureState(AiJobContext context, AiAnalysisRequest request, bool visibleDisplayEnabled)
        {
            var state = new OutputCaptureState
            {
                VisibleDisplayEnabled = visibleDisplayEnabled
            };

            string rawStreamPath = GetStreamOutputPath(context, request);
            if (!string.IsNullOrWhiteSpace(rawStreamPath))
            {
                AiJobFileUtility.EnsureDirectory(Path.GetDirectoryName(rawStreamPath));
                DeleteFileIfExists(rawStreamPath);
                state.RawStreamWriter = new StreamWriter(new FileStream(rawStreamPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8NoBom)
                {
                    AutoFlush = true
                };
            }

            if (visibleDisplayEnabled)
            {
                string displayPath = GetVisibleDisplayLogPath(context, request);
                AiJobFileUtility.EnsureDirectory(Path.GetDirectoryName(displayPath));
                DeleteFileIfExists(displayPath);
                state.DisplayWriter = new StreamWriter(new FileStream(displayPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8NoBom)
                {
                    AutoFlush = true
                };
                state.DisplayWriter.WriteLine("[PSD2UIForm.AI] Visible CLI log mirror");
                state.DisplayWriter.WriteLine("[PSD2UIForm.AI] Provider: " + ProviderId);
                state.DisplayWriter.WriteLine("[PSD2UIForm.AI] Stage: " + (request != null ? request.StageDisplayName : string.Empty));
                state.DisplayWriter.WriteLine(string.Empty);
            }

            return state;
        }

        private static void AppendCapturedOutputLine(OutputCaptureState state, string line)
        {
            if (state == null || state.RawStreamWriter == null || line == null)
            {
                return;
            }

            state.RawStreamWriter.WriteLine(line);
        }

        private static void AppendVisibleDisplayLines(OutputCaptureState state, List<string> displayLines)
        {
            if (state == null || !state.VisibleDisplayEnabled || state.DisplayWriter == null || displayLines == null || displayLines.Count < 1)
            {
                return;
            }

            for (int i = 0; i < displayLines.Count; i++)
            {
                string displayLine = displayLines[i];
                if (!string.IsNullOrWhiteSpace(displayLine))
                {
                    state.DisplayWriter.WriteLine(displayLine);
                }
            }
        }

        private List<string> ConvertVisibleCliLineToDisplayLine(string rawLine, OutputCaptureState state)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || state == null || !state.VisibleDisplayEnabled)
            {
                return null;
            }

            switch (ProviderId)
            {
                case "claude-code-cli":
                    return ConvertClaudeVisibleCliLine(rawLine, state);
                case "opencode-cli":
                    return ConvertOpenCodeVisibleCliLine(rawLine, state);
                case "codex-cli":
                    return ConvertCodexVisibleCliLine(rawLine, state);
                default:
                    return CreateSingleDisplayLine(rawLine);
            }
        }

        private static List<string> ConvertClaudeVisibleCliLine(string rawLine, OutputCaptureState state)
        {
            ClaudeVisibleEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<ClaudeVisibleEnvelope>(rawLine);
            }
            catch
            {
                return CreateSingleDisplayLine(rawLine);
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            {
                return null;
            }

            var lines = new List<string>(2);
            switch (envelope.type)
            {
                case "system":
                    if (string.Equals(envelope.subtype, "init", StringComparison.Ordinal))
                    {
                        int toolCount = envelope.tools != null ? envelope.tools.Length : 0;
                        lines.Add("==> Claude session started (model: " + envelope.model + ", tools: " + toolCount + ")");
                    }
                    break;

                case "stream_event":
                    if (envelope.@event == null || string.IsNullOrWhiteSpace(envelope.@event.type))
                    {
                        break;
                    }

                    if (string.Equals(envelope.@event.type, "content_block_start", StringComparison.Ordinal))
                    {
                        string blockType = envelope.@event.content_block != null ? envelope.@event.content_block.type : string.Empty;
                        state.ClaudeContentBlockType = blockType ?? string.Empty;
                        if (string.Equals(blockType, "thinking", StringComparison.Ordinal))
                        {
                            if (!state.ClaudeThinkingHeaderShown)
                            {
                                lines.Add("--- Thinking ---");
                                state.ClaudeThinkingHeaderShown = true;
                            }
                        }
                        else if (string.Equals(blockType, "tool_use", StringComparison.Ordinal))
                        {
                            string toolName = envelope.@event.content_block != null ? envelope.@event.content_block.name : string.Empty;
                            lines.Add("[Tool: " + toolName + "]");
                        }
                        else if (string.Equals(blockType, "text", StringComparison.Ordinal))
                        {
                            if (!state.ClaudeResponseHeaderShown)
                            {
                                lines.Add("--- Response ---");
                                state.ClaudeResponseHeaderShown = true;
                            }
                        }
                    }
                    else if (string.Equals(envelope.@event.type, "content_block_delta", StringComparison.Ordinal))
                    {
                        string deltaType = envelope.@event.delta != null ? envelope.@event.delta.type : string.Empty;
                        if (string.Equals(deltaType, "thinking_delta", StringComparison.Ordinal))
                        {
                            AppendNormalizedChunk(state.ClaudeThinkingBuffer, envelope.@event.delta.thinking);
                        }
                        else if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
                        {
                            AppendNormalizedChunk(state.ClaudeResponseBuffer, envelope.@event.delta.text);
                        }
                    }
                    else if (string.Equals(envelope.@event.type, "content_block_stop", StringComparison.Ordinal))
                    {
                        FlushClaudeBuffer(state, lines);
                    }
                    break;

                case "user":
                    if (envelope.message != null && envelope.message.content != null && envelope.message.content.Length > 0)
                    {
                        var content = envelope.message.content[0];
                        if (content != null && string.Equals(content.type, "tool_result", StringComparison.Ordinal))
                        {
                            string toolResult = NormalizeSingleLine(content.content, 240);
                            if (!string.IsNullOrWhiteSpace(toolResult))
                            {
                                lines.Add("  -> " + toolResult);
                            }
                        }
                    }
                    break;

                case "result":
                    FlushClaudeBuffer(state, lines);
                    lines.Add("==> Done. Duration: " + envelope.duration_ms + "ms");
                    break;
            }

            return lines.Count > 0 ? lines : null;
        }

        private static List<string> ConvertOpenCodeVisibleCliLine(string rawLine, OutputCaptureState state)
        {
            OpenCodeVisibleEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<OpenCodeVisibleEnvelope>(rawLine);
            }
            catch
            {
                return CreateSingleDisplayLine(rawLine);
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            {
                return null;
            }

            var lines = new List<string>(2);
            switch (envelope.type)
            {
                case "step_start":
                    lines.Add("==> Phase: running task");
                    break;

                case "reasoning":
                    if (!state.OpenCodeThinkingHeaderShown)
                    {
                        lines.Add("--- Thinking ---");
                        state.OpenCodeThinkingHeaderShown = true;
                    }
                    lines.Add(NormalizeSingleLine(envelope.part != null ? envelope.part.text : string.Empty, 600));
                    break;

                case "text":
                    if (!state.OpenCodeResponseHeaderShown)
                    {
                        lines.Add("--- Response ---");
                        state.OpenCodeResponseHeaderShown = true;
                    }
                    lines.Add(NormalizeSingleLine(envelope.part != null ? envelope.part.text : string.Empty, 600));
                    break;

                case "step_finish":
                    int outputTokens = envelope.part != null && envelope.part.tokens != null ? envelope.part.tokens.output : 0;
                    int reasoningTokens = envelope.part != null && envelope.part.tokens != null ? envelope.part.tokens.reasoning : 0;
                    if (outputTokens > 0 && reasoningTokens > 0)
                    {
                        lines.Add("==> Result: step completed (output=" + outputTokens + ", reasoning=" + reasoningTokens + ")");
                    }
                    else if (outputTokens > 0)
                    {
                        lines.Add("==> Result: step completed (output=" + outputTokens + ")");
                    }
                    else
                    {
                        lines.Add("==> Result: step completed");
                    }
                    break;

                case "error":
                    lines.Add("ERROR: " + NormalizeSingleLine(envelope.part != null ? envelope.part.text : string.Empty, 320));
                    break;
            }

            return FilterDisplayLines(lines);
        }

        private static List<string> ConvertCodexVisibleCliLine(string rawLine, OutputCaptureState state)
        {
            CodexVisibleEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<CodexVisibleEnvelope>(rawLine);
            }
            catch
            {
                return CreateSingleDisplayLine(rawLine);
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            {
                return null;
            }

            var lines = new List<string>(2);
            switch (envelope.type)
            {
                case "thread.started":
                case "session.started":
                    lines.Add("==> Codex session started");
                    break;

                case "turn.started":
                    lines.Add("==> Phase: running task");
                    break;

                case "turn.completed":
                    int outputTokens = envelope.usage != null ? envelope.usage.output_tokens : 0;
                    int reasoningTokens = envelope.usage != null ? envelope.usage.reasoning_output_tokens : 0;
                    if (outputTokens > 0 && reasoningTokens > 0)
                    {
                        lines.Add("==> Result: task completed (output=" + outputTokens + ", reasoning=" + reasoningTokens + ")");
                    }
                    else if (outputTokens > 0)
                    {
                        lines.Add("==> Result: task completed (output=" + outputTokens + ")");
                    }
                    else
                    {
                        lines.Add("==> Result: task completed");
                    }
                    break;

                case "turn.failed":
                case "session.failed":
                case "error":
                    lines.Add("ERROR: " + ResolveCodexErrorText(envelope));
                    break;

                case "item.started":
                case "item.completed":
                    if (envelope.item != null)
                    {
                        string phase = ResolveCodexPhase(envelope.item.type);
                        if (!string.IsNullOrWhiteSpace(phase) && ShouldEmitDedupe(state, "codex-phase", phase))
                        {
                            lines.Add("==> Phase: " + phase);
                        }

                        string itemLabel = ResolveCodexItemLabel(envelope.item);
                        if (string.Equals(envelope.item.status, "failed", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add("ERROR: " + itemLabel);
                        }
                        else if (string.Equals(envelope.item.type, "agent_message", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(envelope.type, "item.completed", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add("[Result] " + ResolveCodexAgentMessage(envelope.item));
                        }
                        else if (string.Equals(envelope.type, "item.completed", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add("[Item] Done: " + itemLabel);
                        }
                        else
                        {
                            lines.Add("[Item] " + itemLabel);
                        }
                    }
                    break;
            }

            return FilterDisplayLines(lines);
        }

        private static void FlushClaudeBuffer(OutputCaptureState state, List<string> lines)
        {
            if (state == null || lines == null)
            {
                return;
            }

            if (string.Equals(state.ClaudeContentBlockType, "thinking", StringComparison.Ordinal) && state.ClaudeThinkingBuffer.Length > 0)
            {
                lines.Add(NormalizeSingleLine(state.ClaudeThinkingBuffer.ToString(), 600));
                state.ClaudeThinkingBuffer.Length = 0;
            }
            else if (string.Equals(state.ClaudeContentBlockType, "text", StringComparison.Ordinal) && state.ClaudeResponseBuffer.Length > 0)
            {
                lines.Add(NormalizeSingleLine(state.ClaudeResponseBuffer.ToString(), 600));
                state.ClaudeResponseBuffer.Length = 0;
            }

            state.ClaudeContentBlockType = string.Empty;
        }

        private static void AppendNormalizedChunk(StringBuilder builder, string chunk)
        {
            if (builder == null || string.IsNullOrWhiteSpace(chunk))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(NormalizeSingleLine(chunk, 10000));
        }

        private static bool ShouldEmitDedupe(OutputCaptureState state, string key, string value)
        {
            if (state == null || string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            if (state.Dedupe.TryGetValue(key, out var lastValue) && string.Equals(lastValue, value, StringComparison.Ordinal))
            {
                return false;
            }

            state.Dedupe[key] = value ?? string.Empty;
            return true;
        }

        private static string ResolveCodexPhase(string itemType)
        {
            switch (itemType)
            {
                case "mcp_tool_call":
                case "shell_command":
                case "function_call":
                    return "executing tools";
                case "reasoning":
                    return "reasoning";
                case "agent_message":
                    return "assembling result";
                default:
                    return string.Empty;
            }
        }

        private static string ResolveCodexItemLabel(CodexVisibleItem item)
        {
            if (item == null)
            {
                return "item";
            }

            if (!string.IsNullOrWhiteSpace(item.command))
            {
                return NormalizeSingleLine(item.command, 180);
            }

            if (item.arguments != null && !string.IsNullOrWhiteSpace(item.arguments.title))
            {
                return NormalizeSingleLine(item.arguments.title, 180);
            }

            if (!string.IsNullOrWhiteSpace(item.server) && !string.IsNullOrWhiteSpace(item.tool))
            {
                return NormalizeSingleLine(item.server + "/" + item.tool, 180);
            }

            if (!string.IsNullOrWhiteSpace(item.tool))
            {
                return NormalizeSingleLine(item.tool, 180);
            }

            if (!string.IsNullOrWhiteSpace(item.type))
            {
                return NormalizeSingleLine(item.type, 180);
            }

            return "item";
        }

        private static string ResolveCodexAgentMessage(CodexVisibleItem item)
        {
            if (item == null)
            {
                return "assistant response generated";
            }

            if (!string.IsNullOrWhiteSpace(item.text))
            {
                string trimmed = item.text.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    return "assistant response generated";
                }

                return NormalizeSingleLine(item.text, 240);
            }

            return "assistant response generated";
        }

        private static string ResolveCodexErrorText(CodexVisibleEnvelope envelope)
        {
            if (envelope == null)
            {
                return "codex error";
            }

            if (!string.IsNullOrWhiteSpace(envelope.message))
            {
                return NormalizeSingleLine(envelope.message, 320);
            }

            if (!string.IsNullOrWhiteSpace(envelope.summary))
            {
                return NormalizeSingleLine(envelope.summary, 320);
            }

            if (!string.IsNullOrWhiteSpace(envelope.title))
            {
                return NormalizeSingleLine(envelope.title, 320);
            }

            if (!string.IsNullOrWhiteSpace(envelope.content))
            {
                return NormalizeSingleLine(envelope.content, 320);
            }

            if (envelope.error != null)
            {
                if (!string.IsNullOrWhiteSpace(envelope.error.message))
                {
                    return NormalizeSingleLine(envelope.error.message, 320);
                }

                if (!string.IsNullOrWhiteSpace(envelope.error.text))
                {
                    return NormalizeSingleLine(envelope.error.text, 320);
                }
            }

            if (envelope.item != null && envelope.item.error != null)
            {
                if (!string.IsNullOrWhiteSpace(envelope.item.error.message))
                {
                    return NormalizeSingleLine(envelope.item.error.message, 320);
                }

                if (!string.IsNullOrWhiteSpace(envelope.item.error.text))
                {
                    return NormalizeSingleLine(envelope.item.error.text, 320);
                }
            }

            return "codex error";
        }

        private static List<string> CreateSingleDisplayLine(string text)
        {
            string normalized = NormalizeSingleLine(text, 600);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return new List<string> { normalized };
        }

        private static List<string> FilterDisplayLines(List<string> lines)
        {
            if (lines == null || lines.Count < 1)
            {
                return null;
            }

            var filtered = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string normalized = NormalizeSingleLine(lines[i], 600);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    filtered.Add(normalized);
                }
            }

            return filtered.Count > 0 ? filtered : null;
        }

        private static string NormalizeSingleLine(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (normalized.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("  ", " ");
            }

            if (maxLength > 0 && normalized.Length > maxLength)
            {
                normalized = normalized.Substring(0, maxLength) + "...";
            }

            return normalized;
        }

        private void TryLaunchVisibleCliLogViewer(AiJobContext context, AiAnalysisRequest request, bool visibleDisplayEnabled)
        {
            if (!visibleDisplayEnabled)
            {
                return;
            }

            try
            {
                string displayLogPath = GetVisibleDisplayLogPath(context, request);
                AiCliDebugConsole.TryLaunch(context, ProviderId, displayLogPath, request != null ? request.StageDisplayName : null);
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI log viewer launch failed. error={ex.Message}");
            }
        }

        private static void TrySignalVisibleCliLogViewerClose(AiJobContext context, bool visibleDisplayEnabled)
        {
            if (!visibleDisplayEnabled || context == null || string.IsNullOrWhiteSpace(context.DebugConsoleCloseSignalPath))
            {
                return;
            }

            try
            {
                AiJobFileUtility.WriteText(context.DebugConsoleCloseSignalPath, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                AiJobFileUtility.AppendDebugLog(context, $"Visible CLI log viewer close signal failed. error={ex.Message}");
            }
        }

        private static string GetVisibleDisplayLogPath(AiJobContext context, AiAnalysisRequest request)
        {
            string streamPath = GetStreamOutputPath(context, request);
            if (!string.IsNullOrWhiteSpace(streamPath))
            {
                string directory = Path.GetDirectoryName(streamPath);
                string fileName = Path.GetFileNameWithoutExtension(streamPath);
                return Path.Combine(directory ?? string.Empty, fileName + ".display.log");
            }

            return Path.Combine(context != null ? context.ResponseDirectory : Directory.GetCurrentDirectory(), "visible-cli.display.log");
        }

        private static bool IsMacOS()
        {
            return Environment.OSVersion.Platform == PlatformID.MacOSX
                || (Environment.OSVersion.Platform == PlatformID.Unix && Directory.Exists("/Applications/Terminal.app"));
        }

        private const string VisibleCliDisplayFrameworkFunction = @"
function Write-VisibleCliDisplayEvent {
    param($Event)

    if ($null -eq $Event) { return }

    if ($Event -is [System.Collections.IEnumerable] -and -not ($Event -is [string]) -and -not $Event.PSObject.Properties['text']) {
        foreach ($entry in $Event) {
            Write-VisibleCliDisplayEvent $entry
        }
        return
    }

    $newlineOnly = $false
    if ($Event.PSObject.Properties['newline']) {
        $newlineOnly = [bool]$Event.newline
    }
    if ($newlineOnly) {
        Write-Host ''
        return
    }

    $text = [string]$Event
    if ($Event.PSObject.Properties['text']) {
        $text = [string]$Event.text
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return }

    $dedupeKey = ''
    if ($Event.PSObject.Properties['dedupeKey']) {
        $dedupeKey = [string]$Event.dedupeKey
    }

    $dedupeValue = $text
    if ($Event.PSObject.Properties['dedupeValue']) {
        $dedupeValue = [string]$Event.dedupeValue
    }
    if (-not [string]::IsNullOrWhiteSpace($dedupeKey)) {
        $lastValue = $script:visibleCliEventDedupe[$dedupeKey]
        if ($lastValue -eq $dedupeValue) {
            return
        }

        $script:visibleCliEventDedupe[$dedupeKey] = $dedupeValue
    }

    $blankLineBefore = $false
    if ($Event.PSObject.Properties['blankLineBefore']) {
        $blankLineBefore = [bool]$Event.blankLineBefore
    }
    if ($blankLineBefore) {
        Write-Host ''
    }

    $noNewline = $false
    if ($Event.PSObject.Properties['noNewline']) {
        $noNewline = [bool]$Event.noNewline
    }

    $color = 'White'
    if ($Event.PSObject.Properties['color'] -and -not [string]::IsNullOrWhiteSpace([string]$Event.color)) {
        $color = [string]$Event.color
    }

    if ($noNewline) {
        Write-Host -NoNewline $text -ForegroundColor $color
    } else {
        Write-Host $text -ForegroundColor $color
    }
}

function Invoke-VisibleCliStreamAdapter {
    param([object]$Chunk, [string]$AdapterFunctionName)

    if ($null -eq $Chunk) { return }

    $line = [string]$Chunk
    if ([string]::IsNullOrWhiteSpace($line)) { return }

    if ($script:streamWriter -ne $null) {
        $script:streamWriter.WriteLine($line)
        $script:streamWriter.Flush()
    }

    try {
        $events = & $AdapterFunctionName $line
        Write-VisibleCliDisplayEvent $events
    }
    catch {
        Write-Host $line -ForegroundColor DarkGray
    }
}
";
    }
}
#endif
