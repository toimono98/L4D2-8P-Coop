using System;
using System.IO;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI;

namespace L4D2EightPlayer.Join
{
    /// <summary>The Join side's skins: the shared picker in a window, and the picks put back before Join game starts the game.</summary>
    public sealed partial class JoinForm
    {
        string ClientGameDir => string.IsNullOrEmpty(settings.GamePath) ? null : Path.Combine(settings.GamePath, "left4dead2");

        void OpenSkins()
        {
            using (var f = new SkinsForm(() => ClientGameDir)) f.ShowDialog(TopLevelControl as Form ?? this);
        }

        /// <summary>Before the game starts (not when it's already running: then nothing can change).</summary>
        void ReapplySkins()
        {
            if (Preview || ClientGameDir == null) return;
            try
            {
                Skins.Reapply(ClientGameDir, Manifest.BuiltIn(), SkinPrefs.Load(), (mod, target) =>
                    MessageBox.Show(this, $"A new skin for {target.Name} is installed: {mod.Title}.\n\nUse it instead of your pick? No keeps your pick and turns the new one off (Skins… at the bottom changes it).",
                        Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
            }
            catch { /* the game starts with the add-ons as they are */ }
        }

        /// <summary>
        /// Before Join game starts the game: overlapping skins with nothing picked are asked about, and the Skins
        /// window opens on the first one. False when the user cancelled.
        /// </summary>
        bool SkinsSettled()
        {
            if (Preview || ClientGameDir == null) return true;
            string choose;
            while ((choose = OverlapDialog.Check(this, ClientGameDir, Manifest.BuiltIn())) != null)
            {
                if (choose == "") return false;
                using (var f = new SkinsForm(() => ClientGameDir))
                {
                    f.Panel.SelectTarget(choose);
                    f.ShowDialog(TopLevelControl as Form ?? this);
                }
            }
            return true;
        }
    }
}
