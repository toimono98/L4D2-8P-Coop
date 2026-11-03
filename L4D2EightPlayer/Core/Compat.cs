using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    public enum Severity { Ok = 0, Info = 1, Warning = 2, Problem = 3 }

    public enum FixAction { None, DisablePlugin, EnablePlugin, DisableAddon, EnableAddon, OpenWorkshop, OpenUrl }

    public sealed class Finding
    {
        public string Kind;        // "Addon", "Plugin", "Extension", "Requirement", "Stability"
        public string Name;
        public string Location;    // full path, or empty
        public Severity Severity;
        public string Summary;
        public List<string> Details = new List<string>();
        public FixAction Fix;
        public string FixTarget;
        public bool Managed;       // installed by this tool
        public bool IsNew;         // appeared since the previous scan

        public void Raise(Severity s, string detail)
        {
            if (s > Severity) Severity = s;
            Details.Add(SeverityTag(s) + detail);
        }

        static string SeverityTag(Severity s) =>
            s == Severity.Problem ? "[Problem] " : s == Severity.Warning ? "[Warning] " : s == Severity.Info ? "[Info] " : "";
    }

    public sealed class ScanContext
    {
        public Manifest Manifest;
        public string Mode;              // "dedicated" | "listen"
        public string ClientGameDir;     // ...\Left 4 Dead 2\left4dead2
        public string ServerGameDir;     // ...\server\left4dead2 (dedicated) or same as client (listen)
        public string ServerRoot;        // folder holding srcds.exe / left4dead2.exe
        public bool SyncAddons;
        public string JoinMethod;        // "direct" | "lobby"
        public InstallState State;
    }

    /// <summary>Checks installed mods and plugins for problems with 8-player co-op.</summary>
    public static class Compat
    {
        public const string LobbyModId = "2754956355";

        // Cache parsed VPKs: big campaign archives are slow to re-read on every scan.
        static readonly Dictionary<string, (long size, DateTime mtime, AddonInfo info)> VpkCache = new Dictionary<string, (long, DateTime, AddonInfo)>(StringComparer.OrdinalIgnoreCase);

        public sealed class AddonInfo
        {
            public string Title;
            public string Error;
            public bool Campaign, Gameplay, Mutation, Talker, Ui, SurvivorModels;
            /// <summary>Weapon or melee scripts (scripts/weapon_*, scripts/melee/*): stats, or the model a skin points at.</summary>
            public bool WeaponScripts;
            /// <summary>Replaces models or materials (a skin, or part of a bigger mod).</summary>
            public bool Models;
            public bool Sounds, Scripts;
            public List<string> Files = new List<string>();
            public List<(Severity sev, string text)> ScriptNotes = new List<(Severity, string)>();
            public List<string> Missions = new List<string>();
            /// <summary>
            /// Copied to the dedicated server. A skin isn't, even with a small script of its own: skins show only on
            /// each player's screen, and with its textures on the server, joining stalls on a black screen while
            /// the game asks the server for files it already has.
            /// </summary>
            public bool NeedsServer => Campaign || Mutation || Talker || (Gameplay && !IsSkin);
            public bool IsSkin => Models && Skins.Coverage(Files).Count > 0;
        }

        static readonly Regex LoopTo4 = new Regex(@"\bfor\s*\(\s*local\s+\w+\s*=\s*[01]\s*;\s*\w+\s*<=?\s*4\s*;", RegexOptions.Compiled);
        static readonly Regex SurvivorLimit = new Regex(@"survivor_limit", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex MaxPlayersCvar = new Regex(@"\bsv_(maxplayers|setmax|visiblemaxplayers)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ModeMaxPlayers = new Regex("\"maxplayers\"\\s+\"?(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static AddonInfo ReadAddon(string path)
        {
            var fi = new FileInfo(path);
            lock (VpkCache)   // the Skins tab reads add-ons in the background while the Mods tab may scan
                if (VpkCache.TryGetValue(path, out var c) && c.size == fi.Length && c.mtime == fi.LastWriteTimeUtc) return c.info;

            var info = new AddonInfo();
            try
            {
                var vpk = Vpk.Open(path);
                info.Files = vpk.Entries.Keys.ToList();

                var addoninfo = vpk.ReadText("addoninfo.txt", 64 * 1024);
                if (addoninfo != null)
                {
                    try
                    {
                        var kv = Vdf.Parse(addoninfo);
                        var root = kv.Values.OfType<Dictionary<string, object>>().FirstOrDefault() ?? kv;
                        info.Title = root.Value("addontitle");
                    }
                    catch { }
                }

                int scriptsRead = 0;
                foreach (var f in info.Files)
                {
                    if (f.StartsWith("missions/") && f.EndsWith(".txt")) { info.Campaign = true; info.Missions.Add(Path.GetFileNameWithoutExtension(f)); }
                    else if (f.StartsWith("maps/") && f.EndsWith(".bsp")) info.Campaign = true;
                    else if (f.StartsWith("modes/") && f.EndsWith(".txt")) info.Mutation = true;
                    else if (f.StartsWith("scripts/talker/")) info.Talker = true;
                    else if (f.StartsWith("scripts/weapon_") || f.StartsWith("scripts/melee/")) info.WeaponScripts = true;
                    else if (f.StartsWith("scripts/vscripts/") || f == "scripts/population.txt" || f.StartsWith("scripts/survival") || f.StartsWith("scripts/itemstats"))
                    { info.Gameplay = true; if (f.StartsWith("scripts/vscripts/")) info.Scripts = true; }
                    else if (f.StartsWith("resource/ui/") || f.StartsWith("resource/") || f == "scripts/hudlayout.res") info.Ui = true;
                    else if (f.StartsWith("models/survivors/survivor_")) info.SurvivorModels = true;
                    if (f.StartsWith("models/") || f.StartsWith("materials/models/")) info.Models = true;
                    if (f.StartsWith("sound/")) info.Sounds = true;

                    if (f.EndsWith(".nut") && scriptsRead < 60)
                    {
                        var text = vpk.ReadText(f, 512 * 1024);
                        if (text == null) continue;
                        scriptsRead++;
                        var name = f.Substring(f.LastIndexOf('/') + 1);
                        if (SurvivorLimit.IsMatch(text))
                            info.ScriptNotes.Add((Severity.Warning, $"{name} changes survivor_limit. l4dmultislots manages that value and says it must not be modified."));
                        if (MaxPlayersCvar.IsMatch(text))
                            info.ScriptNotes.Add((Severity.Warning, $"{name} changes the server's player limit cvars, which can undo the 8-player setup."));
                        if (LoopTo4.IsMatch(text))
                            info.ScriptNotes.Add((Severity.Info, $"{name} loops over exactly 4 players. Survivors 5-8 may be ignored by this script."));
                    }

                    if (f.StartsWith("modes/") && f.EndsWith(".txt"))
                    {
                        var text = vpk.ReadText(f, 64 * 1024);
                        var m = text == null ? null : ModeMaxPlayers.Match(text);
                        if (m != null && m.Success && int.TryParse(m.Groups[1].Value, out int mp) && mp < 8)
                            info.ScriptNotes.Add((Severity.Info, $"Mutation '{Path.GetFileNameWithoutExtension(f)}' declares maxplayers {mp}; its lobby will cap at {mp}."));
                    }
                }
            }
            catch (Exception ex) { info.Error = ex.Message; }
            // Weapon scripts alone are a gameplay mod (stats) and belong on the server too. Next to models they're
            // a skin pointing the weapon at its new model: cosmetic, and not copied to the server.
            if (info.WeaponScripts && !info.Models) info.Gameplay = true;

            lock (VpkCache) VpkCache[path] = (fi.Length, fi.LastWriteTimeUtc, info);
            return info;
        }

        // ------------------------------------------------------------------ addonlist.txt

        /// <summary>Reads left4dead2\addonlist.txt: key ("workshop\123.vpk" or "name.vpk") -> enabled.</summary>
        public static Dictionary<string, bool> ReadAddonList(string gameDir)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(gameDir, "addonlist.txt");
            if (!File.Exists(path)) return result;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), "\"([^\"]+\\.vpk)\"\\s+\"(\\d)\"", RegexOptions.IgnoreCase))
                result[m.Groups[1].Value.Replace('/', '\\').Replace("\\\\", "\\")] = m.Groups[2].Value != "0";
            return result;
        }

        /// <summary>
        /// Add-ons the skin picker never turns off and doesn't count as skins: the 8-Player HUD add-on, the 8 Slots
        /// Lobby Mod and the component list's Workshop requirements.
        /// </summary>
        public static bool IsProtected(string vpkPath, Manifest manifest)
        {
            var file = Path.GetFileName(vpkPath);
            var id = Path.GetFileNameWithoutExtension(vpkPath);
            return string.Equals(file, HudAddon.FileName, StringComparison.OrdinalIgnoreCase) || id == LobbyModId
                || manifest?.workshop?.Any(w => w.id == id) == true;
        }

        public static string AddonListKey(string gameDir, string vpkPath)
        {
            var rel = Paths.Rel(Path.Combine(gameDir, "addons"), vpkPath).Replace('/', '\\');
            return rel;
        }

        // ------------------------------------------------------------------ scan

        public static List<Finding> Scan(ScanContext ctx)
        {
            var findings = new List<Finding>();
            var managedFiles = new HashSet<string>(
                ctx.State?.components.Values.SelectMany(c => c.files).Select(f => f.path) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            ScanRequirements(ctx, findings);
            ScanAddons(ctx, findings);
            if (ctx.ServerGameDir != null && Directory.Exists(ctx.ServerGameDir))
            {
                ScanPlugins(ctx, managedFiles, findings);
                ScanExtensions(ctx, findings);
                ScanLogs(ctx, findings);
            }
            return findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Kind).ThenBy(f => f.Name).ToList();
        }

        static void ScanRequirements(ScanContext ctx, List<Finding> findings)
        {
            if (ctx.ClientGameDir == null) return;
            var list = ReadAddonList(ctx.ClientGameDir);
            foreach (var w in ctx.Manifest.workshop ?? new List<WorkshopItem>())
            {
                var vpk = Path.Combine(ctx.ClientGameDir, "addons", "workshop", w.id + ".vpk");
                bool present = File.Exists(vpk);
                bool enabled = present && (!list.TryGetValue("workshop\\" + w.id + ".vpk", out var on) || on);
                var f = new Finding
                {
                    Kind = "Requirement",
                    Name = w.name + " (Workshop)",
                    Location = present ? vpk : "",
                    Fix = FixAction.OpenWorkshop,
                    FixTarget = w.id,
                    Summary = present ? (enabled ? "Subscribed and enabled" : "Subscribed but disabled in the Add-ons menu") : "Not subscribed",
                };
                if (!present || !enabled)
                {
                    bool needed = w.role == "required-host" && (ctx.JoinMethod == "lobby" || ctx.Mode == "listen");
                    f.Raise(needed ? Severity.Warning : Severity.Info, w.why);
                }
                else f.Details.Add(w.why);
                findings.Add(f);
            }

            if (ctx.Mode == "dedicated" && ctx.ServerRoot != null)
            {
                var cv = GameLocator.PatchVersion(Path.GetDirectoryName(ctx.ClientGameDir));
                var sv = GameLocator.PatchVersion(ctx.ServerRoot);
                if (cv != null && sv != null)
                {
                    var f = new Finding { Kind = "Requirement", Name = "Game / server version", Summary = $"Game {cv}, server {sv}" };
                    if (cv != sv) f.Raise(Severity.Problem, "The dedicated server is a different version from your game, so clients will be refused. Click 'Update server' on the Setup tab.");
                    findings.Add(f);
                }
            }
        }

        static void ScanAddons(ScanContext ctx, List<Finding> findings)
        {
            var addonsByFile = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
            var infoByFinding = new Dictionary<Finding, AddonInfo>();
            var lobbyFinding = (Finding)null;

            void Visit(string vpk, bool clientSide, bool enabled)
            {
                var info = ReadAddon(vpk);
                var id = Path.GetFileNameWithoutExtension(vpk);
                var f = new Finding
                {
                    Kind = "Addon",
                    Name = info.Title ?? id,
                    Location = vpk,
                    Fix = clientSide ? (enabled ? FixAction.DisableAddon : FixAction.EnableAddon) : FixAction.DisableAddon,
                    FixTarget = vpk,
                };
                if (info.Title != null && info.Title != id) f.Name += $"  [{id}]";

                var kinds = new List<string>();
                if (info.Campaign) kinds.Add("campaign");
                if (info.Mutation) kinds.Add("mutation");
                if (info.Gameplay) kinds.Add("script/gameplay");
                if (info.Talker) kinds.Add("dialogue");
                if (info.SurvivorModels) kinds.Add("survivor model");
                if (info.Ui) kinds.Add("UI");
                if (kinds.Count == 0) kinds.Add("cosmetic");
                f.Summary = (clientSide ? "" : "Server: ") + string.Join(", ", kinds) + (enabled ? "" : " (disabled)");

                if (info.Error != null) f.Raise(Severity.Problem, "Could not read this VPK (" + info.Error + "). It may be corrupt; re-subscribe or delete it.");
                if (id == LobbyModId) lobbyFinding = f;
                if (clientSide && string.Equals(Path.GetFileName(vpk), HudAddon.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    f.Managed = true;
                    f.Details.Add("The 8-Player HUD add-on, installed by this tool. It restyles the game's scripted HUD for the 8-Player Survivor HUD plugin; other HUD mods that change the same files will fight with it.");
                }

                if (enabled)
                {
                    foreach (var (sev, text) in info.ScriptNotes) f.Raise(sev, text);
                    if (info.SurvivorModels)
                        f.Details.Add("Survivor model replacement. Fine with 8 players: precache_all_survivors is enabled by this tool.");

                    // Host-only workshop items (the lobby mod) are client UI; the server never needs them.
                    bool hostOnly = ctx.Manifest.workshop?.Any(w => w.id == id && w.role == "required-host") == true;
                    if (clientSide && ctx.Mode == "dedicated" && info.NeedsServer && !hostOnly)
                    {
                        if (ctx.SyncAddons) f.Details.Add("Copied to the dedicated server automatically at launch (it changes server-side content).");
                        else f.Raise(Severity.Warning, "This addon changes server-side content but is not on the dedicated server. Turn on 'Copy my server-side addons to the server' on the Play tab.");
                    }
                    else if (clientSide && ctx.Mode == "dedicated" && info.Gameplay && info.IsSkin)
                        f.Details.Add("A skin with a script of its own: it stays on your PC and isn't copied to the dedicated server (skins show only on each player's screen).");

                    foreach (var file in info.Files)
                    {
                        if (file.StartsWith("addoninfo.") || file.StartsWith("addonimage")) continue;
                        if (!addonsByFile.TryGetValue(file, out var l)) addonsByFile[file] = l = new List<Finding>();
                        if (!l.Contains(f)) l.Add(f);
                    }
                }
                infoByFinding[f] = info;
                findings.Add(f);
            }

            if (ctx.ClientGameDir != null)
            {
                var list = ReadAddonList(ctx.ClientGameDir);
                var addons = Path.Combine(ctx.ClientGameDir, "addons");
                var vpks = new List<string>();
                if (Directory.Exists(addons)) vpks.AddRange(Directory.GetFiles(addons, "*.vpk"));
                var ws = Path.Combine(addons, "workshop");
                if (Directory.Exists(ws)) vpks.AddRange(Directory.GetFiles(ws, "*.vpk"));
                foreach (var vpk in vpks)
                {
                    var key = AddonListKey(ctx.ClientGameDir, vpk);
                    bool enabled = !list.TryGetValue(key, out var on) || on;
                    Visit(vpk, clientSide: true, enabled: enabled);
                }
            }

            // Server-only addons the user placed on the dedicated server themselves.
            if (ctx.Mode == "dedicated" && ctx.ServerGameDir != null)
            {
                var sAddons = Path.Combine(ctx.ServerGameDir, "addons");
                var synced = new HashSet<string>(ctx.State?.synced.Select(s => s.path) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(sAddons))
                    foreach (var vpk in Directory.GetFiles(sAddons, "*.vpk"))
                        if (!synced.Contains(Paths.Rel(ctx.ServerGameDir, vpk))) Visit(vpk, clientSide: false, enabled: true);
            }

            // Overlapping files: only one addon's copy is used by the game.
            var pairs = new Dictionary<(Finding, Finding), List<string>>();
            foreach (var kv in addonsByFile.Where(k => k.Value.Count > 1))
                for (int i = 0; i < kv.Value.Count; i++)
                    for (int j = 0; j < kv.Value.Count; j++)
                        if (i != j)
                        {
                            var key = (kv.Value[i], kv.Value[j]);
                            if (!pairs.TryGetValue(key, out var l)) pairs[key] = l = new List<string>();
                            l.Add(kv.Key);
                        }

            foreach (var kv in pairs)
            {
                var (a, b) = kv.Key;
                var files = kv.Value;
                bool scripts = files.Any(p => p.StartsWith("scripts/vscripts/") || p.StartsWith("modes/") || p.StartsWith("missions/"));
                bool lobby = a == lobbyFinding || b == lobbyFinding;
                var sev = lobby && files.Any(p => p.StartsWith("resource/")) ? Severity.Problem : scripts ? Severity.Warning : Severity.Info;
                var sample = string.Join(", ", files.Take(3)) + (files.Count > 3 ? $" (+{files.Count - 3} more)" : "");
                var why = !(lobby && sev == Severity.Problem) ? (scripts ? "only one of the scripts will run" : "only one version will be used")
                    : a == lobbyFinding ? $"both replace the lobby screen, so only one wins. Disable {b.Name} and keep this one"
                    : "it replaces the lobby UI files the 8 Slots Lobby Mod needs, so the lobby may fall back to 4 slots. Disable this one";
                a.Raise(sev, $"Overlaps with {b.Name}: {sample}. {Cap(why)}.");
            }
        }

        static void ScanPlugins(ScanContext ctx, HashSet<string> managedFiles, List<Finding> findings)
        {
            var dir = Path.Combine(ctx.ServerGameDir, "addons", "sourcemod", "plugins");
            if (!Directory.Exists(dir)) return;
            var know = ctx.Manifest.knowledge ?? new Knowledge();
            var enabledFiles = new HashSet<string>(Directory.GetFiles(dir, "*.smx").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            bool multislots = enabledFiles.Contains("l4dmultislots.smx");
            var byName = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
            var extDir = Path.Combine(ctx.ServerGameDir, "addons", "sourcemod", "extensions");

            foreach (var path in Directory.GetFiles(dir, "*.smx"))
            {
                var file = Path.GetFileName(path);
                var rel = Paths.Rel(ctx.ServerGameDir, path);
                var f = new Finding { Kind = "Plugin", Location = path, Name = file, Fix = FixAction.DisablePlugin, FixTarget = path, Managed = managedFiles.Contains(rel) };
                Smx smx = null;
                try { smx = Smx.Open(path); }
                catch (Exception ex) { f.Raise(Severity.Problem, "Not a valid compiled plugin (" + ex.Message + ")."); }

                if (smx != null)
                {
                    f.Name = string.IsNullOrWhiteSpace(smx.Name) ? file : $"{smx.Name}  [{file}]";
                    f.Summary = (f.Managed ? "Installed by this tool" : "Your plugin") + (string.IsNullOrWhiteSpace(smx.Version) ? "" : " · v" + smx.Version) + (string.IsNullOrWhiteSpace(smx.Author) ? "" : " · " + smx.Author);
                    if (!string.IsNullOrWhiteSpace(smx.Name))
                    {
                        if (!byName.TryGetValue(smx.Name, out var l)) byName[smx.Name] = l = new List<Finding>();
                        l.Add(f);
                    }

                    // Known conflicts / broken plugins (never flag our own components).
                    if (!f.Managed)
                    {
                        var stem = Path.GetFileNameWithoutExtension(file);
                        foreach (var rule in know.conflictingPlugins)
                        {
                            var rx = new Regex(rule.match, RegexOptions.IgnoreCase);
                            if (rx.IsMatch(stem) || (smx.Name != null && rx.IsMatch(smx.Name)))
                            {
                                var sev = ParseSeverity(rule.severity);
                                if (!multislots && sev == Severity.Problem) sev = Severity.Warning;
                                f.Raise(sev, rule.reason);
                            }
                        }
                        if (smx.ContainsString("survivor_limit") && multislots)
                            f.Raise(Severity.Warning, "Contains the cvar name survivor_limit. If this plugin changes it, it will fight l4dmultislots over survivor slots.");
                    }

                    // Dependencies.
                    foreach (var lib in smx.Libraries.Where(l => l.Required))
                    {
                        string provider = know.libraryProviders != null && know.libraryProviders.TryGetValue(lib.Name ?? "", out var p) ? p : null;
                        bool ok = provider != null ? enabledFiles.Contains(provider)
                                                   : enabledFiles.Contains((lib.Name ?? "") + ".smx") || enabledFiles.Contains((lib.File ?? "") + ".smx");
                        if (!ok)
                            f.Raise(provider != null ? Severity.Problem : Severity.Warning,
                                $"Requires the plugin library '{lib.Name}'" + (provider != null ? $" ({provider}), which is not installed or is disabled." : ". Make sure the plugin that provides it is installed."));
                    }
                    foreach (var ext in smx.Extensions.Where(e => e.Required && !string.IsNullOrEmpty(e.File)))
                    {
                        var stem = ext.File.EndsWith(".ext") ? ext.File : ext.File.Replace(".ext.dll", ".ext").Replace(".dll", "");
                        bool found = Directory.Exists(extDir) && Directory.GetFiles(extDir, stem + "*.dll").Any();
                        if (!found) f.Raise(Severity.Problem, $"Requires the '{ext.Name}' extension ({ext.File}), which is not installed.");
                    }
                    if (know.nativePrefixes != null && smx.Libraries.All(l => l.Name != "left4dhooks"))
                    {
                        var needs = know.nativePrefixes.Where(kv => smx.Natives.Any(n => n.StartsWith(kv.Key))).Select(kv => kv.Value).Distinct();
                        foreach (var provider in needs.Where(p => !enabledFiles.Contains(p)))
                            f.Raise(Severity.Problem, $"Calls functions from {provider}, which is not installed.");
                    }
                }
                // Stock SourceMod plugins are not interesting unless something is wrong with them.
                if (f.Managed && smx?.Author == "AlliedModders LLC" && f.Severity == Severity.Ok) continue;
                findings.Add(f);
            }

            foreach (var kv in byName.Where(k => k.Value.Count > 1))
                foreach (var f in kv.Value)
                    f.Raise(Severity.Warning, $"'{kv.Key}' is loaded {kv.Value.Count} times: " + string.Join(", ", kv.Value.Select(x => Path.GetFileName(x.Location))) + ". Disable the extra copies.");

            var disabledDir = Path.Combine(dir, "disabled");
            if (Directory.Exists(disabledDir))
                foreach (var path in Directory.GetFiles(disabledDir, "*.smx").Where(p => !managedFiles.Contains(Paths.Rel(ctx.ServerGameDir, p))))
                    findings.Add(new Finding { Kind = "Plugin", Name = Path.GetFileName(path), Location = path, Summary = "Disabled", Fix = FixAction.EnablePlugin, FixTarget = path });
        }

        static void ScanExtensions(ScanContext ctx, List<Finding> findings)
        {
            var know = ctx.Manifest.knowledge ?? new Knowledge();
            var roots = new[]
            {
                Path.Combine(ctx.ServerGameDir, "addons", "sourcemod", "extensions"),
                Path.Combine(ctx.ServerGameDir, "addons"),
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileName(file);
                    foreach (var rule in know.conflictingExtensions)
                    {
                        bool hit = Regex.IsMatch(name, rule.match, RegexOptions.IgnoreCase);
                        if (!hit && name.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase))
                        {
                            try { hit = Regex.IsMatch(File.ReadAllText(file), rule.match, RegexOptions.IgnoreCase); } catch { }
                        }
                        if (hit && !findings.Any(f => f.Location == file))
                        {
                            var f = new Finding { Kind = "Extension", Name = name, Location = file, Summary = "Server extension" };
                            f.Raise(ParseSeverity(rule.severity), rule.reason + " Remove it from the server's addons folder.");
                            findings.Add(f);
                        }
                    }
                }
            }
        }

        static readonly Regex Blame = new Regex(@"Blaming:\s*(\S+?\.smx)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex LoadFail = new Regex("(?:Unable to load plugin|Failed to load plugin|Plugin)\\s*\"?([^\"\\s:]+\\.smx)\"?\\s*(?:failed to load)?\\s*:?\\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex Tagged = new Regex(@"\[([\w\-./]+\.smx)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>The timestamp SourceMod puts on each log line: "L 10/23/2026 - 18:08:21: ...".</summary>
        static DateTime? LogTime(string line)
        {
            var m = Regex.Match(line, @"^L (\d\d/\d\d/\d{4} - \d\d:\d\d:\d\d)");
            return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "MM/dd/yyyy - HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (DateTime?)null;
        }

        static void ScanLogs(ScanContext ctx, List<Finding> findings)
        {
            var logDir = Path.Combine(ctx.ServerGameDir, "addons", "sourcemod", "logs");
            var since = DateTime.Now.AddDays(-7);
            var errors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var loadFailures = new Dictionary<string, (string why, DateTime? when)>(StringComparer.OrdinalIgnoreCase);
            DateTime? lastSession = null; // newest "error session started": the server ran then

            if (Directory.Exists(logDir))
            {
                foreach (var log in Directory.GetFiles(logDir, "errors_*.log").Where(p => File.GetLastWriteTime(p) >= since))
                {
                    string[] lines;
                    try { lines = ReadShared(log).Split('\n'); } catch { continue; }
                    foreach (var line in lines)
                    {
                        Match m;
                        if (line.Contains("error session started"))
                        {
                            var t = LogTime(line);
                            if (t != null && (lastSession == null || t > lastSession)) lastSession = t;
                        }
                        else if ((m = LoadFail.Match(line)).Success && (line.Contains("Unable to load") || line.Contains("Failed to load") || line.Contains("failed to load")))
                            loadFailures[Path.GetFileName(m.Groups[1].Value)] = (m.Groups[2].Value.Trim(), LogTime(line));
                        else if ((m = Blame.Match(line)).Success)
                            Bump(errors, Path.GetFileName(m.Groups[1].Value));
                        else if ((m = Tagged.Match(line)).Success)
                            Bump(errors, Path.GetFileName(m.Groups[1].Value));
                    }
                }
            }

            // A failure logged before the last install/update may already be fixed (a dependency was replaced),
            // and SourceMod logs nothing when a plugin loads fine, so only say so until the server runs again.
            DateTime? lastInstall = null;
            try
            {
                lastInstall = InstallState.Load(ctx.ServerGameDir)?.components.Values
                    .Select(c => DateTime.TryParse(c.installedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (DateTime?)null)
                    .Max();
            }
            catch { }

            // Disabled plugins aren't loaded, so old load failures no longer apply to them.
            foreach (var f in findings.Where(x => x.Kind == "Plugin" && x.Location != null && x.Fix != FixAction.EnablePlugin))
            {
                var file = Path.GetFileName(f.Location);
                if (loadFailures.TryGetValue(file, out var fail))
                {
                    var why = fail.why.Length > 0 ? fail.why : "see the SourceMod error log";
                    bool stale = fail.when != null && lastInstall != null && fail.when < lastInstall;
                    if (stale && lastSession > lastInstall) { } // ran again since the fix and didn't fail: it loads now
                    else if (stale)
                        f.Raise(Severity.Info, $"Failed to load on {fail.when:g} ({why}), before the last install/update on {lastInstall:g}. Start the server to re-check.");
                    else
                        f.Raise(Severity.Problem, "Failed to load on the server in the last 7 days: " + why + ". Usually outdated gamedata or a missing dependency.");
                }
                if (errors.TryGetValue(file, out var n))
                    f.Raise(n >= 20 ? Severity.Warning : Severity.Info, $"Logged {n} runtime error(s) in the last 7 days (addons/sourcemod/logs/errors_*.log).");
            }

            if (ctx.ServerRoot != null && Directory.Exists(ctx.ServerRoot))
            {
                var dumps = Directory.GetFiles(ctx.ServerRoot, "*.mdmp").Select(p => new FileInfo(p)).Where(fi => fi.LastWriteTime >= since).ToList();
                if (dumps.Count > 0)
                {
                    var f = new Finding { Kind = "Stability", Name = "Server crashes", Location = ctx.ServerRoot, Summary = $"{dumps.Count} crash dump(s) in the last 7 days" };
                    f.Raise(dumps.Count >= 3 ? Severity.Problem : Severity.Warning,
                        $"Latest crash: {dumps.Max(d => d.LastWriteTime):g}. Crashes right after a game update are usually outdated plugin gamedata, so run Install / Update.");
                    findings.Add(f);
                }
            }
        }

        // ------------------------------------------------------------------ fixes

        public static string Apply(Finding f, string clientGameDir)
        {
            switch (f.Fix)
            {
                case FixAction.DisablePlugin:
                {
                    var dest = Path.Combine(Path.GetDirectoryName(f.FixTarget), "disabled", Path.GetFileName(f.FixTarget));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(f.FixTarget, dest);
                    return $"Moved {Path.GetFileName(f.FixTarget)} to plugins\\disabled.";
                }
                case FixAction.EnablePlugin:
                {
                    var dest = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(f.FixTarget)), Path.GetFileName(f.FixTarget));
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(f.FixTarget, dest);
                    return $"Re-enabled {Path.GetFileName(f.FixTarget)}.";
                }
                case FixAction.DisableAddon:
                case FixAction.EnableAddon:
                {
                    bool enable = f.Fix == FixAction.EnableAddon;
                    if (clientGameDir != null && f.FixTarget.StartsWith(Path.Combine(clientGameDir, "addons"), StringComparison.OrdinalIgnoreCase))
                    {
                        if (Process.GetProcessesByName("left4dead2").Length > 0)
                            throw new InvalidOperationException("Close Left 4 Dead 2 first. The game rewrites addonlist.txt when it exits.");
                        SetAddonEnabled(clientGameDir, AddonListKey(clientGameDir, f.FixTarget), enable);
                        return (enable ? "Enabled " : "Disabled ") + f.Name + " in addonlist.txt.";
                    }
                    File.Move(f.FixTarget, f.FixTarget + ".disabled");
                    return $"Renamed {Path.GetFileName(f.FixTarget)} to .vpk.disabled on the server.";
                }
                case FixAction.OpenWorkshop:
                    Process.Start("steam://url/CommunityFilePage/" + f.FixTarget);
                    return "Opened the Workshop page in Steam.";
                case FixAction.OpenUrl:
                    Process.Start(f.FixTarget);
                    return "Opened " + f.FixTarget;
            }
            return null;
        }

        /// <summary>Several add-ons on or off in one write (keys as AddonListKey gives them).</summary>
        public static void SetAddonsEnabled(string gameDir, IDictionary<string, bool> changes)
        {
            if (changes.Count == 0) return;
            var path = Path.Combine(gameDir, "addonlist.txt");
            var backup = Path.Combine(InstallState.Dir(gameDir), "addonlist.txt.original");
            string text = File.Exists(path) ? File.ReadAllText(path) : "\"AddonList\"\n{\n}\n";
            if (File.Exists(path) && !File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(path, backup);
            }

            foreach (var kv in changes)
            {
                var value = kv.Value ? "1" : "0";
                var keyPattern = Regex.Escape(kv.Key).Replace("\\\\", "[\\\\/]{1,2}");
                var rx = new Regex("(\"" + keyPattern + "\"\\s+\")(\\d)(\")", RegexOptions.IgnoreCase);
                if (rx.IsMatch(text)) text = rx.Replace(text, "${1}" + value + "${3}");
                else
                {
                    int close = text.LastIndexOf('}');
                    var line = "\t\"" + kv.Key + "\"\t\t\"" + value + "\"\n";
                    text = close >= 0 ? text.Substring(0, close) + line + text.Substring(close) : text + line;
                }
            }
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static void SetAddonEnabled(string gameDir, string key, bool enabled) =>
            SetAddonsEnabled(gameDir, new Dictionary<string, bool> { [key] = enabled });

        // ------------------------------------------------------------------ helpers

        static Severity ParseSeverity(string s) =>
            s == "problem" ? Severity.Problem : s == "warning" ? Severity.Warning : s == "info" ? Severity.Info : Severity.Ok;

        static void Bump(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs))
                return r.ReadToEnd();
        }
    }
}
