using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Custom campaigns found in add-on VPKs (their missions/*.txt): title, chapter maps and the Workshop item,
    /// for the Play tab's campaign list, the invite and the rotation list.
    /// </summary>
    public static class Campaigns
    {
        public sealed class Campaign
        {
            public string Title;
            public List<string> Maps = new List<string>();   // coop chapters in order
            public string WorkshopId;                       // from a Workshop VPK's file name (the item id)
            public string File;
            public string FirstMap => Maps.Count > 0 ? Maps[0] : null;
            public string WorkshopUrl => WorkshopId == null ? null : "https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkshopId;
        }

        static readonly Dictionary<string, (long size, DateTime mtime, List<Campaign> found)> Cache =
            new Dictionary<string, (long, DateTime, List<Campaign>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every custom campaign in the add-on folders of these left4dead2 folders (first map wins on duplicates).</summary>
        public static List<Campaign> Find(params string[] gameDirs)
        {
            var all = new List<Campaign>();
            foreach (var dir in gameDirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var addons = Path.Combine(dir, "addons");
                if (!Directory.Exists(addons)) continue;
                string[] vpks;
                try { vpks = Directory.GetFiles(addons, "*.vpk", SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (var vpk in vpks)
                    foreach (var c in Read(vpk))
                        if (!all.Any(a => a.FirstMap.Equals(c.FirstMap, StringComparison.OrdinalIgnoreCase))) all.Add(c);
            }
            return all.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The campaign whose chapters include this map, if any.</summary>
        public static Campaign ForMap(IEnumerable<Campaign> list, string map) =>
            list?.FirstOrDefault(c => c.Maps.Any(m => m.Equals(map ?? "", StringComparison.OrdinalIgnoreCase)));

        static List<Campaign> Read(string vpkPath)
        {
            var fi = new FileInfo(vpkPath);
            if (Cache.TryGetValue(vpkPath, out var c) && c.size == fi.Length && c.mtime == fi.LastWriteTimeUtc) return c.found;
            var found = new List<Campaign>();
            try
            {
                var vpk = Vpk.Open(vpkPath);
                foreach (var name in vpk.Entries.Keys.Where(k => k.StartsWith("missions/") && k.EndsWith(".txt")).ToList())
                {
                    var camp = ParseMission(vpk.ReadText(name, 256 * 1024));
                    if (camp == null || ServerQuery.IsOfficialMap(camp.FirstMap)) continue;   // a mod changing an official campaign
                    camp.File = vpkPath;
                    var stem = Path.GetFileNameWithoutExtension(vpkPath);
                    if (Regex.IsMatch(stem, @"^\d{6,}$")) camp.WorkshopId = stem;
                    found.Add(camp);
                }
            }
            catch (Exception) { /* not a readable VPK: no campaigns */ }
            Cache[vpkPath] = (fi.Length, fi.LastWriteTimeUtc, found);
            return found;
        }

        /// <summary>A missions/*.txt: "mission" { "DisplayTitle" … "modes" { "coop" { "1" { "Map" "…" } … } } }.</summary>
        static Campaign ParseMission(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Dictionary<string, object> root;
            try { root = Vdf.Parse(text); } catch (Exception) { return null; }
            var mission = root.Block("mission") ?? root.Values.OfType<Dictionary<string, object>>().FirstOrDefault();
            var modes = mission.Block("modes");
            if (modes == null) return null;
            var coop = modes.Block("coop") ?? modes.Block("realism") ?? modes.Values.OfType<Dictionary<string, object>>().FirstOrDefault();
            if (coop == null) return null;
            var camp = new Campaign { Title = mission.Value("DisplayTitle") ?? mission.Value("Name") ?? "Custom campaign" };
            foreach (var kv in coop.Where(kv => int.TryParse(kv.Key, out _)).OrderBy(kv => int.Parse(kv.Key)))
            {
                var map = (kv.Value as Dictionary<string, object>).Value("Map");
                if (!string.IsNullOrWhiteSpace(map)) camp.Maps.Add(map.Trim());
            }
            return camp.Maps.Count > 0 ? camp : null;
        }
    }
}
