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
    /// Before a launch: add-ons that are on together and replace the same files, so the game would show part of
    /// each. For every pair the player keeps one (the other is turned off) or both; Launch waits for a choice on each.
    /// </summary>
    sealed class ClashDialog : Form
    {
        const int MaxPairs = 3;
        readonly List<Clashes.Clash> clashes;
        readonly List<ChoiceCard[]> choices = new List<ChoiceCard[]>();
        readonly GameButton launch;

        public ClashDialog(List<Clashes.Clash> clashes)
        {
            this.clashes = clashes.Take(MaxPairs).ToList();
            Text = "Add-ons";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Bone;
            Font = Theme.Body(10f);
            Icon = Theme.AppIcon();

            const int width = 560;
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(24, 20, 24, 16), BackColor = Theme.Panel, Location = Point.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(width)));
            t.Controls.Add(new Label { Text = this.clashes.Count == 1 ? "Two add-ons replace the same files" : "Add-ons that replace the same files", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Display(15f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 8) });
            t.Controls.Add(new Label { Text = "The game uses one copy of each file, and which add-on's copy isn't predictable, so with both on you'd see part of each. Choose what to keep.", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 6) });

            var tips = new ToolTip();
            foreach (var c in this.clashes)
            {
                t.Controls.Add(new Label { Text = $"{c.A.Title}  +  {c.B.Title}", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.BodyBold(10.5f), ForeColor = Theme.Bone, Margin = P(0, 12, 0, 2) });
                t.Controls.Add(new Label { Text = $"Both replace {c.Files.Count} file{(c.Files.Count == 1 ? "" : "s")}: {c.What}.", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 6) });
                var keeper = c.A.Protected ? c.A : c.B.Protected ? c.B : null;
                if (keeper != null)
                    t.Controls.Add(new Label { Text = $"{keeper.Title} is one the app needs, so it stays on.", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 6) });

                // one card per line, so long Workshop names fit; each pair is its own radio group
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = P(0), BackColor = Theme.Panel };
                ChoiceCard Card(string title, string detail, string tip)
                {
                    var card = new ChoiceCard(title, detail) { Width = S(width), Margin = P(0, 0, 0, 6) };
                    tips.SetToolTip(card, tip);
                    card.CheckedChanged += (s, e) => UpdateLaunch();
                    row.Controls.Add(card);
                    return card;
                }
                // keeping one turns the other off, and the add-ons the app needs are never turned off
                var keepA = c.B.Protected ? null : Card("Keep " + c.A.Title, "Turns off " + c.B.Title, $"Keep {c.A.Title} and turn off {c.B.Title}.");
                var keepB = c.A.Protected ? null : Card("Keep " + c.B.Title, "Turns off " + c.A.Title, $"Keep {c.B.Title} and turn off {c.A.Title}.");
                var both = Card("Keep both", "You'd see a mix of the two, and won't be asked about this pair again", "Leave both on. You won't be asked about this pair again while they share the same files.");
                choices.Add(new[] { keepA, keepB, both });
                t.Controls.Add(row);
            }
            if (clashes.Count > MaxPairs)
                t.Controls.Add(new Label { Text = $"…and {clashes.Count - MaxPairs} more, asked about next time.", AutoSize = true, Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 8, 0, 0) });
            t.Controls.Add(new Label { Text = "Turned-off add-ons stay installed. Turn them back on in the game's Add-ons menu.", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 14, 0, 12) });

            launch = new GameButton("Launch", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0), Enabled = false };
            var cancel = new GameButton("Cancel", ButtonKind.Ghost, 10.5f) { DialogResult = DialogResult.Cancel, Margin = P(0) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0), BackColor = Theme.Panel };
            buttons.Controls.Add(launch);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            Controls.Add(t);
            AcceptButton = launch;
            CancelButton = cancel;
            ActiveControl = cancel;   // nothing chosen until the player picks
            ClientSize = t.GetPreferredSize(Size.Empty);
            Theme.Style(this);
        }

        void UpdateLaunch() => launch.Enabled = choices.All(row => row.Any(c => c != null && c.Checked));

        /// <summary>For the screenshot check: a choice made on the first pair.</summary>
        internal void Choose(int pair, int option) { var c = choices[pair][option]; if (c != null) c.Checked = true; }

        IEnumerable<Clashes.Addon> TurnOff()
        {
            for (int i = 0; i < clashes.Count; i++)
            {
                if (choices[i][0]?.Checked == true) yield return clashes[i].B;   // kept A
                if (choices[i][1]?.Checked == true) yield return clashes[i].A;   // kept B
            }
        }

        IEnumerable<Clashes.Clash> KeptBoth() => clashes.Where((c, i) => choices[i][2].Checked);

        /// <summary>Asks when add-ons on in the game folder clash. False when the user cancelled (the launch stops).</summary>
        public static bool Check(IWin32Window owner, string gameDir, Manifest manifest)
        {
            List<Clashes.Clash> found;
            SkinPrefs prefs;
            try { prefs = SkinPrefs.Load(); found = Clashes.Find(gameDir, manifest, prefs); }
            catch { return true; }   // a launch never fails over this
            if (found.Count == 0) return true;
            using (var d = new ClashDialog(found))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                Clashes.Resolve(gameDir, d.TurnOff().ToList(), d.KeptBoth().ToList(), prefs);
                return true;
            }
        }
    }
}
