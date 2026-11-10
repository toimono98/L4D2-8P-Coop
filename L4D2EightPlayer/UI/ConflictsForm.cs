using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// Disable conflicting: every pair of add-ons that are on and replace the same files with different copies, each
    /// with Keep one / Keep the other / Keep both, the app's suggestion already chosen. Below, the harmless overlaps
    /// (the same copy of a file in both), which the game's Add-ons menu marks as conflicts too.
    /// </summary>
    sealed class ConflictsForm : Form
    {
        const int ColumnWidth = 600, MaxHarmless = 8;
        readonly List<Clashes.Clash> clashes;
        readonly List<ChoiceCard[]> choices = new List<ChoiceCard[]>();
        readonly GameButton apply;

        public ConflictsForm(List<Clashes.Clash> clashes, List<Clashes.Clash> harmless, SkinPrefs prefs)
        {
            this.clashes = clashes;
            Text = "Disable conflicting add-ons";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Bone;
            Font = Theme.Body(10f);
            Icon = Theme.AppIcon();

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(24, 20, 24, 16), BackColor = Theme.Panel, Location = Point.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(ColumnWidth)));
            Label Line(string text, Font font, Color color, Padding margin) => new Label { Text = text, AutoSize = true, MaximumSize = Sz(ColumnWidth, 0), Font = font, ForeColor = color, Margin = margin, UseMnemonic = false };
            t.Controls.Add(Line("Disable conflicting add-ons", Theme.Display(16f), Theme.Bone, P(0, 0, 0, 4)));
            t.Controls.Add(Line(clashes.Count == 0 ? "No add-ons that are on replace the same files with different copies."
                : "These add-ons are on together and replace the same files with different copies. The game uses one copy of each file, so you'd see part of each. " +
                  "The suggested choice is picked for each pair: change any of them, then turn them off.", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 4)));

            // made first: picking each pair's suggestion below updates its text
            apply = new GameButton("Turn off", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0), AutoSizeLocked = true };
            var tips = new ToolTip();
            foreach (var c in clashes)
            {
                t.Controls.Add(Line($"{c.A.Title}  +  {c.B.Title}", Theme.BodyBold(10.5f), Theme.Bone, P(0, 14, 0, 2)));
                t.Controls.Add(Line($"Both replace {c.Files.Count} file{(c.Files.Count == 1 ? "" : "s")}: {c.What}." + (c.Same > 0 ? $" {c.Same} more are the same in both (harmless)." : ""), Theme.Body(9.5f), Theme.Muted, P(0, 0, 0, 6)));
                var keeper = c.A.Protected ? c.A : c.B.Protected ? c.B : null;
                if (keeper != null) t.Controls.Add(Line($"{keeper.Title} is one the app needs, so it stays on.", Theme.Body(9.5f), Theme.Amber, P(0, 0, 0, 6)));
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = P(0), BackColor = Theme.Panel };
                ChoiceCard Card(string title, string detail, string tip)
                {
                    var card = new ChoiceCard(title, detail) { Width = S(ColumnWidth), Margin = P(0, 0, 0, 6) };
                    tips.SetToolTip(card, tip);
                    card.CheckedChanged += (s, e) => UpdateApply();
                    row.Controls.Add(card);
                    return card;
                }
                var keepA = c.B.Protected ? null : Card("Keep " + c.A.Title, "Turns off " + c.B.Title, $"Keep {c.A.Title} and turn off {c.B.Title}.");
                var keepB = c.A.Protected ? null : Card("Keep " + c.B.Title, "Turns off " + c.A.Title, $"Keep {c.B.Title} and turn off {c.A.Title}.");
                var both = Card("Keep both", "You'd see a mix of the two, and this pair isn't counted again", "Leave both on. This pair isn't listed again while they share the same files.");
                choices.Add(new[] { keepA, keepB, both });
                // the suggestion: never the add-on the app needs; the skin you picked, a campaign, the Workshop copy stay
                var off = Clashes.Suggest(c, prefs);
                var pick = off == c.B ? keepA : keepB;
                if (pick != null) pick.Checked = true; else both.Checked = true;
                t.Controls.Add(row);
            }
            if (clashes.Count > 0) t.Controls.Add(Line("Turned-off add-ons stay installed. Turn them back on in the game's Add-ons menu, or in the skin picker.", Theme.Body(9.5f), Theme.Muted, P(0, 6, 0, 0)));

            if (harmless.Count > 0)
            {
                t.Controls.Add(Line("HARMLESS OVERLAPS", Theme.BodyBold(8.5f), Theme.Muted, P(0, 18, 0, 2)));
                t.Controls.Add(Line("These share files that are the same in both, often parts of one campaign or the same mod installed twice. The game's Add-ons menu may show them as conflicting; nothing is wrong, so they're left alone.",
                    Theme.Body(9.5f), Theme.Muted, P(0, 0, 0, 6)));
                foreach (var c in harmless.Take(MaxHarmless))
                    t.Controls.Add(Line($"{c.A.Title}  +  {c.B.Title}  ·  {c.Same} file{(c.Same == 1 ? "" : "s")} the same", Theme.Body(9.5f), Theme.Bone, P(0, 0, 0, 3)));
                if (harmless.Count > MaxHarmless) t.Controls.Add(Line($"…and {harmless.Count - MaxHarmless} more.", Theme.Body(9.5f), Theme.Muted, P(0)));
            }

            var cancel = new GameButton(clashes.Count == 0 ? "Close" : "Cancel", ButtonKind.Ghost, 10.5f) { DialogResult = DialogResult.Cancel, Margin = P(0) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0, 18, 0, 0), BackColor = Theme.Panel };
            if (clashes.Count > 0) buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);

            // a long list scrolls instead of running off the screen
            var host = new Panel { AutoScroll = true, BackColor = Theme.Panel, Dock = DockStyle.Fill };
            host.Controls.Add(t);
            Controls.Add(host);
            Theme.DarkScrollbars(host);
            UpdateApply();
            var size = t.GetPreferredSize(Size.Empty);
            var max = Screen.FromPoint(Cursor.Position).WorkingArea.Height - S(80);
            ClientSize = new Size(size.Width + (size.Height > max ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(size.Height, max));
            AcceptButton = clashes.Count > 0 ? (IButtonControl)apply : cancel;
            CancelButton = cancel;
            Shown += (s, e) => host.AutoScrollPosition = Point.Empty;
            Theme.Style(this);
        }

        void UpdateApply()
        {
            int n = TurnOff().Select(a => a.Key.ToLowerInvariant()).Distinct().Count();
            apply.Text = n == 0 ? "Keep them all" : $"Turn off {n} add-on{(n == 1 ? "" : "s")}";
            apply.Size = apply.GetPreferredSize(Size.Empty);
        }

        public IEnumerable<Clashes.Addon> TurnOff()
        {
            for (int i = 0; i < choices.Count; i++)   // the pairs made so far, while the window is being built
            {
                if (choices[i][0]?.Checked == true) yield return clashes[i].B;   // kept A
                if (choices[i][1]?.Checked == true) yield return clashes[i].A;   // kept B
            }
        }

        public IEnumerable<Clashes.Clash> KeptBoth() => clashes.Where((c, i) => i < choices.Count && choices[i][2].Checked);

        /// <summary>
        /// Looks at a game folder, shows the conflicts and applies the choices. The game has to be closed (it rewrites
        /// its add-on list when it exits). Returns the number of add-ons turned off, or -1 when cancelled.
        /// </summary>
        public static int Run(IWin32Window owner, string gameDir, Manifest manifest)
        {
            if (gameDir == null) return -1;
            if (Launcher.GameRunning())
            {
                MessageBox.Show(owner, "Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.", "Disable conflicting", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return -1;
            }
            var prefs = SkinPrefs.Load();
            var survey = Clashes.Look(gameDir, manifest);
            var real = Clashes.Conflicts(survey, prefs);
            var harmless = survey.Harmless.Where(c => c.BothOn).ToList();
            using (var d = new ConflictsForm(real, harmless, prefs))
            {
                if (d.ShowDialog(owner) != DialogResult.OK || real.Count == 0) return -1;
                var off = d.TurnOff().ToList();
                Clashes.Resolve(gameDir, off, d.KeptBoth().ToList(), prefs);
                return off.Select(a => a.Key.ToLowerInvariant()).Distinct().Count();
            }
        }
    }
}
