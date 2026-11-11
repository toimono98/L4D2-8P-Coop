using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Updates for an installed app. The Setup asks whether to install them automatically or by hand (updates.json,
    /// shared by both sides). Automatic: a newer release's Setup is downloaded in the background and installed when
    /// the app closes (or straight away with Restart now). By hand: the header says a version is out, and Install now
    /// does the same on request. Either way the Setup runs silently: it waits for the app to exit, updates the install
    /// with the same folder, shortcuts and start side, and (after Restart now or Install now) opens the app again.
    /// A copy of the app that isn't the installed one (run from Downloads) only points at the release page.
    /// </summary>
    public static class Updater
    {
        public const string SetupAsset = "L4D2-8P-Setup.exe";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\L4D2-8P-Coop";

        public sealed class Prefs
        {
            /// <summary>True: install updates automatically. False: let me install them. Null: never asked (installed before 2.5.8): by hand.</summary>
            public bool? Auto { get; set; }
        }

        static string PrefsFile => Path.Combine(Paths.AppData, "updates.json");
        public static string Folder => Path.Combine(Paths.AppData, "updates");
        /// <summary>The last update's Setup log (the app reads it to say when an update didn't go through).</summary>
        public static string LastLog => Path.Combine(Folder, "last-update.log");

        public static Prefs Load() { try { return Json.Load<Prefs>(PrefsFile); } catch { return new Prefs(); } }
        public static bool Auto => Load().Auto == true;

        public static void SetAuto(bool on)
        {
            try { Json.Save(PrefsFile, new Prefs { Auto = on }); } catch { /* the next start asks GitHub the same way */ }
        }

        /// <summary>The installed app's folder (from its Installed apps entry), or null.</summary>
        public static string InstalledFolder()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    var folder = k?.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : folder;
                }
            }
            catch { return null; }
        }

        /// <summary>Whether this running app is the installed one (an update replaces that one, not a stray copy).</summary>
        public static bool RunningInstalled(string exePath)
        {
            var folder = InstalledFolder();
            return folder != null && exePath != null
                && string.Equals(Path.GetFullPath(Path.GetDirectoryName(exePath)).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A downloaded Setup for this version that checks out, or null.</summary>
        public static string Downloaded(string version)
        {
            var path = Path.Combine(Folder, $"L4D2-8P-Setup-{version}.exe");
            return File.Exists(path) && IsSetupFor(path, version) ? path : null;
        }

        /// <summary>The file is the Setup for that version: its version stamp says so (and it's big enough to carry the app).</summary>
        public static bool IsSetupFor(string path, string version)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length < 1024 * 1024) return false;
                var v = FileVersionInfo.GetVersionInfo(path);
                var pv = v.ProductVersion ?? "";
                return v.ProductName == "L4D2 8-Player Co-op Setup" && (pv == version || pv.StartsWith(version + "+", StringComparison.Ordinal));
            }
            catch { return false; }
        }

        /// <summary>Downloads the Setup of a release (from GitHub's release assets) and checks it. Returns its path.</summary>
        public static async Task<string> Download(string version, ILog log, CancellationToken ct = default)
        {
            var ready = Downloaded(version);
            if (ready != null) return ready;
            var json = Json.Parse(await Http.GetString($"https://api.github.com/repos/{UpdateCheck.Repo}/releases/tags/v{version}", ct).ConfigureAwait(false)) as Dictionary<string, object>;
            var url = (json != null && json.TryGetValue("assets", out var a) && a is object[] assets ? assets : new object[0])
                .OfType<Dictionary<string, object>>()
                .Where(x => x.TryGetValue("name", out var n) && n as string == SetupAsset)
                .Select(x => x.TryGetValue("browser_download_url", out var u) ? u as string : null)
                .FirstOrDefault(u => u != null && u.StartsWith("https://github.com/" + UpdateCheck.Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase));
            if (url == null) throw new IOException($"version {version} has no {SetupAsset} to download");
            var dest = Path.Combine(Folder, $"L4D2-8P-Setup-{version}.exe");
            await Http.Download(url, dest, log ?? new NullLog(), ct).ConfigureAwait(false);
            if (!IsSetupFor(dest, version))
            {
                try { File.Delete(dest); } catch { }
                throw new IOException($"the download isn't the Setup for version {version}");
            }
            // older downloads aren't needed any more
            foreach (var old in Directory.GetFiles(Folder, "L4D2-8P-Setup-*.exe").Where(f => !string.Equals(f, dest, StringComparison.OrdinalIgnoreCase)))
                try { File.Delete(old); } catch { }
            return dest;
        }

        /// <summary>
        /// Starts the downloaded Setup silently. It waits for this app (process id) to exit, updates the install with
        /// its folder, shortcuts and start side, and with restart opens the app again afterwards.
        /// </summary>
        public static void RunSetup(string setup, int pid, string side, bool restart)
        {
            Directory.CreateDirectory(Folder);
            var args = $"--silent --update --wait {pid} --path {side ?? StartMode.Host}" + (restart ? " --restart" : "") + $" --out \"{LastLog}\"";
            Process.Start(new ProcessStartInfo(setup, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Folder });
        }

        /// <summary>Downloaded Setups for this version or older: done with, once the app is running the new version.</summary>
        public static void CleanUp()
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var f in Directory.GetFiles(Folder, "L4D2-8P-Setup-*.exe"))
                {
                    var v = Path.GetFileNameWithoutExtension(f).Substring("L4D2-8P-Setup-".Length);
                    if (!UpdateCheck.IsNewer(v, AppInfo.Version)) try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// When the last update didn't go through (its Setup log has an error, and this is still the older version):
        /// the error, once. Null otherwise.
        /// </summary>
        public static string LastFailure()
        {
            try
            {
                if (!File.Exists(LastLog)) return null;
                var err = File.ReadAllLines(LastLog).FirstOrDefault(l => l.StartsWith("ERROR "));
                if (err == null) return null;
                if (File.Exists(LastLog + ".seen")) File.Delete(LastLog + ".seen");
                File.Move(LastLog, LastLog + ".seen");   // said once
                return err.Substring("ERROR ".Length);
            }
            catch { return null; }
        }
    }
}
