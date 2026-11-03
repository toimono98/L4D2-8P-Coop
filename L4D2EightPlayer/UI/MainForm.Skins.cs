using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>The Skins tab (the shared SkinsPanel, for your game) and putting the picks back before each launch.</summary>
    public sealed partial class MainForm
    {
        SkinsPanel skins;
        bool skinsLoaded;

        Panel BuildSkinsTab()
        {
            var page = Page(840, 600); // one column of cards beside the choices still fits the narrowest window
            skins = new SkinsPanel(() => settings.ClientGameDir, () => manifest,
                path => findings.FirstOrDefault(f => string.Equals(f.Location, path, StringComparison.OrdinalIgnoreCase)));
            // turning add-ons on and off changes what the Mods tab reports
            skins.Changed += async (s, e) => { Info("Skins: add-on list updated. It takes effect the next time Left 4 Dead 2 starts."); await RunScan(auto: true, quiet: true); };
            page.Controls.Add(skins);
            return page;
        }

        /// <summary>
        /// Before the game starts: the remembered skin picks back in its add-on list (the game's Add-ons menu or a
        /// new Workshop subscription may have changed it). A new skin for a picked target is asked about.
        /// </summary>
        void ReapplySkins()
        {
            if (Preview || settings.ClientGameDir == null) return;
            try
            {
                var lines = Skins.Reapply(settings.ClientGameDir, manifest, SkinPrefs.Load(), (mod, target) =>
                    MessageBox.Show(this, $"A new skin for {target.Name} is installed: {mod.Title}.\n\nUse it instead of your pick? No keeps your pick and turns the new one off (you can change it on the Skins tab).",
                        Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
                foreach (var line in lines) Info("Skins: " + line);
                if (lines.Count > 0 && skinsLoaded) _ = skins.Reload();
            }
            catch (Exception ex) { Warn("Skins: couldn't put your picks back (" + ex.Message + ")."); }
        }

        /// <summary>
        /// Before the game starts: overlapping skins with nothing picked are asked about. False when the user went to
        /// choose on the Skins tab (or cancelled), so the launch stops there.
        /// </summary>
        async Task<bool> SkinsSettled()
        {
            if (Preview || settings.ClientGameDir == null) return true;
            var choose = OverlapDialog.Check(this, settings.ClientGameDir, manifest);
            if (choose == null) return true;
            if (choose != "")
            {
                skins.SelectTarget(choose);   // kept while the list is read
                bool first = !skinsLoaded;
                tabs.SelectedIndex = TabSkins;
                if (!first) await skins.Reload();
                Info("Skins: pick one skin for each highlighted target, then launch again.");
            }
            return false;
        }
    }
}
