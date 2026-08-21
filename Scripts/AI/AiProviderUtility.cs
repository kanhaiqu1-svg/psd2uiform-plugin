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
using System.Text.RegularExpressions;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal enum AiStreamTerminalEventKind
    {
        None = 0,
        Completed = 1,
        Failed = 2
    }

    internal static class AiProviderUtility
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex JsonStringFieldRegex = new Regex("\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ForbiddenPatchAliasFieldRegex = new Regex("\"(?<key>target|nodeId|siblingIndex|judgement|mainType|roleType|currentMainType|currentRoleType|predictedMainType|predictedRoleType)\"\\s*:", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex StructuredLogRegex = new Regex("^(?<time>\\d{4}-\\d{2}-\\d{2}T\\S+)\\s+(?<level>[A-Z]+)\\s+(?<body>.+)$", RegexOptions.Compiled);
        private static readonly Regex NestedItemTypeRegex = new Regex("\"item\"\\s*:\\s*\\{.*?\"type\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex NestedItemStatusRegex = new Regex("\"item\"\\s*:\\s*\\{.*?\"status\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex NestedItemCommandRegex = new Regex("\"item\"\\s*:\\s*\\{.*?\"command\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex StreamTextDeltaRegex = new Regex("\"delta\"\\s*:\\s*\\{.*?\"type\"\\s*:\\s*\"text_delta\".*?\"text\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex AssistantTextBlockRegex = new Regex("\"type\"\\s*:\\s*\"text\"\\s*,\\s*\"text\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ResultPayloadRegex = new Regex("\"result\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ApiErrorStatusRegex = new Regex("\"api_error_status\"\\s*:\\s*(?<value>\\d+)", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex AbsoluteWindowsJsonPathRegex = new Regex("(?<path>[A-Za-z]:\\\\(?:[^\\\\/:*?\"<>|\\r\\n]+\\\\)*[^\\\\/:*?\"<>|\\r\\n]+\\.json(?:\\.tmp)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AbsoluteUnixJsonPathRegex = new Regex("(?<path>/(?:[^/\\r\\n\"'`]+/)*[^/\\r\\n\"'`]+\\.json(?:\\.tmp)?)", RegexOptions.Compiled);

        internal static string BuildCliPrompt(string promptTemplatePath, AiAnalysisRequest request)
        {
            if (string.IsNullOrWhiteSpace(promptTemplatePath) || !File.Exists(promptTemplatePath))
            {
                return string.Empty;
            }

            string template = ReadUtf8Text(promptTemplatePath);
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var output = new StringBuilder(template);
            if (request != null && request.PromptTags != null && request.PromptTags.Length > 0)
            {
                for (int i = 0; i < request.PromptTags.Length; i++)
                {
                    var tag = request.PromptTags[i];
                    if (tag == null || string.IsNullOrWhiteSpace(tag.key))
                    {
                        continue;
                    }

                    output.Replace(tag.key, tag.value ?? string.Empty);
                }
            }

            if (request != null && request.RequireExplicitOutputJsonFile)
            {
                string explicitOutputContract = BuildExplicitOutputFileContract(request);
                if (!string.IsNullOrWhiteSpace(explicitOutputContract))
                {
                    output.Insert(0, explicitOutputContract + Environment.NewLine + Environment.NewLine);
                }
            }

            return output.ToString();
        }

        internal static string ReadUtf8Text(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Utf8NoBom, true))
            {
                return reader.ReadToEnd();
            }
        }

        internal static bool TryLoadValidatedJsonDocument(AiAnalysisRequest request, out string error)
        {
            error = null;
            if (request == null)
            {
                error = "AI request is null.";
                return false;
            }

            string sourcePath = ResolveReadableOutputJsonPath(request);
            if ((string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                && !TryRecoverExplicitOutputJsonFile(request, out sourcePath, out _))
            {
                error = "Result JSON file not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Result JSON file not found.";
                return false;
            }

            string jsonText;
            try
            {
                jsonText = AiJobFileUtility.ReadText(sourcePath);
            }
            catch (IOException ex)
            {
                error = $"Result JSON file is not ready yet: {ex.Message}";
                return false;
            }

            return TryValidateJsonText(request.ResultDocumentKind, jsonText, out error);
        }

        internal static bool TryPersistJsonDocumentFromRawOutput(string rawOutput, AiAnalysisRequest request, out string error)
        {
            error = null;
            if (request == null)
            {
                error = "AI request is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                error = "Provider output is empty.";
                return false;
            }

            string normalized = ExtractJsonCandidate(rawOutput, request.ResultDocumentKind);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "Could not extract JSON object from provider output.";
                return false;
            }

            if (!TryValidateJsonText(request.ResultDocumentKind, normalized, out error))
            {
                return false;
            }

            try
            {
                AiJobFileUtility.WriteText(ResolveWritableOutputJsonPath(request), normalized);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to persist JSON output: {ex.Message}";
                return false;
            }
        }

        internal static bool TryCommitValidatedJsonDocument(AiAnalysisRequest request, out string error)
        {
            return TryCommitValidatedJsonDocument(request, allowRecoveredExplicitArtifact: true, out error);
        }

        internal static bool TryCommitValidatedJsonDocument(AiAnalysisRequest request, bool allowRecoveredExplicitArtifact, out string error)
        {
            error = null;
            if (request == null)
            {
                error = "AI request is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.OutputJsonPath))
            {
                error = "Final result JSON path is empty.";
                return false;
            }

            string sourcePath = ResolveReadableOutputJsonPath(request);
            if ((string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                && (!allowRecoveredExplicitArtifact || !TryRecoverExplicitOutputJsonFile(request, out sourcePath, out error)))
            {
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = "Result JSON file not found.";
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Result JSON file not found.";
                return false;
            }

            string jsonText;
            try
            {
                jsonText = AiJobFileUtility.ReadText(sourcePath);
            }
            catch (IOException ex)
            {
                error = $"Result JSON file is not ready yet: {ex.Message}";
                return false;
            }

            if (!TryValidateJsonText(request.ResultDocumentKind, jsonText, out error))
            {
                return false;
            }

            try
            {
                if (!PathsEqual(sourcePath, request.OutputJsonPath))
                {
                    AiJobFileUtility.WriteText(request.OutputJsonPath, jsonText);
                    AiJobFileUtility.DeleteFileIfExists(sourcePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to commit JSON output: {ex.Message}";
                return false;
            }
        }

        internal static bool TryExtractAssistantTextFromStreamOutput(string streamOutputPath, out string text, out string error)
        {
            text = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(streamOutputPath) || !File.Exists(streamOutputPath))
            {
                error = "Stream output file not found.";
                return false;
            }

            string streamText;
            try
            {
                streamText = ReadUtf8Text(streamOutputPath);
            }
            catch (IOException ex)
            {
                error = $"Stream output file is not ready yet: {ex.Message}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(streamText))
            {
                error = "Stream output is empty.";
                return false;
            }

            var builder = new StringBuilder(streamText.Length / 8);
            string[] lines = streamText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string topLevelType = TryExtractJsonStringField(line, "type");
                if (string.Equals(topLevelType, "stream_event", StringComparison.Ordinal))
                {
                    AppendRegexMatches(builder, StreamTextDeltaRegex, line);
                    continue;
                }

                if (string.Equals(topLevelType, "assistant", StringComparison.Ordinal))
                {
                    AppendRegexMatches(builder, AssistantTextBlockRegex, line);
                    continue;
                }

                if (string.Equals(topLevelType, "result", StringComparison.Ordinal))
                {
                    AppendRegexMatches(builder, ResultPayloadRegex, line);
                }
            }

            text = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Could not recover assistant text from stream output.";
                return false;
            }

            return true;
        }

        internal static bool TryResolveCliFailureMessage(string streamOutputPath, string rawOutputPath, string stderr, out string message)
        {
            message = string.Empty;

            if (TryResolveFailureTextFromStreamOutput(streamOutputPath, out message))
            {
                return true;
            }

            if (TryResolveFailureText(stderr, out message))
            {
                return true;
            }

            if (TryResolveFailureText(ReadUtf8TextSafe(rawOutputPath), out message))
            {
                return true;
            }

            return false;
        }

        internal static bool CouldContainFinalJsonDocument(AiResultDocumentKind kind, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            int jsonStart = trimmed.IndexOf('{');
            int jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return false;
            }

            string candidate = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
            if (!candidate.Contains("\"treeHash\"", StringComparison.Ordinal))
            {
                return false;
            }

            switch (kind)
            {
                case AiResultDocumentKind.MainType:
                    return candidate.Contains("\"nodes\"", StringComparison.Ordinal);
                case AiResultDocumentKind.ChildRelation:
                    return candidate.Contains("\"relations\"", StringComparison.Ordinal);
                case AiResultDocumentKind.RecognitionCombined:
                    return candidate.Contains("\"owners\"", StringComparison.Ordinal)
                        && candidate.Contains("\"roles\"", StringComparison.Ordinal)
                        && candidate.Contains("\"nodeLabels\"", StringComparison.Ordinal);
                case AiResultDocumentKind.Structural:
                    return candidate.Contains("\"operations\"", StringComparison.Ordinal);
                case AiResultDocumentKind.Patch:
                    return candidate.Contains("\"analysis\"", StringComparison.Ordinal)
                        || candidate.Contains("\"operations\"", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static string ResolveReadableOutputJsonPath(AiAnalysisRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(request.OutputTempJsonPath) && File.Exists(request.OutputTempJsonPath))
            {
                return request.OutputTempJsonPath;
            }

            return request.OutputJsonPath ?? string.Empty;
        }

        private static string BuildExplicitOutputFileContract(AiAnalysisRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            string outputPath = !string.IsNullOrWhiteSpace(request.OutputTempJsonPath)
                ? request.OutputTempJsonPath
                : request.OutputJsonPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(512);
            builder.AppendLine("## CLI Output Contract");
            builder.AppendLine();
            builder.AppendLine("- 你必须把最终 " + GetVisibleBootstrapResultLabel(request.ResultDocumentKind) + " 写入下面这个 UTF-8 文件路径：");
            builder.AppendLine("  " + outputPath);
            builder.AppendLine("- 这个文件是 Unity 认定任务成功的唯一产物。");
            builder.AppendLine("- 不要写入 `recognition_output.json` 或任何其它自定义结果文件。");
            builder.AppendLine("- 最终 assistant 回复必须与该文件内容完全一致，且只能是一个 JSON 对象。");
            builder.AppendLine("- 不要输出总结、进度汇报、Markdown、代码块或额外说明。");
            builder.AppendLine("- 如果需要自检或中间处理，只能放在 thinking/tool 事件里，不能放在最终 assistant 回复里。");
            return builder.ToString().TrimEnd();
        }

        private static bool TryRecoverExplicitOutputJsonFile(AiAnalysisRequest request, out string sourcePath, out string error)
        {
            sourcePath = string.Empty;
            error = null;
            if (request == null)
            {
                error = "AI request is null.";
                return false;
            }

            string writablePath = ResolveWritableOutputJsonPath(request);
            if (string.IsNullOrWhiteSpace(writablePath))
            {
                error = "Writable result JSON path is empty.";
                return false;
            }

            string workingDirectory = request.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                return false;
            }

            string rawTail = ReadUtf8TailText(request.OutputRawTextPath, 262144);
            string streamTail = ReadUtf8TailText(request.StreamOutputPath, 262144);
            var candidatePaths = new List<string>(8);
            AddRecoveredJsonPathCandidates(candidatePaths, rawTail);
            AddRecoveredJsonPathCandidates(candidatePaths, streamTail);

            string lastValidationError = null;
            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string candidatePath = candidatePaths[i];
                if (!IsPathWithinWorkingDirectory(candidatePath, workingDirectory) || !File.Exists(candidatePath))
                {
                    continue;
                }

                string jsonText;
                try
                {
                    jsonText = AiJobFileUtility.ReadText(candidatePath);
                }
                catch (IOException ex)
                {
                    lastValidationError = $"Recovered JSON file is not ready yet: {ex.Message}";
                    continue;
                }

                if (!TryValidateJsonText(request.ResultDocumentKind, jsonText, out lastValidationError))
                {
                    continue;
                }

                try
                {
                    if (!PathsEqual(candidatePath, writablePath))
                    {
                        AiJobFileUtility.WriteText(writablePath, jsonText);
                    }

                    sourcePath = ResolveReadableOutputJsonPath(request);
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        sourcePath = writablePath;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    lastValidationError = $"Failed to recover JSON output: {ex.Message}";
                }
            }

            error = lastValidationError;
            return false;
        }

        private static void AddRecoveredJsonPathCandidates(List<string> candidates, string text)
        {
            AddRecoveredJsonPathCandidates(candidates, text, AbsoluteWindowsJsonPathRegex);
            AddRecoveredJsonPathCandidates(candidates, text, AbsoluteUnixJsonPathRegex);
        }

        private static void AddRecoveredJsonPathCandidates(List<string> candidates, string text, Regex regex)
        {
            if (candidates == null || regex == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var matches = regex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.Success)
                {
                    continue;
                }

                string candidatePath = match.Groups["path"].Value;
                if (string.IsNullOrWhiteSpace(candidatePath))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidatePath.Trim());
                }
                catch
                {
                    continue;
                }

                bool exists = false;
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    if (string.Equals(candidates[candidateIndex], fullPath, GetPathComparison()))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    candidates.Add(fullPath);
                }
            }
        }

        private static string ResolveWritableOutputJsonPath(AiAnalysisRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(request.OutputTempJsonPath)
                ? request.OutputTempJsonPath
                : (request.OutputJsonPath ?? string.Empty);
        }

        private static bool IsPathWithinWorkingDirectory(string candidatePath, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(workingDirectory))
            {
                return false;
            }

            string fullCandidatePath;
            string fullWorkingDirectory;
            try
            {
                fullCandidatePath = Path.GetFullPath(candidatePath);
                fullWorkingDirectory = Path.GetFullPath(workingDirectory);
            }
            catch
            {
                return false;
            }

            if (string.Equals(fullCandidatePath, fullWorkingDirectory, GetPathComparison()))
            {
                return true;
            }

            string normalizedRoot = fullWorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullCandidatePath.StartsWith(normalizedRoot, GetPathComparison());
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left ?? string.Empty),
                Path.GetFullPath(right ?? string.Empty),
                GetPathComparison());
        }

        private static StringComparison GetPathComparison()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        internal static bool TryValidateJsonText(AiResultDocumentKind kind, string jsonText, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                error = "JSON output is empty.";
                return false;
            }

            switch (kind)
            {
                case AiResultDocumentKind.MainType:
                    return TryValidateMainTypeJson(jsonText, out error);
                case AiResultDocumentKind.ChildRelation:
                    return TryValidateChildRelationJson(jsonText, out error);
                case AiResultDocumentKind.RecognitionCombined:
                    return TryValidateRecognitionCombinedJson(jsonText, out error);
                case AiResultDocumentKind.Structural:
                    return TryValidateStructuralJson(jsonText, out error);
                case AiResultDocumentKind.Patch:
                    return TryValidatePatchJsonText(jsonText, out error);
                default:
                    error = "Unsupported AI result document kind.";
                    return false;
            }
        }

        internal static bool TryValidatePatchJsonText(string jsonText, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                error = "Patch JSON is empty.";
                return false;
            }

            var match = ForbiddenPatchAliasFieldRegex.Match(jsonText);
            if (match.Success)
            {
                error = $"Patch JSON contains forbidden alias field '{match.Groups["key"].Value}'.";
                return false;
            }

            try
            {
                var patch = JsonUtility.FromJson<AiPatchDocument>(jsonText);
                if (patch == null || string.IsNullOrWhiteSpace(patch.treeHash))
                {
                    error = "Patch JSON is missing treeHash.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"Patch JSON parse failed: {ex.Message}";
                return false;
            }

            return true;
        }

        internal static bool TryParseCodexTerminalEvent(string rawLine, out AiStreamTerminalEventKind kind, out string message)
        {
            kind = AiStreamTerminalEventKind.None;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return false;
            }

            string line = rawLine.Trim();
            if (line.Length < 2 || line[0] != '{')
            {
                return false;
            }

            string type = TryExtractJsonStringField(line, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            switch (type)
            {
                case "turn.completed":
                case "response.completed":
                case "session.completed":
                    kind = AiStreamTerminalEventKind.Completed;
                    message =
                        TryExtractJsonStringField(line, "message")
                        ?? TryExtractJsonStringField(line, "summary")
                        ?? type;
                    return true;

                case "turn.failed":
                case "response.failed":
                case "session.failed":
                case "error":
                    kind = AiStreamTerminalEventKind.Failed;
                    message =
                        TryExtractJsonStringField(line, "message")
                        ?? TryExtractJsonStringField(line, "content")
                        ?? TryExtractJsonStringField(line, "title")
                        ?? type;
                    return true;
            }

            return false;
        }

        internal static string ExtractProgressDetail(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return string.Empty;
            }

            string line = rawLine.Trim();
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                line = line.Substring(5).Trim();
            }

            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return "AI 输出完成，正在整理结果...";
            }

            if (line[0] == '{')
            {
                if (line.IndexOf("\"targetId\"", StringComparison.Ordinal) >= 0
                    || line.IndexOf("\"predictedUIType\"", StringComparison.Ordinal) >= 0
                    || line.IndexOf("\"ownerId\"", StringComparison.Ordinal) >= 0
                    || line.IndexOf("\"roleType\"", StringComparison.Ordinal) >= 0
                    || line.IndexOf("\"memberNodeIds\"", StringComparison.Ordinal) >= 0
                    || line.IndexOf("\"operations\"", StringComparison.Ordinal) >= 0
                    || (line.IndexOf("\"op\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"uiType\"", StringComparison.Ordinal) >= 0))
                {
                    return string.Empty;
                }

                string topLevelType = TryExtractJsonStringField(line, "type");
                string preferredContent =
                    TryExtractJsonStringField(line, "reasoning_content")
                    ?? TryExtractJsonStringField(line, "content")
                    ?? TryExtractJsonStringField(line, "text")
                    ?? TryExtractJsonStringField(line, "message")
                    ?? TryExtractJsonStringField(line, "summary")
                    ?? TryExtractJsonStringField(line, "title");

                if (!string.IsNullOrWhiteSpace(preferredContent))
                {
                    return NormalizeProgressDetail(preferredContent);
                }

                if (!string.IsNullOrWhiteSpace(topLevelType)
                    && topLevelType.StartsWith("item.", StringComparison.OrdinalIgnoreCase))
                {
                    string nestedType = TryExtractNestedItemField(NestedItemTypeRegex, line);
                    string nestedStatus = TryExtractNestedItemField(NestedItemStatusRegex, line);
                    string nestedCommand = TryExtractNestedItemField(NestedItemCommandRegex, line);
                    if (!string.IsNullOrWhiteSpace(nestedType) || !string.IsNullOrWhiteSpace(nestedStatus))
                    {
                        string detail = !string.IsNullOrWhiteSpace(nestedType) ? nestedType : topLevelType;
                        if (!string.IsNullOrWhiteSpace(nestedStatus))
                        {
                            detail += $" ({nestedStatus})";
                        }

                        string commandSummary = SummarizeCommand(nestedCommand);
                        if (!string.IsNullOrWhiteSpace(commandSummary))
                        {
                            detail += $": {commandSummary}";
                        }

                        return NormalizeProgressDetail($"AI任务进程: {detail}");
                    }
                }

                string type =
                    topLevelType
                    ?? TryExtractJsonStringField(line, "event")
                    ?? TryExtractJsonStringField(line, "subtype")
                    ?? TryExtractJsonStringField(line, "status");

                if (!string.IsNullOrWhiteSpace(type))
                {
                    return NormalizeProgressDetail($"AI任务进程: {type}");
                }
            }

            var structuredLog = StructuredLogRegex.Match(line);
            if (structuredLog.Success)
            {
                string body = structuredLog.Groups["body"].Value;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    return NormalizeProgressDetail($"AI任务进程: {body}");
                }
            }

            return NormalizeProgressDetail(line);
        }

        internal static string GetVisibleBootstrapResultLabel(AiResultDocumentKind kind)
        {
            switch (kind)
            {
                case AiResultDocumentKind.MainType:
                    return "主控件识别结果 JSON";
                case AiResultDocumentKind.ChildRelation:
                    return "子控件关系识别结果 JSON";
                case AiResultDocumentKind.RecognitionCombined:
                    return "UI元素识别结果 JSON";
                case AiResultDocumentKind.Structural:
                    return "结构修复计划 JSON";
                case AiResultDocumentKind.Patch:
                    return "最终 patch JSON";
                default:
                    return "最终 JSON 结果";
            }
        }

        private static bool TryValidateMainTypeJson(string jsonText, out string error)
        {
            error = null;
            try
            {
                var document = JsonUtility.FromJson<AiMainTypeResultDocument>(jsonText);
                if (document == null)
                {
                    error = "MainType JSON is invalid.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(document.treeHash))
                {
                    error = "MainType JSON is missing treeHash.";
                    return false;
                }
                if (document.nodes == null)
                {
                    error = "MainType JSON is missing nodes.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"MainType JSON parse failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryValidateRecognitionCombinedJson(string jsonText, out string error)
        {
            error = null;
            try
            {
                var document = JsonUtility.FromJson<AiRecognitionCombinedResultDocument>(jsonText);
                if (document == null)
                {
                    error = "RecognitionCombined JSON is invalid.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(document.treeHash))
                {
                    error = "RecognitionCombined JSON is missing treeHash.";
                    return false;
                }
                bool hasSemanticGraph =
                    document.owners != null
                    && document.roles != null
                    && document.nodeLabels != null;
                if (!hasSemanticGraph)
                {
                    error = "RecognitionCombined JSON must contain owners, roles, and nodeLabels.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"RecognitionCombined JSON parse failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryValidateChildRelationJson(string jsonText, out string error)
        {
            error = null;
            try
            {
                var document = JsonUtility.FromJson<AiChildRelationResultDocument>(jsonText);
                if (document == null)
                {
                    error = "ChildRelation JSON is invalid.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(document.treeHash))
                {
                    error = "ChildRelation JSON is missing treeHash.";
                    return false;
                }
                if (document.relations == null)
                {
                    error = "ChildRelation JSON is missing relations.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"ChildRelation JSON parse failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryValidateStructuralJson(string jsonText, out string error)
        {
            error = null;
            try
            {
                var document = JsonUtility.FromJson<AiStructuralResultDocument>(jsonText);
                if (document == null)
                {
                    error = "Structural JSON is invalid.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(document.treeHash))
                {
                    error = "Structural JSON is missing treeHash.";
                    return false;
                }
                if (document.operations == null)
                {
                    error = "Structural JSON is missing operations.";
                    return false;
                }

                for (int i = 0; i < document.operations.Count; i++)
                {
                    var operation = document.operations[i];
                    if (operation == null)
                    {
                        error = $"Structural operation[{i}] is null.";
                        return false;
                    }

                    if (!AiPatchOperationNames.TryParse(operation.op, out var kind)
                        || (kind != AiPatchOperationKind.CreateGroup
                            && kind != AiPatchOperationKind.MoveNode
                            && kind != AiPatchOperationKind.SetUIType))
                    {
                        error = $"Structural operation[{i}] has unsupported op '{operation.op}'.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Structural JSON parse failed: {ex.Message}";
                return false;
            }
        }

        private static string ExtractJsonCandidate(string rawOutput, AiResultDocumentKind kind)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return null;
            }

            string trimmed = StripMarkdownFence(rawOutput.Trim());
            var candidates = new List<string>(8);
            AddCandidate(candidates, trimmed);
            AddCandidate(candidates, ExtractBroadJsonSlice(trimmed));

            var balancedCandidates = ExtractBalancedJsonObjects(trimmed);
            for (int i = 0; i < balancedCandidates.Count; i++)
            {
                AddCandidate(candidates, balancedCandidates[i]);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (TryValidateJsonText(kind, candidate, out _))
                {
                    return candidate;
                }
            }

            return candidates.Count > 0 ? candidates[0] : null;
        }

        private static void AppendRegexMatches(StringBuilder builder, Regex regex, string text)
        {
            if (builder == null || regex == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var matches = regex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.Success)
                {
                    continue;
                }

                string value = Regex.Unescape(match.Groups["value"].Value);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                builder.Append(value);
            }
        }

        private static string StripMarkdownFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed.Substring(firstNewLine + 1).Trim();
            }

            int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                trimmed = trimmed.Substring(0, fenceEnd).Trim();
            }

            return trimmed;
        }

        private static string ExtractBroadJsonSlice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            int jsonStart = text.IndexOf('{');
            int jsonEnd = text.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                return null;
            }

            return text.Substring(jsonStart, jsonEnd - jsonStart + 1).Trim();
        }

        private static List<string> ExtractBalancedJsonObjects(string text)
        {
            var results = new List<string>(4);
            if (string.IsNullOrWhiteSpace(text))
            {
                return results;
            }

            int depth = 0;
            int startIndex = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        startIndex = i;
                    }
                    depth++;
                    continue;
                }

                if (c != '}')
                {
                    continue;
                }

                if (depth <= 0)
                {
                    continue;
                }

                depth--;
                if (depth == 0 && startIndex >= 0)
                {
                    results.Add(text.Substring(startIndex, i - startIndex + 1).Trim());
                    startIndex = -1;
                }
            }

            return results;
        }

        private static void AddCandidate(List<string> candidates, string candidate)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            string normalized = candidate.Trim();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i], normalized, StringComparison.Ordinal))
                {
                    return;
                }
            }

            candidates.Add(normalized);
        }

        private static bool TryResolveFailureTextFromStreamOutput(string streamOutputPath, out string message)
        {
            message = string.Empty;
            string tail = ReadUtf8TailText(streamOutputPath, 65536);
            if (string.IsNullOrWhiteSpace(tail))
            {
                return false;
            }

            string[] lines = tail.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (TryResolveFailureTextFromJsonLine(lines[i], out message))
                {
                    return true;
                }
            }

            return TryResolveFailureText(tail, out message);
        }

        private static bool TryResolveFailureTextFromJsonLine(string rawLine, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return false;
            }

            string line = rawLine.Trim();
            if (line.Length < 2 || line[0] != '{')
            {
                return TryResolveFailureText(line, out message);
            }

            if (TryParseCodexTerminalEvent(line, out var kind, out var terminalMessage)
                && kind == AiStreamTerminalEventKind.Failed
                && TryResolveFailureText(terminalMessage, out message))
            {
                return true;
            }

            string topLevelType = TryExtractJsonStringField(line, "type");
            bool explicitError =
                line.IndexOf("\"is_error\":true", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("\"error\":", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(topLevelType, "error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(topLevelType, "result", StringComparison.OrdinalIgnoreCase)
                || string.Equals(topLevelType, "assistant", StringComparison.OrdinalIgnoreCase);

            string candidate =
                TryExtractJsonStringField(line, "result")
                ?? TryExtractJsonStringField(line, "message")
                ?? TryExtractJsonStringField(line, "content")
                ?? TryExtractJsonStringField(line, "title")
                ?? TryExtractJsonStringField(line, "summary")
                ?? TryExtractJsonStringField(line, "text");

            if (string.IsNullOrWhiteSpace(candidate)
                && string.Equals(topLevelType, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                candidate = ExtractAssistantTextBlocks(line);
            }

            if (TryResolveFailureText(candidate, out message))
            {
                return true;
            }

            if (explicitError)
            {
                string status = ExtractApiErrorStatus(line);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    message = "API Error: " + status + " Request Error";
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveFailureText(string text, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Replace("\r", "\n").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = NormalizeProgressDetail(lines[i]);
                if (!LooksLikeFailureText(line))
                {
                    continue;
                }

                message = line;
                return true;
            }

            normalized = NormalizeProgressDetail(normalized);
            if (!LooksLikeFailureText(normalized))
            {
                return false;
            }

            message = normalized;
            return true;
        }

        private static string ReadUtf8TextSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                return AiJobFileUtility.ReadText(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadUtf8TailText(string path, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || maxBytes < 1)
            {
                return string.Empty;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int bytesToRead = (int)Math.Min(maxBytes, stream.Length);
                    if (bytesToRead < 1)
                    {
                        return string.Empty;
                    }

                    stream.Seek(-bytesToRead, SeekOrigin.End);
                    var buffer = new byte[bytesToRead];
                    int read = stream.Read(buffer, 0, bytesToRead);
                    return Utf8NoBom.GetString(buffer, 0, read);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractAssistantTextBlocks(string jsonLine)
        {
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(256);
            AppendRegexMatches(builder, AssistantTextBlockRegex, jsonLine);
            return builder.ToString().Trim();
        }

        private static string ExtractApiErrorStatus(string jsonLine)
        {
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                return string.Empty;
            }

            var match = ApiErrorStatusRegex.Match(jsonLine);
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        private static string TryExtractNestedItemField(Regex regex, string jsonLine)
        {
            if (regex == null || string.IsNullOrWhiteSpace(jsonLine))
            {
                return null;
            }

            var match = regex.Match(jsonLine);
            if (!match.Success)
            {
                return null;
            }

            return Regex.Unescape(match.Groups["value"].Value);
        }

        private static string TryExtractJsonStringField(string jsonLine, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(jsonLine) || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            var matches = JsonStringFieldRegex.Matches(jsonLine);
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.Success)
                {
                    continue;
                }

                if (!string.Equals(match.Groups["key"].Value, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Regex.Unescape(match.Groups["value"].Value);
            }

            return null;
        }

        private static string SummarizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            string normalized = NormalizeProgressDetail(command);
            int getContentIndex = normalized.IndexOf("Get-Content", StringComparison.OrdinalIgnoreCase);
            if (getContentIndex >= 0)
            {
                return normalized.Substring(getContentIndex);
            }

            int shellIndex = normalized.LastIndexOf('\\');
            if (shellIndex >= 0 && shellIndex + 1 < normalized.Length)
            {
                return normalized.Substring(shellIndex + 1);
            }

            return normalized;
        }

        private static string NormalizeProgressDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return string.Empty;
            }

            string normalized = detail.Replace("\r", " ").Replace("\n", " ").Trim();
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }
            return normalized;
        }

        private static bool LooksLikeFailureText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim();
            if (normalized.Length < 4)
            {
                return false;
            }

            if (string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return normalized.IndexOf("API Error", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("Request Error", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
