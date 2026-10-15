using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    public enum AiMode { Claude, Codex, OpenCode, Qoder, Cursor }
    [Serializable] public sealed class AiProfile
    {
        public AiMode mode = AiMode.Qoder;
        public string executable = "qodercli";
        public int requestTimeoutMinutes = 120;
    }
    [Serializable] public sealed class AiReference { public string path, role; }
    [Serializable] public sealed class AiTaskRequest
    {
        public int schemaVersion = 1;
        public string taskId, callId, profileFingerprint, prompt, outputPath, workingDirectory;
        public string operation = "Llm", systemPrompt = "", repairJsonPath = "";
        public List<AiReference> references = new List<AiReference>();
    }
    [Serializable] public sealed class AiTaskResult
    {
        public int schemaVersion = 1;
        public string taskId, callId, status, outputPath, json, model, finishReason, error, completedUtc;
        public long inputTokens = -1, outputTokens = -1;
    }
    [Serializable] public sealed class OwnedProcess { public int pid; public double created; public string exe, jobDir; }
    [Serializable] public sealed class OwnedProcesses { public List<OwnedProcess> processes = new List<OwnedProcess>(); }
}
