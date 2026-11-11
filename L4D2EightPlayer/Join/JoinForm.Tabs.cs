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
            // the page, the footer (only under Join) and the tab's highlight change in one paint
            header.SelectedChanged += (s, e) => { if (joinTabs.SelectedIndex != header.Selected) Draw.Batch(this, () => joinTabs.SelectedIndex = header.Selected); };
            joinTabs.SelectedIndexChanged += async (s, e) =>
            {
                Draw.Batch(this, () => { header.Selected = joinTabs.SelectedIndex; joinFooter.Visible = joinTabs.SelectedIndex == TabJoin; });
                await ShowTab();
            };
            return joinTabs;
        }

        bool modsStale, skinsStale;

        /// <summary>The Mods and Skins tabs read ahead (a moment after the window opens), so their first visit shows them whole.</summary>
        async Task ReadAhead()
        {
            if (Preview || Audit.On) return;
            await Task.Delay(1500);
            if (IsDisposed) return;
            if (!modsLoaded) { modsLoaded = true; await modsPanel.Reload(); }
            if (!skinsLoaded) { skinsLoaded = true; await skinsPanel.Reload(); }
        }

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

        SettingsPage settingsPage;
        Label lblGameFolder, lblHudState;
        GameCheck chkDownloadModsSetting;
        GameButton btnHud;

        /// <summary>The shared Settings page, with the Join side's own section: your game.</summary>
        Control BuildSettingsPage()
        {
            settingsPage = new SettingsPage(
                "New here, or want a reminder? The tour walks through pasting the invite, checking the server, joining, and these tabs.",
                () => { joinTabs.SelectedIndex = TabJoin; BeginInvoke((Action)StartTour); },
                p =>
                {
                    p.Section("YOUR GAME");
                    lblGameFolder = p.Note("");
                    p.Add(SettingsPage.Row(
                        SettingsPage.Button("Change game folder", ButtonKind.Outline, () => { BrowseGame(); RefreshSettings(); }),
                        SettingsPage.Button("Open the addons folder", ButtonKind.Ghost, () => { var d = ClientGameDir == null ? null : Path.Combine(ClientGameDir, "addons"); if (d != null && Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\""); })));
                    chkDownloadModsSetting = p.Check("Download a server's missing mods when I join (the required ones, and the cosmetic ones I tick)", on =>
                    {
                        settings.DownloadMods = on;
                        if (!Preview) settings.Save();
                        if (chkDownloadMods.Checked != on) chkDownloadMods.Checked = on;   // the Join tab's box
                    });
                    lblHudState = p.Note("");
                    btnHud = SettingsPage.Button("Install the HUD add-on", ButtonKind.Outline, () => { ToggleHudAddon(); RefreshSettings(); });
                    p.Add(SettingsPage.Row(btnHud));
                });
            settingsPage.Refreshing += (s, e) => RefreshGameSection();
            RefreshGameSection();
            return settingsPage;
        }

        void RefreshSettings() => settingsPage?.RefreshAll();

        void RefreshGameSection()
        {
            if (lblGameFolder == null) return;
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
    }
}