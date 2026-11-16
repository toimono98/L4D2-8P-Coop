using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --skins-scroll &lt;report&gt; [shots dir]: a game folder with a skin for every target (the longest list the picker
    /// can show), in windows of several sizes and scales: scrolled to the bottom, the last card must be in view, also
    /// after the list is rebuilt while scrolled down (a pick, Look again, coming back to the tab).
    /// </summary>
    static class SkinsScrollTest
    {
        public static int Run(string report, string shots)
        {
            var log = new StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            var work = Path.Combine(Path.GetTempPath(), "l4d2-8p-skins-scroll");
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                var game = Path.Combine(work, "left4dead2");
                var addons = Path.Combine(game, "addons");
                Directory.CreateDirectory(addons);
                int made = 0;
                foreach (var t in Skins.Targets)
                {
                    var model = Plain(t.Models.ToString());
                    if (model == null || !t.Models.IsMatch(model)) { log.AppendLine("     (no plain model path for " + t.Id + ")"); continue; }
                    WriteVpk(Path.Combine(addons, "skin_" + t.Id + ".vpk"), new Dictionary<string, byte[]>
                    {
                        ["addoninfo.txt"] = Encoding.ASCII.GetBytes("\"AddonInfo\"\n{\n\t\"addontitle\" \"" + t.Name + " Test Skin\"\n}\n"),
                        [model + ".mdl"] = Encoding.ASCII.GetBytes("mdl " + t.Id),
                    });
                    made++;
                }
                // and a long column of choices: 14 skins for Nick
                for (int n = 1; n <= 14; n++)
                    WriteVpk(Path.Combine(addons, "nick_" + n + ".vpk"), new Dictionary<string, byte[]>
                    {
                        ["addoninfo.txt"] = Encoding.ASCII.GetBytes("\"AddonInfo\"\n{\n\t\"addontitle\" \"Nick Skin Number " + n + "\"\n}\n"),
                        ["models/survivors/survivor_gambler.mdl"] = Encoding.ASCII.GetBytes("mdl nick " + n),
                    });
                File.WriteAllText(Path.Combine(game, "addonlist.txt"), "\"AddonList\"\n{\n}\n");
                Check($"{made} test skins, one for each of {Skins.Targets.Count} targets", made >= Skins.Targets.Count - 3);

                SkinPrefs.FilePath = Path.Combine(work, "skins.json");
                SkinPrefs.ReadOnly = true;
                Application.EnableVisualStyles();
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }

                foreach (var (scale, w, h) in new[] { (1.0f, 1920, 1032), (1.0f, 1280, 760), (1.5f, 1920, 1032), (1.25f, 1600, 900) })
                {
                    Ui.Test = scale / Ui.System;   // the scale the window is drawn at, whatever this PC's own
                    using (var f = new SkinsForm(() => game) { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), ShowInTaskbar = false })
                    {
                        f.MinimumSize = Size.Empty;
                        f.Size = new Size(w, h);
                        f.Show();
                        Pump(2500);
                        var list = (ScrollableControl)f.Panel.TargetList;
                        string name = $"{scale:0.00} {w}x{h}";
                        bool Bottom(string when)
                        {
                            list.AutoScrollPosition = new Point(0, 1 << 20);
                            Pump(300);
                            var last = list.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Bottom).Last();
                            bool seen = last.Bottom <= list.ClientSize.Height;
                            Check($"{name}, {when}: the last card ({(last as Control).Text}{last.GetType().Name}) ends at {last.Bottom} in a list {list.ClientSize.Height} high", seen);
                            if (shots != null) { Directory.CreateDirectory(shots); using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size)); bmp.Save(Path.Combine(shots, $"scroll-{scale:0.00}-{w}x{h}-{when.Replace(' ', '-')}.png")); } }
                            return seen;
                        }
                        Bottom("first shown");
                        // rebuilt while scrolled down: Look again, or coming back to the tab after a change elsewhere
                        f.Panel.Reload().GetAwaiter();
                        Pump(2500);
                        Bottom("after a reload");
                        f.Panel.SelectTarget("common"); Pump(300);
                        Bottom("after a pick");
                        // the choices column, scrolled to its end
                        f.Panel.SelectTarget("nick"); Pump(400);
                        var opts = (ScrollableControl)f.Panel.OptionsList;
                        opts.AutoScrollPosition = new Point(0, 1 << 20); Pump(300);
                        var lastOpt = opts.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Bottom).Last();
                        Check($"{name}: the last of {opts.Controls.Count} choices for Nick ends at {lastOpt.Bottom} in a column {opts.ClientSize.Height} high", opts.Controls.Count >= 15 && lastOpt.Bottom <= opts.ClientSize.Height);
                        // the Packs & collections view: a card per skin, each a whole row
                        f.Panel.ShowPacks(null); Pump(600);
                        Bottom("packs view");
                        f.Close();
                    }
                }
            }
            catch (Exception ex) { Check("no exception: " + ex, false); }
            File.WriteAllText(report, log.ToString());
            return ok ? 0 : 1;
        }

        /// <summary>One model path a target's pattern accepts: optional parts left out, the first of each choice.</summary>
        static string Plain(string pattern)
        {
            var p = pattern.TrimStart('^').TrimEnd('$');
            if (p.StartsWith("(") && p.EndsWith(")")) p = p.Substring(1, p.Length - 2);
            p = p.Split('|')[0];
            p = Regex.Replace(p, @"\([^()]*\)\?", "");
            p = Regex.Replace(p, @"\[(.)[^\]]*\]", "$1");
            p = Regex.Replace(p, @".\?", "");
            p = p.Replace("(", "").Replace(")", "").Replace("\\", "");
            return Regex.IsMatch(p, @"[*+{}\[\]?]") ? null : p;
        }

        /// <summary>A single-file VPK v1, as add-ons are.</summary>
        static void WriteVpk(string path, Dictionary<string, byte[]> files)
        {
            var tree = new MemoryStream();
            var blob = new MemoryStream();
            var tw = new BinaryWriter(tree);
            void Z(string s) { tw.Write(Encoding.ASCII.GetBytes(s)); tw.Write((byte)0); }
            foreach (var byExt in files.GroupBy(kv => Path.GetExtension(kv.Key).TrimStart('.')).OrderBy(g => g.Key))
            {
                Z(byExt.Key.Length == 0 ? " " : byExt.Key);
                foreach (var byDir in byExt.GroupBy(kv => Path.GetDirectoryName(kv.Key).Replace('\\', '/')).OrderBy(g => g.Key))
                {
                    Z(byDir.Key.Length == 0 ? " " : byDir.Key);
                    foreach (var kv in byDir)
                    {
                        Z(Path.GetFileNameWithoutExtension(kv.Key));
                        tw.Write(Crc32(kv.Value)); tw.Write((ushort)0); tw.Write((ushort)0x7FFF);
                        tw.Write((uint)blob.Length); tw.Write((uint)kv.Value.Length); tw.Write((ushort)0xFFFF);
                        blob.Write(kv.Value, 0, kv.Value.Length);
                    }
                    tw.Write((byte)0);
                }
                tw.Write((byte)0);
            }
            tw.Write((byte)0);
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(0x55AA1234u); bw.Write(1u); bw.Write((uint)tree.Length);
                tree.WriteTo(fs);
                blob.WriteTo(fs);
            }
        }

        static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
            return ~crc;
        }
    }
}
