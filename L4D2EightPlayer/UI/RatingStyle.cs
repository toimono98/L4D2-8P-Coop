using System.Drawing;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>The color key's colors, and the key itself (on the Mods tab and the skin picker).</summary>
    static class RatingStyle
    {
        public static Color Color(Rating r)
        {
            switch (r)
            {
                case Rating.Safe: return Theme.Hex("#5b9be8");
                case Rating.Stable: return Theme.Hex("#5cb85c");
                case Rating.Moderate: return Theme.Hex("#e8c93a");
                case Rating.Unstable: return Theme.Hex("#ef7d22");
                default: return Theme.Hex("#e5483b");
            }
        }

        /// <summary>Text on the color: dark on the light ones, light on red.</summary>
        public static Color Text(Rating r) => r == Rating.Unsafe ? Theme.BoneText : Theme.Ground;

        public static string Dot(Rating r) => "● " + Ratings.Name(r).ToUpperInvariant();

        /// <summary>"KEY ● SAFE ● STABLE ...", each with what it means on hover.</summary>
        public static FlowLayoutPanel Key(Padding margin)
        {
            var key = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = margin, BackColor = System.Drawing.Color.Transparent };
            var tips = new ToolTip();
            var head = new Label { Text = "RATING KEY", AutoSize = true, Font = Theme.BodyBold(8.5f), ForeColor = Theme.Muted, Margin = P(0, 1, 12, 0) };
            tips.SetToolTip(head, "Conflicts: other add-ons that are on and replace the same files with different copies.\n" +
                                  "Requirements: what it changes beyond looks (a campaign, a game mode, gameplay scripts, weapon stats, dialogue, animations).\n" +
                                  "Overlaps where both have the same copy of a file are harmless and don't count.");
            key.Controls.Add(head);
            foreach (Rating r in System.Enum.GetValues(typeof(Rating)))
            {
                var l = new Label { Text = Dot(r), AutoSize = true, Font = Theme.BodyBold(9f), ForeColor = Color(r), Margin = P(0, 0, 14, 0), UseMnemonic = false };
                tips.SetToolTip(l, Ratings.Name(r) + ": " + Ratings.Meaning(r));
                key.Controls.Add(l);
            }
            return key;
        }
    }
}
