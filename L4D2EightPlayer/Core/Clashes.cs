using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Add-ons that are on together and replace the same files. The game uses one copy of each file, so with both
    /// on you get part of each: a survivor pack showing another pack's HUD portraits, or a HUD mod with half of the
    /// 8-player HUD. Skins for the same thing are the skin picker's question (Skins.Overlaps); this covers the rest
    /// and is asked about before a launch.
    /// </summary>
    public static class Clashes
    {
        public sealed class Addon
        {
            public string Key, Path, Title;
            /// <summary>The HUD add-on, the lobby mod and the other add-ons the app needs: never turned off here.</summary>
            public bool Protected;
            public List<Skins.Target> Targets = new List<Skins.Target>();
            public override string ToString() => Title;
        }

        public sealed class Clash
        {
            public Addon A, B;
            public List<string> Files = new List<string>();
            /// <summary>What the shared files are, in plain words: "survivor portraits on the HUD (12), ...".</summary>
            public string What => Describe(Files);
            internal string Id => string.Join("|", new[] { A.Key, B.Key }.Select(k => k.ToLowerInvariant()).OrderBy(k => k));
            internal string Stamp => Files.Count.ToString();
        }

        /// <summary>The clashes between the add-ons turned on in a game's left4dead2 folder, the biggest first.</summary>
        public static List<Clash> Find(string gameDir, Manifest manifest, SkinPrefs prefs)
        {
            var result = new List<Clash>();
            if (gameDir == null || !Directory.Exists(gameDir)) return result;
            var list = Compat.ReadAddonList(gameDir);
            var owners = new Dictionary<string, List<Addon>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vpk in AddonFiles(gameDir))
            {
                var key = Compat.AddonListKey(gameDir, vpk);
                if (list.TryGetValue(key, out var on) && !on) continue;   // no entry: the game turns it on
                var info = Compat.ReadAddon(vpk);
                if (info.Error != null) continue;
                var a = new Addon
                {
                    Key = key, Path = vpk, Protected = Compat.IsProtected(vpk, manifest), Targets = Skins.Coverage(info.Files),
                    Title = Compat.AddonTitle(vpk, info),
                };
                foreach (var f in info.Files)
                {
                    if (f.StartsWith("addoninfo") || f.StartsWith("addonimage") || Compat.PerAddonScript(f)) continue;
                    if (!owners.TryGetValue(f, out var l)) owners[f] = l = new List<Addon>();
                    l.Add(a);
                }
            }

            var pairs = new Dictionary<(Addon, Addon), List<string>>();
            foreach (var kv in owners.Where(o => o.Value.Count > 1))
            {
                var ow = kv.Value.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < ow.Count; i++)
                    for (int j = i + 1; j < ow.Count; j++)
                    {
                        if (!pairs.TryGetValue((ow[i], ow[j]), out var l)) pairs[(ow[i], ow[j])] = l = new List<string>();
                        l.Add(kv.Key);
                    }
            }
            foreach (var kv in pairs)
            {
                var (a, b) = kv.Key;
                if (a.Protected && b.Protected) continue;
                if (a.Targets.Intersect(b.Targets).Any()) continue;   // skins for the same thing: the skin picker asks
                var c = new Clash { A = a, B = b, Files = kv.Value.OrderBy(f => f).ToList() };
                if (prefs.KeepClash.TryGetValue(c.Id, out var kept) && kept == c.Stamp) continue;
                result.Add(c);
            }
            return result.OrderByDescending(c => c.Files.Count).ToList();
        }

        /// <summary>
        /// The choices from the question before a launch: add-ons to turn off, and clashes to leave as they are (not
        /// asked about again while they share the same files). A skin pick pointing at an add-on turned off here is
        /// dropped, so the next launch doesn't turn it back on.
        /// </summary>
        public static void Resolve(string gameDir, IEnumerable<Addon> turnOff, IEnumerable<Clash> keep, SkinPrefs prefs)
        {
            var off = turnOff.Where(a => !a.Protected).GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();   // one add-on can lose two pairs
            if (off.Count > 0) Compat.SetAddonsEnabled(gameDir, off.ToDictionary(a => a.Key, a => false, StringComparer.OrdinalIgnoreCase));
            foreach (var a in off)
                foreach (var pick in prefs.Picks.Where(p => string.Equals(p.Value, a.Key, StringComparison.OrdinalIgnoreCase)).ToList())
                    prefs.Picks.Remove(pick.Key);
            foreach (var c in keep) prefs.KeepClash[c.Id] = c.Stamp;
            prefs.Save();
        }

        static IEnumerable<string> AddonFiles(string gameDir)
        {
            var addons = System.IO.Path.Combine(gameDir, "addons");
            var vpks = new List<string>();
            if (Directory.Exists(addons)) vpks.AddRange(Directory.GetFiles(addons, "*.vpk"));
            var ws = System.IO.Path.Combine(addons, "workshop");
            if (Directory.Exists(ws)) vpks.AddRange(Directory.GetFiles(ws, "*.vpk"));
            return vpks.OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
        }

        // first match wins, so the specific kinds come before the general ones
        static readonly (string prefix, string what)[] Kinds =
        {
            ("materials/vgui/s_panel_", "survivor portraits on the HUD"),
            ("materials/vgui/select_", "character-select pictures"),
            ("materials/vgui/hud/", "HUD icons"),
            ("scripts/hudlayout.res", "the HUD layout"),
            ("resource/ui/hud/", "the HUD layout"),
            ("resource/ui/", "menus"),
            ("resource/", "game text and menus"),
            ("materials/vgui/", "menu and HUD pictures"),
            ("models/", "models"),
            ("materials/", "textures"),
            ("sound/", "sounds"),
            ("scripts/vscripts/", "scripts"),
            ("scripts/talker/", "dialogue"),
            ("scripts/", "game scripts"),
            ("missions/", "campaign files"),
            ("maps/", "maps"),
            ("particles/", "effects"),
        };

        public static string Describe(IEnumerable<string> files)
        {
            var counts = new List<(string what, int n)>();
            foreach (var f in files)
            {
                var what = Kinds.FirstOrDefault(k => f.StartsWith(k.prefix, StringComparison.OrdinalIgnoreCase)).what ?? "other files";
                int i = counts.FindIndex(c => c.what == what);
                if (i < 0) counts.Add((what, 1)); else counts[i] = (what, counts[i].n + 1);
            }
            return string.Join(", ", counts.OrderByDescending(c => c.n).Select(c => $"{c.what} ({c.n})"));
        }
    }
}
