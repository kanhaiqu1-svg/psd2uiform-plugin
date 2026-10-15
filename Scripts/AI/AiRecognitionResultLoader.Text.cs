#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using cn.efunstudio.psdreader.FullSerializer;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    internal static partial class AiRecognitionResultLoader
    {
        private static fsData WriteText(AiTextSpec text)
        {
            var fields = new Dictionary<string, fsData> { { "mode", new fsData(text.mode) } };
            if (text.mode == "source") fields.Add("source", new fsData(text.source));
            else
            {
                fields.Add("value", new fsData(text.value));
                if (!string.IsNullOrEmpty(text.styleSource)) fields.Add("styleSource", new fsData(text.styleSource));
                if (text.style != null)
                {
                    var s = text.style;
                    var style = new Dictionary<string, fsData>();
                    Add("font", s.font); Add("color", s.color); Add("fontStyle", s.fontStyle);
                    Add("alignment", s.alignment); Add("outlineColor", s.outlineColor);
                    if (s.fontSize > 0) style.Add("fontSize", new fsData((double)s.fontSize));
                    if (s.outlineSize > 0) style.Add("outlineSize", new fsData((double)s.outlineSize));
                    fields.Add("style", new fsData(style));
                    void Add(string key, string value) { if (!string.IsNullOrEmpty(value)) style.Add(key, new fsData(value)); }
                }
                if (text.rotation != 0) fields.Add("rotation", new fsData((double)text.rotation));
                if (text.wrap) fields.Add("wrap", new fsData(true));
            }
            return new fsData(fields);
        }

        private static AiTextSpec ReadText(fsData data, string id, List<AiRecognitionIssue> issues)
        {
            var text = new AiTextSpec();
            try
            {
                if (!data.IsDictionary) throw new FormatException("text 必须是对象。");
                text.mode = RequiredString(data, "mode");
                if (text.mode == "source")
                {
                    text.source = RequiredString(data, "source");
                    if (data.AsDictionary.ContainsKey("value") || data.AsDictionary.ContainsKey("styleSource"))
                        throw new FormatException("source 模式完整引用原生文字，不接受 value/styleSource。");
                    return text;
                }
                if (text.mode != "value") throw new FormatException("text.mode 只能是 source 或 value。");
                if (!data.AsDictionary.TryGetValue("value", out var value) || !value.IsString)
                    throw new FormatException("value 模式必须提供文字字符串，可以为空。");
                if (data.AsDictionary.ContainsKey("source")) throw new FormatException("value 模式不能同时提供 text.source。");
                text.value = value.AsString;
            }
            catch (Exception ex) { Issue("invalid_text", ex.Message); return null; }

            text.styleSource = ReadString(data, "styleSource");
            if (data.AsDictionary.TryGetValue("style", out var style) && !style.IsNull)
            {
                if (!style.IsDictionary) Issue("invalid_text_style", "忽略非对象的 style。");
                else text.style = new AiTextStyle {
                    font = ReadString(style, "font"), fontSize = ReadNumber(style, "fontSize", true),
                    color = ReadColor(style, "color"), fontStyle = ReadEnum<FontStyle>(style, "fontStyle"),
                    alignment = ReadEnum<TextAnchor>(style, "alignment"),
                    outlineSize = ReadNumber(style, "outlineSize", true), outlineColor = ReadColor(style, "outlineColor")
                };
            }
            text.rotation = ReadNumber(data, "rotation", false);
            if (data.AsDictionary.TryGetValue("wrap", out var wrap))
            {
                if (wrap.IsBool) text.wrap = wrap.AsBool;
                else Issue("invalid_text_wrap", "忽略非布尔的 wrap。");
            }
            return text;

            void Issue(string code, string reason) => AddIssue(issues, id, code, reason + " 保留其他有效字段和节点。");
            string ReadString(fsData owner, string key)
            {
                if (!owner.AsDictionary.TryGetValue(key, out var value) || value.IsNull) return null;
                if (value.IsString) return value.AsString;
                Issue("invalid_text_" + key, key + " 必须是字符串。"); return null;
            }
            float ReadNumber(fsData owner, string key, bool positive)
            {
                if (!owner.AsDictionary.ContainsKey(key)) return 0;
                try
                {
                    float value = Number(owner, key);
                    if (positive && value <= 0) throw new FormatException(key + " 必须为正数。");
                    return value;
                }
                catch (Exception ex) { Issue("invalid_text_" + key, ex.Message); return 0; }
            }
            string ReadColor(fsData owner, string key)
            {
                string value = ReadString(owner, key);
                if (value == null) return null;
                if ((value.Length == 7 || value.Length == 9) && value[0] == '#'
                    && ColorUtility.TryParseHtmlString(value, out var color)) return "#" + ColorUtility.ToHtmlStringRGBA(color);
                Issue("invalid_text_" + key, key + " 必须是 #RRGGBB 或 #RRGGBBAA。"); return null;
            }
            string ReadEnum<T>(fsData owner, string key) where T : struct
            {
                string value = ReadString(owner, key);
                if (value == null) return null;
                if (Enum.TryParse(value, false, out T parsed) && Enum.IsDefined(typeof(T), parsed) && parsed.ToString() == value) return value;
                Issue("invalid_text_" + key, key + " 枚举值无效。"); return null;
            }
        }
    }
}
#endif
