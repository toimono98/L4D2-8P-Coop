using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.Join;
using L4D2EightPlayer.UI;

namespace L4D2EightPlayer
{
    static class Program
    {
        /// <summary>
        ///   (no arguments)              opens on the side used last (the Setup sets the first one)
        ///   --host | --join             opens on that side
        ///   steam://connect/… or IP     opens the join side with that invite filled in and checked
        ///   --tour | --no-tour          from the Setup's last page: "Show me around" ticked or unticked
        ///   --selftest …                the host side's headless checks (see SelfTest)
        ///   --join-selftest &lt;address&gt; &lt;out&gt;               queries a server the way Check server does
        ///   --join-audit &lt;dir&gt; &lt;scale&gt; &lt;mock&gt; &lt;report&gt;    the join window's layout audit
        ///   --join-shot &lt;file.png&gt; [--tour]               the join window (and each tour stop) to PNGs
        ///   --shell-test &lt;report&gt;                         the one window: switching sides, size, closing (off-screen)
        ///   --paint-test &lt;report&gt; [--off-screen]          repaint passes, two-pass boxes and the progress bar's frame rate
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Headless checks for development and troubleshooting: L4D2-8P-Coop.exe --selftest --out report.txt
            if (args.Contains("--selftest")) { Environment.Exit(SelfTest.Run(args)); return; }

