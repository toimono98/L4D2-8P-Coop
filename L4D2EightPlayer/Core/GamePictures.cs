using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The game's own look for each skin target, as a picture: the survivors' HUD portraits, the special infected's
    /// team pictures, and each weapon's and item's HUD icon (where its script says it is). Read from the player's own
    /// game (its pak01 files, the update folder first), never shipped with the app, and kept as PNGs in
    /// %LOCALAPPDATA%\L4D2-8P-Manager\pictures\&lt;game version&gt; so it's done once per game update.
    /// Targets the game has no picture for (the uncommon infected) have none.
    /// </summary>
    public static class GamePictures
    {
        // where each target's picture is: a weapon or melee script (its HUD sprite), or a texture
        static readonly Dictionary<string, string> Sources = new Dictionary<string, string>
        {
            ["nick"] = "vtf:vgui/s_panel_gambler", ["rochelle"] = "vtf:vgui/s_panel_producer", ["coach"] = "vtf:vgui/s_panel_coach", ["ellis"] = "vtf:vgui/s_panel_mechanic",
            ["bill"] = "vtf:vgui/s_panel_namvet", ["zoey"] = "vtf:vgui/s_panel_teenangst", ["louis"] = "vtf:vgui/s_panel_manager", ["francis"] = "vtf:vgui/s_panel_biker",
            ["pistol"] = "script:weapon_pistol", ["magnum"] = "script:weapon_pistol_magnum", ["uzi"] = "script:weapon_smg", ["mac10"] = "script:weapon_smg_silenced",
            ["mp5"] = "script:weapon_smg_mp5", ["pumpshotgun"] = "script:weapon_pumpshotgun", ["chromeshotgun"] = "script:weapon_shotgun_chrome",
            ["autoshotgun"] = "script:weapon_autoshotgun", ["spas"] = "script:weapon_shotgun_spas", ["m16"] = "script:weapon_rifle", ["ak47"] = "script:weapon_rifle_ak47",
            ["scar"] = "script:weapon_rifle_desert", ["sg552"] = "script:weapon_rifle_sg552", ["huntingrifle"] = "script:weapon_hunting_rifle",
            ["militarysniper"] = "script:weapon_sniper_military", ["scout"] = "script:weapon_sniper_scout", ["awp"] = "script:weapon_sniper_awp",
            ["m60"] = "script:weapon_rifle_m60", ["grenadelauncher"] = "script:weapon_grenade_launcher", ["chainsaw"] = "script:weapon_chainsaw",
            ["bat"] = "script:melee/baseball_bat", ["cricketbat"] = "script:melee/cricket_bat", ["crowbar"] = "script:melee/crowbar", ["guitar"] = "script:melee/electric_guitar",
            ["fireaxe"] = "script:melee/fireaxe", ["fryingpan"] = "script:melee/frying_pan", ["golfclub"] = "script:melee/golfclub", ["katana"] = "script:melee/katana",
            ["knife"] = "script:melee/knife", ["machete"] = "script:melee/machete", ["pitchfork"] = "script:melee/pitchfork", ["shovel"] = "script:melee/shovel",
            ["tonfa"] = "script:melee/tonfa", ["riotshield"] = "vtf:vgui/hud/icon_riotshield",
            ["medkit"] = "script:weapon_first_aid_kit", ["pills"] = "script:weapon_pain_pills", ["adrenaline"] = "script:weapon_adrenaline",
            ["defib"] = "script:weapon_defibrillator", ["molotov"] = "script:weapon_molotov", ["pipebomb"] = "script:weapon_pipe_bomb", ["bile"] = "script:weapon_vomitjar",
            ["incendiaryammo"] = "script:weapon_upgradepack_incendiary", ["explosiveammo"] = "script:weapon_upgradepack_explosive", ["gascan"] = "vtf:vgui/achievements/ach_scavenge_collect_all_cans",
            ["propane"] = "script:weapon_propanetank", ["oxygen"] = "script:weapon_oxygentank", ["fireworks"] = "script:weapon_fireworkcrate",
            // no HUD icon for these: the game's achievement pictures show them
            ["gnome"] = "vtf:vgui/achievements/asset_gnome", ["cola"] = "script:weapon_cola_bottles",
            ["clown"] = "vtf:vgui/achievements/ach_honk_a_clowns_nose", ["mudmen"] = "vtf:vgui/achievements/ach_kill_submerged_mudmen",
            // the uncommons without one of their own: the game's picture of them all
            ["ceda"] = "vtf:vgui/achievements/ach_kill_every_uncommon_infected", ["fallen"] = "vtf:vgui/achievements/ach_kill_every_uncommon_infected",
            ["roadcrew"] = "vtf:vgui/achievements/ach_kill_every_uncommon_infected", ["riot"] = "vtf:vgui/achievements/ach_kill_every_uncommon_infected",
            ["jimmy"] = "vtf:vgui/achievements/ach_kill_every_uncommon_infected",
            ["hunter"] = "vtf:vgui/hud/zombieteamimage_hunter", ["smoker"] = "vtf:vgui/hud/zombieteamimage_smoker", ["boomer"] = "vtf:vgui/hud/zombieteamimage_boomer",
            ["charger"] = "vtf:vgui/hud/zombieteamimage_charger", ["jockey"] = "vtf:vgui/hud/zombieteamimage_jockey", ["spitter"] = "vtf:vgui/hud/zombieteamimage_spitter",
            ["tank"] = "vtf:vgui/hud/zombieteamimage_tank", ["witch"] = "vtf:vgui/witch", ["common"] = "vtf:vgui/infected",
        };

        /// <summary>The cached pictures, by target id. Builds what's missing the first time for this game version.</summary>
        public static Dictionary<string, string> For(string gameDir)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (gameDir == null || !Directory.Exists(gameDir)) return result;
                var root = Path.GetDirectoryName(Path.GetFullPath(gameDir).TrimEnd('\\'));
                var folder = Path.Combine(Paths.AppData, "pictures", Safe(GameLocator.PatchVersion(root) ?? "game"));
                var done = Path.Combine(folder, "done.txt");
                if (!File.Exists(done))
                {
                    Directory.CreateDirectory(folder);
                    Build(root, folder);
                    File.WriteAllText(done, DateTime.Now.ToString("s"));
                }
                foreach (var id in Sources.Keys)
                {
                    var png = Path.Combine(folder, id + ".png");
                    if (File.Exists(png)) result[id] = png;
                }
            }
            catch { /* no pictures: the cards show the target's initial */ }
            return result;
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());

        static void Build(string root, string folder)
        {
            // the game's files, newest first: the update folder, then the DLC folders, then the base game
            var paks = new[] { "update", "left4dead2_dlc3", "left4dead2_dlc2", "left4dead2_dlc1", "left4dead2" }
                .Select(d => Path.Combine(root, d, "pak01_dir.vpk")).Where(File.Exists)
                .Select(p => { try { return Vpk.Open(p); } catch { return null; } }).Where(v => v != null).ToList();
            byte[] Read(string path) { foreach (var v in paks) if (v.Entries.ContainsKey(path)) { var b = v.Read(path, 16 * 1024 * 1024); if (b != null) return b; } return null; }

            foreach (var kv in Sources)
            {
                try
                {
                    using (var raw = Picture(kv.Value, Read))
                    using (var pic = Trim(raw))
                        if (pic != null) pic.Save(Path.Combine(folder, kv.Key + ".png"), ImageFormat.Png);
                }
                catch { /* this one has no picture */ }
            }
        }

        /// <summary>The picture without its see-through edges (HUD sprites leave room around the icon), as a copy.</summary>
        static Bitmap Trim(Bitmap b)
        {
            if (b == null) return null;
            int l = b.Width, t = b.Height, r = -1, bo = -1;
            var bits = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, px, 0, px.Length);
            b.UnlockBits(bits);
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (((px[y * b.Width + x] >> 24) & 0xFF) > 24) { if (x < l) l = x; if (x > r) r = x; if (y < t) t = y; if (y > bo) bo = y; }
            if (r < 0) return null;   // nothing to see
            var rect = Rectangle.FromLTRB(l, t, r + 1, bo + 1);
            return b.Clone(rect, PixelFormat.Format32bppArgb);
        }

        static Bitmap Picture(string source, Func<string, byte[]> read)
        {
            if (source.StartsWith("vtf:")) return Vtf.Decode(read("materials/" + source.Substring(4) + ".vtf"));
            // a weapon script's HUD sprite: TextureData { "weapon" (or a melee's "sprite_active") { file, x, y, width, height } }
            var text = read("scripts/" + source.Substring(7) + ".txt");
            if (text == null) return null;
            var lines = System.Text.Encoding.UTF8.GetString(text).Split('\n').Select(l => { int c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l.Substring(0, c) : l; });
            var script = string.Join("\n", lines);
            int td = script.IndexOf("TextureData", StringComparison.OrdinalIgnoreCase);
            if (td < 0) return null;
            foreach (var sprite in new[] { "weapon", "sprite_active", "weapon_s" })
            {
                var m = Regex.Match(script.Substring(td), "\"" + sprite + "\"\\s*\\{([^}]*)\\}", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string Field(string name) { var f = Regex.Match(m.Groups[1].Value, "\"" + name + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase); return f.Success ? f.Groups[1].Value : null; }
                var file = Field("file");
                if (file == null) continue;
                var full = Vtf.Decode(read("materials/" + file + ".vtf"));
                if (full == null) continue;
                int.TryParse(Field("x"), out var x);
                int.TryParse(Field("y"), out var y);
                int.TryParse(Field("width"), out var w);
                int.TryParse(Field("height"), out var h);
                if (w <= 0 || h <= 0) return full;
                // the sprite's place in the texture (clamped: some scripts describe a bigger sheet than the file)
                var r = Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, full.Width, full.Height));
                if (r.Width <= 0 || r.Height <= 0 || r.Size == full.Size) return full;
                using (full) return full.Clone(r, PixelFormat.Format32bppArgb);
            }
            return null;
        }
    }
}
