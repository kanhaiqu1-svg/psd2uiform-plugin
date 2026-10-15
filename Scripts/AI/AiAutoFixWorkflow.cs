#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UGF.EditorTools.Psd2UGUI.Cli;
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiAutoFixWorkflow
    {
        private static readonly AiJobManager Jobs = new AiJobManager();

        private sealed class Listener : IAiJobListener
        {
            private readonly Psd2UIFormConverter converter;
            internal Listener(Psd2UIFormConverter converter) { this.converter = converter; }
            public void OnAiJobCompleted(AiJobContext context)
            {
                if (converter == null) return;
                if (!Apply(converter, context, out var report, out var error))
                    EditorUtility.DisplayDialog("AI 识别", error, "确定");
                else EditorUtility.DisplayDialog("AI 识别", BuildRecognitionDurationLine(context) + "\n" + Summary(report), "确定");
            }
            public void OnAiJobFailed(AiJobContext context, string errorMessage)
                => EditorUtility.DisplayDialog("AI 识别", errorMessage, "确定");
        }

        internal static bool Start(Psd2UIFormConverter converter, out string error)
        {
            error = null;
            if (Jobs.HasRunningJobs) { error = "已有 AI 任务正在执行。"; return false; }
            var provider = AiProviderFactory.Create(UGUIParser.Instance);
            if (!Prepare(converter, provider, out var context, out var request, out error)) return false;
            Jobs.StartJob(context, provider, new Listener(converter), (job, cancellation) => {
                request.CancellationToken = cancellation;
                ExecuteRecognition(provider, job, request);
            });
            return true;
        }

        internal static bool ApplyCurrentResult(Psd2UIFormConverter converter, out string error)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string directory = AiJobManager.ResolveLatestScopedJobDirectory(Path.Combine(root, AiPathDefaults.JobsRootRelative), converter.PsdAssetName);
            var context = AiJobManager.ContextForDirectory(directory);
            return Apply(converter, context, out _, out error);
        }

        internal static bool RunBlocking(Psd2UIFormConverter converter, bool apply, out AiJobContext context, out string error, string providerIdOverride = null)
        {
            context = null; error = null;
            if (Jobs.HasRunningJobs) { error = "已有 AI 任务正在执行。"; return false; }
            var provider = string.IsNullOrEmpty(providerIdOverride) ? AiProviderFactory.Create(UGUIParser.Instance) : AiProviderFactory.CreateById(providerIdOverride);
            if (!Prepare(converter, provider, out context, out var request, out error)) return false;
            try
            {
                ExecuteRecognition(provider, context, request);
                return !apply || Apply(converter, context, out _, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static bool Prepare(Psd2UIFormConverter converter, CliAiProvider provider, out AiJobContext context, out AiAnalysisRequest request, out string error)
        {
            context = null; request = null; error = null;
            if (converter == null || provider == null || UGUIParser.Instance == null)
            { error = "转换器或 AI Provider 配置不可用。"; return false; }
            try
            {
                if (CliCommandResolver.ResolveCommandPath(provider.Executable) == null)
                {
                    error = $"检测到您未安装 {provider.ProviderId} CLI。\n请安装并登录，确认CLI可成功对话后，再重试AI自动识别。";
                    return false;
                }
                string root = Directory.GetParent(Application.dataPath).FullName;
                context = Jobs.CreateJob(provider.ProviderId, Path.Combine(root, AiPathDefaults.JobsRootRelative), converter.PsdAssetName);
                if (!new AiAnalysisBundleBuilder().Build(converter, context, out var hash, out error)) return false;
                if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package))
                { error = "无法读取素材证据包。"; return false; }
                // 本轮分析包已生成成功，此刻才清掉上一轮结果：更早失败（分析包没出来）时旧结果仍可"应用AI结果"。
                AiJobManager.ClearStaleRecognitionResults(context);
                AiJobFileUtility.WriteJson(context.MetaPath, new AiJobMetaDocument {
                    jobId = context.JobId, providerId = provider.ProviderId, createdAtUtc = DateTime.UtcNow.ToString("o"),
                    projectPath = root, psdAssetPath = converter.PsdAssetName, treeHash = hash,
                    analysisPackageVersion = AiProtocolVersions.AnalysisPackage, recognitionVersion = AiProtocolVersions.Recognition
                });
                // Unity image operations and asset access run on the editor thread, before provider dispatch.
                if (!new AiRecognitionInputBuilder().Build(context, package, root, out var manifest, out error)) return false;
                string template = AiPathDefaults.ToAbsolutePluginPath(root, AiPathDefaults.CombinedRecognitionPromptTemplateRelative);
                string prompt = File.ReadAllText(template, System.Text.Encoding.UTF8).Replace("[RECOGNITION_VERSION]", AiProtocolVersions.Recognition);
                AiJobFileUtility.WriteText(context.PromptPath, prompt);
                request = new AiAnalysisRequest {
                    StageName = "recognition", StageDisplayName = "AI 正在识别目标 UI 树", ResultDocumentKind = AiResultDocumentKind.Recognition,
                    AnalysisPackageJsonPath = context.RecognitionInputManifestPath, PromptTemplatePath = context.PromptPath,
                    OutputJsonPath = context.RecognitionPath, OutputTempJsonPath = context.RecognitionTempPath,
                    OutputRawTextPath = context.RawOutputPath, OutputCompletedPath = context.CompletedPath,
                    StreamOutputPath = context.StreamOutputPath, VisibleCliCompletionPath = context.VisibleCliCompletionPath,
                    WorkingDirectory = context.JobDirectory, ImageInputPaths = AiRecognitionInputBuilder.ResolveImageInputs(manifest, context.JobDirectory),
                    AllowVisibleCliExecution = UGUIParser.Instance.AiProviderConfig.showCliWindow,
                    RequireExplicitOutputJsonFile = true, DisableOutputRecovery = true
                };
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        internal static void ExecuteRecognition(IAiProvider provider, AiJobContext context, AiAnalysisRequest request)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            try { AiJobFileUtility.WriteStatusStage(context, "AI 正在识别主 UI、子元素和素材归属"); }
            catch (IOException ex) { Debug.LogWarning("AI 进度写入失败: " + ex.Message); }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            string providerError = null;
            try { provider.ExecuteJob(context, request); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { providerError = ex.Message; }
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!AiProviderUtility.TryCommitValidatedJsonDocument(request, out var commitError))
                throw new InvalidOperationException(providerError ?? commitError);
            if (!AiRecognitionResultLoader.TryRead(context.RecognitionPath, out var document, out var error))
                throw new InvalidOperationException(error);
            if (providerError != null) AiRecognitionResultLoader.AddIssue(document.issues, "", "provider_exit", providerError);
            long initialOutputBytes = new FileInfo(context.RecognitionPath).Length;
            var repairMetrics = AiCompositionRepair.Run(provider, context, request, document);
            // The committed recognition file remains usable even if optional diagnostics cannot be saved.
            try { AiJobFileUtility.WriteText(context.RecognitionPath, AiRecognitionResultLoader.Serialize(document)); }
            catch (Exception ex) { Debug.LogWarning("AI 识别诊断写入失败: " + ex.Message); }
            try
            {
                var report = new AiRecognitionRunReport {
                    status = document.issues.Count == 0 ? "recognized" : "partial_recognition",
                    cliMilliseconds = timer.ElapsedMilliseconds, elapsedMilliseconds = timer.ElapsedMilliseconds,
                    outputTextBytes = initialOutputBytes + repairMetrics.outputTextBytes,
                    compositionRepairCalls = repairMetrics.compositionRepairCalls,
                    compositionRepairMilliseconds = repairMetrics.compositionRepairMilliseconds,
                    repairedCompositions = repairMetrics.repairedCompositions,
                    targetNodeCount = document.ui.Count, pendingItemCount = document.issues.Count, issues = document.issues
                };
                if (AiJobFileUtility.TryReadJson(Path.Combine(context.JobDirectory, "export-metrics.json"), out AiRecognitionRunReport export))
                { report.exportMilliseconds = export.exportMilliseconds; report.elapsedMilliseconds += export.exportMilliseconds; }
                report.inputTextBytes = repairMetrics.inputTextBytes + new FileInfo(context.PromptPath).Length + new FileInfo(context.RecognitionInputManifestPath).Length + new FileInfo(Path.Combine(context.JobDirectory, "nodes.tsv")).Length;
                report.imageInputBytes = repairMetrics.imageInputBytes;
                foreach (string path in request.ImageInputPaths) report.imageInputBytes += new FileInfo(path).Length;
                AiRecognitionTelemetry.Read(provider.ProviderId, context.StreamOutputPath, report);
                if (!string.IsNullOrEmpty(repairMetrics.telemetryError))
                    report.telemetryError = (report.telemetryError ?? "") + " 局部修正统计: " + repairMetrics.telemetryError;
                if (repairMetrics.fileReadCount >= 0 && report.fileReadCount >= 0)
                {
                    report.fileReadCount += repairMetrics.fileReadCount;
                    report.repeatedFileReadCount += repairMetrics.repeatedFileReadCount;
                    report.contextCompactionCount += repairMetrics.contextCompactionCount;
                    report.contextCompactionEstimated |= repairMetrics.contextCompactionEstimated;
                }
                AiJobFileUtility.WriteJson(Path.Combine(context.JobDirectory, "recognition-metrics.json"), report);
            }
            catch (Exception ex) { Debug.LogWarning("AI 性能记录写入失败，识别结果仍可应用: " + ex.Message); }
        }

        internal static bool Apply(Psd2UIFormConverter converter, AiJobContext context, out AiUiApplyReport report, out string error)
        {
            report = null; error = null;
            if (!AiJobFileUtility.TryReadJson(context.MetaPath, out AiJobMetaDocument meta)
                || !string.Equals(meta.psdAssetPath?.Replace('\\', '/'), converter.PsdAssetName?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            { error = "识别结果不属于当前 PSD。"; return false; }
            if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package)
                || package.version != AiProtocolVersions.AnalysisPackage)
            { error = "素材证据包不可用或版本已更新，请重新识别。"; return false; }
            if (!AiRecognitionResultLoader.TryRead(context.RecognitionPath, out var document, out error)) return false;
            var plan = AiUiPlan.Build(package, document);
            report = AiUiCompiler.Apply(converter, package, plan);
            try { AiJobFileUtility.WriteStatusDetail(context, Summary(report)); }
            catch (Exception ex) { AiRecognitionResultLoader.AddIssue(report.issues, "", "status_write_failed", ex.Message); }
            try { AiJobFileUtility.WriteJson(Path.Combine(context.JobDirectory, "apply-report.json"), report); }
            catch (Exception ex)
            {
                AiRecognitionResultLoader.AddIssue(report.issues, "", "report_write_failed", ex.Message);
                Debug.LogWarning("AI 应用报告写入失败，已应用的节点保留: " + ex.Message);
            }
            if (report.appliedNodes == 0) { error = Summary(report); return false; }
            return true;
        }

        private static string Summary(AiUiApplyReport report) => $"已应用 {report.appliedNodes}/{report.declaredNodes} 个 AI 类型节点，绑定 {report.appliedSources}/{report.declaredSources} 个本轮声明素材（源文档共 {report.sourceCount} 个），可生成 {report.readyControls} 个控件，{report.pendingNodes} 个节点待绑定；{report.issues.Count} 项局部问题见 apply-report.json。";
        internal static bool TryGetRecognitionDurationMilliseconds(AiJobContext context, out long milliseconds)
        {
            milliseconds = 0;
            if (!AiJobFileUtility.TryReadJson(Path.Combine(context.JobDirectory, "recognition-metrics.json"), out AiRecognitionRunReport report)) return false;
            milliseconds = report.elapsedMilliseconds; return true;
        }
        internal static string FormatRecognitionDuration(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");
        internal static string BuildRecognitionDurationLine(AiJobContext context)
            => TryGetRecognitionDurationMilliseconds(context, out long ms) ? "识别耗时：" + FormatRecognitionDuration(ms) : "";
    }
}
#endif
