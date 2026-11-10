using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// New versions, for both sides of the window (one check per run, the same notice in each header):
    ///   automatic updates: the Setup downloads in the background ("DOWNLOADING VERSION X"), then "VERSION X READY ·
    ///   RESTART TO UPDATE"; it installs when the app closes, or now with Restart now.
    ///   by hand: "VERSION X IS OUT · INSTALL", and Install now downloads it, closes the app, updates it and opens it again.
    ///   a copy that isn't the installed app: "VERSION X IS OUT · DOWNLOAD" opens the release page, as before.
    /// The notice's window also switches between automatic and by hand.
    /// </summary>
    static class UpdateUi
    {
        enum Stage { None, Out, Downloading, Ready, Failed }
        static Stage stage;
        static string version, setup;
        static bool started, installed, restartOnExit;
        static readonly List<(NavBar nav, Action<string> log)> headers = new List<(NavBar, Action<string>)>();

        /// <summary>A header that shows the notice (and opens the update window when it's clicked).</summary>
        public static void Attach(NavBar nav, Action<string> log = null)
        {
            headers.Add((nav, log));
            nav.NoticeClicked += async (s, e) => await Clicked();
            nav.Notice = Notice;
        }

        /// <summary>Asks GitHub once per run (the answer is cached for a day) and, for automatic updates, downloads it.</summary>
        public static async Task Begin()
        {
            if (started) return;
            started = true;
            var failed = Updater.LastFailure();
            if (failed != null) Log("The last update didn't install (" + failed + "). It's tried again when the next version is ready; or download it from " + UpdateCheck.ReleasePage);
            var v = await UpdateCheck.Newer();
            if (v == null) return;
            version = v;
            installed = Updater.RunningInstalled(Application.ExecutablePath);
            if (installed && Updater.Auto) { await Download(); return; }
            Set(Stage.Out);
            Log($"Version {v} of L4D2 8-Player Co-op is out (you have {AppInfo.Version}). " +
                (installed ? "Click the notice at the top to install it." : "Download it from " + UpdateCheck.ReleasePage));
        }

        /// <summary>
        /// Check for updates: asks GitHub now, whatever the daily check said. A newer version goes the usual way
        /// (downloaded for automatic updates, else offered) and its window opens. Returns a line saying where things stand.
        /// </summary>
        public static async Task<string> CheckNow()
        {
            started = true;   // no second, background check this run
            if (!await UpdateCheck.Refresh()) return "Couldn't reach GitHub to check (offline?). Try again later.";
            var v = UpdateCheck.IsNewer(UpdateCheck.Last.Latest, AppInfo.Version) ? UpdateCheck.Last.Latest : null;
            if (v == null) return $"You have the latest version ({AppInfo.Version}).";
            if (stage == Stage.None || version != v)
            {
                version = v;
                installed = Updater.RunningInstalled(Application.ExecutablePath);
                if (installed && Updater.Auto) await Download(); else Set(Stage.Out);
            }
            await Clicked();
            return stage == Stage.Ready ? $"Version {v} is downloaded" + (Updater.Auto ? ": it installs when you close the app." : ".")
                 : stage == Stage.Failed ? $"Version {v} is out, but it couldn't be downloaded: the release page has it."
                 : $"Version {v} is out (you have {AppInfo.Version}).";
        }

        static async Task<bool> Download()
        {
            Set(Stage.Downloading);
            try
            {
                setup = await Updater.Download(version, null);
                Set(Stage.Ready);
                Log(Updater.Auto ? $"Version {version} is downloaded: it installs when you close the app (or click the notice at the top to restart now)."
                                 : $"Version {version} is downloaded.");
                return true;
            }
            catch (Exception ex)
            {
                Set(Stage.Failed);
                Log($"Version {version} is out, but it couldn't be downloaded ({ex.Message}). Click the notice at the top for the release page.");
                return false;
            }
        }

        static string Notice =>
            stage == Stage.Out ? $"VERSION {version} IS OUT · " + (installed ? "INSTALL" : "DOWNLOAD")
            : stage == Stage.Downloading ? $"DOWNLOADING VERSION {version}…"
            : stage == Stage.Ready ? $"VERSION {version} READY · " + (Updater.Auto ? "RESTART TO UPDATE" : "INSTALL")
            : stage == Stage.Failed ? $"VERSION {version} IS OUT · DOWNLOAD"
            : null;

        static void Set(Stage s)
        {
            stage = s;
            foreach (var (nav, _) in headers) if (!nav.IsDisposed) nav.Notice = Notice;
        }

        static void Log(string line) { foreach (var (_, log) in headers) log?.Invoke(line); }

        static async Task Clicked()
        {
            if (stage == Stage.Downloading) return;
            if (stage == Stage.Failed || (stage == Stage.Out && !installed)) { Process.Start(UpdateCheck.ReleasePage); return; }
            if (stage != Stage.Out && stage != Stage.Ready) return;
            var owner = AppWindow.Instance;
            using (var d = new UpdateDialog(version, stage == Stage.Ready))
            {
                var r = d.ShowDialog(owner);
                if (d.Auto != Updater.Auto)
                {
                    Updater.SetAuto(d.Auto);
                    Log(d.Auto ? "Updates now install automatically." : "Updates are now yours to install: the notice at the top says when one is out.");
                    Set(stage);
                }
                if (r != DialogResult.OK) return;
            }
            if (stage != Stage.Ready && !await Download()) { MessageBox.Show(owner, $"Version {version} couldn't be downloaded. The release page opens instead.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning); Process.Start(UpdateCheck.ReleasePage); return; }
            // the Setup runs once the window has closed (the host side may ask about a running server first)
            restartOnExit = true;
            owner?.Close();
            if (owner != null && !owner.IsDisposed && owner.Visible) restartOnExit = false;   // closing was cancelled
        }

        /// <summary>
        /// After the window has closed: Restart now / Install now update and open the app again; with automatic
        /// updates a downloaded version installs now, quietly, without opening it.
        /// </summary>
        public static void OnExit()
        {
            try
            {
                if (setup == null || stage != Stage.Ready) return;
                if (restartOnExit) Updater.RunSetup(setup, Process.GetCurrentProcess().Id, StartMode.Load(), restart: true);
                else if (Updater.Auto && installed) Updater.RunSetup(setup, Process.GetCurrentProcess().Id, StartMode.Load(), restart: false);
            }
            catch { /* the next start offers it again */ }
        }

        /// <summary>For the screenshot check: the update window.</summary>
        internal static Form DialogFor(string v, bool ready) => new UpdateDialog(v, ready);
    }

    /// <summary>"Version X is out": install now or later, what's new, and automatic updates on or off.</summary>
    sealed class UpdateDialog : Form
    {
        readonly GameCheck auto;
        public bool Auto => auto.Checked;

        public UpdateDialog(string version, bool ready)
        {
            Text = "Update";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Bone;
            Font = Theme.Body(10f);
            Icon = Theme.AppIcon();
            const int W = 500;
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(24, 20, 24, 16), BackColor = Theme.Panel };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(W)));
            Label Line(string text, Font font, Color color, Padding margin) => new Label { Text = text, AutoSize = true, MaximumSize = Sz(W, 0), Font = font, ForeColor = color, Margin = margin };
            t.Controls.Add(Line($"Version {version} is out", Theme.Display(16f), Theme.Bone, P(0, 0, 0, 4)));
            t.Controls.Add(Line($"You have {AppInfo.Version}. " + (ready
                ? "It's downloaded. Restarting closes the app, installs it and opens the app again; your settings, skins and server stay as they are."
                : "Installing downloads it, closes the app, installs it and opens the app again; your settings, skins and server stay as they are."),
                Theme.Body(10f), Theme.Muted, P(0, 0, 0, 6)));
            var notes = new LinkLabel { Text = "What's new in " + version, AutoSize = true, Font = Theme.BodySemi(10f), Margin = P(0, 0, 0, 12) };
            notes.LinkClicked += (s, e) => Process.Start(UpdateCheck.ReleasePage);
            t.Controls.Add(notes);
            auto = new GameCheck { Text = "Install updates automatically (they install when you close the app)", Checked = Updater.Auto, AutoSize = true, MaximumSize = Sz(W, 0), Font = Theme.Body(10f), Margin = P(0, 0, 0, 0) };
            t.Controls.Add(auto);
            var go = new GameButton(ready ? "Restart now" : "Install now", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0) };
            var later = new GameButton("Later", ButtonKind.Ghost, 10.5f) { DialogResult = DialogResult.Cancel, Margin = P(0) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0, 18, 0, 0), BackColor = Theme.Panel };
            buttons.Controls.Add(go);
            buttons.Controls.Add(later);
            t.Controls.Add(buttons);
            Controls.Add(t);
            ClientSize = t.GetPreferredSize(Size.Empty);
            AcceptButton = go;
            CancelButton = later;
            Theme.Style(this);
        }
    }
}
