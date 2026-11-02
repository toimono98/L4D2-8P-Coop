using System.Collections.Generic;

namespace L4D2EightPlayer.Core
{
    /// <summary>User choices, saved to %LOCALAPPDATA%\L4D2-8P-Manager\settings.json.</summary>
    public sealed class Settings
    {
        public string GamePath { get; set; }
        public string Mode { get; set; } = "dedicated";          // "dedicated" | "listen"
        public string ServerRoot { get; set; }                   // holds steamcmd\ and server\
        public string ExistingServerPath { get; set; }           // use an srcds folder the user already has
        public List<string> Selected { get; set; }               // component ids the user ticked

        public string ServerName { get; set; } = "L4D2 8-Player Co-op";
        public int Port { get; set; } = 27015;
        public int MaxPlayers { get; set; } = 8;
        public bool FillWithBots { get; set; } = false;
        public bool HudAddonLook { get; set; } = false;           // 8-Player HUD: health bars + icons (everyone needs the add-on)
        public bool ScaleItems { get; set; } = true;              // Items For 5+ Survivors: item spawns scale with the survivor count
        public string GameMode { get; set; } = "coop";
        public string Difficulty { get; set; } = "Normal";
        public string Map { get; set; } = "c1m1_hotel";
        public string Password { get; set; } = "";
        public string JoinMethod { get; set; } = "direct";      // "direct" | "lobby"
        public string NetworkMode { get; set; } = "internet";   // "internet" | "lan" | "local" (who can join)

        public bool Internet => NetworkMode != "lan" && NetworkMode != "local";
        public string LobbyAddress { get; set; } = "";
        public bool SyncAddons { get; set; } = true;
        public bool UpdateServerOnStart { get; set; } = false;
        public bool WatchMods { get; set; } = true;
        public bool ScanBeforeLaunch { get; set; } = true;
        public string ManifestUrl { get; set; } = "";
        public bool AutoPortForward { get; set; } = true;
        public bool PortForwardAsked { get; set; }
        public int ForwardedPort { get; set; }                    // port this tool opened on the router (0 = none)

        public static Settings Load() => Json.Load<Settings>(Paths.SettingsFile);
        /// <summary>Set by the layout audit so it never overwrites the user's real settings.</summary>
        public static bool ReadOnly;
        public void Save() { if (!ReadOnly) Json.Save(Paths.SettingsFile, this); }

        /// <summary>The srcds install folder (holds srcds.exe) for dedicated mode.</summary>
        public string ServerInstallDir =>
            !string.IsNullOrWhiteSpace(ExistingServerPath) ? ExistingServerPath
            : string.IsNullOrWhiteSpace(ServerRoot) ? null : System.IO.Path.Combine(ServerRoot, "server");

        public string ClientGameDir => string.IsNullOrWhiteSpace(GamePath) ? null : System.IO.Path.Combine(GamePath, "left4dead2");

        /// <summary>The left4dead2 folder that mods get installed into for the current mode.</summary>
        public string TargetGameDir =>
            Mode == "listen" ? ClientGameDir
            : ServerInstallDir == null ? null : System.IO.Path.Combine(ServerInstallDir, "left4dead2");
    }
}
