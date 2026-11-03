using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The Workshop add-ons a game uses, so joining players can get the ones they're missing. The host's app makes the
    /// list from the add-ons on in the host's game: required ones (on the server: campaigns, game modes, game-altering
    /// scripts and weapon stats, dialogue; and animation mods) and cosmetic ones (the host's own skins, sounds, UI).
    /// The server publishes it (l4d2_8p_mods); the Join side reads it with the server's rules, shows it, and downloads
    /// what's missing straight from Steam's public file servers into addons\8p_&lt;id&gt;.vpk (no sign-in).
    /// </summary>
    public static class ServerMods
    {
        public sealed class Item
        {
            public string Id;
            public bool Required;
            /// <summary>Why it's on the list, from the host's side: "campaign", "changes how the game plays"... (empty on the Join side).</summary>
            public string Why;
            /// <summary>From Steam (Join side): title, size in bytes, the public download address (null if Steam has none).</summary>
            public string Title, Url;
            public long Size;
            public bool Have;
        }

        const int Parts = 8, PartLength = 200;
        public const string Prefix = "8p_";

        // ================================================================== host: what this game uses

        /// <summary>The Workshop add-ons on in the host's game (and ones the app downloaded), required or cosmetic.</summary>
        public static List<Item> Collect(string clientGameDir, Manifest manifest)
        {
            var items = new List<Item>();
            if (clientGameDir == null || !Directory.Exists(clientGameDir)) return items;
            var list = Compat.ReadAddonList(clientGameDir);
            var addons = Path.Combine(clientGameDir, "addons");
            var vpks = new List<string>();
            if (Directory.Exists(addons)) vpks.AddRange(Directory.GetFiles(addons, Prefix + "*.vpk"));
            var ws = Path.Combine(addons, "workshop");
            if (Directory.Exists(ws)) vpks.AddRange(Directory.GetFiles(ws, "*.vpk"));
            foreach (var vpk in vpks)
            {
                var id = WorkshopId(vpk);
                if (id == null || items.Any(i => i.Id == id) || Compat.IsProtected(vpk, manifest)) continue;
                var key = Compat.AddonListKey(clientGameDir, vpk);
                if (list.TryGetValue(key, out var on) && !on) continue;   // no entry: the game turns it on
                var info = Compat.ReadAddon(vpk);
                if (info.Error != null) continue;
                // required: what's on the server (it changes the game everyone plays), and animation mods; a skin that
                // brings its own animations (a Shrek Coach) is still a skin
                bool anim = !info.IsSkin && info.Files.Any(IsAnimation);
                bool required = info.NeedsServer || anim;
                string why = !required ? (info.IsSkin ? "skins" : info.Sounds ? "sounds" : info.Ui ? "menus and HUD" : "looks")
                    : info.Campaign ? "campaign" : info.Mutation ? "game mode" : info.Gameplay ? "changes how the game plays"
                    : info.Talker ? "dialogue" : "animations";
                items.Add(new Item { Id = id, Required = required, Why = why });
            }
            return items.OrderByDescending(i => i.Required).ToList();
        }

        /// <summary>A Workshop item number from workshop\123.vpk or the app's own addons\8p_123.vpk; null otherwise.</summary>
        public static string WorkshopId(string vpk)
        {
            var name = Path.GetFileNameWithoutExtension(vpk);
            bool workshop = string.Equals(Path.GetFileName(Path.GetDirectoryName(vpk)), "workshop", StringComparison.OrdinalIgnoreCase);
            var m = Regex.Match(name, workshop ? @"^(\d{3,20})$" : "^" + Prefix + @"(\d{3,20})$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Animation files: a model's own sequences (anim_*.mdl, the kind animation mods replace).</summary>
        static bool IsAnimation(string file) => file.StartsWith("models/") && Path.GetFileName(file).StartsWith("anim_", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".mdl");

        /// <summary>The cfg the plugin reads: the list in parts short enough for a config line.</summary>
        public static List<string> CvarLines(IEnumerable<Item> items)
        {
            var parts = new List<string> { "" };
            foreach (var t in items.Select(i => (i.Required ? "r" : "c") + i.Id))
            {
                if (parts[parts.Count - 1].Length + t.Length + 1 > PartLength)
                {
                    if (parts.Count == Parts) break;   // the rest doesn't fit; the required ones come first
                    parts.Add("");
                }
                parts[parts.Count - 1] += (parts[parts.Count - 1].Length > 0 ? " " : "") + t;
            }
            return Enumerable.Range(0, Parts).Select(n => $"l4d2_8p_mods_{n + 1} \"{(n < parts.Count ? parts[n] : "")}\"").ToList();
        }

        // ================================================================== joining: what the server says, what you have

        /// <summary>The list from a server's rules (empty when the server doesn't publish one).</summary>
        public static List<Item> FromRules(Dictionary<string, string> rules)
        {
            var items = new List<Item>();
            if (rules == null) return items;
            for (int n = 1; n <= Parts; n++)
                if (rules.TryGetValue("l4d2_8p_mods_" + n, out var part))
                    foreach (Match m in Regex.Matches(part ?? "", @"\b([rc])(\d{3,20})\b"))
                        if (!items.Any(i => i.Id == m.Groups[2].Value))
                            items.Add(new Item { Id = m.Groups[2].Value, Required = m.Groups[1].Value == "r" });
            return items;
        }

        /// <summary>Whether this game has it: subscribed (addons\workshop) or downloaded by the app (addons\8p_).</summary>
        public static bool Have(string gameDir, string id) =>
            gameDir != null && (File.Exists(Path.Combine(gameDir, "addons", "workshop", id + ".vpk")) || File.Exists(Path.Combine(gameDir, "addons", Prefix + id + ".vpk")));

        /// <summary>Titles, sizes and download addresses from Steam, and what this game already has.</summary>
        public static async Task Describe(List<Item> items, string gameDir, CancellationToken ct = default)
        {
            foreach (var i in items) i.Have = Have(gameDir, i.Id);
            if (items.Count == 0) return;
            foreach (var d in await Workshop.ItemDetails(items.Select(i => i.Id).ToList(), ct).ConfigureAwait(false))
            {
                var item = items.FirstOrDefault(i => i.Id == d.id);
                if (item == null) continue;
                item.Title = d.title;
                item.Size = d.size;
                item.Url = d.url;
            }
        }

        /// <summary>
        /// Downloads one into addons\8p_&lt;id&gt;.vpk (through a temporary file, so a cut-off download never leaves a broken
        /// add-on). The game loads it the next time it starts.
        /// </summary>
        public static async Task Download(string gameDir, Item item, ILog log, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(item.Url)) throw new InvalidOperationException($"{item.Title ?? item.Id} can't be downloaded directly: subscribe to it on the Workshop.");
            var dest = Path.Combine(gameDir, "addons", Prefix + item.Id + ".vpk");
            var tmp = dest + ".download";
            await Http.Download(item.Url, tmp, log, ct).ConfigureAwait(false);
            using (var f = File.OpenRead(tmp))
            {
                var head = new byte[4];
                if (f.Read(head, 0, 4) < 4 || BitConverter.ToUInt32(head, 0) != 0x55AA1234)
                {
                    f.Dispose();
                    File.Delete(tmp);
                    throw new InvalidDataException($"{item.Title ?? item.Id}: Steam sent something that isn't an add-on.");
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            item.Have = true;
        }

        /// <summary>An add-on the app downloaded that's now subscribed too: the app's copy goes (two copies would clash).</summary>
        public static List<string> Tidy(string gameDir)
        {
            var removed = new List<string>();
            var addons = gameDir == null ? null : Path.Combine(gameDir, "addons");
            if (addons == null || !Directory.Exists(addons)) return removed;
            foreach (var vpk in Directory.GetFiles(addons, Prefix + "*.vpk"))
            {
                var id = WorkshopId(vpk);
                if (id == null || !File.Exists(Path.Combine(addons, "workshop", id + ".vpk"))) continue;
                try { File.Delete(vpk); removed.Add(id); } catch (IOException) { }
            }
            return removed;
        }
    }
}
