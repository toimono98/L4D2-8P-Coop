using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace L4D2EightPlayer.Core
{
    /// <summary>Receives log lines from long-running work so the UI can show them.</summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Progress(string label, double fraction);
    }

    public sealed class NullLog : ILog
    {
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m) { }
        public void Progress(string l, double f) { }
    }

    public static class Paths
    {
        public static string AppData
        {
            get
            {
                var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "L4D2-8P-Manager");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string Cache => Ensure(Path.Combine(AppData, "cache"));
        public static string Toolchain => Path.Combine(AppData, "toolchain");
        public static string SettingsFile => Path.Combine(AppData, "settings.json");

        public static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Joins a forward-slash relative path onto a Windows root.</summary>
        public static string Join(string root, string rel) =>
            Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));

        public static string Rel(string root, string full)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            var f = Path.GetFullPath(full);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length).Replace('\\', '/') : f;
        }
    }

    public static class Hash
    {
        public static string File(string path)
        {
            using (var s = System.IO.File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class Json
    {
        static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        public static T Parse<T>(string json) => Serializer.Deserialize<T>(json);
        public static object Parse(string json) => Serializer.DeserializeObject(json);

        public static string Write(object o) => Pretty(Serializer.Serialize(o));

        public static T Load<T>(string path) where T : new()
        {
            try
            {
                if (System.IO.File.Exists(path)) return Parse<T>(System.IO.File.ReadAllText(path)) ?? new T();
            }
            catch { /* corrupt file: start fresh */ }
            return new T();
        }

        public static void Save(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, Write(o), new UTF8Encoding(false));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }

        /// <summary>Indents compact JSON so saved state files stay readable.</summary>
        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false, escaped = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; sb.Append(c); continue; }
                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c).Append('\n').Append(' ', ++indent * 2);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n').Append(' ', --indent * 2).Append(c);
                        break;
                    case ',':
                        sb.Append(c).Append('\n').Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public static class Dict
    {
        public static string Str(this IDictionary<string, object> d, string key, string fallback = null) =>
            d != null && d.TryGetValue(key, out var v) && v != null ? v.ToString() : fallback;
    }
}
