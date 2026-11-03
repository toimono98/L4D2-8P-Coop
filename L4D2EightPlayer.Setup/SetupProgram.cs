using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.Setup
{
    static class SetupProgram
    {
        /// <summary>
        ///   (no arguments)            the setup window
        ///   --uninstall [--silent]    what Windows' Installed apps runs
        ///   --silent --path host|join [--dir folder] [--no-start-menu] [--no-desktop] [--hud] [--no-register] [--out log]
        ///                             (the path is the side the app opens on; --no-register: a test install, no
        ///                             Installed apps entry and the start side left alone)
        ///   --shot folder             renders every page to PNGs (layout check)
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            bool silent = args.Contains("--silent");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Contains("--uninstall"))
            {
                if (!silent && MessageBox.Show("Remove " + SetupEngine.Product + " from this PC?\n\nYour settings, the dedicated server and your game stay as they are.",
                        "Uninstall " + SetupEngine.Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                var log = new StringBuilder();
                try
                {
                    SetupEngine.Uninstall(m => log.AppendLine(m));
                    if (!silent) MessageBox.Show(SetupEngine.Product + " was removed.", "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    WriteLog(Arg("--out"), log);
                    return 0;
                }
                catch (Exception ex)
                {
                    log.AppendLine("ERROR " + ex.Message);
                    WriteLog(Arg("--out"), log);
                    if (!silent) MessageBox.Show(ex.Message, "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }
            }

            if (silent)
            {
                var log = new StringBuilder();
                var choice = SetupEngine.ById(Arg("--path"));
                if (choice == null) { log.AppendLine("ERROR --path host or --path join"); WriteLog(Arg("--out"), log); return 1; }
                try
                {
                    SetupEngine.Install(new SetupEngine.Options
                    {
                        Choice = choice,
                        Folder = Arg("--dir") ?? SetupEngine.DefaultFolder,
                        StartMenu = !args.Contains("--no-start-menu"),
                        Desktop = !args.Contains("--no-desktop"),
                        HudAddon = args.Contains("--hud"),
                        Register = !args.Contains("--no-register"),
                    }, m => log.AppendLine(m));
                    WriteLog(Arg("--out"), log);
                    return 0;
                }
                catch (Exception ex) { log.AppendLine("ERROR " + ex.Message); WriteLog(Arg("--out"), log); return 2; }
            }

            if (Arg("--shot") != null)
            {
                Shot(Arg("--shot"));
                return 0;
            }

            Application.Run(new SetupForm(Arg("--path")));
            return 0;
        }

        static void WriteLog(string file, StringBuilder log)
        {
            if (file != null) File.WriteAllText(file, log.ToString());
        }

        /// <summary>The choose page, both option pages: off-screen, saved as PNGs.</summary>
        static void Shot(string dir)
        {
            Directory.CreateDirectory(dir);
            void Save(Form f, string name)
            {
                var until = DateTime.Now.AddMilliseconds(600);
                while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); }
                using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(Path.Combine(dir, name + ".png"));
                }
            }
            foreach (var path in new[] { "host", "join" })
            {
                var f = new SetupForm(path) { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-30000, -30000), ShowInTaskbar = false };
                f.Show();
                Save(f, "choose-" + path);
                f.ShowOptions();
                Save(f, "options-" + path);
                f.ShowDone(new SetupEngine.Options { Choice = SetupEngine.ById(path), Folder = SetupEngine.DefaultFolder, HudAddon = path == "join", StartMenu = true });
                Save(f, "done-" + path);
                f.Close();
            }
        }
    }
}
