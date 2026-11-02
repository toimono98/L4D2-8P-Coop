using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Posts "the server is up" (and optionally "stopped") to a Discord channel through a webhook. The webhook
    /// URL is a secret: it is stored in settings.json only and never written to the log or an error message.
    /// </summary>
    public static class Discord
    {
        static readonly Regex Webhook = new Regex(@"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+/?$", RegexOptions.IgnoreCase);

        public static bool IsWebhook(string url) => Webhook.IsMatch((url ?? "").Trim());

        public sealed class Announcement
        {
            public string Server, Campaign, Mode, Difficulty, SteamUrl, ConsoleCommand, NeedsTitle, NeedsUrl;
            public int MaxPlayers;
            public bool Stopped;
        }

        const int Red = 0xA8231B, Muted = 0x5A4F44;

        /// <summary>The webhook body: one embed in the app's colours.</summary>
        public static string Payload(Announcement a)
        {
            var embed = new Dictionary<string, object>
            {
                ["title"] = Cut(a.Stopped ? $"{a.Server} has stopped" : $"{a.Server} is up", 256),
                ["color"] = a.Stopped ? Muted : Red,
                ["footer"] = new Dictionary<string, object> { ["text"] = "L4D2 8-Player Co-op Manager " + AppInfo.Version },
            };
            if (!a.Stopped)
            {
                embed["description"] = Cut($"**{a.Campaign}** · {a.Mode} · {a.Difficulty} · up to {a.MaxPlayers} survivors", 4096);
                var fields = new List<object>
                {
                    Field("Join", $"`{a.SteamUrl}`\nor in the L4D2 console: `{a.ConsoleCommand}`"),
                    Field("No Join app yet?", $"[Download it for Windows]({UpdateCheck.ReleasePage}) and paste the link above."),
                };
                if (a.NeedsTitle != null)
                    fields.Add(Field("Custom campaign", a.NeedsUrl != null ? $"Subscribe first: [{a.NeedsTitle}]({a.NeedsUrl})" : $"Install {a.NeedsTitle} first."));
                embed["fields"] = fields;
            }
            return Json.Write(new Dictionary<string, object>
            {
                ["username"] = "L4D2 8-Player Co-op",
                ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new List<object>() },   // never ping anyone
                ["embeds"] = new List<object> { embed },
            });
        }

        static Dictionary<string, object> Field(string name, string value) =>
            new Dictionary<string, object> { ["name"] = Cut(name, 256), ["value"] = Cut(value, 1024), ["inline"] = false };

        static string Cut(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        public static Task Post(string webhook, Announcement a) => Http.PostJson(webhook.Trim(), Payload(a));
    }
}
