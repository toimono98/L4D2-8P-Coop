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
            ("setup-dedicated", async () => { tabs.SelectedIndex = 0; AuditMode("dedicated"); await Task.CompletedTask; }),
            ("setup-component-highlighted", async () => { AuditSelect(lvComponents, i => i.Tag is Component); ShowComponentInfo(); await Task.CompletedTask; }),
            ("setup-listen", async () => { AuditMode("listen"); await Task.CompletedTask; }),
            ("mods-nothing-selected", async () => { AuditMode("dedicated"); tabs.SelectedIndex = 1; AuditSelect(lvFindings, i => false); ShowFindingDetail(null); await Task.CompletedTask; }),
            ("mods-problem-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Severity == Severity.Problem); await Task.CompletedTask; }),
            ("mods-clear-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Severity == Severity.Ok); await Task.CompletedTask; }),
            ("mods-requirement-selected", async () => { AuditSelect(lvFindings, i => ((Finding)i.Tag).Kind == "Requirement"); await Task.CompletedTask; }),
            ("play-internet-direct-fill-off", async () => { tabs.SelectedIndex = 2; cmbNetwork.SelectedItem = "internet"; rbDirect.Checked = true; chkFill.Checked = false; numPlayers.Value = 8; await Task.CompletedTask; }),
            ("play-internet-lobby-fill-on", async () => { rbLobby.Checked = true; chkFill.Checked = true; await Task.CompletedTask; }),
            ("play-lan", async () => { cmbNetwork.SelectedItem = "lan"; await Task.CompletedTask; }),
            ("play-just-me", async () => { cmbNetwork.SelectedItem = "local"; await Task.CompletedTask; }),
            ("play-5-players", async () => { cmbNetwork.SelectedItem = "internet"; rbDirect.Checked = true; numPlayers.Value = 5; await Task.CompletedTask; }),
            ("play-16-players", async () => { numPlayers.Value = 16; await Task.CompletedTask; }),
            ("play-difficulty-expert", async () => { numPlayers.Value = 8; cmbDifficulty.SelectedItem = "Impossible"; await Task.CompletedTask; }),
            ("play-listen-mode", async () => { cmbDifficulty.SelectedItem = "Normal"; AuditMode("listen"); tabs.SelectedIndex = 2; await Task.CompletedTask; }),
            ("play-campaign-dropdown-open", async () => { AuditMode("dedicated"); tabs.SelectedIndex = 2; cmbCampaign.Focus(); cmbCampaign.DroppedDown = true; await Task.CompletedTask; }),
            ("play-chapter-dropdown-open", async () => { cmbCampaign.DroppedDown = false; cmbCampaign.SelectedIndex = 10; cmbChapter.Focus(); cmbChapter.DroppedDown = true; await Task.CompletedTask; }),
            ("play-mode-dropdown-open", async () => { cmbChapter.DroppedDown = false; cmbMode.Focus(); cmbMode.DroppedDown = true; await Task.CompletedTask; }),
            ("play-host-survivor-zoey", async () => { cmbMode.DroppedDown = false; cmbHostCharacter.SelectedIndex = 6; await Task.CompletedTask; }),
            ("internet-checked", async () => { cmbMode.DroppedDown = false; tabs.SelectedIndex = 3; await CheckNetwork(verbose: false); }),
            ("internet-lan-mode", async () => { cmbNetwork.SelectedItem = "lan"; await CheckNetwork(verbose: false); }),
            ("internet-just-me", async () => { cmbNetwork.SelectedItem = "local"; await CheckNetwork(verbose: false); }),
            ("internet-listen-mode", async () => { cmbNetwork.SelectedItem = "internet"; AuditMode("listen"); tabs.SelectedIndex = 3; await CheckNetwork(verbose: false); }),
        };
    }
}
