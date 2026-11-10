using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.Join
{
    /// <summary>
    /// The Join side's tabs, for friends who only use this side: JOIN (the invite and the server), MODS (every add-on,
    /// on or off), SKINS (the skin picker the Host side has too) and SETTINGS (updates, the game folder, the HUD add-on,
    /// the tour and the app's files). The Mods and Skins tabs read your add-ons the first time they're shown, and
    /// again after a change on the other one or when add-ons are installed.
    /// </summary>
    public sealed partial class JoinForm
    {
        public const int TabJoin = 0, TabMods = 1, TabSkins = 2, TabSettings = 3;
        PageHost joinTabs;
        ModsPanel modsPanel;
        SkinsPanel skinsPanel;
        bool modsLoaded, skinsLoaded;
        /// <summary>The links under the Join tab (game folder, Workshop, HUD add-on, conflicts): the other tabs have their own.</summary>
        FlowLayoutPanel joinFooter;

        /// <summary>The page under the header: the join page given, then Mods, Skins and Settings.</summary>
        Control BuildTabs(Control joinPage)
        {
            joinTabs = new PageHost { Dock = DockStyle.Fill };
            joinTabs.Add(joinPage);
            modsPanel = new ModsPanel(() => ClientGameDir, Manifest.BuiltIn);
            skinsPanel = new SkinsPanel(() => ClientGameDir, Manifest.BuiltIn);
            // what one tab turns on or off, the other shows next time it's opened; the footer's conflict link too
            modsPanel.Changed += (s, e) => { skinsStale = true; _ = CheckConflicts(); };
            skinsPanel.Changed += (s, e) => { modsStale = true; _ = CheckConflicts(); };
            joinTabs.Add(modsPanel);
            joinTabs.Add(skinsPanel);
            joinTabs.Add(BuildSettingsPage());
            header.SelectedChanged += (s, e) => { if (joinTabs.SelectedIndex != header.Selected) joinTabs.SelectedIndex = header.Selected; };
            joinTabs.SelectedIndexChanged += async (s, e) => { header.Selected = joinTabs.SelectedIndex; joinFooter.Visible = joinTabs.SelectedIndex == TabJoin; await ShowTab(); };
            return joinTabs;
        }

        bool modsStale, skinsStale;

        async Task ShowTab()
        {
            switch (joinTabs.SelectedIndex)
            {
                case TabMods: if (!modsLoaded || modsStale) { modsLoaded = true; modsStale = false; await modsPanel.Reload(); } break;
                case TabSkins: if (!skinsLoaded || skinsStale) { skinsLoaded = true; skinsStale = false; await skinsPanel.Reload(); } break;
                case TabSettings: RefreshSettings(); break;
            }
        }

        /// <summary>Add-ons changed on disk (a download, a subscription): the open tab reads them again, the other one when shown.</summary>
        async Task AddonsChanged()
        {
            modsStale = skinsStale = true;
            if (joinTabs.SelectedIndex == TabMods || joinTabs.SelectedIndex == TabSkins) await ShowTab();
        }

        /// <summary>The Skins tab, on one target when given.</summary>
        internal async Task ShowSkins(string target = null)
        {
            if (target != null) skinsPanel.SelectTarget(target);
            joinTabs.SelectedIndex = TabSkins;
            await Task.CompletedTask;
        }

        // ================================================================== settings

        Label lblAppVersion, lblUpdateResult, lblGameFolder, lblHudState;
        ChoiceCard cardAutoUpdate, cardManualUpdate;
        GameCheck chkDownloadModsSetting;
        GameButton btnCheckApp, btnHud;

        Control BuildSettingsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Ground };
            Theme.DarkScrollbars(page);
            var g = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Padding = P(26, 20, 26, 20), Margin = P(0) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            const int W = 760;
            Label Line(string text, Font font, Color color, Padding margin) => new Label { Text = text, AutoSize = true, MaximumSize = Sz(W, 0), Font = font, ForeColor = color, Margin = margin, UseMnemonic = false };
            Control Row(params Control[] cs) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = P(0, 0, 0, 6) }; r.Controls.AddRange(cs); return r; }
            GameButton Button(string text, ButtonKind kind, Action click) { var b = new GameButton(text, kind, 10f) { Margin = P(0, 0, 10, 6) }; b.Click += (s, e) => click(); return b; }
            void Section(string title) => g.Controls.Add(Line(title, Theme.Stencil(13f), Theme.Bone, P(0, 18, 0, 6)));

            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 4) };
            head.Controls.Add(new TapeLabel("Settings") { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 6) });
            head.Controls.Add(new Label { Text = "SETTINGS", AutoSize = true, Font = Theme.Display(26f), ForeColor = Theme.Bone, Margin = P(0) });
            g.Controls.Add(head);

            // ---- updates
            Section("UPDATES");
            lblAppVersion = Line("", Theme.BodySemi(10.5f), Theme.Bone, P(0, 0, 0, 6));
            g.Controls.Add(lblAppVersion);
            btnCheckApp = Button("Check for updates", ButtonKind.Outline, async () =>
            {
                btnCheckApp.Enabled = false;
                lblUpdateResult.Text = "Asking GitHub…";
                lblUpdateResult.ForeColor = Theme.Muted;
                lblUpdateResult.Visible = true;
                try { lblUpdateResult.Text = await UpdateUi.CheckNow(); lblUpdateResult.ForeColor = Theme.Bone; }
                catch (Exception ex) { lblUpdateResult.Text = "Couldn't check: " + ex.Message; }
                finally { if (!IsDisposed) { btnCheckApp.Enabled = true; RefreshSettings(keepResult: true); } }
            });
            g.Controls.Add(Row(btnCheckApp, Button("What's new", ButtonKind.Ghost, () => Process.Start(UpdateCheck.ReleasePage))));
            lblUpdateResult = Line("", Theme.Body(10f), Theme.Bone, P(0, 0, 0, 8));
            g.Controls.Add(lblUpdateResult);
            var updates = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 4) };
            cardAutoUpdate = new ChoiceCard("Install updates automatically", "New versions download in the background and install when you close the app") { Width = S(W), Margin = P(0, 0, 0, 6) };
            cardManualUpdate = new ChoiceCard("Let me install updates myself", "The notice at the top says when a new version is out; you choose when to install it") { Width = S(W), Margin = P(0) };
            cardAutoUpdate.CheckedChanged += (s, e) => { if (cardAutoUpdate.Checked && !settingsLoading) Updater.SetAuto(true); };
            cardManualUpdate.CheckedChanged += (s, e) => { if (cardManualUpdate.Checked && !settingsLoading) Updater.SetAuto(false); };
            updates.Controls.Add(cardAutoUpdate);
            updates.Controls.Add(cardManualUpdate);
            g.Controls.Add(updates);

            // ---- the game
            Section("YOUR GAME");
            lblGameFolder = Line("", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6));
            g.Controls.Add(lblGameFolder);
            g.Controls.Add(Row(Button("Change game folder", ButtonKind.Outline, () => { BrowseGame(); RefreshSettings(); }),
                               Button("Open the addons folder", ButtonKind.Ghost, () => { var d = ClientGameDir == null ? null : Path.Combine(ClientGameDir, "addons"); if (d != null && Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\""); })));
            chkDownloadModsSetting = new GameCheck { Text = "Download a server's missing mods when I join (the required ones, and the cosmetic ones I tick)", AutoSize = true, MaximumSize = Sz(W, 0), Font = Theme.Body(10f), Margin = P(0, 6, 0, 10) };
            chkDownloadModsSetting.CheckedChanged += (s, e) =>
            {
                if (settingsLoading) return;
                settings.DownloadMods = chkDownloadModsSetting.Checked;
                if (!Preview) settings.Save();
                if (chkDownloadMods.Checked != settings.DownloadMods) chkDownloadMods.Checked = settings.DownloadMods;   // the Join tab's box
            };
            g.Controls.Add(chkDownloadModsSetting);
            lblHudState = Line("", Theme.Body(10f), Theme.Muted, P(0, 4, 0, 6));
            g.Controls.Add(lblHudState);
            btnHud = Button("Install the HUD add-on", ButtonKind.Outline, () => { ToggleHudAddon(); RefreshSettings(); });
            g.Controls.Add(Row(btnHud));

            // ---- help and files
            Section("HELP AND FILES");
            g.Controls.Add(Line("New here, or want a reminder? The tour walks through pasting the invite, checking the server, joining, and these tabs.", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
            g.Controls.Add(Line("Your settings, skin picks and logs are kept in " + Paths.AppData + ". Removing the app keeps them.", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
            g.Controls.Add(Row(
                Button("Show the tour", ButtonKind.Outline, () => { joinTabs.SelectedIndex = TabJoin; BeginInvoke((Action)StartTour); }),
                Button("Open the settings folder", ButtonKind.Ghost, () => Process.Start("explorer.exe", "\"" + Paths.AppData + "\"")),
                Button("Open the error log", ButtonKind.Ghost, () =>
                {
                    var log = Path.Combine(Paths.AppData, "errors.log");
                    if (File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
                    else MessageBox.Show(this, "No errors have been logged.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                Button("Uninstall…", ButtonKind.Ghost, () => Process.Start("ms-settings:appsfeatures"))));

            page.Controls.Add(g);
            RefreshSettings();
            return page;
        }

        bool settingsLoading;

        void RefreshSettings(bool keepResult = false)
        {
            if (lblAppVersion == null) return;
            settingsLoading = true;
            try
            {
                var last = UpdateCheck.Last;
                bool installedHere = Updater.RunningInstalled(Application.ExecutablePath);
                lblAppVersion.Text = $"L4D2 8-Player Co-op {AppInfo.Version}" + (last.Latest == null ? "" : $" · latest on GitHub: {last.Latest}, checked {Ago(last.CheckedUtc)}")
                    + (installedHere ? "" : Environment.NewLine + "This copy isn't the installed app (it runs from " + Path.GetDirectoryName(Application.ExecutablePath) + "), so updates open the release page instead of installing.");
                if (!keepResult) lblUpdateResult.Text = "";
                lblUpdateResult.Visible = lblUpdateResult.Text.Length > 0;
                // never chosen (installed before 2.5.8): by hand, as it was
                if (Updater.Auto) cardAutoUpdate.Checked = true; else cardManualUpdate.Checked = true;
                lblGameFolder.Text = settings.GamePath == null ? "Left 4 Dead 2 wasn't found." : "Left 4 Dead 2: " + settings.GamePath;
                chkDownloadModsSetting.Checked = settings.DownloadMods;
                var hud = HudAddon.Check(settings.GamePath);
                lblHudState.Text = hud == HudAddon.Status.Installed ? "The 8-Player HUD add-on is installed: hosts with the 8-Player HUD show everyone's health bars and items."
                    : hud == HudAddon.Status.Outdated ? "The 8-Player HUD add-on is out of date."
                    : hud == HudAddon.Status.Disabled ? "The 8-Player HUD add-on is installed but off in the game's Add-ons menu."
                    : hud == HudAddon.Status.NoGame ? "The 8-Player HUD add-on needs the game folder." : "The 8-Player HUD add-on isn't installed (optional: it shows everyone's health bars and items on hosts with the 8-Player HUD).";
                btnHud.Text = hud == HudAddon.Status.Installed ? "Remove the HUD add-on" : hud == HudAddon.Status.Outdated ? "Update the HUD add-on" : hud == HudAddon.Status.Disabled ? "Turn the HUD add-on on" : "Install the HUD add-on";
                btnHud.Size = btnHud.GetPreferredSize(Size.Empty);
                btnHud.Enabled = hud != HudAddon.Status.NoGame;
            }
            finally { settingsLoading = false; }
        }

        static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            return d.TotalMinutes < 2 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} minutes ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")} ago" : $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
        }
    }
}
