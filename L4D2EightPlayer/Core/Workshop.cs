using System.Diagnostics;

namespace L4D2EightPlayer.Core
{
    /// <summary>Left 4 Dead 2's Steam Workshop, where skins and campaigns come from.</summary>
    public static class Workshop
    {
        public const string SteamUrl = "steam://url/SteamWorkshopPage/550";
        public const string WebUrl = "https://steamcommunity.com/app/550/workshop/";

        /// <summary>In the Steam client when it's installed (signed in, so Subscribe works right away), else in the browser.</summary>
        public static void Open()
        {
            try { Process.Start(GameLocator.FindSteam() != null ? SteamUrl : WebUrl); }
            catch { try { Process.Start(WebUrl); } catch { } }
        }
    }
}
