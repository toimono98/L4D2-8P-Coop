using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using L4D2EightPlayer.Core;
using Microsoft.Win32;

namespace L4D2EightPlayer.Setup
{
    /// <summary>
    /// Installs for the current user only (no admin): the chosen app into a folder, Start menu and Desktop
    /// shortcuts, and an entry in Windows' Installed apps that runs "L4D2-8P-Setup.exe --uninstall".
    /// Settings (%LOCALAPPDATA%\L4D2-8P-Manager), the dedicated server and the game are never touched,
    /// apart from the optional HUD add-on in the game's addons folder.
    /// </summary>
    static class SetupEngine
    {
        public const string Product = "L4D2 8-Player Co-op";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\L4D2-8P-Coop";
        public const string SetupExe = "L4D2-8P-Setup.exe";
        const string UninstallShortcut = "Uninstall " + Product;

        public sealed class App
        {
            public string Id, Exe, Shortcut, Process;
            public string[] Extras;   // other files beside it
        }

        public static readonly App Host = new App { Id = "host", Exe = "L4D2-8P-Manager.exe", Shortcut = "L4D2 8-Player Manager", Process = "L4D2-8P-Manager", Extras = new[] { "README.md" } };
        public static readonly App Join = new App { Id = "join", Exe = "L4D2-8P-Join.exe", Shortcut = "L4D2 8-Player Join", Process = "L4D2-8P-Join", Extras = new[] { "READ ME FIRST.txt" } };
        public static App ById(string id) => id == "join" ? Join : id == "host" ? Host : null;

        public sealed class Options
        {
            public App App;
            public string Folder;
            public bool StartMenu = true, Desktop = true, HudAddon, Register = true;
        }

        public static string DefaultFolder =>
            Existing()?.folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Product);

