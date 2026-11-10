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
        ///   --silent --path host|join [--dir folder] [--no-start-menu] [--no-desktop] [--hud] [--no-register]
        ///            [--updates auto|manual] [--out log]
        ///                             (the path is the side the app opens on; --no-register: a test install, no
        ///                             Installed apps entry and the start side left alone; --updates: how updates install)
        ///   --silent --update --path host|join [--wait pid] [--restart] [--out log]
        ///                             what the app runs to update itself: waits for it (pid) to exit, updates the
        ///                             install in place with the shortcuts it has, then with --restart opens it again
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
                if (args.Contains("--update")) return Update(choice, Arg("--wait"), args.Contains("--restart"), Arg("--out"));
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
                        AutoUpdate = Arg("--updates") == "auto" ? true : Arg("--updates") == "manual" ? (bool?)false : null,
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

        /// <summary>
        /// The app updating itself: wait for it to close (it locks its exe), install over it with its folder, shortcuts
        /// and side, and with restart open it again: the new version, or the old one if the update failed.
        /// </summary>
        static int Update(SetupEngine.Choice choice, string waitPid, bool restart, string logFile)
        {
            var log = new StringBuilder();
            log.AppendLine($"{DateTime.Now:s} updating to {AppInfo.Version}");
            var o = SetupEngine.ForUpdate(choice);
            int code = 0;
            try
            {
                if (int.TryParse(waitPid, out var pid))
                    try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) p.WaitForExit(120000); } catch (ArgumentException) { /* already gone */ }
                // a moment for Windows to let go of the exe, then the same check the install makes
                for (int i = 0; i < 30 && SetupEngine.RunningFrom(o.Folder) != null; i++) Thread.Sleep(1000);
                SetupEngine.Install(o, m => log.AppendLine(m));
            }
            catch (Exception ex) { log.AppendLine("ERROR " + ex.Message); code = 2; }
            WriteLog(logFile, log);
            if (restart)
            {
                var exe = Path.Combine(o.Folder, SetupEngine.Exe);
                try { if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--" + choice.Id + " --no-tour") { WorkingDirectory = o.Folder }); }
                catch { }
            }
            return code;
        }

        static void WriteLog(string file, StringBuilder log)
        {
            if (file == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            File.WriteAllText(file, log.ToString());
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
                f.ShowDone(new SetupEngine.Options { Choice = SetupEngine.ById(path), Folder = SetupEngine.DefaultFolder, HudAddon = path == "join", StartMenu = true, AutoUpdate = true });
                Save(f, "done-" + path);
                f.Close();
            }
        }
    }
}
