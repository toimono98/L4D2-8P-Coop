using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The official campaigns, and custom ones found in add-on VPKs (their missions/*.txt): title, chapter maps
    /// and names, and the Workshop item, for the Play tab's campaign and chapter lists, the invite and the rotation list.
    /// </summary>
    public static class Campaigns
    {
        public sealed class Campaign
        {
            public string Title;
            public List<string> Maps = new List<string>();       // coop chapters in order
            public List<string> Chapters = new List<string>();   // their names as the game shows them (null where unknown)
            public string WorkshopId;                           // from a Workshop VPK's file name (the item id)
            public string File;
            public bool Official;
            public string FirstMap => Maps.Count > 0 ? Maps[0] : null;
            public string WorkshopUrl => WorkshopId == null ? null : "https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkshopId;
            /// <summary>"1 · Hotel"; the map's own name when the campaign doesn't name the chapter.</summary>
            public override string ToString() => Title;   // how the campaign list shows it
            public string ChapterLabel(int i) => $"{i + 1} · {(i < Chapters.Count && !string.IsNullOrWhiteSpace(Chapters[i]) ? Chapters[i] : Maps[i])}";
        }

        static Campaign Of(string title, params string[] chapters) => new Campaign
        {
            Title = title, Official = true,
            Maps = chapters.Select(c => c.Split('|')[0]).ToList(),
            Chapters = chapters.Select(c => c.Split('|')[1]).ToList(),
        };

        /// <summary>The 14 official campaigns in the game's order, with their chapters' in-game names.</summary>
        public static readonly IReadOnlyList<Campaign> Official = new[]
        {
            Of("Dead Center", "c1m1_hotel|Hotel", "c1m2_streets|Streets", "c1m3_mall|Mall", "c1m4_atrium|Atrium"),
            Of("Dark Carnival", "c2m1_highway|Highway", "c2m2_fairgrounds|Fairground", "c2m3_coaster|Coaster", "c2m4_barns|Barns", "c2m5_concert|Concert"),
            Of("Swamp Fever", "c3m1_plankcountry|Plank Country", "c3m2_swamp|Swamp", "c3m3_shantytown|Shanty Town", "c3m4_plantation|Plantation"),
            Of("Hard Rain", "c4m1_milltown_a|Milltown", "c4m2_sugarmill_a|Sugar Mill", "c4m3_sugarmill_b|Mill Escape", "c4m4_milltown_b|Return to Town", "c4m5_milltown_escape|Town Escape"),
            Of("The Parish", "c5m1_waterfront|Waterfront", "c5m2_park|Park", "c5m3_cemetery|Cemetery", "c5m4_quarter|Quarter", "c5m5_bridge|Bridge"),
            Of("The Passing", "c6m1_riverbank|Riverbank", "c6m2_bedlam|Underground", "c6m3_port|Port"),
            Of("The Sacrifice", "c7m1_docks|Docks", "c7m2_barge|Barge", "c7m3_port|Port"),
            Of("No Mercy", "c8m1_apartment|Apartments", "c8m2_subway|Subway", "c8m3_sewers|Sewer", "c8m4_interior|Hospital", "c8m5_rooftop|Rooftop Finale"),
            Of("Crash Course", "c9m1_alleys|Alleys", "c9m2_lots|Truck Depot Finale"),
            Of("Death Toll", "c10m1_caves|The Turnpike", "c10m2_drainage|The Drains", "c10m3_ranchhouse|The Church", "c10m4_mainstreet|The Town", "c10m5_houseboat|Boathouse Finale"),
            Of("Dead Air", "c11m1_greenhouse|The Greenhouse", "c11m2_offices|The Crane", "c11m3_garage|The Construction Site", "c11m4_terminal|The Terminal", "c11m5_runway|Runway Finale"),
            Of("Blood Harvest", "c12m1_hilltop|The Woods", "c12m2_traintunnel|The Tunnel", "c12m3_bridge|The Bridge", "c12m4_barn|The Train Station", "c12m5_cornfield|Farmhouse Finale"),
            Of("Cold Stream", "c13m1_alpinecreek|Alpine Creek", "c13m2_southpinestream|South Pine Stream", "c13m3_memorialbridge|Memorial Bridge", "c13m4_cutthroatcreek|Cut-throat Creek"),
            Of("The Last Stand", "c14m1_junkyard|The Junkyard", "c14m2_lighthouse|Lighthouse Finale"),
        };

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
                var chapter = kv.Value as Dictionary<string, object>;
                var map = chapter.Value("Map");
                if (string.IsNullOrWhiteSpace(map)) continue;
                camp.Maps.Add(map.Trim());
                // "DisplayName" is the chapter's name; a "#token" needs the add-on's translation files, so it's left out
                var name = chapter.Value("DisplayName")?.Trim();
                camp.Chapters.Add(string.IsNullOrEmpty(name) || name.StartsWith("#") ? null : name);
            }
            return camp.Maps.Count > 0 ? camp : null;
        }
    }
}
