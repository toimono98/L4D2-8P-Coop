using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>Left 4 Dead 2's Steam Workshop, where skins and campaigns come from.</summary>
    public static class Workshop
    {
        public const string SteamUrl = "steam://url/SteamWorkshopPage/550";
        public const string WebUrl = "https://steamcommunity.com/app/550/workshop/";
        const string Api = "https://api.steampowered.com/ISteamRemoteStorage/";

        /// <summary>In the Steam client when it's installed (signed in, so Subscribe works right away), else in the browser.</summary>
        public static void Open()
        {
            try { Process.Start(GameLocator.FindSteam() != null ? SteamUrl : WebUrl); }
            catch { try { Process.Start(WebUrl); } catch { } }
        }

        /// <summary>One Workshop item's page (a collection's has "Subscribe to all"), in Steam when it's installed.</summary>
        public static void OpenItem(string id)
        {
            var web = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
            try { Process.Start(GameLocator.FindSteam() != null ? "steam://url/CommunityFilePage/" + id : web); }
            catch { try { Process.Start(web); } catch { } }
        }

        /// <summary>The item number in a Workshop link ("...filedetails/?id=123") or a bare number; null if neither.</summary>
        public static string ItemId(string linkOrId)
        {
            var s = (linkOrId ?? "").Trim();
            if (Regex.IsMatch(s, @"^\d{3,20}$")) return s;
            var m = Regex.Match(s, @"[?&]id=(\d{3,20})");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Reads a collection's title and items from Steam's public Web API (no sign-in, nothing downloaded but
        /// the list and its picture). Collections inside it are opened one level deep.
        /// </summary>
        public static async Task<SkinPrefs.Collection> FetchCollection(string linkOrId, CancellationToken ct = default)
        {
            var id = ItemId(linkOrId) ?? throw new FormatException("That isn't a Workshop link. Copy the collection's page address (it has ?id= and a number) and paste it here.");
            var details = await Details(new[] { id }, ct).ConfigureAwait(false);
            if (details.Count == 0 || Num(details[0], "result") != 1) throw new InvalidOperationException("Steam doesn't know that Workshop item, or it's private or deleted.");
            var d = details[0];
            if (Num(d, "consumer_app_id") != GameLocator.GameAppId) throw new InvalidOperationException("That Workshop item isn't for Left 4 Dead 2.");

            var children = await Children(new[] { id }, ct).ConfigureAwait(false);
            if (!children.TryGetValue(id, out var kids) || kids.Count == 0)
                throw new InvalidOperationException("That's a single Workshop item, not a collection. Subscribe to it on the Workshop instead.");
            var items = kids.Where(k => k.type != 2).Select(k => k.id).ToList();
            var nested = kids.Where(k => k.type == 2).Select(k => k.id).ToList();
            if (nested.Count > 0)
                foreach (var inner in (await Children(nested, ct).ConfigureAwait(false)).Values)
                    items.AddRange(inner.Where(k => k.type != 2).Select(k => k.id));

            var c = new SkinPrefs.Collection
            {
                Id = id,
                Title = (d.TryGetValue("title", out var t) ? t as string : null)?.Trim() is string title && title.Length > 0 ? title : "Collection " + id,
                Items = items.Distinct().ToList(),
                Fetched = DateTime.Now.ToString("s"),
            };
            // its picture, for the card (best effort)
            try
            {
                if (d.TryGetValue("preview_url", out var u) && u is string url && url.StartsWith("http"))
                    await Http.Download(url, CollectionPicture(id), null, ct).ConfigureAwait(false);
            }
            catch { }
            return c;
        }

        /// <summary>Where a collection's picture is kept.</summary>
        public static string CollectionPicture(string id) => Path.Combine(Paths.Cache, "collections", id + ".jpg");

        static async Task<List<Dictionary<string, object>>> Details(IList<string> ids, CancellationToken ct)
        {
            var fields = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("itemcount", ids.Count.ToString()) };
            for (int i = 0; i < ids.Count; i++) fields.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", ids[i]));
            var json = await Http.PostForm(Api + "GetPublishedFileDetails/v1/", fields, ct).ConfigureAwait(false);
            return List(json, "publishedfiledetails");
        }

        /// <summary>Each collection's items: (item id, file type; 2 is a collection).</summary>
        static async Task<Dictionary<string, List<(string id, int type)>>> Children(IList<string> ids, CancellationToken ct)
        {
            var fields = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("collectioncount", ids.Count.ToString()) };
            for (int i = 0; i < ids.Count; i++) fields.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", ids[i]));
            var json = await Http.PostForm(Api + "GetCollectionDetails/v1/", fields, ct).ConfigureAwait(false);
            var result = new Dictionary<string, List<(string, int)>>();
            foreach (var c in List(json, "collectiondetails"))
            {
                var kids = new List<(string, int)>();
                if (c.TryGetValue("children", out var ch) && ch is System.Collections.IEnumerable arr)
                    foreach (var o in arr)
                        if (o is Dictionary<string, object> k && k.TryGetValue("publishedfileid", out var kid))
                            kids.Add((Convert.ToString(kid), (int)Num(k, "filetype")));
                if (c.TryGetValue("publishedfileid", out var cid)) result[Convert.ToString(cid)] = kids;
            }
            return result;
        }

        static List<Dictionary<string, object>> List(string json, string name)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root != null && root.TryGetValue("response", out var r) && r is Dictionary<string, object> resp && resp.TryGetValue(name, out var l) && l is System.Collections.IEnumerable arr)
                return arr.OfType<Dictionary<string, object>>().ToList();
            return new List<Dictionary<string, object>>();
        }

        static long Num(Dictionary<string, object> d, string key)
        {
            try { return d.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : -1; }
            catch { return -1; }
        }
    }
}
