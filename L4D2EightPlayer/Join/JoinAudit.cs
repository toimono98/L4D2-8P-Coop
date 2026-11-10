using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.Join
{
    public sealed partial class JoinForm
    {
        /// <summary>Every state the layout audit visits. Join is never pressed (it would start Steam).</summary>
        internal List<(string name, Func<Task> act)> AuditSteps(string mockAddress) => new List<(string, Func<Task>)>
        {
            ("empty", async () => { txtTarget.Text = ""; await Task.CompletedTask; }),
            ("not-an-address", async () => { txtTarget.Text = "hello there!!"; await Check(); }),
            ("no-answer", async () => { txtTarget.Text = "127.0.0.1:27098"; await Check(); }),
            ("no-answer-lan-address", async () => { txtTarget.Text = "192.168.0.250:27098"; await Check(); }),
            ("found-official-map", async () => { txtTarget.Text = "steam://connect/" + mockAddress; await Check(); }),
            ("found-custom-map-full-password", async () => { await Check(); }),
            // the server's mods, as a host's server lists them: a required campaign you're missing, a script you have, cosmetics
            ("server-mods", async () =>
            {
                serverMods = new List<ServerMods.Item>
                {
                    new ServerMods.Item { Id = "121086524", Required = true, Title = "Urban Flight", Size = 200L << 20, Url = "x" },
                    new ServerMods.Item { Id = "213445426", Required = true, Title = "Manacat Common Library", Size = 199 << 10, Url = "x", Have = true },
                    new ServerMods.Item { Id = "3787719478", Title = "[Chainsaw Man]: Power (Replace Rochelle)", Size = 8L << 20, Url = "x" },
                    new ServerMods.Item { Id = "3815316510", Title = "Daffy Duck [Nick]", Size = 12L << 20, Url = "x" },
                    new ServerMods.Item { Id = "3807277805", Title = "MW19: Holger-26 (M16)", Size = 45L << 20, Url = "x", Have = true },
                };
                ShowMods();
                cosmeticPicks["3815316510"].Checked = true;
                await Task.CompletedTask;
            }),
            // a host with many mods: the card holds every row and the page scrolls to the bottom of it
            ("server-mods-many", async () =>
            {
                serverMods = Enumerable.Range(0, 28).Select(i => new ServerMods.Item
                {
                    Id = (3700000000L + i).ToString(), Required = i < 5, Have = i % 3 == 0, Url = "x", Size = (i + 1) * 3L << 20,
                    Title = i < 5 ? $"Required campaign or script {i + 1}" : $"Cosmetic skin number {i + 1} with a fairly long Workshop title",
                }).ToList();
                ShowMods();
                await Task.Delay(200);
                body.AutoScrollPosition = new Point(0, body.DisplayRectangle.Height);
            }),
            ("lan-search", async () => { body.AutoScrollPosition = Point.Empty; serverMods.Clear(); ShowMods(); await FindLan(); }),
        };
    }

    /// <summary>
    /// Layout audit for the join app. It runs inside Application.Run, exactly like the real app, so
    /// awaits resume on the UI thread; it renders off-screen and never presses Join.
    /// </summary>
    static class JoinAudit
    {
        public static void Run(string dir, float scale, string mock, string outFile)
        {
            Ui.Test = scale;
            Audit.On = true;
            JoinSettings.ReadOnly = true;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            void Flush() { lock (sb) File.WriteAllText(outFile, sb.ToString()); }

            // An off-screen error dialog would block forever: write the exception and stop instead.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { lock (sb) sb.AppendLine("EXCEPTION " + e.Exception); Flush(); Environment.Exit(3); };
            Application.EnableVisualStyles();

            var f = new JoinForm("") { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), ShowInTaskbar = false };

            // Watchdog: a step that never finishes gets reported instead of hanging the run.
            var stepStarted = DateTime.Now;
            string current = "startup";
            new Thread(() =>
            {
                while ((DateTime.Now - stepStarted).TotalSeconds < 30) Thread.Sleep(500);
                lock (sb) sb.AppendLine($"STALLED in {current}");
                Flush();
                Environment.Exit(4);
            }) { IsBackground = true }.Start();

            f.Shown += async (s, e) =>
            {
                await Task.Delay(1500);
                lock (sb) sb.AppendLine($"audit at scale {Ui.Factor:0.00}, window {f.Size}");
                int total = 0;
                foreach (var (name, act) in f.AuditSteps(mock))
                {
                    current = name;
                    stepStarted = DateTime.Now;
                    await act();
                    await Task.Delay(400);
                    lock (Audit.Issues) Audit.Issues.Clear();
                    f.Refresh();
                    Audit.Tree(f);
                    var issues = Audit.Issues.ToList();
                    total += issues.Count(i => !i.StartsWith("LIST CELL"));
                    lock (sb)
                    {
                        sb.AppendLine($"== {name}: {issues.Count} issue(s)");
                        foreach (var i in issues) sb.AppendLine("   " + i);
                    }
                    Flush();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(Path.Combine(dir, $"{scale:0.00}-{name}.png"));
                    }
                }
                lock (sb) sb.AppendLine($"TOTAL {total} non-list issue(s)");
                Flush();
                stepStarted = DateTime.MaxValue.AddDays(-1);
                f.Close();
            };
            Application.Run(f);
        }
    }
}
