using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TapGJ.Playthrough
{
    /// <summary>
    /// 够用就好的 Unity YAML 读取器：只处理关卡资产 / 预制体 / 事件资产里出现的那几种写法
    /// （缩进 map、`- item` 列表、行内 `{...}` 与 `[]`、带 \u 转义的双引号字符串）。
    /// 不是通用 YAML 解析器，也不打算是。
    /// </summary>
    public sealed class YamlDoc
    {
        public string ClassTag;   // 例如 "!u!114"
        public string FileId;     // 例如 "11400000"
        public Dictionary<string, object> Root = new Dictionary<string, object>();

        public string Str(string key)
            => Root.TryGetValue(key, out var v) ? v as string : null;

        public int Int(string key, int fallback = 0)
        {
            var s = Str(key);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        }

        public bool Bool(string key, bool fallback = false)
        {
            var s = Str(key);
            if (s == null) return fallback;
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public Dictionary<string, object> Map(string key)
            => Root.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public List<object> List(string key)
            => Root.TryGetValue(key, out var v) ? v as List<object> : null;

        /// <summary>从 `{fileID: 1, guid: abc, type: 3}` 里取 guid。</summary>
        public static string GuidOf(string inline)
        {
            if (string.IsNullOrEmpty(inline)) return null;
            int i = inline.IndexOf("guid:", StringComparison.Ordinal);
            if (i < 0) return null;
            int start = i + 5;
            int end = start;
            while (end < inline.Length && inline[end] != ',' && inline[end] != '}') end++;
            return inline.Substring(start, end - start).Trim();
        }
    }

    public static class Yaml
    {
        public static List<YamlDoc> Load(string path)
        {
            var lines = System.IO.File.ReadAllLines(path);
            var docs = new List<YamlDoc>();
            var current = new List<string>();
            string header = null;

            foreach (var raw in lines)
            {
                if (raw.StartsWith("---", StringComparison.Ordinal))
                {
                    if (header != null) docs.Add(ParseDoc(header, current));
                    header = raw;
                    current = new List<string>();
                    continue;
                }
                if (header == null) continue;
                current.Add(raw);
            }
            if (header != null) docs.Add(ParseDoc(header, current));
            return docs;
        }

        static YamlDoc ParseDoc(string header, List<string> body)
        {
            var doc = new YamlDoc();
            // --- !u!114 &11400000
            var parts = header.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (p.StartsWith("!u!", StringComparison.Ordinal)) doc.ClassTag = p;
                if (p.StartsWith("&", StringComparison.Ordinal)) doc.FileId = p.Substring(1);
            }

            int i = 0;
            // 跳过类名行（MonoBehaviour: / GameObject: / Transform:）
            while (i < body.Count && string.IsNullOrWhiteSpace(body[i])) i++;
            if (i < body.Count && body[i].TrimEnd().EndsWith(":", StringComparison.Ordinal) &&
                !body[i].StartsWith(" ", StringComparison.Ordinal))
            {
                i++;
            }

            doc.Root = ParseMap(body, ref i, i < body.Count ? IndentOf(body[i]) : 0);
            return doc;
        }

        static int IndentOf(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ') n++;
            return n;
        }

        static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

        static Dictionary<string, object> ParseMap(List<string> lines, ref int i, int indent)
        {
            var map = new Dictionary<string, object>();
            while (i < lines.Count)
            {
                var line = lines[i];
                if (IsBlank(line)) { i++; continue; }

                int ind = IndentOf(line);
                if (ind < indent) break;
                if (ind > indent) { i++; continue; } // 不该出现；跳过，别死循环

                var trimmed = line.Trim();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-") break;

                int colon = trimmed.IndexOf(':');
                if (colon < 0) { i++; continue; }

                var key = trimmed.Substring(0, colon).Trim();
                var rest = trimmed.Substring(colon + 1).Trim();
                i++;

                if (rest.Length == 0)
                {
                    // 嵌套块：Unity 的序列项常和 key 同缩进（`levels:` 下面是 `- {...}`），
                    // 所以「同缩进 + 以 - 开头」也要当成子块。
                    int j = i;
                    while (j < lines.Count && IsBlank(lines[j])) j++;
                    if (j < lines.Count)
                    {
                        int jind = IndentOf(lines[j]);
                        bool isList = lines[j].TrimStart().StartsWith("-", StringComparison.Ordinal);

                        if (jind > ind || (jind == ind && isList))
                        {
                            i = j;
                            map[key] = isList ? ParseList(lines, ref i, jind) : ParseMap(lines, ref i, jind);
                            continue;
                        }
                    }

                    map[key] = null;
                }
                else if (rest == "[]")
                {
                    map[key] = new List<object>();
                }
                else
                {
                    map[key] = ReadScalar(lines, ref i, rest);
                }
            }
            return map;
        }

        static List<object> ParseList(List<string> lines, ref int i, int indent)
        {
            var list = new List<object>();
            while (i < lines.Count)
            {
                var line = lines[i];
                if (IsBlank(line)) { i++; continue; }

                int ind = IndentOf(line);
                if (ind < indent) break;
                if (ind > indent) { i++; continue; }

                var trimmed = line.Trim();
                if (!trimmed.StartsWith("-", StringComparison.Ordinal)) break;

                var rest = trimmed.Length > 1 ? trimmed.Substring(1).Trim() : string.Empty;

                if (rest.Length == 0)
                {
                    i++;
                    int j = i;
                    while (j < lines.Count && IsBlank(lines[j])) j++;
                    if (j < lines.Count && IndentOf(lines[j]) > ind)
                    {
                        i = j;
                        list.Add(ParseMap(lines, ref i, IndentOf(lines[i])));
                    }
                    else list.Add(null);
                    continue;
                }

                int colon = rest.IndexOf(':');
                bool looksLikeMapItem = colon > 0 && !rest.StartsWith("{", StringComparison.Ordinal) &&
                                        rest[colon + 1] == ' ';
                if (looksLikeMapItem)
                {
                    // `- title: "第一章"` + 后续更深缩进的兄弟字段
                    var itemIndent = ind + 2;
                    // 把当前行改写成同等缩进的 key: value，再交给 ParseMap 继续吃后面的行
                    lines[i] = new string(' ', itemIndent) + rest;
                    list.Add(ParseMap(lines, ref i, itemIndent));
                    continue;
                }

                i++;
                list.Add(ReadScalar(lines, ref i, rest));
            }
            return list;
        }

        static string ReadScalar(List<string> lines, ref int i, string first)
        {
            if (first.Length > 0 && first[0] == '"')
            {
                var sb = new StringBuilder();
                var text = first;
                while (true)
                {
                    int closing = FindClosingQuote(text, 1);
                    if (closing >= 0)
                    {
                        sb.Append(text, 1, closing - 1);
                        break;
                    }

                    sb.Append(text, 1, text.Length - 1);
                    sb.Append(' ');
                    if (i >= lines.Count) break;
                    text = lines[i].Trim();
                    i++;
                }
                return Unescape(sb.ToString());
            }

            if (first.Length > 0 && first[0] == '\'')
            {
                int end = first.IndexOf('\'', 1);
                return end > 0 ? first.Substring(1, end - 1) : first.Trim('\'');
            }

            return first;
        }

        static int FindClosingQuote(string s, int from)
        {
            for (int k = from; k < s.Length; k++)
            {
                if (s[k] == '\\') { k++; continue; }
                if (s[k] == '"') return k;
            }
            return -1;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }

                char c = s[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else sb.Append("\\u");
                        break;
                    default: sb.Append('\\').Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
