using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --checkup &lt;report&gt;: the Setup and Mods tabs' checks on this PC's real setup, written to a file and nothing
    /// changed: each component's installed version and state against the latest release, the compatibility scan
    /// (game and server), and add-ons that clash. Settings are only read. With --apply, the components behind their
    /// latest release are installed again (as Install / update does), and nothing else is touched.
    /// </summary>
    static class Checkup
    {
        public static int Run(string report, bool apply = false)
        {
            var sb = new StringBuilder();
            var log = new SelfTest.FileLog(sb);
            Settings.ReadOnly = true;
            SkinPrefs.ReadOnly = true;
            try
            {
                var settings = Settings.Load();
                var manifest = Manifest.Load(settings.ManifestUrl, log).GetAwaiter().GetResult();
                sb.AppendLine($"mode {settings.Mode}; game {settings.GamePath} ({GameLocator.PatchVersion(settings.GamePath)}); server {settings.ServerInstallDir} ({GameLocator.PatchVersion(settings.ServerInstallDir)})");
                sb.AppendLine($"target {settings.TargetGameDir}");

                sb.AppendLine().AppendLine("== components (installed / latest / state)");
                var inst = new Installer(settings.TargetGameDir, log);
                var chosen = new HashSet<string>(settings.Selected ?? new List<string>());
                var outdated = new List<Component>();
                foreach (var c in manifest.components.Where(c => c.AppliesTo(settings.Mode)))
                {
                    string latest = null;
                    try { latest = Installer.LatestVersion(c, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception ex) { latest = "(couldn't check: " + ex.Message + ")"; }
                    var cur = inst.InstalledVersion(c);
                    var status = inst.Status(c);
                    bool behind = cur != null && latest != null && !latest.StartsWith("(") && latest != cur;
                    if (behind && c.client == null) outdated.Add(c);
                    sb.AppendLine($"{(behind ? "UPDATE " : status == ComponentStatus.Modified ? "CHANGED" : "       ")} {c.id,-22} {status,-15} {(chosen.Contains(c.id) ? "selected" : "-"),-9} {cur ?? "-"}  →  {latest ?? "-"}");
                }

                if (apply && outdated.Count > 0)
                {
                    sb.AppendLine().AppendLine("== updating " + string.Join(", ", outdated.Select(c => c.id)));
                    if (Launcher.ServersRunningFrom(settings.ServerInstallDir).Any()) throw new InvalidOperationException("The server is running: stop it first.");
                    foreach (var c in outdated) inst.Install(c, CancellationToken.None).GetAwaiter().GetResult();
                    inst.ApplySourceModGuard(manifest);
                    foreach (var c in outdated) sb.AppendLine($"   {c.id} now {new Installer(settings.TargetGameDir, log).InstalledVersion(c)}");
                }

                sb.AppendLine().AppendLine("== compatibility scan (warnings and problems)");
                var ctx = new ScanContext
                {
                    Manifest = manifest,
                    Mode = settings.Mode,
                    ClientGameDir = GameLocator.IsGameDir(settings.GamePath) ? settings.ClientGameDir : null,
                    ServerGameDir = settings.TargetGameDir,
                    ServerRoot = settings.Mode == "listen" ? settings.GamePath : settings.ServerInstallDir,
                    SyncAddons = settings.SyncAddons,
                    JoinMethod = settings.JoinMethod,
                    State = settings.TargetGameDir != null && Directory.Exists(settings.TargetGameDir) ? InstallState.Load(settings.TargetGameDir) : null,
                };
                var findings = Compat.Scan(ctx);
                foreach (var g in findings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key))
                    sb.AppendLine($"   {g.Key}: {g.Count()}");
                foreach (var f in findings.Where(f => f.Severity >= Severity.Warning).OrderByDescending(f => f.Severity))
                {
                    sb.AppendLine($"{f.Severity.ToString().ToUpperInvariant(),-8} [{f.Kind}] {f.Name}: {f.Summary}");
                    if (!string.IsNullOrEmpty(f.Location)) sb.AppendLine("         " + f.Location);
                    foreach (var d in f.Details) sb.AppendLine("         - " + d);
                }

                sb.AppendLine().AppendLine("== add-ons that clash (your game)");
                var survey = Clashes.Look(ctx.ClientGameDir, manifest);
                var real = Clashes.Conflicts(survey, SkinPrefs.Load());
                sb.AppendLine($"   {real.Count} real, {survey.Harmless.Count(c => c.BothOn)} harmless overlaps among add-ons that are on");
                foreach (var c in real) sb.AppendLine($"   {c.A.Title}  +  {c.B.Title}: {c.Files.Count} files ({c.What})");

                sb.AppendLine().AppendLine("== ratings (your game's add-ons that are on)");
                var rated = Ratings.Rate(survey, p => findings.FirstOrDefault(f => string.Equals(f.Location, p, StringComparison.OrdinalIgnoreCase))?.Severity >= Severity.Problem ? "the scan found a problem" : null);
                foreach (var g in rated.Where(kv => kv.Value.On).GroupBy(kv => kv.Value.Rating).OrderByDescending(g => g.Key))
                {
                    sb.AppendLine($"   {g.Key}: {g.Count()}");
                    if (g.Key >= Rating.Moderate) foreach (var kv in g) sb.AppendLine($"      {Path.GetFileName(kv.Key)}: {kv.Value.Why}");
                }
            }
            catch (Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
            File.WriteAllText(report, sb.ToString());
            return 0;
        }
    }
}
