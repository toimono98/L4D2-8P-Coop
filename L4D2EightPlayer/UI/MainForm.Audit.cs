using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// Every screen state the layout audit visits (see SelfTest --audit). Nothing here saves settings
    /// or touches the network beyond the read-only router check: Preview and Settings.ReadOnly are on.
    /// </summary>
    public sealed partial class MainForm
    {
        void AuditMode(string mode)
        {
            loading = true;
            rbDedicated.Checked = mode != "listen";
            rbListen.Checked = mode == "listen";
            loading = false;
            settings.Mode = mode;
            RefreshAll();
        }

        void AuditSelect(ListView lv, Func<ListViewItem, bool> pick)
        {
            foreach (ListViewItem i in lv.Items) i.Selected = false;
            var item = lv.Items.Cast<ListViewItem>().FirstOrDefault(pick);
            if (item != null) { item.Selected = true; item.EnsureVisible(); }
        }

        internal List<(string name, Func<Task> act)> AuditSteps() => new List<(string, Func<Task>)>
        {
            ("setup-dedicated", async () => { tabs.SelectedIndex = TabSetup; AuditMode("dedicated"); await Task.CompletedTask; }),
            // Advanced opened by its button, as a click does: the component list appears (its window is made) for the first time
            ("setup-advanced-open", async () => { btnAdvanced.PerformClick(); await Task.Delay(500); }),
            ("setup-component-highlighted", async () => { AuditSelect(lvComponents, i => i.Tag is Component); ShowComponentInfo(); await Task.CompletedTask; }),
            ("setup-listen", async () => { AuditMode("listen"); await Task.CompletedTask; }),
            ("mods-nothing-selected", async () => { AuditMode("dedicated"); tabs.SelectedIndex = TabMods; AuditSelect(lvFindings, i => false); ShowFindingDetail(null); await Task.CompletedTask; }),
            ("mods-problem-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Severity == Severity.Problem); await Task.CompletedTask; }),
            ("mods-clear-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Severity == Severity.Ok); await Task.CompletedTask; }),
            ("mods-requirement-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Kind == "Requirement"); await Task.CompletedTask; }),
            ("play-internet-direct-fill-off", async () => { tabs.SelectedIndex = TabPlay; cmbNetwork.SelectedItem = "internet"; rbDirect.Checked = true; chkFill.Checked = false; numPlayers.Value = 8; await Task.CompletedTask; }),
            ("play-internet-lobby-fill-on", async () => { rbLobby.Checked = true; chkFill.Checked = true; await Task.CompletedTask; }),
            ("play-lan", async () => { cmbNetwork.SelectedItem = "lan"; await Task.CompletedTask; }),
            ("play-just-me", async () => { cmbNetwork.SelectedItem = "local"; await Task.CompletedTask; }),
            ("play-5-players", async () => { cmbNetwork.SelectedItem = "internet"; rbDirect.Checked = true; numPlayers.Value = 5; await Task.CompletedTask; }),
            ("play-16-players", async () => { numPlayers.Value = 16; await Task.CompletedTask; }),
            ("play-difficulty-expert", async () => { numPlayers.Value = 8; cmbDifficulty.SelectedItem = "Impossible"; await Task.CompletedTask; }),
            ("play-listen-mode", async () => { cmbDifficulty.SelectedItem = "Normal"; AuditMode("listen"); tabs.SelectedIndex = TabPlay; await Task.CompletedTask; }),
            ("play-campaign-dropdown-open", async () => { AuditMode("dedicated"); tabs.SelectedIndex = TabPlay; cmbCampaign.Focus(); cmbCampaign.DroppedDown = true; await Task.CompletedTask; }),
            ("play-chapter-dropdown-open", async () => { cmbCampaign.DroppedDown = false; cmbCampaign.SelectedIndex = 10; cmbChapter.Focus(); cmbChapter.DroppedDown = true; await Task.CompletedTask; }),
            ("play-mode-dropdown-open", async () => { cmbChapter.DroppedDown = false; cmbMode.Focus(); cmbMode.DroppedDown = true; await Task.CompletedTask; }),
            ("play-host-survivor-zoey", async () => { cmbMode.DroppedDown = false; PickHostCharacter(5); await Task.CompletedTask; }),
            ("play-host-zoey-5-players", async () => { numPlayers.Value = 5; await Task.CompletedTask; }),
            ("play-host-any-again", async () => { PickHostCharacter(5); numPlayers.Value = 8; await Task.CompletedTask; }),
            ("play-more-settings", async () => { cmbMode.DroppedDown = false; if (!moreSettings.Visible) btnMore.PerformClick(); await Task.CompletedTask; }),
            ("play-internet-checked", async () => { if (moreSettings.Visible) btnMore.PerformClick(); cmbNetwork.SelectedItem = "internet"; await CheckNetwork(verbose: false); }),
            ("play-lan-mode", async () => { cmbNetwork.SelectedItem = "lan"; await Task.CompletedTask; }),
            ("play-just-me-mode", async () => { cmbNetwork.SelectedItem = "local"; await Task.CompletedTask; }),
            ("play-listen-internet", async () => { cmbNetwork.SelectedItem = "internet"; AuditMode("listen"); await CheckNetwork(verbose: false); }),
            ("skins-tab", async () => { AuditMode("dedicated"); tabs.SelectedIndex = TabSkins; await skins.Reload(); }),
            ("skins-tab-bottom", async () => { if (skins.TargetList is ScrollableControl sl) sl.AutoScrollPosition = new System.Drawing.Point(0, 100000); await Task.Delay(200); }),
            ("skins-packs", async () => { skins.ShowPacks("add"); await Task.Delay(200); }),
            ("settings-tab", async () => { tabs.SelectedIndex = TabSettings; await Task.CompletedTask; }),
            // the narrowest window: the five tabs and HOST | JOIN must all still fit in the top bar
            ("narrow-window-skins", async () => { var top = TopLevelControl as Form ?? this; top.WindowState = FormWindowState.Normal; top.Size = top.MinimumSize; await Task.Delay(300); }),
            ("narrow-window-skin-packs", async () => { tabs.SelectedIndex = TabSkins; skins.ShowPacks(); await Task.Delay(200); }),
            ("narrow-window-play", async () => { tabs.SelectedIndex = TabPlay; await Task.CompletedTask; }),
        };
    }
}
