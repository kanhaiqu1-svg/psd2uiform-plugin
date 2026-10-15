#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UGF.EditorTools.Psd2UGUI.Cli;

namespace UGF.EditorTools.Psd2UGUI
{
    internal sealed class CliAiProvider : IAiProvider
    {
        readonly AiProfile profile;
        internal CliAiProvider(AiProfile profile) { this.profile = profile; }
        internal string Executable => profile.executable;
        public string ProviderId => profile.mode.ToString();
        public AiProviderCapabilities Capabilities => new AiProviderCapabilities { SupportsImages = true, SupportsStrictJson = true, UsesVisibleCliExecution = false };
        public void ExecuteJob(AiJobContext context, AiAnalysisRequest request)
        {
            var prompt = new StringBuilder(File.ReadAllText(request.PromptTemplatePath, AiFiles.Utf8));
            if (request.PromptTags != null) foreach (var tag in request.PromptTags) prompt.Replace(tag.key, tag.value ?? "");
            string call = request.StageName + "-" + Guid.NewGuid().ToString("N"); string root = Path.Combine(request.WorkingDirectory, ".ai-calls");
            string answer;
            var session = new AiSession(profile, root, (stage, message) => AiJobFileUtility.AppendDebugLog(context, stage + " " + message, false), request.CancellationToken);
            answer = session.ExecuteLlmAsync(call, prompt.ToString(), request.ImageInputPaths ?? new string[0], "Recognition", json => { if (!AiRecognitionResultLoader.TryParse(json, out _, out var error)) throw new FormatException(error); }, request.WorkingDirectory).GetAwaiter().GetResult();
            AiJobFileUtility.WriteText(request.OutputTempJsonPath ?? request.OutputJsonPath, answer);
            string directory = Path.Combine(root, "calls", call);
            if (!string.IsNullOrEmpty(request.OutputRawTextPath)) AiJobFileUtility.WriteText(request.OutputRawTextPath, answer);
            if (!string.IsNullOrEmpty(request.StreamOutputPath))
            {
                using (var writer = new StreamWriter(request.StreamOutputPath, false, AiFiles.Utf8))
                    foreach (string path in Directory.GetFiles(directory, "events.jsonl", SearchOption.AllDirectories)) using (var reader = new StreamReader(path, AiFiles.Utf8)) { string line; while ((line = reader.ReadLine()) != null) writer.WriteLine(line); }
            }
            if (!string.IsNullOrEmpty(request.OutputCompletedPath)) AiJobFileUtility.WriteText(request.OutputCompletedPath, DateTime.UtcNow.ToString("O"));
        }
    }
}
#endif
