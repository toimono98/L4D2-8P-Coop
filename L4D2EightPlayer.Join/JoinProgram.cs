using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.Join
{
    static class JoinProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Headless check for development: L4D2-8P-Join.exe --selftest <address> <outfile>
            if (args.Length >= 3 && args[0] == "--selftest")
            {
                var t = JoinTarget.Parse(args[1]);
                var info = t == null ? null : ServerQuery.Query(t.Host, t.Port).GetAwaiter().GetResult();
                File.WriteAllText(args[2], t == null ? "parse failed" :
                    $"target {t.Address} pw='{t.Password}' steam={t.SteamUrl} workshop={t.WorkshopId ?? "-"}\n" +
                    (info == null ? "no answer" : $"{info.Name} | {info.Map} | {info.Players}/{info.MaxPlayers} bots {info.Bots} | v{info.Version} | pw {info.Password} | {info.PingMs}ms") +
                    "\nplayers: " + string.Join(", ", ServerQuery.Players(t.Host, t.Port).GetAwaiter().GetResult() ?? new System.Collections.Generic.List<string> { "(no answer)" }));
                return;
            }

            // Layout audit: L4D2-8P-Join.exe --audit <dir> <scale> <mock host:port> <report>
            if (args.Length >= 5 && args[0] == "--audit")
            {
                JoinAudit.Run(args[1], float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3], args[4]);
                return;
            }

            if (args.Length >= 2 && args[0] == "--shot")
            {
                Application.EnableVisualStyles();
                var f = new JoinForm("steam://connect/127.0.0.1:27099") { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
                f.Show();
                var until = DateTime.Now.AddSeconds(6);
                while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[1]);
                }
                f.Close();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new JoinForm(string.Join(" ", args)));
        }
    }
}
