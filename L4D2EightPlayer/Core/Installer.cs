using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    public sealed class FileRecord
    {
        public string path { get; set; }    // relative to the left4dead2 folder, forward slashes
        public string sha256 { get; set; }
        public string backup { get; set; }  // relative path of the file we replaced, if any
    }

    public sealed class ComponentState
    {
        public string version { get; set; }
        public string installedAt { get; set; }
        public List<FileRecord> files { get; set; } = new List<FileRecord>();
    }

    public sealed class SyncedAddon
    {
        public string path { get; set; }
        public string source { get; set; }
    }

    /// <summary>What this tool has put into one left4dead2 folder. Lives inside that folder.</summary>
    public sealed class InstallState
    {
        public Dictionary<string, ComponentState> components { get; set; } = new Dictionary<string, ComponentState>();
        public List<SyncedAddon> synced { get; set; } = new List<SyncedAddon>();

        public static string Dir(string gameDir) => Path.Combine(gameDir, "addons", "l4d2_8p_manager");
        public static string FilePath(string gameDir) => Path.Combine(Dir(gameDir), "state.json");
        public static InstallState Load(string gameDir) => Json.Load<InstallState>(FilePath(gameDir));
        public void Save(string gameDir) => Json.Save(FilePath(gameDir), this);

        public bool Owns(string rel) => components.Values.Any(c => c.files.Any(f => string.Equals(f.path, rel, StringComparison.OrdinalIgnoreCase)));
    }

    public enum ComponentStatus { NotInstalled, Installed, Modified, FoundUnmanaged, UpdateAvailable }

    /// <summary>
    /// Installs manifest components into a left4dead2 folder. Every file written is recorded with
    /// its hash; files we replace are backed up first so uninstalling puts them back.
    /// </summary>
    public sealed class Installer
    {
        readonly string gameDir;
        readonly ILog log;
        readonly InstallState state;
        readonly string backupRoot;

        public InstallState State => state;

        public Installer(string gameDir, ILog log)
        {
            this.gameDir = gameDir;
            this.log = log;
            state = InstallState.Load(gameDir);
            backupRoot = Path.Combine(InstallState.Dir(gameDir), "backups");
        }

        // ---------------------------------------------------------------- status

        public ComponentStatus Status(Component c)
        {
            if (state.components.TryGetValue(c.id, out var cs))
            {
                foreach (var f in cs.files)
                {
                    // Config files are meant to be edited; they do not count as tampering.
                    if ((c.keepExisting ?? new List<string>()).Any(k => f.path.StartsWith(k, StringComparison.OrdinalIgnoreCase))) continue;
                    var p = ExistingVariant(Paths.Join(gameDir, f.path));
                    if (p == null || Hash.File(p) != f.sha256) return ComponentStatus.Modified;
                }
                return ComponentStatus.Installed;
            }
            if (!string.IsNullOrEmpty(c.detect) && ExistingVariant(Paths.Join(gameDir, c.detect)) != null) return ComponentStatus.FoundUnmanaged;
            var targets = c.KnownTargets().ToList();
            if (targets.Count > 0 && targets.All(t => ExistingVariant(Paths.Join(gameDir, t)) != null)) return ComponentStatus.FoundUnmanaged;
            return ComponentStatus.NotInstalled;
        }

        public string InstalledVersion(Component c) => state.components.TryGetValue(c.id, out var cs) ? cs.version : null;

        /// <summary>
        /// For our own plugins (built from sources inside the exe): "built-in" plus a hash of those sources, so a
        /// newer app can tell they changed. Null for components that come from anywhere else.
        /// </summary>
        public static string BuiltInVersion(Component c)
        {
            var sources = (c.compile ?? new List<string>()).Concat((c.files ?? new List<List<string>>()).Where(f => f.Count > 0).Select(f => f[0])).ToList();
            if (c.archive != null || sources.Count == 0 || !sources.All(s => s.StartsWith("embedded:", StringComparison.OrdinalIgnoreCase))) return null;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (var s in sources)
                    using (var res = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("plugins/" + s.Substring("embedded:".Length)))
                    {
                        if (res == null) return null;
                        var bytes = new MemoryStream(); res.CopyTo(bytes);
                        sha.TransformBlock(bytes.ToArray(), 0, (int)bytes.Length, null, 0);
                    }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return "built-in " + BitConverter.ToString(sha.Hash, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Our own plugins installed here by an older version of the app.</summary>
        public List<Component> OutdatedBuiltIns(Manifest m) =>
            m.components.Where(c => state.components.ContainsKey(c.id) && BuiltInVersion(c) is string v && InstalledVersion(c) != v).ToList();

        /// <summary>Returns the file, or its ".disabled" twin (listen-mode toggle), or null.</summary>
        static string ExistingVariant(string full)
        {
            if (File.Exists(full)) return full;
            if (File.Exists(full + ".disabled")) return full + ".disabled";
            return null;
        }

        /// <summary>Finds the newest upstream version for archive components (null if not applicable).</summary>
        public static async Task<string> LatestVersion(Component c, CancellationToken ct)
        {
            if (c.archive == null) return null;
            switch (c.archive.type)
            {
                case "alliedmods":
                    var name = Http.FileNameFromUrl(await Http.ResolveAlliedModders(c.archive.latest, c.archive.@base, ct));
                    return IsAvoided(c.archive, name) ? Http.FileNameFromUrl(c.archive.fallback) : name;
                case "github-release":
                    if (!string.IsNullOrEmpty(c.archive.tag)) return c.archive.tag;
                    return (await Http.ResolveGitHubRelease(c.archive.repo, c.archive.asset, ct)).tag;
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- install

        public async Task Install(Component c, CancellationToken ct)
        {
            log.Info($"── {c.name}");
            // Client-side pieces (the HUD add-on) live in the player's game, not this folder: the app installs them.
            if (c.client != null) { log.Info("   installed into your own game by the app, not into this folder"); return; }
            var stage = Path.Combine(Paths.Cache, "stage", c.id);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);

            // relative destination -> staged file
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string version = null;

            if (c.archive != null)
            {
                var (url, ver) = await ResolveArchive(c.archive, ct);
                version = ver;
                var zipPath = Path.Combine(Paths.Cache, "downloads", Http.FileNameFromUrl(url));
                if (!File.Exists(zipPath) || c.archive.type == "github-release")
                {
                    log.Info("Downloading " + url);
                    await Http.Download(url, zipPath, log, ct);
                }
                else log.Info("Using cached " + Path.GetFileName(zipPath));
                ExtractArchive(zipPath, c.extract, stage, staged);

                if (c.id == "sourcemod") RefreshToolchain(zipPath);
            }

            var baseUrl = c.@base ?? "";
            foreach (var f in c.files ?? new List<List<string>>())
            {
                if (f.Count < 2) continue;
                var local = Paths.Join(stage, f[1]);
                if (f[0].StartsWith("embedded:", StringComparison.OrdinalIgnoreCase))
                    ExtractEmbedded("plugins/" + f[0].Substring("embedded:".Length), local);
                else
                    await Http.Download(baseUrl + f[0], local, log, ct);
                staged[f[1]] = local;
            }

            if ((c.includes?.Count ?? 0) > 0 || (c.compile?.Count ?? 0) > 0)
                await EnsureToolchain(ct);

            var incBase = c.includesBase ?? baseUrl;
            foreach (var inc in c.includes ?? new List<string>())
            {
                int i = inc.IndexOf("include/", StringComparison.OrdinalIgnoreCase);
                var rel = i >= 0 ? inc.Substring(i + "include/".Length) : inc;
                await Http.Download(incBase + inc, Paths.Join(Path.Combine(Paths.Toolchain, "extra"), rel), log, ct);
            }

            foreach (var src in c.compile ?? new List<string>())
            {
                var spLocal = Path.Combine(stage, "src", Path.GetFileName(src));
                if (src.StartsWith("embedded:", StringComparison.OrdinalIgnoreCase))
                    ExtractEmbedded("plugins/" + src.Substring("embedded:".Length), spLocal); // our own plugins ship inside the exe
                else
                    await Http.Download(baseUrl + src, spLocal, log, ct);
                var rel = "addons/sourcemod/plugins/" + Path.GetFileNameWithoutExtension(src) + ".smx";
                var outPath = Paths.Join(stage, rel);
                Compile(spLocal, outPath);
                staged[rel] = outPath;
            }

            if (version == null) version = BuiltInVersion(c) ?? "latest @ " + DateTime.Now.ToString("yyyy-MM-dd");
            Apply(c, staged, version);
            log.Info($"   installed {staged.Count} file(s), version {version}");
        }

        async Task<(string url, string version)> ResolveArchive(ArchiveSource a, CancellationToken ct)
        {
            switch (a.type)
            {
                case "alliedmods":
                    var url = await Http.ResolveAlliedModders(a.latest, a.@base, ct);
                    if (IsAvoided(a, Http.FileNameFromUrl(url)))
                    {
                        log.Warn($"{Http.FileNameFromUrl(url)} is a known-bad build; using {Http.FileNameFromUrl(a.fallback)} instead.");
                        url = a.fallback;
                    }
                    return (url, Http.FileNameFromUrl(url));
                case "github-release":
                    if (!string.IsNullOrEmpty(a.tag)) return (a.fallback, a.tag);
                    try
                    {
                        var (u, tag) = await Http.ResolveGitHubRelease(a.repo, a.asset, ct);
                        return (u, tag);
                    }
                    catch (Exception ex) when (!string.IsNullOrEmpty(a.fallback))
                    {
                        log.Warn($"GitHub lookup failed ({ex.Message}); using the pinned release instead.");
                        return (a.fallback, Http.FileNameFromUrl(a.fallback));
                    }
                default:
                    return (a.url, Http.FileNameFromUrl(a.url));
            }
        }

        /// <summary>
        /// SourceMod 1.12 builds up to git7258 crash L4D2 servers at a map load when a plugin hooks entity
        /// outputs (alliedmodders/sourcemod#2589). Components marked pauseUntilSourceMod are renamed to
        /// .smx.disabled (SourceMod skips them; status still reads Installed) while the installed SourceMod is
        /// older than that build, and renamed back once it isn't. Returns how many plugins are paused.
        /// </summary>
        public int ApplySourceModGuard(Manifest manifest)
        {
            var sm = manifest.Get("sourcemod");
            var m = Regex.Match(sm == null ? "" : InstalledVersion(sm) ?? "", @"git(\d+)");
            int build = m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue; // a SourceMod we didn't install: leave it alone
            int paused = 0;
            var names = new List<string>();
            foreach (var c in manifest.components.Where(x => x.pauseUntilSourceMod > 0 && state.components.ContainsKey(x.id)))
            {
                bool pause = build < c.pauseUntilSourceMod;
                foreach (var f in state.components[c.id].files.Where(f => f.path.EndsWith(".smx", StringComparison.OrdinalIgnoreCase)))
                {
                    var on = Paths.Join(gameDir, f.path);
                    var off = on + ".disabled";
                    try
                    {
                        if (pause && File.Exists(on)) { if (File.Exists(off)) File.Delete(off); File.Move(on, off); }
                        else if (!pause && File.Exists(off) && !File.Exists(on)) { File.Move(off, on); log.Info($"Switched {Path.GetFileName(on)} back on (SourceMod git{build})."); }
                    }
                    catch (IOException ex) { log.Warn($"{Path.GetFileName(on)}: {ex.Message}"); }
                    if (pause && File.Exists(off)) { paused++; names.Add(Path.GetFileNameWithoutExtension(on)); }
                }
            }
            if (paused > 0)
                log.Warn($"Paused {string.Join(", ", names)}: SourceMod git{build} crashes on map change while plugins hook entity outputs " +
                         "(alliedmodders/sourcemod#2589). They switch back on by themselves once a fixed SourceMod is installed.");
            return paused;
        }

        static bool IsAvoided(ArchiveSource a, string fileName) =>
            !string.IsNullOrEmpty(a.fallback) && (a.avoid ?? new List<string>()).Any(x => fileName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

        static void ExtractArchive(string zipPath, List<ExtractRule> rules, string stage, Dictionary<string, string> staged)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/") || entry.Length == 0 && entry.Name == "") continue;
                    var name = entry.FullName.Replace('\\', '/');
                    foreach (var r in rules)
                    {
                        string dest = null;
                        if (r.from.EndsWith("/"))
                        {
                            if (name.StartsWith(r.from, StringComparison.OrdinalIgnoreCase))
                                dest = r.to + name.Substring(r.from.Length);
                        }
                        else if (name.Equals(r.from, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + r.from, StringComparison.OrdinalIgnoreCase))
                            dest = r.to;
                        if (dest == null) continue;
                        // L4D2 Windows servers are 32-bit: skip Linux and 64-bit-only binaries.
                        if (dest.EndsWith(".so", StringComparison.OrdinalIgnoreCase)) break;
                        var local = Paths.Join(stage, dest);
                        Directory.CreateDirectory(Path.GetDirectoryName(local));
                        entry.ExtractToFile(local, true);
                        staged[dest] = local;
                        break;
                    }
                }
            }
        }

        void Apply(Component c, Dictionary<string, string> staged, string version)
        {
            state.components.TryGetValue(c.id, out var previous);
            var next = new ComponentState { version = version, installedAt = DateTime.Now.ToString("s") };
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            foreach (var kv in staged)
            {
                var rel = kv.Key;
                var dest = Paths.Join(gameDir, rel);
                var prevRec = previous?.files.FirstOrDefault(f => f.path.Equals(rel, StringComparison.OrdinalIgnoreCase));

                // Respect user configuration (admins, plugin configs, ...): only replace a config
                // file if it is still exactly the copy we installed last time.
                if (File.Exists(dest) && (c.keepExisting ?? new List<string>()).Any(k => rel.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                {
                    if (prevRec == null) continue;
                    if (Hash.File(dest) != prevRec.sha256) { next.files.Add(prevRec); continue; }
                }

                // A listen-mode toggle may have parked the file as ".disabled"; write back to whichever exists.
                if (!File.Exists(dest) && File.Exists(dest + ".disabled")) dest += ".disabled";

                string backup = prevRec?.backup;
                if (File.Exists(dest))
                {
                    var current = Hash.File(dest);
                    bool ours = prevRec != null && prevRec.sha256 == current;
                    bool identical = current == Hash.File(kv.Value);
                    if (!ours && !identical && backup == null)
                    {
                        backup = "backups/" + stamp + "/" + rel;
                        var bpath = Paths.Join(InstallState.Dir(gameDir), backup);
                        Directory.CreateDirectory(Path.GetDirectoryName(bpath));
                        File.Copy(dest, bpath, true);
                        log.Info("   backed up existing " + rel);
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(kv.Value, dest, true);
                next.files.Add(new FileRecord { path = rel, sha256 = Hash.File(dest), backup = backup });
            }

            // Remove files an older version of this component installed but the new one no longer ships.
            if (previous != null)
            {
                foreach (var old in previous.files.Where(o => !staged.ContainsKey(o.path)))
                    RemoveRecord(old);
            }

            state.components[c.id] = next;
            state.Save(gameDir);
        }

        // ---------------------------------------------------------------- uninstall

        public void Uninstall(Component c)
        {
            if (!state.components.TryGetValue(c.id, out var cs)) return;
            log.Info("Removing " + c.name);
            foreach (var f in cs.files) RemoveRecord(f);
            state.components.Remove(c.id);
            state.Save(gameDir);
        }

        void RemoveRecord(FileRecord f)
        {
            var full = ExistingVariant(Paths.Join(gameDir, f.path));
            if (full != null)
            {
                if (Hash.File(full) == f.sha256) File.Delete(full);
                else { log.Warn("   kept " + f.path + " (changed since install)"); return; }
            }
            if (f.backup != null)
            {
                var b = Paths.Join(InstallState.Dir(gameDir), f.backup);
                if (File.Exists(b))
                {
                    var dest = Paths.Join(gameDir, f.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(b, dest, true);
                    log.Info("   restored your original " + f.path);
                }
            }
        }

        // ---------------------------------------------------------------- listen-mode toggles

        /// <summary>
        /// Enables or disables the .vdf files that make the game load Metamod/L4DToolZ.
        /// Listen mode keeps them disabled whenever the game is not launched through this tool.
        /// </summary>
        public void SetLoaders(Manifest m, bool enabled)
        {
            foreach (var c in m.components)
            {
                foreach (var rel in c.toggleFiles ?? new List<string>())
                {
                    var on = Paths.Join(gameDir, rel);
                    var off = on + ".disabled";
                    try
                    {
                        if (enabled && File.Exists(off) && !File.Exists(on)) File.Move(off, on);
                        if (!enabled && File.Exists(on))
                        {
                            if (File.Exists(off)) File.Delete(off);
                            File.Move(on, off);
                        }
                    }
                    catch (IOException ex) { log.Warn($"Could not {(enabled ? "enable" : "disable")} {rel}: {ex.Message}"); }
                }
            }
        }

        public bool LoadersEnabled(Manifest m) =>
            m.components.SelectMany(c => c.toggleFiles ?? new List<string>()).Any(rel => File.Exists(Paths.Join(gameDir, rel)));

        // ---------------------------------------------------------------- SourcePawn toolchain

        static string Spcomp => Path.Combine(Paths.Toolchain, "spcomp.exe");

        async Task EnsureToolchain(CancellationToken ct)
        {
            if (File.Exists(Spcomp)) return;
            log.Info("Fetching the SourcePawn compiler (from SourceMod 1.12)...");
            var url = await Http.ResolveAlliedModders("https://sm.alliedmods.net/smdrop/1.12/sourcemod-latest-windows", "https://sm.alliedmods.net/smdrop/1.12/", ct);
            var zip = Path.Combine(Paths.Cache, "downloads", Http.FileNameFromUrl(url));
            if (!File.Exists(zip)) await Http.Download(url, zip, log, ct);
            RefreshToolchain(zip);
        }

        static void RefreshToolchain(string smZip)
        {
            const string prefix = "addons/sourcemod/scripting/";
            var tc = Paths.Ensure(Paths.Toolchain);
            using (var zip = ZipFile.OpenRead(smZip))
            {
                foreach (var e in zip.Entries)
                {
                    var n = e.FullName.Replace('\\', '/');
                    if (!n.StartsWith(prefix) || n.EndsWith("/")) continue;
                    var rel = n.Substring(prefix.Length);
                    if (!(rel.StartsWith("include/") || rel.StartsWith("spcomp"))) continue;
                    var dest = Paths.Join(tc, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
        }

        static void ExtractEmbedded(string resource, string dest)
        {
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) throw new FileNotFoundException("Built-in plugin source is missing from this build: " + resource);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (var f = File.Create(dest)) s.CopyTo(f);
            }
        }

        void Compile(string sp, string outPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var psi = new ProcessStartInfo(Spcomp)
            {
                Arguments = $"\"{sp}\" -i\"{Path.Combine(Paths.Toolchain, "include")}\" -i\"{Path.Combine(Paths.Toolchain, "extra")}\" -o\"{outPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(sp),
            };
            log.Info("   compiling " + Path.GetFileName(sp));
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(outPath))
                {
                    var errors = string.Join(Environment.NewLine, output.Split('\n').Where(l => l.Contains("error")).Take(5));
                    throw new InvalidOperationException($"Compiling {Path.GetFileName(sp)} failed:{Environment.NewLine}{errors}");
                }
            }
        }
    }
}