        static string StartMenuFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product);
        static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        /// <summary>The current install, if any: its folder and which apps it has.</summary>
        public static (string folder, List<string> apps)? Existing()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var folder = k?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                var apps = ((k.GetValue("Apps") as string) ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                return (folder, apps);
            }
        }

        /// <summary>An app of this install that is running and would lock its exe.</summary>
        public static App RunningFrom(string folder)
        {
            foreach (var a in new[] { Host, Join })
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(a.Process))
                {
                    try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return a; }
                    catch { }
                }
            return null;
        }

        public static void Install(Options o, Action<string> say)
        {
            var folder = Path.GetFullPath(o.Folder);
            Directory.CreateDirectory(folder);
            var running = RunningFrom(folder);
            if (running != null) throw new InvalidOperationException($"{running.Shortcut} is open. Close it, then try again.");

            say("Copying " + o.App.Exe);
            Extract("app/" + o.App.Exe, Path.Combine(folder, o.App.Exe));
            foreach (var x in o.App.Extras) Extract("app/" + x, Path.Combine(folder, x));
            foreach (var lic in Resources().Where(r => r.StartsWith("app/licenses/")))
                Extract(lic, Path.Combine(folder, "licenses", lic.Substring("app/licenses/".Length)));

            // a copy of this setup stays, for uninstalling or adding the other app later
            var self = Assembly.GetExecutingAssembly().Location;
            var kept = Path.Combine(folder, SetupExe);
            if (!string.Equals(Path.GetFullPath(self), kept, StringComparison.OrdinalIgnoreCase)) File.Copy(self, kept, true);
            // a visible way out, beside the apps (the same as Windows' Installed apps)
            Shortcut(Path.Combine(folder, UninstallShortcut + ".lnk"), kept, folder, "--uninstall");

            if (o.StartMenu)
            {
                say("Adding the Start menu shortcuts");
                Directory.CreateDirectory(StartMenuFolder);
                Shortcut(Path.Combine(StartMenuFolder, o.App.Shortcut + ".lnk"), Path.Combine(folder, o.App.Exe), folder);
                Shortcut(Path.Combine(StartMenuFolder, UninstallShortcut + ".lnk"), kept, folder, "--uninstall");
            }
            if (o.Desktop)
            {
                say("Adding the Desktop shortcut");
                Shortcut(Path.Combine(Desktop, o.App.Shortcut + ".lnk"), Path.Combine(folder, o.App.Exe), folder);
            }
            if (o.HudAddon)
            {
                var game = GameLocator.FindGame();
                if (game != null)
                {
                    say("Installing the 8-Player HUD add-on into Left 4 Dead 2");
                    HudAddon.Install(game);
                }
            }
            if (o.Register)
            {
                say("Adding it to Windows' Installed apps");
                var apps = Existing()?.apps ?? new List<string>();
                if (!apps.Contains(o.App.Id)) apps.Add(o.App.Id);
                Register(folder, apps);
            }
            say("Done");
        }

        static void Register(string folder, List<string> apps)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                var names = apps.Select(ById).Where(a => a != null).Select(a => a.Shortcut.Replace("L4D2 8-Player ", "")).ToList();
                k.SetValue("DisplayName", Product + " (" + string.Join(" + ", names) + ")");
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", "L4D2 8-Player Co-op");
                k.SetValue("InstallLocation", folder);
                k.SetValue("Apps", string.Join(",", apps));
                k.SetValue("DisplayIcon", Path.Combine(folder, apps.Contains("host") ? Host.Exe : Join.Exe));
                k.SetValue("UninstallString", $"\"{Path.Combine(folder, SetupExe)}\" --uninstall");
                k.SetValue("ModifyPath", $"\"{Path.Combine(folder, SetupExe)}\"");
                k.SetValue("URLInfoAbout", "https://github.com/" + UpdateCheck.Repo);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
                k.SetValue("EstimatedSize", (int)Math.Min(kb, int.MaxValue), RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes both apps, the shortcuts and the Installed apps entry. Settings and the server stay.</summary>
        public static void Uninstall(Action<string> say)
        {
            var existing = Existing();
            var folder = existing?.folder ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var running = RunningFrom(folder);
            if (running != null) throw new InvalidOperationException($"{running.Shortcut} is open. Close it, then try again.");

            foreach (var a in new[] { Host, Join })
            {
                foreach (var lnk in new[] { Path.Combine(StartMenuFolder, a.Shortcut + ".lnk"), Path.Combine(Desktop, a.Shortcut + ".lnk") })
                    if (File.Exists(lnk)) { File.Delete(lnk); say("Removed " + lnk); }
                foreach (var f in new[] { a.Exe }.Concat(a.Extras))
                {
                    var p = Path.Combine(folder, f);
                    if (File.Exists(p)) { File.Delete(p); say("Removed " + p); }
                }
            }
            foreach (var lnk in new[] { Path.Combine(folder, UninstallShortcut + ".lnk"), Path.Combine(StartMenuFolder, UninstallShortcut + ".lnk") })
                if (File.Exists(lnk)) { File.Delete(lnk); say("Removed " + lnk); }
            var lic = Path.Combine(folder, "licenses");
            if (Directory.Exists(lic)) Directory.Delete(lic, true);
            if (Directory.Exists(StartMenuFolder) && !Directory.EnumerateFileSystemEntries(StartMenuFolder).Any()) Directory.Delete(StartMenuFolder);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            say("Removed the Installed apps entry");

            // this exe may be the copy inside the folder: delete it (and the folder, if empty) once it has exited
            var kept = Path.Combine(folder, SetupExe);
            if (File.Exists(kept))
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{kept}\" & rmdir \"{folder}\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            else if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }

        static IEnumerable<string> Resources() => Assembly.GetExecutingAssembly().GetManifestResourceNames();

        static void Extract(string resource, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) throw new FileNotFoundException("This setup is missing " + resource + ". Download it again.");
                var tmp = dest + ".new";
                using (var f = File.Create(tmp)) s.CopyTo(f);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
            }
        }

        /// <summary>A .lnk through Windows' own shell object (no extra libraries).</summary>
        static void Shortcut(string lnk, string target, string workDir, string arguments = null)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = target;
                if (arguments != null) sc.Arguments = arguments;
                sc.WorkingDirectory = workDir;
                sc.IconLocation = target + ",0";
                sc.Save();
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
    }
}