            if (args.Length >= 3 && args[0] == "--join-selftest")
            {
                var t = JoinTarget.Parse(args[1]);
                var info = t == null ? null : ServerQuery.Query(t.Host, t.Port).GetAwaiter().GetResult();
                File.WriteAllText(args[2], t == null ? "parse failed" :
                    $"target {t.Address} pw='{t.Password}' steam={t.SteamUrl} workshop={t.WorkshopId ?? "-"}\n" +
                    (info == null ? "no answer" : $"{info.Name} | {info.Map} | {info.Players}/{info.MaxPlayers} bots {info.Bots} | v{info.Version} | pw {info.Password} | {info.PingMs}ms") +
                    "\nplayers: " + string.Join(", ", ServerQuery.Players(t.Host, t.Port).GetAwaiter().GetResult() ?? new System.Collections.Generic.List<string> { "(no answer)" }));
                return;
            }
            if (args.Length >= 5 && args[0] == "--join-audit")
            {
                JoinAudit.Run(args[1], float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3], args[4]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--join-shot") { JoinShot(args[1], args.Contains("--tour")); return; }
            if (args.Length >= 2 && args[0] == "--shell-test") { Environment.Exit(ShellTest(args[1])); return; }
            if (args.Length >= 2 && args[0] == "--paint-test") { PaintTest.OffScreen = args.Contains("--off-screen"); Environment.Exit(PaintTest.Run(args[1])); return; }

            // an invite on the command line (steam://connect/…, IP:port) opens the join side with it
            var invite = string.Join(" ", args.Where(a => !a.StartsWith("--")));
            var mode = args.Contains("--join") ? StartMode.Join
                     : args.Contains("--host") ? StartMode.Host
                     : JoinTarget.Parse(invite) != null ? StartMode.Join
                     : StartMode.Load() ?? StartMode.Host;
            // the Setup's tour choice is about the side it opens on
            if (mode == StartMode.Join) { JoinForm.TourRequested = args.Contains("--tour"); JoinForm.TourDeclined = args.Contains("--no-tour"); }
            else { MainForm.TourRequested = args.Contains("--tour"); MainForm.TourDeclined = args.Contains("--no-tour"); }

            using (var single = new Mutex(true, "L4D2-8P-Manager-single-instance", out bool first))
            {
                if (!first)
                {
                    MessageBox.Show("L4D2 8-Player Co-op is already running.", "L4D2 8-Player Co-op", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(AppWindow.Instance = new AppWindow(mode, invite));
            }
        }

        /// <summary>
        /// Off-screen check of the one window: it opens on host, HOST | JOIN flips sides inside it (the
        /// window keeps its size, the hidden side stays alive, each side gets Load and Shown), the tour
        /// runs inside it, and closing it ends the app. Writes what it saw, plus a PNG of each side next to
        /// the report; exit code 0 = all as expected.
        /// </summary>
        static int ShellTest(string outFile)
        {
            var log = new System.Text.StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            var dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
            void Shot(Form f, string name) { using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height)); bmp.Save(Path.Combine(dir, name)); } }
            MainForm.Preview = true; Settings.ReadOnly = true; JoinForm.Preview = true; JoinSettings.ReadOnly = true;
            AppWindow.Test = true;
            Application.EnableVisualStyles();
            var w = AppWindow.Instance = new AppWindow(StartMode.Host, "");
            System.Drawing.Size size = default;
            UI.Themes.Tour tour = null;
            var steps = new Action[]
            {
                () =>
                {
                    Check("opens on host", w.Mode == StartMode.Host && w.HostSide.Visible && w.JoinSide == null && w.Text.Contains("Host"));
                    Check("host side got Load and Shown: " + string.Join(", ", w.Seen), w.Seen.Contains("host load") && w.Seen.Contains("host shown"));
                    Shot(w, "shell-host.png");
                    w.Size = size = new System.Drawing.Size(UI.Themes.Ui.S(1000), UI.Themes.Ui.S(760));   // as if the user resized it
                    w.Switch(StartMode.Join);
                },
                () =>
                {
                    Check("join shows in the same window, host hidden but alive", w.JoinSide.Visible && !w.HostSide.Visible && !w.HostSide.IsDisposed && w.Text.Contains("Join"));
                    Check($"the window kept its size ({w.Size})", w.Size == size);
                    Check("join side fills it", w.JoinSide.Size == w.ClientSize);
                    Check("join side got Load and Shown: " + string.Join(", ", w.Seen), w.Seen.Contains("join load") && w.Seen.Contains("join shown"));
                    Shot(w, "shell-join.png");
                    tour = ((JoinForm)w.JoinSide).BuildTour();
                    tour.Start();
                    tour.Go(tour.Count - 1);
                },
                () =>
                {
                    using (var bmp = tour.Snapshot()) bmp.Save(Path.Combine(dir, "shell-join-tour.png"));
                    Check("the tour runs inside the window", tour.Running);
                    tour.Close();
                    w.Switch(StartMode.Host);
                },
                () =>
                {
                    Check("host shows again, same window and size", w.HostSide.Visible && !w.JoinSide.Visible && w.Size == size);
                    w.Close();
                },
            };
            int i = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (s, e) => { if (i < steps.Length) steps[i++](); else { timer.Stop(); Check("closing left the app open", false); Application.ExitThread(); } };
            timer.Start();
            Application.Run(w);
            timer.Stop();
            Check("closing the window ended the app with both sides gone", i == steps.Length && w.IsDisposed && w.HostSide.IsDisposed && w.JoinSide.IsDisposed);
            File.WriteAllText(outFile, log.ToString());
            return ok ? 0 : 1;
        }

        /// <summary>The join window off-screen to a PNG; with tour, also &lt;file&gt;-tour1.png… for each stop.</summary>
        static void JoinShot(string file, bool tour)
        {
            Application.EnableVisualStyles();
            JoinForm.Preview = true;
            var f = new JoinForm("steam://connect/127.0.0.1:27099") { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
            f.Show();
            void Pump(double s) { var until = DateTime.Now.AddSeconds(s); while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); } }
            void Save(System.Drawing.Bitmap bmp, string path) { using (bmp) bmp.Save(path); }
            Pump(6);
            var shot = new System.Drawing.Bitmap(f.Width, f.Height);
            f.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
            Save(shot, file);
            if (tour)
            {
                var t = f.BuildTour();
                t.Start();
                for (int i = 0; i < t.Count; i++)
                {
                    t.Go(i);
                    Pump(0.8);
                    Save(t.Snapshot(), Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFileNameWithoutExtension(file) + $"-tour{i + 1}.png"));
                }
                t.Close();
            }
            f.Close();
        }
    }
}
