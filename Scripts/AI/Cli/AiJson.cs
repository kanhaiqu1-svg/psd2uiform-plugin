using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UGF.EditorTools.Psd2UGUI.Cli
{
    // JSON is confined to the model/network boundary. No external Unity package or runtime dependency.
    public static class AiJson
    {
        public static Dictionary<string, object> Object(string text)
        {
            var value = new Reader(text).ReadDocument();
            if (value is Dictionary<string, object> map) return map;
            throw new FormatException("Expected a JSON object.");
        }
        public static string Stringify(object value) { var b = new StringBuilder(); Write(b, value); return b.ToString(); }
        static void Write(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string s)
            {
                b.Append('"');
                foreach (char c in s)
                    switch (c)
                    {
                        case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break;
                        case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break;
                        default: if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
                    }
                b.Append('"'); return;
            }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary<string, object> map)
            {
                b.Append('{'); bool first = true;
                foreach (var item in map) { if (!first) b.Append(','); first = false; Write(b, item.Key); b.Append(':'); Write(b, item.Value); }
                b.Append('}'); return;
            }
            if (value is IList list)
            {
                b.Append('['); for (int i = 0; i < list.Count; ++i) { if (i > 0) b.Append(','); Write(b, list[i]); } b.Append(']'); return;
            }
            b.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        public static Dictionary<string, object> Map(params object[] pairs)
        {
            var map = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) map.Add((string)pairs[i], pairs[i + 1]); return map;
        }
        public static void Merge(Dictionary<string, object> destination, Dictionary<string, object> patch)
        {
            foreach (var p in patch)
                if (p.Value is Dictionary<string, object> nested && destination.TryGetValue(p.Key, out var current) && current is Dictionary<string, object> map) Merge(map, nested);
                else destination[p.Key] = p.Value;
        }
        sealed class Reader
        {
            readonly string text; int position, depth;
            internal Reader(string text) { this.text = text; }
            internal object ReadDocument() { object value = Read(); Space(); if (position != text.Length) Fail(); return value; }
            void Space() { while (position < text.Length && char.IsWhiteSpace(text[position])) ++position; }
            void Fail() => throw new FormatException("Invalid JSON at character " + position);
            bool Take(char c) { Space(); if (position < text.Length && text[position] == c) { ++position; return true; } return false; }
            void Expect(char c) { if (!Take(c)) Fail(); }
            object Read()
            {
                Space(); if (position == text.Length || ++depth > 64) Fail();
                object value;
                char c = text[position];
                if (c == '{')
                {
                    ++position; var map = new Dictionary<string, object>();
                    if (!Take('}')) { do { string key = String(); Expect(':'); if (map.ContainsKey(key)) Fail(); map.Add(key, Read()); } while (Take(',')); Expect('}'); }
                    value = map;
                }
                else if (c == '[')
                {
                    ++position; var list = new List<object>();
                    if (!Take(']')) { do { list.Add(Read()); } while (Take(',')); Expect(']'); } value = list;
                }
                else if (c == '"') value = String();
                else if (c == 't') { Literal("true"); value = true; }
                else if (c == 'f') { Literal("false"); value = false; }
                else if (c == 'n') { Literal("null"); value = null; }
                else
                {
                    int start = position;
                    while (position < text.Length && "-+0123456789.eE".IndexOf(text[position]) >= 0) ++position;
                    double number = 0;
                    if (position == start || !double.TryParse(text.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number)) Fail();
                    value = number;
                }
                --depth; return value;
            }
            void Literal(string literal) { if (position + literal.Length > text.Length || text.Substring(position, literal.Length) != literal) Fail(); position += literal.Length; }
            string String()
            {
                Expect('"'); var b = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++]; if (c == '"') return b.ToString(); if (c < 32) Fail();
                    if (c != '\\') { b.Append(c); continue; }
                    if (position == text.Length) Fail(); c = text[position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': b.Append(c); break;
                        case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                        case 'u': if (position + 4 > text.Length) Fail(); b.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber)); position += 4; break;
                        default: Fail(); break;
                    }
                }
                Fail(); return null;
            }
        }
    }
}
