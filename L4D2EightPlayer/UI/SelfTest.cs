using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// Headless diagnostics, written to a text file (the exe has no console):
    ///   --selftest --out report.txt [--detect] [--smx file] [--vpk file]
    ///              [--install dir [--mode dedicated|listen] [--only id,id]] [--uninstall dir]
    ///              [--scan-client dir] [--scan-server dir]
    /// "dir" is a left4dead2 folder; point it at a scratch folder to test without touching a real install.
    /// </summary>
    static class SelfTest
    {
        sealed class FileLog : ILog
        {
            readonly StringBuilder sb;
            public FileLog(StringBuilder sb) { this.sb = sb; }
            public void Info(string m) => sb.AppendLine(m);
            public void Warn(string m) => sb.AppendLine("WARN  " + m);
            public void Error(string m) => sb.AppendLine("ERROR " + m);
            public void Progress(string l, double f) { }
        }

        static L4D2EightPlayer.UI.Themes.PageHost FindTabs(System.Windows.Forms.Control c)
        {
            foreach (System.Windows.Forms.Control child in c.Controls)
            {
                if (child is L4D2EightPlayer.UI.Themes.PageHost t) return t;
                var found = FindTabs(child);
                if (found != null) return found;
            }
            return null;
        }

        static IEnumerable<System.Windows.Forms.Control> AllControls(System.Windows.Forms.Control c)
        {
            foreach (System.Windows.Forms.Control child in c.Controls)
            {
                yield return child;
                foreach (var g in AllControls(child)) yield return g;
            }
        }

        public static int Run(string[] args)
        {
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var sb = new StringBuilder();
            var log = new FileLog(sb);
            int rc = 0;
            try
            {
                var manifest = Manifest.Load(null, log).GetAwaiter().GetResult();
                log.Info($"manifest: {manifest.components.Count} components, source {manifest.Source}");

                if (args.Contains("--detect"))
                {
                    var steam = GameLocator.FindSteam();
                    log.Info("steam: " + steam);
                    foreach (var l in GameLocator.LibraryFolders(steam)) log.Info("  library: " + l);
                    var game = GameLocator.FindGame(steam);
                    log.Info("game: " + game + "  version " + (game == null ? "" : GameLocator.PatchVersion(game)));
                    log.Info("tools server: " + GameLocator.FindSteamToolsServer(steam));
                }

                if (Arg("--smx") != null)
                {
                    var smx = Smx.Open(Arg("--smx"));
                    log.Info($"smx: name='{smx.Name}' author='{smx.Author}' version='{smx.Version}'");
                    log.Info("  natives: " + smx.Natives.Count + " e.g. " + string.Join(", ", smx.Natives.Take(8)));
                    foreach (var e in smx.Extensions) log.Info($"  ext: {e.Name} file={e.File} required={e.Required}");
                    foreach (var l in smx.Libraries) log.Info($"  lib: {l.Name} file={l.File} required={l.Required}");
                    log.Info("  contains survivor_limit: " + smx.ContainsString("survivor_limit"));
                }

                if (Arg("--vpk") != null)
                {
                    var info = Compat.ReadAddon(Arg("--vpk"));
                    log.Info($"vpk: title='{info.Title}' files={info.Files.Count} error={info.Error}");
                    log.Info($"  campaign={info.Campaign} gameplay={info.Gameplay} mutation={info.Mutation} talker={info.Talker} ui={info.Ui} models={info.SurvivorModels}");
                    foreach (var n in info.ScriptNotes) log.Info($"  {n.sev}: {n.text}");
                }

                if (Arg("--audit") != null)
                {
                    // Layout audit: walk every screen state at a simulated display scale, record clipped
                    // text and controls spilling out of their parents, and save a screenshot per state.
                    var dir = Paths.Ensure(Arg("--audit"));
                    if (float.TryParse(Arg("--scale") ?? "1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                        L4D2EightPlayer.UI.Themes.Ui.Test = scale;
                    L4D2EightPlayer.UI.Themes.Audit.On = true;
                    MainForm.Preview = true;
                    Settings.ReadOnly = true;
                    System.Windows.Forms.Application.EnableVisualStyles();
                    // Rendered off-screen, inside Application.Run like the real app (so awaits resume on
                    // the UI thread). Nothing pops up over the desktop and nothing outside the app is captured.
                    var form = new MainForm { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
                    var stepStarted = DateTime.Now;
                    string current = "startup";
                    var outPath = Arg("--out") ?? Path.Combine(Paths.AppData, "selftest.txt");
                    new Thread(() =>
                    {
                        while ((DateTime.Now - stepStarted).TotalSeconds < 45) Thread.Sleep(500);
                        lock (sb) { log.Info("STALLED in " + current); File.WriteAllText(outPath, sb.ToString()); }
                        Environment.Exit(4);
                    }) { IsBackground = true }.Start();
                    form.Shown += async (s, e) =>
                    {
                        await Task.Delay(8000); // startup scan and checks
                        log.Info($"audit at scale {L4D2EightPlayer.UI.Themes.Ui.Factor:0.00} (system {L4D2EightPlayer.UI.Themes.Ui.System:0.00} x test {scale:0.00}), window {form.Size}");
                        int total = 0;
                        foreach (var (name, act) in form.AuditSteps())
                        {
                            current = name;
                            stepStarted = DateTime.Now;
                            await act();
                            await Task.Delay(800);
                            lock (L4D2EightPlayer.UI.Themes.Audit.Issues) L4D2EightPlayer.UI.Themes.Audit.Issues.Clear();
                            form.Refresh();
                            foreach (var lvw in AllControls(form).OfType<System.Windows.Forms.ListView>()) lvw.Refresh();
                            L4D2EightPlayer.UI.Themes.Audit.Tree(form);
                            var issues = L4D2EightPlayer.UI.Themes.Audit.Issues.ToList();
                            total += issues.Count(i => !i.StartsWith("LIST CELL"));
                            lock (sb)
                            {
                                log.Info($"== {name}: {issues.Count} issue(s)");
                                foreach (var i in issues) log.Info("   " + i);
                            }
                            using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                                bmp.Save(Path.Combine(dir, $"{scale:0.00}-{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        log.Info($"TOTAL {total} non-list issue(s)");
                        stepStarted = DateTime.MaxValue.AddDays(-1);
                        form.Close();
                    };
                    System.Windows.Forms.Application.Run(form);
                }

                if (Arg("--shot") != null)
                {
                    // Render each tab of the real window off-screen to PNGs.
                    var dir = Paths.Ensure(Arg("--shot"));
                    System.Windows.Forms.Application.EnableVisualStyles();
                    MainForm.Preview = true;
                    Settings.ReadOnly = true;   // the Custom screenshot below flips settings; never save them
                    var form = new MainForm { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
                    form.Show();
                    var until = DateTime.Now.AddSeconds(12);
                    while (DateTime.Now < until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                    var tabs = FindTabs(form);
                    for (int i = 0; i < tabs.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        var t2 = DateTime.Now.AddSeconds(i == 3 ? 10 : 1);
                        while (DateTime.Now < t2) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                        using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            bmp.Save(Path.Combine(dir, $"tab{i + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        if (i == 0)
                        {
                            // and the end of the component list (the Optional group)
                            var lv = (System.Windows.Forms.ListView)typeof(MainForm).GetField("lvComponents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(form);
                            if (lv.Items.Count > 0) lv.EnsureVisible(lv.Items.Count - 1);
                            System.Windows.Forms.Application.DoEvents();
                            using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                                bmp.Save(Path.Combine(dir, "tab1-end.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                    }
                    // the Play tab with both scaling settings on Custom (shows the rules links), and the rules dialog
                    tabs.SelectedIndex = 2;
                    foreach (var name in new[] { "cmbItemsMode", "cmbInfectedMode" })
                    {
                        var pills = (Themes.Pills)typeof(MainForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(form);
                        pills.SelectedItem = "custom";
                    }
                    // and the warning a failed plugin leaves under the Start button
                    typeof(MainForm).GetField("healthIssues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .SetValue(form, new List<ServerHealth.Issue> { new ServerHealth.Issue { File = "l4d2_8p_characters.smx", Component = "Unique Survivors (built in)", Problem = "example" } });
                    typeof(MainForm).GetMethod("UpdateHealthLink", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(form, null);
                    var t3 = DateTime.Now.AddSeconds(1);
                    while (DateTime.Now < t3) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(Path.Combine(dir, "tab3-custom.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    var rules = new[]
                    {
                        new RulesDialog.Rule { Title = "Specials at once", Detail = "How many special infected can be alive together", Value = 25 },
                        new RulesDialog.Rule { Title = "Horde size", Detail = "Common infected limit, mobs and panic hordes", Value = 25 },
                        new RulesDialog.Rule { Title = "Tank health", Detail = "Set when each Tank spawns", Value = 100 },
                        new RulesDialog.Rule { Title = "Faster specials", Detail = "How much sooner a dead special's slot refills", Value = 0, Max = 75 },
                    };
                    using (var d = new RulesDialog("Harder infected for 5+: custom rules",
                        "Each rule is % more for every survivor beyond 4. 25% matches Auto: with 8 survivors, 4 x 25% = twice the normal amount. Bots count as survivors. Applies the next time you start the server.", rules)
                        { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false })
                    {
                        d.Show(form);
                        var t4 = DateTime.Now.AddSeconds(1);
                        while (DateTime.Now < t4) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                        using (var bmp = new System.Drawing.Bitmap(d.Width, d.Height))
                        {
                            d.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, d.Width, d.Height));
                            bmp.Save(Path.Combine(dir, "rules.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        d.Close();
                    }
                    form.Close();
                    log.Info("screenshots saved to " + dir);
                }

                if (Arg("--campaigns") != null)
                {
                    // --campaigns <left4dead2 dir>[;<dir>...]: the custom campaigns the Play tab would list
                    foreach (var c in Campaigns.Find(Arg("--campaigns").Split(';')))
                        log.Info($"campaign {c.Title} | maps {string.Join(",", c.Maps)} | workshop {c.WorkshopId ?? "-"} | {Path.GetFileName(c.File)}");
                    log.Info("campaigns: done");
                }

                if (Arg("--health") != null)
                {
                    // --health <left4dead2 dir> [--minutes N]: the post-start plugin check over the last N minutes of logs
                    int.TryParse(Arg("--minutes") ?? "60", out int minutes);
                    var issues = ServerHealth.Check(Arg("--health"), DateTime.Now.AddMinutes(-minutes), manifest);
                    log.Info($"health: {issues.Count} issue(s) in the last {minutes} min");
                    foreach (var i in issues) log.Info($"  {i.File} | {i.Component} | {i.Problem}");
                }

                if (args.Contains("--net"))
                {
                    // Read-only: discovers the router and reads its state; never adds or removes mappings.
                    log.Info($"lan {Network.LanAddress()} gateway {Network.Gateway()}");
                    var u = Upnp.Discover().GetAwaiter().GetResult();
                    foreach (var d in Upnp.LastDiagnostics) log.Info("  diag: " + d);
                    if (u == null) log.Info("upnp: no router answered");
                    else
                    {
                        var ext = u.ExternalAddress().GetAwaiter().GetResult();
                        log.Info($"upnp: {u.FriendlyName} · {u.ServiceType} · external {ext} · private/CGNAT {Network.IsPrivate(ext)}");
                        log.Info("  27015/UDP mapped to: " + (u.MappedTo(27015, "UDP").GetAwaiter().GetResult() ?? "none"));
                        log.Info("  27015/TCP mapped to: " + (u.MappedTo(27015, "TCP").GetAwaiter().GetResult() ?? "none"));
                    }
                    log.Info("firewall rule present: " + Firewall.RuleExists());
                }

                var mode = Arg("--mode") ?? "dedicated";
                if (Arg("--cfg") != null)
                {
                    // Writes just the managed config blocks for a mode/network combination and prints them.
                    var dir = Arg("--cfg");
                    var st = new Settings { Mode = mode, NetworkMode = Arg("--network") ?? "internet", JoinMethod = Arg("--join") ?? "direct", Port = 27015 };
                    ServerConfig.Apply(st, dir);
                    var cfg = Path.Combine(dir, "cfg", mode == "listen" ? "listenserver.cfg" : "server.cfg");
                    log.Info($"--- {mode} / {st.NetworkMode} / {st.JoinMethod}: {Path.GetFileName(cfg)}");
                    log.Info(File.ReadAllText(cfg));
                    var exe = Path.Combine(dir, "srcds.exe");
                    log.Info("launch: srcds.exe " + Launcher.ServerArgs(st));
                }
                if (Arg("--install") != null)
                {
                    var target = Arg("--install");
                    Directory.CreateDirectory(target);
                    var only = Arg("--only")?.Split(',');
                    var ids = only ?? manifest.components.Where(c => c.required || c.@default).Select(c => c.id).ToArray();
                    var inst = new Installer(target, log);
                    // --no-deps installs just the listed components; --keep-cfg leaves the server's config untouched
                    // (both for refreshing one component on a real server).
                    var todo = args.Contains("--no-deps") ? ids.Select(manifest.Get).Where(c => c != null).ToList() : manifest.Resolve(ids, mode);
                    foreach (var c in todo)
                    {
                        try { inst.Install(c, CancellationToken.None).GetAwaiter().GetResult(); }
                        catch (Exception ex) { rc = 1; log.Error(c.id + ": " + ex.Message); }
                    }
                    inst.ApplySourceModGuard(manifest);
                    if (!args.Contains("--keep-cfg")) ServerConfig.Apply(new Settings { Mode = mode }, target);
                    foreach (var c in manifest.components.Where(c => c.AppliesTo(mode)))
                        log.Info($"  status {c.id,-22} {inst.Status(c),-16} {inst.InstalledVersion(c)}");
                }

                if (Arg("--uninstall") != null)
                {
                    var target = Arg("--uninstall");
                    var inst = new Installer(target, log);
                    foreach (var id in inst.State.components.Keys.ToList()) inst.Uninstall(manifest.Get(id) ?? new Component { id = id, name = id });
                    ServerConfig.ClearBlock(Path.Combine(target, "cfg", "server.cfg"));
                    ServerConfig.ClearBlock(Path.Combine(target, "cfg", "sourcemod", "l4dmultislots.cfg"));
                    var left = Directory.Exists(target) ? Directory.GetFiles(target, "*", SearchOption.AllDirectories).Where(f => !f.Contains("l4d2_8p_manager")).ToList() : new List<string>();
                    log.Info($"uninstall: {left.Count} file(s) left outside the manager folder");
                    foreach (var f in left.Take(20)) log.Info("  left: " + f);
                }

                if (args.Contains("--launch-join"))
                {
                    // The dedicated "Launch L4D2 & join" path with the saved settings, checked by a player query.
                    var st = Settings.Load();
                    ServerConfig.Apply(st, st.TargetGameDir);
                    var host = Launcher.LocalServerAddress();
                    log.Info($"mode {st.NetworkMode}, server {host}:{st.Port}, password {(string.IsNullOrEmpty(st.Password) ? "none" : "set")}");
                    var srv = Launcher.StartServer(st);
                    try
                    {
                        bool ready = Launcher.WaitUntilReady(st, srv, TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
                        var info = ServerQuery.Query(host, st.Port).GetAwaiter().GetResult();
                        log.Info(!ready ? "server never came up" : info == null ? "server up (not answering queries)" : $"server up: {info.Name} on {info.Map}");
                        if (!ready) rc = 1;
                        else
                        {
                            var pw = st.Password ?? "";
                            bool dropped = Launcher.LaunchGameDirect(st.GamePath, $"+connect {host}:{st.Port}" + (pw.Length == 0 ? "" : $" +password \"{pw}\""), dropInsecure: true);
                            log.Info("game launched directly" + (dropped ? " (left out -insecure)" : ""));
                            int seen = 0;
                            for (var until = DateTime.Now.AddSeconds(150); seen == 0 && DateTime.Now < until; Thread.Sleep(3000))
                            {
                                var players = ServerQuery.Players(host, st.Port).GetAwaiter().GetResult();
                                seen = players?.Count ?? 0;
                                if (seen > 0) log.Info("on the server: " + string.Join(", ", players));
                            }
                            if (seen == 0) { rc = 1; log.Error("the game never showed up on the server"); }
                        }
                    }
                    finally
                    {
                        foreach (var g in System.Diagnostics.Process.GetProcessesByName("left4dead2")) try { g.Kill(); } catch { }
                        try { if (!srv.HasExited) srv.Kill(); } catch { }
                    }
                }

                if (Arg("--updates") != null)
                {
                    // Read-only: which installed components differ from upstream. Archive components compare
                    // release versions; plain-file components compare the downloaded file with the installed hash.
                    var target = Arg("--updates");
                    var inst = new Installer(target, log);
                    var tmp = Paths.Ensure(Path.Combine(Paths.Cache, "updatecheck"));
                    foreach (var c in manifest.components.Where(c => c.AppliesTo(mode)))
                    {
                        var status = inst.Status(c);
                        var cur = inst.InstalledVersion(c);
                        string verdict;
                        try
                        {
                            if (status == ComponentStatus.NotInstalled || status == ComponentStatus.FoundUnmanaged) verdict = "-";
                            else if (c.archive != null)
                            {
                                var latest = Installer.LatestVersion(c, CancellationToken.None).GetAwaiter().GetResult();
                                verdict = latest == null || latest == cur ? "current" : "UPDATE " + latest;
                            }
                            else
                            {
                                var recorded = inst.State.components[c.id].files.ToDictionary(f => f.path, f => f.sha256, StringComparer.OrdinalIgnoreCase);
                                var changed = new List<string>();
                                foreach (var f in c.files ?? new List<List<string>>())
                                {
                                    if (f.Count < 2) continue;
                                    var local = Path.Combine(tmp, c.id, Path.GetFileName(f[1]));
                                    Http.Download((c.@base ?? "") + f[0], local, log, CancellationToken.None).GetAwaiter().GetResult();
                                    if (!recorded.TryGetValue(f[1], out var sha) || Hash.File(local) != sha) changed.Add(Path.GetFileName(f[1]));
                                }
                                verdict = changed.Count == 0 ? ((c.compile?.Count ?? 0) > 0 ? "current (source-built)" : "current") : "UPDATE " + string.Join(", ", changed);
                            }
                        }
                        catch (Exception ex) { verdict = "check failed: " + ex.Message; }
                        log.Info($"  update {c.id,-22} {status,-14} {cur,-24} {verdict}");
                    }
                }

                if (Arg("--scan-client") != null || Arg("--scan-server") != null)
                {
                    var server = Arg("--scan-server");
                    var ctx = new ScanContext
                    {
                        Manifest = manifest,
                        Mode = mode,
                        ClientGameDir = Arg("--scan-client"),
                        ServerGameDir = server,
                        ServerRoot = server == null ? null : Path.GetDirectoryName(server),
                        SyncAddons = args.Contains("--sync"),
                        JoinMethod = Arg("--join") ?? "direct",
                        State = server == null ? null : InstallState.Load(server),
                    };
                    var findings = Compat.Scan(ctx);
                    log.Info($"scan: {findings.Count} findings");
                    foreach (var f in findings)
                    {
                        log.Info($"  [{f.Severity}] {f.Kind}: {f.Name} | {f.Summary}{(f.Managed ? " | managed" : "")}");
                        foreach (var d in f.Details) log.Info("        " + d);
                    }
                }
            }
            catch (Exception ex) { rc = 2; log.Error(ex.ToString()); }

            var outFile = Arg("--out") ?? Path.Combine(Paths.AppData, "selftest.txt");
            File.WriteAllText(outFile, sb.ToString());
            return rc;
        }
    }
}
