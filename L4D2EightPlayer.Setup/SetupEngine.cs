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
    /// Installs for the current user only (no admin): the app into a folder, Start menu and Desktop
    /// shortcuts, and an entry in Windows' Installed apps that runs "L4D2-8P-Setup.exe --uninstall".
    /// Hosting or joining only picks which side the app opens on (StartMode); it's the same app.
    /// Settings (%LOCALAPPDATA%\L4D2-8P-Manager), the dedicated server and the game are never touched,
    /// apart from the optional HUD add-on in the game's addons folder.
    /// </summary>
    static class SetupEngine
    {
        public const string Product = "L4D2 8-Player Co-op";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\L4D2-8P-Coop";
        public const string SetupExe = "L4D2-8P-Setup.exe";
        const string UninstallShortcut = "Uninstall " + Product;

        public const string Exe = "L4D2-8P-Coop.exe";
        const string Process = "L4D2-8P-Coop";
        static readonly string[] Extras = { "README.md" };

        /// <summary>Before 2.0 the host and join sides were two apps; an update removes them and their shortcuts.</summary>
        static readonly string[] LegacyFiles = { "L4D2-8P-Manager.exe", "L4D2-8P-Join.exe", "READ ME FIRST.txt" };
        static readonly string[] LegacyShortcuts = { "L4D2 8-Player Manager", "L4D2 8-Player Join" };
        static readonly string[] LegacyProcesses = { "L4D2-8P-Manager", "L4D2-8P-Join" };

        /// <summary>The two ways in. Both install the same app; the path is the side it opens on.</summary>
        public sealed class Choice
        {
            public string Id, Name;
        }
        public static readonly Choice Host = new Choice { Id = StartMode.Host, Name = "hosting" };
        public static readonly Choice Join = new Choice { Id = StartMode.Join, Name = "joining" };
        public static Choice ById(string id) => id == StartMode.Join ? Join : id == StartMode.Host ? Host : null;

        public sealed class Options
        {
            public Choice Choice;
            public string Folder;
            public bool StartMenu = true, Desktop = true, HudAddon;
            /// <summary>False for test installs: no Installed apps entry, and the start mode is left alone.</summary>
            public bool Register = true;
            /// <summary>Install updates automatically (true) or by hand (false); null leaves the choice as it is.</summary>
            public bool? AutoUpdate;
        }

        /// <summary>An update of the current install: the same folder, and the shortcuts it has now (none added back).</summary>
        public static Options ForUpdate(Choice choice) => new Options
        {
            Choice = choice,
            Folder = DefaultFolder,
            StartMenu = File.Exists(Path.Combine(StartMenuFolder, Product + ".lnk")),
            Desktop = File.Exists(Path.Combine(Desktop, Product + ".lnk")),
        };

        public static string DefaultFolder =>
            Existing() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Product);

        static string StartMenuFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product);
        static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        /// <summary>The current install's folder, if any (this version's or the older two-app one).</summary>
        public static string Existing()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var folder = k?.GetValue("InstallLocation") as string;
                return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : folder;
            }
        }

        /// <summary>The app (or an older one) running from this folder, which would lock its exe.</summary>
        public static string RunningFrom(string folder)
        {
            foreach (var name in new[] { Process }.Concat(LegacyProcesses))
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return name == Process ? Product : name.Replace("L4D2-8P-", "The 8-Player "); }
                    catch { }
                }
            return null;
        }

        public static void Install(Options o, Action<string> say)
        {
            var folder = Path.GetFullPath(o.Folder);
            Directory.CreateDirectory(folder);
            var running = RunningFrom(folder);
            if (running != null) throw new InvalidOperationException($"{running} is open. Close it, then try again.");

            say("Copying " + Exe);
            Extract("app/" + Exe, Path.Combine(folder, Exe));
            foreach (var x in Extras) Extract("app/" + x, Path.Combine(folder, x));
            foreach (var lic in Resources().Where(r => r.StartsWith("app/licenses/")))
                Extract(lic, Path.Combine(folder, "licenses", lic.Substring("app/licenses/".Length)));
            RemoveLegacy(folder, say);

            // a copy of this setup stays, for uninstalling (or switching the start side) later
            var self = Assembly.GetExecutingAssembly().Location;
            var kept = Path.Combine(folder, SetupExe);
            if (!string.Equals(Path.GetFullPath(self), kept, StringComparison.OrdinalIgnoreCase)) File.Copy(self, kept, true);
            // a visible way out, beside the app (the same as Windows' Installed apps)
            Shortcut(Path.Combine(folder, UninstallShortcut + ".lnk"), kept, folder, "--uninstall");

            if (o.StartMenu)
            {
                say("Adding the Start menu shortcuts");
                Directory.CreateDirectory(StartMenuFolder);
                Shortcut(Path.Combine(StartMenuFolder, Product + ".lnk"), Path.Combine(folder, Exe), folder);
                Shortcut(Path.Combine(StartMenuFolder, UninstallShortcut + ".lnk"), kept, folder, "--uninstall");
            }
            if (o.Desktop)
            {
                say("Adding the Desktop shortcut");
                Shortcut(Path.Combine(Desktop, Product + ".lnk"), Path.Combine(folder, Exe), folder);
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
                say("Opening on " + o.Choice.Name + " from now on");
                StartMode.Save(o.Choice.Id);
                if (o.AutoUpdate.HasValue)
                {
                    say(o.AutoUpdate.Value ? "Updates install automatically" : "Updates are yours to install");
                    Updater.SetAuto(o.AutoUpdate.Value);
                }
                say("Adding it to Windows' Installed apps");
                Register(folder);
            }
            say("Done");
        }

        /// <summary>The two older apps, their notes, and their shortcuts that point into this folder (this app replaces both).</summary>
        static void RemoveLegacy(string folder, Action<string> say)
        {
            foreach (var f in LegacyFiles.Select(n => Path.Combine(folder, n)))
                if (File.Exists(f)) { File.Delete(f); say("Removed the older " + Path.GetFileName(f)); }
            foreach (var name in LegacyShortcuts)
                foreach (var lnk in new[] { Path.Combine(StartMenuFolder, name + ".lnk"), Path.Combine(Desktop, name + ".lnk") })
                    if (File.Exists(lnk) && PointsInto(lnk, folder)) { File.Delete(lnk); say("Removed the older shortcut " + lnk); }
        }

        /// <summary>Whether a .lnk's target is in the folder (so it belongs to this install, not another copy).</summary>
        static bool PointsInto(string lnk, string folder)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                string target = shell.CreateShortcut(lnk).TargetPath;
                if (string.IsNullOrEmpty(target)) return false;
                return Path.GetDirectoryName(Path.GetFullPath(target)).TrimEnd('\\').Equals(Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        static void Register(string folder)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Product);
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", "L4D2 8-Player Co-op");
                k.SetValue("InstallLocation", folder);
                k.DeleteValue("Apps", false);   // 1.9-1.10: which of the two apps were installed
                k.SetValue("DisplayIcon", Path.Combine(folder, Exe));
                k.SetValue("UninstallString", $"\"{Path.Combine(folder, SetupExe)}\" --uninstall");
                k.SetValue("ModifyPath", $"\"{Path.Combine(folder, SetupExe)}\"");
                k.SetValue("URLInfoAbout", "https://github.com/" + UpdateCheck.Repo);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
                k.SetValue("EstimatedSize", (int)Math.Min(kb, int.MaxValue), RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes the app, the shortcuts and the Installed apps entry. Settings and the server stay.</summary>
        public static void Uninstall(Action<string> say)
        {
            var folder = Existing() ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var running = RunningFrom(folder);
            if (running != null) throw new InvalidOperationException($"{running} is open. Close it, then try again.");

            foreach (var lnk in new[] { Product, UninstallShortcut }.SelectMany(n => new[] { Path.Combine(StartMenuFolder, n + ".lnk"), Path.Combine(Desktop, n + ".lnk") })
                                .Append(Path.Combine(folder, UninstallShortcut + ".lnk")))
                if (File.Exists(lnk)) { File.Delete(lnk); say("Removed " + lnk); }
            foreach (var f in new[] { Exe }.Concat(Extras).Select(n => Path.Combine(folder, n)))
                if (File.Exists(f)) { File.Delete(f); say("Removed " + f); }
            RemoveLegacy(folder, say);
            var lic = Path.Combine(folder, "licenses");
            if (Directory.Exists(lic)) Directory.Delete(lic, true);
            if (Directory.Exists(StartMenuFolder) && !Directory.EnumerateFileSystemEntries(StartMenuFolder).Any()) Directory.Delete(StartMenuFolder);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            say("Removed the Installed apps entry");

            // this exe may be the copy inside the folder: delete it (and the folder, if empty) once it has exited
            var kept = Path.Combine(folder, SetupExe);
            if (File.Exists(kept))
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo("cmd.exe",
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
