using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// After the server starts: reads SourceMod's logs for plugins and extensions that failed to load or hit
    /// errors (often a game update that broke a plugin's gamedata), and names the component each belongs to.
    /// </summary>
    public static class ServerHealth
    {
        public sealed class Issue
        {
            public string File;       // e.g. l4d2_8p_characters.smx
            public string Component;  // the manifest name, or the file when it isn't ours
            public string Problem;    // one line, plain words
            public override string ToString() => Component + ": " + Problem;
        }

        // L 11/02/2026 - 16:49:09: message
        static readonly Regex Line = new Regex(@"^L (\d\d/\d\d/\d{4}) - (\d\d:\d\d:\d\d): (.*)$", RegexOptions.Compiled);
        static readonly Regex FailedPlugin = new Regex(@"^\[SM\] (?:Failed|Unable) to load plugin ""([^""]+)"": (.*)$", RegexOptions.Compiled);
        static readonly Regex FailedExtension = new Regex(@"^\[SM\] (?:Failed|Unable) to load extension ""([^""]+)"": (.*)$", RegexOptions.Compiled);
        static readonly Regex Exception = new Regex(@"^\[SM\] Exception reported: (.*)$", RegexOptions.Compiled);
        static readonly Regex Blaming = new Regex(@"^\[SM\] Blaming: (.+?\.smx)", RegexOptions.Compiled);
        static readonly Regex PluginError = new Regex(@"^\[([^\]]+\.smx)\] (.*)$", RegexOptions.Compiled);

        /// <summary>Problems logged since <paramref name="since"/> (local time) in the server's left4dead2 folder.</summary>
        public static List<Issue> Check(string gameDir, DateTime since, Manifest manifest)
        {
            var issues = new List<Issue>();
            var logs = Path.Combine(gameDir ?? "", "addons", "sourcemod", "logs");
            if (!Directory.Exists(logs)) return issues;

            // the error logs from the start date on (a server started before midnight writes two)
            var files = Directory.GetFiles(logs, "errors_*.log")
                .Where(f => File.GetLastWriteTime(f) >= since.AddMinutes(-1))
                .OrderBy(f => f);
            string pendingException = null;
            foreach (var file in files)
            {
                foreach (var raw in ReadShared(file))
                {
                    var m = Line.Match(raw);
                    if (!m.Success) continue;
                    if (!DateTime.TryParseExact(m.Groups[1].Value + " " + m.Groups[2].Value, "MM/dd/yyyy HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) || when < since) continue;
                    var msg = m.Groups[3].Value.Trim();

                    Match x;
                    if ((x = FailedPlugin.Match(msg)).Success)
                    {
                        // SourceMod's own plugins for other games (nextmap) always fail on L4D2; that's expected
                        if (x.Groups[2].Value.IndexOf("incompatible with this game", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        Add(issues, manifest, x.Groups[1].Value, "didn't load: " + Clean(x.Groups[2].Value));
                    }
                    else if ((x = FailedExtension.Match(msg)).Success)
                        Add(issues, manifest, x.Groups[1].Value, "extension didn't load: " + Clean(x.Groups[2].Value));
                    else if ((x = Exception.Match(msg)).Success)
                        pendingException = Clean(x.Groups[1].Value);
                    else if ((x = Blaming.Match(msg)).Success && pendingException != null)
                    {
                        Add(issues, manifest, x.Groups[1].Value, "hit an error: " + pendingException);
                        pendingException = null;
                    }
                    else if ((x = PluginError.Match(msg)).Success)
                        Add(issues, manifest, x.Groups[1].Value, Clean(x.Groups[2].Value));
                }
            }
            return Merge(issues);
        }

        const string StartupFailed = "didn't load: Error detected in plugin startup (see error logs)";

        /// <summary>One line per file: "didn't load" with the plugin's own reason when it failed to start.</summary>
        static List<Issue> Merge(List<Issue> issues)
        {
            var merged = new List<Issue>();
            foreach (var g in issues.GroupBy(i => i.File, StringComparer.OrdinalIgnoreCase))
            {
                var all = g.ToList();
                bool failed = all.Any(i => i.Problem == StartupFailed);
                var reasons = all.Where(i => i.Problem != StartupFailed).Select(i => i.Problem.StartsWith("hit an error: ") ? i.Problem.Substring(14) : i.Problem).Distinct().ToList();
                string problem = failed
                    ? "didn't load" + (reasons.Count > 0 ? ": " + reasons.Last() : " (see the error log)")
                    : string.Join("; ", all.Select(i => i.Problem).Distinct());
                merged.Add(new Issue { File = all[0].File, Component = all[0].Component, Problem = problem });
            }
            return merged;
        }

        static IEnumerable<string> ReadShared(string file)
        {
            // the server keeps its log open; read it without locking
            string text;
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) text = r.ReadToEnd();
            }
            catch (IOException) { yield break; }
            catch (UnauthorizedAccessException) { yield break; }
            foreach (var l in text.Split('\n')) yield return l.TrimEnd('\r');
        }

        static string Clean(string s) => s.Length > 200 ? s.Substring(0, 200) + "…" : s;

        static void Add(List<Issue> issues, Manifest manifest, string file, string problem)
        {
            file = Path.GetFileName(file);
            if (issues.Any(i => i.File.Equals(file, StringComparison.OrdinalIgnoreCase) && i.Problem == problem)) return;
            issues.Add(new Issue { File = file, Component = ComponentFor(manifest, file) ?? file, Problem = problem });
        }

        /// <summary>The component that installs this plugin or extension file, by name.</summary>
        static string ComponentFor(Manifest manifest, string file)
        {
            if (manifest == null) return null;
            var stem = Path.GetFileNameWithoutExtension(file);
            foreach (var c in manifest.components)
            {
                foreach (var t in c.KnownTargets())
                {
                    var n = Path.GetFileNameWithoutExtension(t);
                    if (n.Equals(stem, StringComparison.OrdinalIgnoreCase) || n.Equals(stem + ".ext", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(t).StartsWith(stem + ".ext", StringComparison.OrdinalIgnoreCase))
                        return c.name;
                }
            }
            return null;
        }
    }
}
