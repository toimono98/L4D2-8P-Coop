using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    public sealed partial class MainForm : Form, ILog
    {
        const string Title = "L4D2 8-Player Co-op Manager";

        Settings settings = Settings.Load();
        Manifest manifest = Manifest.BuiltIn();
        Process serverProcess;
        CancellationTokenSource cts;
        bool busy, loading;
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly System.Windows.Forms.Timer rescanTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly System.Windows.Forms.Timer gameWatch = new System.Windows.Forms.Timer { Interval = 3000 };
        bool gameSeen;
        DateTime gameLaunchAt;
        List<Finding> findings = new List<Finding>();
        HashSet<string> seen;
        readonly Dictionary<string, string> latestVersions = new Dictionary<string, string>();

        // ================================================================== startup / settings

        async Task OnLoaded()
        {
            seen = new HashSet<string>(Json.Load<List<string>>(Path.Combine(Paths.AppData, "seen.json")), StringComparer.OrdinalIgnoreCase);
            manifest = await Manifest.Load(settings.ManifestUrl, this);
            Info($"{Title} ready. Component list: {manifest.Source} (updated {manifest.updated}).");

            if (string.IsNullOrWhiteSpace(settings.GamePath) || !GameLocator.IsGameDir(settings.GamePath)) DetectGame(false);
            if (string.IsNullOrWhiteSpace(settings.ServerRoot) && string.IsNullOrWhiteSpace(settings.ExistingServerPath))
            {
                var tools = GameLocator.FindSteamToolsServer();
                if (tools != null) { settings.ExistingServerPath = settings.ServerRoot = tools; Info("Found a dedicated server installed through Steam: " + tools); }
                else settings.ServerRoot = Path.Combine(Path.GetPathRoot(settings.GamePath ?? @"C:\") ?? @"C:\", "L4D2-8P-Server");
            }
            if (!Preview) settings.Save();

            // If the game is not running, make sure listen-mode loaders are switched off (see LaunchGame).
            if (settings.ClientGameDir != null && Directory.Exists(settings.ClientGameDir) && !Launcher.GameRunning())
            {
                var inst = new Installer(settings.ClientGameDir, this);
                if (inst.State.components.Count > 0 && inst.LoadersEnabled(manifest))
                {
                    inst.SetLoaders(manifest, false);
                    Info("Turned listen-server mods back off (L4D2 is not running).");
                }
            }

            LoadSettingsIntoUi();
            RefreshAll();
            await LoadCampaigns();
            SetupWatchers();
            if (settings.TargetGameDir != null) await RunScan(auto: true, quiet: true);
            await CleanupStaleForward();
        }

        List<Campaigns.Campaign> customCampaigns = new List<Campaigns.Campaign>();

        /// <summary>Adds the custom campaigns in the server's (and your game's) add-ons to the campaign list.</summary>
        async Task LoadCampaigns()
        {
            var dirs = new[] { settings.TargetGameDir, settings.ClientGameDir };
            customCampaigns = await Task.Run(() => Campaigns.Find(dirs));
            var text = cmbMap.Text;
            cmbMap.BeginUpdate();
            cmbMap.Items.Clear();
            cmbMap.Items.AddRange(ServerConfig.Maps);
            foreach (var c in customCampaigns) cmbMap.Items.Add(c.FirstMap);
            cmbMap.EndUpdate();
            loading = true;
            cmbMap.Text = text;
            loading = false;
            lblCampaign.Text = CampaignTitle(cmbMap.Text);
            if (customCampaigns.Count > 0)
                Info($"Custom campaigns: {string.Join(", ", customCampaigns.Select(c => c.Title + " (" + c.FirstMap + ")"))}.");
        }

        /// <summary>The campaign's name for a map: official, an installed custom campaign, or "CUSTOM MAP".</summary>
        string CampaignTitle(string map)
        {
            if (ServerQuery.IsOfficialMap(map)) return CampaignName(map);
            var c = Campaigns.ForMap(customCampaigns, map);
            return c != null ? c.Title.ToUpperInvariant() : "CUSTOM MAP";
        }

        void LoadSettingsIntoUi()
        {
            loading = true;
            txtGame.Text = settings.GamePath ?? "";
            txtServer.Text = settings.ExistingServerPath ?? settings.ServerRoot ?? "";
            rbDedicated.Checked = settings.Mode != "listen";
            rbListen.Checked = settings.Mode == "listen";
            txtServerName.Text = settings.ServerName;
            numPort.Value = Math.Max(numPort.Minimum, Math.Min(numPort.Maximum, settings.Port));
            txtPassword.Text = settings.Password;
            numPlayers.Value = Math.Max(numPlayers.Minimum, Math.Min(numPlayers.Maximum, settings.MaxPlayers));
            chkFill.Checked = settings.FillWithBots;
            chkHudLook.Checked = settings.HudAddonLook;
            cmbItemsMode.SelectedItem = settings.ItemsModeOrDefault;
            if (cmbItemsMode.SelectedIndex < 0) cmbItemsMode.SelectedIndex = 1;
            cmbInfectedMode.SelectedItem = settings.InfectedMode;
            if (cmbInfectedMode.SelectedIndex < 0) cmbInfectedMode.SelectedIndex = 1;
            cmbRotation.SelectedItem = settings.RotationMode;
            if (cmbRotation.SelectedIndex < 0) cmbRotation.SelectedIndex = 1;
            UpdateRulesLinks();
            UpdateHudAddonLink();
            cmbMode.SelectedItem = settings.GameMode;
            if (cmbMode.SelectedIndex < 0) cmbMode.SelectedIndex = 0;
            cmbDifficulty.SelectedItem = settings.Difficulty;
            if (cmbDifficulty.SelectedIndex < 0) cmbDifficulty.SelectedIndex = 1;
            cmbMap.Text = settings.Map;
            rbDirect.Checked = settings.JoinMethod != "lobby";
            rbLobby.Checked = settings.JoinMethod == "lobby";
            cmbNetwork.SelectedItem = settings.Internet ? "internet" : settings.NetworkMode;
            txtLobbyAddress.Text = settings.LobbyAddress;
            chkSync.Checked = settings.SyncAddons;
            chkUpdateOnStart.Checked = settings.UpdateServerOnStart;
            chkScanFirst.Checked = settings.ScanBeforeLaunch;
            chkWatch.Checked = settings.WatchMods;
            loading = false;
        }

        void ReadPlaySettings()
        {
            if (loading) return;
            settings.ServerName = txtServerName.Text.Trim();
            settings.Port = (int)numPort.Value;
            settings.Password = txtPassword.Text;
            settings.MaxPlayers = (int)numPlayers.Value;
            settings.FillWithBots = chkFill.Checked;
            settings.HudAddonLook = chkHudLook.Checked;
            settings.ItemsMode = cmbItemsMode.SelectedItem as string ?? "auto";
            settings.InfectedMode = cmbInfectedMode.SelectedItem as string ?? "auto";
            settings.RotationMode = cmbRotation.SelectedItem as string ?? "next";
            UpdateRulesLinks();
            UpdateHudAddonLink();
            settings.GameMode = cmbMode.SelectedItem as string ?? "coop";
            settings.Difficulty = cmbDifficulty.SelectedItem as string ?? "Normal";
            settings.Map = string.IsNullOrWhiteSpace(cmbMap.Text) ? "c1m1_hotel" : cmbMap.Text.Trim();
            settings.JoinMethod = rbLobby.Checked ? "lobby" : "direct";
            settings.NetworkMode = cmbNetwork.SelectedItem as string ?? "internet";
            settings.LobbyAddress = txtLobbyAddress.Text.Trim();
            settings.SyncAddons = chkSync.Checked;
            settings.UpdateServerOnStart = chkUpdateOnStart.Checked;
            settings.ScanBeforeLaunch = chkScanFirst.Checked;
            settings.Save();
            UpdatePlayState();
        }

        /// <summary>The "Custom rules…" links only show while their setting is on Custom.</summary>
        void UpdateRulesLinks()
        {
            lnkItemsRules.Visible = settings.ItemsModeOrDefault == "custom";
            lnkInfectedRules.Visible = settings.InfectedMode == "custom";
        }

        void EditItemRules()
        {
            var rules = new[]
            {
                new RulesDialog.Rule { Title = "Weapons", Detail = "Weapon piles, pistols, melee weapons", Value = settings.ItemsCustomWeapons },
                new RulesDialog.Rule { Title = "Throwables", Detail = "Molotovs, pipe bombs, bile jars", Value = settings.ItemsCustomThrowables },
                new RulesDialog.Rule { Title = "Healing", Detail = "First aid kits, pills, adrenaline, defibrillators", Value = settings.ItemsCustomHealing },
            };
            if (!EditRules("More items for 5+: custom rules", rules)) return;
            settings.ItemsCustomWeapons = rules[0].Value;
            settings.ItemsCustomThrowables = rules[1].Value;
            settings.ItemsCustomHealing = rules[2].Value;
            settings.Save();
        }

        void EditInfectedRules()
        {
            var rules = new[]
            {
                new RulesDialog.Rule { Title = "Specials at once", Detail = "How many special infected can be alive together", Value = settings.InfectedCustomSpecials },
                new RulesDialog.Rule { Title = "Horde size", Detail = "Common infected limit, mobs and panic hordes", Value = settings.InfectedCustomHorde },
                new RulesDialog.Rule { Title = "Tank health", Detail = "Set when each Tank spawns", Value = settings.InfectedCustomTank },
                new RulesDialog.Rule { Title = "Faster specials", Detail = "How much sooner a dead special's slot refills", Value = settings.InfectedCustomRespawn, Max = 75 },
            };
            if (!EditRules("Harder infected for 5+: custom rules", rules)) return;
            settings.InfectedCustomSpecials = rules[0].Value;
            settings.InfectedCustomHorde = rules[1].Value;
            settings.InfectedCustomTank = rules[2].Value;
            settings.InfectedCustomRespawn = rules[3].Value;
            settings.Save();
        }

        bool EditRules(string title, RulesDialog.Rule[] rules)
        {
            const string intro = "Each rule is % more for every survivor beyond 4. 25% matches Auto: with 8 survivors, 4 x 25% = twice the normal amount. " +
                "Bots count as survivors. Applies the next time you start the server.";
            using (var d = new RulesDialog(title, intro, rules))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                d.Save();
                return true;
            }
        }

        /// <summary>The add-on link under the HUD setting: what this PC has, and the one thing to do about it.</summary>
        void UpdateHudAddonLink()
        {
            var st = HudAddon.Check(settings.GamePath);
            switch (st)
            {
                case HudAddon.Status.Installed: lnkHudAddon.Text = "Add-on installed on this PC · Remove"; break;
                case HudAddon.Status.Outdated: lnkHudAddon.Text = "Update the add-on on this PC"; break;
                case HudAddon.Status.Disabled: lnkHudAddon.Text = "Add-on is off in the game's Add-ons menu · Turn on"; break;
                case HudAddon.Status.NotInstalled: lnkHudAddon.Text = "Install the add-on on this PC"; break;
                default: lnkHudAddon.Text = "Set the game folder to install the add-on"; break;
            }
            lnkHudAddon.LinkColor = st == HudAddon.Status.Installed ? Theme.Muted : Theme.Amber;
            lnkHudAddon.Enabled = st != HudAddon.Status.NoGame;
        }

        void ToggleHudAddon()
        {
            try
            {
                if (HudAddon.Check(settings.GamePath) == HudAddon.Status.Installed) HudAddon.Remove(settings.GamePath);
                else HudAddon.Install(settings.GamePath);
                Info(HudAddon.Describe(HudAddon.Check(settings.GamePath)) + " (" + HudAddon.FilePath(settings.GamePath) + ").");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "HUD add-on", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            UpdateHudAddonLink();
            RefreshComponents();
        }

        void SaveAndRefresh()
        {
            settings.Save();
            RefreshAll();
            SetupWatchers();
        }

        void RefreshAll()
        {
            bool listen = settings.Mode == "listen";
            var gameOk = GameLocator.IsGameDir(settings.GamePath);
            lblGameStatus.Text = gameOk
                ? "L4D2 " + (GameLocator.PatchVersion(settings.GamePath) ?? "") + " found" + (Launcher.GameRunning() ? " · running" : "")
                : "L4D2 NOT FOUND · CLICK DETECT OR BROWSE";
            lblGameStatus.ForeColor = gameOk ? Theme.Muted : Theme.Amber;

            txtServer.Enabled = btnServerBrowse.Enabled = !listen;
            if (listen) lblServerStatus.Text = "Listen mode installs into your game folder. The mods are only switched on while you play through this tool.";
            else
            {
                var dir = settings.ServerInstallDir;
                bool installed = GameLocator.IsServerDir(dir);
                lblServerStatus.Text = installed
                    ? $"Dedicated server found ({GameLocator.PatchVersion(dir) ?? "version unknown"}) in {dir}"
                    : $"The dedicated server will be downloaded with SteamCMD into {dir ?? "(choose a folder)"} (about 10 GB).";
            }
            lblServerStatus.ForeColor = Theme.Muted;

            RefreshComponents();
            UpdatePlayState();
            UpdateHudAddonLink();
        }

        void SaveSelection()
        {
            settings.Selected = lvComponents.Items.Cast<ListViewItem>().Where(i => i.Checked && i.Tag is Component).Select(i => ((Component)i.Tag).id).ToList();
            settings.Save();
        }

        void RefreshComponents()
        {
            loading = true;
            lvComponents.BeginUpdate();
            lvComponents.Items.Clear();
            var target = settings.TargetGameDir;
            Installer inst = target != null && Directory.Exists(target) ? new Installer(target, this) : null;
            var groups = new Dictionary<string, object>();
            foreach (var c in manifest.components.Where(c => c.AppliesTo(settings.Mode)))
            {
                if (!groups.ContainsKey(c.group ?? "Other"))
                {
                    groups[c.group ?? "Other"] = null;
                    var label = (c.group ?? "Other") + (c.group == "Core" ? " · required" : "");
                    lvComponents.Items.Add(new ListViewItem(new[] { label, "", "" }) { Tag = new SectionRow() });
                }
                var status = inst?.Status(c) ?? ComponentStatus.NotInstalled;
                var installedVersion = inst?.InstalledVersion(c);
                string statusText;
                switch (status)
                {
                    case ComponentStatus.Installed: statusText = "Installed"; break;
                    case ComponentStatus.Modified: statusText = "Installed (files changed)"; break;
                    case ComponentStatus.FoundUnmanaged: statusText = "Found (your existing copy)"; break;
                    default: statusText = "Not installed"; break;
                }
                if (installedVersion != null && latestVersions.TryGetValue(c.id, out var latest) && latest != null && latest != installedVersion)
                    statusText = "Update available: " + latest;
                if (c.client != null) ClientStatus(c, out status, out statusText);

                var item = new ListViewItem(new[] { c.name + (c.required ? "  (required)" : ""), statusText, installedVersion ?? "" }) { Tag = c, ToolTipText = c.name + Environment.NewLine + c.description };
                // Until the user saves a selection: defaults, plus anything already installed.
                bool installed = status == ComponentStatus.Installed || status == ComponentStatus.Modified;
                bool selected = settings.Selected == null ? (c.required || c.@default || installed) : (c.required || settings.Selected.Contains(c.id));
                item.Checked = selected;
                if (statusText.StartsWith("Update")) item.ForeColor = Theme.Amber;
                else if (status == ComponentStatus.NotInstalled) item.ForeColor = Theme.Muted;
                else item.ForeColor = Theme.Bone;
                lvComponents.Items.Add(item);
            }
            lvComponents.EndUpdate();
            loading = false;
        }

        /// <summary>Status of a piece that lives in the player's own game (the HUD add-on) rather than the server.</summary>
        void ClientStatus(Component c, out ComponentStatus status, out string text)
        {
            var st = HudAddon.Check(settings.GamePath);
            status = st == HudAddon.Status.Installed || st == HudAddon.Status.Outdated || st == HudAddon.Status.Disabled ? ComponentStatus.Installed : ComponentStatus.NotInstalled;
            text = st == HudAddon.Status.Installed ? "Installed in your game"
                 : st == HudAddon.Status.Outdated ? "Update available"
                 : st == HudAddon.Status.Disabled ? "Installed (off in the game's Add-ons menu)"
                 : "Not installed";
        }

        void ShowComponentInfo()
        {
            if (lvComponents.SelectedItems.Count == 0 || !(lvComponents.SelectedItems[0].Tag is Component c)) return;
            var src = c.archive?.repo != null ? "github.com/" + c.archive.repo : c.archive?.latest != null ? new Uri(c.archive.latest).Host : c.@base != null ? new Uri(c.@base).Host + new Uri(c.@base).AbsolutePath.Split('/').Skip(1).Take(2).Aggregate("", (a, b) => a + "/" + b) : "";
            var deps = c.requires?.Count > 0 ? "  Needs: " + string.Join(", ", c.requires.Select(d => manifest.Get(d)?.name ?? d)) + "." : "";
            var compile = c.compile?.Count > 0 ? "  Compiled on your PC from source with SourceMod's compiler." : "";
            txtComponentInfo.Text = $"{c.description}{deps}{compile}{Environment.NewLine}Source: {src}";
        }

        // ================================================================== folders

        void DetectGame(bool announce)
        {
            var game = GameLocator.FindGame();
            if (game != null)
            {
                settings.GamePath = game;
                txtGame.Text = game;
                if (announce) Info("Found Left 4 Dead 2 at " + game);
                SaveAndRefresh();
            }
            else if (announce)
                MessageBox.Show(this, "Left 4 Dead 2 was not found in your Steam libraries. Use Browse to pick the folder containing left4dead2.exe.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void BrowseGame()
        {
            using (var d = new FolderBrowserDialog { Description = "Select the 'Left 4 Dead 2' folder (it contains left4dead2.exe)", SelectedPath = settings.GamePath ?? "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var p = d.SelectedPath;
                if (!GameLocator.IsGameDir(p) && GameLocator.IsGameDir(Path.GetDirectoryName(p))) p = Path.GetDirectoryName(p);
                if (!GameLocator.IsGameDir(p))
                {
                    MessageBox.Show(this, "That folder does not contain left4dead2.exe and a left4dead2 subfolder.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                settings.GamePath = p;
                txtGame.Text = p;
                SaveAndRefresh();
            }
        }

        void BrowseServer()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where the dedicated server lives. Pick an existing srcds folder to reuse it, or an empty folder for a new install.", ShowNewFolderButton = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SetServerFolder(d.SelectedPath);
                txtServer.Text = d.SelectedPath;
            }
        }

        void SetServerFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (GameLocator.IsServerDir(path)) { settings.ExistingServerPath = path; settings.ServerRoot = path; }
            else if (GameLocator.IsServerDir(Path.Combine(path, "server"))) { settings.ExistingServerPath = null; settings.ServerRoot = path; }
            else { settings.ExistingServerPath = null; settings.ServerRoot = path; }
            SaveAndRefresh();
        }

        static void OpenFolder(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
        }

        void ShowManualExtras()
        {
            var text = "These fixes are only published on the AlliedModders forum, which blocks automatic downloads. "
                     + "If you want them, download the .smx and put it in addons\\sourcemod\\plugins. The compatibility scan checks them like any other plugin.\n\n"
                     + string.Join("\n\n", manifest.manual.Select(m => $"• {m.name}\n   {m.why}\n   {m.url}"))
                     + "\n\nOpen these pages in your browser now?";
            if (MessageBox.Show(this, text, "Forum-only extras", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                foreach (var m in manifest.manual) Process.Start(m.url);
        }

        // ================================================================== install

        async Task InstallSelected()
        {
            if (!GameLocator.IsGameDir(settings.GamePath))
            {
                MessageBox.Show(this, "Set your Left 4 Dead 2 folder first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (settings.Mode == "listen" && Launcher.GameRunning())
            {
                MessageBox.Show(this, "Close Left 4 Dead 2 before installing into it.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (settings.Mode == "dedicated" && serverProcess != null && !serverProcess.HasExited)
            {
                MessageBox.Show(this, "Stop the server before installing or updating.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var ids = lvComponents.Items.Cast<ListViewItem>().Where(i => i.Checked && i.Tag is Component).Select(i => ((Component)i.Tag).id);
            var list = manifest.Resolve(ids, settings.Mode);
            bool needServer = settings.Mode == "dedicated" && !GameLocator.IsServerDir(settings.ServerInstallDir);

            var hosts = list.Select(c => c.archive?.repo != null ? "github.com" : c.archive?.latest != null ? new Uri(c.archive.latest).Host : c.@base != null ? new Uri(c.@base).Host : null)
                            .Where(h => h != null).Distinct();
            var msg = (needServer ? $"• Left 4 Dead 2 Dedicated Server from Valve via SteamCMD (about 10 GB) into:\n   {settings.ServerInstallDir}\n" : "")
                    + $"• {list.Count} mod component(s) into:\n   {settings.TargetGameDir}\n\n"
                    + string.Join("\n", list.Select(c => "   - " + c.name))
                    + $"\n\nDownloads come from: {string.Join(", ", hosts)}{(needServer ? ", Valve (Steam)" : "")}.\n"
                    + "Any existing file that gets replaced is backed up first, and 'Remove everything' puts it back.\n\nDownload and install now?";
            if (MessageBox.Show(this, msg, "Download & install", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            await RunBusy("Installing", async ct =>
            {
                if (needServer) await SteamCmd.UpdateServer(settings.ServerRoot, settings.ServerInstallDir, this, ct);
                var target = settings.TargetGameDir;
                var inst = new Installer(target, this);
                int failed = 0;
                foreach (var c in list)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (c.client != null)
                        {
                            Info($"── {c.name}");
                            HudAddon.Install(settings.GamePath);
                            Info("   " + HudAddon.FilePath(settings.GamePath));
                        }
                        else await Task.Run(() => inst.Install(c, ct), ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failed++;
                        Error($"{c.name}: {ex.Message}");
                        if (c.required) throw new InvalidOperationException($"A required component failed ({c.name}). Fix the problem above and run Install again.");
                    }
                }
                inst.ApplySourceModGuard(manifest);
                ServerConfig.Apply(settings, target, customCampaigns);
                Info("Wrote 8-player settings to cfg\\" + (settings.Mode == "listen" ? "listenserver.cfg" : "server.cfg") + " and cfg\\sourcemod\\l4dmultislots.cfg.");
                if (settings.Mode == "listen")
                {
                    inst.SetLoaders(manifest, false);
                    Info("Listen mode: the mods stay switched off until you launch through the Play tab, so normal Steam launches stay clean.");
                }
                Info(failed == 0 ? "All components installed." : $"Done, but {failed} optional component(s) failed (see above).");
            });

            latestVersions.Clear();
            RefreshAll();
            await RunScan(auto: false, quiet: true);
            OfferLobbyMod();
            tabs.SelectedIndex = findings.Any(f => f.Severity == Severity.Problem) ? 1 : 2;
        }

        void OfferLobbyMod()
        {
            var lobby = findings.FirstOrDefault(f => f.Kind == "Requirement" && f.FixTarget == Compat.LobbyModId);
            if (lobby == null || lobby.Summary.StartsWith("Subscribed and")) return;
            if (MessageBox.Show(this, "For the 8-slot in-game lobby, the host needs the '8 Slots Lobby Mod' from the Steam Workshop (you don't need it for direct connect).\n\nOpen its Workshop page so you can subscribe?",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Compat.Apply(lobby, settings.ClientGameDir);
        }

        async Task CheckUpdates()
        {
            await RunBusy("Checking for updates", async ct =>
            {
                var inst = new Installer(settings.TargetGameDir, this);
                int updates = 0;
                foreach (var c in manifest.components.Where(c => c.archive != null && c.AppliesTo(settings.Mode)))
                {
                    try
                    {
                        var latest = await Installer.LatestVersion(c, ct);
                        latestVersions[c.id] = latest;
                        var cur = inst.InstalledVersion(c);
                        if (cur != null && latest != cur) { updates++; Info($"{c.name}: {cur} → {latest}"); }
                    }
                    catch (Exception ex) { Warn($"{c.name}: could not check ({ex.Message})"); }
                }
                if (settings.Mode == "dedicated" && GameLocator.IsServerDir(settings.ServerInstallDir))
                {
                    var cv = GameLocator.PatchVersion(settings.GamePath);
                    var sv = GameLocator.PatchVersion(settings.ServerInstallDir);
                    if (cv != null && sv != null && cv != sv) { updates++; Warn($"Server is {sv} but your game is {cv}. Install will update the server with SteamCMD."); }
                }
                Info(updates == 0 ? "Everything is up to date. Plugins from GitHub are re-downloaded at their latest version whenever you click Install."
                                  : $"{updates} update(s) available. Click 'Download & install selected' to apply them.");
            });
            RefreshComponents();
        }

        async Task UninstallHighlighted()
        {
            var sel = lvComponents.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Component>().ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "Highlight one or more components in the list first.", Title); return; }
            var required = sel.Where(c => c.required).ToList();
            var text = $"Remove {sel.Count} component(s)?" + (required.Count > 0 ? "\n\nRequired components are selected, so 8-player mode will stop working until you install them again." : "");
            if (MessageBox.Show(this, text, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunBusy("Removing", ct => Task.Run(() =>
            {
                var inst = new Installer(settings.TargetGameDir, this);
                foreach (var c in sel)
                {
                    if (c.client != null) { HudAddon.Remove(settings.GamePath); Info("Removed " + c.name + "."); }
                    else inst.Uninstall(c);
                }
            }, ct));
            RefreshAll();
        }

        async Task UninstallAll()
        {
            if (MessageBox.Show(this, $"Remove everything this tool installed from\n{settings.TargetGameDir}\nand restore the files it replaced?\n\n(The dedicated server itself and your own mods are left alone.)",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBusy("Removing", ct => Task.Run(() =>
            {
                var target = settings.TargetGameDir;
                var inst = new Installer(target, this);
                foreach (var id in inst.State.components.Keys.ToList())
                {
                    var c = manifest.Get(id) ?? new Component { id = id, name = id };
                    inst.Uninstall(c);
                }
                foreach (var s in inst.State.synced.ToList())
                {
                    var p = Paths.Join(target, s.path);
                    if (File.Exists(p)) File.Delete(p);
                    inst.State.synced.Remove(s);
                }
                inst.State.Save(target);
                ServerConfig.ClearBlock(Path.Combine(target, "cfg", "server.cfg"));
                ServerConfig.ClearBlock(Path.Combine(target, "cfg", "listenserver.cfg"));
                ServerConfig.ClearBlock(Path.Combine(target, "cfg", "sourcemod", "l4dmultislots.cfg"));
                Info("Everything installed by this tool was removed.");
            }, ct));
            RefreshAll();
        }

        // ================================================================== compatibility

        ScanContext Context() => new ScanContext
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

        async Task RunScan(bool auto, bool quiet = false)
        {
            if (busy && auto) { rescanTimer.Start(); return; }
            var ctx = Context();
            lblSummary.Text = "Scanning...";
            List<Finding> result;
            try { result = await Task.Run(() => Compat.Scan(ctx)); }
            catch (Exception ex) { Error("Scan failed: " + ex.Message); lblSummary.Text = ""; return; }

            bool firstRun = seen.Count == 0;
            var fresh = new List<Finding>();
            foreach (var f in result.Where(f => !string.IsNullOrEmpty(f.Location) && (f.Kind == "Addon" || f.Kind == "Plugin")))
            {
                if (!seen.Contains(f.Location))
                {
                    if (!firstRun) { f.IsNew = true; fresh.Add(f); }
                    seen.Add(f.Location);
                }
            }
            if (!Preview) Json.Save(Path.Combine(Paths.AppData, "seen.json"), seen.ToList());

            findings = result;
            ShowFindings();

            int problems = result.Count(f => f.Severity == Severity.Problem), warnings = result.Count(f => f.Severity == Severity.Warning);
            if (!quiet || fresh.Count > 0)
                Info($"Compatibility scan: {result.Count(f => f.Kind == "Addon")} addon(s), {result.Count(f => f.Kind == "Plugin")} plugin(s): {problems} problem(s), {warnings} warning(s).");

            if (fresh.Count > 0)
            {
                foreach (var f in fresh) Info($"New {f.Kind.ToLower()} detected: {f.Name} → {Verdict(f)}");
                var head = fresh.Count == 1 ? $"New mod checked: {fresh[0].Name}" : $"{fresh.Count} new mods checked";
                var body = fresh.Count == 1 ? Verdict(fresh[0]) : $"{fresh.Count(f => f.Severity >= Severity.Warning)} need attention";
                tray.ShowBalloonTip(6000, head, body + " (click for details)", fresh.Any(f => f.Severity >= Severity.Warning) ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        }

        static string Verdict(Finding f) =>
            f.Severity == Severity.Problem ? "PROBLEM: " + f.Details.FirstOrDefault()
            : f.Severity == Severity.Warning ? "warning: " + f.Details.FirstOrDefault()
            : "compatible with 8 players";

        void ShowFindings()
        {
            lvFindings.BeginUpdate();
            lvFindings.Items.Clear();
            foreach (var f in findings)
            {
                string status = f.Severity == Severity.Problem ? "PROBLEM" : f.Severity == Severity.Warning ? "WARNING" : f.Severity == Severity.Info ? "NOTE" : "CLEAR";
                var item = new ListViewItem(new[] { status, f.Kind.ToUpperInvariant(), (f.IsNew ? "NEW · " : "") + f.Name, f.Summary ?? "" }) { Tag = f, UseItemStyleForSubItems = false, ToolTipText = f.Name + (string.IsNullOrEmpty(f.Summary) ? "" : Environment.NewLine + f.Summary) };
                var statusColor = f.Severity == Severity.Problem ? Theme.Hex("#ff8a7a") : f.Severity == Severity.Warning ? Theme.Amber : Theme.Muted;
                item.ForeColor = statusColor;
                item.SubItems[1].ForeColor = Theme.Muted;
                item.SubItems[2].ForeColor = Theme.Bone;
                item.SubItems[3].ForeColor = Theme.Muted;
                if (f.Severity == Severity.Problem) item.BackColor = Theme.ProblemBg;
                else if (f.Severity == Severity.Warning) item.BackColor = Theme.WarningBg;
                if (f.IsNew) item.Font = Theme.BodyBold(10f);
                lvFindings.Items.Add(item);
            }
            lvFindings.EndUpdate();
            int p = findings.Count(f => f.Severity == Severity.Problem), w = findings.Count(f => f.Severity == Severity.Warning);
            lblSummary.Text = p + w == 0 ? "No problems found" : $"{p} problem(s), {w} warning(s)";
            tileProblems.Count = p;
            tileWarnings.Count = w;
            tileClear.Count = findings.Count(f => f.Severity <= Severity.Info && (f.Kind == "Addon" || f.Kind == "Plugin"));
            nav.SetItem(1, p > 0 ? $"Mods ({p})" : "Mods");
            ShowFindingDetail(SelectedFinding());
            var latestNew = findings.FirstOrDefault(f => f.IsNew);
            if (latestNew != null)
            {
                newModTitle.Text = latestNew.Name;
                newModBody.Text = (latestNew.Summary ?? "") + " · " + Verdict(latestNew);
                newModNote.Visible = true;
            }
        }

        Finding SelectedFinding() => lvFindings.SelectedItems.Count > 0 ? (Finding)lvFindings.SelectedItems[0].Tag : null;

        async Task FixHighlighted()
        {
            var f = SelectedFinding();
            if (f == null || f.Fix == FixAction.None) { MessageBox.Show(this, "Highlight an addon or plugin in the list first.", Title); return; }
            if (f.Managed && f.Fix == FixAction.DisablePlugin &&
                MessageBox.Show(this, "This plugin is part of the 8-player setup. Disable it anyway?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Info(Compat.Apply(f, settings.ClientGameDir)); }
            catch (Exception ex) { Error(ex.Message); MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            await RunScan(auto: false, quiet: true);
        }

        void SetupWatchers()
        {
            foreach (var w in watchers) w.Dispose();
            watchers.Clear();
            if (!settings.WatchMods) return;

            void Watch(string dir, string filter, bool sub)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                var w = new FileSystemWatcher(dir, filter) { IncludeSubdirectories = sub, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => BeginInvoke((Action)(() => { rescanTimer.Stop(); rescanTimer.Start(); }));
                w.Created += h; w.Changed += h; w.Deleted += h;
                w.Renamed += (s, e) => h(s, e);
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }

            if (settings.ClientGameDir != null) Watch(Path.Combine(settings.ClientGameDir, "addons"), "*.vpk", true);
            var target = settings.TargetGameDir;
            if (target != null)
            {
                Watch(Path.Combine(target, "addons", "sourcemod", "plugins"), "*.smx", true);
                if (settings.Mode == "dedicated") Watch(Path.Combine(target, "addons"), "*.vpk", false);
            }
        }

        // ================================================================== play

        void UpdatePlayState()
        {
            bool listen = settings.Mode == "listen";
            bool running = serverProcess != null && !serverProcess.HasExited;
            grpServer.Text = listen ? "Listen server (uses your game)" : "Dedicated server";
            txtServerName.Enabled = numPort.Enabled = txtPassword.Enabled = cmbMap.Enabled = cmbMode.Enabled = !listen;
            if (listen) cmbMap.SelectionLength = 0;
            numPlayers.Maximum = listen ? 8 : 16;
            rbDirect.Enabled = rbLobby.Enabled = txtLobbyAddress.Enabled = chkSync.Enabled = chkUpdateOnStart.Enabled = !listen;
            btnStart.Visible = btnStop.Visible = !listen;
            btnStart.Enabled = !running && !busy;
            btnStop.Enabled = running;
            btnJoin.Kind = listen ? ButtonKind.Primary : ButtonKind.Outline;
            btnJoin.Text = listen ? "Launch L4D2 to host" : "Launch L4D2 & join";
            lblServerState.Text = listen ? "⚠ YOUR GAME RUNS -INSECURE (NO VAC) WHILE YOU HOST" : "✔ YOUR GAME STAYS VAC-SECURE · THE MODS RUN ON THE SERVER";
            lblServerState.ForeColor = listen ? Theme.Amber : Theme.Muted;
            rbLobby.Enabled = !listen && settings.Internet;
            txtLobbyAddress.Enabled = rbLobby.Enabled;
            lblNetworkNote.Text = NetworkNote();
            nav.Status = listen ? (gameWatch.Enabled ? "● Hosting from your game" : "Host from my game") : running ? $"● Server live · port {settings.Port}" : "○ Server offline";
            nav.StatusColor = running || gameWatch.Enabled ? Theme.Amber : Theme.Muted;
            txtJoinInfo.Text = JoinInstructions(publicIp);
            lblConnect.Text = $"connect {ShareHost()}:{(listen ? 27015 : settings.Port)}";
            lblCampaign.Text = CampaignTitle(cmbMap.Text);
            UpdateRoster();
        }

        string publicIp;

        /// <summary>The address friends should use for the current "who can join" mode.</summary>
        string ShareHost() =>
            settings.NetworkMode == "local" ? Launcher.LocalServerAddress()
            : settings.NetworkMode == "lan" ? Launcher.LanAddress()
            : publicIp ?? Launcher.LanAddress();

        string NetworkNote()
        {
            int port = settings.Mode == "listen" ? 27015 : settings.Port;
            switch (settings.NetworkMode)
            {
                case "lan": return $"Same network only · not listed online · router untouched · connect {Launcher.LanAddress()}:{port}";
                case "local": return settings.Mode == "listen"
                    ? "Just you (and your LAN) · for testing maps, mods and bot fill"
                    : "Just you · off the internet, nothing opened on your router · for testing maps, mods and bot fill";
                default: return $"Friends anywhere · port {port} opens on your router while you host";
            }
        }

        string JoinInstructions(string ip)
        {
            var nl = Environment.NewLine;
            var addr = (ip ?? "<your public IP>") + ":" + settings.Port;
            if (settings.NetworkMode == "local")
                return "JUST ME" + nl +
                       (settings.Mode == "listen" ? "1. Click 'Launch L4D2 to host', then start a game with Local Server." : "1. Click 'Start server', then 'Launch L4D2 & join'. You connect automatically.") + nl +
                       "2. Turn on Bot fill to see a full team of 8 survivors." + nl + nl +
                       "Use this to try new maps and mods before inviting friends. Switch to Internet or LAN only when they're ready to join." + nl + nl +
                       "The server stays off the internet. Someone on your own network could still join if they know your address; set a password to stop that.";
            if (settings.NetworkMode == "lan")
                return "LAN ONLY" + nl +
                       (settings.Mode == "listen" ? "1. Click 'Launch L4D2 to host'." : "1. Click 'Start server', then 'Launch L4D2 & join'.") + nl +
                       "2. Friends on the same network open the console (~) and type:" + nl + nl +
                       $"      connect {Launcher.LanAddress()}:{(settings.Mode == "listen" ? 27015 : settings.Port)}" + nl + nl +
                       "   Or they open L4D2-8P-Join.exe and click 'Find games on my network'." + nl + nl +
                       "The server stays off the internet: nothing is forwarded on your router.";
            if (settings.Mode == "listen")
                return "HOSTING FROM YOUR GAME" + nl +
                       "1. Click 'Launch L4D2 to host'. The tool switches the mods on and starts the game with -insecure." + nl +
                       "2. Create a lobby (with the 8 Slots Lobby Mod you get 8 slots), choose 'Local Server', invite friends and start." + nl +
                       "3. Players 5-8 get their own survivor automatically." + nl + nl +
                       "When you quit the game, the tool switches the mods off again. Don't launch from Steam directly while they are on: -insecure keeps you off VAC-secured servers." + nl + nl +
                       "Your friends only need the same campaign addons; they install nothing else.";
            if (settings.JoinMethod == "lobby")
                return "STEAM LOBBY" + nl +
                       "1. Start the server, then click 'Launch L4D2 and join'. The game starts with mm_dedicated_force_servers pointed at your server." + nl +
                       "2. Create a lobby (8 slots with the 8 Slots Lobby Mod), set Server Type to 'Best Available Dedicated', invite friends and start." + nl +
                       "3. Everyone is sent to your server. Once 4 players are in, l4d_unreservelobby opens the remaining slots." + nl + nl +
                       $"Friends who aren't in the lobby can still join with:  connect {addr}" + nl + nl +
                       "Internet play: the Internet hosting tab opens the router port for you and makes an invite link for friends.";
            return "DIRECT CONNECT" + nl +
                   "1. Click 'Start server', then 'Launch L4D2 and join'. You connect automatically." + nl +
                   "2. Friends open the console (~) and type:" + nl + nl +
                   $"      connect {addr}" + (string.IsNullOrEmpty(settings.Password) ? "" : $";password {settings.Password}") + nl + nl +
                   $"   Friends on the same network:  connect {Launcher.LanAddress()}:{settings.Port}" + nl + nl +
                   "Internet play: the Internet hosting tab opens the router port for you and makes an invite link for friends." + nl +
                   "Friends need no mods, only the same custom campaigns if you play one." + nl +
                   "Console not opening? Steam > Library > L4D2 > Properties > Launch Options:  -console";
        }

        async Task FindPublicIp()
        {
            try
            {
                Info("Looking up your public IP address (api.ipify.org)...");
                publicIp = await Launcher.PublicAddress();
                Info("Public IP: " + (publicIp ?? "unknown"));
                UpdatePlayState();
            }
            catch (Exception ex) { Error("Could not look up your public IP: " + ex.Message); }
        }

        void CopyConnect()
        {
            var cmd = $"connect {(publicIp ?? Launcher.LanAddress())}:{settings.Port}";
            Clipboard.SetText(cmd);
            Info("Copied: " + cmd + (publicIp == null ? "  (LAN address; click 'Find my public IP' for internet play)" : ""));
        }

        bool EnsureInstalled()
        {
            var target = settings.TargetGameDir;
            var ms = manifest.Get("multislots");
            if (target != null && Directory.Exists(target) && ms != null && new Installer(target, this).Status(ms) != ComponentStatus.NotInstalled) return true;
            if (MessageBox.Show(this, "The 8-player mods are not installed for this hosting mode yet. Go to Setup and install them now?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                tabs.SelectedIndex = 0;
            return false;
        }

        async Task<bool> PreflightScan()
        {
            if (!settings.ScanBeforeLaunch) return true;
            await RunScan(auto: false, quiet: true);
            var problems = findings.Where(f => f.Severity == Severity.Problem).ToList();
            if (problems.Count == 0) return true;
            var list = string.Join("\n", problems.Take(8).Select(p => "• " + p.Name + ": " + p.Details.FirstOrDefault(d => d.StartsWith("[Problem]"))?.Substring(10)));
            var r = MessageBox.Show(this, $"The compatibility check found {problems.Count} problem(s) that can break 8-player games:\n\n{list}\n\nLaunch anyway?",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) { tabs.SelectedIndex = 1; return false; }
            return true;
        }

        async Task StartServer()
        {
            ReadPlaySettings();
            if (!EnsureInstalled()) return;
            bool ok = await RunBusy("Preparing server", async ct =>
            {
                if (settings.UpdateServerOnStart) await SteamCmd.UpdateServer(settings.ServerRoot, settings.ServerInstallDir, this, ct);
                ServerConfig.Apply(settings, settings.TargetGameDir, customCampaigns);
                new Installer(settings.TargetGameDir, this).ApplySourceModGuard(manifest);
                if (settings.SyncAddons && GameLocator.IsGameDir(settings.GamePath))
                    await Task.Run(() => AddonSync.Sync(settings.ClientGameDir, settings.TargetGameDir, InstallState.Load(settings.TargetGameDir), this), ct);
            });
            if (!ok || !await PreflightScan()) return;

            try
            {
                serverProcess = Launcher.StartServer(settings);
                serverProcess.EnableRaisingEvents = true;
                serverProcess.Exited += (s, e) => { if (!IsDisposed) BeginInvoke((Action)(async () => { Info("Server stopped."); UpdatePlayState(); await AutoForward(false); })); };
                Info($"Server starting on port {settings.Port} ({settings.GameMode}, {settings.Map}, up to {settings.MaxPlayers} players" + (settings.FillWithBots ? ", bot fill on" : "") + "). Its console window shows the log.");
                _ = WatchServerHealth(serverProcess, DateTime.Now.AddSeconds(-2));
                await AutoForward(true);
            }
            catch (Exception ex) { Error(ex.Message); MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            UpdatePlayState();
        }

        List<ServerHealth.Issue> healthIssues;

        /// <summary>Once the server answers, checks SourceMod's logs for plugins that failed to load or broke.</summary>
        async Task WatchServerHealth(System.Diagnostics.Process proc, DateTime started)
        {
            healthIssues = null;
            UpdateHealthLink();
            var host = Launcher.LocalServerAddress();
            bool up = false;
            for (int i = 0; i < 60 && !proc.HasExited && !up; i++)
            {
                await Task.Delay(2000);
                try { up = await ServerQuery.Query(host, settings.Port) != null; } catch { }
            }
            if (!up || proc.HasExited) return;
            await Task.Delay(5000);   // plugins finish loading after the first map is up
            var issues = await Task.Run(() => ServerHealth.Check(settings.TargetGameDir, started, manifest));
            if (IsDisposed || proc.HasExited) return;
            healthIssues = issues;
            if (issues.Count == 0) Info("Server check: every plugin loaded cleanly.");
            else
            {
                Warn($"Server check: {issues.Count} plugin problem(s) on this start.");
                foreach (var i in issues) Warn("  " + i);
                Warn("This usually follows a game update. Check for updates on the Setup tab, then Download & install.");
            }
            UpdateHealthLink();
        }

        void UpdateHealthLink()
        {
            int n = healthIssues?.Count ?? 0;
            lnkHealth.Visible = n > 0;
            lnkHealth.Text = $"⚠ PLUGIN PROBLEMS ON THE LAST START ({n}) · DETAILS";
        }

        void ShowHealthDetails()
        {
            if (healthIssues == null || healthIssues.Count == 0) return;
            var text = string.Join(Environment.NewLine + Environment.NewLine, healthIssues.Select(i => "• " + i)) + Environment.NewLine + Environment.NewLine +
                "These usually follow a Left 4 Dead 2 update that a plugin hasn't caught up with yet. Check for updates on the Setup tab, " +
                "then Download & install. The rest of the server keeps running.\n\nOpen SourceMod's log folder?";
            if (MessageBox.Show(this, text, "Plugin problems on the last start", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var logs = Path.Combine(settings.TargetGameDir ?? "", "addons", "sourcemod", "logs");
                if (Directory.Exists(logs)) System.Diagnostics.Process.Start("explorer.exe", "\"" + logs + "\"");
            }
        }

        void StopServer()
        {
            Launcher.StopServer(serverProcess);
            UpdatePlayState();
        }

        async Task LaunchGame()
        {
            ReadPlaySettings();
            if (!GameLocator.IsGameDir(settings.GamePath)) { MessageBox.Show(this, "Set your Left 4 Dead 2 folder on the Setup tab first.", Title); return; }
            if (Launcher.GameRunning())
            {
                MessageBox.Show(this, "Left 4 Dead 2 is already running. Close it first so the launch options can be applied.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureInstalled()) return;

            if (settings.Mode == "listen")
            {
                ServerConfig.Apply(settings, settings.ClientGameDir, customCampaigns);
                if (!await PreflightScan()) return;
                var inst = new Installer(settings.ClientGameDir, this);
                inst.SetLoaders(manifest, true);
                Launcher.LaunchGame("-insecure " + Launcher.LanArg(settings));
                await AutoForward(true);
                gameSeen = false;
                gameLaunchAt = DateTime.Now;
                gameWatch.Start();
                Info("Mods switched on. L4D2 is starting with -insecure. They switch off again when you quit the game.");
                return;
            }

            if (serverProcess == null || serverProcess.HasExited)
            {
                if (MessageBox.Show(this, "The server isn't running. Start it first?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    await StartServer();
                    if (serverProcess == null || serverProcess.HasExited) return;
                    Info("Waiting for the server to load the map...");
                    // Joining before the map is up fails after 10 retries.
                    await Launcher.WaitUntilReady(settings, serverProcess, TimeSpan.FromSeconds(90));
                    if (serverProcess.HasExited) { Error("The server closed while loading. Check its console window."); return; }
                }
            }

            string args;
            if (settings.JoinMethod == "lobby" && settings.Internet)
            {
                var addr = settings.LobbyAddress;
                if (string.IsNullOrWhiteSpace(addr))
                {
                    if (publicIp == null) await FindPublicIp();
                    addr = (publicIp ?? Launcher.LanAddress()) + ":" + settings.Port;
                }
                args = "+mm_dedicated_force_servers " + addr;
                Info("Starting L4D2 for a lobby game pointed at " + addr + ". Create a lobby and choose 'Best Available Dedicated'.");
            }
            else
            {
                args = $"+connect {Launcher.LocalServerAddress()}:{settings.Port}" + (string.IsNullOrEmpty(settings.Password) ? "" : $" +password \"{settings.Password}\"");
                Info("Starting L4D2 and connecting to your server...");
            }
            try
            {
                // Started directly: while srcds runs, Steam thinks L4D2 is already open and ignores -applaunch.
                if (Launcher.LaunchGameDirect(settings.GamePath, args, dropInsecure: true))
                    Warn("Left -insecure out of your Steam launch options for this launch: an -insecure game can't join a VAC-secured server. " +
                         "Remove it in Steam (L4D2 > Properties > Launch options) if you don't need it.");
            }
            catch (Exception ex) { Error(ex.Message); }
        }

        void WatchListenGame()
        {
            bool running = Launcher.GameRunning();
            if (running) { gameSeen = true; return; }
            if (!gameSeen && DateTime.Now - gameLaunchAt < TimeSpan.FromMinutes(3)) return; // still starting
            gameWatch.Stop();
            new Installer(settings.ClientGameDir, this).SetLoaders(manifest, false);
            _ = AutoForward(false);
            Info(gameSeen ? "L4D2 closed: listen-server mods switched off." : "L4D2 didn't start within 3 minutes: listen-server mods switched off.");
        }

        // ================================================================== busy / logging

        async Task<bool> RunBusy(string label, Func<CancellationToken, Task> work)
        {
            if (busy) return false;
            busy = true;
            cts = new CancellationTokenSource();
            SetBusyUi(true, label);
            try
            {
                await work(cts.Token);
                return true;
            }
            catch (OperationCanceledException) { Warn("Cancelled."); return false; }
            catch (UnauthorizedAccessException ex)
            {
                Error(ex.Message);
                MessageBox.Show(this, "Windows denied access to a file:\n" + ex.Message + "\n\nClose the game and server, or run this tool as administrator if the game is in a protected folder.", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                busy = false;
                SetBusyUi(false, "Ready.");
                cts.Dispose();
                cts = null;
            }
        }

        void SetBusyUi(bool on, string label)
        {
            foreach (var b in new[] { btnInstall, btnCheckUpdates, btnUninstall, btnUninstallAll, btnScan, btnFix, btnJoin }) b.Enabled = !on;
            btnCancel.Enabled = on;
            lblStatus.Text = label + (on ? "..." : "");
            progress.Marquee = on;
            progress.Value = 0;
            UpdatePlayState();
        }

        void Append(string prefix, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => Append(prefix, message))); return; }
            txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {prefix}{message}{Environment.NewLine}");
        }

        public void Info(string message) => Append("", message);
        public void Warn(string message) => Append("⚠ ", message);
        public void Error(string message) => Append("✖ ", message);

        DateTime lastProgress;
        public void Progress(string label, double fraction)
        {
            if (DateTime.Now - lastProgress < TimeSpan.FromMilliseconds(100) && fraction < 1) return;
            lastProgress = DateTime.Now;
            if (InvokeRequired) { BeginInvoke((Action)(() => Progress(label, fraction))); return; }
            progress.Marquee = false;
            progress.Value = (int)Math.Max(0, Math.Min(100, fraction * 100));
            lblStatus.Text = $"{label} {fraction:P0}";
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (serverProcess != null && !serverProcess.HasExited)
            {
                var r = MessageBox.Show(this, "The server is still running. Stop it before closing?", Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) Launcher.StopServer(serverProcess);
            }
            if (gameWatch.Enabled && Launcher.GameRunning())
                MessageBox.Show(this, "L4D2 is still running with the listen-server mods switched on. They will be switched off the next time you open this tool.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (gameWatch.Enabled)
                new Installer(settings.ClientGameDir, this).SetLoaders(manifest, false);
            tray.Visible = false;
            tray.Dispose();
            foreach (var w in watchers) w.Dispose();
        }
    }
}
