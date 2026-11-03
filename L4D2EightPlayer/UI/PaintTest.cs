using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --paint-test &lt;report&gt;: how the host window repaints, for chasing flicker and choppy animation.
    ///  - boxes that paint without a back buffer (each one draws in two passes: background, then content,
    ///    which is what a "blink" is);
    ///  - for each tab switch: how many paint passes reach the screen and how long the repaint takes;
    ///  - the progress bar's animation: frames per second, the longest gap and the biggest jump.
    /// The window opens in a corner of the screen for a few seconds (paints are only real on screen).
    /// </summary>
    static class PaintTest
    {
        /// <summary>Counts the paint messages one window gets, and when.</summary>
        sealed class Spy : NativeWindow
        {
            public readonly Control Control;
            public int Paints, Erases;
            public readonly List<double> PaintTimes = new List<double>();
            public Action Painted;
            public static readonly Stopwatch Clock = Stopwatch.StartNew();
            public Spy(Control c) { Control = c; AssignHandle(c.Handle); c.HandleDestroyed += (s, e) => ReleaseHandle(); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x000F) { Paints++; PaintTimes.Add(Clock.Elapsed.TotalMilliseconds); Painted?.Invoke(); }
                else if (m.Msg == 0x0014) Erases++;
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);
        const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100;

        static readonly MethodInfo GetStyle = typeof(Control).GetMethod("GetStyle", BindingFlags.NonPublic | BindingFlags.Instance);
        static bool Has(Control c, ControlStyles s) => (bool)GetStyle.Invoke(c, new object[] { s });

        static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in All(c)) yield return d; }
        }

        /// <summary>A box the user can see blink: painted by WinForms, in two passes (no back buffer).</summary>
        static bool TwoPass(Control c) =>
            !(c is TextBoxBase || c is ComboBox || c is UpDownBase || c is ListView || c is WebBrowserBase) &&
            !(Has(c, ControlStyles.OptimizedDoubleBuffer) || Has(c, ControlStyles.DoubleBuffer)) &&
            !(c is Form);

        /// <summary>--off-screen: measure without showing the window (Windows may skip some paints there).</summary>
        public static bool OffScreen;

        public static int Run(string outFile)
        {
            var log = new StringBuilder();
            MainForm.Preview = true;
            Settings.ReadOnly = true;
            Application.EnableVisualStyles();
            var wa = Screen.PrimaryScreen.WorkingArea;
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = OffScreen ? new Point(-30000, -30000) : new Point(wa.Left, wa.Top), ShowInTaskbar = false };
            var spies = new List<Spy>();
            void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }

            form.Show();
            Pump(9000);   // startup scan and checks
            var tabs = All(form).OfType<PageHost>().First();
            // visit every tab once so every box has its window, then watch them all
            for (int i = 0; i < tabs.Count; i++) { tabs.SelectedIndex = i; Pump(i == MainForm.TabPlay ? 4000 : 500); }
            tabs.SelectedIndex = 0;
            Pump(500);
            foreach (var c in All(form).Where(c => c.IsHandleCreated)) spies.Add(new Spy(c));

            var twoPass = All(form).Where(TwoPass).ToList();
            log.AppendLine($"boxes: {All(form).Count()} controls, {twoPass.Count} paint in two passes (no back buffer):");
            foreach (var g in twoPass.GroupBy(c => c.GetType().Name).OrderByDescending(g => g.Count()))
                log.AppendLine($"   {g.Count(),4}  {g.Key}");

            // each tab, from the one before it: paint passes that reach the screen, and the time to draw it all
            int totalPasses = 0;
            double totalMs = 0;
            foreach (var i in new[] { MainForm.TabMods, MainForm.TabPlay, MainForm.TabPlay, MainForm.TabSetup, MainForm.TabPlay })
            {
                foreach (var s in spies) { s.Paints = s.Erases = 0; }
                var sw = Stopwatch.StartNew();
                tabs.SelectedIndex = i;
                RedrawWindow(form.Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN);
                Application.DoEvents();
                sw.Stop();
                // a painted box is one pass; one that also erased without a back buffer is two
                int passes = spies.Sum(s => s.Paints + (TwoPass(s.Control) ? s.Erases : 0));
                int windows = spies.Count(s => s.Paints > 0);
                totalPasses += passes;
                totalMs += sw.Elapsed.TotalMilliseconds;
                log.AppendLine($"tab {i + 1}: {windows} boxes repainted in {passes} passes, {sw.Elapsed.TotalMilliseconds:0} ms");
                Pump(300);
            }
            log.AppendLine($"tab switches: {totalPasses} passes, {totalMs:0} ms in all");

            // a whole-window repaint, the way a restore or an uncovered window does it
            foreach (var s in spies) { s.Paints = s.Erases = 0; }
            var full = Stopwatch.StartNew();
            RedrawWindow(form.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            full.Stop();
            log.AppendLine($"whole window: {spies.Sum(s => s.Paints + (TwoPass(s.Control) ? s.Erases : 0))} passes, {full.Elapsed.TotalMilliseconds:0} ms");

            // the progress bar's marquee for 3 seconds: frames, gaps, and how far the block moves between frames
            var meter = All(form).OfType<Meter>().First();
            var spy = spies.First(s => s.Control == meter);
            var phase = typeof(Meter).GetField("phase", BindingFlags.NonPublic | BindingFlags.Instance);
            var xs = new List<int>();
            spy.Painted = () => xs.Add((int)phase.GetValue(meter));
            spy.PaintTimes.Clear();
            meter.Marquee = true;
            Pump(3000);
            meter.Marquee = false;
            spy.Painted = null;
            var t = spy.PaintTimes;
            var gaps = t.Zip(t.Skip(1), (a, b) => b - a).ToList();
            var jumps = xs.Zip(xs.Skip(1), (a, b) => Math.Abs(b - a)).Where(j => j < meter.Width).ToList();   // not the wrap-around
            log.AppendLine($"marquee: {t.Count / 3.0:0} frames/s, frame gap {(gaps.Count > 0 ? gaps.Average() : 0):0.0} ms avg / {(gaps.Count > 0 ? gaps.Max() : 0):0} ms max, " +
                           $"block moves {(jumps.Count > 0 ? jumps.Average() : 0):0.0} px avg / {(jumps.Count > 0 ? jumps.Max() : 0)} px max per frame");

            form.Close();
            File.WriteAllText(outFile, log.ToString());
            return 0;
        }
    }
}
