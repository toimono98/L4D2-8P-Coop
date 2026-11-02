using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// "Version X is out": asks GitHub for the latest release at most once a day (both apps share the answer)
    /// and says nothing when offline or on any error.
    /// </summary>
    public static class UpdateCheck
    {
        public const string Repo = "toimono98/L4D2-8P-Coop";
        public static string ReleasePage => "https://github.com/" + Repo + "/releases/latest";

        public sealed class State
        {
            public DateTime CheckedUtc { get; set; }
            public string Latest { get; set; }
        }

        /// <summary>The newer version ("1.6.0"), or null when this one is current or the check failed.</summary>
        public static async Task<string> Newer()
        {
            var file = Path.Combine(Paths.AppData, "update.json");
            var st = Json.Load<State>(file);
            if (st.Latest == null || (DateTime.UtcNow - st.CheckedUtc).TotalHours >= 24)
            {
                try
                {
                    var release = Json.Parse(await Http.GetString("https://api.github.com/repos/" + Repo + "/releases/latest").ConfigureAwait(false)) as Dictionary<string, object>;
                    if (release != null && release.TryGetValue("tag_name", out var tag) && tag is string t)
                    {
                        st.Latest = t.TrimStart('v', 'V');
                        st.CheckedUtc = DateTime.UtcNow;
                        Json.Save(file, st);
                    }
                }
                catch (Exception) { /* offline, rate-limited or no releases: try again next start */ }
            }
            return IsNewer(st.Latest, AppInfo.Version) ? st.Latest : null;
        }

        public static bool IsNewer(string candidate, string current) =>
            Version.TryParse(candidate ?? "", out var a) && Version.TryParse(current ?? "", out var b) && a > b;
    }
}
