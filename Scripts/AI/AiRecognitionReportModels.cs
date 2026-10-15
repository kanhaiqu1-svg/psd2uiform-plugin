#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionIssue
    {
        public string nodeId;
        public string code;
        public string action;
        public string reason;
        public string[] relatedNodeIds = Array.Empty<string>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionHelperContract
    {
        public string ownerType;
        public string[] allowedRoles;
        public string[] textRoles;
    }

    [Serializable]
    internal sealed class AiRecognitionRunReport
    {
        public string status;
        public int targetNodeCount;
        public long elapsedMilliseconds;
        public long exportMilliseconds;
        public long cliMilliseconds;
        public int compositionRepairCalls;
        public long compositionRepairMilliseconds;
        public int repairedCompositions;
        public int pendingItemCount;
        public long inputTextBytes;
        public long imageInputBytes;
        public long outputTextBytes;
        public int fileReadCount = -1;
        public int repeatedFileReadCount = -1;
        public int contextCompactionCount = -1;
        public bool contextCompactionEstimated;
        public string telemetryError;
        public List<AiRecognitionIssue> issues = new List<AiRecognitionIssue>();
    }
}
#endif
