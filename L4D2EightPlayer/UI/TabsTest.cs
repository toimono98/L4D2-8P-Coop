using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --tabs-test &lt;report&gt;: the tab order (TabOrder) and the top bar showing it (NavBar.Order): stored lists made whole,
    /// clicks and arrow keys landing on the right page whatever the order. Nothing is saved.
    /// </summary>
    static class TabsTest
    {
        sealed class Bar : NavBar
        {
            public Bar(params string[] s) : base(s) { }
            public void Key(Keys k) => OnKeyDown(new KeyEventArgs(k));
            public void Click(Point p) => OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        public static int Run(string report)
        {
            var log = new StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            string Names(TabOrder.Side side, int[] o) => string.Join(", ", o.Select(i => TabOrder.Title(side, i)));
            try
            {
                var H = TabOrder.Side.Host;
                var J = TabOrder.Side.Join;
                Check("host default: " + Names(H, TabOrder.Indexes(H, null)), Names(H, TabOrder.Indexes(H, null)) == "Play, Skins, Mods, Setup, Settings");
                Check("join default: " + Names(J, TabOrder.Indexes(J, null)), Names(J, TabOrder.Indexes(J, null)) == "Join, Skins, Mods, Settings");
                var mixed = TabOrder.Indexes(H, new[] { "SETTINGS", "bogus", "mods", "settings" });
                Check("a stored list made whole (unknown and repeated names dropped, missing tabs added): " + Names(H, mixed), Names(H, mixed) == "Settings, Mods, Play, Skins, Setup");
                Check("the default is recognised", TabOrder.IsDefault(H, TabOrder.Indexes(H, null)) && !TabOrder.IsDefault(H, mixed));

                using (var bar = new Bar("Setup", "Mods", "Skins", "Play", "Settings") { Width = 1400 })
                {
                    bar.Order = TabOrder.Indexes(H, null);
                    Check("the bar takes the order", bar.Order.SequenceEqual(new[] { 3, 2, 1, 0, 4 }));
                    bar.Order = new[] { 0, 0, 1, 2, 3 };
                    Check("a broken order is refused (as added)", bar.Order.SequenceEqual(new[] { 0, 1, 2, 3, 4 }));
                    bar.Order = TabOrder.Indexes(H, null);
                    using (var bmp = new Bitmap(bar.Width, bar.Height)) bar.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));   // lays out the tabs
                    // the tabs' places along the row, found by clicking across it
                    var landed = new System.Collections.Generic.List<int>();
                    for (int x = 0; x < bar.Width; x += 4)
                    {
                        bar.Selected = -1;
                        bar.Click(new Point(x, bar.Height / 2));
                        if (bar.Selected >= 0 && (landed.Count == 0 || landed[landed.Count - 1] != bar.Selected)) landed.Add(bar.Selected);
                    }
                    Check("clicks left to right open " + Names(H, landed.ToArray()), landed.SequenceEqual(new[] { 3, 2, 1, 0, 4 }));
                    bar.Selected = 3;   // Play, first on the row
                    bar.Key(Keys.Right);
                    Check("Right from Play goes to Skins", bar.Selected == 2);
                    bar.Key(Keys.Left); bar.Key(Keys.Left);
                    Check("Left from Play wraps to Settings", bar.Selected == 4);
                }
            }
            catch (Exception ex) { Check("no exception: " + ex, false); }
            File.WriteAllText(report, log.ToString());
            return ok ? 0 : 1;
        }
    }
}
