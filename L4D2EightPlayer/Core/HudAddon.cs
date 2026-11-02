using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The 8-Player HUD client add-on (HudAddon\build_addon.py): one VPK in the player's own
    /// left4dead2\addons folder that lets the HUD plugin draw health bars and item icons. Every player
    /// needs it when the host turns the add-on look on; the Manager and the Join app both install it.
    /// </summary>
    public static class HudAddon
    {
        public const string FileName = "l4d2_8p_hud.vpk";
        const string Resource = "addon/l4d2_8p_hud.vpk";

        public enum Status { NoGame, NotInstalled, Installed, Outdated, Disabled }

        /// <summary>gamePath is the folder holding left4dead2.exe.</summary>
        public static string FilePath(string gamePath) => Path.Combine(gamePath, "left4dead2", "addons", FileName);

        static byte[] Embedded()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (s == null) throw new InvalidOperationException("The HUD add-on is missing from this build.");
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string Sha(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public static Status Check(string gamePath)
        {
            if (!GameLocator.IsGameDir(gamePath)) return Status.NoGame;
            var file = FilePath(gamePath);
            if (!File.Exists(file)) return Status.NotInstalled;
            if (Compat.ReadAddonList(Path.Combine(gamePath, "left4dead2")).TryGetValue(FileName, out var on) && !on) return Status.Disabled;
            return Hash.File(file) == Sha(Embedded()) ? Status.Installed : Status.Outdated;
        }

        public static string Describe(Status s)
        {
            switch (s)
            {
                case Status.Installed: return "HUD add-on installed";
                case Status.Outdated: return "HUD add-on out of date";
                case Status.Disabled: return "HUD add-on turned off in the game's Add-ons menu";
                case Status.NotInstalled: return "HUD add-on not installed";
                default: return "L4D2 not found";
            }
        }

        /// <summary>Copies the add-on in (or updates it) and makes sure the game has it switched on.</summary>
        public static void Install(string gamePath)
        {
            if (!GameLocator.IsGameDir(gamePath)) throw new InvalidOperationException("Left 4 Dead 2 wasn't found.");
            if (Launcher.GameRunning()) throw new InvalidOperationException("Close Left 4 Dead 2 first: the game only picks up add-ons when it starts.");
            var file = FilePath(gamePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, Embedded());
            SetListed(Path.Combine(gamePath, "left4dead2"), true);
        }

        public static void Remove(string gamePath)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("Close Left 4 Dead 2 first.");
            var file = FilePath(gamePath);
            if (File.Exists(file)) File.Delete(file);
            SetListed(Path.Combine(gamePath, "left4dead2"), false);
        }

        /// <summary>Adds (enabled) or drops the add-on's line in left4dead2\addonlist.txt, leaving every other line alone.</summary>
        static void SetListed(string gameDir, bool listed)
        {
            var path = Path.Combine(gameDir, "addonlist.txt");
            var line = new Regex("^\\s*\"" + Regex.Escape(FileName) + "\"\\s+\"\\d\"\\s*$", RegexOptions.IgnoreCase);
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new[] { "\"AddonList\"", "{", "}" }.ToList();
            lines.RemoveAll(l => line.IsMatch(l));
            if (listed)
            {
                int close = lines.FindLastIndex(l => l.Trim() == "}");
                if (close < 0) { lines.Add("\"AddonList\""); lines.Add("{"); close = lines.Count; lines.Add("}"); }
                lines.Insert(close, "\t\"" + FileName + "\"\t\t\"1\"");
            }
            else if (!File.Exists(path)) return;
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }
}
