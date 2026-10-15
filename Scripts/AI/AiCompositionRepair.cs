#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI
{
    // A bounded model correction using the same target-tree protocol, never a local type guess.
    internal static class AiCompositionRepair
    {
        internal static AiRecognitionRunReport Run(IAiProvider provider, AiJobContext context,
            AiAnalysisRequest request, AiRecognitionDocument document)
        {
            var metrics = new AiRecognitionRunReport();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!AiJobFileUtility.TryReadJson(context.AnalysisPackagePath, out AiAnalysisPackageDocument package)) return metrics;
                var plan = AiUiPlan.Build(package, document);
                if (plan.mergeConflicts.Count == 0) return metrics;
                try { AiJobFileUtility.WriteStatusStage(context, "AI 正在拆分局部跨层合并"); }
                catch (IOException ex) { metrics.telemetryError = ex.Message; }
                string stem = Path.Combine(context.JobDirectory, "order-repair");
                var repairContext = new AiJobContext {
                    JobId = context.JobId, ProviderId = provider.ProviderId, JobDirectory = context.JobDirectory,
                    RequestDirectory = context.RequestDirectory, ResponseDirectory = context.ResponseDirectory,
                    AnalysisPackagePath = context.AnalysisPackagePath, RecognitionInputManifestPath = context.RecognitionInputManifestPath,
                    PromptPath = stem + "-prompt.md", RecognitionPath = stem + ".json", RecognitionTempPath = stem + ".json.tmp",
                    RawOutputPath = stem + "-raw.txt", StreamOutputPath = stem + "-stream.jsonl", CompletedPath = stem + ".complete",
                    VisibleCliCompletionPath = stem + "-cli-completed.json", DebugConsoleCloseSignalPath = stem + ".close",
                    StatusPath = context.StatusPath, DebugLogPath = context.DebugLogPath
                };
                AiJobFileUtility.DeleteFileIfExists(repairContext.RecognitionPath);
                AiJobFileUtility.DeleteFileIfExists(repairContext.RecognitionTempPath);
                AiJobFileUtility.WriteText(repairContext.PromptPath, BuildPrompt(document, plan));
                try { AiJobFileUtility.WriteText(Path.Combine(context.JobDirectory, "recognition-initial.json"), AiRecognitionResultLoader.Serialize(document)); }
                catch (IOException ex) { metrics.telemetryError = ex.Message; }
                var repairRequest = new AiAnalysisRequest {
                    StageName = "composition-repair", StageDisplayName = "AI 正在拆分局部跨层合并",
                    ResultDocumentKind = AiResultDocumentKind.Recognition,
                    AnalysisPackageJsonPath = request.AnalysisPackageJsonPath, PromptTemplatePath = repairContext.PromptPath,
                    OutputJsonPath = repairContext.RecognitionPath, OutputTempJsonPath = repairContext.RecognitionTempPath,
                    OutputRawTextPath = repairContext.RawOutputPath, OutputCompletedPath = repairContext.CompletedPath,
                    StreamOutputPath = repairContext.StreamOutputPath, VisibleCliCompletionPath = repairContext.VisibleCliCompletionPath,
                    WorkingDirectory = request.WorkingDirectory, ImageInputPaths = SelectImages(package, plan, request.ImageInputPaths),
                    AllowVisibleCliExecution = request.AllowVisibleCliExecution, RequireExplicitOutputJsonFile = true,
                    DisableOutputRecovery = true, CancellationToken = request.CancellationToken
                };
                request.CancellationToken.ThrowIfCancellationRequested();
                metrics.compositionRepairCalls = 1;
                string providerError = null;
                try { provider.ExecuteJob(repairContext, repairRequest); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { providerError = ex.Message; }
                request.CancellationToken.ThrowIfCancellationRequested();
                if (!AiProviderUtility.TryCommitValidatedJsonDocument(repairRequest, out string error)
                    || !AiRecognitionResultLoader.TryRead(repairContext.RecognitionPath, out var repair, out error))
                    throw new InvalidOperationException(providerError ?? error);
                metrics.repairedCompositions = Merge(package, document, plan, repair);
                try
                {
                    metrics.outputTextBytes = new FileInfo(repairContext.RecognitionPath).Length;
                    metrics.inputTextBytes = new FileInfo(repairContext.PromptPath).Length
                        + new FileInfo(request.AnalysisPackageJsonPath).Length
                        + new FileInfo(Path.Combine(context.JobDirectory, "nodes.tsv")).Length;
                    foreach (string path in repairRequest.ImageInputPaths) metrics.imageInputBytes += new FileInfo(path).Length;
                    AiRecognitionTelemetry.Read(provider.ProviderId, repairContext.StreamOutputPath, metrics);
                }
                catch (Exception ex) { metrics.telemetryError = ex.Message; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AiRecognitionResultLoader.AddIssue(document.issues, "", "composition_repair_failed",
                    "局部顺序修正未完成，其他结果保留: " + ex.Message);
            }
            finally { if (metrics.compositionRepairCalls > 0) metrics.compositionRepairMilliseconds = timer.ElapsedMilliseconds; }
            return metrics;
        }

        private static string[] SelectImages(AiAnalysisPackageDocument package, AiUiPlan plan, string[] images)
        {
            if (images.Length == 0) return images;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in plan.nodes)
                if (node.compositionPending) foreach (string leaf in node.leaves) ids.Add(leaf);
            foreach (var conflict in plan.mergeConflicts) ids.Add(conflict.between);
            var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in package.nodes)
                if ((ids.Contains(node.id) || ids.Contains(node.shortId)) && node.atlas != null)
                    pages.Add(Path.GetFileName(node.atlas.page));
            var result = new List<string> { images[0] }; // Whole-screen context, plus affected atlas pages only.
            for (int i = 1; i < images.Length; i++) if (pages.Contains(Path.GetFileName(images[i]))) result.Add(images[i]);
            return result.ToArray();
        }

        internal static string BuildPrompt(AiRecognitionDocument document, AiUiPlan plan)
        {
            var scope = new HashSet<string>(StringComparer.Ordinal);
            var prompt = new StringBuilder();
            prompt.AppendLine("只修正以下图片合并造成的绘制顺序冲突，不重新识别整张界面。类型、角色及拆分方案仍由你根据视觉判断。");
            prompt.AppendLine("图片文字保留为独立 Image，不转为 Text 或文本角色，也不借用 styleSource 重建。");
            prompt.AppendLine("节点 name 必须使用英文语义名称，只含英文字母、数字和下划线，并以英文字母开头；不得使用中文或拼音。text.value 保留实际显示文字。");
            foreach (var conflict in plan.mergeConflicts)
            {
                prompt.AppendLine(conflict.targetId + ": " + conflict.Description);
                scope.Add(conflict.targetId);
                foreach (var node in document.ui)
                    if (node.id == conflict.targetId && !string.IsNullOrEmpty(node.parent)) scope.Add(node.parent);
            }
            var context = new AiRecognitionDocument { version = document.version };
            foreach (var node in document.ui)
                if (scope.Contains(node.id) || !string.IsNullOrEmpty(node.parent) && scope.Contains(node.parent)) context.ui.Add(node);
            prompt.AppendLine("以下是相关目标节点及其同级元素（上下文，不是要求重新输出的全部节点）：");
            prompt.AppendLine(AiRecognitionResultLoader.Serialize(context));
            prompt.AppendLine("查看附件、manifest.json 和 nodes.tsv 的 draw 顺序；仅在局部需要时查看 detail 图。禁止读取源码、PSD 或运行脚本。");
            prompt.AppendLine("将每个有冲突节点的 sources 拆成若干独立元素。保留原 id、type 和 parent，给它保留正确的那部分素材；其余部分使用新的唯一 id（orderfix_原id_序号），由你选择正确 type。新增元素的 parent 与原节点相同。没有 parent 的仍为根级。Background 中穿插在 Fill 上方的刻度或装饰，应由你识别为独立 Image，不能继续并入 Background 或 Fill。");
            prompt.AppendLine("每个冲突节点的原素材必须恰好分配一次，不新增、遗漏、重复素材，不改变其他目标节点，不借用其他节点的素材；group 引用可拆为它的实际叶素材。不得通过设为 Null 隐藏内容、改变可见性或扩大 rect 规避冲突。每种角色在同一控件下最多一个。");
            prompt.AppendLine("仍输出 {\"version\":\"" + document.version + "\",\"ui\":[...],\"issues\":[]}，ui 仅包含冲突节点的保留部分及新增拆分元素；无需输出未变的父级或其他上下文节点。parent 可以引用上述已有目标。不能确定的局部记录 issue，继续交付其他明确的拆分。只写一次指定结果文件。");
            return prompt.ToString();
        }

        internal static int Merge(AiAnalysisPackageDocument package, AiRecognitionDocument original,
            AiUiPlan originalPlan, AiRecognitionDocument repair)
        {
            var order = new AiRenderOrderIndex(package.nodes);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in package.nodes) aliases.Add(node.shortId, node.id);
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in original.ui) existingIds.Add(node.id);
            var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in repair.ui) { idCounts.TryGetValue(node.id, out int count); idCounts[node.id] = count + 1; }
            int applied = 0;
            foreach (var conflict in originalPlan.mergeConflicts)
            {
                AiUiPlan.Node source = null;
                foreach (var node in originalPlan.nodes) if (node.source.id == conflict.targetId) { source = node; break; }
                if (source == null) continue;
                var allowed = new HashSet<string>(source.leaves, StringComparer.Ordinal);
                var covered = new HashSet<string>(StringComparer.Ordinal);
                var replacements = new List<AiUiNode>();
                bool valid = true, retainsIdentity = false;
                foreach (var node in repair.ui)
                {
                    var sourceIds = new List<string>();
                    bool known = true;
                    foreach (string alias in node.sources)
                        if (aliases.TryGetValue(alias, out string id)) sourceIds.Add(id); else known = false;
                    var leaves = order.Expand(sourceIds.ToArray());
                    bool relevant = node.id == conflict.targetId;
                    foreach (string leaf in leaves) relevant |= allowed.Contains(leaf);
                    if (!relevant) continue;
                    bool originalIdentity = node.id == conflict.targetId;
                    if (!known || idCounts[node.id] != 1 || leaves.Length == 0
                        || !string.Equals(node.parent ?? "", source.source.parent ?? "", StringComparison.Ordinal)
                        || !AiUiContract.TryParse(node.type, out var type) || type == GUIType.Null
                        || originalIdentity && node.type != source.source.type
                        || !originalIdentity && existingIds.Contains(node.id)) valid = false;
                    foreach (string leaf in leaves)
                        if (!allowed.Contains(leaf) || !covered.Add(leaf)) valid = false;
                    retainsIdentity |= originalIdentity;
                    replacements.Add(node);
                }
                if (valid && retainsIdentity && replacements.Count > 1 && covered.SetEquals(allowed))
                {
                    var candidate = new AiRecognitionDocument { version = original.version };
                    foreach (var node in original.ui)
                        if (node.id == conflict.targetId) candidate.ui.AddRange(replacements); else candidate.ui.Add(node);
                    var candidatePlan = AiUiPlan.Build(package, candidate);
                    var replacementIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var node in replacements) replacementIds.Add(node.id);
                    foreach (var node in candidatePlan.nodes)
                        if (replacementIds.Contains(node.source.id) && (node.rejected || !node.ownerValid || node.compositionPending || node.leaves.Length == 0)) valid = false;
                    if (valid)
                    {
                        original.ui = candidate.ui;
                        foreach (var node in replacements) existingIds.Add(node.id);
                        applied++;
                        continue;
                    }
                }
                AiRecognitionResultLoader.AddIssue(original.issues, conflict.targetId, "composition_repair_pending",
                    "局部 AI 拆分未完整满足素材与层级契约；保留类型和其他正确结果，暂停该合并图片。");
            }
            original.issues.AddRange(repair.issues);
            return applied;
        }
    }
}
#endif
