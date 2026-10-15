using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    public sealed class AiSession
    {
        readonly AiProfile profile;
        readonly string root;
        readonly Action<string, string> progress;
        readonly CancellationToken token;
        public AiSession(AiProfile profile, string root, Action<string, string> progress, CancellationToken token)
        {
            this.profile = profile;
            this.root = Path.GetFullPath(root);
            this.progress = progress;
            this.token = token;
        }
        public async Task<string> ExecuteLlmAsync(string callId, string prompt, string[] images, string stage = "", Action<string> validate = null, string workingDirectory = null, int maxAttempts = 3, string repairTemplate = null, string systemPrompt = "")
        {
            var p = profile; string directory = Path.Combine(root, "calls", callId); Directory.CreateDirectory(directory);
            AiCommands.Validate(p);
            var request = new AiTaskRequest { taskId = Path.GetFileName(root), callId = callId, operation = "Llm", prompt = prompt, systemPrompt = systemPrompt, profileFingerprint = AiProfiles.Fingerprint(p), workingDirectory = workingDirectory };
            foreach (string image in images) request.references.Add(new AiReference { path = image, role = "visual reference " + request.references.Count });
            var fingerprint = new StringBuilder(UnityEngine.JsonUtility.ToJson(request)); foreach (string file in images) fingerprint.Append(AiFiles.Hash(Convert.ToBase64String(File.ReadAllBytes(file))));
            string hash = AiFiles.Hash(fingerprint.ToString()), cache = Path.Combine(directory, "response-cache.json");
            AiTaskResult candidate = null;
            if (File.Exists(cache))
            { var old = AiJson.Object(File.ReadAllText(cache, AiFiles.Utf8)); if ((string)old["hash"] == hash) candidate = UnityEngine.JsonUtility.FromJson<AiTaskResult>(AiJson.Stringify(old["result"])); }
            string label = (stage == "" ? callId : stage) + " · " + p.mode;
            progress?.Invoke("Llm", label);
            for (int attempt = 1; attempt <= maxAttempts; ++attempt)
            {
                request.callId = callId + "-" + attempt;
                AiTaskResult result = null;
                try
                {
                    result = candidate;
                    candidate = null;
                    if (result == null)
                    {
                        result = await AiCli.Execute(p, request, Path.Combine(directory, attempt.ToString()), token);
                        AiFiles.WriteText(cache, AiJson.Stringify(AiJson.Map("hash", hash, "result", AiJson.Object(UnityEngine.JsonUtility.ToJson(result)))));
                    }
                    AiJson.Object(result.json); validate?.Invoke(result.json);
                    result.status = "Completed"; result.completedUtc = DateTime.UtcNow.ToString("O"); AiFiles.Write(Path.Combine(Path.GetDirectoryName(result.outputPath), "status.json"), result);
                    AiFiles.Write(Path.Combine(directory, "result.json"), result); return result.json;
                }
                catch (FormatException e) when (attempt < maxAttempts)
                {
                    if (result != null) { result.status = "Rejected"; result.error = e.Message; AiFiles.Write(Path.Combine(Path.GetDirectoryName(result.outputPath), "status.json"), result); }
                    request.repairJsonPath = result == null ? "" : result.outputPath;
                    request.prompt = prompt + "\n" + (repairTemplate == null ? "Previous result rejected: " + e.Message + ". Correct only this issue in the supplied result file." : repairTemplate.Replace("[ERROR]", e.Message));
                    request.references.Clear(); foreach (string image in images) request.references.Add(new AiReference { path = image, role = "visual reference" });
                }
                catch (Exception e) when ((e is IOException || e is TimeoutException) && attempt < maxAttempts)
                {
                    // CLI failures are transient; retry the same request instead of failing the task.
                    await Task.Delay(2000 * attempt, token);
                }
                catch (Exception e)
                {
                    if (result != null) { result.status = "Rejected"; result.error = e.Message; AiFiles.Write(Path.Combine(Path.GetDirectoryName(result.outputPath), "status.json"), result); }
                    AiFiles.Write(Path.Combine(directory, "status.json"), new AiTaskResult { callId = callId, status = e is OperationCanceledException ? "Cancelled" : "Failed", error = e.Message }); throw;
                }
            }
            throw new FormatException(maxAttempts + " 次结构化响应未通过验证：" + callId);
        }
    }
}
