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
    /// "Before you launch": everything worth a look before the game starts, in one window instead of a question
    /// for each. Skins for the same thing that are on together (Choose skins goes to the picker), add-ons that replace
    /// the same files (keep one or both: Launch waits for a choice on each pair), and the compatibility check's
    /// problems (Open the Mods tab). Launching keeps the overlapping skins and applies the choices.
    /// </summary>
    sealed class LaunchReview : Form
    {
        public enum Outcome { Launch, Cancel, ChooseSkins, OpenMods }
        const int MaxPairs = 3, MaxListed = 6, ColumnWidth = 560;

        readonly List<Skins.TargetState> overlaps;
        readonly List<Clashes.Clash> clashes;
        readonly List<ChoiceCard[]> choices = new List<ChoiceCard[]>();
        readonly GameButton launch;
        public Outcome Result { get; private set; } = Outcome.Cancel;

        public LaunchReview(List<Skins.TargetState> overlaps, List<Clashes.Clash> clashes, List<string> problems, bool modsButton)
        {
            this.overlaps = overlaps ?? new List<Skins.TargetState>();
            this.clashes = (clashes ?? new List<Clashes.Clash>()).Take(MaxPairs).ToList();
            problems = problems ?? new List<string>();
            Text = "Before you launch";
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
            Label Line(string text, Font font, Color color, Padding margin) => new Label { Text = text, AutoSize = true, MaximumSize = Sz(ColumnWidth, 0), Font = font, ForeColor = color, Margin = margin };
            t.Controls.Add(Line("Before you launch", Theme.Display(16f), Theme.Bone, P(0, 0, 0, 4)));
            t.Controls.Add(Line("A few things would show up wrong, or not at all, in game. Sort them out here, or launch as they are.", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 4)));

            void Section(string title, string note)
            {
                t.Controls.Add(Line(title.ToUpperInvariant(), Theme.BodyBold(8.5f), Theme.Amber, P(0, 16, 0, 2)));
                if (note != null) t.Controls.Add(Line(note, Theme.Body(9.5f), Theme.Muted, P(0, 0, 0, 6)));
            }

            if (this.overlaps.Count > 0)
            {
                Section("More than one skin for the same thing", "The game shows only one of them, and which one isn't predictable. Launching keeps them as they are (not asked again until another turns up).");
                foreach (var s in this.overlaps.Take(MaxListed))
                    t.Controls.Add(Line($"{s.Target.Name}:  {string.Join(", ", s.On.Select(m => m.Title))}", Theme.Body(10f), Theme.Bone, P(0, 0, 0, 3)));
                if (this.overlaps.Count > MaxListed) t.Controls.Add(Line($"…and {this.overlaps.Count - MaxListed} more.", Theme.Body(9.5f), Theme.Muted, P(0)));
                var choose = new GameButton("Choose skins", ButtonKind.Outline, 10f) { Margin = P(0, 6, 0, 0) };
                choose.Click += (s, e) => { Result = Outcome.ChooseSkins; DialogResult = DialogResult.Cancel; };
                t.Controls.Add(choose);
            }

            if (this.clashes.Count > 0)
            {
                Section("Add-ons that replace the same files", "The game uses one copy of each file, so with both on you'd see part of each. Choose what to keep for each pair.");
                var tips = new ToolTip();
                foreach (var c in this.clashes)
                {
                    t.Controls.Add(Line($"{c.A.Title}  +  {c.B.Title}", Theme.BodyBold(10.5f), Theme.Bone, P(0, 8, 0, 2)));
                    t.Controls.Add(Line($"Both replace {c.Files.Count} file{(c.Files.Count == 1 ? "" : "s")}: {c.What}.", Theme.Body(9.5f), Theme.Muted, P(0, 0, 0, 6)));
                    var keeper = c.A.Protected ? c.A : c.B.Protected ? c.B : null;
                    if (keeper != null) t.Controls.Add(Line($"{keeper.Title} is one the app needs, so it stays on.", Theme.Body(9.5f), Theme.Amber, P(0, 0, 0, 6)));
                    // one card per line, so long Workshop names fit; each pair is its own radio group
                    var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = P(0), BackColor = Theme.Panel };
                    ChoiceCard Card(string title, string detail, string tip)
                    {
                        var card = new ChoiceCard(title, detail) { Width = S(ColumnWidth), Margin = P(0, 0, 0, 6) };
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
                if (clashes.Count > MaxPairs) t.Controls.Add(Line($"…and {clashes.Count - MaxPairs} more, asked about next time.", Theme.Body(9.5f), Theme.Muted, P(0, 4, 0, 0)));
                t.Controls.Add(Line("Turned-off add-ons stay installed. Turn them back on in the game's Add-ons menu.", Theme.Body(9.5f), Theme.Muted, P(0, 4, 0, 0)));
            }

            if (problems.Count > 0)
            {
                Section("Compatibility problems", $"The compatibility check found {problems.Count} problem{(problems.Count == 1 ? "" : "s")} that can break 8-player games.");
                foreach (var p in problems.Take(MaxListed)) t.Controls.Add(Line("• " + p, Theme.Body(10f), Theme.Bone, P(0, 0, 0, 3)));
                if (problems.Count > MaxListed) t.Controls.Add(Line($"…and {problems.Count - MaxListed} more.", Theme.Body(9.5f), Theme.Muted, P(0)));
                if (modsButton)
                {
                    var mods = new GameButton("Open the Mods tab", ButtonKind.Outline, 10f) { Margin = P(0, 6, 0, 0) };
                    mods.Click += (s, e) => { Result = Outcome.OpenMods; DialogResult = DialogResult.Cancel; };
                    t.Controls.Add(mods);
                }
            }

            launch = new GameButton(problems.Count > 0 ? "Launch anyway" : "Launch", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0) };
            launch.Click += (s, e) => Result = Outcome.Launch;
            var cancel = new GameButton("Cancel", ButtonKind.Ghost, 10.5f) { DialogResult = DialogResult.Cancel, Margin = P(0) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0, 18, 0, 0), BackColor = Theme.Panel };
            buttons.Controls.Add(launch);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);

            // a long review scrolls instead of running off the screen
            var host = new Panel { AutoScroll = true, BackColor = Theme.Panel, Dock = DockStyle.Fill };
            host.Controls.Add(t);
            Controls.Add(host);
            Theme.DarkScrollbars(host);
            var size = t.GetPreferredSize(Size.Empty);
            var max = Screen.FromPoint(Cursor.Position).WorkingArea.Height - S(80);
            ClientSize = new Size(size.Width + (size.Height > max ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(size.Height, max));
            AcceptButton = launch;
            CancelButton = cancel;
            ActiveControl = cancel;   // nothing chosen until the player picks
            Shown += (s, e) => host.AutoScrollPosition = Point.Empty;   // a long review opens at its top
            UpdateLaunch();
            Theme.Style(this);
        }

        void UpdateLaunch() => launch.Enabled = choices.All(row => row.Any(c => c != null && c.Checked));

        /// <summary>For the screenshot check: a choice made on a pair.</summary>
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

        /// <summary>
        /// Looks at a game folder (null: skip the add-on checks) and the compatibility problems given, and asks when there's
        /// anything. Launch applies the choices. Returns what the player chose, and the first overlapping target for Choose skins.
        /// </summary>
        public static (Outcome outcome, string target) Check(IWin32Window owner, string gameDir, Manifest manifest, List<string> problems, bool modsButton)
        {
            var overlaps = new List<Skins.TargetState>();
            var clashes = new List<Clashes.Clash>();
            SkinPrefs prefs = null;
            if (gameDir != null)
                try
                {
                    prefs = SkinPrefs.Load();
                    overlaps = Skins.Overlaps(gameDir, manifest, prefs);
                    clashes = Clashes.Find(gameDir, manifest, prefs);
                }
                catch { }   // a launch never fails over this
            if (overlaps.Count == 0 && clashes.Count == 0 && (problems == null || problems.Count == 0)) return (Outcome.Launch, null);
            using (var d = new LaunchReview(overlaps, clashes, problems, modsButton))
            {
                d.ShowDialog(owner);
                if (d.Result == Outcome.Launch && prefs != null)
                {
                    if (overlaps.Count > 0) Skins.KeepOverlaps(overlaps, prefs);
                    Clashes.Resolve(gameDir, d.TurnOff().ToList(), d.KeptBoth().ToList(), prefs);
                }
                return (d.Result, overlaps.FirstOrDefault()?.Target.Id);
            }
        }
    }
}
