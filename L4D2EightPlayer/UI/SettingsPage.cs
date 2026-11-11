using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The Settings tab, on both sides: UPDATES (the version, Check for updates, What's new, automatic or by hand),
    /// then the side's own sections (the Join side: your game; the Host side: the app), then HELP AND FILES (the
    /// tour, the settings folder, the error log, uninstalling). Shown is refreshed each time the tab is opened.
    /// </summary>
    public sealed class SettingsPage : Panel
    {
        public const int Width760 = 760;
        readonly TableLayoutPanel g;
        readonly Label lblAppVersion, lblUpdateResult;
        readonly ChoiceCard cardAuto, cardManual;
        readonly GameButton btnCheck;
        /// <summary>True while the page is filling its boxes from the settings (their change events aren't the user's).</summary>
        public bool Loading { get; private set; }
        /// <summary>Each refresh, after the updates section: the side's own boxes.</summary>
        public event EventHandler Refreshing;

        /// <param name="tourText">What the tour covers, for its line in Help and files.</param>
        /// <param name="showTour">Starts the tour (from its button).</param>
        /// <param name="addSections">Adds the side's own sections, between Updates and Help and files.</param>
        public SettingsPage(string tourText, Action showTour, Action<SettingsPage> addSections)
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Theme.Ground;
            Theme.DarkScrollbars(this);
            g = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Padding = P(26, 20, 26, 20), Margin = P(0) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 4) };
            head.Controls.Add(new TapeLabel("Settings") { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 6) });
            head.Controls.Add(new Label { Text = "SETTINGS", AutoSize = true, Font = Theme.Display(26f), ForeColor = Theme.Bone, Margin = P(0) });
            g.Controls.Add(head);

            // ---- updates
            Section("UPDATES");
            lblAppVersion = Add(Line("", Theme.BodySemi(10.5f), Theme.Bone, P(0, 0, 0, 6)));
            btnCheck = Button("Check for updates", ButtonKind.Outline, async () =>
            {
                btnCheck.Enabled = false;
                lblUpdateResult.Text = "Asking GitHub…";
                lblUpdateResult.ForeColor = Theme.Muted;
                lblUpdateResult.Visible = true;
                try { lblUpdateResult.Text = await UpdateUi.CheckNow(); lblUpdateResult.ForeColor = Theme.Bone; }
                catch (Exception ex) { lblUpdateResult.Text = "Couldn't check: " + ex.Message; }
                finally { if (!IsDisposed) { btnCheck.Enabled = true; RefreshAll(keepResult: true); } }
            });
            Add(Row(btnCheck, Button("What's new", ButtonKind.Ghost, () => Process.Start(UpdateCheck.ReleasePage))));
            lblUpdateResult = Add(Line("", Theme.Body(10f), Theme.Bone, P(0, 0, 0, 8)));
            var updates = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 4) };
            cardAuto = new ChoiceCard("Install updates automatically", "New versions download in the background and install when you close the app") { Width = S(Width760), Margin = P(0, 0, 0, 6) };
            cardManual = new ChoiceCard("Let me install updates myself", "The notice at the top says when a new version is out; you choose when to install it") { Width = S(Width760), Margin = P(0) };
            cardAuto.CheckedChanged += (s, e) => { if (cardAuto.Checked && !Loading) Updater.SetAuto(true); };
            cardManual.CheckedChanged += (s, e) => { if (cardManual.Checked && !Loading) Updater.SetAuto(false); };
            updates.Controls.Add(cardAuto);
            updates.Controls.Add(cardManual);
            Add(updates);

            addSections?.Invoke(this);

            // ---- help and files
            Section("HELP AND FILES");
            Add(Line(tourText, Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
            Add(Line("Your settings, skin picks and logs are kept in " + Paths.AppData + ". Removing the app keeps them.", Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
            Add(Row(
                Button("Show the tour", ButtonKind.Outline, showTour),
                Button("Open the settings folder", ButtonKind.Ghost, () => Process.Start("explorer.exe", "\"" + Paths.AppData + "\"")),
                Button("Open the error log", ButtonKind.Ghost, () =>
                {
                    var log = Path.Combine(Paths.AppData, "errors.log");
                    if (File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
                    else MessageBox.Show(FindForm(), "No errors have been logged.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                Button("Uninstall…", ButtonKind.Ghost, () => Process.Start("ms-settings:appsfeatures"))));

            Controls.Add(g);
            // a narrow window: the text and the two update choices follow its width instead of running past it
            g.SizeChanged += (s, e) =>
            {
                int w = Math.Max(S(300), Math.Min(S(Width760), g.ClientSize.Width - g.Padding.Horizontal));
                foreach (var l in g.Controls.OfType<Label>()) if (l.MaximumSize.Width != w) l.MaximumSize = new Size(w, 0);
                foreach (var c in new[] { cardAuto, cardManual }) if (c.Width != w) c.Width = w;
                foreach (var c in g.Controls.OfType<GameCheck>()) if (c.MaximumSize.Width != w) c.MaximumSize = new Size(w, 0);
            };
            RefreshAll();
        }

        // ------------------------------------------------------------------ pieces the sides use

        public T Add<T>(T c) where T : Control { g.Controls.Add(c); return c; }
        public void Section(string title) => Add(Line(title, Theme.Stencil(13f), Theme.Bone, P(0, 18, 0, 6)));
        public static Label Line(string text, Font font, Color color, Padding margin) =>
            new Label { Text = text, AutoSize = true, MaximumSize = Sz(Width760, 0), Font = font, ForeColor = color, Margin = margin, UseMnemonic = false };
        public Label Note(string text) => Add(Line(text, Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
        public static Control Row(params Control[] cs) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = P(0, 0, 0, 6) }; r.Controls.AddRange(cs); return r; }
        public static GameButton Button(string text, ButtonKind kind, Action click) { var b = new GameButton(text, kind, 10f) { Margin = P(0, 0, 10, 6) }; b.Click += (s, e) => click(); return b; }
        public GameCheck Check(string text, Action<bool> changed)
        {
            var c = new GameCheck { Text = text, AutoSize = true, MaximumSize = Sz(Width760, 0), Font = Theme.Body(10f), Margin = P(0, 6, 0, 10) };
            c.CheckedChanged += (s, e) => { if (!Loading) changed(c.Checked); };
            return Add(c);
        }

        /// <summary>Fills every box from the current settings (opening the tab, and after a check).</summary>
        public void RefreshAll(bool keepResult = false)
        {
            Loading = true;
            try
            {
                var last = UpdateCheck.Last;
                bool installedHere = Updater.RunningInstalled(Application.ExecutablePath);
                lblAppVersion.Text = $"L4D2 8-Player Co-op {AppInfo.Version}" + (last.Latest == null ? "" : $" · latest on GitHub: {last.Latest}, checked {Ago(last.CheckedUtc)}")
                    + (installedHere ? "" : Environment.NewLine + "This copy isn't the installed app (it runs from " + Path.GetDirectoryName(Application.ExecutablePath) + "), so updates open the release page instead of installing.");
                if (!keepResult) lblUpdateResult.Text = "";
                lblUpdateResult.Visible = lblUpdateResult.Text.Length > 0;
                // never chosen (installed before 2.5.8): by hand, as it was
                if (Updater.Auto) cardAuto.Checked = true; else cardManual.Checked = true;
                Refreshing?.Invoke(this, EventArgs.Empty);
            }
            finally { Loading = false; }
        }

        static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            return d.TotalMinutes < 2 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} minutes ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")} ago" : $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
        }
    }
}
