using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --mods-test &lt;report&gt;: the server mod list (ServerMods) without a server: a long list split over the cvars and read
    /// back, Steam's details for two real Workshop items, and downloading them into a throwaway game folder (needs the
    /// internet). Nothing in the real game folder is touched.
    /// </summary>
    static class ModsTest
    {
        public static int Run(string report)
        {
            var log = new StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            try
            {
                // 1. the list in cvar-sized parts, and back
                var many = Enumerable.Range(0, 60).Select(i => new ServerMods.Item { Id = (3000000000L + i * 7919).ToString(), Required = i < 6 }).ToList();
                var lines = ServerMods.CvarLines(many);
                var rules = lines.Select(l => l.Split(new[] { ' ' }, 2)).ToDictionary(p => p[0], p => p[1].Trim('"'), StringComparer.OrdinalIgnoreCase);
                var back = ServerMods.FromRules(rules);
                Check($"60 items in {lines.Count(l => !l.EndsWith("\"\""))} parts, every line under 230 characters", lines.All(l => l.Length < 230));
                Check($"read back: {back.Count} items, the required ones first and still required", back.Count == 60 && back.Take(6).All(i => i.Required) && back.Skip(6).All(i => !i.Required) && back.Select(i => i.Id).SequenceEqual(many.Select(i => i.Id)));
                Check("no list: nothing", ServerMods.FromRules(new Dictionary<string, string> { ["sv_tags"] = "coop" }).Count == 0 && ServerMods.FromRules(null).Count == 0);

                // 2. Steam's details, and what a game folder has
                var game = Path.Combine(Path.GetTempPath(), "l4d2-8p-mods-test", "left4dead2");
                if (Directory.Exists(Path.GetDirectoryName(game))) Directory.Delete(Path.GetDirectoryName(game), true);
                Directory.CreateDirectory(Path.Combine(game, "addons", "workshop"));
                var items = ServerMods.FromRules(new Dictionary<string, string> { ["l4d2_8p_mods_1"] = "r213445426 c3465734946" });
                ServerMods.Describe(items, game).GetAwaiter().GetResult();
                Check("Steam's details: " + string.Join(", ", items.Select(i => $"'{i.Title}' {i.Size / 1024} KB")), items.All(i => i.Title != null && i.Size > 0 && i.Url != null));
                Check("nothing there yet", items.All(i => !i.Have));

                // 3. downloading, then subscribing to one (the app's copy goes)
                foreach (var i in items) ServerMods.Download(game, i, new NullLog(), CancellationToken.None).GetAwaiter().GetResult();
                Check("downloaded as addons\\8p_<id>.vpk at Steam's size, no leftovers", items.All(i => i.Have && new FileInfo(Path.Combine(game, "addons", "8p_" + i.Id + ".vpk")).Length == i.Size)
                    && Directory.GetFiles(Path.Combine(game, "addons"), "*.download").Length == 0);
                var info = Compat.ReadAddon(Path.Combine(game, "addons", "8p_" + items[1].Id + ".vpk"));
                Check($"a downloaded add-on reads like any other: '{info.Title}', {info.Files.Count} files", info.Error == null && info.Files.Count > 0);
                Check("the app's copy counts as having it", ServerMods.Have(game, items[0].Id) && ServerMods.WorkshopId(Path.Combine(game, "addons", "8p_" + items[0].Id + ".vpk")) == items[0].Id);
                File.Copy(Path.Combine(game, "addons", "8p_" + items[0].Id + ".vpk"), Path.Combine(game, "addons", "workshop", items[0].Id + ".vpk"));
                var tidied = ServerMods.Tidy(game);
                Check("subscribed later: the app's copy goes, the subscribed one stays", tidied.SequenceEqual(new[] { items[0].Id }) && !File.Exists(Path.Combine(game, "addons", "8p_" + items[0].Id + ".vpk")) && ServerMods.Have(game, items[0].Id));
                Directory.Delete(Path.GetDirectoryName(game), true);
            }
            catch (Exception ex) { Check("exception: " + ex, false); }
            File.WriteAllText(report, log.ToString());
            return ok ? 0 : 1;
        }
    }
}
