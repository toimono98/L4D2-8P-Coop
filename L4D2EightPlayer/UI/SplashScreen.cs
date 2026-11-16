using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>The loading screen designs to choose from (--splash-preview renders them, --splash-demo plays them).</summary>
    public enum SplashStyle
    {
        /// <summary>The game's health bar: it fills from red through orange to green as the app gets ready.</summary>
        HealthBar,
        /// <summary>Eight survivor cards light up one per step: "5 / 8 READY".</summary>
        RollCall,
        /// <summary>A sprayed wall: the title in graffiti, chalk tally marks counting up.</summary>
        SafeRoomWall,
        /// <summary>A title card with the startup steps ticked off one by one, and a thin bar along the bottom.</summary>
        Checklist,
    }

    /// <summary>
    /// The startup loading screen: a borderless card in the middle of the screen while the app reads its settings,
    /// finds the game and the server, and checks the mods. Every design is drawn here in one pass (no child
    /// controls), so it never draws half a frame; a light timer animates the pulse and shimmer.
    /// </summary>
    public sealed class SplashScreen : Form
    {
        /// <summary>What the app does at startup, in order (eight, like the survivors).</summary>
        public static readonly string[] DefaultSteps =
        {
            "Reading the component list", "Finding Left 4 Dead 2", "Looking for the dedicated server", "Loading custom campaigns",
            "Checking your mods", "Reading your skins", "Checking for updates", "Ready",
        };

        public SplashStyle Style { get; set; }
        public string[] Steps { get; set; } = DefaultSteps;
        /// <summary>Steps finished (0 to Steps.Length).</summary>
        public int Done { get; private set; }
        public string Side { get; set; } = "HOST";
        double phase;   // animation, 0..1 repeating
        readonly Timer tick = new Timer { Interval = 33 };
        static readonly Size Design = new Size(720, 405);

        public SplashScreen(SplashStyle style)
        {
            Style = style;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Ground;
            ClientSize = Sz(Design.Width, Design.Height);
            Icon = Theme.AppIcon();
            Text = "L4D2 8-Player Co-op";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            tick.Tick += (s, e) => { phase = (phase + 0.02) % 1; Invalidate(); };
            Shown += (s, e) => tick.Start();
            FormClosed += (s, e) => tick.Dispose();
        }

        /// <summary>Moves on: done steps finished, the next one is what's happening now.</summary>
        public void Set(int done) { Done = Math.Max(0, Math.Min(Steps.Length, done)); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics, ClientRectangle, Style, Steps, Done, phase, Side);

        // ================================================================== drawing (shared with the preview)

        /// <summary>Draws one design: for the window, and for --splash-preview's pictures.</summary>
        public static void Render(Graphics g, Rectangle r, SplashStyle style, string[] steps, int done, double phase, string side)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(Theme.Ground)) g.FillRectangle(b, r);
            Grain(g, r);
            double p = steps.Length == 0 ? 1 : done / (double)steps.Length;
            string now = done >= steps.Length ? steps.Last() : steps[done] + "…";
            switch (style)
            {
                case SplashStyle.HealthBar: HealthBar(g, r, p, now, done, steps.Length, phase, side); break;
                case SplashStyle.RollCall: RollCall(g, r, now, done, steps.Length, phase); break;
                case SplashStyle.SafeRoomWall: Wall(g, r, p, now, phase); break;
                default: Checklist(g, r, p, steps, done, phase, side); break;
            }
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            Write(g, "v" + AppInfo.Version, Theme.BodyBold(8f), Theme.Bot, new Rectangle(r.Right - S(120), r.Bottom - S(26), S(104), S(18)), right: true);
        }

        // ---- 1. the health bar
        static void HealthBar(Graphics g, Rectangle r, double p, string now, int done, int total, double phase, string side)
        {
            int pad = S(40);
            Tape(g, "Loading", new PointF(r.X + pad + S(50), r.Y + S(46)), -2f);
            Write(g, "8-PLAYER CO-OP", Theme.Display(34f), Theme.Bone, new Rectangle(r.X + pad, r.Y + S(78), r.Width, S(60)));
            Write(g, "LEFT 4 DEAD 2 · " + side, Theme.BodyBold(9.5f), Theme.Muted, new Rectangle(r.X + pad + S(3), r.Y + S(136), r.Width, S(20)));
            Graffiti(g, "patching you up", 15f, Theme.Amber, new PointF(r.Right - pad - S(120), r.Y + S(120)), -4f);

            // the bar: red when it starts, orange halfway, green once nearly there (the way the game shows health)
            var bar = new Rectangle(r.X + pad, r.Bottom - S(118), r.Width - pad * 2, S(28));
            Write(g, now.ToUpperInvariant(), Theme.Stencil(12f), Theme.Bone, new Rectangle(bar.X, bar.Y - S(30), bar.Width - S(80), S(24)));
            Write(g, $"{(int)(p * 100)}%", Theme.Display(16f), Theme.Bone, new Rectangle(bar.Right - S(90), bar.Y - S(34), S(90), S(28)), right: true);
            using (var b = new SolidBrush(Theme.Console)) g.FillRectangle(b, bar);
            var fillColor = p < 0.34 ? Theme.Hex("#c0392b") : p < 0.67 ? Theme.Hex("#e0802a") : Theme.Hex("#5cb85c");
            int fw = (int)(bar.Width * p);
            if (fw > 0)
            {
                var fill = new Rectangle(bar.X, bar.Y, fw, bar.Height);
                using (var lg = new LinearGradientBrush(fill, Light(fillColor, 0.18f), Dark(fillColor, 0.25f), LinearGradientMode.Vertical)) g.FillRectangle(lg, fill);
                // a soft glow moving along the filled part
                int gx = fill.X + (int)((fill.Width + S(80)) * phase) - S(80);
                using (var glow = new LinearGradientBrush(new Rectangle(gx, fill.Y, S(80), fill.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), LinearGradientMode.Horizontal))
                {
                    glow.SetBlendTriangularShape(0.5f);
                    var clip = g.Clip; g.SetClip(fill); g.FillRectangle(glow, gx, fill.Y, S(80), fill.Height); g.Clip = clip;
                }
            }
            // ticks every tenth, like the HUD's
            using (var tickPen = new Pen(Color.FromArgb(150, Theme.Ground), S(2)))
                for (int i = 1; i < 10; i++) { int x = bar.X + bar.Width * i / 10; g.DrawLine(tickPen, x, bar.Y + S(4), x, bar.Bottom - S(4)); }
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);
            Write(g, $"STEP {Math.Min(done + 1, total)} OF {total}", Theme.BodyBold(8.5f), Theme.Muted, new Rectangle(bar.X, bar.Bottom + S(10), S(200), S(18)));
        }

        // ---- 2. roll call: one survivor per step
        static readonly (string id, string name)[] Survivors =
            { ("nick", "NICK"), ("rochelle", "ROCHELLE"), ("coach", "COACH"), ("ellis", "ELLIS"), ("bill", "BILL"), ("zoey", "ZOEY"), ("louis", "LOUIS"), ("francis", "FRANCIS") };

        static void RollCall(Graphics g, Rectangle r, string now, int done, int total, double phase)
        {
            int pad = S(36);
            Tape(g, "Your group", new PointF(r.X + pad + S(58), r.Y + S(40)), -2f);
            Write(g, "ROLL CALL", Theme.Display(30f), Theme.Bone, new Rectangle(r.X + pad + S(130), r.Y + S(18), S(400), S(50)));
            int ready = (int)Math.Round(done * 8.0 / Math.Max(1, total));
            Write(g, $"{ready} / 8 READY", Theme.Display(20f), ready == 8 ? Theme.Hex("#5cb85c") : Theme.Bone, new Rectangle(r.Right - pad - S(220), r.Y + S(28), S(220), S(36)), right: true);

            int gap = S(8), cw = (r.Width - pad * 2 - gap * 7) / 8, ch = cw + S(30);
            int top = r.Y + S(108);
            var pics = Portraits();
            for (int i = 0; i < 8; i++)
            {
                var card = new Rectangle(r.X + pad + i * (cw + gap), top, cw, ch);
                bool lit = i < ready, current = i == ready && ready < 8;
                using (var b = new SolidBrush(lit ? Theme.Panel2 : Theme.Panel)) g.FillRectangle(b, card);
                var pic = new Rectangle(card.X + S(6), card.Y + S(6), cw - S(12), cw - S(12));
                if (pics.TryGetValue(Survivors[i].id, out var img))
                {
                    if (lit) g.DrawImage(img, pic);
                    else using (var dim = Dim(current ? 0.55f : 0.22f)) g.DrawImage(img, pic, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, dim);
                }
                else
                {
                    using (var b = new SolidBrush(Theme.Header)) g.FillRectangle(b, pic);
                    Write(g, Survivors[i].name.Substring(0, 1), Theme.Display(22f), lit ? Theme.Bone : Theme.Bot, pic, center: true);
                }
                Write(g, Survivors[i].name, Theme.Stencil(8.5f), lit ? Theme.Bone : Theme.Bot, new Rectangle(card.X, pic.Bottom + S(4), card.Width, S(18)), center: true);
                var edge = lit ? Theme.Bone : current ? Blend(Theme.Line, Theme.Amber, 0.5 + 0.5 * Math.Sin(phase * Math.PI * 2)) : Theme.Line;
                using (var pen = new Pen(edge, lit || current ? S(2) : 1)) g.DrawRectangle(pen, card.X, card.Y, card.Width - 1, card.Height - 1);
                if (lit) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, card.X, card.Bottom - S(4), card.Width, S(4));
            }
            Write(g, now, Theme.BodySemi(12f), Theme.Bone, new Rectangle(r.X + pad, top + ch + S(26), r.Width - pad * 2, S(24)));
            // the server card's eight slots, filling up
            int pipY = top + ch + S(62), pipW = (r.Width - pad * 2 - S(6) * 7) / 8;
            for (int i = 0; i < 8; i++)
            {
                var pip = new Rectangle(r.X + pad + i * (pipW + S(6)), pipY, pipW, S(10));
                using (var b = new SolidBrush(i < ready ? Theme.Red : Theme.Panel)) g.FillRectangle(b, pip);
                using (var pen = new Pen(i < ready ? Theme.Red : Theme.Line)) g.DrawRectangle(pen, pip.X, pip.Y, pip.Width - 1, pip.Height - 1);
            }
            Graffiti(g, "no one left behind", 13f, Theme.Amber, new PointF(r.Right - pad - S(110), r.Bottom - S(50)), -3f);
        }

        // ---- 3. the wall: graffiti and chalk tallies
        static void Wall(Graphics g, Rectangle r, double p, string now, double phase)
        {
            // a darker, greenish concrete with a few drips of paint
            using (var lg = new LinearGradientBrush(r, Theme.Hex("#1d1f1a"), Theme.Hex("#121311"), LinearGradientMode.Vertical)) g.FillRectangle(lg, r);
            Grain(g, r, 2);
            var rnd = new Random(8);
            using (var pen = new Pen(Color.FromArgb(70, Theme.Red), S(2)))
                for (int i = 0; i < 5; i++) { int x = r.X + S(150) + i * S(105) + rnd.Next(S(30)); g.DrawLine(pen, x, r.Y + S(132), x, r.Y + S(132) + S(14 + rnd.Next(40))); }
            var title = "8-PLAYER CO-OP";
            var tf = Theme.Graffiti(40f);
            var ts = g.MeasureString(title, tf);
            var state = g.Save();
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + S(92));
            g.RotateTransform(-3f);
            using (var shadow = new SolidBrush(Color.FromArgb(160, Theme.Red))) g.DrawString(title, tf, shadow, -ts.Width / 2 + S(4), -ts.Height / 2 + S(4));
            using (var b = new SolidBrush(Theme.Bone)) g.DrawString(title, tf, b, -ts.Width / 2, -ts.Height / 2);
            g.Restore(state);
            Graffiti(g, "see you in the safe room", 15f, Theme.Amber, new PointF(r.X + r.Width / 2f + S(60), r.Y + S(152)), -2f);

            // chalk tallies: twenty marks in fours with a stroke through (one batch of five per quarter)
            int marks = (int)Math.Round(p * 20);
            int groupW = S(70), startX = r.X + (r.Width - groupW * 4 - S(40) * 3) / 2, y = r.Y + S(206), h = S(52);
            var jitter = new Random(3);
            using (var chalk = new Pen(Color.FromArgb(225, Theme.Bone), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var faint = new Pen(Color.FromArgb(28, Theme.Bone), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int grp = 0; grp < 4; grp++)
                {
                    int gx = startX + grp * (groupW + S(40));
                    for (int k = 0; k < 5; k++)
                    {
                        int n = grp * 5 + k;
                        var pen = n < marks ? chalk : faint;
                        float j1 = (float)(jitter.NextDouble() - 0.5) * S(6), j2 = (float)(jitter.NextDouble() - 0.5) * S(6);
                        if (k < 4) g.DrawLine(pen, gx + k * S(16) + j1, y + S(2), gx + k * S(16) + j2, y + h);
                        else g.DrawLine(pen, gx - S(8), y + h - S(6), gx + S(64), y + S(8));
                    }
                }
            // what's happening, with a blinking cursor
            bool cursor = phase < 0.5;
            Write(g, now.ToUpperInvariant() + (cursor ? " _" : ""), Theme.Stencil(12f), Theme.Bone, new Rectangle(r.X + S(40), r.Bottom - S(78), r.Width - S(200), S(24)));
            Tape(g, $"{(int)(p * 100)}%", new PointF(r.Right - S(90), r.Bottom - S(66)), 3f);
        }

        // ---- 4. the checklist title card
        static void Checklist(Graphics g, Rectangle r, double p, string[] steps, int done, double phase, string side)
        {
            int pad = S(40), split = r.X + r.Width * 45 / 100;
            Tape(g, "Starting up", new PointF(r.X + pad + S(62), r.Y + S(52)), -2f);
            Write(g, "8-PLAYER", Theme.Display(36f), Theme.Bone, new Rectangle(r.X + pad, r.Y + S(84), split - pad, S(60)));
            Write(g, "CO-OP", Theme.Display(36f), Theme.Bone, new Rectangle(r.X + pad, r.Y + S(138), split - pad, S(60)));
            using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, r.X + pad + S(2), r.Y + S(204), S(110), S(5));
            Write(g, "LEFT 4 DEAD 2 · " + side, Theme.BodyBold(9.5f), Theme.Muted, new Rectangle(r.X + pad + S(2), r.Y + S(218), split - pad, S(20)));
            Graffiti(g, "safe room ahead", 14f, Theme.Amber, new PointF(r.X + pad + S(110), r.Y + S(280)), -4f);

            // the steps, ticked off as they finish
            int rowH = S(32), y = r.Y + S(48);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, split, r.Y + S(40), split, r.Bottom - S(56));
            for (int i = 0; i < steps.Length; i++)
            {
                var row = new Rectangle(split + S(28), y + i * rowH, r.Right - split - S(56), rowH);
                bool isDone = i < done, current = i == done;
                var box = new Rectangle(row.X, row.Y + (rowH - S(18)) / 2, S(18), S(18));
                if (isDone)
                {
                    using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, box);
                    using (var pen = new Pen(Theme.BoneText, S(2)))
                        g.DrawLines(pen, new[] { new Point(box.X + S(4), box.Y + S(9)), new Point(box.X + S(8), box.Y + S(13)), new Point(box.X + S(14), box.Y + S(5)) });
                }
                else if (current)
                {
                    // three dots taking turns, while it works
                    using (var pen = new Pen(Theme.Amber, S(2))) g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                    for (int d = 0; d < 3; d++)
                    {
                        bool on = (int)(phase * 3) == d;
                        using (var b = new SolidBrush(on ? Theme.Amber : Theme.Bot)) g.FillEllipse(b, row.Right - S(36) + d * S(12), row.Y + rowH / 2 - S(3), S(6), S(6));
                    }
                }
                else using (var pen = new Pen(Theme.Bot)) g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                Write(g, steps[i], current ? Theme.BodySemi(11f) : Theme.Body(11f), isDone ? Theme.Muted : current ? Theme.Bone : Theme.Bot,
                    new Rectangle(row.X + S(30), row.Y, row.Width - S(70), rowH), vcenter: true);
            }

            // a thin bar along the bottom, with a shimmer running over it
            var bar = new Rectangle(r.X + pad, r.Bottom - S(46), r.Width - pad * 2, S(6));
            using (var b = new SolidBrush(Theme.LineSoft)) g.FillRectangle(b, bar);
            int fw = (int)(bar.Width * p);
            if (fw > 0)
            {
                using (var b = new SolidBrush(Theme.Bone)) g.FillRectangle(b, bar.X, bar.Y, fw, bar.Height);
                int sx = bar.X + (int)((fw + S(60)) * phase) - S(60);
                using (var lg = new LinearGradientBrush(new Rectangle(sx, bar.Y, S(60), bar.Height), Color.FromArgb(0, Theme.Amber), Theme.Amber, LinearGradientMode.Horizontal))
                {
                    lg.SetBlendTriangularShape(0.5f);
                    var clip = g.Clip; g.SetClip(new Rectangle(bar.X, bar.Y, fw, bar.Height)); g.FillRectangle(lg, sx, bar.Y, S(60), bar.Height); g.Clip = clip;
                }
            }
        }

        // ================================================================== pieces

        static void Write(Graphics g, string s, Font f, Color c, Rectangle r, bool right = false, bool center = false, bool vcenter = false)
        {
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
                        | (right ? TextFormatFlags.Right : center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)
                        | (vcenter || center ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top);
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        /// <summary>A strip of tape with the label on it (the app's page tags), centred on a point.</summary>
        static void Tape(Graphics g, string text, PointF at, float angle)
        {
            var f = Theme.BodyBold(10f);
            var t = g.MeasureString(text.ToUpperInvariant(), f);
            var state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(angle);
            var rect = new RectangleF(-t.Width / 2 - S(10), -t.Height / 2 - S(4), t.Width + S(20), t.Height + S(8));
            using (var b = new SolidBrush(Theme.Bone)) g.FillRectangle(b, rect);
            using (var b = new SolidBrush(Theme.Ground))
                g.DrawString(text.ToUpperInvariant(), f, b, rect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            g.Restore(state);
        }

        /// <summary>A spray-painted note in the distressed font, tilted, centred on a point.</summary>
        static void Graffiti(Graphics g, string text, float pt, Color c, PointF at, float angle)
        {
            var f = Theme.Graffiti(pt);
            var t = g.MeasureString(text.ToUpperInvariant(), f);
            var state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(angle);
            using (var b = new SolidBrush(c)) g.DrawString(text.ToUpperInvariant(), f, b, -t.Width / 2, -t.Height / 2);
            g.Restore(state);
        }

        static Bitmap grain;
        /// <summary>A faint film grain over the background, so it isn't flat (the same noise every time).</summary>
        static void Grain(Graphics g, Rectangle r, int strength = 1)
        {
            if (grain == null)
            {
                grain = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
                var rnd = new Random(1);
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        int v = rnd.Next(256);
                        grain.SetPixel(x, y, Color.FromArgb(rnd.Next(10), v, v, v));
                    }
            }
            using (var tb = new TextureBrush(grain))
                for (int i = 0; i < strength; i++) g.FillRectangle(tb, r);
        }

        static Dictionary<string, Image> portraits;
        /// <summary>The survivors' portraits the skin picker keeps (GamePictures), when they're there already; else none.</summary>
        static Dictionary<string, Image> Portraits()
        {
            if (portraits != null) return portraits;
            portraits = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dir = Path.Combine(Paths.AppData, "pictures");
                // the newest folder that has the portraits (one per game version)
                var folder = Directory.Exists(dir) ? Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "nick.png"))).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (folder != null)
                    foreach (var (id, _) in Survivors)
                    {
                        var png = Path.Combine(folder, id + ".png");
                        if (File.Exists(png)) using (var img = Image.FromFile(png)) portraits[id] = new Bitmap(img);
                    }
            }
            catch { }
            return portraits;
        }

        static ImageAttributes Dim(float opacity)
        {
            var a = new ImageAttributes();
            // greyed and faded: waiting for their turn
            a.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { .3f, .3f, .3f, 0, 0 }, new[] { .59f, .59f, .59f, 0, 0 }, new[] { .11f, .11f, .11f, 0, 0 },
                new[] { 0, 0, 0, opacity, 0 }, new[] { 0f, 0, 0, 0, 1 },
            }));
            return a;
        }

        static Color Light(Color c, float k) => Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k));
        static Color Dark(Color c, float k) => Color.FromArgb(c.A, (int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)));
        static Color Blend(Color a, Color b, double t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // ================================================================== previews

        /// <summary>--splash-preview &lt;dir&gt;: each design at three points of the startup, as PNGs, and one sheet of them all.</summary>
        public static void Preview(string dir)
        {
            Directory.CreateDirectory(dir);
            Theme.LoadFonts();
            var points = new[] { 2, 5, 8 };
            var size = Sz(Design.Width, Design.Height);
            var styles = (SplashStyle[])Enum.GetValues(typeof(SplashStyle));
            int thumbW = size.Width / 2, thumbH = size.Height / 2, gap = 12;
            using (var sheet = new Bitmap(points.Length * (thumbW + gap) + gap, styles.Length * (thumbH + gap + 22) + gap))
            using (var sg = Graphics.FromImage(sheet))
            {
                sg.Clear(Theme.Console);
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int si = 0; si < styles.Length; si++)
                    for (int pi = 0; pi < points.Length; pi++)
                        using (var bmp = new Bitmap(size.Width, size.Height))
                        {
                            using (var g = Graphics.FromImage(bmp)) Render(g, new Rectangle(Point.Empty, size), styles[si], DefaultSteps, points[pi], 0.35, "HOST");
                            bmp.Save(Path.Combine(dir, $"splash-{si + 1}-{styles[si]}-{points[pi]}of8.png"), ImageFormat.Png);
                            int x = gap + pi * (thumbW + gap), y = gap + si * (thumbH + gap + 22);
                            TextRenderer.DrawText(sg, $"{si + 1}. {styles[si]}  ·  step {points[pi]} of 8", Theme.BodyBold(9f), new Point(x, y), Theme.Muted);
                            sg.DrawImage(bmp, x, y + 20, thumbW, thumbH);
                        }
                sheet.Save(Path.Combine(dir, "splash-sheet.png"), ImageFormat.Png);
            }
        }

        /// <summary>--splash-demo [n]: plays the designs (or just design n) on screen, a step every 0.6 seconds.</summary>
        public static void Demo(int only)
        {
            Application.EnableVisualStyles();
            Theme.LoadFonts();
            var styles = ((SplashStyle[])Enum.GetValues(typeof(SplashStyle))).Where((s, i) => only <= 0 || i + 1 == only).ToList();
            foreach (var style in styles)
                using (var f = new SplashScreen(style))
                {
                    f.Show();
                    for (int step = 0; step <= f.Steps.Length; step++)
                    {
                        f.Set(step);
                        var until = DateTime.Now.AddMilliseconds(step == f.Steps.Length ? 1500 : 600);
                        while (DateTime.Now < until && !f.IsDisposed) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    }
                    f.Close();
                }
        }
    }
}
