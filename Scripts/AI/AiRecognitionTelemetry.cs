#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    // Scan once after execution and retain only event metadata, not tool outputs or image payloads.
    internal static class AiRecognitionTelemetry
    {
#pragma warning disable 0649
        [Serializable] private sealed class Event { public string type; public Part part; }
        [Serializable] private sealed class Part { public string id; public string type; public string tool; public string callID; public string text; public State state; }
        [Serializable] private sealed class State { public string status; public Input input; }
        [Serializable] private sealed class Input { public string filePath; }
#pragma warning restore 0649

        internal static void Read(string providerId, string path, AiRecognitionRunReport report)
        {
            // Other providers expose different event shapes. -1 means unavailable, never zero.
            if (providerId != "OpenCode" || !File.Exists(path)) return;
            int reads = 0, repeats = 0, summaries = 0, compactions = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var events = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] != '{') continue;
                        Event item;
                        try { item = JsonUtility.FromJson<Event>(line); }
                        catch (ArgumentException) { continue; }
                        var part = item?.part;
                        if (part == null) continue;
                        string id = part.callID ?? part.id;
                        if (item.type == "tool_use" && part.tool == "read" && part.state?.status == "completed")
                        {
                            if (id != null && !events.Add("read:" + id)) continue;
                            string file = part.state.input?.filePath;
                            if (string.IsNullOrEmpty(file)) continue;
                            reads++;
                            file = file.Replace('\\', '/');
                            while (file.Contains("//")) file = file.Replace("//", "/");
                            if (!paths.Add(file)) repeats++;
                        }
                        else if (part.type == "compaction" || item.type == "compaction")
                        {
                            if (id == null || events.Add("compact:" + id)) compactions++;
                        }
                        else if (item.type == "text" && part.text != null && part.text.StartsWith("## Objective", StringComparison.Ordinal)
                            && part.text.Contains("## Important Details"))
                        {
                            if (id == null || events.Add("summary:" + id)) summaries++;
                        }
                    }
                }
                report.fileReadCount = reads;
                report.repeatedFileReadCount = repeats;
                report.contextCompactionCount = compactions > 0 ? compactions : summaries;
                report.contextCompactionEstimated = compactions == 0;
            }
            catch (IOException ex) { report.telemetryError = ex.Message; }
        }
    }
}
#endif
