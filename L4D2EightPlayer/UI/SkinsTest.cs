using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --skins-test &lt;fixtures&gt; &lt;report&gt;: the skin picker's logic on the fake game folder made by
    /// tools\make_skin_fixtures.py (copied first, so the fixtures stay as built). Checks what's recognised as a
    /// skin, the changes each pick makes, that protected and campaign add-ons are never turned off, and the
    /// re-apply at launch (restoring picks, a skin that's gone, a new skin). Nothing outside the copy is written.
    /// </summary>
    static class SkinsTest
    {
        public static int Run(string fixtures, string outFile)
        {
            var log = new StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }

            // a working copy, and the real files whose contents must not change
            var work = Path.Combine(Path.GetTempPath(), "l4d2-8p-skins-test");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Copy(Path.Combine(fixtures, "left4dead2"), Path.Combine(work, "left4dead2"));
            var game = Path.Combine(work, "left4dead2");
            SkinPrefs.FilePath = Path.Combine(work, "skins.json");
            var realGame = GameLocator.FindGame();
            var realList = realGame == null ? null : Path.Combine(realGame, "left4dead2", "addonlist.txt");
            var realSkins = Path.Combine(Paths.AppData, "skins.json");
            string before = Hash(realList), beforeSkins = Hash(realSkins);
            var manifest = Manifest.BuiltIn();

            // 1. what's a skin, and for what
            var inv = Skins.Scan(game, manifest);
            Skins.Mod M(string title) => inv.Mods.FirstOrDefault(m => m.Title == title);
            string Names(IEnumerable<Skins.Mod> mods) => string.Join(", ", mods.Select(m => m.Title).OrderBy(t => t));
            string Ids(IEnumerable<Skins.Target> ts) => string.Join(",", ts.Select(t => t.Id).OrderBy(t => t));
            Check("skins found: " + Names(inv.Mods), Names(inv.Mods) == "Campaign With AK, Commons Skin, Hunter Skin, Nick Model Skin, Nick Texture Skin, Rifle Pack, Survivors and AK Pack");
            Check("protected add-ons left out (HUD, lobby mod)", inv.Mods.All(m => !m.Path.EndsWith("l4d2_8p_hud.vpk") && !m.Path.EndsWith("2754956355.vpk")));
            Check($"script, sound, icon and HUD mods aren't skins (other add-ons on: {inv.Others})", inv.Others == 4);
            Check("Nick: model skin, texture skin, pack", Names(inv.ByTarget["nick"].Mods) == "Nick Model Skin, Nick Texture Skin, Survivors and AK Pack");
            Check("the pack covers Nick, Rochelle, Coach, Ellis and the AK-47: " + Ids(M("Survivors and AK Pack").Targets), Ids(M("Survivors and AK Pack").Targets) == "ak47,coach,ellis,nick,rochelle");
            Check("a texture-only skin is recognised (Hunter, commons)", Ids(M("Hunter Skin").Targets) == "hunter" && Ids(M("Commons Skin").Targets) == "common");
            Check("M16: only the rifle pack (the lobby mod's copy is protected)", Names(inv.ByTarget["m16"].Mods) == "Rifle Pack");
            Check("the campaign is locked, the rifle pack notes its weapon script", M("Campaign With AK").Locked && M("Rifle Pack").WeaponScripts && !M("Rifle Pack").Locked);
            Check("enabled: missing entry = on, 0 = off, doubled backslash read", M("Hunter Skin").Enabled && !M("Nick Texture Skin").Enabled && M("Survivors and AK Pack").Enabled);
            Check("Nick and the AK-47 are conflicts (two skins on)", inv.ByTarget["nick"].Conflict && inv.ByTarget["ak47"].Conflict);

            // 2. the change sets
            var cs = Skins.Plan(inv, Skins.Get("nick"), M("Nick Texture Skin"));
            Check("Nick -> texture skin: turns it on, turns the model skin and the pack off", Names(cs.TurnOn) == "Nick Texture Skin" && Names(cs.TurnOff) == "Nick Model Skin, Survivors and AK Pack");
            Check("... and Rochelle, Coach, Ellis go back to default (the AK-47 keeps other skins): " + Ids(cs.BackToDefault), Ids(cs.BackToDefault) == "coach,ellis,rochelle" && cs.Wider);
            var ak = Skins.Plan(inv, Skins.Get("ak47"), null);
            Check("AK-47 -> default: the packs go off, the campaign stays on", Names(ak.TurnOff) == "Rifle Pack, Survivors and AK Pack" && Names(ak.StaysOn) == "Campaign With AK");
            Check("... M16, Rochelle, Coach, Ellis back to default, Nick keeps its model skin: " + Ids(ak.BackToDefault), Ids(ak.BackToDefault) == "coach,ellis,m16,rochelle");
            var pack = Skins.Plan(inv, Skins.Get("rochelle"), M("Survivors and AK Pack"));
            Check("Rochelle -> the pack (already on): sets all its targets, turns the Nick model skin and the rifle pack off", pack.TurnOn.Count == 0 && Names(pack.TurnOff) == "Nick Model Skin, Rifle Pack" && pack.Sets.Count == 5 && Ids(pack.BackToDefault) == "m16");
            var hunter = Skins.Plan(inv, Skins.Get("hunter"), M("Hunter Skin"));
            Check("Hunter -> its skin (already the only one): nothing to do, nothing wider", hunter.Nothing && !hunter.Wider);

            // 3. overlaps asked about before a launch
            var ov = Skins.Overlaps(game, manifest, SkinPrefs.Load());
            Check("before launch: Nick and the AK-47 have more than one skin on and nothing picked: " + Ids(ov.Select(s => s.Target)), Ids(ov.Select(s => s.Target)) == "ak47,nick");
            Skins.KeepOverlaps(ov, SkinPrefs.Load());
            Check("'Launch anyway' isn't asked again while the same skins are on", Skins.Overlaps(game, manifest, SkinPrefs.Load()).Count == 0);
            Compat.SetAddonEnabled(game, "nick_texture.vpk", true);
            Check("... but is when another skin for it is turned on", Ids(Skins.Overlaps(game, manifest, SkinPrefs.Load()).Select(s => s.Target)) == "nick");
            Compat.SetAddonEnabled(game, "nick_texture.vpk", false);
            File.Delete(SkinPrefs.FilePath);

            // add-ons replacing the same files (not skins for the same thing): asked about before a launch too
            var clashes = Clashes.Find(game, manifest, SkinPrefs.Load());
            string Pair(Clashes.Clash c) => c.A.Title + " + " + c.B.Title;
            Check("clashes: " + string.Join("; ", clashes.Select(Pair)), string.Join("; ", clashes.Select(Pair)) == "Survivor Icons + Survivors and AK Pack; 8-Player HUD + HUD Mod");
            Check("... skins for the same thing aren't clashes (Nick model skin and the pack share a model)", !clashes.Any(c => c.A.Title == "Nick Model Skin" || c.B.Title == "Nick Model Skin"));
            Check("described: " + clashes[0].What + " / " + clashes[1].What, clashes[0].What == "survivor portraits on the HUD (2)" && clashes[1].What == "the HUD layout (1)");
            Check("the HUD add-on is marked as one the app needs", clashes[1].A.Protected && !clashes[1].B.Protected);
            var cp = SkinPrefs.Load();
            cp.Picks["nick"] = "workshop\\1000000001.vpk";
            Clashes.Resolve(game, new[] { clashes[0].B, clashes[1].A }, new[] { clashes[1] }, cp);
            var after = Compat.ReadAddonList(game);
            Check("keeping the icons turns the pack off, never the HUD add-on", !after["workshop\\1000000001.vpk"] && after["l4d2_8p_hud.vpk"]);
            Check("... a skin pick for the add-on turned off is dropped, so the next launch doesn't turn it back on", !SkinPrefs.Load().Picks.ContainsKey("nick"));
            Check("'Keep both' isn't asked again, and nothing else is left", Clashes.Find(game, manifest, SkinPrefs.Load()).Count == 0);
            Compat.SetAddonEnabled(game, "workshop\\1000000001.vpk", true);
            Compat.SetAddonEnabled(game, "survivor_icons.vpk", false);
            Compat.SetAddonEnabled(game, "hud_mod.vpk", false);
            File.Delete(SkinPrefs.FilePath);

            // 4. applying and remembering
            var prefs = SkinPrefs.Load();
            Skins.Apply(inv, cs, prefs);
            var list = Compat.ReadAddonList(game);
            Check("applied: texture skin on, model skin and pack off in addonlist.txt", list["nick_texture.vpk"] && !list["nick_model.vpk"] && !list["workshop\\1000000001.vpk"]);
            prefs = SkinPrefs.Load();
            Check("remembered: Nick = texture skin, Rochelle/Coach/Ellis = default", prefs.Picks["nick"] == "nick_texture.vpk" && prefs.Picks["rochelle"] == "" && prefs.Picks["coach"] == "" && prefs.Picks["ellis"] == "");
            Check("a picked target isn't an overlap: only the AK-47 (rifle pack and campaign) is left", Ids(Skins.Overlaps(game, manifest, prefs).Select(s => s.Target)) == "ak47");

            // 5. re-applying at launch
            var first = Skins.Reapply(game, manifest, prefs);
            Check("first launch: nothing to restore, every add-on noted as seen (" + prefs.Seen.Count + ")", first.Count == 0 && prefs.Seen.Count == 7);
            Compat.SetAddonEnabled(game, "workshop\\1000000001.vpk", true);   // the player turns the pack back on in the game's menu
            var again = Skins.Reapply(game, manifest, prefs);
            Check("the pack turned back on is turned off again: " + string.Join(" | ", again), !Compat.ReadAddonList(game)["workshop\\1000000001.vpk"] && again.Count == 1);

            File.Copy(Path.Combine(game, "addons", "nick_model.vpk"), Path.Combine(game, "addons", "nick_new.vpk"));   // a new Nick skin arrives (on: no entry)
            var asked = new List<string>();
            var keep = Skins.Reapply(game, manifest, prefs, (m, t) => { asked.Add(m.Title + " for " + t.Name); return false; });
            Check("a new skin for a picked target is asked about, and kept off when declined: " + string.Join(", ", asked), asked.Count == 1 && !Compat.ReadAddonList(game)["nick_new.vpk"]);
            File.Delete(Path.Combine(game, "addons", "nick_texture.vpk"));
            var gone = Skins.Reapply(game, manifest, prefs);
            Check("a picked skin that's gone is reported: " + string.Join(" | ", gone), gone.Any(l => l.StartsWith("Nick:") && l.Contains("gone")));

            // 6. nothing outside the copy changed
            Check("your real addonlist.txt and skins.json are untouched", Hash(realList) == before && Hash(realSkins) == beforeSkins);
            Check("the game's original add-on list was backed up inside the copy", Directory.GetFiles(game, "addonlist.txt.original", SearchOption.AllDirectories).Length == 1);

            File.WriteAllText(outFile, log.ToString());
            return ok ? 0 : 1;
        }

        /// <summary>
        /// --skins-shot &lt;fixtures&gt; &lt;dir&gt;: the Skins window on a copy of the fixtures, off-screen, to PNGs: the overview,
        /// Nick (two skins on), the AK-47, a pack's question, and the read-only look while the game runs.
        /// </summary>
        public static int Shots(string fixtures, string dir)
        {
            var work = Path.Combine(Path.GetTempPath(), "l4d2-8p-skins-shot");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Copy(Path.Combine(fixtures, "left4dead2"), Path.Combine(work, "left4dead2"));
            SkinPrefs.FilePath = Path.Combine(work, "skins.json");
            SkinPrefs.ReadOnly = true;
            Directory.CreateDirectory(dir);
            // a real picture where Steam keeps the pack's Workshop thumbnail (the fixtures' one is a placeholder)
            using (var pic = new System.Drawing.Bitmap(256, 256))
            {
                using (var g = System.Drawing.Graphics.FromImage(pic))
                {
                    g.Clear(System.Drawing.Color.FromArgb(120, 30, 24));
                    g.DrawString("PACK", new System.Drawing.Font("Segoe UI", 48, System.Drawing.FontStyle.Bold), System.Drawing.Brushes.White, 40, 90);
                }
                pic.Save(Path.Combine(work, "left4dead2", "addons", "workshop", "1000000001.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            System.Windows.Forms.Application.EnableVisualStyles();
            // awaits come back to this thread, as they do inside the app's message loop
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            var f = new SkinsForm(() => Path.Combine(work, "left4dead2")) { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
            void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            void Save(System.Windows.Forms.Form form, string name)
            {
                using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(dir, name + ".png"));
                }
            }
            f.Show();
            Pump(2500);
            Save(f, "skins-overview");
            f.Panel.SelectTarget("nick"); Pump(400); Save(f, "skins-nick-conflict");
            f.Panel.SelectTarget("ak47"); Pump(400); Save(f, "skins-ak47");
            using (var q = f.Panel.QuestionFor("rochelle", "Survivors and AK Pack"))
            {
                q.StartPosition = System.Windows.Forms.FormStartPosition.Manual; q.Location = new System.Drawing.Point(-30000, -30000); q.ShowInTaskbar = false;
                q.Show(); Pump(400); Save(q, "skins-pack-question"); q.Close();
            }
            using (var q = new OverlapDialog(Skins.Overlaps(Path.Combine(work, "left4dead2"), Manifest.BuiltIn(), new SkinPrefs())))
            {
                q.StartPosition = System.Windows.Forms.FormStartPosition.Manual; q.Location = new System.Drawing.Point(-30000, -30000); q.ShowInTaskbar = false;
                q.Show(); Pump(400); Save(q, "skins-overlap-question"); q.Close();
            }
            using (var q = new ClashDialog(Clashes.Find(Path.Combine(work, "left4dead2"), Manifest.BuiltIn(), new SkinPrefs())))
            {
                q.StartPosition = System.Windows.Forms.FormStartPosition.Manual; q.Location = new System.Drawing.Point(-30000, -30000); q.ShowInTaskbar = false;
                q.Show(); q.Choose(0, 0); Pump(400); Save(q, "skins-clash-question"); q.Close();
            }
            // long Workshop names, as on a real PC: a survivor pack and an icons pack sharing 16 HUD pictures
            var longPair = new Clashes.Clash
            {
                A = new Clashes.Addon { Key = "dead_space.vpk", Title = "Dead Space Advanced Suit Squads (All 8 Survivors)" },
                B = new Clashes.Addon { Key = "survivor_icons.vpk", Title = "Scott Pilgrim Takes Off Styled Survivor Icons" },
                Files = Enumerable.Range(0, 12).Select(i => "materials/vgui/s_panel_" + i + ".vtf").Concat(Enumerable.Range(0, 4).Select(i => "materials/vgui/select_" + i + ".vtf")).ToList(),
            };
            using (var q = new ClashDialog(new List<Clashes.Clash> { longPair }))
            {
                q.StartPosition = System.Windows.Forms.FormStartPosition.Manual; q.Location = new System.Drawing.Point(-30000, -30000); q.ShowInTaskbar = false;
                q.Show(); q.Choose(0, 0); Pump(400); Save(q, "skins-clash-long"); q.Close();
            }
            SkinsPanel.PretendGameOpen = true;
            var again = f.Panel.Reload();
            for (var until = DateTime.Now.AddSeconds(20); !again.IsCompleted && DateTime.Now < until; ) Pump(100);
            Pump(300);
            if (again.IsFaulted) File.WriteAllText(Path.Combine(dir, "reload-error.txt"), again.Exception.ToString());
            f.Panel.SelectTarget("nick"); Pump(300); Save(f, "skins-game-running");
            f.Close();
            return 0;
        }

        static void Copy(string from, string to)
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(from, to));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(from, to), true);
        }

        static string Hash(string path)
        {
            if (path == null || !File.Exists(path)) return "none";
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}
