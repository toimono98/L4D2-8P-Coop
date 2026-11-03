using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>The window's layout in the "safe room" theme. Behaviour lives in MainForm.cs.</summary>
    public sealed partial class MainForm
    {
        // ---- controls
        PageHost tabs;
        NavBar nav;
        TextBox txtGame, txtServer, txtComponentInfo, txtLog, txtFindingDetails, txtJoinInfo, txtServerName, txtPassword, txtLobbyAddress;
        Label lblGameStatus, lblServerStatus, lblSummary, lblServerState, lblStatus, lblRoster, lblFillNote, lblCampaign, lblConnect, lblFindingTitle, lblFindingPath;
        ChoiceCard rbDedicated, rbListen, rbDirect, rbLobby;
        ListView lvComponents, lvFindings;
        GameButton btnInstall, btnCheckUpdates, btnUninstall, btnUninstallAll, btnScan, btnFix, btnStart, btnStop, btnJoin, btnCancel, btnServerBrowse;
        CheckBox chkWatch, chkSync, chkUpdateOnStart, chkScanFirst;
        ToggleSwitch chkFill, chkHudLook;
        LinkLabel lnkHudAddon, lnkItemsRules, lnkInfectedRules, lnkHealth;
        Pills cmbItemsMode, cmbInfectedMode, cmbRotation;
        Stepper numPlayers;
        NumericUpDown numPort;
        ComboBox cmbMode, cmbCampaign, cmbChapter;
        Pills cmbDifficulty, cmbNetwork;
        Label lblNetworkNote;
        Meter progress;
        NotifyIcon tray;
        Card grpServer, findingCard, newModNote;
        Tag findingTag;
        Label newModTitle, newModBody;
        Tile tileProblems, tileWarnings, tileClear;
        readonly SurvivorCard[] roster = new SurvivorCard[8];
        readonly System.Windows.Forms.Timer rosterTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        List<string> livePlayers;
        bool rosterBusy;

        /// <summary>Set by the screenshot self-test: never touch the router or saved state.</summary>
        public static bool Preview;

        static readonly string[] Characters = { "NICK", "ROCHELLE", "COACH", "ELLIS", "BILL", "ZOEY", "LOUIS", "FRANCIS" };

        public MainForm()
        {
            Theme.LoadFonts();
            Text = Title + " " + AppInfo.Version;
            Font = Theme.Body(10f);
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            AutoScaleMode = AutoScaleMode.None; // every size goes through Ui.S, so WinForms must not scale again
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = Sz(860, 640);
            Size = Sz(1200, 1000);
            FitToScreen(this);
            Icon = Theme.AppIcon();

            BuildUi();
            Theme.Style(this);
            tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = Title, Visible = true };
            tray.BalloonTipClicked += (s, e) => { AppWindow.Instance?.Switch(StartMode.Host); (TopLevelControl as Form ?? this).Activate(); tabs.SelectedIndex = 1; };

            rescanTimer.Tick += async (s, e) => { rescanTimer.Stop(); await RunScan(auto: true); };
            gameWatch.Tick += (s, e) => WatchListenGame();
            rosterTimer.Tick += async (s, e) => await PollRoster();
            rosterTimer.Start();
            Load += async (s, e) => await OnLoaded();
            Shown += (s, e) => ActiveControl = nav;
            Shown += async (s, e) => await MaybeStartTour();
            FormClosing += OnClosing;
        }

        // ================================================================== frame

        void BuildUi()
        {
            nav = new NavBar("Setup", "Mods", "Play", "Internet");
            nav.SelectedChanged += (s, e) => { if (tabs.SelectedIndex != nav.Selected) tabs.SelectedIndex = nav.Selected; };
            nav.NoticeClicked += (s, e) => Process.Start(UpdateCheck.ReleasePage);
            nav.Help = "? TOUR";
            nav.HelpClicked += (s, e) => StartTour();
            // the same app joins friends: HOST | JOIN swaps to the join window (this one stays alive)
            nav.Modes = new[] { "HOST", "JOIN" };
            nav.Mode = 0;
            nav.ModeClicked += (s, i) => AppWindow.Instance?.Switch(StartMode.Join);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = 1 };

            // NavBar is the tab row; PageHost shows one page at a time.
            tabs = new PageHost { Dock = DockStyle.Fill };
            tabs.SelectedIndexChanged += async (s, e) =>
            {
                nav.Selected = tabs.SelectedIndex;
                if (tabs.SelectedIndex == 3 && !routerSearched && !busy) await CheckNetwork(verbose: false);
            };
            split.Panel1.Controls.Add(tabs);
            tabs.Add(BuildSetupTab());
            tabs.Add(BuildCompatTab());
            tabs.Add(BuildPlayTab());
            tabs.Add(BuildHostingTab());

            split.Panel2.Controls.Add(BuildLogPanel());
            split.Panel2MinSize = 120;
            Shown += (s, e) => { try { split.SplitterDistance = Math.Max(S(300), split.Height - S(140)); } catch { } };

            Controls.Add(split);
            Controls.Add(nav);
        }

        Control BuildLogPanel()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = P(26, 10, 26, 12), BackColor = Theme.Header };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(260)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var caption = SmallCaps("Radio log");
            caption.Margin = P(0, 10, 16, 6);
            lblStatus = new Label { Text = "Ready.", AutoSize = true, Anchor = AnchorStyles.Left, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 6, 0, 6) };
            progress = new Meter { Dock = DockStyle.Fill, Margin = P(12, 12, 12, 10) };
            btnCancel = new GameButton("Cancel", ButtonKind.Ghost, 9f) { Enabled = false, Margin = P(0, 0, 0, 6) };
            btnCancel.Click += (s, e) => cts?.Cancel();
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Mono(9f) };
            t.Controls.Add(caption, 0, 0);
            t.Controls.Add(lblStatus, 1, 0);
            t.Controls.Add(progress, 2, 0);
            t.Controls.Add(btnCancel, 3, 0);
            t.Controls.Add(txtLog, 0, 1);
            t.SetColumnSpan(txtLog, 4);
            return t;
        }

        // ================================================================== helpers

        static TableLayoutPanel Grid(int columns, Padding? padding = null)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Padding = padding ?? P(26, 20, 26, 16), Margin = P(0) };
            if (columns == 1) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        /// <summary>A one-column stack that never grows wider than its parent.</summary>
        static TableLayoutPanel Stack(Padding margin, bool autoSize = false)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = margin, AutoSize = autoSize };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        static Label Caption(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = Theme.BodySemi(10f), ForeColor = Theme.Muted, Margin = P(0, 8, 12, 6) };

        static Label SmallCaps(string text) => new Label { Text = text.ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(8.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 6) };

        static Label Heading(string text, float pt = 26f) => new Label { Text = text, AutoSize = true, Font = Theme.Display(pt), ForeColor = Theme.Bone, Margin = P(0, 0, 16, 0), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };

        static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = P(0) };
            f.Controls.AddRange(controls);
            return f;
        }

        static GameButton Btn(string text, EventHandler onClick) => Btn(text, ButtonKind.Ghost, onClick);

        static GameButton Btn(string text, ButtonKind kind, EventHandler onClick, float pt = 10.5f)
        {
            var b = new GameButton(text, kind, pt);
            b.Click += onClick;
            return b;
        }

        /// <summary>Tape label + big stencil heading + optional graffiti note on the right.</summary>
        static TableLayoutPanel HeadingRow(string tape, string heading, Control right = null)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 14) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var tl = new TapeLabel(tape) { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 6) };
            t.Controls.Add(tl, 0, 0);
            t.Controls.Add(Heading(heading), 1, 0);
            if (right != null) { right.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; t.Controls.Add(right, 2, 0); }
            return t;
        }

        // ================================================================== setup

        /// <summary>A page that scrolls instead of clipping when the window is smaller than its content.</summary>
        static Panel Page(int minWidth, int minHeight)
        {
            var page = new Panel { BackColor = Theme.Ground, AutoScroll = true, AutoScrollMinSize = Sz(minWidth, minHeight) };
            Theme.DarkScrollbars(page);
            return page;
        }

        /// <summary>Keeps a window (and its minimum size) within the screen it opens on.</summary>
        internal static void FitToScreen(Form f)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, wa.Width), Math.Min(f.MinimumSize.Height, wa.Height));
            f.Size = new Size(Math.Min(f.Width, wa.Width), Math.Min(f.Height, wa.Height));
        }

        Panel BuildSetupTab()
        {
            var page = Page(980, 640);
            var g = Grid(1);
            for (int i = 0; i < 6; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblGameStatus = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 10) };
            g.Controls.Add(HeadingRow("Supplies", "WHAT GETS INSTALLED", new GraffitiLabel("backups made before anything changes", 13f) { Angle = -2f }), 0, 0);

            // Game folder
            var gameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = P(0, 0, 0, 10) };
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(140)));
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtGame = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Theme.Body(10.5f), Margin = P(0, 0, 10, 0) };
            txtGame.Leave += (s, e) => { settings.GamePath = txtGame.Text.Trim(); SaveAndRefresh(); };
            gameRow.Controls.Add(Caption("Game folder"), 0, 0);
            gameRow.Controls.Add(txtGame, 1, 0);
            gameRow.Controls.Add(Btn("Browse", (s, e) => BrowseGame()), 2, 0);
            gameRow.Controls.Add(Btn("Detect", (s, e) => DetectGame(true)), 3, 0);
            gameRow.Controls.Add(lblGameStatus, 1, 1);
            gameRow.SetColumnSpan(lblGameStatus, 3);
            g.Controls.Add(gameRow, 0, 1);

            // Hosting mode
            var modeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = P(0, 4, 0, 12) };
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            rbDedicated = new ChoiceCard("Dedicated server", "Recommended · stays VAC-secure · up to 16 players · ~10 GB") { Dock = DockStyle.Fill };
            rbListen = new ChoiceCard("Host from my game", "No download · max 8 players · runs -insecure (no VAC) while hosting") { Dock = DockStyle.Fill, Margin = P(0) };
            rbDedicated.CheckedChanged += (s, e) => { if (!loading && rbDedicated.Checked) { settings.Mode = "dedicated"; SaveAndRefresh(); } };
            rbListen.CheckedChanged += (s, e) =>
            {
                if (loading || !rbListen.Checked) return;
                var ok = MessageBox.Show(this,
                    "Hosting from your game loads the mods into your game client, so L4D2 has to run with -insecure while you host. " +
                    "VAC-secured servers won't let you in until you restart the game normally.\n\n" +
                    "The dedicated server keeps your game VAC-secure (the mods run in a separate server program).\n\nUse host-from-my-game anyway?",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!ok) { loading = true; rbDedicated.Checked = true; loading = false; return; }
                settings.Mode = "listen";
                SaveAndRefresh();
            };
            modeRow.Controls.Add(rbDedicated, 0, 0);
            modeRow.Controls.Add(rbListen, 1, 0);
            g.Controls.Add(modeRow, 0, 2);
            this.modeRow = modeRow;

            // Server folder
            var serverRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = P(0) };
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(140)));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtServer = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Theme.Body(10.5f), Margin = P(0, 0, 10, 0) };
            txtServer.Leave += (s, e) => SetServerFolder(txtServer.Text.Trim());
            btnServerBrowse = Btn("Browse", (s, e) => BrowseServer());
            serverRow.Controls.Add(Caption("Server folder"), 0, 0);
            serverRow.Controls.Add(txtServer, 1, 0);
            serverRow.Controls.Add(btnServerBrowse, 2, 0);
            serverRow.Controls.Add(Btn("Open", (s, e) => OpenFolder(settings.TargetGameDir)), 3, 0);
            g.Controls.Add(serverRow, 0, 3);
            lblServerStatus = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(140, 4, 0, 14) };
            g.Controls.Add(lblServerStatus, 0, 4);

            g.Controls.Add(SmallCaps("Components"), 0, 5);
            lvComponents = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            lvComponents.Columns.Add("Item", S(470));
            lvComponents.Columns.Add("Status", S(230));
            lvComponents.Columns.Add("Version", S(260));
            DarkList.Apply(lvComponents);
            lvComponents.ItemCheck += (s, e) =>
            {
                if (loading) return;
                if (!(lvComponents.Items[e.Index].Tag is Component c)) { e.NewValue = e.CurrentValue; return; }
                if (c.required && e.NewValue != CheckState.Checked) e.NewValue = CheckState.Checked;
            };
            lvComponents.ItemChecked += (s, e) => { if (!loading) SaveSelection(); };
            lvComponents.SelectedIndexChanged += (s, e) => ShowComponentInfo();
            HoverTips.Attach(lvComponents);
            var listBorder = new Card { Dock = DockStyle.Fill, Padding = P(1), Margin = P(0, 0, 0, 8) };
            listBorder.Controls.Add(DarkList.WithHeader(lvComponents));
            g.Controls.Add(listBorder, 0, 6);

            txtComponentInfo = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Height = S(44), Font = Theme.Body(9.5f), Margin = P(0, 0, 0, 10),
                                             Text = "Highlight a component to see what it does and where it comes from." };
            g.Controls.Add(txtComponentInfo, 0, 7);

            btnInstall = Btn("Download & install", ButtonKind.Primary, async (s, e) => await InstallSelected(), 13f);
            btnCheckUpdates = Btn("Check for updates", ButtonKind.Outline, async (s, e) => await CheckUpdates());
            btnUninstall = Btn("Remove highlighted", async (s, e) => await UninstallHighlighted());
            btnUninstallAll = Btn("Remove everything", async (s, e) => await UninstallAll());
            var manualLink = new LinkLabel { Text = "Forum-only extras…", AutoSize = true, Font = Theme.BodySemi(10f), Margin = P(6, 16, 0, 0) };
            manualLink.LinkClicked += (s, e) => ShowManualExtras();
            g.Controls.Add(Row(btnInstall, btnCheckUpdates, btnUninstall, btnUninstallAll, manualLink), 0, 8);

            page.Controls.Add(g);
            return page;
        }

        // ================================================================== mods

        Panel BuildCompatTab()
        {
            var page = Page(980, 560);
            var g = Grid(1);
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            chkWatch = new GameCheck { Text = "Watch for new mods and check them automatically", AutoSize = true, Font = Theme.Body(10f), Margin = P(0, 0, 0, 10) };
            chkWatch.CheckedChanged += (s, e) => { if (loading) return; settings.WatchMods = chkWatch.Checked; settings.Save(); SetupWatchers(); };
            g.Controls.Add(HeadingRow("Scan", "INFECTION REPORT", chkWatch), 0, 0);

            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true, Margin = P(0, 0, 0, 14) };
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tileProblems = new Tile("Problems") { Dock = DockStyle.Fill, Fill = Theme.Red, Border = Color.Empty, NumberColor = Theme.BoneText, LabelColor = Theme.BoneText };
            tileWarnings = new Tile("Warnings") { Dock = DockStyle.Fill, Border = Theme.Amber, NumberColor = Theme.Amber, LabelColor = Theme.Bone };
            tileClear = new Tile("Clear for 8") { Dock = DockStyle.Fill };
            btnScan = Btn("Scan now", ButtonKind.Outline, async (s, e) => await RunScan(auto: false));
            btnScan.Margin = P(14, 9, 10, 0);
            var btnOpen = Btn("Show in folder", (s, e) =>
            {
                var f = SelectedFinding();
                if (f != null && !string.IsNullOrEmpty(f.Location) && File.Exists(f.Location)) Process.Start("explorer.exe", "/select,\"" + f.Location + "\"");
                else if (f != null && Directory.Exists(f.Location)) OpenFolder(f.Location);
            });
            btnOpen.Margin = P(0, 9, 0, 0);
            tiles.Controls.Add(tileProblems, 0, 0);
            tiles.Controls.Add(tileWarnings, 1, 0);
            tiles.Controls.Add(tileClear, 2, 0);
            tiles.Controls.Add(btnScan, 3, 0);
            tiles.Controls.Add(btnOpen, 4, 0);
            g.Controls.Add(tiles, 0, 1);

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = P(0) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(400)));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            lvFindings = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            lvFindings.Columns.Add("Status", S(100));
            lvFindings.Columns.Add("Type", S(128));
            lvFindings.Columns.Add("Name", S(300));
            lvFindings.Columns.Add("Summary", S(260));
            HoverTips.Attach(lvFindings);
            DarkList.Apply(lvFindings);
            lvFindings.SelectedIndexChanged += (s, e) => ShowFindingDetail(SelectedFinding());
            var listBorder = new Card { Dock = DockStyle.Fill, Padding = P(1), Margin = P(0, 0, 20, 0) };
            listBorder.Controls.Add(DarkList.WithHeader(lvFindings));
            main.Controls.Add(listBorder, 0, 0);

            var right = Stack(P(0));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            findingCard = new Card { Dock = DockStyle.Fill, Padding = P(18), Margin = P(0, 0, 0, 14) };
            var fc = Stack(P(0));
            for (int i = 0; i < 2; i++) fc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            findingTag = new Tag("Select an item") { Margin = P(0, 0, 0, 10) };
            findingTag.Set("Select an item", Color.Empty, Theme.Muted, Theme.Muted, true);
            lblFindingTitle = new Label { AutoSize = true, MaximumSize = Sz(360, 0), Font = Theme.Display(15f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 10), Text = "Pick a mod or plugin on the left" };
            txtFindingDetails = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Body(10.5f), BorderStyle = BorderStyle.None, Margin = P(0, 0, 0, 10) };
            lblFindingPath = new Label { AutoSize = true, MaximumSize = Sz(360, 0), Font = Theme.Mono(8.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 12) };
            btnFix = Btn("Disable", ButtonKind.Primary, async (s, e) => await FixHighlighted(), 11.5f);
            btnFix.Dock = DockStyle.Top;
            btnFix.AutoSizeLocked = true;
            btnFix.Enabled = false;
            fc.Controls.Add(findingTag, 0, 0);
            fc.Controls.Add(lblFindingTitle, 0, 1);
            fc.Controls.Add(txtFindingDetails, 0, 2);
            fc.Controls.Add(lblFindingPath, 0, 3);
            fc.Controls.Add(btnFix, 0, 4);
            findingCard.Controls.Add(fc);
            right.Controls.Add(findingCard, 0, 0);

            newModNote = new Card { Dock = DockStyle.Fill, BackColor = Theme.Bone, BorderColor = Theme.Bone, AutoSize = true, Visible = false, Margin = P(0) };
            var nm = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, BackColor = Theme.Bone };
            nm.Controls.Add(new Label { Text = "NEW MOD CHECKED", AutoSize = true, Font = Theme.BodyBold(8.5f), ForeColor = Theme.Ground, Margin = P(0, 0, 0, 2) });
            newModTitle = new Label { AutoSize = true, MaximumSize = Sz(360, 0), Font = Theme.Display(13f), ForeColor = Theme.Ground, Margin = P(0, 0, 0, 2) };
            newModBody = new Label { AutoSize = true, MaximumSize = Sz(360, 0), Font = Theme.Body(10f), ForeColor = Theme.Ground, Margin = P(0) };
            nm.Controls.Add(newModTitle);
            nm.Controls.Add(newModBody);
            newModNote.Controls.Add(nm);
            right.Controls.Add(newModNote, 0, 1);
            main.Controls.Add(right, 1, 0);
            g.Controls.Add(main, 0, 2);

            lblSummary = new Label { Visible = false }; // kept for status text during scans
            page.Controls.Add(lblSummary);
            page.Controls.Add(g);
            return page;
        }

        void ShowFindingDetail(Finding f)
        {
            if (f == null)
            {
                findingTag.Set("Select an item", Color.Empty, Theme.Muted, Theme.Muted, true);
                lblFindingTitle.Text = "Pick a mod or plugin on the left";
                txtFindingDetails.Text = "";
                lblFindingPath.Text = "";
                btnFix.Enabled = false;
                findingCard.BorderColor = Theme.Line;
                return;
            }
            switch (f.Severity)
            {
                case Severity.Problem: findingTag.Set("Problem · " + f.Kind, Theme.Red, Color.Empty, Theme.BoneText); findingCard.BorderColor = Theme.Red; break;
                case Severity.Warning: findingTag.Set("Warning · " + f.Kind, Theme.Amber, Color.Empty, Theme.Ground); findingCard.BorderColor = Theme.Amber; break;
                case Severity.Info: findingTag.Set("Note · " + f.Kind, Color.Empty, Theme.Bone, Theme.Bone); findingCard.BorderColor = Theme.Line; break;
                default: findingTag.Set("Clear · " + f.Kind, Color.Empty, Theme.Muted, Theme.Muted); findingCard.BorderColor = Theme.Line; break;
            }
            findingCard.BorderWidth = f.Severity >= Severity.Warning ? 2 : 1;
            lblFindingTitle.Text = f.Name;
            var details = f.Details.Count > 0 ? f.Details.Select(d => d.Replace("[Problem] ", "✖ ").Replace("[Warning] ", "⚠ ").Replace("[Info] ", "• ")) : new[] { "No issues found for 8-player co-op." };
            txtFindingDetails.Text = (f.Summary ?? "") + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, details);
            lblFindingPath.Text = f.Location ?? "";
            btnFix.Enabled = f.Fix != FixAction.None;
            btnFix.Text = f.Fix == FixAction.EnablePlugin || f.Fix == FixAction.EnableAddon ? "Enable it again"
                        : f.Fix == FixAction.OpenWorkshop ? "Open Workshop page"
                        : f.Fix == FixAction.None ? "Nothing to fix" : "Disable this " + (f.Kind == "Addon" ? "addon" : "plugin");
        }

        // ================================================================== play

        Panel BuildPlayTab()
        {
            var page = Page(1040, 720);
            var outer = Grid(2);
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // the settings column scrolls on short windows: room for its scrollbar, so the 360 px of
            // cards and pill rows never get clipped on the right when it appears
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(360) + SystemInformation.VerticalScrollBarWidth));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- left: the group
            var left = Stack(P(0, 0, 24, 0));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(S(236), SurvivorCard.MinHeight * 2 + S(12))));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 12) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.Controls.Add(new TapeLabel("Your group") { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 8) }, 0, 0);
            lblRoster = Heading("1 / 8 SURVIVORS", 28f);
            head.Controls.Add(lblRoster, 1, 0);
            head.Controls.Add(new GraffitiLabel("no one left behind", 12.5f) { Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Angle = -4f }, 2, 0);
            left.Controls.Add(head, 0, 0);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = P(0) };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 8; i++)
            {
                roster[i] = new SurvivorCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i % 4 == 3 ? 0 : 12, i < 4 ? 12 : 0) };
                grid.Controls.Add(roster[i], i % 4, i / 4);
            }
            left.Controls.Add(grid, 0, 1);

            lblFillNote = new Label { AutoSize = true, MaximumSize = Sz(720, 0), Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 8, 0, 10) };
            left.Controls.Add(lblFillNote, 0, 2);

            var joinCard = new Card("Who can join") { Dock = DockStyle.Fill, Margin = P(0) };
            var jc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = P(0) };
            jc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            jc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            jc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            jc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            jc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            jc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            jc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cmbNetwork = new Pills(new[] { "internet", "lan", "local" }, new[] { "Internet", "LAN only", "Just me" }, 420) { Margin = P(0, 0, 0, 6) };
            lblNetworkNote = new Label { AutoSize = true, MaximumSize = Sz(700, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 10) };
            jc.Controls.Add(cmbNetwork, 0, 0);
            jc.SetColumnSpan(cmbNetwork, 3);
            jc.Controls.Add(lblNetworkNote, 0, 1);
            jc.SetColumnSpan(lblNetworkNote, 2);
            rbDirect = new ChoiceCard("Direct connect", "Paste invite or connect <IP>") { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = P(0, 0, 10, 10) };
            rbLobby = new ChoiceCard("Steam lobby", "8 Slots Lobby Mod") { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = P(0, 0, 10, 10) };
            txtLobbyAddress = new TextBox { Dock = DockStyle.Top, Font = Theme.Body(10f) };
            var lobbyBox = Stack(P(0, 0, 0, 10), autoSize: true);
            lobbyBox.Controls.Add(new Label { Text = "Lobby address", AutoSize = true, Font = Theme.Body(8.5f), ForeColor = Theme.Muted, Margin = P(0, 2, 0, 4) });
            new ToolTip().SetToolTip(txtLobbyAddress, "IP:port the lobby sends everyone to. Leave blank to use your public IP.");
            lobbyBox.Controls.Add(txtLobbyAddress);
            txtJoinInfo = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Body(9.5f), BorderStyle = BorderStyle.None };
            jc.Controls.Add(rbDirect, 0, 2);
            jc.Controls.Add(rbLobby, 1, 2);
            jc.Controls.Add(lobbyBox, 2, 2);
            txtJoinInfo.Visible = false; // kept up to date; shown through the "step-by-step" link
            var steps = new LinkLabel { Text = "How to get in…", AutoSize = true, Font = Theme.BodySemi(9.5f), Margin = P(0, 0, 0, 10), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            steps.LinkClicked += (s, e) => MessageBox.Show(this, txtJoinInfo.Text, "How to get in", MessageBoxButtons.OK, MessageBoxIcon.Information);
            jc.Controls.Add(steps, 2, 1);
            jc.Controls.Add(txtJoinInfo, 0, 3);
            joinCard.Controls.Add(jc);
            left.Controls.Add(joinCard, 0, 3);
            outer.Controls.Add(left, 0, 0);

            // ---- right: settings
            var right = Stack(P(0));
            right.AutoScroll = true;
            Theme.DarkScrollbars(right);
            for (int i = 0; i < 10; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var camp = new Card("Campaign") { Dock = DockStyle.Fill, AutoSize = true };
            var cc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = P(0) };
            cc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            cc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            lblCampaign = new Label { AutoSize = true, Font = Theme.Display(17f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 6), Text = "DEAD CENTER" };
            // the campaign by its in-game name, then which of its chapters the server starts on
            cmbCampaign = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0, 0, 8, 0), AccessibleName = "Campaign" };
            cmbChapter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0), AccessibleName = "Starting chapter" };
            cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0), AccessibleName = "Game mode" };
            foreach (var cb in new[] { cmbCampaign, cmbChapter, cmbMode }) Theme.StyleCombo(cb);
            cmbMode.Items.AddRange(ServerConfig.GameModes);
            var chapterCap = SmallCaps("Start from chapter");
            chapterCap.Margin = P(0, 10, 0, 4);
            cc.Controls.Add(lblCampaign, 0, 0);
            cc.SetColumnSpan(lblCampaign, 2);
            cc.Controls.Add(cmbCampaign, 0, 1);
            cc.Controls.Add(cmbMode, 1, 1);
            cc.Controls.Add(chapterCap, 0, 2);
            cc.SetColumnSpan(chapterCap, 2);
            cc.Controls.Add(cmbChapter, 0, 3);
            cc.SetColumnSpan(cmbChapter, 2);
            int comboRow = Math.Max(cmbCampaign.Height, cmbMode.Height) + S(2);
            cc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cc.RowStyles.Add(new RowStyle(SizeType.Absolute, comboRow));
            cc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cc.RowStyles.Add(new RowStyle(SizeType.Absolute, comboRow));
            new ToolTip().SetToolTip(cmbChapter, "The chapter the server starts on. Later chapters follow as usual, and the next campaign starts from chapter 1.");
            camp.Controls.Add(cc);
            right.Controls.Add(camp, 0, 0);

            var diffBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 12) };
            diffBox.Controls.Add(SmallCaps("Difficulty"));
            cmbDifficulty = new Pills(ServerConfig.Difficulties, new[] { "Easy", "Normal", "Advanced", "Expert" }, 358);
            diffBox.Controls.Add(cmbDifficulty);
            right.Controls.Add(diffBox, 0, 1);

            right.Controls.Add(SettingRow("Bot fill", "Bots hold every empty slot", chkFill = new ToggleSwitch { AccessibleName = "Bot fill" }), 0, 2);
            new ToolTip().SetToolTip(chkFill, "On: the round starts with a full team and bots hold every empty slot; joining players take them over.\nOff: the game's normal 4 survivors; extra survivors appear only when players 5+ join.");
            right.Controls.Add(SettingRow("Max players", null, numPlayers = new Stepper("Players") { Minimum = 5, Maximum = 16 }), 0, 3);
            right.Controls.Add(ModeRow("More items for 5+", "Weapons, throwables and healing", out cmbItemsMode, out lnkItemsRules,
                "Weapon piles, throwables and healing items scale with the survivors in the game, bots included.\n" +
                "Auto: x survivors / 4, so 8 survivors get twice the map's items (a pile of 5 holds 10, each kit can be taken twice).\n" +
                "Half: half that (8 survivors: 1.5x). Custom: your own % per category. Off: the map's normal items for 4.\n" +
                "Survivors joining mid-chapter top up what's left."), 0, 4);
            right.Controls.Add(ModeRow("Harder infected for 5+", "Specials, horde size and Tank health", out cmbInfectedMode, out lnkInfectedRules,
                "Special infected, hordes and the Tank scale with the survivors in the game, bots included.\n" +
                "Auto: x survivors / 4, so 8 survivors face 4 specials at once instead of 2, hordes of 60 instead of 30 and an 8000 HP Tank on Normal.\n" +
                "Half: half that (8 survivors: 1.5x). Custom: your own % rules, including faster special respawns. Off: the game's own director.\n" +
                "Co-op, realism and survival."), 0, 5);
            right.Controls.Add(ChoiceRow("After the finale", "Where the server goes after the rescue", Settings.RotationModes,
                new[] { "Stay", "Next", "Random", "Vote" }, out cmbRotation, out _,
                "Stay: the game's own behaviour (the server sits on the finale).\n" +
                "Next: the next campaign in order (Dead Center, Dark Carnival, … then your custom campaigns).\n" +
                "Random: any other campaign. Vote: the players choose between a few, and can start the vote early with !mapvote.\n" +
                "The change comes a few seconds after the rescue, once the credits start. Co-op and realism.", false), 0, 6);
            lnkItemsRules.LinkClicked += (s, e) => EditItemRules();
            lnkInfectedRules.LinkClicked += (s, e) => EditInfectedRules();

            lnkHudAddon = new LinkLabel { AutoSize = true, Font = Theme.BodySemi(9f), Margin = P(0, 4, 0, 0) };
            lnkHudAddon.LinkClicked += (s, e) => ToggleHudAddon();
            right.Controls.Add(SettingRow("HUD bars + icons", "Every player needs the HUD add-on",
                chkHudLook = new ToggleSwitch { AccessibleName = "HUD bars and icons" }, lnkHudAddon), 0, 7);
            new ToolTip().SetToolTip(chkHudLook, "On: the 8-Player HUD shows survivors without a stock panel as dark boxes with a health bar coloured by state and the game's own kit/pills icons.\n" +
                "Everyone needs the HUD add-on (the app's JOIN side installs it); without it they see stray boxes.\nOff: plain text that works for everyone.");

            grpServer = new Card("Dedicated server") { Dock = DockStyle.Fill, AutoSize = true };
            var sg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = P(0) };
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            txtServerName = new TextBox { Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0, 4, 0, 4) };
            numPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0, 4, 0, 4) };
            txtPassword = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill, Font = Theme.Body(10f), Margin = P(0, 4, 0, 4) };
            sg.Controls.Add(Caption("Name"), 0, 0); sg.Controls.Add(txtServerName, 1, 0); sg.SetColumnSpan(txtServerName, 3);
            sg.Controls.Add(Caption("Port"), 0, 1); sg.Controls.Add(numPort, 1, 1);
            var pwCap = Caption("Password"); pwCap.Margin = P(14, 8, 12, 6); sg.Controls.Add(pwCap, 2, 1); sg.Controls.Add(txtPassword, 3, 1);
            grpServer.Controls.Add(sg);
            right.Controls.Add(grpServer, 0, 8);

            var opts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 4) };
            chkSync = new GameCheck { Text = "Copy campaigns and script mods to the server", AutoSize = true, Font = Theme.Body(9.5f) };
            chkUpdateOnStart = new GameCheck { Text = "Update the server before starting", AutoSize = true, Font = Theme.Body(9.5f) };
            chkScanFirst = new GameCheck { Text = "Check compatibility before launching", AutoSize = true, Font = Theme.Body(9.5f) };
            opts.Controls.AddRange(new Control[] { chkSync, chkUpdateOnStart, chkScanFirst });
            right.Controls.Add(opts, 0, 9);
            outer.Controls.Add(right, 1, 0);

            // ---- footer
            btnStart = Btn("Start server", ButtonKind.Primary, async (s, e) => await StartServer(), 14f);
            btnJoin = Btn("Launch L4D2 & join", ButtonKind.Outline, async (s, e) => await LaunchGame(), 11f);
            btnJoin.Margin = P(0, 0, 10, 4);
            btnStop = Btn("Stop", ButtonKind.Ghost, async (s, e) => { StopServer(); await Task.CompletedTask; }, 10f);
            btnStop.Margin = P(0, 0, 10, 4);
            lblServerState = new Label { AutoSize = true, Font = Theme.BodyBold(9f), ForeColor = Theme.Muted, Margin = P(6, 20, 0, 0) };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = P(0, 16, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblServerState.Margin = P(2, 8, 0, 0); // shows whether your game stays VAC-secure
            var actions = Row(btnStart, btnJoin, btnStop);
            actions.WrapContents = false;
            var actionStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0) };
            actionStack.Controls.Add(actions);
            actionStack.Controls.Add(lblServerState);
            // plugins that failed on the last start (ServerHealth), hidden while there are none
            lnkHealth = new LinkLabel { AutoSize = true, Font = Theme.BodyBold(9f), Margin = P(2, 4, 0, 0), Visible = false, LinkColor = Theme.Amber, ActiveLinkColor = Theme.Bone };
            lnkHealth.LinkClicked += (s, e) => ShowHealthDetails();
            actionStack.Controls.Add(lnkHealth);
            footer.Controls.Add(actionStack, 0, 0);

            var connectBox = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = P(0), Anchor = AnchorStyles.Right };
            var cap = SmallCaps("Friends join with");
            lblConnect = new Label { AutoSize = true, Font = Theme.Mono(10f), ForeColor = Theme.Bone, BackColor = Theme.Panel, Padding = P(10, 8, 10, 8), Margin = P(0, 0, 8, 0), BorderStyle = BorderStyle.FixedSingle };
            var btnCopy = Btn("Copy", ButtonKind.Ghost, (s, e) => CopyConnect(), 10f);
            var btnIp = Btn("Find my IP", ButtonKind.Ghost, async (s, e) => await FindPublicIp(), 10f);
            btnIp.Margin = P(0);
            connectBox.Controls.Add(cap, 0, 0);
            connectBox.SetColumnSpan(cap, 3);
            connectBox.Controls.Add(lblConnect, 0, 1);
            connectBox.Controls.Add(btnCopy, 1, 1);
            connectBox.Controls.Add(btnIp, 2, 1);
            footer.Controls.Add(connectBox, 1, 0);
            outer.Controls.Add(footer, 0, 1);
            outer.SetColumnSpan(footer, 2);

            foreach (Control c in new Control[] { txtServerName, txtPassword, txtLobbyAddress })
                c.Leave += (s, e) => ReadPlaySettings();
            numPort.ValueChanged += (s, e) => ReadPlaySettings();
            numPlayers.ValueChanged += (s, e) => ReadPlaySettings();
            cmbMode.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            cmbDifficulty.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            cmbNetwork.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            cmbCampaign.SelectedIndexChanged += (s, e) =>
            {
                if (loading || !(cmbCampaign.SelectedItem is Campaigns.Campaign c)) return;
                FillChapters(c, null);   // a new campaign starts from its first chapter
                lblCampaign.Text = c.Title.ToUpperInvariant();
                ReadPlaySettings();
            };
            cmbChapter.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            foreach (var c in new CheckBox[] { chkFill, chkHudLook, chkSync, chkUpdateOnStart, chkScanFirst }) c.CheckedChanged += (s, e) => ReadPlaySettings();
            cmbItemsMode.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            cmbInfectedMode.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            cmbRotation.SelectedIndexChanged += (s, e) => ReadPlaySettings();
            rbDirect.CheckedChanged += (s, e) => ReadPlaySettings();
            rbLobby.CheckedChanged += (s, e) => ReadPlaySettings();

            page.Controls.Add(outer);
            return page;
        }

        static Card SettingRow(string title, string detail, Control control, Control below = null)
        {
            var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Padding = P(14, 8, 14, 8), Margin = P(0, 0, 0, 10) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = P(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0), Anchor = AnchorStyles.Left };
            text.Controls.Add(new Label { Text = title.ToUpperInvariant(), AutoSize = true, Font = Theme.Stencil(11.5f), ForeColor = Theme.Bone, Margin = P(0) });
            if (detail != null) text.Controls.Add(new Label { Text = detail, AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 2, 0, 0) });
            if (below != null) text.Controls.Add(below);
            control.Anchor = AnchorStyles.Right;
            t.Controls.Add(text, 0, 0);
            t.Controls.Add(control, 1, 0);
            card.Controls.Add(t);
            return card;
        }

        /// <summary>A scaling setting: title and detail, the Off/Auto/Half/Custom pills, and a link to the Custom rules.</summary>
        static Card ModeRow(string title, string detail, out Pills pills, out LinkLabel rules, string tip) =>
            ChoiceRow(title, detail, Settings.ScalingModes, new[] { "Off", "Auto", "Half", "Custom" }, out pills, out rules, tip, true);

        /// <summary>A setting with a row of pills under its title, and optionally a "Custom rules…" link.</summary>
        static Card ChoiceRow(string title, string detail, string[] values, string[] labels, out Pills pills, out LinkLabel rules, string tip, bool withRules)
        {
            var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Padding = P(14, 8, 14, 10), Margin = P(0, 0, 0, 10) };
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0), Dock = DockStyle.Fill };
            var head = new Label { Text = title.ToUpperInvariant(), AutoSize = true, Font = Theme.Stencil(11.5f), ForeColor = Theme.Bone, Margin = P(0) };
            text.Controls.Add(head);
            text.Controls.Add(new Label { Text = detail, AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 2, 0, 8) });
            // narrower than the Difficulty pills: this sits inside a card's padding
            pills = new Pills(values, labels, 318) { AccessibleName = title };
            text.Controls.Add(pills);
            rules = null;
            if (withRules)
            {
                rules = new LinkLabel { Text = "Custom rules…", AutoSize = true, Font = Theme.BodySemi(9f), Margin = P(0, 6, 0, 0) };
                text.Controls.Add(rules);
            }
            var tt = new ToolTip();
            tt.SetToolTip(head, tip);
            tt.SetToolTip(pills, tip);
            card.Controls.Add(text);
            return card;
        }

        // ---- roster

        /// <summary>Fills the 8 survivor cards: a preview of the settings, or the live server's players while it runs.</summary>
        void UpdateRoster()
        {
            if (roster[0] == null) return;
            int slots = (int)numPlayers.Value;
            bool fill = chkFill.Checked;
            bool live = livePlayers != null;
            var names = livePlayers ?? new List<string>();
            int humans = live ? names.Count : 1;
            int filled = 0;
            for (int i = 0; i < slots; i++) if (i < humans || fill || i < 4) filled++;
            for (int i = 0; i < 8; i++)
            {
                var card = roster[i];
                card.Visible = i < slots;
                SlotKind kind = i < humans ? (i == 0 ? SlotKind.Host : SlotKind.Player) : (fill || i < 4) ? SlotKind.Bot : SlotKind.Empty;
                string who = live ? (i < names.Count ? names[i] : null) : (i == 0 ? "You" : null);
                card.Set(i + 1, Characters[i], kind, who);
            }
            lblRoster.Text = $"{filled} / {slots} SURVIVORS";
            lblFillNote.Text = (live ? $"Live from your server: {names.Count} player(s) connected. " : "Preview of your settings. ")
                + (slots > 8 ? $"Showing the first 8 of {slots} slots. " : "")
                + (fill ? $"Bot fill is on: the round starts with {slots} survivors, and friends who join take over a bot."
                        : "Bot fill is off: the game keeps its usual 4 survivors. Slots 5+ appear only when players 5+ join.");
        }

        async Task PollRoster()
        {
            if (rosterBusy) return;
            bool hosting = (serverProcess != null && !serverProcess.HasExited) || (settings.Mode == "listen" && gameWatch.Enabled);
            if (!hosting)
            {
                if (livePlayers != null) { livePlayers = null; UpdateRoster(); }
                return;
            }
            rosterBusy = true;
            try
            {
                var port = settings.Mode == "listen" ? 27015 : settings.Port;
                var players = await ServerQuery.Players(Launcher.LocalServerAddress(), port);
                livePlayers = players;
                UpdateRoster();
            }
            catch { }
            finally { rosterBusy = false; }
        }
    }
}
