using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.Join
{
    /// <summary>
    /// The Join side keeps an eye on your add-ons too: when one is installed (a Workshop subscription, a server's mod
    /// downloaded) they're checked again, and add-ons that are on and replace the same files with different copies
    /// show as a link in the footer that opens Disable conflicting. Harmless overlaps (the same copy in both) don't count.
    /// </summary>
    public sealed partial class JoinForm
    {
        LinkLabel lnkConflicts;
        FileSystemWatcher addonWatch;
        readonly Timer addonTimer = new Timer { Interval = 3000 };
        bool checkingConflicts;

        Control BuildConflictsLink()
        {
            lnkConflicts = new LinkLabel { AutoSize = true, Font = Theme.Body(9f), Margin = P(24, 0, 0, 0), Visible = false, LinkColor = Theme.Amber };
            lnkConflicts.LinkClicked += async (s, e) =>
            {
                int n = ConflictsForm.Run(TopLevelControl as Form ?? this, ClientGameDir, Manifest.BuiltIn());
                if (n >= 0) { await CheckConflicts(); await AddonsChanged(); }
            };
            new ToolTip().SetToolTip(lnkConflicts, "Add-ons that are on and replace the same files with different copies: the game uses only one, so you'd see part of each.\n" +
                "Click to choose which to turn off. Add-ons that share the same copy of a file are harmless and aren't counted.");
            addonTimer.Tick += async (s, e) => { addonTimer.Stop(); await CheckConflicts(); await AddonsChanged(); };
            return lnkConflicts;
        }

        /// <summary>Looks again (in the background) and shows or hides the footer link.</summary>
        async Task CheckConflicts()
        {
            var dir = ClientGameDir;
            if (Preview || Audit.On || dir == null || checkingConflicts) return;
            checkingConflicts = true;
            try
            {
                int n = await Task.Run(() => Clashes.Conflicts(Clashes.Look(dir, Manifest.BuiltIn()), SkinPrefs.Load()).Count);
                if (IsDisposed) return;
                lnkConflicts.Text = $"{n} add-on conflict{(n == 1 ? "" : "s")} · Disable conflicting";
                lnkConflicts.Visible = n > 0;
            }
            catch { lnkConflicts.Visible = false; }
            finally { checkingConflicts = false; }
            WatchAddons(dir);
        }

        /// <summary>New, changed or removed add-ons: a fresh look a few seconds later (downloads write in bursts).</summary>
        void WatchAddons(string dir)
        {
            var addons = Path.Combine(dir, "addons");
            if (addonWatch != null && string.Equals(addonWatch.Path, addons, StringComparison.OrdinalIgnoreCase)) return;
            addonWatch?.Dispose();
            addonWatch = null;
            if (!Directory.Exists(addons)) return;
            try
            {
                var w = new FileSystemWatcher(addons, "*.vpk") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => { if (!IsDisposed) BeginInvoke((Action)(() => { addonTimer.Stop(); addonTimer.Start(); })); };
                w.Created += h; w.Changed += h; w.Deleted += h;
                w.Renamed += (s, e) => h(s, e);
                // the game's own list changes when add-ons are turned on and off in its menu
                w.EnableRaisingEvents = true;
                addonWatch = w;
                FormClosed += (s, e) => { addonWatch?.Dispose(); addonTimer.Dispose(); };
            }
            catch { /* no watching: Check server and the Skins window still look again */ }
        }
    }
}
