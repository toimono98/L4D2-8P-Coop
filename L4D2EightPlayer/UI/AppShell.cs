using System;
using System.Drawing;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.Join;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// One app, two sides: the host's window (set up and run the server) and the join window.
    /// One shows at a time. Switching hides the other instead of closing it, so a running server or
    /// an install carries on; closing the window you're on closes the app (the host's usual
    /// "the server is still running" question included).
    /// </summary>
    sealed class AppShell : ApplicationContext
    {
        public static AppShell Instance;
        /// <summary>Set by --shell-test: windows open off-screen and the start side isn't saved.</summary>
        public static bool Test;
        MainForm host;
        JoinForm join;
        Form current;
        bool exiting, done;
        readonly string invite;

        public AppShell(string mode, string invite)
        {
            this.invite = invite;
            Switch(mode);
        }

        public string Mode => current == join ? StartMode.Join : StartMode.Host;
        internal Form HostWindow => host;
        internal Form JoinWindow => join;

        /// <summary>Shows the host or join window, where the current one is, and remembers it for next time.</summary>
        public void Switch(string mode)
        {
            Form next = mode == StartMode.Join
                ? join ?? (join = Track(new JoinForm(invite ?? "")))
                : host ?? (Form)(host = Track(new MainForm()));
            if (next == current) { next.Activate(); return; }
            if (Test) { next.StartPosition = FormStartPosition.Manual; next.Location = new Point(-30000, -30000); next.ShowInTaskbar = false; }
            else if (current != null) Place(next, current);
            next.Show();
            if (next.WindowState == FormWindowState.Minimized) next.WindowState = FormWindowState.Normal;
            next.Activate();
            current?.Hide();
            current = next;
            if (!Test) StartMode.Save(mode);
        }

        /// <summary>Centres the next window over the last one, inside that screen.</summary>
        static void Place(Form next, Form last)
        {
            var from = last.WindowState == FormWindowState.Normal ? last.Bounds : last.RestoreBounds;
            var wa = Screen.FromRectangle(from).WorkingArea;
            var at = new Point(from.X + (from.Width - next.Width) / 2, from.Y + (from.Height - next.Height) / 2);
            at.X = Math.Max(wa.Left, Math.Min(at.X, wa.Right - next.Width));
            at.Y = Math.Max(wa.Top, Math.Min(at.Y, wa.Bottom - next.Height));
            next.StartPosition = FormStartPosition.Manual;
            next.Location = at;
        }

        T Track<T>(T f) where T : Form
        {
            f.FormClosing += (s, e) =>
            {
                // closing the join window while the host's is alive: close that too, and if it says
                // "not yet" (the server is running), stay open on the host side instead
                if (exiting || e.Cancel || !(f is JoinForm) || host == null) return;
                exiting = true;
                host.Close();
                exiting = false;
                if (host != null) { e.Cancel = true; Switch(StartMode.Host); }
            };
            f.FormClosed += (s, e) =>
            {
                if (f == (Form)host) host = null; else join = null;
                if (!exiting && (host != null || join != null))
                {
                    exiting = true;
                    host?.Close();
                    join?.Close();
                }
                if (host == null && join == null && !done) { done = true; ExitThread(); }
            };
            return f;
        }
    }
}
