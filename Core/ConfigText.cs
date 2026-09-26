using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZTX.BioCirculation.Core
{
    // Reads the mod's config.rules far enough for Config.Load (2026-09-26). config.rules is written
    // in the game's own ObjectText syntax, and every PART value in it reaches the DLL through the
    public static class ConfigText
    {
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return result;
            string t = StripComments(text.Replace("\r\n", "\n").TrimStart('﻿'));
            var stack = new List<string>();
            string pending = null;
            int i = 0, n = t.Length;
            while (i < n)
            {
                char c = t[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == ';' || c == ',')
                {
                    i++;
                    continue;
                }
                if (c == '{')
                {
                    stack.Add(pending ?? "?");
                    pending = null;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    pending = null;
                    i++;
                    continue;
                }
                if (c == '[')
                {
                    i = SkipBracketed(t, i, '[', ']');
                    pending = null;
                    continue;
                }
                if (c == ']')
                {
                    i++;
                    continue;
                }
                if (c == ':')
                {
                    // an inheritance list: run to the block opener or the end of the line
                    while (i < n && t[i] != '{' && t[i] != '[' && t[i] != '\n') i++;
                    continue;
                }
                if (c == '=')
                {
                    i++;
                    while (i < n && (t[i] == ' ' || t[i] == '\t')) i++;
                    if (i < n && t[i] == '{')
                    {
                        stack.Add(pending ?? "?");
                        pending = null;
                        i++;
                        continue;
                    }
                    if (i < n && t[i] == '[')
                    {
                        i = SkipBracketed(t, i, '[', ']');
                        pending = null;
                        continue;
                    }
                    int start = i;
                    while (i < n && t[i] != '\n' && t[i] != ';' && t[i] != '}') i++;
                    string value = t.Substring(start, i - start).Trim();
                    if (pending != null)
                    {
                        result[Path(stack, pending)] = value;
                        pending = null;
                    }
                    continue;
                }
                if (IsNameChar(c))
                {
                    int start = i;
                    while (i < n && IsNameChar(t[i])) i++;
                    pending = t.Substring(start, i - start);
                    continue;
                }
                i++;    // anything else (a stray quote, a lone value) is not ours
            }
            return result;
        }

        public static bool TryGetFloat(string raw, ref float target)
        {
            float f;
            if (raw != null && float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
            {
                target = f;
                return true;
            }
            return false;
        }

        public static bool TryGetBool(string raw, ref bool target)
        {
            bool b;
            if (raw != null && bool.TryParse(raw.Trim(), out b))
            {
                target = b;
                return true;
            }
            return false;
        }

        private static bool IsNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '.';
        }

        private static string Path(List<string> stack, string name)
        {
            if (stack.Count == 0) return name;
            var sb = new StringBuilder();
            foreach (var s in stack) sb.Append(s).Append('/');
            sb.Append(name);
            return sb.ToString();
        }

        private static int SkipBracketed(string t, int i, char open, char close)
        {
            int depth = 0;
            for (; i < t.Length; i++)
            {
                if (t[i] == open) depth++;
                else if (t[i] == close && --depth == 0) return i + 1;
            }
            return i;
        }

        private static string StripComments(string t)
        {
            var sb = new StringBuilder(t.Length);
            int i = 0, n = t.Length;
            bool inString = false;
            while (i < n)
            {
                char c = t[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '"') inString = false;
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    sb.Append(c);
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && t[i + 1] == '/')
                {
                    while (i < n && t[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && t[i + 1] == '*')
                {
                    int end = t.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    i = end < 0 ? n : end + 2;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }
    }
}
