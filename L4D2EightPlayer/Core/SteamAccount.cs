using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace L4D2EightPlayer.Core
{
    /// <summary>The Steam account signed in on this PC: how the server tells which player is the host.</summary>
    public static class SteamAccount
    {
        const long Base = 76561197960265728;   // SteamID64 of account 0

        /// <summary>"STEAM_1:y:z" (the form SourceMod reports) for the signed-in account, else the last one used; null if none.</summary>
        public static string Steam2()
        {
            long account = 0;
            try { account = Convert.ToInt64(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0) ?? 0); } catch { }
            if (account <= 0) account = MostRecent();
            return account > 0 ? $"STEAM_1:{account & 1}:{account >> 1}" : null;
        }

        /// <summary>The account Steam marks MostRecent in config\loginusers.vdf (when Steam isn't running).</summary>
        static long MostRecent()
        {
            try
            {
                var steam = GameLocator.FindSteam();
                var file = steam == null ? null : Path.Combine(steam, "config", "loginusers.vdf");
                if (file == null || !File.Exists(file)) return 0;
                var users = Vdf.ParseFile(file).Block("users");
                var id = users?.FirstOrDefault(u => (u.Value as System.Collections.Generic.Dictionary<string, object>).Value("MostRecent") == "1").Key;
                return long.TryParse(id, out var id64) && id64 > Base ? id64 - Base : 0;
            }
            catch { return 0; }
        }
    }
}
