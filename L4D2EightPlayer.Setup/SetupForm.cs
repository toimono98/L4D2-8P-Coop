using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.Setup
{
    /// <summary>Three steps: how will you play (host or join), where it goes, then installing and done.</summary>
    sealed class SetupForm : Form
    {
        const string Title = "L4D2 8-Player Co-op Setup";
        readonly Panel body = new Panel { Dock = DockStyle.Fill };
        NavBar header;
        PathCard cardHost, cardJoin;
        SetupEngine.App chosen;
        TextBox txtFolder;
        CheckBox chkStart, chkDesktop, chkHud, chkOpen, chkTour;
        readonly string game = GameLocator.FindGame();

        public SetupForm(string preselect = null)
        {
            Theme.LoadFonts();
            Text = Title + " " + AppInfo.Version;
            Font = Theme.Body(10f);
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = Sz(780, 600);
            Icon = Theme.AppIcon();
            header = new NavBar() { Title = "8-PLAYER CO-OP", Subtitle = "LEFT 4 DEAD 2 · SETUP" };
            Controls.Add(body);
            Controls.Add(header);
            chosen = SetupEngine.ById(preselect);
            ShowChoose();
        }

        protected override void OnLoad(EventArgs e)
        {
            Theme.Style(body);
            base.OnLoad(e);
        }

        // ------------------------------------------------------------------ step 1

        public void ShowChoose()
        {
            var g = Page(out var buttons);
            g.Controls.Add(Heading("How will you play?"), 0, 0);
            var existing = SetupEngine.Existing();
            g.Controls.Add(Note(existing == null
                ? "Pick one. Only one person in your group hosts; everyone else joins. You can add the other one later by running this setup again."
                : "Already installed here: " + string.Join(" and ", existing.Value.apps.ConvertAll(a => SetupEngine.ById(a)?.Shortcut ?? a)) +
                  ". Pick one to update it, or add the other."), 0, 1);

            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = P(0, 6, 0, 0) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cardHost = new PathCard("Host a game", "FOR ONE PERSON IN YOUR GROUP",
                "Run the server everyone plays on. Installs the 8-Player Manager, which downloads and sets up the server mods, starts the server and gives you an invite for your friends.") { Dock = DockStyle.Fill, Margin = P(0, 0, 8, 0) };
            cardJoin = new PathCard("Join a friend", "FOR EVERYONE ELSE",
                "Play on your host's server. Installs the small Join app: paste the host's invite, check the server, join. No mods, and your game stays as it is.") { Dock = DockStyle.Fill, Margin = P(8, 0, 0, 0) };
            cards.Controls.Add(cardHost, 0, 0);
            cards.Controls.Add(cardJoin, 1, 0);
            g.Controls.Add(cards, 0, 2);

            var next = new GameButton("Next", ButtonKind.Primary, 12f) { Enabled = chosen != null };
            cardHost.Selected = chosen == SetupEngine.Host;
            cardJoin.Selected = chosen == SetupEngine.Join;
            cardHost.Click += (s, e) => { chosen = SetupEngine.Host; cardHost.Selected = true; cardJoin.Selected = false; next.Enabled = true; };
            cardJoin.Click += (s, e) => { chosen = SetupEngine.Join; cardJoin.Selected = true; cardHost.Selected = false; next.Enabled = true; };
            cardHost.DoubleClick += (s, e) => ShowOptions();
            cardJoin.DoubleClick += (s, e) => ShowOptions();
            next.Click += (s, e) => ShowOptions();
            buttons.Controls.Add(next);
            buttons.Controls.Add(Btn("Cancel", ButtonKind.Ghost, () => Close()));
            if (existing != null) buttons.Controls.Add(Btn("Uninstall", ButtonKind.Ghost, async () => await DoUninstall()));
            AcceptButton = next;
        }

        async Task DoUninstall()
        {
            if (MessageBox.Show(this, "Remove " + SetupEngine.Product + " (the Manager and the Join app) from this PC?\n\nYour settings, the dedicated server and your game stay as they are.",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var g = Page(out var buttons, fillRow: 5);
            g.Controls.Add(Heading("Removing…"), 0, 0);
            string error = null;
            await Task.Run(() => { try { SetupEngine.Uninstall(_ => { }); } catch (Exception ex) { error = ex.Message; } });
            if (error != null) { ShowError(error); return; }
            g = Page(out buttons, fillRow: 5);
            g.Controls.Add(Heading("Removed"), 0, 0);
            g.Controls.Add(Note(SetupEngine.Product + " is gone from this PC: the apps, their shortcuts and the Installed apps entry. Your settings, the dedicated server and your game are as they were."), 0, 1);
            var close = new GameButton("Close", ButtonKind.Primary, 12f);
            close.Click += (s, e) => Close();
            buttons.Controls.Add(close);
            AcceptButton = close;
        }

        // ------------------------------------------------------------------ step 2

        public void ShowOptions()
        {
            if (chosen == null) return;
            var g = Page(out var buttons, fillRow: 5);
            g.Controls.Add(Heading(chosen == SetupEngine.Host ? "Install the 8-Player Manager" : "Install the Join app"), 0, 0);
            g.Controls.Add(Note(game != null
                ? "Left 4 Dead 2 found in " + game + "."
                : "Left 4 Dead 2 wasn't found. Install it from Steam first, or point the app at it later."), 0, 1);

            var opts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = P(0, 10, 0, 0) };
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.Controls.Add(new Label { Text = "INSTALL FOLDER", AutoSize = true, Font = Theme.BodyBold(8.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 4) }, 0, 0);
            txtFolder = new TextBox { Dock = DockStyle.Fill, Font = Theme.Body(10f), Text = SetupEngine.DefaultFolder, Margin = P(0, 4, 10, 10) };
            opts.Controls.Add(txtFolder, 0, 1);
            var browse = Btn("Browse", ButtonKind.Ghost, () =>
            {
                using (var d = new FolderBrowserDialog { Description = "Where should " + chosen.Shortcut + " go?", SelectedPath = txtFolder.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = Path.Combine(d.SelectedPath, Path.GetFileName(d.SelectedPath).Equals(SetupEngine.Product, StringComparison.OrdinalIgnoreCase) ? "" : SetupEngine.Product);
            });
            browse.Margin = P(0, 0, 0, 10);
            opts.Controls.Add(browse, 1, 1);
            g.Controls.Add(opts, 0, 2);

            var checks = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 6, 0, 0) };
            chkStart = new GameCheck { Text = "Add it to the Start menu", Checked = true, AutoSize = true, Font = Theme.Body(10.5f), Margin = P(0, 0, 0, 6) };
            chkDesktop = new GameCheck { Text = "Put a shortcut on the Desktop", Checked = true, AutoSize = true, Font = Theme.Body(10.5f), Margin = P(0, 0, 0, 6) };
            checks.Controls.Add(chkStart);
            checks.Controls.Add(chkDesktop);
            chkHud = new GameCheck
            {
                Text = "Also install the 8-Player HUD add-on into Left 4 Dead 2 (shows a host's health bars and item icons)",
                AutoSize = true, MaximumSize = Sz(700, 0), Font = Theme.Body(10.5f), Margin = P(0, 0, 0, 6),
                Checked = game != null && HudAddon.Check(game) != HudAddon.Status.Installed,
                Enabled = game != null,
                Visible = chosen == SetupEngine.Join,
            };
            checks.Controls.Add(chkHud);
            g.Controls.Add(checks, 0, 3);
            g.Controls.Add(Note(chosen == SetupEngine.Host
                ? "Next, the Manager walks you through setting up the server: its Setup tab downloads everything it needs."
                : "Next, paste the invite your host sends you into the Join app."), 0, 4);

            var install = new GameButton("Install", ButtonKind.Primary, 12f);
            install.Click += async (s, e) => await DoInstall();
            buttons.Controls.Add(install);
            buttons.Controls.Add(Btn("Back", ButtonKind.Ghost, ShowChoose));
            AcceptButton = install;
        }

        // ------------------------------------------------------------------ step 3

        async Task DoInstall()
        {
            var o = new SetupEngine.Options
            {
                App = chosen,
                Folder = txtFolder.Text.Trim(),
                StartMenu = chkStart.Checked,
                Desktop = chkDesktop.Checked,
                HudAddon = chkHud.Visible && chkHud.Enabled && chkHud.Checked,
            };
            if (o.HudAddon && Launcher.GameRunning())
            {
                MessageBox.Show(this, "Close Left 4 Dead 2 first: the HUD add-on can't be installed while it runs. Or untick it; the Join app can install it later.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // a first install gets the tour ticked; an update or reinstall doesn't
            bool firstTime = SetupEngine.Existing()?.apps.Contains(o.App.Id) != true;
            var g = Page(out var buttons, fillRow: 5);
            g.Controls.Add(Heading("Installing…"), 0, 0);
            var meter = new Meter { Dock = DockStyle.Top, Height = S(10), Marquee = true, Margin = P(0, 14, 0, 14) };
            g.Controls.Add(meter, 0, 1);
            var status = Note("");
            g.Controls.Add(status, 0, 2);
            string error = null;
            await Task.Run(() =>
            {
                try { SetupEngine.Install(o, m => BeginInvoke((Action)(() => status.Text = m))); }
                catch (Exception ex) { error = ex.Message; }
            });
            if (error != null) { ShowError(error); return; }
            ShowDone(o, firstTime);
        }

        void ShowError(string error)
        {
            var g = Page(out var buttons, fillRow: 5);
            g.Controls.Add(Heading("That didn't work"), 0, 0);
            g.Controls.Add(Note(error), 0, 1);
            buttons.Controls.Add(Btn("Back", ButtonKind.Outline, ShowOptions));
            buttons.Controls.Add(Btn("Close", ButtonKind.Ghost, () => Close()));
        }

        internal void ShowDone(SetupEngine.Options o, bool firstTime = true)
        {
            var g = Page(out var buttons, fillRow: 5);
            g.Controls.Add(Heading("Ready"), 0, 0);
            g.Controls.Add(Note(o.App == SetupEngine.Host
                ? "The 8-Player Manager is installed in " + o.Folder + ". Open it and follow its Setup tab: it finds your game, downloads the server and the mods, and checks everything before your first game."
                : "The Join app is installed in " + o.Folder + ". Open it, paste the invite your host sends you, click Check server, then Join game." +
                  (o.HudAddon ? " The HUD add-on is in your game too." : "")), 0, 1);
            g.Controls.Add(Note("To remove it later, open Uninstall " + SetupEngine.Product + " in the install folder" + (o.StartMenu ? " or the Start menu" : "") +
                ", or use Windows' Installed apps." + (o.App == SetupEngine.Host ? " Your settings stay, and so does the server." : " Your settings stay.")), 0, 2);
            var opens = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 16, 0, 0) };
            chkOpen = new GameCheck { Text = "Open " + o.App.Shortcut + " now", Checked = true, AutoSize = true, Font = Theme.Body(10.5f), Margin = P(0, 0, 0, 6) };
            chkTour = new GameCheck
            {
                Text = o.App == SetupEngine.Host
                    ? "Show me around: a one-minute tour of setting up the server, checking mods and inviting friends"
                    : "Show me around: a quick tour of pasting the invite, checking the server and joining",
                Checked = firstTime, AutoSize = true, MaximumSize = Sz(700, 0), Font = Theme.Body(10.5f), Margin = P(28, 0, 0, 0),
            };
            chkOpen.CheckedChanged += (s, e) => chkTour.Enabled = chkOpen.Checked;
            opens.Controls.Add(chkOpen);
            opens.Controls.Add(chkTour);
            g.Controls.Add(opens, 0, 3);
            var finish = new GameButton("Finish", ButtonKind.Primary, 12f);
            finish.Click += (s, e) =>
            {
                if (chkOpen.Checked)
                    Process.Start(new ProcessStartInfo(Path.Combine(o.Folder, o.App.Exe), chkTour.Checked ? "--tour" : "--no-tour") { WorkingDirectory = o.Folder });
                Close();
            };
            buttons.Controls.Add(finish);
            AcceptButton = finish;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Clears the body and returns the page grid (rows: heading, notes, content…) and its button row.</summary>
        TableLayoutPanel Page(out FlowLayoutPanel buttons, int fillRow = 2)
        {
            body.Controls.Clear();
            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = P(32, 26, 32, 10) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles[fillRow] = new RowStyle(SizeType.Percent, 100);
            buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = P(32, 10, 32, 22), BackColor = Theme.Ground };
            body.Controls.Add(g);
            body.Controls.Add(buttons);
            // styled once the page is built (BeginInvoke runs after the caller has filled it);
            // the first page is built before the window exists, and OnLoad styles that one
            if (IsHandleCreated) BeginInvoke((Action)(() => Theme.Style(body)));
            return g;
        }

        GameButton Btn(string text, ButtonKind kind, Action click)
        {
            var b = new GameButton(text, kind, 11f) { Margin = P(10, 0, 0, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        static Label Heading(string text) =>
            new Label { Text = text.ToUpperInvariant(), AutoSize = true, Font = Theme.Display(22f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 8) };

        static Label Note(string text) =>
            new Label { Text = text, AutoSize = true, MaximumSize = Sz(716, 0), Font = Theme.Body(10.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 8) };
    }

    /// <summary>A big clickable choice: title, a small tag line and a paragraph.</summary>
    sealed class PathCard : Control
    {
        readonly string title, tag, detail;
        bool selected, hover;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public PathCard(string title, string tag, string detail)
        {
            this.title = title; this.tag = tag; this.detail = detail;
            Text = title;   // what accessibility tools read (the card paints itself)
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = title;
            AccessibleDescription = detail;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(selected ? Theme.Panel : hover ? Theme.Panel2 : Theme.Header)) g.FillRectangle(b, r);
            Draw.Border(g, r, selected ? Theme.Bone : Theme.Line, selected ? S(2) : 1);
            if (selected) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, r.X, r.Y, r.Width, S(5));
            int pad = S(22), y = S(26);
            var tf = Theme.Display(20f);
            var th = Draw.Measure(title, tf).Height;
            Draw.Text(g, title.ToUpperInvariant(), tf, selected ? Theme.BoneText : Theme.Bone, new Rectangle(pad, y, r.Width - 2 * pad, th), TextFormatFlags.Left | TextFormatFlags.SingleLine);
            y += th + S(4);
            var gf = Theme.BodyBold(8.5f);
            var gh = Draw.Measure(tag, gf).Height;
            Draw.Text(g, tag, gf, selected ? Theme.Amber : Theme.Muted, new Rectangle(pad, y, r.Width - 2 * pad, gh), TextFormatFlags.Left | TextFormatFlags.SingleLine);
            y += gh + S(16);
            Draw.Text(g, detail, Theme.Body(10.5f), Theme.Bone, new Rectangle(pad, y, r.Width - 2 * pad, r.Height - y - pad), TextFormatFlags.Left | TextFormatFlags.WordBreak);
            if (Focused && ShowFocusCues) Draw.FocusRing(g, Rectangle.Inflate(r, -S(4), -S(4)));
        }
    }
}
