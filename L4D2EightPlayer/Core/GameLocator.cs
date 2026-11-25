using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace L4D2EightPlayer.Core
{
    /// <summary>Finds Steam, its library folders, and the Left 4 Dead 2 install.</summary>
    public static class GameLocator
    {
        public const int GameAppId = 550;
        public const int ServerAppId = 222860;

        public static string FindSteam()
        {
            var candidates = new List<string>
            {
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
            };
            foreach (var c in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var p = Path.GetFullPath(c.Replace('/', '\\'));
                if (File.Exists(Path.Combine(p, "steam.exe"))) return p;
            }
            return null;
        }

        public static List<string> LibraryFolders(string steam)
        {
            var libs = new List<string>();
            if (steam == null) return libs;
            libs.Add(steam);

            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return libs;
            try
            {
                var root = Vdf.ParseFile(vdf);
                var folders = root.Block("libraryfolders") ?? root.Block("LibraryFolders");
                if (folders != null)
                {
                    foreach (var kv in folders)
                    {
                        // New format: "0" { "path" "D:\\SteamLibrary" ... }; old format: "1" "D:\\SteamLibrary"
                        string path = kv.Value is Dictionary<string, object> d ? d.Value("path") : kv.Value as string;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) libs.Add(Path.GetFullPath(path));
                    }
                }
            }
            catch { /* unreadable libraryfolders.vdf: fall back to the main library only */ }

            return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Returns the Left 4 Dead 2 folder (the one holding left4dead2.exe), or null.</summary>
        public static string FindGame(string steam = null)
        {
            steam = steam ?? FindSteam();
            foreach (var lib in LibraryFolders(steam))
            {
                var dir = AppDir(lib, GameAppId, "Left 4 Dead 2");
                if (dir != null && IsGameDir(dir)) return dir;
            }

            // Last resort: common hand-made locations.
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                foreach (var rel in new[] { @"SteamLibrary\steamapps\common\Left 4 Dead 2", @"Steam\steamapps\common\Left 4 Dead 2", @"Games\Left 4 Dead 2" })
                {
                    var p = Path.Combine(drive.RootDirectory.FullName, rel);
                    if (IsGameDir(p)) return p;
                }
            }
            return null;
        }

        /// <summary>A dedicated server installed through the Steam client's Tools list, if any.</summary>
        public static string FindSteamToolsServer(string steam = null)
        {
            steam = steam ?? FindSteam();
            foreach (var lib in LibraryFolders(steam))
            {
                var dir = AppDir(lib, ServerAppId, "Left 4 Dead 2 Dedicated Server");
                if (dir != null && IsServerDir(dir)) return dir;
            }
            return null;
        }

        static string AppDir(string library, int appId, string defaultName)
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            string installDir = defaultName;
            if (File.Exists(manifest))
            {
                try { installDir = Vdf.ParseFile(manifest).Block("AppState").Value("installdir") ?? defaultName; }
                catch { }
            }
            var dir = Path.Combine(library, "steamapps", "common", installDir);
            return Directory.Exists(dir) ? dir : null;
        }

        public static bool IsGameDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "left4dead2.exe")) && Directory.Exists(Path.Combine(dir, "left4dead2"));

        public static bool IsServerDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "srcds.exe")) && Directory.Exists(Path.Combine(dir, "left4dead2"));

        public enum ServerSync { Same, ServerBehind, GameBehind, Unknown }

        /// <summary>
        /// Whether the dedicated server runs the same game code as your game. Valve ships the same server.dll and
        /// engine.dll in both, so once both are current they're identical byte for byte. A patch can leave PatchVersion
        /// as it was (November 2026's did), so the files themselves are compared; the side with the newer files is the
        /// current one. Unknown when either is missing.
        /// </summary>
        public static ServerSync CompareServer(string gamePath, string serverDir)
        {
            try
            {
                if (!IsGameDir(gamePath) || !IsServerDir(serverDir)) return ServerSync.Unknown;
                bool differ = false;
                DateTime game = DateTime.MinValue, server = DateTime.MinValue;
                foreach (var rel in new[] { @"left4dead2\bin\server.dll", @"bin\engine.dll" })
                {
                    var g = new FileInfo(Path.Combine(gamePath, rel));
                    var s = new FileInfo(Path.Combine(serverDir, rel));
                    if (!g.Exists || !s.Exists) return ServerSync.Unknown;
                    if (g.LastWriteTimeUtc > game) game = g.LastWriteTimeUtc;
                    if (s.LastWriteTimeUtc > server) server = s.LastWriteTimeUtc;
                    if (!differ && (g.Length != s.Length || Hash.File(g.FullName) != Hash.File(s.FullName))) differ = true;
                }
                if (!differ) return ServerSync.Same;
                return game >= server ? ServerSync.ServerBehind : ServerSync.GameBehind;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return ServerSync.Unknown; }
        }

        /// <summary>Reads PatchVersion from left4dead2\steam.inf (for example "2.2.4.3").</summary>
        public static string PatchVersion(string installDir)
        {
            try
            {
                var inf = Path.Combine(installDir, "left4dead2", "steam.inf");
                if (!File.Exists(inf)) return null;
                foreach (var line in File.ReadAllLines(inf))
                    if (line.StartsWith("PatchVersion=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring("PatchVersion=".Length).Trim();
            }
            catch { }
            return null;
        }
    }
}
