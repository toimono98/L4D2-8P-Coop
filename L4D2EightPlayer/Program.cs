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
                Application.Run(AppShell.Instance = new AppShell(mode, invite));
            }
        }

        /// <summary>
        /// Off-screen check of the HOST | JOIN switch: host, join, host, join, then closing the join window
        /// must close the hidden host one too and end the app. Writes what it saw; exit code 0 = all as expected.
        /// </summary>
        static int ShellTest(string outFile)
        {
            var log = new System.Text.StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            MainForm.Preview = true; Settings.ReadOnly = true; JoinForm.Preview = true; JoinSettings.ReadOnly = true;
            AppShell.Test = true;
            Application.EnableVisualStyles();
            var shell = AppShell.Instance = new AppShell(StartMode.Host, "");
            var steps = new Action[]
            {
                () => { Check("starts on host", shell.Mode == StartMode.Host && shell.HostWindow.Visible && shell.JoinWindow == null); shell.Switch(StartMode.Join); },
                () => { Check("join shows, host hidden but alive", shell.JoinWindow.Visible && !shell.HostWindow.Visible && !shell.HostWindow.IsDisposed); shell.Switch(StartMode.Host); },
                () => { Check("host shows again, same window", shell.HostWindow.Visible && !shell.JoinWindow.Visible); shell.Switch(StartMode.Join); },
                () => { Check("join again, same window", shell.JoinWindow.Visible); shell.JoinWindow.Close(); },
            };
            int i = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (s, e) => { if (i < steps.Length) steps[i++](); else { timer.Stop(); Check("closing join left the app open", false); Application.ExitThread(); } };
            timer.Start();
            Application.Run(shell);
            timer.Stop();
            Check("closing the join window closed both and ended the app", i == steps.Length && shell.HostWindow == null && shell.JoinWindow == null);
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
