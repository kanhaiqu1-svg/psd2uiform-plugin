using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public static class AiCli
    {
        public static void Kill(OwnedProcess record)
        {
            if (!AiNativeProcess.Matches(record)) return;
            if (Environment.OSVersion.Platform != PlatformID.Unix)
            {
                using (var killer = Process.Start(new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"), Arguments = "/PID " + record.pid + " /T /F", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                { killer.StandardOutput.ReadToEndAsync(); killer.StandardError.ReadToEndAsync(); killer.WaitForExit(10000); }
            }
            else
            {
                var children = new Dictionary<int, List<int>>();
                string listing = AiNativeProcess.Read("/bin/ps", "-axo pid=,ppid=");
                foreach (string line in listing.Split('\n'))
                {
                    string[] v = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); if (v.Length != 2) continue;
                    int pid = int.Parse(v[0]), parent = int.Parse(v[1]); if (!children.TryGetValue(parent, out var list)) children.Add(parent, list = new List<int>()); list.Add(pid);
                }
                KillChildren(record.pid, children); using (var p = Process.GetProcessById(record.pid)) if (!p.HasExited) p.Kill();
            }
        }
        static void KillChildren(int parent, Dictionary<int, List<int>> children)
        {
            if (!children.TryGetValue(parent, out var list)) return;
            foreach (int id in list) { KillChildren(id, children); try { using (var p = Process.GetProcessById(id)) p.Kill(); } catch (ArgumentException) { } catch (InvalidOperationException) { } }
        }
        public static OwnedProcess Identity(Process p, string root) => new OwnedProcess { pid = p.Id, created = (p.StartTime.ToUniversalTime() - AiNativeProcess.Epoch).TotalSeconds, exe = AiNativeProcess.Executable(p), jobDir = root };
        public static async Task<AiTaskResult> Execute(AiProfile profile, AiTaskRequest request, string root, CancellationToken token)
        {
            try { return await ExecuteCore(profile, request, root, token); }
            catch (Exception e)
            {
                AiFiles.Write(Path.Combine(root, "status.json"), new AiTaskResult { taskId = request.taskId, callId = request.callId, status = e is OperationCanceledException ? "Cancelled" : "Failed", error = e.Message });
                throw;
            }
        }
        static async Task<AiTaskResult> ExecuteCore(AiProfile profile, AiTaskRequest request, string root, CancellationToken token)
        {
            Directory.CreateDirectory(root); string inputs = Path.Combine(root, "inputs"); Directory.CreateDirectory(inputs);
            for (int i = 0; i < request.references.Count; ++i)
            {
                var reference = request.references[i]; string path = Path.Combine(inputs, i.ToString("D2") + Path.GetExtension(reference.path));
                if (!File.Exists(reference.path)) throw new FileNotFoundException("参考图不存在", reference.path);
                if (Path.GetFullPath(reference.path) != Path.GetFullPath(path)) File.Copy(reference.path, path, true); reference.path = path;
            }
            request.outputPath = Path.Combine(root, "response.json");
            string ownerPath = Path.Combine(root, "process.json");
            if (File.Exists(ownerPath)) { var previous = AiFiles.Read<OwnedProcess>(ownerPath); if (previous.jobDir == root) Kill(previous); }
            request.profileFingerprint = AiProfiles.Fingerprint(profile); AiFiles.Write(Path.Combine(root, "request.json"), request);
            AiFiles.Write(Path.Combine(root, "profile.json"), profile);
            string rawPath = Path.Combine(root, "response.txt"), promptPath = Path.Combine(root, "prompt.txt");
            if (request.repairJsonPath != request.outputPath)
            {
                File.Delete(request.outputPath);
                if (request.repairJsonPath != "") File.Copy(request.repairJsonPath, request.outputPath);
            }
            File.Delete(rawPath);
            if (request.systemPrompt != "") AiFiles.WriteText(Path.Combine(root, "system-prompt.txt"), request.systemPrompt);
            string commandRoot = string.IsNullOrEmpty(request.workingDirectory) ? root : request.workingDirectory;
            var prompt = new StringBuilder();
            if (request.systemPrompt != "") prompt.AppendLine(request.systemPrompt).AppendLine();
            prompt.Append(request.prompt).AppendLine().AppendLine("## Task files");
            foreach (var r in request.references) prompt.AppendLine(r.role + ": " + Path.GetFullPath(r.path));

            string output = AiFiles.Relative(commandRoot, request.outputPath);
            if (request.repairJsonPath != "") prompt.AppendLine(output + " already contains the previous complete result. Read and Edit only the rejected fields in this file, preserve other fields and IDs, and do not rewrite the entire file when a targeted edit suffices.");
            prompt.AppendLine("Write one complete JSON object following the task's output protocol to " + output + ". No reasoning or markdown in the file. A brief JSON validation is allowed; fix only reported errors. When finished, reply only with the file path, without a task summary.");

            AiFiles.WriteText(promptPath, prompt.ToString());
            int major = 2;
            if (profile.mode == AiMode.OpenCode)
            {
                string version = await Capture(profile.executable, new List<string> { "--version" }, root, token);
                major = 0; foreach (string word in version.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) if (Version.TryParse(word.TrimStart('v').Split('-')[0], out var parsed)) { major = parsed.Major; break; }
                if (major != 1 && major != 2) throw new IOException("无法识别受支持的 OpenCode 版本：" + version.Trim());
            }
            var args = AiCommands.Cli(profile, commandRoot, request, rawPath, major);
            var launch = Resolve(profile.executable, args, AiFiles.Relative(commandRoot, promptPath));
            var start = StartInfo(launch, commandRoot); start.RedirectStandardInput = launch.UseStandardInput;
            var answer = new StringBuilder(); string error = null, finish = null; object structured = null;
            var result = new AiTaskResult { taskId = request.taskId, callId = request.callId, status = "Produced", outputPath = request.outputPath, model = "" };
            using (var process = new Process { StartInfo = start })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(profile.requestTimeoutMinutes)); process.Start(); var owner = Identity(process, root); AiFiles.Write(ownerPath, owner);
                AiFiles.Write(Path.Combine(root, "status.json"), new AiTaskResult { taskId = request.taskId, callId = request.callId, status = "Running" });
                AiFiles.WriteText(Path.Combine(root, "launch.json"), AiJson.Stringify(AiJson.Map("exe", launch.FileName, "args", args, "mode", profile.mode.ToString())));
                using (timeout.Token.Register(() => Kill(owner)))
                using (var stream = new StreamWriter(Path.Combine(root, "events.jsonl"), false, AiFiles.Utf8) { AutoFlush = true })
                {
                    var stderr = process.StandardError.ReadToEndAsync();
                    Task input = launch.UseStandardInput ? WriteInput(process, prompt.ToString()) : Task.CompletedTask;
                    string eventLine;
                    while ((eventLine = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        if (timeout.IsCancellationRequested) throw new TimeoutException(profile.mode + " 超过请求超时。");
                        stream.WriteLine(eventLine);
                        ParseEvent(profile.mode, eventLine, answer, ref structured, ref error, ref finish, result);
                    }
                    await Task.Run(() => process.WaitForExit());
                    string errors = await stderr; AiFiles.WriteText(Path.Combine(root, "stderr.txt"), errors);
                    try { await input; } catch (IOException) { if (process.ExitCode == 0) throw; }
                    token.ThrowIfCancellationRequested(); if (timeout.IsCancellationRequested) throw new TimeoutException(profile.mode.ToString() + " 超过请求超时。");
                    if (process.ExitCode != 0 || error != null)
                    {
                        string detail = error ?? errors;
                        if (error != null && !string.IsNullOrEmpty(errors))
                        {
                            string tail = errors.Trim().Replace("\r", "");
                            int line = tail.LastIndexOf('\n');
                            if (line >= 0) tail = tail.Substring(line + 1);
                            if (tail.Length > 240) tail = tail.Substring(tail.Length - 240);
                            if (tail != "") detail += "；stderr: " + tail;
                        }
                        throw new IOException(profile.mode.ToString() + " 调用失败：" + detail + " (exit " + process.ExitCode + ")");
                    }
                }
                string text = structured != null ? AiJson.Stringify(structured) : File.Exists(rawPath) ? File.ReadAllText(rawPath, AiFiles.Utf8) : answer.ToString();
                if (!File.Exists(request.outputPath)) throw new FormatException("CLI 未写出约定的 response.json。请查看任务日志。");
                text = File.ReadAllText(request.outputPath, AiFiles.Utf8);
                if (finish == "length" || finish == "max_tokens") throw new FormatException("CLI 输出已截断，未提交结果。请调整 CLI 自身配置或拆分任务。");
                result.json = text; result.finishReason = finish;

                return result;
            }
        }
        static async Task WriteInput(Process process, string prompt) { await process.StandardInput.WriteAsync(prompt); process.StandardInput.Close(); }
        static void ParseEvent(AiMode mode, string line, StringBuilder answer, ref object structured, ref string error, ref string finish, AiTaskResult result)
        {
            if (!line.StartsWith("{", StringComparison.Ordinal)) return;
            Dictionary<string, object> e; try { e = AiJson.Object(line); } catch (FormatException) { return; }
            e.TryGetValue("type", out var type);
            if (e.TryGetValue("usage", out var usage) && usage is Dictionary<string, object> u)
            { if (u.TryGetValue("input_tokens", out var input)) result.inputTokens = Convert.ToInt64(input); if (u.TryGetValue("output_tokens", out var output)) result.outputTokens = Convert.ToInt64(output); }
            if (e.TryGetValue("model", out var model) && model is string modelName) result.model = modelName;
            if ((string)type == "error" || (string)type == "turn.failed" || (e.TryGetValue("is_error", out var failed) && failed is bool f && f)) error = EventError(e);
            if (mode == AiMode.Codex && e.TryGetValue("item", out var item) && item is Dictionary<string, object> map && map.TryGetValue("type", out var it) && (string)it == "agent_message" && map.TryGetValue("text", out var text)) { answer.Clear(); answer.Append(text); }
            if ((mode == AiMode.Claude || mode == AiMode.Qoder || mode == AiMode.Cursor) && (string)type == "result")
            { if (e.TryGetValue("structured_output", out var value)) structured = value; if (e.TryGetValue("result", out var response)) { answer.Clear(); answer.Append(response); } if (e.TryGetValue("subtype", out var sub) && (string)sub != "success") error = EventError(e); }
            if (mode == AiMode.OpenCode && e.TryGetValue("part", out var part) && part is Dictionary<string, object> p)
            { if ((string)type == "text" && p.TryGetValue("text", out var openText)) answer.Append(openText); if (p.TryGetValue("reason", out var reason)) finish = reason as string; }
            if (mode == AiMode.Qoder && e.TryGetValue("message", out var message) && message is Dictionary<string, object> qoderMessage && qoderMessage.TryGetValue("model", out var qoderModel)) result.model = qoderModel as string;
            if (mode == AiMode.Qoder && (string)type == "result" && e.TryGetValue("stop_reason", out var stop)) finish = stop as string;
        }
        static string EventError(Dictionary<string, object> e)
        {
            if (e.TryGetValue("error", out var value))
            {
                if (value is string text) return text;
                if (value is Dictionary<string, object> map && map.TryGetValue("message", out var message)) return message as string ?? "CLI 返回错误";
            }
            if (e.TryGetValue("result", out var result) && result is string description) return description;
            if (e.TryGetValue("message", out var detail) && detail is string summary) return summary;
            return e.TryGetValue("subtype", out var subtype) ? "CLI 返回错误（" + subtype + "）" : "CLI 返回错误，详情见 events.jsonl";
        }
        public static CliCommandResolver.LaunchSpec Resolve(string exe, List<string> args, string promptPath)
        {
            var b = new StringBuilder(); foreach (string a in args) { if (b.Length != 0) b.Append(' '); b.Append(AiNativeProcess.Quote(a)); }
            if (!CliCommandResolver.TryResolve(exe, b.ToString(), promptPath, out var launch, out var error)) throw new IOException(error); return launch;
        }
        public static ProcessStartInfo StartInfo(CliCommandResolver.LaunchSpec launch, string root)
        {
            var info = new ProcessStartInfo { FileName = launch.FileName, Arguments = launch.Arguments, WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = AiFiles.Utf8, StandardErrorEncoding = AiFiles.Utf8 };
            // Unity/Hub can retain proxy variables removed from the user's current environment.
            // CLI connections use their own configuration, without Unity's inherited proxy.
            foreach (string name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) info.EnvironmentVariables.Remove(name);
            return info;
        }
        static async Task<string> Capture(string exe, List<string> args, string root, CancellationToken token)
        {
            using (var process = Process.Start(StartInfo(Resolve(exe, args, null), root)))
            {
                var owner = Identity(process, root); using (token.Register(() => Kill(owner)))
                { var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); if (!await Task.Run(() => process.WaitForExit(10000))) { Kill(owner); throw new TimeoutException("读取 CLI 版本超时。"); } token.ThrowIfCancellationRequested(); if (process.ExitCode != 0) throw new IOException(await error); return await output; }
            }
        }
    }
}
