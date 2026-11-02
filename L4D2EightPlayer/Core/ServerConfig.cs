using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Writes this tool's settings into config files as a marked block, so anything else the
    /// user keeps in those files is left alone.
    /// </summary>
    public static class ServerConfig
    {
        const string Begin = "// >>> L4D2-8P-Manager: this block is rewritten by the 8-Player Manager. Put your own settings outside it.";
        const string End = "// <<< L4D2-8P-Manager";

        public static void WriteBlock(string file, IEnumerable<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string text = File.Exists(file) ? File.ReadAllText(file) : "";
            text = RemoveBlock(text).TrimEnd();

            var sb = new StringBuilder(text);
            if (sb.Length > 0) sb.Append("\r\n\r\n");
            sb.Append(Begin).Append("\r\n");
            foreach (var l in lines) sb.Append(l).Append("\r\n");
            sb.Append(End).Append("\r\n");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        public static void ClearBlock(string file)
        {
            if (!File.Exists(file)) return;
            var text = RemoveBlock(File.ReadAllText(file));
            if (text.Trim().Length == 0) File.Delete(file);
            else File.WriteAllText(file, text.TrimEnd() + "\r\n", new UTF8Encoding(false));
        }

        static string RemoveBlock(string text) =>
            Regex.Replace(text, Regex.Escape("// >>> L4D2-8P-Manager") + ".*?" + Regex.Escape(End) + @"\r?\n?", "", RegexOptions.Singleline);

        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";

        /// <summary>Writes server.cfg (or listenserver.cfg) and the l4dmultislots overrides.</summary>
        public static void Apply(Settings s, string gameDir)
        {
            bool listen = s.Mode == "listen";
            int slots = listen ? Math.Min(s.MaxPlayers, 8) : s.MaxPlayers;
            var lines = new List<string> { "// Player limits (cvars from the L4DToolZ extension)" };
            // A listen server tops out at 8 players anyway; leave its client limit at the engine default.
            if (!listen) lines.Add("sv_setmax 31");
            lines.Add("sv_maxplayers " + slots);
            lines.Add("sv_visiblemaxplayers " + slots);

            if (!listen)
            {
                lines.Add("hostname " + Q(s.ServerName));
                // No sv_password here: server.cfg runs on every map load, and re-setting sv_password there
                // crashes SourceMod on the following map change while a player is connected. The password
                // goes on the command line instead (Launcher.ServerArgs), so it's set once at startup.
                // Internet: listed with Steam and reachable from outside. LAN / Just me: sv_lan 1 keeps it local.
                // The launch line sets the same value before the first map (Launcher.LanArg); this line must
                // never differ from it, or the server reconnects to Steam and crashes a map change later.
                lines.Add("sv_lan " + (s.Internet ? 0 : 1));
                lines.Add("sv_region 255");
                if (s.JoinMethod == "lobby" && s.Internet)
                {
                    lines.Add("// Lobby joins: reservation stays on until full, then l4d_unreservelobby lifts it for players 5+");
                    lines.Add("sv_allow_lobby_connect_only 1");
                    lines.Add("sv_force_unreserved 0");
                }
                else
                {
                    lines.Add("// Direct connect / server browser: no lobby reservation at all");
                    lines.Add("sv_allow_lobby_connect_only 0");
                    lines.Add("sv_force_unreserved 1");
                }
            }

            if (listen) lines.Add("sv_lan " + (s.Internet ? 0 : 1));
            lines.Add("// 5+ survivor stability (from the 8+ Survivors In Coop guide)");
            lines.Add("sm_cvar precache_all_survivors 1");
            lines.Add("sm_cvar sv_consistency 0");
            lines.Add("z_difficulty " + s.Difficulty);

            WriteBlock(Path.Combine(gameDir, "cfg", listen ? "listenserver.cfg" : "server.cfg"), lines);

            // l4dmultislots generates its own cfg on first run and executes it after server.cfg,
            // so the overrides go at the end of that file.
            var ms = new List<string>
            {
                "l4d_multislots_max_survivors " + slots,
                "l4d_multislots_min_survivors " + (s.FillWithBots ? slots : 4),
                "l4d_multislots_spawn_survivors_roundstart " + (s.FillWithBots ? 1 : 0),
                // Survivors 5+ start like the first four: starting pistol, a first aid kit, 100 health
                // (the plugin's defaults are a random tier 2 gun, secondary and throwable, and 80+20 health).
                "l4d_multislots_firstweapon 0",
                "l4d_multislots_secondweapon 0",
                "l4d_multislots_thirdweapon 0",
                "l4d_multislots_forthweapon 1",
                "l4d_multislots_fifthweapon 0",
                "l4d_multislots_respawnhp 100",
                "l4d_multislots_respawnbuffhp 0",
            };
            WriteBlock(Path.Combine(gameDir, "cfg", "sourcemod", "l4dmultislots.cfg"), ms);

            // The 8-Player HUD plugin's look: 1 = health bars and icons, which every player needs the add-on for.
            WriteBlock(Path.Combine(gameDir, "cfg", "sourcemod", "l4d2_8p_hud.cfg"),
                new[] { "l4d2_8p_hud_style " + (s.HudAddonLook ? 1 : 0) });

            // Scaling for 5+ survivors (Play tab). The per-category on/off and the caps stay the user's.
            WriteBlock(Path.Combine(gameDir, "cfg", "sourcemod", "l4d2_8p_items.cfg"), new[]
            {
                "l4d2_8p_items_mode " + ModeIndex(s.ItemsModeOrDefault),
                "l4d2_8p_items_custom_weapons " + s.ItemsCustomWeapons,
                "l4d2_8p_items_custom_throwables " + s.ItemsCustomThrowables,
                "l4d2_8p_items_custom_healing " + s.ItemsCustomHealing,
            });
            WriteBlock(Path.Combine(gameDir, "cfg", "sourcemod", "l4d2_8p_infected.cfg"), new[]
            {
                "l4d2_8p_infected_mode " + ModeIndex(s.InfectedMode),
                "l4d2_8p_infected_custom_specials " + s.InfectedCustomSpecials,
                "l4d2_8p_infected_custom_horde " + s.InfectedCustomHorde,
                "l4d2_8p_infected_custom_tank " + s.InfectedCustomTank,
                "l4d2_8p_infected_custom_respawn " + s.InfectedCustomRespawn,
            });
        }

        /// <summary>Plugin mode number for a Settings.ScalingModes name (unknown = auto).</summary>
        static int ModeIndex(string mode)
        {
            int i = Array.IndexOf(Settings.ScalingModes, mode);
            return i < 0 ? 1 : i;
        }

        public static readonly string[] Maps =
        {
            "c1m1_hotel", "c2m1_highway", "c3m1_plankcountry", "c4m1_milltown_a", "c5m1_waterfront",
            "c6m1_riverbank", "c7m1_docks", "c8m1_apartment", "c9m1_alleys", "c10m1_caves",
            "c11m1_greenhouse", "c12m1_hilltop", "c13m1_alpinecreek", "c14m1_junkyard",
        };

        public static readonly string[] GameModes = { "coop", "realism", "survival", "versus", "scavenge" };
        public static readonly string[] Difficulties = { "Easy", "Normal", "Hard", "Impossible" };
    }
}
