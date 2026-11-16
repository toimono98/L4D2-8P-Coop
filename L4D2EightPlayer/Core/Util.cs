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

    /// <summary>
    /// Reading and writing the files the app keeps (settings, picks, the game's add-on list) without losing them: a
    /// write goes to a temporary copy that's then swapped in whole (File.Replace), so a crash or a power cut leaves
    /// either the old file or the new one, never none; antivirus holding a file for a moment is waited out. Reads
    /// let other programs keep the file open (the game writing addonlist.txt).
    /// </summary>
    public static class SafeFile
    {
        public static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tmp = path + ".tmp";
            Retry(() => System.IO.File.WriteAllText(tmp, text, new UTF8Encoding(false)));
            Retry(() =>
            {
                if (System.IO.File.Exists(path)) System.IO.File.Replace(tmp, path, null, true);
                else System.IO.File.Move(tmp, path);
            });
        }

        public static string ReadText(string path)
        {
            string text = null;
            Retry(() =>
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8, true))
                    text = r.ReadToEnd();
            });
            return text;
        }

        /// <summary>A few tries over about a second: a scanner or the game holding the file lets go quickly.</summary>
        static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (IOException) when (i < 4) { System.Threading.Thread.Sleep(150 + i * 100); }
                catch (UnauthorizedAccessException) when (i < 4) { System.Threading.Thread.Sleep(150 + i * 100); }
            }
        }

        /// <summary>Notes a failure in errors.log (a save the app can live without, for now).</summary>
        public static void Note(string what, Exception ex)
        {
            try { System.IO.File.AppendAllText(Path.Combine(Paths.AppData, "errors.log"), $"{DateTime.Now:s}  {what}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}"); } catch { }
        }
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

        /// <summary>
        /// The saved object, or a new one. A missing or damaged file falls back to the copy a save left behind
        /// (path.tmp: written, not yet swapped in), so an interrupted save never resets the settings.
        /// </summary>
        public static T Load<T>(string path) where T : new()
        {
            foreach (var p in new[] { path, path + ".tmp" })
            {
                try
                {
                    if (!System.IO.File.Exists(p)) continue;
                    var o = Parse<T>(SafeFile.ReadText(p));
                    if (o != null) return o;
                }
                catch { /* damaged: try the next copy */ }
            }
            return new T();
        }

        public static void Save(string path, object o) => SafeFile.WriteText(path, Write(o));

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
