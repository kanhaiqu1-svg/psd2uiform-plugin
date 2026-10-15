#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using cn.efunstudio.psdreader.FullSerializer;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static partial class AiRecognitionResultLoader
    {
        internal static string Serialize(AiRecognitionDocument document)
        {
            var nodes = new List<fsData>(document.ui.Count);
            foreach (var node in document.ui)
            {
                var fields = new Dictionary<string, fsData> {
                    { "id", new fsData(node.id) }, { "type", new fsData(node.type) }
                };
                if (!string.IsNullOrEmpty(node.parent)) fields.Add("parent", new fsData(node.parent));
                if (!string.IsNullOrEmpty(node.name)) fields.Add("name", new fsData(node.name));
                if (node.sources.Length > 0)
                {
                    var sources = new List<fsData>(node.sources.Length);
                    foreach (string id in node.sources) sources.Add(new fsData(id));
                    fields.Add("sources", new fsData(sources));
                }
                if (node.rect != null)
                {
                    var r = node.rect;
                    fields.Add("rect", new fsData(new Dictionary<string, fsData> {
                        { "x", new fsData((double)r.x) }, { "y", new fsData((double)r.y) },
                        { "w", new fsData((double)r.w) }, { "h", new fsData((double)r.h) }
                    }));
                }
                if (node.text != null) fields.Add("text", WriteText(node.text));
                nodes.Add(new fsData(fields));
            }
            var issues = new List<fsData>(document.issues.Count);
            foreach (var issue in document.issues)
                issues.Add(new fsData(new Dictionary<string, fsData> {
                    { "nodeId", new fsData(issue.nodeId ?? "") }, { "code", new fsData(issue.code ?? "") },
                    { "reason", new fsData(issue.reason ?? "") }
                }));
            return fsJsonPrinter.CompressedJson(new fsData(new Dictionary<string, fsData> {
                { "version", new fsData(document.version) }, { "ui", new fsData(nodes) }, { "issues", new fsData(issues) }
            }));
        }

        internal static bool TryRead(string path, out AiRecognitionDocument result, out string error)
        {
            result = null; error = null;
            try { return TryParse(AiJobFileUtility.ReadTextShared(path), out result, out error); }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        internal static bool TryParse(string json, out AiRecognitionDocument result, out string error)
        {
            result = null; error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "识别结果为空。"; return false; }
            fsData data;
            bool complete = fsJsonParser.Parse(json, out data).Succeeded && data.IsDictionary;
            List<fsData> records;
            Dictionary<string, fsData> root = complete ? data.AsDictionary : null;
            if (complete)
            {
                if (!root.TryGetValue("version", out var version) || !version.IsString || version.AsString != AiProtocolVersions.Recognition)
                { error = "识别协议已更新，请重新识别。"; return false; }
                if (!root.TryGetValue("ui", out var nodes) || !nodes.IsList)
                { error = "识别结果缺少 ui 数组。"; return false; }
                records = nodes.AsList;
            }
            else if (!TryReadCompletePrefix(json, out records))
            { error = "识别结果没有协议版本已确认的完整节点。"; return false; }

            result = new AiRecognitionDocument { version = AiProtocolVersions.Recognition };
            if (!complete) AddIssue(result.issues, "", "incomplete_document", "仅应用已完整交付的节点，未猜补截断内容。");
            for (int ordinal = 0; ordinal < records.Count; ordinal++)
            {
                var item = records[ordinal];
                AiUiNode node;
                try
                {
                    if (!item.IsDictionary) throw new FormatException("节点必须是对象。");
                    node = new AiUiNode { id = RequiredString(item, "id"), type = RequiredString(item, "type") };
                }
                catch (Exception ex) { AddIssue(result.issues, "record:" + (ordinal + 1), "invalid_record", ex.Message); continue; }
                var fields = item.AsDictionary;
                node.parent = OptionalString(item, "parent");
                node.name = OptionalString(item, "name");
                if (fields.TryGetValue("parent", out var parent) && !parent.IsNull && !parent.IsString)
                    AddIssue(result.issues, node.id, "invalid_parent", "忽略非字符串父节点 ID，保留类型。");
                if (fields.TryGetValue("name", out var name) && !name.IsNull && !name.IsString)
                    AddIssue(result.issues, node.id, "invalid_name", "忽略非字符串名称。");
                if (!IsEnglishNodeName(node.name))
                {
                    string suppliedName = node.name;
                    node.name = node.type + "_" + (IsEnglishNodeName(node.id) ? node.id : "Node" + (ordinal + 1));
                    if (!string.IsNullOrEmpty(suppliedName))
                        AddIssue(result.issues, node.id, "invalid_name", "节点名必须使用英文字母、数字和下划线，并以英文字母开头；已使用 " + node.name + "。");
                }
                if (fields.TryGetValue("sources", out var sources) && !sources.IsNull)
                {
                    if (!sources.IsList) AddIssue(result.issues, node.id, "invalid_sources", "sources 必须是素材 ID 数组；保留类型和结构。");
                    else
                    {
                        var ids = new List<string>(sources.AsList.Count);
                        foreach (var source in sources.AsList)
                            if (source.IsString) ids.Add(source.AsString);
                            else AddIssue(result.issues, node.id, "invalid_source", "忽略非字符串素材 ID。");
                        node.sources = ids.ToArray();
                    }
                }
                if (fields.TryGetValue("rect", out var rect) && !rect.IsNull)
                {
                    try
                    {
                        if (!rect.IsDictionary) throw new FormatException("rect 必须是 x、y、w、h 对象。");
                        var r = new RectData { x = Number(rect, "x"), y = Number(rect, "y"), w = Number(rect, "w"), h = Number(rect, "h") };
                        if (r.w <= 0 || r.h <= 0) throw new FormatException("rect 尺寸必须为正。");
                        node.rect = r;
                    }
                    catch (Exception ex) { AddIssue(result.issues, node.id, "invalid_rect", ex.Message + " 忽略该字段，保留节点。"); }
                }
                if (fields.TryGetValue("text", out var text) && !text.IsNull)
                    node.text = ReadText(text, node.id, result.issues);
                result.ui.Add(node);
            }
            if (root != null && root.TryGetValue("issues", out var issues) && issues.IsList)
                foreach (var item in issues.AsList)
                    if (item.IsDictionary) result.issues.Add(new AiRecognitionIssue {
                        nodeId = OptionalString(item, "nodeId"), code = OptionalString(item, "code"),
                        reason = OptionalString(item, "reason"), action = "复核该局部，其余结果继续应用"
                    });
            return true;
        }

        private static bool IsEnglishNodeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            char first = value[0];
            if (!(first >= 'A' && first <= 'Z' || first >= 'a' && first <= 'z')) return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
            }
            return true;
        }

        // Read a syntactically complete prefix, never regex-match nested objects or repair JSON.
        private static bool TryReadCompletePrefix(string json, out List<fsData> records)
        {
            records = new List<fsData>();
            int position = 0;
            if (!Take(json, ref position, '{')) return false;
            bool versionKnown = false;
            while (position < json.Length)
            {
                if (!ReadValue(json, ref position, out var key) || !key.IsString || !Take(json, ref position, ':')) return false;
                if (key.AsString == "ui")
                {
                    if (!versionKnown || !Take(json, ref position, '[')) return false;
                    while (ReadValue(json, ref position, out var item))
                    {
                        records.Add(item);
                        if (!Take(json, ref position, ',')) break;
                    }
                    return records.Count > 0;
                }
                if (!ReadValue(json, ref position, out var value)) return false;
                if (key.AsString == "version") versionKnown = value.IsString && value.AsString == AiProtocolVersions.Recognition;
                if (!Take(json, ref position, ',')) return false;
            }
            return false;
        }

        private static bool Take(string text, ref int p, char expected)
        {
            while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
            if (p == text.Length || text[p] != expected) return false;
            p++; return true;
        }

        private static bool ReadValue(string text, ref int p, out fsData value)
        {
            value = null;
            while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
            int start = p, depth = 0;
            bool quoted = false, escaped = false;
            for (; p < text.Length; p++)
            {
                char c = text[p];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') { quoted = false; if (depth == 0) { p++; break; } }
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    if (depth == 0) break;
                    if (--depth == 0) { p++; break; }
                }
                else if (depth == 0 && (c == ',' || c == ':' || char.IsWhiteSpace(c))) break;
            }
            return p > start && !quoted && depth == 0 && fsJsonParser.Parse(text.Substring(start, p - start), out value).Succeeded;
        }

        internal static void AddIssue(List<AiRecognitionIssue> issues, string id, string code, string reason)
            => issues.Add(new AiRecognitionIssue { nodeId = id, code = code, reason = reason, action = "复核该局部，其余结果继续应用" });
        private static string OptionalString(fsData data, string key)
            => data.AsDictionary.TryGetValue(key, out var value) && value.IsString ? value.AsString : string.Empty;
        private static string RequiredString(fsData data, string key)
        {
            string value = OptionalString(data, key);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException(key + " 必须是非空字符串。");
            return value;
        }
        private static float Number(fsData data, string key)
        {
            if (!data.AsDictionary.TryGetValue(key, out var value) || !value.IsDouble && !value.IsInt64)
                throw new FormatException("rect 缺少数值 " + key);
            float n = (float)(value.IsDouble ? value.AsDouble : value.AsInt64);
            if (float.IsNaN(n) || float.IsInfinity(n)) throw new FormatException("rect 数值无效。");
            return n;
        }
    }
}
#endif
