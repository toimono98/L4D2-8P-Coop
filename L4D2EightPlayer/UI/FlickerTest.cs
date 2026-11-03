using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --flicker-test &lt;report&gt; [--legacy] [--composited]: what the screen really shows while the window repaints.
    /// A background thread copies the window from the screen non-stop, like a screen recording, while the UI
    /// thread repaints: a dropdown over and over, then a round of tab switches. A captured frame that matches
    /// neither the picture before nor the one after is a glitch the eye can catch: a blink, a half-drawn box,
    /// a stale column. --legacy turns this version's drawing fixes off for comparison; --composited draws the
    /// whole window through one back buffer (WS_EX_COMPOSITED). The window shows topmost in the main screen's
    /// top-left corner, and only its own rectangle is captured.
    /// </summary>
    static class FlickerTest
    {
        /// <summary>A screen capture, shrunk to a grey grid of 4x4 blocks (enough to see what changed).</summary>
        sealed class Frame
        {
            public byte[] Cells;
            public int W, H;
            public double At;
            public Bitmap Image;   // kept only when asked (to look at a glitch)
            public static bool Keep;

            public static Frame Grab(Rectangle screen, int block = 4)
            {
                using (var bmp = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppRgb))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                    var f = new Frame { W = screen.Width / block, H = screen.Height / block, At = Clock.Elapsed.TotalMilliseconds, Image = Keep ? (Bitmap)bmp.Clone() : null };
                    f.Cells = new byte[f.W * f.H];
                    var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    var px = new byte[data.Stride * data.Height];
                    Marshal.Copy(data.Scan0, px, 0, px.Length);
                    bmp.UnlockBits(data);
                    for (int cy = 0; cy < f.H; cy++)
                        for (int cx = 0; cx < f.W; cx++)
                        {
                            int sum = 0;
                            for (int y = 0; y < block; y++)
                            {
                                int row = (cy * block + y) * data.Stride + cx * block * 4;
                                for (int x = 0; x < block; x++) { int i = row + x * 4; sum += px[i] + px[i + 1] + px[i + 2]; }
                            }
                            f.Cells[cy * f.W + cx] = (byte)(sum / (block * block * 3));
                        }
                    return f;
                }
            }
        }

        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        const int Same = 10;   // grey levels two cells may differ by and still look the same

        /// <summary>Records frames of a screen rectangle on a background thread until stopped.</summary>
        sealed class Recorder
        {
            readonly List<Frame> frames = new List<Frame>();
            volatile bool stop;
            Thread thread;
            public void Start(Rectangle r) { thread = new Thread(() => { while (!stop) { var f = Frame.Grab(r); lock (frames) frames.Add(f); } }) { IsBackground = true }; thread.Start(); }
            public List<Frame> Stop() { stop = true; thread.Join(); return frames; }
        }

        static void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(5); } }

        static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in All(c)) yield return d; }
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        public static int Run(string outFile, bool legacy, bool composited)
        {
            var log = new StringBuilder();
            PaintOver.Direct = legacy;
            Frame.Keep = Environment.GetCommandLineArgs().Contains("--keep");
            Draw.NoBatch = legacy;
            MainForm.Preview = true;
            Settings.ReadOnly = true;
            Application.EnableVisualStyles();
            var wa = Screen.PrimaryScreen.WorkingArea;
            bool max = Environment.GetCommandLineArgs().Contains("--max"), first = Environment.GetCommandLineArgs().Contains("--first-visit");
            // the host side as the app shows it: embedded in the one app window (--standalone: on its own)
            Form window;
            MainForm form;
            if (Environment.GetCommandLineArgs().Contains("--standalone"))
                window = form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(wa.Left, wa.Top), ShowInTaskbar = false };
            else
            {
                AppWindow.Test = true;   // (keeps the start side as it is)
                var w = new AppWindow(StartMode.Host, "");
                w.Location = new Point(wa.Left, wa.Top);
                window = w;
                form = (MainForm)w.HostSide;
            }
            window.TopMost = true;
            if (max) window.WindowState = FormWindowState.Maximized;
            if (composited) window.HandleCreated += (s, e) => SetWindowLong(window.Handle, -20, GetWindowLong(window.Handle, -20) | 0x02000000 /* WS_EX_COMPOSITED */);
            log.AppendLine($"drawing: {(legacy ? "as before (overlay on screen, no held drawing)" : "this version")}{(composited ? ", whole window composited" : "")}");
            window.Show();
            Pump(9000);
            var tabs = All(form).OfType<PageHost>().First();
            // every tab once, so later switches only redraw (--first-visit leaves the Internet tab for the test)
            for (int i = 0; i < tabs.Count; i++) { if (first && i == 3) continue; tabs.SelectedIndex = i; Pump(i == 3 ? 4000 : 700); }
            log.AppendLine($"window: {window.Size}{(form.TopLevel ? ", host side on its own" : ", host side inside the app window")}{(max ? " (maximized)" : "")}{(first ? ", the Internet tab opened for the first time in the test" : "")}");

            // 1. a dropdown repainting 400 times: any captured frame unlike its settled look is a blink
            tabs.SelectedIndex = 2;
            Pump(1000);
            var combo = (ComboBox)typeof(MainForm).GetField("cmbCampaign", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var cr = combo.RectangleToScreen(combo.ClientRectangle);
            // every capture comes from the recording thread (screen coordinates can be scaled per thread),
            // the settled look included: the last one before the repaints start
            var rec = new Recorder();
            rec.Start(cr);
            Pump(400);
            var t0 = Clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 400; i++) { combo.Invalidate(); combo.Update(); Application.DoEvents(); }
            var t1 = Clock.Elapsed.TotalMilliseconds;
            var all = rec.Stop();
            var settled = all.Last(f => f.At < t0);
            var shots = all.Where(f => f.At >= t0 && f.At <= t1 + 50).ToList();
            int blinks = shots.Count(f => Differ(f, settled) > 4);
            if (Frame.Keep)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                settled.Image?.Save(Path.Combine(dir, "dropdown-settled.png"));
                int n = 0;
                foreach (var f in shots.Where(f => Differ(f, settled) > 4).Take(4)) f.Image?.Save(Path.Combine(dir, $"dropdown-glitch{++n}.png"));
                log.AppendLine("   most different cells: " + string.Join(", ", shots.Select(f => Differ(f, settled)).OrderByDescending(d => d).Take(8)) + $" of {settled.Cells.Length}");
            }
            log.AppendLine($"dropdown: 400 repaints in {t1 - t0:0} ms, {shots.Count} screen captures, {blinks} showed it half-drawn");

            // 2. tab switches: frames that are neither the old page nor the new one
            var client = window.RectangleToScreen(window.ClientRectangle);
            int totalMixed = 0;
            foreach (var to in new[] { 3, 2, 1, 2, 0, 2 })   // from the Play tab
            {
                var from = tabs.SelectedIndex;
                rec = new Recorder();
                rec.Start(client);
                Pump(150);
                var switchedAt = Clock.Elapsed.TotalMilliseconds;
                tabs.SelectedIndex = to;
                Pump(1200);
                var frames = rec.Stop();
                var before = frames.Last(f => f.At < switchedAt);
                var after = frames.Last();
                // only the cells that differ between the two pages say which page a frame shows
                var changing = Enumerable.Range(0, before.Cells.Length).Where(i => Math.Abs(before.Cells[i] - after.Cells[i]) > Same).ToArray();
                int mixed = 0;
                double firstMixed = -1, lastMixed = -1;
                foreach (var f in frames.Where(f => f.At >= switchedAt))
                {
                    int asAfter = changing.Count(i => Math.Abs(f.Cells[i] - after.Cells[i]) <= Same);
                    int asBefore = changing.Count(i => Math.Abs(f.Cells[i] - before.Cells[i]) <= Same);
                    double fa = changing.Length == 0 ? 1 : (double)asAfter / changing.Length, fb = changing.Length == 0 ? 0 : (double)asBefore / changing.Length;
                    if (fa < 0.97 && fb < 0.97)
                    {
                        mixed++;
                        if (firstMixed < 0) firstMixed = f.At - switchedAt;
                        lastMixed = f.At - switchedAt;
                    }
                }
                totalMixed += mixed;
                log.AppendLine($"tab {from + 1} -> {to + 1}: {frames.Count(f => f.At >= switchedAt)} captures, {mixed} half-switched" +
                               (mixed > 0 ? $" (from {firstMixed:0} to {lastMixed:0} ms after the click)" : ""));
            }
            log.AppendLine($"tab switches: {totalMixed} half-switched captures in all");

            window.Close();
            File.WriteAllText(outFile, log.ToString());
            return 0;
        }

        /// <summary>How many cells of two same-size frames look different.</summary>
        static int Differ(Frame a, Frame b) => a.Cells.Length != b.Cells.Length ? int.MaxValue : a.Cells.Where((c, i) => Math.Abs(c - b.Cells[i]) > Same).Count();
    }
}
