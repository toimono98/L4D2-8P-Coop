using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// "Version X is out": asks GitHub for the latest release at most every few hours (both sides share the answer)
    /// and says nothing when offline or on any error. Refresh asks now (Check for updates). GitHub allows 60
    /// questions an hour from one address, so even a house full of friends running the app stays well under.
    /// </summary>
    public static class UpdateCheck
    {
        public const string Repo = "toimono98/L4D2-8P-Coop";
        public static string ReleasePage => "https://github.com/" + Repo + "/releases/latest";
        static string FilePath => Path.Combine(Paths.AppData, "update.json");

        public sealed class State
        {
            public DateTime CheckedUtc { get; set; }
            public string Latest { get; set; }
        }

        /// <summary>The last answer from GitHub (Latest null when it was never reached).</summary>
        public static State Last => Json.Load<State>(FilePath);

        /// <summary>How old the last answer can be before GitHub is asked again.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

        /// <summary>The newer version ("1.6.0"), or null when this one is current or the check failed.</summary>
        public static async Task<string> Newer()
        {
            var st = Last;
            if (st.Latest == null || DateTime.UtcNow - st.CheckedUtc >= MaxAge) await Refresh().ConfigureAwait(false);
            st = Last;
            return IsNewer(st.Latest, AppInfo.Version) ? st.Latest : null;
        }

        /// <summary>Asks GitHub now. False when it couldn't be reached (offline, rate-limited, no releases).</summary>
        public static async Task<bool> Refresh()
        {
            try
            {
                var release = Json.Parse(await Http.GetString("https://api.github.com/repos/" + Repo + "/releases/latest").ConfigureAwait(false)) as Dictionary<string, object>;
                if (release != null && release.TryGetValue("tag_name", out var tag) && tag is string t)
                {
                    Json.Save(FilePath, new State { Latest = t.TrimStart('v', 'V'), CheckedUtc = DateTime.UtcNow });
                    return true;
                }
            }
            catch (Exception) { /* offline, rate-limited or no releases: try again next start */ }
            return false;
        }

        public static bool IsNewer(string candidate, string current) =>
            Version.TryParse(candidate ?? "", out var a) && Version.TryParse(current ?? "", out var b) && a > b;
    }
}
