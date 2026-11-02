using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    public sealed class Manifest
    {
        public int manifestVersion { get; set; }
        public string updated { get; set; }
        public string notes { get; set; }
        public List<Component> components { get; set; } = new List<Component>();
        public List<ManualItem> manual { get; set; } = new List<ManualItem>();
        public List<WorkshopItem> workshop { get; set; } = new List<WorkshopItem>();
        public Knowledge knowledge { get; set; } = new Knowledge();

        /// <summary>Where this manifest came from, for display.</summary>
        public string Source { get; set; } = "built-in";

        public Component Get(string id) => components.FirstOrDefault(c => c.id == id);

        /// <summary>Loads, in order of preference: a remote URL, components.json beside the exe, the built-in copy.</summary>
        public static async Task<Manifest> Load(string remoteUrl, ILog log)
        {
            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                try
                {
                    var m = Json.Parse<Manifest>(await Http.GetString(remoteUrl.Trim()));
                    if (m?.components?.Count > 0) { m.Source = remoteUrl.Trim(); return m; }
                }
                catch (Exception ex) { log.Warn("Could not load the component list from " + remoteUrl + ": " + ex.Message + ". Using the local copy."); }
            }

            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "components.json");
            if (File.Exists(local))
            {
                try
                {
                    var m = Json.Parse<Manifest>(File.ReadAllText(local));
                    if (m?.components?.Count > 0) { m.Source = local; return m; }
                }
                catch (Exception ex) { log.Warn("components.json next to the exe is invalid (" + ex.Message + "). Using the built-in list."); }
            }

            return BuiltIn();
        }

        public static Manifest BuiltIn()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("components.json"))
            using (var r = new StreamReader(s))
                return Json.Parse<Manifest>(r.ReadToEnd());
        }

        /// <summary>The ids plus everything they depend on, in install order.</summary>
        public List<Component> Resolve(IEnumerable<string> ids, string mode)
        {
            var order = new List<Component>();
            var seen = new HashSet<string>();
            void Visit(string id)
            {
                if (!seen.Add(id)) return;
                var c = Get(id);
                if (c == null || !c.AppliesTo(mode)) return;
                foreach (var dep in c.requires ?? new List<string>()) Visit(dep);
                order.Add(c);
            }
            foreach (var id in ids) Visit(id);
            return order;
        }
    }

    public sealed class Component
    {
        public string id { get; set; }
        public string name { get; set; }
        public string group { get; set; }
        public bool required { get; set; }
        public bool @default { get; set; }
        public string description { get; set; }
        public List<string> requires { get; set; } = new List<string>();
        public List<string> modes { get; set; }
        public ArchiveSource archive { get; set; }
        public List<ExtractRule> extract { get; set; } = new List<ExtractRule>();
        public List<string> keepExisting { get; set; } = new List<string>();
        public string detect { get; set; }
        public List<string> toggleFiles { get; set; } = new List<string>();
        public string @base { get; set; }
        public List<List<string>> files { get; set; } = new List<List<string>>();
        public string includesBase { get; set; }
        public List<string> includes { get; set; } = new List<string>();
        public List<string> compile { get; set; } = new List<string>();
        /// <summary>Kept switched off while the installed SourceMod build is older than this (0 = never).</summary>
        public int pauseUntilSourceMod { get; set; }
        /// <summary>Goes into the player's own game rather than the server: "hud-addon" (Core\HudAddon.cs).</summary>
        public string client { get; set; }

        public bool AppliesTo(string mode) => modes == null || modes.Count == 0 || modes.Contains(mode);

        /// <summary>Relative paths this component puts into the game folder (known without downloading).</summary>
        public IEnumerable<string> KnownTargets()
        {
            foreach (var f in files ?? new List<List<string>>()) if (f.Count >= 2) yield return f[1];
            foreach (var src in compile ?? new List<string>())
                yield return "addons/sourcemod/plugins/" + Path.GetFileNameWithoutExtension(src) + ".smx";
            foreach (var e in extract ?? new List<ExtractRule>())
                if (!e.from.EndsWith("/")) yield return e.to;
        }

        public override string ToString() => name;
    }

    public sealed class ArchiveSource
    {
        public string type { get; set; }      // "alliedmods" | "github-release" | "url"
        public string latest { get; set; }
        public string @base { get; set; }
        public string repo { get; set; }
        public string asset { get; set; }
        public string fallback { get; set; }
        /// <summary>Pin a github-release to this tag (downloads <see cref="fallback"/>); for builds that must match the SourceMod branch.</summary>
        public string tag { get; set; }
        /// <summary>Builds to skip (matched against the resolved file name); <see cref="fallback"/> is used instead.</summary>
        public List<string> avoid { get; set; } = new List<string>();
        public string url { get; set; }
    }

    public sealed class ExtractRule
    {
        public string from { get; set; }
        public string to { get; set; }
    }

    public sealed class ManualItem
    {
        public string name { get; set; }
        public string url { get; set; }
        public string why { get; set; }
    }

    public sealed class WorkshopItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public string role { get; set; }
        public string why { get; set; }
    }

    public sealed class Knowledge
    {
        public List<KnownRule> conflictingPlugins { get; set; } = new List<KnownRule>();
        public List<KnownRule> conflictingExtensions { get; set; } = new List<KnownRule>();
        public Dictionary<string, string> libraryProviders { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> nativePrefixes { get; set; } = new Dictionary<string, string>();
    }

    public sealed class KnownRule
    {
        public string match { get; set; }
        public string severity { get; set; }
        public string reason { get; set; }
    }
}
