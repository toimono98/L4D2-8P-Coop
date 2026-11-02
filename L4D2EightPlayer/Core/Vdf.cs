using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Minimal Valve KeyValues (text VDF) reader. Values are either strings or nested
    /// case-insensitive dictionaries; duplicate keys keep the last value.
    /// </summary>
    public static class Vdf
    {
        public static Dictionary<string, object> ParseFile(string path) => Parse(File.ReadAllText(path));

        public static Dictionary<string, object> Parse(string text)
        {
            int pos = 0;
            return ReadBlock(text, ref pos, topLevel: true);
        }

        public static Dictionary<string, object> Block(this Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static string Value(this Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as string : null;

        static Dictionary<string, object> ReadBlock(string s, ref int pos, bool topLevel)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = NextToken(s, ref pos);
                if (key == null) return result;            // end of input
                if (key == "}" && !topLevel) return result;
                if (key == "{" || key == "}") continue;    // stray brace, ignore

                var value = NextToken(s, ref pos);
                if (value == null) return result;
                if (value == "{")
                    result[key] = ReadBlock(s, ref pos, topLevel: false);
                else
                    result[key] = value;

                SkipConditional(s, ref pos);
            }
        }

        /// <summary>Skips platform conditionals such as [$WIN32] that may follow a value.</summary>
        static void SkipConditional(string s, ref int pos)
        {
            int p = pos;
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t')) p++;
            if (p < s.Length && s[p] == '[')
            {
                int end = s.IndexOf(']', p);
                if (end > 0) pos = end + 1;
            }
        }

        static string NextToken(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }
                if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
                {
                    while (pos < s.Length && s[pos] != '\n') pos++;
                    continue;
                }
                break;
            }
            if (pos >= s.Length) return null;

            char ch = s[pos];
            if (ch == '{' || ch == '}') { pos++; return ch.ToString(); }

            var sb = new StringBuilder();
            if (ch == '"')
            {
                pos++;
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length)
                    {
                        char n = s[pos + 1];
                        if (n == '"' || n == '\\') { sb.Append(n); pos += 2; continue; }
                        if (n == 'n') { sb.Append('\n'); pos += 2; continue; }
                        if (n == 't') { sb.Append('\t'); pos += 2; continue; }
                    }
                    sb.Append(s[pos++]);
                }
                pos++; // closing quote
                return sb.ToString();
            }

            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}' && s[pos] != '"')
                sb.Append(s[pos++]);
            return sb.ToString();
        }
    }
}
