using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>Installs and updates the dedicated server with SteamCMD.</summary>
    public static class SteamCmd
    {
        const string ZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        static readonly Regex ProgressLine = new Regex(@"progress:\s*([\d.]+)", RegexOptions.Compiled);

        public static async Task UpdateServer(string root, string installDir, ILog log, CancellationToken ct)
        {
            var dir = Path.Combine(root, "steamcmd");
            var exe = Path.Combine(dir, "steamcmd.exe");
            if (!File.Exists(exe))
            {
                log.Info("Downloading SteamCMD from Valve...");
                var zip = Path.Combine(Paths.Cache, "downloads", "steamcmd.zip");
                await Http.Download(ZipUrl, zip, log, ct);
                Directory.CreateDirectory(dir);
                ZipFile.ExtractToDirectory(zip, dir);
            }

            Directory.CreateDirectory(installDir);
            // SteamCMD updates itself on first run and sometimes exits early; retry a few times.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                log.Info($"SteamCMD: installing/updating the L4D2 dedicated server (app {GameLocator.ServerAppId}), attempt {attempt}. The first install is roughly 10 GB.");
                var (code, success) = await Run(exe, $"+force_install_dir \"{installDir}\" +login anonymous +app_update {GameLocator.ServerAppId} validate +quit", log, ct);
                // SteamCMD on Windows often exits with 7 even after "Success! App ... fully installed".
                if (GameLocator.IsServerDir(installDir) && (code == 0 || success))
                {
                    log.Info("Dedicated server is up to date (" + (GameLocator.PatchVersion(installDir) ?? "version unknown") + ").");
                    return;
                }
                log.Warn($"SteamCMD exited with code {code}.");
            }
            throw new InvalidOperationException("SteamCMD could not install the dedicated server. Check your disk space and internet connection, then try again.");
        }

        static Task<(int code, bool success)> Run(string exe, string args, ILog log, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<(int, bool)>();
            bool success = false;
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                },
                EnableRaisingEvents = true,
            };
            DataReceivedEventHandler onLine = (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.Contains("fully installed")) success = true;
                var m = ProgressLine.Match(e.Data);
                if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    log.Progress("SteamCMD", pct / 100.0);
                else
                    log.Info("   " + e.Data.Trim());
            };
            p.OutputDataReceived += onLine;
            p.ErrorDataReceived += onLine;
            p.Exited += (s, e) => { p.WaitForExit(); tcs.TrySetResult((p.ExitCode, success)); p.Dispose(); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } tcs.TrySetCanceled(); });
            return tcs.Task;
        }
    }

    /// <summary>Copies (hard-links where possible) the client's server-side addons to the dedicated server.</summary>
    public static class AddonSync
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

        public static void Sync(string clientGameDir, string serverGameDir, InstallState state, ILog log)
        {
            var list = Compat.ReadAddonList(clientGameDir);
            var addons = Path.Combine(clientGameDir, "addons");
            var candidates = new List<string>();
            if (Directory.Exists(addons)) candidates.AddRange(Directory.GetFiles(addons, "*.vpk"));
            if (Directory.Exists(Path.Combine(addons, "workshop"))) candidates.AddRange(Directory.GetFiles(Path.Combine(addons, "workshop"), "*.vpk"));

            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // server rel -> client path
            foreach (var vpk in candidates)
            {
                var key = Compat.AddonListKey(clientGameDir, vpk);
                if (list.TryGetValue(key, out var on) && !on) continue;
                var info = Compat.ReadAddon(vpk);
                if (info.Error != null || !info.NeedsServer) continue;
                wanted["addons/" + Path.GetFileName(vpk)] = vpk;
            }

            // Remove addons we synced earlier that are no longer wanted.
            foreach (var old in state.synced.ToList())
            {
                if (wanted.ContainsKey(old.path)) continue;
                var full = Paths.Join(serverGameDir, old.path);
                try { if (File.Exists(full)) File.Delete(full); } catch (IOException) { continue; }
                state.synced.Remove(old);
                log.Info("Removed from server: " + Path.GetFileName(full));
            }

            int linked = 0, copied = 0;
            foreach (var kv in wanted)
            {
                var dest = Paths.Join(serverGameDir, kv.Key);
                var src = new FileInfo(kv.Value);
                bool tracked = state.synced.Any(s => s.path.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (File.Exists(dest))
                {
                    if (!tracked) continue; // the user put their own copy there; leave it alone
                    var d = new FileInfo(dest);
                    if (d.Length == src.Length && d.LastWriteTimeUtc == src.LastWriteTimeUtc) continue;
                    File.Delete(dest);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (SameVolume(src.FullName, dest) && CreateHardLink(dest, src.FullName, IntPtr.Zero)) linked++;
                else { File.Copy(src.FullName, dest, true); File.SetLastWriteTimeUtc(dest, src.LastWriteTimeUtc); copied++; }
                if (!tracked) state.synced.Add(new SyncedAddon { path = kv.Key, source = kv.Value });
            }
            state.Save(serverGameDir);
            log.Info($"Server addons in sync: {wanted.Count} server-side addon(s)" + (linked + copied > 0 ? $" ({linked} linked, {copied} copied)" : "") + ".");
        }

        static bool SameVolume(string a, string b) =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts the dedicated server and the game client.</summary>
    public static class Launcher
    {
        public static Process StartServer(Settings s)
        {
            var dir = s.ServerInstallDir;
            var exe = Path.Combine(dir, "srcds.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("srcds.exe was not found. Install the dedicated server on the Setup tab first.", exe);
            var args = ServerArgs(s);
            return Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = dir, UseShellExecute = true });
        }


        // No +ip: srcds ignores packets from 127.0.0.1, so a loopback-bound server is unreachable even from
        // this PC. "Just me" stays off the internet through sv_lan 1 and an unforwarded port instead.
        public static string ServerArgs(Settings s)
        {
            // The password is set once here rather than in server.cfg (see ServerConfig.Apply).
            var pw = string.IsNullOrEmpty(s.Password) ? "" : $" +sv_password \"{s.Password.Replace("\"", "'")}\"";
            return $"-console -game left4dead2 -port {s.Port} -condebug +sv_setmax 31 {LanArg(s)}{pw} +mp_gamemode {s.GameMode} +map {s.Map}";
        }

        /// <summary>
        /// sv_lan has to be right before the first map: the server connects to Steam then, and if server.cfg
        /// changes sv_lan afterwards it reconnects at the next map, leaving the game's end-of-map Steam stats
        /// upload with a dead Steam interface. That crashes the server on the following map change.
        /// </summary>
        public static string LanArg(Settings s) => "+sv_lan " + (s.Internet ? 0 : 1);

        /// <summary>Where this PC reaches its own server. Not 127.0.0.1: srcds drops loopback traffic.</summary>
        public static string LocalServerAddress() => Network.LanAddress();

        /// <summary>
        /// Waits until a freshly started server has its map up: it answers a query, or (a passworded server
        /// answers none) its -condebug console log reports the Steam connection. False if it exits or times out.
        /// </summary>
        public static async Task<bool> WaitUntilReady(Settings s, Process server, TimeSpan timeout)
        {
            var log = s.TargetGameDir == null ? null : Path.Combine(s.TargetGameDir, "console.log");
            long from = log != null && File.Exists(log) ? new FileInfo(log).Length : 0;
            var until = DateTime.Now + timeout;
            while (DateTime.Now < until)
            {
                if (server.HasExited) return false;
                if (await ServerQuery.Query(LocalServerAddress(), s.Port, 1500) != null) return true;
                if (LogSince(log, from).Contains("Connection to Steam servers successful")) return true;
                await Task.Delay(1500);
            }
            return false;
        }

        static string LogSince(string path, long from)
        {
            try
            {
                if (path == null || !File.Exists(path)) return "";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < from) from = 0; // log was replaced
                    fs.Seek(from, SeekOrigin.Begin);
                    return new StreamReader(fs).ReadToEnd();
                }
            }
            catch (IOException) { return ""; }
        }

        public static void StopServer(Process p)
        {
            if (p == null || p.HasExited) return;
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(5000)) p.Kill();
            }
            catch (InvalidOperationException) { }
        }

        public static bool GameRunning() => Process.GetProcessesByName("left4dead2").Length > 0;

        public static void LaunchGame(string args)
        {
            var steam = GameLocator.FindSteam();
            if (steam == null) throw new InvalidOperationException("Steam was not found. Start Steam and try again.");
            Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), $"-applaunch {GameLocator.GameAppId} {args}".Trim()) { UseShellExecute = true });
        }

        /// <summary>
        /// Starts left4dead2.exe itself, the way Steam does (-steam plus Steam's app id environment). Needed while
        /// the dedicated server runs: srcds registers with the Steam client as app 550, so "steam -applaunch 550"
        /// is treated as "already running" and nothing starts. Carries over the user's Steam launch options;
        /// returns true if -insecure was left out (an -insecure game can't join a VAC-secured server).
        /// </summary>
        public static bool LaunchGameDirect(string gamePath, string args, bool dropInsecure)
        {
            var exe = Path.Combine(gamePath, "left4dead2.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("left4dead2.exe was not found in the game folder.", exe);
            var opts = SteamLaunchOptions(GameLocator.GameAppId) ?? "";
            bool dropped = false;
            if (dropInsecure && Regex.IsMatch(opts, @"(^|\s)-insecure(\s|$)"))
            {
                opts = Regex.Replace(opts, @"(^|\s)-insecure(?=\s|$)", " ").Trim();
                dropped = true;
            }
            var psi = new ProcessStartInfo(exe, $"-steam {opts} {args}".Replace("  ", " ").Trim()) { WorkingDirectory = gamePath, UseShellExecute = false };
            psi.EnvironmentVariables["SteamAppId"] = GameLocator.GameAppId.ToString();
            psi.EnvironmentVariables["SteamGameId"] = GameLocator.GameAppId.ToString();
            Process.Start(psi);
            return dropped;
        }

        /// <summary>The launch options set in Steam (game Properties) for this app, from the newest localconfig.vdf.</summary>
        public static string SteamLaunchOptions(int appId)
        {
            try
            {
                var steam = GameLocator.FindSteam();
                var userdata = steam == null ? null : Path.Combine(steam, "userdata");
                if (userdata == null || !Directory.Exists(userdata)) return null;
                var cfg = Directory.GetDirectories(userdata)
                    .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (cfg == null) return null;
                var steamBlock = Vdf.ParseFile(cfg).Block("UserLocalConfigStore").Block("Software").Block("Valve").Block("Steam");
                var opts = (steamBlock.Block("apps") ?? steamBlock.Block("Apps")).Block(appId.ToString()).Value("LaunchOptions");
                if (string.IsNullOrWhiteSpace(opts)) return null;
                // "WRAPPER %command% -opts": only what follows %command% are game arguments.
                int i = opts.IndexOf("%command%", StringComparison.OrdinalIgnoreCase);
                return (i >= 0 ? opts.Substring(i + "%command%".Length) : opts).Trim();
            }
            catch { return null; }
        }

        public static string LanAddress() => Network.LanAddress();

        public static async Task<string> PublicAddress()
        {
            var ip = (await Http.GetString("https://api.ipify.org")).Trim();
            return Regex.IsMatch(ip, @"^\d{1,3}(\.\d{1,3}){3}$") ? ip : null;
        }
    }
}
