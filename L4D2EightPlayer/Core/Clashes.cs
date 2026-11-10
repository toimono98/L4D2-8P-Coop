using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Add-ons that replace the same files. The game uses one copy of each file, so with two different copies on
    /// you get part of each: a survivor pack showing another pack's HUD portraits, or a HUD mod with half of the
    /// 8-player HUD. When both hold the same copy (parts of one campaign, the same mod installed twice) nothing
    /// changes whichever wins: that overlap is harmless, even though the game's Add-ons menu marks it as a conflict.
    /// Skins for the same thing are the skin picker's question (Skins.Overlaps); Find covers the rest and is asked
    /// about before a launch, Conflicts is everything Disable conflicting can settle.
    /// </summary>
    public static class Clashes
    {
        public sealed class Addon
        {
            public string Key, Path, Title;
            /// <summary>The HUD add-on, the lobby mod and the other add-ons the app needs: never turned off here.</summary>
            public bool Protected;
            /// <summary>On in the game's add-on list (a missing entry is on).</summary>
            public bool Enabled = true;
            public List<Skins.Target> Targets = new List<Skins.Target>();
            public Compat.AddonInfo Info;
            public bool Workshop => Key != null && Key.StartsWith("workshop\\", StringComparison.OrdinalIgnoreCase);
            /// <summary>A campaign or game mode: the heavy kind to turn off by accident.</summary>
            public bool Big => Info != null && (Info.Campaign || Info.Mutation);
            public override string ToString() => Title;
        }

        public sealed class Clash
        {
            public Addon A, B;
            /// <summary>The shared files that differ: what actually clashes.</summary>
            public List<string> Files = new List<string>();
            /// <summary>Shared files that are the same in both (harmless).</summary>
            public int Same;
            /// <summary>What the shared files are, in plain words: "survivor portraits on the HUD (12), ...".</summary>
            public string What => Describe(Files);
            public bool Harmless => Files.Count == 0;
            public bool BothOn => A.Enabled && B.Enabled;
            public Addon Other(Addon a) => a == A ? B : A;
            internal string Id => string.Join("|", new[] { A.Key, B.Key }.Select(k => k.ToLowerInvariant()).OrderBy(k => k));
            internal string Stamp => Files.Count.ToString();
        }

        /// <summary>Every readable add-on in a game folder (on or off) and every pair of them that shares files.</summary>
        public sealed class Survey
        {
            public List<Addon> Addons = new List<Addon>();
            public List<Clash> Pairs = new List<Clash>();
            public IEnumerable<Clash> Real => Pairs.Where(p => !p.Harmless);
            public IEnumerable<Clash> Harmless => Pairs.Where(p => p.Harmless);
            public Addon ByPath(string path) => Addons.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            /// <summary>The add-ons that really clash with this one and are on (counted for its rating).</summary>
            public List<Addon> ConflictsOf(Addon a) => Real.Where(p => p.A == a || p.B == a).Select(p => p.Other(a)).Where(o => o.Enabled).Distinct().ToList();
        }

        public static Survey Look(string gameDir, Manifest manifest)
        {
            var s = new Survey();
            if (gameDir == null || !Directory.Exists(gameDir)) return s;
            var list = Compat.ReadAddonList(gameDir);
            var owners = new Dictionary<string, List<Addon>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vpk in AddonFiles(gameDir))
            {
                var key = Compat.AddonListKey(gameDir, vpk);
                var info = Compat.ReadAddon(vpk);
                var a = new Addon
                {
                    Key = key, Path = vpk, Protected = Compat.IsProtected(vpk, manifest), Info = info,
                    Enabled = !list.TryGetValue(key, out var on) || on,   // no entry: the game turns it on
                    Title = Compat.AddonTitle(vpk, info),
                    Targets = info.Error == null ? Skins.Coverage(info.Files) : new List<Skins.Target>(),
                };
                s.Addons.Add(a);
                if (info.Error != null) continue;
                foreach (var f in info.Files)
                {
                    if (Compat.NeverClashes(f)) continue;
                    if (!owners.TryGetValue(f, out var l)) owners[f] = l = new List<Addon>();
                    l.Add(a);
                }
            }

            var pairs = new Dictionary<(Addon, Addon), Clash>();
            foreach (var kv in owners.Where(o => o.Value.Count > 1))
            {
                var ow = kv.Value.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Key, StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < ow.Count; i++)
                    for (int j = i + 1; j < ow.Count; j++)
                    {
                        if (!pairs.TryGetValue((ow[i], ow[j]), out var c)) pairs[(ow[i], ow[j])] = c = new Clash { A = ow[i], B = ow[j] };
                        if (Compat.SameFile(ow[i].Path, ow[i].Info, ow[j].Path, ow[j].Info, kv.Key)) c.Same++;
                        else c.Files.Add(kv.Key);
                    }
            }
            foreach (var c in pairs.Values.Where(c => !(c.A.Protected && c.B.Protected)))
            {
                c.Files.Sort(StringComparer.Ordinal);
                s.Pairs.Add(c);
            }
            s.Pairs = s.Pairs.OrderByDescending(c => c.Files.Count).ThenByDescending(c => c.Same).ToList();
            return s;
        }

        /// <summary>
        /// Before a launch: the clashes between add-ons that are on, the biggest first. Harmless overlaps, skins for
        /// the same thing (the skin picker asks) and pairs the user chose to keep aren't listed.
        /// </summary>
        public static List<Clash> Find(string gameDir, Manifest manifest, SkinPrefs prefs) =>
            Look(gameDir, manifest).Real
                .Where(c => c.BothOn && !c.A.Targets.Intersect(c.B.Targets).Any() && !Kept(c, prefs))
                .ToList();

        /// <summary>Everything Disable conflicting settles: every real clash between add-ons that are on, skins included.</summary>
        public static List<Clash> Conflicts(Survey s, SkinPrefs prefs) => s.Real.Where(c => c.BothOn && !Kept(c, prefs)).ToList();

        static bool Kept(Clash c, SkinPrefs prefs) => prefs != null && prefs.KeepClash.TryGetValue(c.Id, out var kept) && kept == c.Stamp;

        /// <summary>
        /// Which of a pair to turn off, as a suggestion: never one the app needs; else keep the skin you picked, a
        /// campaign or game mode, the subscribed Workshop copy over a loose file, then the add-on with more in it.
        /// </summary>
        public static Addon Suggest(Clash c, SkinPrefs prefs)
        {
            if (c.A.Protected) return c.B;
            if (c.B.Protected) return c.A;
            int Score(Addon a) =>
                (prefs != null && prefs.Picks.Values.Any(v => string.Equals(v, a.Key, StringComparison.OrdinalIgnoreCase)) ? 8 : 0)
                + (a.Big ? 4 : 0) + (a.Workshop ? 2 : 0);
            int sa = Score(c.A), sb = Score(c.B);
            if (sa != sb) return sa > sb ? c.B : c.A;
            int fa = c.A.Info?.Files.Count ?? 0, fb = c.B.Info?.Files.Count ?? 0;
            return fa >= fb ? c.B : c.A;
        }

        /// <summary>
        /// The choices made about clashes: add-ons to turn off, and clashes to leave as they are (not asked about again
        /// while they share the same files). A skin pick pointing at an add-on turned off here is dropped, so the next
        /// launch doesn't turn it back on.
        /// </summary>
        public static void Resolve(string gameDir, IEnumerable<Addon> turnOff, IEnumerable<Clash> keep, SkinPrefs prefs)
        {
            var off = turnOff.Where(a => !a.Protected).GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();   // one add-on can lose two pairs
            if (off.Count > 0) Compat.SetAddonsEnabled(gameDir, off.ToDictionary(a => a.Key, a => false, StringComparer.OrdinalIgnoreCase));
            foreach (var a in off)
            {
                a.Enabled = false;
                foreach (var pick in prefs.Picks.Where(p => string.Equals(p.Value, a.Key, StringComparison.OrdinalIgnoreCase)).ToList())
                    prefs.Picks.Remove(pick.Key);
            }
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
