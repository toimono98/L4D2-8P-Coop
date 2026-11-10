using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.Join
{
    public sealed class RecentServer
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string LastJoined { get; set; }
    }

    public sealed class JoinSettings
    {
        public string GamePath { get; set; }
        public List<RecentServer> Recent { get; set; } = new List<RecentServer>();
        public bool TourSeen { get; set; }   // the first-run tour has been shown
        /// <summary>Join game downloads the server's required mods you're missing (and the cosmetic ones you tick) first.</summary>
        public bool DownloadMods { get; set; } = true;

        static string FilePath => Path.Combine(Paths.AppData, "join.json");
        public static bool Exists => File.Exists(FilePath);
        public static JoinSettings Load() => Json.Load<JoinSettings>(FilePath);
        /// <summary>Set by the layout audit so it never overwrites the real recent list.</summary>
        public static bool ReadOnly;
        public void Save() { if (!ReadOnly) Json.Save(FilePath, this); }
    }

    /// <summary>The 8 survivor pips under a server: red = host, bone = filled, outline = open.</summary>
    sealed class PipBar : Control, Themed
    {
        int players, max = 8;
        public PipBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Height = S(12);
            Margin = P(0, 4, 0, 14);
        }
        public void Set(int p, int m) { players = p; max = Math.Max(1, Math.Min(16, m)); AccessibleName = $"{p} of {m} survivors"; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Panel);
            int gap = S(6), w = (Width - gap * (max - 1)) / max;
            for (int i = 0; i < max; i++)
            {
                var r = new Rectangle(i * (w + gap), 0, w, Height - 1);
                if (i < players) using (var b = new SolidBrush(i == 0 ? Theme.Red : Theme.Bone)) g.FillRectangle(b, r);
                else using (var p = new Pen(Theme.Bot)) g.DrawRectangle(p, r);
            }
        }
    }

    /// <summary>Everything a friend needs: paste the host's invite, check the server, join.</summary>
    public sealed partial class JoinForm : Form, UI.IAppSide
    {
        const string Title = "L4D2 8-Player Co-op · Join";
        /// <summary>No join.json yet: someone opening the Join app for the first time.</summary>
        readonly bool firstRun = !JoinSettings.Exists;
        readonly JoinSettings settings = JoinSettings.Load();
        NavBar header;
        TextBox txtTarget, txtPassword;
        ListView lvRecent;
        GameButton btnCheck, btnJoin, btnLobby;
        Panel holder, body;
        /// <summary>The server card's height without the server's mods; the mod list adds its own height on top.</summary>
        const int CardBase = 390, BodyMinHeight = 720;
        LinkLabel lnkMap, lnkLan, lnkGame, lnkHud, lnkSkins, lnkWorkshop;
        Card serverCard, emptyCard;
        Label lblFound, lblName, lblMap, lblCount, lblPing, lblVersion, lblMapState, lblNotes, lblEmptyTitle, lblEmptyBody;
        PipBar pips;
        string missingMap, missingWorkshopId;

        public JoinForm(string initial)
        {
            Theme.LoadFonts();
            Text = Title + " " + AppInfo.Version;
            Font = Theme.Body(10f);
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            AutoScaleMode = AutoScaleMode.None; // every size goes through Ui.S
            StartPosition = FormStartPosition.CenterScreen;
            Size = Sz(820, 880);
            MinimumSize = Sz(640, 600);
            FitToScreen();
            Icon = Theme.AppIcon();
            BuildUi();
            Theme.Style(this);
            if (!GameLocator.IsGameDir(settings.GamePath)) { settings.GamePath = GameLocator.FindGame(); settings.Save(); }
            UpdateGameLabel();
            RefreshRecent();
            ShowEmpty("No server checked yet", "Paste your host's invite (it looks like steam://connect/1.2.3.4:27015) and click Check server. You don't need any mods to join.");
            if (JoinTarget.Parse(initial) != null) txtTarget.Text = initial;
            Shown += async (s, e) => await MaybeStartTour();
            Shown += async (s, e) =>
            {
                ActiveControl = txtTarget;
                if (txtTarget.Text.Length > 0) await Check();
                // "Version X is out" in the header (GitHub, checked once a day; silent offline)
                var v = await UpdateCheck.Newer();
                if (v != null && !IsDisposed) header.Notice = $"VERSION {v} IS OUT · DOWNLOAD";
            };
        }

        // ================================================================== layout

        void BuildUi()
        {
            header = new NavBar() { Title = "JOIN YOUR GROUP", Subtitle = "L4D2 8-PLAYER CO-OP" };
            header.NoticeClicked += (s, e) => Process.Start(UpdateCheck.ReleasePage);
            header.Help = "? TOUR";
            header.HelpClicked += (s, e) => StartTour();
            // the same app hosts: HOST | JOIN swaps to the host window
            header.Modes = new[] { "HOST", "JOIN" };
            header.Mode = 1;
            header.ModeClicked += (s, i) => UI.AppWindow.Instance?.Switch(StartMode.Host);

            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = P(24, 18, 24, 18) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- invite
            var inv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 8) };
            inv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var cap = Caps("Paste the invite your host sent");
            inv.Controls.Add(cap, 0, 0);
            inv.SetColumnSpan(cap, 3);
            txtTarget = new TextBox { Dock = DockStyle.Fill, Font = Theme.Mono(12f), Margin = P(0, 4, 10, 0), AccessibleName = "Invite, IP or connect line" };
            txtTarget.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Check(); } };
            var paste = new GameButton("Paste", ButtonKind.Ghost, 10f) { Margin = P(0, 0, 10, 0) };
            paste.Click += (s, e) => { if (Clipboard.ContainsText()) txtTarget.Text = Clipboard.GetText().Trim(); };
            btnCheck = new GameButton("Check server", ButtonKind.Outline, 11f) { Margin = P(0) };
            btnCheck.Click += async (s, e) => await Check();
            inv.Controls.Add(txtTarget, 0, 1);
            inv.Controls.Add(paste, 1, 1);
            inv.Controls.Add(btnCheck, 2, 1);
            g.Controls.Add(inv, 0, 0);

            var pw = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = P(0, 0, 0, 14) };
            pw.Controls.Add(new Label { Text = "Password (if any)", AutoSize = true, Font = Theme.BodySemi(10f), ForeColor = Theme.Muted, Margin = P(0, 8, 12, 0) });
            txtPassword = new TextBox { Width = S(220), UseSystemPasswordChar = true, Font = Theme.Body(10.5f), Margin = P(0, 4, 0, 0), AccessibleName = "Password" };
            pw.Controls.Add(txtPassword);
            g.Controls.Add(pw, 0, 1);

            // ---- server card (shown after a check) and its empty state
            holder = new Panel { Dock = DockStyle.Fill, Height = S(CardBase), Margin = P(0, 0, 0, 16) };
            serverCard = new Card { Dock = DockStyle.Fill, Padding = P(18, 16, 18, 16), Visible = false };
            var sc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = P(0) };
            sc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sc.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 5; i++) sc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lblFound = Caps("Server found");
            lblName = new Label { AutoSize = true, Font = Theme.Display(17f), ForeColor = Theme.Bone, Margin = P(0, 2, 0, 2) };
            lblMap = new Label { AutoSize = true, Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 8) };
            lblCount = new Label { AutoSize = true, Font = Theme.Display(17f), ForeColor = Theme.Bone, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = P(12, 14, 0, 0) };
            pips = new PipBar { Dock = DockStyle.Top };
            var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 10) };
            for (int i = 0; i < 3; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            stats.Controls.Add(Stat("Ping", out lblPing), 0, 0);
            stats.Controls.Add(Stat("Version", out lblVersion), 1, 0);
            var last = Stat("Map", out lblMapState); last.Margin = P(0);
            stats.Controls.Add(last, 2, 0);
            lblNotes = new Label { AutoSize = true, MaximumSize = Sz(700, 0), Font = Theme.BodySemi(10f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 6) };
            lnkMap = new LinkLabel { AutoSize = true, Visible = false, Font = Theme.BodySemi(10f), Margin = P(0, 0, 0, 8) };
            // the host's invite names the exact Workshop item: open it in Steam, where Subscribe is one click
            lnkMap.LinkClicked += (s, e) => Process.Start(missingWorkshopId != null
                ? "steam://url/CommunityFilePage/" + missingWorkshopId
                : "https://steamcommunity.com/workshop/browse/?appid=550&searchtext=" + Uri.EscapeDataString(missingMap ?? ""));
            btnJoin = new GameButton("Join game", ButtonKind.Primary, 15f) { Dock = DockStyle.Top, AutoSizeLocked = true, Margin = P(0) };
            btnJoin.Click += async (s, e) => await JoinGame();
            // the game's own lobby instead: start L4D2 (mods and skins ready) and stop at the main menu
            btnLobby = new GameButton("Go to lobby", ButtonKind.Outline, 12f) { AutoSizeLocked = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = P(10, 0, 0, 0) };
            btnLobby.Height = btnJoin.Height - S(4);
            btnLobby.Click += async (s, e) => await JoinGame(lobby: true);
            new ToolTip().SetToolTip(btnLobby, "Starts Left 4 Dead 2 without joining the server, so you land on the main menu.\n" +
                "From there, join your host's lobby: accept their invite, or right-click them in Steam's friends list and choose Join Game.");
            var joinRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = P(0, 4, 0, 0) };
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            joinRow.Controls.Add(btnJoin, 0, 0);
            joinRow.Controls.Add(btnLobby, 1, 0);
            sc.Controls.Add(lblFound, 0, 0);
            sc.Controls.Add(lblCount, 1, 0); sc.SetRowSpan(lblCount, 3);
            sc.Controls.Add(lblName, 0, 1);
            sc.Controls.Add(lblMap, 0, 2);
            sc.Controls.Add(pips, 0, 3); sc.SetColumnSpan(pips, 2);
            sc.Controls.Add(stats, 0, 4); sc.SetColumnSpan(stats, 2);
            // Notes on top, Join pinned to the bottom of the card so it never moves between servers.
            var tail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = P(0) };
            tail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tail.Controls.Add(lblNotes, 0, 0);
            tail.Controls.Add(lnkMap, 0, 1);
            tail.Controls.Add(BuildModsPanel(), 0, 2);   // the server's mods, between the notes and Join
            tail.Controls.Add(joinRow, 0, 3);
            sc.Controls.Add(tail, 0, 5); sc.SetColumnSpan(tail, 2);
            serverCard.Controls.Add(sc);

            emptyCard = new Card { Dock = DockStyle.Fill, Dashed = true, BorderColor = Theme.Bot, BackColor = Theme.Header, Padding = P(30) };
            var ec = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = P(0) };
            ec.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ec.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            ec.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ec.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ec.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            lblEmptyTitle = new Label { AutoSize = true, Font = Theme.Display(15f), ForeColor = Theme.Bone, Anchor = AnchorStyles.None, Margin = P(0, 0, 0, 8) };
            lblEmptyBody = new Label { AutoSize = true, MaximumSize = Sz(620, 0), Font = Theme.Body(10.5f), ForeColor = Theme.Muted, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter };
            ec.Controls.Add(lblEmptyTitle, 0, 1);
            ec.Controls.Add(lblEmptyBody, 0, 2);
            emptyCard.Controls.Add(ec);
            holder.Controls.Add(serverCard);
            holder.Controls.Add(emptyCard);
            g.Controls.Add(holder, 0, 2);

            // ---- recent
            var rh = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 6) };
            rh.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rh.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rh.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rh.Controls.Add(Caps("Recent groups"), 0, 0);
            rh.Controls.Add(new GraffitiLabel("see you in the safe room", 12.5f) { Anchor = AnchorStyles.Right, Angle = -3f }, 1, 0);
            lnkLan = new LinkLabel { Text = "Find games on my network", AutoSize = true, Font = Theme.BodySemi(10f), Margin = P(16, 6, 0, 0), Anchor = AnchorStyles.Right };
            lnkLan.LinkClicked += async (s, e) => await FindLan();
            rh.Controls.Add(lnkLan, 2, 0);
            g.Controls.Add(rh, 0, 3);

            lvRecent = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            lvRecent.Columns.Add("Server", S(330));
            lvRecent.Columns.Add("Address", S(200));
            lvRecent.Columns.Add("Last joined", S(160));
            DarkList.Apply(lvRecent);
            lvRecent.DoubleClick += async (s, e) =>
            {
                if (lvRecent.SelectedItems.Count == 0) return;
                txtTarget.Text = ((RecentServer)lvRecent.SelectedItems[0].Tag).Address;
                await Check();
            };
            var lb = new Card { Dock = DockStyle.Fill, Padding = P(1), Margin = P(0) };
            lb.Controls.Add(DarkList.WithHeader(lvRecent));
            g.Controls.Add(lb, 0, 4);

            lnkGame = new LinkLabel { Text = "Change game folder…", AutoSize = true, Font = Theme.Body(9f), Margin = P(0, 0, 24, 0) };
            lnkGame.LinkClicked += (s, e) => BrowseGame();
            // Optional: the host's 8-Player HUD can show health bars and item icons, which needs this add-on.
            lnkHud = new LinkLabel { AutoSize = true, Font = Theme.Body(9f), Margin = P(0) };
            lnkHud.LinkClicked += (s, e) => ToggleHudAddon();
            new ToolTip().SetToolTip(lnkHud, "The host's 8-Player HUD can show survivors 6-8 with health bars and the game's item icons.\nThat look needs this small add-on in your game; without it you'd see stray boxes.");
            var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = P(24, 0, 24, 10), BackColor = Theme.Ground };
            lnkSkins = new LinkLabel { Text = "Skins…", AutoSize = true, Font = Theme.Body(9f), Margin = P(0, 0, 24, 0) };
            lnkSkins.LinkClicked += (s, e) => OpenSkins();
            new ToolTip().SetToolTip(lnkSkins, "Pick which skin add-on to use for each survivor, weapon, item and infected (only you see them).");
            lnkWorkshop = new LinkLabel { Text = "Open Workshop", AutoSize = true, Font = Theme.Body(9f), Margin = P(0, 0, 24, 0) };
            lnkWorkshop.LinkClicked += (s, e) => Workshop.Open();
            new ToolTip().SetToolTip(lnkWorkshop, "Left 4 Dead 2's Steam Workshop, for skins and the campaigns your host plays.");
            foot.Controls.Add(lnkGame);
            foot.Controls.Add(lnkSkins);
            foot.Controls.Add(lnkWorkshop);
            foot.Controls.Add(lnkHud);

            // On a screen too short for the whole window, the body scrolls instead of squashing the lists.
            // A long list of the server's mods makes the card, and so the page, taller: it scrolls (FitServerCard).
            body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = Sz(620, BodyMinHeight), BackColor = Theme.Ground };
            Theme.DarkScrollbars(body);
            body.Controls.Add(g);
            Controls.Add(body);
            Controls.Add(foot);
            Controls.Add(header);
        }

        /// <summary>
        /// The server card grows until the whole mod list fits (measured from the laid-out list, so there's no gap
        /// above Join game), and the page scrolls to fit the card. Without a list it's back to its usual height.
        /// </summary>
        void FitServerCard()
        {
            var panel = modsList.Parent;
            int h = S(CardBase);
            if (serverCard.Visible && panel.Visible)
            {
                serverCard.PerformLayout();
                h = holder.Height + (modsList.Bottom + modsList.Margin.Bottom) - panel.Height;
            }
            h = Math.Max(S(CardBase), h);
            if (holder.Height == h) return;
            holder.Height = h;
            body.AutoScrollMinSize = new Size(S(620), S(BodyMinHeight) + h - S(CardBase));
        }

        void FitToScreen()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
        }

        static Label Caps(string text) => new Label { Text = text.ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(8.5f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 4) };

        static Card Stat(string label, out Label value)
        {
            var c = new Card { Dock = DockStyle.Fill, Height = S(58), BackColor = Theme.Panel, Padding = P(10, 8, 10, 8), Margin = P(0, 0, 8, 0) };
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = P(0), BackColor = Color.Transparent };
            f.Controls.Add(new Label { Text = label.ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(7.5f), ForeColor = Theme.Muted, Margin = P(0) });
            value = new Label { AutoSize = true, Font = Theme.BodyBold(11f), ForeColor = Theme.Bone, Margin = P(0, 2, 0, 0) };
            f.Controls.Add(value);
            c.Controls.Add(f);
            return c;
        }

        void ShowEmpty(string title, string body)
        {
            // one pass for the swap, so the server card doesn't flash through
            Draw.Batch(emptyCard.Parent, () =>
            {
                lblEmptyTitle.Text = title.ToUpperInvariant();
                lblEmptyBody.Text = body;
                emptyCard.Visible = true;
                serverCard.Visible = false;
            });
            FitServerCard();
        }

        void UpdateGameLabel()
        {
            bool ok = GameLocator.IsGameDir(settings.GamePath);
            header.Status = ok ? $"L4D2 {GameLocator.PatchVersion(settings.GamePath)} · no mods needed" : "L4D2 not found · change game folder below";
            header.StatusColor = ok ? Theme.Muted : Theme.Amber;
            UpdateHudLink();
        }

        void UpdateHudLink()
        {
            var st = HudAddon.Check(settings.GamePath);
            lnkHud.Visible = st != HudAddon.Status.NoGame;
            lnkHud.Text = st == HudAddon.Status.Installed ? "8-player HUD add-on installed · Remove"
                : st == HudAddon.Status.Outdated ? "Update the 8-player HUD add-on"
                : st == HudAddon.Status.Disabled ? "8-player HUD add-on is off in the game's Add-ons menu · Turn on"
                : "Install the 8-player HUD add-on (optional)";
            lnkHud.LinkColor = st == HudAddon.Status.Installed ? Theme.Muted : Theme.Amber;
        }

        void ToggleHudAddon()
        {
            try
            {
                if (HudAddon.Check(settings.GamePath) == HudAddon.Status.Installed) HudAddon.Remove(settings.GamePath);
                else HudAddon.Install(settings.GamePath);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            UpdateHudLink();
        }

        void BrowseGame()
        {
            using (var d = new FolderBrowserDialog { Description = "Select the 'Left 4 Dead 2' folder (it contains left4dead2.exe)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!GameLocator.IsGameDir(d.SelectedPath)) { MessageBox.Show(this, "That folder doesn't contain left4dead2.exe.", Title); return; }
                settings.GamePath = d.SelectedPath;
                settings.Save();
                UpdateGameLabel();
            }
        }

        // ================================================================== behaviour

        JoinTarget Target()
        {
            var t = JoinTarget.Parse(txtTarget.Text);
            if (t != null && string.IsNullOrEmpty(t.Password)) t.Password = txtPassword.Text;
            else if (t != null && txtPassword.Text.Length == 0) txtPassword.Text = t.Password;
            return t;
        }

        async Task<ServerInfo> Check()
        {
            var t = Target();
            if (t == null)
            {
                ShowEmpty("That isn't an address", "Paste the invite (steam://connect/...), an IP like 1.2.3.4:27015, or the host's 'connect ...' line.");
                return null;
            }

            btnCheck.Enabled = false;
            ShowEmpty("Calling " + t.Address + "…", "Asking the server for its name, map and players.");
            try
            {
                ServerInfo info;
                try { info = await ServerQuery.Query(t.Host, t.Port, 3000); }
                catch (Exception ex) { ShowEmpty("Couldn't find " + t.Host, ex.Message); return null; }

                if (info == null)
                {
                    ShowEmpty("No answer from " + t.Address,
                        Network.IsPrivate(t.Host)
                            ? $"{t.Host} is a local network address. It only works on the host's network (or the same Tailscale/ZeroTier/Radmin network). Ask the host for the invite (Copy invite on their Play tab)."
                            : $"The server may not be started yet, the host's router may not be forwarding port {t.Port}, or the address is wrong. You can still try Join: some servers don't answer queries.");
                    serverCard.Visible = false;
                    return null;
                }

                lblName.Text = info.Name;
                lblMap.Text = $"{info.Map} · {info.Game}";
                lblCount.Text = $"{info.Players} / {info.MaxPlayers}";
                pips.Set(info.Players, info.MaxPlayers);
                lblPing.Text = $"{info.PingMs} ms";

                var notes = new List<string>();
                var mine = GameLocator.PatchVersion(settings.GamePath);
                bool versionOk = mine == null || info.Version == null || mine == info.Version;
                lblVersion.Text = info.Version + (mine == null ? "" : versionOk ? " · matches" : " · yours " + mine);
                lblVersion.ForeColor = versionOk ? Theme.Bone : Theme.Amber;
                if (!versionOk) notes.Add($"⚠ Your game is {mine} but the server is {info.Version}. Whoever is older needs to update first.");
                if (info.Password && string.IsNullOrEmpty(t.Password)) notes.Add("⚠ This server needs a password. Enter it above.");
                if (info.MaxPlayers > 0 && info.Players >= info.MaxPlayers) notes.Add("⚠ The server is full right now.");

                lnkMap.Visible = false;
                if (ServerQuery.IsOfficialMap(info.Map)) { lblMapState.Text = "Official · you have it"; lblMapState.ForeColor = Theme.Bone; }
                else
                {
                    var maps = await Task.Run(() => ServerQuery.ClientMaps(settings.GamePath == null ? null : Path.Combine(settings.GamePath, "left4dead2")));
                    if (maps.Contains(info.Map)) { lblMapState.Text = "Custom · you have it"; lblMapState.ForeColor = Theme.Bone; }
                    else
                    {
                        missingMap = info.Map;
                        missingWorkshopId = t.WorkshopId;
                        lblMapState.Text = "Custom · missing";
                        lblMapState.ForeColor = Theme.Amber;
                        notes.Add($"⚠ The host is on a custom map ({info.Map}) that you don't have. Subscribe to the same campaign first, then start L4D2 once so it downloads.");
                        lnkMap.Text = t.WorkshopId != null ? "Open the host's campaign on the Workshop (subscribe there)" : $"Search the Workshop for '{info.Map}'";
                        lnkMap.Visible = true;
                    }
                }

                await LoadServerMods(t);
                // the host's campaign is one of the server's required mods: it comes with them
                if (missingMap != null && lblMapState.Text == "Custom · missing" && settings.DownloadMods && serverMods.Any(m => m.Required && !m.Have && m.Url != null))
                {
                    notes.RemoveAll(n => n.Contains("custom map"));
                    notes.Add($"The host is on a custom map ({info.Map}). It's in the server's required mods below, which download when you click Join game.");
                    lnkMap.Visible = false;
                }

                lblNotes.Text = string.Join(Environment.NewLine, notes);
                lblNotes.Visible = notes.Count > 0;
                btnJoin.Text = "Join game";
                btnLobby.Text = "Go to lobby";
                Draw.Batch(serverCard.Parent, () => { emptyCard.Visible = false; serverCard.Visible = true; });
                FitServerCard();
                Remember(t, info.Name);
                return info;
            }
            finally { btnCheck.Enabled = true; }
        }

        const string LobbyHow = "join your host's lobby: accept their invite, or right-click them in Steam's friends list and choose Join Game.";

        /// <summary>
        /// Join game: start L4D2 and connect once the add-ons load. lobby: the same preparation (the server's mods, the
        /// skin picks) but the game stays on the main menu, for joining the host's lobby there.
        /// </summary>
        async Task JoinGame(bool lobby = false)
        {
            var t = Target();
            if (t == null && !lobby) { await Check(); return; }
            var steam = GameLocator.FindSteam();
            if (steam == null) { MessageBox.Show(this, "Steam wasn't found. Start Steam and try again.", Title); return; }
            try
            {
                if (lobby)
                {
                    if (Launcher.GameRunning())
                    {
                        MessageBox.Show(this, "Left 4 Dead 2 is already open. To play in the lobby, " + LobbyHow, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!await GetServerMods()) return;
                    ReapplySkins();
                    if (!SkinsSettled()) return;
                    Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), $"-applaunch {GameLocator.GameAppId}") { UseShellExecute = true });
                    if (t != null) Remember(t, null, joined: true);
                    btnLobby.Text = "Starting L4D2…";
                    lblNotes.Text = "Starting L4D2 to the main menu. Once it's there, " + LobbyHow + " The lobby sends everyone to the server when the host starts.";
                    lblNotes.ForeColor = Theme.Bone;
                    lblNotes.Visible = true;
                    return;
                }
                if (Launcher.GameRunning())
                {
                    // a running game can't load new add-ons: missing required mods mean closing it first
                    if (serverMods.Any(m => m.Required && !m.Have))
                    {
                        MessageBox.Show(this, "This server needs mods you don't have yet. Close Left 4 Dead 2, then click Join game: the mods download first and the game loads them when it starts.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Process.Start(t.SteamUrl); // the running game connects straight away
                }
                else
                {
                    if (!await GetServerMods()) return;   // the server's mods you're missing, before the game reads its add-ons
                    ReapplySkins();   // the remembered skin picks, before the game reads its add-on list
                    if (!SkinsSettled()) return;
                    // the connect waits until the game has loaded the Workshop add-ons: joining straight away leaves them
                    // (skins, the host's campaign) out for the whole session
                    var started = DateTime.UtcNow;
                    Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), $"-applaunch {GameLocator.GameAppId}") { UseShellExecute = true });
                    _ = ConnectOnceAddonsLoad(t, started);
                }
                Remember(t, null, joined: true);
                btnJoin.Text = "Starting L4D2…";
                lblNotes.Text = "Connecting. You'll spawn as a survivor automatically. If you end up spectating, type !join in chat.";
                lblNotes.ForeColor = Theme.Bone;
                lblNotes.Visible = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            await Task.CompletedTask;
        }

        /// <summary>The game just started: once it has loaded the Workshop add-ons, it's sent to the server (steam://connect).</summary>
        async Task ConnectOnceAddonsLoad(JoinTarget t, DateTime started)
        {
            lblNotes.Text = "Starting L4D2. It joins once your Workshop add-ons (skins, campaigns) have loaded, usually under a minute.";
            bool loaded = ClientGameDir != null && await Launcher.WaitForWorkshopAddons(ClientGameDir, started, TimeSpan.FromMinutes(2));
            if (!loaded && ClientGameDir == null) await Task.Delay(20000);
            if (IsDisposed || !Launcher.GameRunning()) return;
            try { Process.Start(t.SteamUrl); } catch (Exception ex) { lblNotes.Text = "Couldn't send the game to the server: " + ex.Message; return; }
            lblNotes.Text = "Connecting. You'll spawn as a survivor automatically. If you end up spectating, type !join in chat.";
        }

        async Task FindLan()
        {
            lnkLan.Enabled = false;
            ShowEmpty("Listening on your network…", "Looking for L4D2 servers on this network.");
            try
            {
                var found = await ServerQuery.FindLan();
                if (found.Count == 0) { ShowEmpty("Nothing on your network", "No L4D2 servers answered here. Use the host's invite instead."); return; }
                foreach (var f in found) Remember(JoinTarget.Parse(f.Address), f.Name);
                ShowEmpty($"Found {found.Count} game(s) on your network", "They're in Recent groups below. Double-click one to check it.");
            }
            finally { lnkLan.Enabled = true; }
        }

        void Remember(JoinTarget t, string name, bool joined = false)
        {
            var r = settings.Recent.FirstOrDefault(x => x.Address == t.Address);
            if (r == null) { r = new RecentServer { Address = t.Address }; settings.Recent.Insert(0, r); }
            if (!string.IsNullOrEmpty(name)) r.Name = name;
            if (joined) r.LastJoined = DateTime.Now.ToString("g");
            settings.Recent = settings.Recent.Take(15).ToList();
            settings.Save();
            RefreshRecent();
        }

        void RefreshRecent()
        {
            lvRecent.BeginUpdate();
            lvRecent.Items.Clear();
            foreach (var r in settings.Recent)
                lvRecent.Items.Add(new ListViewItem(new[] { r.Name ?? "(unknown)", r.Address, r.LastJoined ?? "" }) { Tag = r, ForeColor = Theme.Bone });
            lvRecent.EndUpdate();
        }
    }
}
