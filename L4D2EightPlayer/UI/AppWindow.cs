using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.Join;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The app's one window. Its two sides, hosting (set up and run the server) and joining a friend's
    /// game, are full views inside it, and HOST | JOIN in either side's top bar flips between them.
    /// The hidden side carries on (a running server, an install), the window keeps its size and place,
    /// and closing it asks the host side first (its "the server is still running" question).
    /// Each side is a Form embedded as a control, so it also still opens on its own for the screenshot checks.
    /// </summary>
    sealed class AppWindow : Form
    {
        public static AppWindow Instance;
        /// <summary>Set by --shell-test: the window opens off-screen and the start side isn't saved.</summary>
        public static bool Test;
        MainForm host;
        JoinForm join;
        Form current;
        readonly string invite;
        /// <summary>Each side's Load and Shown, as they happen (for --shell-test).</summary>
        internal readonly List<string> Seen = new List<string>();

        public AppWindow(string mode, string invite)
        {
            this.invite = invite;
            Theme.LoadFonts();
            Icon = Theme.AppIcon();
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            AutoScaleMode = AutoScaleMode.None;   // every size goes through Ui.S
            StartPosition = FormStartPosition.CenterScreen;
            // big enough for the host side; the join side is happy in anything from 640 x 600
            MinimumSize = Sz(860, 640);
            Size = mode == StartMode.Join ? Sz(860, 880) : Sz(1200, 1000);
            MainForm.FitToScreen(this);
            if (Test) { StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000); ShowInTaskbar = false; }
            Switch(mode);
            FormClosing += OnClosingWindow;
        }

        public string Mode => current == join ? StartMode.Join : StartMode.Host;
        internal Form HostSide => host;
        internal Form JoinSide => join;

        /// <summary>Shows the host or join side in this window, and remembers it for next time.</summary>
        public void Switch(string mode)
        {
            Form next = mode == StartMode.Join
                ? join ?? (join = Embed(new JoinForm(invite ?? ""), "join"))
                : host ?? (Form)(host = Embed(new MainForm(), "host"));
            if (next == current) return;
            SuspendLayout();
            next.Visible = true;
            next.BringToFront();
            if (current != null) current.Visible = false;
            ResumeLayout();
            current = next;
            Text = next.Text;
            if (IsHandleCreated) next.SelectNextControl(null, true, true, true, true);
            if (!Test) StartMode.Save(mode);
        }

        /// <summary>A side as a borderless view that fills the window.</summary>
        T Embed<T>(T side, string name) where T : Form
        {
            side.TopLevel = false;
            side.FormBorderStyle = FormBorderStyle.None;
            side.MinimumSize = Size.Empty;
            side.Dock = DockStyle.Fill;
            side.Visible = false;
            // Both sides start work in Shown (the host's tour and layout, the join's server check).
            // A form inside another one may not raise it, so it's raised here once it's on screen.
            bool shown = false;
            side.Load += (s, e) => Seen.Add(name + " load");
            side.Shown += (s, e) => { shown = true; Seen.Add(name + " shown"); };
            side.VisibleChanged += (s, e) =>
            {
                if (!side.Visible || shown || !side.IsHandleCreated) return;
                BeginInvoke((Action)(() => { if (!shown && !side.IsDisposed) ((IAppSide)side).RaiseShown(); }));
            };
            Controls.Add(side);
            return side;
        }

        void OnClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (host == null || host.IsDisposed) return;
            // the host side's own closing: the running-server question, switching listen mods off, the tray icon
            var ask = new FormClosingEventArgs(e.CloseReason, false);
            host.OnClosing(host, ask);
            if (ask.Cancel) { e.Cancel = true; Switch(StartMode.Host); }
        }
    }

    /// <summary>What the window needs from each side.</summary>
    interface IAppSide
    {
        /// <summary>Raises the side's Shown event (for a side shown inside the window).</summary>
        void RaiseShown();
    }
}
