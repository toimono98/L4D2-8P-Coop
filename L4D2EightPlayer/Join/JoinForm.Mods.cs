using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.Join
{
    /// <summary>
    /// The server's mods (ServerMods): what the host's game uses, read with Check server. Required ones (campaigns,
    /// game-altering mods, animations) are marked REQUIRED; cosmetic ones (the host's skins, sounds) can be ticked.
    /// Join game downloads what's missing first (Download missing mods when I join), straight from Steam.
    /// </summary>
    public sealed partial class JoinForm
    {
        List<ServerMods.Item> serverMods = new List<ServerMods.Item>();
        readonly Dictionary<string, CheckBox> cosmeticPicks = new Dictionary<string, CheckBox>();
        TableLayoutPanel modsList;
        Label modsSummary;
        GameCheck chkDownloadMods;

        Control BuildModsPanel()
        {
            var box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = P(0, 4, 0, 6), Visible = false };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = P(0) };
            head.Controls.Add(Caps("Mods on this server"));
            modsSummary = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(10, 1, 0, 0) };
            head.Controls.Add(modsSummary);
            box.Controls.Add(head, 0, 0);
            chkDownloadMods = new GameCheck { Text = "Download missing mods when I join", AutoSize = true, Font = Theme.Body(10f), Checked = settings.DownloadMods, Margin = P(0, 4, 0, 6) };
            chkDownloadMods.CheckedChanged += (s, e) => { settings.DownloadMods = chkDownloadMods.Checked; settings.Save(); ShowMods(); };
            new ToolTip().SetToolTip(chkDownloadMods, "Required mods you don't have, and the cosmetic ones you tick, download from Steam before the game starts.\n" +
                "They go into your game's addons folder (8p_<number>.vpk). If you subscribe to one later, this copy is removed.");
            box.Controls.Add(chkDownloadMods, 0, 1);
            // every mod is listed in full: the card grows and the page scrolls (FitServerCard), no box inside a box
            modsList = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Margin = P(0) };
            modsList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            box.Controls.Add(modsList, 0, 2);
            return box;
        }

        /// <summary>The server's list (its rules), what each is (Steam), and what this game already has.</summary>
        async Task LoadServerMods(JoinTarget t)
        {
            serverMods = new List<ServerMods.Item>();
            try
            {
                ServerMods.Tidy(ClientGameDir);
                serverMods = ServerMods.FromRules(await ServerQuery.Rules(t.Host, t.Port, 3000));
                if (serverMods.Count > 0) await ServerMods.Describe(serverMods, ClientGameDir);
            }
            catch { /* no list, or Steam unreachable: titles stay as numbers */ }
            foreach (var m in serverMods) if (m.Title == null) m.Title = "Workshop item " + m.Id;
            ShowMods();
        }

        void ShowMods()
        {
            var panel = modsList.Parent;
            panel.Visible = serverMods.Count > 0;
            if (serverMods.Count == 0) { FitServerCard(); return; }
            var picked = new HashSet<string>(cosmeticPicks.Where(kv => kv.Value.Checked).Select(kv => kv.Key));
            cosmeticPicks.Clear();
            modsList.SuspendLayout();
            modsList.Controls.Clear();
            modsList.RowStyles.Clear();
            var req = serverMods.Where(m => m.Required).ToList();
            var cos = serverMods.Where(m => !m.Required).ToList();
            int reqMissing = req.Count(m => !m.Have);
            modsSummary.Text = $"{req.Count} required" + (reqMissing > 0 ? $" ({reqMissing} missing)" : "") + $" · {cos.Count} cosmetic";
            modsSummary.ForeColor = reqMissing > 0 ? Theme.Amber : Theme.Muted;
            foreach (var m in req)
            {
                var row = new Label
                {
                    AutoSize = true, MaximumSize = Sz(660, 0), UseMnemonic = false, Font = Theme.Body(9.5f), Margin = P(0, 1, 0, 2),
                    ForeColor = m.Have ? Theme.Muted : Theme.Amber,
                    Text = $"REQUIRED   {m.Title} · {SizeText(m.Size)} · " + (m.Have ? "you have it" : m.Url == null ? "missing: subscribe on the Workshop" : settings.DownloadMods ? "downloads when you join" : "missing"),
                };
                modsList.Controls.Add(row);
            }
            foreach (var m in cos)
            {
                if (m.Have)
                {
                    modsList.Controls.Add(new Label { AutoSize = true, MaximumSize = Sz(660, 0), UseMnemonic = false, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 1, 0, 2), Text = $"✔  {m.Title} · you have it" });
                    continue;
                }
                var c = new GameCheck { Text = $"{m.Title} · {SizeText(m.Size)} · cosmetic", AutoSize = true, Font = Theme.Body(9.5f), Margin = P(0, 0, 0, 0), Checked = picked.Contains(m.Id), Enabled = m.Url != null };
                c.UseMnemonic = false;
                cosmeticPicks[m.Id] = c;
                modsList.Controls.Add(c);
            }
            modsList.ResumeLayout();
            FitServerCard();
        }

        static string SizeText(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024)} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        /// <summary>
        /// Before Join game starts the game: the missing required mods (with the option on) and the ticked cosmetic ones,
        /// downloaded. False stops the join: a required one is missing and can't be had here.
        /// </summary>
        async Task<bool> GetServerMods()
        {
            if (Preview || ClientGameDir == null) return true;
            var missingRequired = serverMods.Where(m => m.Required && !m.Have).ToList();
            if (missingRequired.Count > 0 && !settings.DownloadMods)
            {
                var list = string.Join("\n", missingRequired.Select(m => "• " + m.Title));
                var r = MessageBox.Show(this, $"This server needs mods you don't have:\n\n{list}\n\nWithout them you'll be sent back, or see missing models. Download them now?",
                    Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Cancel) return false;
                if (r == DialogResult.No) return true;
            }
            var todo = missingRequired.Concat(serverMods.Where(m => !m.Required && !m.Have && cosmeticPicks.TryGetValue(m.Id, out var c) && c.Checked)).ToList();
            if (todo.Count == 0) return true;

            var failed = new List<string>();
            btnJoin.Enabled = false;
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    var m = todo[i];
                    var progress = new ButtonProgress(this, $"Downloading {i + 1}/{todo.Count}");
                    lblNotes.Text = $"Downloading {m.Title} ({SizeText(m.Size)}) from Steam…";
                    lblNotes.ForeColor = Theme.Bone;
                    lblNotes.Visible = true;
                    try { await ServerMods.Download(ClientGameDir, m, progress, CancellationToken.None); }
                    catch (Exception ex) { failed.Add(m.Title + (m.Required ? " (required)" : "") + ": " + ex.Message); }
                }
            }
            finally { btnJoin.Enabled = true; btnJoin.Text = "Join game"; }
            ShowMods();
            if (failed.Count == 0) return true;
            bool requiredFailed = todo.Any(m => m.Required && !m.Have);
            var msg = "Some mods didn't download:\n\n" + string.Join("\n", failed.Select(f => "• " + f)) +
                      (requiredFailed ? "\n\nSubscribe to the required ones on the Workshop (Open Workshop below), start the game once, then join." : "");
            MessageBox.Show(this, msg, Title, MessageBoxButtons.OK, requiredFailed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return !requiredFailed;
        }

        /// <summary>Download progress on the Join button.</summary>
        sealed class ButtonProgress : ILog
        {
            readonly JoinForm form;
            readonly string label;
            public ButtonProgress(JoinForm form, string label) { this.form = form; this.label = label; }
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Progress(string what, double fraction)
            {
                if (form.IsDisposed) return;
                if (form.InvokeRequired) { form.BeginInvoke((Action)(() => Progress(what, fraction))); return; }
                form.btnJoin.Text = $"{label} · {fraction:P0}";
            }
        }
    }
}
