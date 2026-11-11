using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI.Themes
{
    static class Draw
    {
        /// <summary>Draws text; in audit mode also records text that doesn't fit its box.</summary>
        public static void Text(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags)
        {
            if (Audit.On && !string.IsNullOrEmpty(text) && r.Width > 0)
            {
                Audit.CountDraw(text);
                bool wrap = (flags & TextFormatFlags.WordBreak) != 0;
                var need = wrap
                    ? TextRenderer.MeasureText(g, text, font, new Size(r.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix)
                    : TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (need.Width > r.Width + 1 || need.Height > r.Height + 2)
                    Audit.Text(text, need, r.Size);
            }
            TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix);
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);

        /// <summary>
        /// Makes a change (a page or card swap, a refilled panel) with drawing held, then paints the result in
        /// one pass, so boxes don't appear one by one or flash the old state on the way.
        /// </summary>
        /// <summary>Set by --flicker-test --legacy: changes draw as they happen, as before 2.1.1 (for comparison).</summary>
        public static bool NoBatch;

        public static void Batch(Control c, Action change)
        {
            if (c == null || !c.IsHandleCreated || !c.Visible || NoBatch) { change(); return; }
            SendMessage(c.Handle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);
            try { change(); }
            finally
            {
                SendMessage(c.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                // RDW_ERASE | RDW_FRAME | RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW
                RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, 0x4 | 0x400 | 0x1 | 0x80 | 0x100);
            }
        }

        public static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        public static void Border(Graphics g, Rectangle r, Color c, int width = 1)
        {
            using (var p = new Pen(c, width) { Alignment = PenAlignment.Inset })
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        public static void Dashed(Graphics g, Rectangle r, Color c)
        {
            using (var p = new Pen(c) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>The nearest opaque background behind a control (parents are often transparent).</summary>
        public static Color SolidBack(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Theme.Ground;
        }

        public static void FocusRing(Graphics g, Rectangle r)
        {
            using (var p = new Pen(Theme.Amber, S(2)) { Alignment = PenAlignment.Inset })
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>Draws a small filled/outlined tag (HOST, BOT, READY) with its text centred.</summary>
        public static void TagBox(Graphics g, Rectangle r, string text, Font f, Color fill, Color border, Color fg, bool dashed = false)
        {
            if (fill != Color.Empty) using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            if (border != Color.Empty) { if (dashed) Dashed(g, r, border); else Border(g, r, border); }
            Text(g, text, f, fg, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        /// <summary>Size of a tag for this text: text plus scaled padding.</summary>
        public static Size TagSize(string text, Font f)
        {
            var t = Measure(text, f);
            return new Size(t.Width + S(14), t.Height + S(6));
        }
    }

    // ====================================================================== buttons

    public enum ButtonKind { Primary, Outline, Ghost, Dark }

    /// <summary>Flat, game-style button. Primary is the blood-red "do it" button with a pressed-in shadow.</summary>
    public class GameButton : Button, Themed
    {
        ButtonKind kind;
        bool hover, down;
        public ButtonKind Kind { get => kind; set { kind = value; Invalidate(); } }
        public float FontPt { get; set; } = 11f;

        public GameButton(string text, ButtonKind kind = ButtonKind.Outline, float pt = 11f)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text;
            this.kind = kind;
            FontPt = pt;
            Font = Theme.Display(pt);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Margin = P(0, 0, 10, 0);
            AutoSize = false;
            Size = GetPreferredSize(Size.Empty);
        }

        /// <summary>Shadow under the Primary face, in pixels.</summary>
        int Shadow => S(4);

        public override Size GetPreferredSize(Size proposed)
        {
            var t = Draw.Measure(Text.ToUpperInvariant(), Font);
            int h = Math.Max(t.Height + S(18), S(40)) + (kind == ButtonKind.Primary ? Shadow : 0);
            return new Size(t.Width + Math.Max(S(24), t.Height * 2), h);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (!AutoSizeLocked) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        /// <summary>Set when a layout gives the button a fixed size (Dock/Anchor), so text changes don't resize it.</summary>
        public bool AutoSizeLocked { get; set; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            g.Clear(Draw.SolidBack(this));
            var r = ClientRectangle;
            Color fg;
            switch (kind)
            {
                case ButtonKind.Primary:
                {
                    int sh = Shadow, press = down ? sh - S(1) : 0;
                    var face = new Rectangle(0, press, r.Width, r.Height - sh);
                    using (var b = new SolidBrush(Enabled ? Theme.RedShadow : Theme.LineSoft)) g.FillRectangle(b, 0, r.Height - sh - press, r.Width, sh + press);
                    using (var b = new SolidBrush(!Enabled ? Theme.Line : hover ? Theme.RedHover : Theme.Red)) g.FillRectangle(b, face);
                    fg = Enabled ? Theme.BoneText : Theme.Muted;
                    r = face;
                    break;
                }
                case ButtonKind.Dark:
                    using (var b = new SolidBrush(hover && Enabled ? Theme.Hex("#2a2420") : Theme.Ground)) g.FillRectangle(b, r);
                    fg = Enabled ? Theme.Bone : Theme.Muted;
                    break;
                case ButtonKind.Outline:
                    if (hover && Enabled) using (var b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, r);
                    Draw.Border(g, r, Enabled ? Theme.Bone : Theme.Line, S(2));
                    fg = Enabled ? Theme.Bone : Theme.Muted;
                    break;
                default:
                    if (hover && Enabled) using (var b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, r);
                    Draw.Border(g, r, hover && Enabled ? Theme.Muted : Theme.Line);
                    fg = Enabled ? Theme.Bone : Theme.Muted;
                    break;
            }
            var inner = Rectangle.Inflate(r, -S(6), 0);
            Draw.Text(g, Text.ToUpperInvariant(), Font, fg, inner, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) Draw.FocusRing(g, ClientRectangle);
        }
    }

    // ====================================================================== containers

    /// <summary>A bordered panel with an optional small-caps title, used instead of GroupBox.</summary>
    public class Card : Panel, Themed
    {
        Color border = Theme.Line;
        int borderWidth = 1;
        public Color BorderColor { get => border; set { border = value; Invalidate(); } }
        public int BorderWidth { get => borderWidth; set { borderWidth = value; Invalidate(); } }
        public bool Dashed { get; set; }

        static Font TitleFont => Theme.BodyBold(8.5f);

        public Card(string title = null)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Bone;
            Text = title ?? "";
            Margin = P(0, 0, 0, 12);
            UpdatePadding();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdatePadding(); Invalidate(); }

        int TitleHeight => Draw.Measure("X", TitleFont).Height;

        void UpdatePadding() => Padding = new Padding(S(14), string.IsNullOrEmpty(Text) ? S(12) : S(14) + TitleHeight + S(6), S(14), S(12));

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            base.OnPaint(e);
            var g = e.Graphics;
            if (Dashed) using (var p = new Pen(border, borderWidth) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            else Draw.Border(g, ClientRectangle, border, borderWidth);
            if (!string.IsNullOrEmpty(Text))
                Draw.Text(g, Text.ToUpperInvariant(), TitleFont, Theme.Muted, new Rectangle(S(14), S(12), Width - S(28), TitleHeight + S(2)), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>A strip of tape with a stencilled label, slightly crooked ("SUPPLIES", "RADIO").</summary>
    public class TapeLabel : Control, Themed
    {
        public float Angle { get; set; } = -2f;

        public TapeLabel(string text)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            Text = text;
            Font = Theme.Display(9.5f);
            BackColor = Color.Transparent;
            var t = Draw.Measure(text.ToUpperInvariant(), Font);
            Size = new Size(t.Width + S(26), t.Height + S(14));
            Margin = P(0, 6, 14, 0);
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var t = g.MeasureString(Text.ToUpperInvariant(), Font);
            g.TranslateTransform(Width / 2f, Height / 2f);
            g.RotateTransform(Angle);
            var rect = new RectangleF(-t.Width / 2 - S(9), -t.Height / 2 - S(3), t.Width + S(18), t.Height + S(6));
            using (var b = new SolidBrush(Theme.Bone)) g.FillRectangle(b, rect);
            using (var b = new SolidBrush(Theme.Ground))
                g.DrawString(Text.ToUpperInvariant(), Font, b, rect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
    }

    /// <summary>Spray-painted note in the distressed font. Can be tilted and centred.</summary>
    public class GraffitiLabel : Control, Themed
    {
        public float Angle { get; set; } = -3f;
        public bool Centered { get; set; }

        public GraffitiLabel(string text, float pt = 15f)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Amber;
            Font = Theme.Graffiti(pt);
            Text = text;
            Margin = P(0);
            AccessibleRole = AccessibleRole.StaticText;
        }

        /// <summary>Room the tilted text needs (GDI+ measure plus a margin for the rotation).</summary>
        public Size Needed
        {
            get
            {
                using (var g = CreateGraphics())
                {
                    var t = g.MeasureString(Text.ToUpperInvariant(), Font);
                    return new Size((int)Math.Ceiling(t.Width) + S(16), (int)Math.Ceiling(t.Height) + S(14));
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            if (!Centered) Size = Needed;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Audit.On && !Centered)
            {
                var need = Needed;
                if (need.Width > Width + 1) { Audit.Context = this; Audit.Text(Text, need, Size); }
            }
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.TranslateTransform(Width / 2f, Height / 2f);
            g.RotateTransform(Angle);
            using (var b = new SolidBrush(ForeColor))
                g.DrawString(Text.ToUpperInvariant(), Font, b, new RectangleF(-Width / 2f, -Height / 2f, Width, Height),
                    new StringFormat(Centered ? 0 : StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None });
        }
    }

    /// <summary>A tag such as HOST / BOT / PROBLEM / UPDATE. Filled or outlined.</summary>
    public class Tag : Control, Themed
    {
        Color fill = Color.Empty, border = Theme.Bone, text = Theme.Bone;
        bool dashed;

        public Tag(string label)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            Font = Theme.BodyBold(7.5f);
            Margin = P(0, 4, 0, 0);
            Set(label, Color.Empty, Theme.Bone, Theme.Bone);
        }

        public void Set(string label, Color fillColor, Color borderColor, Color textColor, bool dashedBorder = false)
        {
            Text = label;
            fill = fillColor; border = borderColor; text = textColor; dashed = dashedBorder;
            var t = Draw.TagSize(label.ToUpperInvariant(), Font);
            Size = new Size(t.Width + S(2), t.Height);
            AccessibleName = label;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            Draw.TagBox(e.Graphics, ClientRectangle, Text.ToUpperInvariant(), Font, fill, border, text, dashed);
        }
    }

    // ====================================================================== inputs

    /// <summary>A square on/off switch (CheckBox underneath, so Checked/CheckedChanged work as usual).</summary>
    public class ToggleSwitch : CheckBox, Themed
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            Size = Sz(64, 34);
            Cursor = Cursors.Hand;
            Margin = P(0);
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.SolidBack(this));
            var r = new Rectangle(0, 0, Width, Height);
            using (var b = new SolidBrush(Checked ? Theme.Red : Theme.LineSoft)) g.FillRectangle(b, r);
            Draw.Border(g, r, Theme.Line);
            int pad = S(4), k = Height - pad * 2;
            var knob = new Rectangle(Checked ? Width - k - pad : pad, pad, k, k);
            using (var b = new SolidBrush(Enabled ? Theme.Bone : Theme.Muted)) g.FillRectangle(b, knob);
            if (Focused && ShowFocusCues) Draw.FocusRing(g, r);
        }
    }

    /// <summary>A checkbox drawn in the theme: red box with a bone tick when on, outlined when off.</summary>
    public class GameCheck : CheckBox, Themed
    {
        public GameCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
        }

        int Box => S(18);
        int Gap => S(10);

        public override Size GetPreferredSize(Size proposed)
        {
            var t = Draw.Measure(Text, Font);
            return new Size(Box + Gap + t.Width + S(8), Math.Max(t.Height, Box) + S(8));
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            g.Clear(Draw.SolidBack(this));
            var box = new Rectangle(0, (Height - Box) / 2, Box, Box);
            if (Checked) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, box);
            Draw.Border(g, box, Checked ? Theme.Red : Theme.Muted);
            if (Checked)
                using (var p = new Pen(Theme.BoneText, S(2)))
                    g.DrawLines(p, new[] { new Point(box.X + Box * 2 / 9, box.Y + Box / 2), new Point(box.X + Box * 4 / 9, box.Y + Box * 13 / 18), new Point(box.X + Box * 7 / 9, box.Y + Box * 5 / 18) });
            Draw.Text(g, Text, Font, Enabled ? Theme.Bone : Theme.Muted, new Rectangle(Box + Gap, 0, Width - Box - Gap, Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) Draw.FocusRing(g, ClientRectangle);
        }
    }

    /// <summary>"−  8  +" number picker. Mirrors NumericUpDown's Value/Minimum/Maximum/ValueChanged.</summary>
    public class Stepper : FlowLayoutPanel, Themed
    {
        decimal value, min = 0, max = 100;
        readonly GameButton minus, plus;
        readonly Label number;
        public event EventHandler ValueChanged;

        public decimal Minimum { get => min; set { min = value; Value = this.value; } }
        public decimal Maximum
        {
            get => max;
            set
            {
                max = value;
                number.Width = Math.Max(number.Width, Draw.Measure(max.ToString("0"), number.Font).Width + S(12));
                Value = this.value;
            }
        }
        /// <summary>How much − and + change the value.</summary>
        public decimal Step { get; set; } = 1;
        public decimal Value
        {
            get => value;
            set
            {
                var v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) { Refresh2(); return; }
                this.value = v;
                Refresh2();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Stepper(string label)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            AccessibleName = label;
            minus = new GameButton("−", ButtonKind.Ghost, 13f) { Size = Sz(44, 44), AutoSizeLocked = true, Margin = P(0), AccessibleName = "Fewer " + label.ToLowerInvariant() };
            plus = new GameButton("+", ButtonKind.Ghost, 13f) { Size = Sz(44, 44), AutoSizeLocked = true, Margin = P(0), AccessibleName = "More " + label.ToLowerInvariant() };
            number = new Label { Font = Theme.Display(17f), ForeColor = Theme.Bone, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(Math.Max(S(48), Draw.Measure("16", Theme.Display(17f)).Width + S(12)), S(44)), Margin = P(0) };
            minus.Click += (s, e) => Value -= Step;
            plus.Click += (s, e) => Value += Step;
            WrapContents = false;
            Margin = P(0);
            Padding = P(0);
            Controls.AddRange(new Control[] { minus, number, plus });
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Refresh2();
        }

        void Refresh2()
        {
            number.Text = value.ToString("0");
            minus.Enabled = value > min;
            plus.Enabled = value < max;
        }
    }

    /// <summary>
    /// Row tooltips for a ListView (each item's ToolTipText) that show after a short pause and go away the
    /// moment the mouse leaves the row or the list. The ListView's own item tooltips linger for seconds.
    /// </summary>
    public static class HoverTips
    {
        /// <summary>For the flicker test: no tooltips (one popping up under a resting mouse isn't a drawing glitch).</summary>
        public static bool Off;

        public static void Attach(ListView lv)
        {
            lv.ShowItemToolTips = false;
            var tip = new ToolTip { UseAnimation = false, UseFading = false };
            var timer = new Timer { Interval = 450 };
            ListViewItem pending = null, shown = null;

            void HideAll()
            {
                timer.Stop();
                pending = null;
                if (shown != null) { tip.Hide(lv); shown = null; }
            }

            lv.MouseMove += (s, e) =>
            {
                var item = lv.GetItemAt(e.X, e.Y);
                if (item == shown || item == pending) return;
                HideAll();
                if (item == null || string.IsNullOrEmpty(item.ToolTipText)) return;
                pending = item;
                timer.Start();
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (Off || pending == null || !lv.IsHandleCreated) return;
                var p = lv.PointToClient(Cursor.Position);
                if (lv.GetItemAt(p.X, p.Y) != pending) { pending = null; return; }
                shown = pending;
                pending = null;
                tip.Show(shown.ToolTipText, lv, p.X + S(14), p.Y + S(20), 20000);
            };
            lv.MouseLeave += (s, e) => HideAll();
            lv.MouseDown += (s, e) => HideAll();
            lv.MouseWheel += (s, e) => HideAll();
            lv.Leave += (s, e) => HideAll();
            lv.Disposed += (s, e) => { timer.Dispose(); tip.Dispose(); };
        }
    }

    /// <summary>Segmented choice ("EASY | NORMAL | ADVANCED | EXPERT"). Mirrors ComboBox's SelectedItem/SelectedIndex.</summary>
    public class Pills : FlowLayoutPanel, Themed
    {
        readonly string[] values;
        readonly List<GameButton> buttons = new List<GameButton>();
        int selected = -1;
        public event EventHandler SelectedIndexChanged;

        /// <param name="width">Total width in logical (100%) pixels.</param>
        public Pills(string[] values, string[] labels, int width)
        {
            this.values = values;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            AutoSize = true;
            WrapContents = false;
            Margin = P(0);
            BackColor = Color.Transparent;
            int gap = S(6);
            int avail = S(width) - gap * (values.Length - 1);
            var font = Theme.Display(9.5f);
            var need = labels.Select(l => Draw.Measure(l.ToUpperInvariant(), font).Width + S(16)).ToArray();
            // Equal widths when every label fits; otherwise share the width in proportion to the labels.
            bool equal = need.All(n => n <= avail / values.Length);
            int used = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int idx = i;
                int w = equal ? avail / values.Length
                    : i == values.Length - 1 ? avail - used : avail * need[i] / need.Sum();
                used += w;
                var b = new GameButton(labels[i], ButtonKind.Ghost, 9.5f) { AutoSizeLocked = true, Margin = new Padding(0, 0, i < values.Length - 1 ? gap : 0, 0) };
                b.Size = new Size(w, S(44));
                b.Click += (s, e) => SelectedIndex = idx;
                buttons.Add(b);
                Controls.Add(b);
            }
        }

        public int SelectedIndex
        {
            get => selected;
            set
            {
                if (value == selected) return;
                selected = value;
                for (int i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Kind = i == selected ? ButtonKind.Outline : ButtonKind.Ghost;
                    buttons[i].AccessibleDescription = i == selected ? "selected" : null;
                }
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get => selected >= 0 ? values[selected] : null;
            set => SelectedIndex = Array.IndexOf(values, value as string);
        }
    }

    /// <summary>A selectable card with a title and a line of detail (RadioButton underneath).</summary>
    public class ChoiceCard : RadioButton, Themed
    {
        public string Detail { get; set; }
        bool hover;
        static Font TitleFont => Theme.Stencil(11.5f);
        static Font DetailFont => Theme.Body(9.5f);

        public ChoiceCard(string title, string detail)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = title;
            Detail = detail;
            AutoSize = false;
            Height = PreferredHeight;
            Cursor = Cursors.Hand;
            Margin = P(0, 0, 10, 0);
            AccessibleDescription = detail;
        }

        /// <summary>Title line + one detail line + padding.</summary>
        public static int PreferredHeight => Draw.Measure("X", TitleFont).Height + Draw.Measure("X", DetailFont).Height + S(22);

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Checked ? Theme.Panel : hover ? Theme.Panel2 : Theme.Header)) g.FillRectangle(b, r);
            Draw.Border(g, r, Checked ? Theme.Bone : Theme.Line, Checked ? S(2) : 1);
            var titleColor = !Enabled ? Theme.Line : Checked ? Theme.Bone : Theme.Muted;
            int th = Draw.Measure("X", TitleFont).Height, dh = Draw.Measure("X", DetailFont).Height;
            int top = (Height - th - dh - S(2)) / 2;
            Draw.Text(g, Text.ToUpperInvariant(), TitleFont, titleColor, new Rectangle(S(14), top, Width - S(28), th), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Draw.Text(g, Detail ?? "", DetailFont, Enabled ? Theme.Muted : Theme.Line, new Rectangle(S(14), top + th + S(2), Width - S(28), dh), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) Draw.FocusRing(g, r);
        }
    }

    /// <summary>A flat progress meter (bone fill on a dark track); Marquee animates while busy.</summary>
    public class Meter : Control, Themed
    {
        int value;
        bool marquee;
        int phase;   // the moving block's right edge
        // The block's place comes from the time since it started, not a fixed step per tick, so it glides at
        // the same speed however unevenly the timer fires; the timer only asks for a frame about 60 times a second.
        readonly Timer timer = new Timer { Interval = 15 };
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        public Meter()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Height = S(14);
            timer.Tick += (s, e) =>
            {
                int next = (int)(clock.Elapsed.TotalSeconds * S(170)) % Math.Max(1, Width + S(80));
                if (next != phase) { phase = next; Invalidate(); }
            };
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        public int Value
        {
            get => value;
            set { var v = Math.Max(0, Math.Min(100, value)); if (v == this.value) return; this.value = v; AccessibleDescription = v + "%"; Invalidate(); }
        }

        public bool Marquee
        {
            get => marquee;
            set
            {
                if (value == marquee) return;
                marquee = value;
                if (value) clock.Restart(); else clock.Stop();
                timer.Enabled = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.LineSoft)) g.FillRectangle(b, r);
            using (var b = new SolidBrush(Theme.Bone))
            {
                if (marquee) g.FillRectangle(b, phase - S(80), 0, S(80), Height);
                else g.FillRectangle(b, 0, 0, r.Width * value / 100, r.Height);
            }
            Draw.Border(g, r, Theme.Line);
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // ====================================================================== navigation

    /// <summary>The top bar: app title, section tabs with a red underline, a status line on the right.</summary>
    public class NavBar : Panel, Themed
    {
        readonly List<string> items = new List<string>();
        readonly List<Rectangle> hits = new List<Rectangle>();
        int selected, hover = -1;
        public string Title { get; set; } = "8-PLAYER CO-OP";
        public string Subtitle { get; set; } = "LEFT 4 DEAD 2 · HOST";
        public string Status { get => status; set { status = value; Invalidate(); } }
        public Color StatusColor { get => statusColor; set { statusColor = value; Invalidate(); } }
        string status = "";
        Color statusColor = Theme.Muted;
        public event EventHandler SelectedChanged;

        /// <summary>A clickable amber line under the status (e.g. "Version 1.6 is out"); null hides it.</summary>
        public string Notice { get => notice; set { notice = value; Invalidate(); } }
        public event EventHandler NoticeClicked;
        string notice;
        Rectangle noticeHit;

        /// <summary>A small link at the far right (e.g. "? TOUR"); null hides it.</summary>
        public string Help { get => help; set { help = value; Invalidate(); } }
        public event EventHandler HelpClicked;
        string help;
        Rectangle helpHit;
        bool helpHover;

        /// <summary>A two-way switch left of Help (HOST | JOIN): Modes are its labels, Mode the lit one; null hides it.</summary>
        public string[] Modes { get => modes; set { modes = value; Invalidate(); } }
        public int Mode { get => mode; set { mode = value; Invalidate(); } }
        /// <summary>Raised with the index of the side clicked (never the lit one).</summary>
        public event EventHandler<int> ModeClicked;
        string[] modes;
        int mode, modeHover = -1;
        readonly List<Rectangle> modeHits = new List<Rectangle>();
        static Font ModeFont => Theme.Stencil(9.5f);

        static Font TitleFont => Theme.Display(17f);
        static Font SubFont => Theme.BodyBold(7.5f);
        static Font ItemFont => Theme.Stencil(10.5f);

        public NavBar(params string[] sections)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            items.AddRange(sections);
            Dock = DockStyle.Top;
            Height = Draw.Measure("X", TitleFont).Height + Draw.Measure("X", SubFont).Height + S(22);
            BackColor = Theme.Header;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public int Selected { get => selected; set { if (selected == value) return; selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); } }
        public void SetItem(int i, string text) { items[i] = text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            g.Clear(Theme.Header);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            var ts = Draw.Measure(Title, TitleFont);
            var ss = Draw.Measure(Subtitle, SubFont);
            int top = (Height - ts.Height - ss.Height + S(4)) / 2;
            int left = S(26);
            Draw.Text(g, Title, TitleFont, Theme.Bone, new Rectangle(left, top, ts.Width + S(4), ts.Height), TextFormatFlags.Left | TextFormatFlags.Top);
            Draw.Text(g, Subtitle, SubFont, Theme.Muted, new Rectangle(left + S(2), top + ts.Height - S(4), ss.Width + S(4), ss.Height), TextFormatFlags.Left | TextFormatFlags.Top);

            hits.Clear();
            int x = left + Math.Max(ts.Width, ss.Width) + S(36);
            // On a narrow window the tabs close up (less room around each name) so HOST | JOIN and the help link
            // still fit on the right; they never shrink below their names.
            int pad = S(32);
            if (items.Count > 0)
            {
                int names = items.Sum(it => Draw.Measure(it.ToUpperInvariant(), ItemFont).Width) + S(2) * items.Count;
                int right = 0;
                if (modes != null && modes.Length > 1) right += (modes.Max(m => Draw.Measure(m, ModeFont).Width) + S(26)) * modes.Length + S(10);
                if (!string.IsNullOrEmpty(help)) right += Draw.Measure(help, ItemFont).Width + S(16) + S(8);
                int room = Width - S(26) - right - x;
                if (names + pad * items.Count > room) pad = Math.Max(S(12), (room - names) / items.Count);
            }
            for (int i = 0; i < items.Count; i++)
            {
                var t = items[i].ToUpperInvariant();
                int w = Draw.Measure(t, ItemFont).Width + pad;
                var r = new Rectangle(x, 0, w, Height);
                hits.Add(r);
                bool sel = i == selected;
                if (i == hover && !sel) using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, r);
                Draw.Text(g, t, ItemFont, sel ? Theme.Bone : Theme.Muted, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (sel) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, r.X + pad * 10 / 32, Height - S(5), r.Width - pad * 20 / 32, S(4));
                if (sel && Focused && ShowFocusCues) Draw.FocusRing(g, Rectangle.Inflate(r, -S(4), -S(12)));
                x += w + S(2);
            }

            helpHit = Rectangle.Empty;
            int helpRoom = 0;
            if (!string.IsNullOrEmpty(help))
            {
                var hs = Draw.Measure(help, ItemFont);
                if (Width - x - S(26) >= hs.Width + S(16))
                {
                    helpHit = new Rectangle(Width - S(26) - hs.Width - S(16), 0, hs.Width + S(16), Height);
                    if (helpHover) using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, helpHit);
                    Draw.Text(g, help, ItemFont, helpHover ? Theme.Bone : Theme.Muted, helpHit, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    helpRoom = helpHit.Width + S(20);
                }
            }

            modeHits.Clear();
            int modeRoom = 0;
            if (modes != null && modes.Length > 1)
            {
                int seg = modes.Max(m => Draw.Measure(m, ModeFont).Width) + S(26), h = S(30);
                int right = helpHit.IsEmpty ? Width - S(26) : helpHit.Left - S(8);
                int mx = right - seg * modes.Length, my = (Height - h) / 2;
                if (mx >= x + S(10))
                {
                    for (int i = 0; i < modes.Length; i++)
                    {
                        var r = new Rectangle(mx + i * seg, my, seg, h);
                        modeHits.Add(r);
                        bool lit = i == mode;
                        if (lit || i == modeHover) using (var b = new SolidBrush(lit ? Theme.Bone : Theme.Panel)) g.FillRectangle(b, r);
                        Draw.Text(g, modes[i], ModeFont, lit ? Theme.Ground : i == modeHover ? Theme.Bone : Theme.Muted, r,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                    Draw.Border(g, new Rectangle(mx, my, seg * modes.Length, h), Theme.Line);
                    modeRoom = seg * modes.Length + S(18);
                }
            }
            // the layout check: HOST | JOIN must always be on screen
            if (Audit.On && modes != null && modes.Length > 1 && modeHits.Count == 0) lock (Audit.Issues) Audit.Issues.Add("CLIPPED: the top bar has no room for " + string.Join(" | ", modes) + $" at width {Width}");

            // The status repeats what the page shows, so on a narrow window it gives way instead of clipping.
            int roomAll = Width - x - S(46) - helpRoom - modeRoom;
            bool hasNotice = !string.IsNullOrEmpty(notice);
            int shift = hasNotice ? S(9) : 0;
            if (!string.IsNullOrEmpty(status))
            {
                var st = status.ToUpperInvariant();
                var sf = Theme.BodyBold(8.5f);
                if (roomAll >= Draw.Measure(st, sf).Width)
                    Draw.Text(g, st, sf, statusColor, new Rectangle(x, -shift, roomAll, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            noticeHit = Rectangle.Empty;
            if (hasNotice)
            {
                var nf = Theme.BodyBold(8.5f);
                var size = Draw.Measure(notice, nf);
                if (roomAll >= size.Width)
                {
                    noticeHit = new Rectangle(x + roomAll - size.Width, Height / 2 + S(3), size.Width, size.Height);
                    using (var f = new Font(nf, FontStyle.Underline))
                        Draw.Text(g, notice, f, Theme.Amber, noticeHit, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = hits.FindIndex(r => r.Contains(e.Location));
            bool onHelp = helpHit.Contains(e.Location);
            int m = modeHits.FindIndex(r => r.Contains(e.Location));
            if (m == mode) m = -1;
            Cursor = h >= 0 || onHelp || m >= 0 || noticeHit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (h != hover || onHelp != helpHover || m != modeHover) { hover = h; helpHover = onHelp; modeHover = m; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; helpHover = false; modeHover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = hits.FindIndex(r => r.Contains(e.Location));
            if (h >= 0) { Focus(); Selected = h; }
            else if (noticeHit.Contains(e.Location)) NoticeClicked?.Invoke(this, EventArgs.Empty);
            else if (helpHit.Contains(e.Location)) HelpClicked?.Invoke(this, EventArgs.Empty);
            else
            {
                int m = modeHits.FindIndex(r => r.Contains(e.Location));
                if (m >= 0 && m != mode) ModeClicked?.Invoke(this, m);
            }
            base.OnMouseClick(e);
        }

        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Right) Selected = (selected + 1) % items.Count;
            else if (e.KeyCode == Keys.Left) Selected = (selected + items.Count - 1) % items.Count;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // ====================================================================== game pieces

    /// <summary>Who has a survivor: Spare is a character not in use (more characters than player slots).</summary>
    public enum SlotKind { Host, Player, Bot, Empty, Spare }

    /// <summary>One survivor slot in the lobby roster.</summary>
    public class SurvivorCard : Control, Themed
    {
        public string Character { get; private set; }
        public int Slot { get; private set; }
        public SlotKind Kind { get; private set; }
        public string Who { get; private set; }
        bool hover;

        /// <summary>The card can be clicked (to play as this survivor): a hand cursor and a lighter frame on hover.</summary>
        public bool Clickable
        {
            get => clickable;
            set { clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; AccessibleRole = value ? AccessibleRole.PushButton : AccessibleRole.StaticText; Invalidate(); }
        }
        bool clickable;

        static Font NameFont => Theme.Display(14.5f);
        static Font SlotFont => Theme.BodyBold(7.5f);
        static Font WhoFont => Theme.BodySemi(10.5f);
        static Font TagFont => Theme.BodyBold(7f);

        public SurvivorCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Margin = P(0, 0, 12, 12);
            AccessibleRole = AccessibleRole.StaticText;
        }

        /// <summary>Smallest height that fits name, slot, who and tag without overlap.</summary>
        public static int MinHeight =>
            Draw.Measure("X", NameFont).Height + Draw.Measure("X", SlotFont).Height + Draw.Measure("X", WhoFont).Height + Draw.TagSize("X", TagFont).Height + S(30);

        public void Set(int slot, string character, SlotKind kind, string who)
        {
            if (Slot == slot && Character == character && Kind == kind && Who == who) return;
            Slot = slot; Character = character; Kind = kind; Who = who;
            AccessibleName = kind == SlotKind.Spare ? $"{character}: not used" : $"Slot {slot}, {character}: {(kind == SlotKind.Empty ? "empty" : kind == SlotKind.Bot ? "bot" : who)}";
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; if (clickable) Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; if (clickable) Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            bool empty = Kind == SlotKind.Empty || Kind == SlotKind.Spare, hot = clickable && hover;
            using (var b = new SolidBrush(hot ? Theme.Panel2 : empty ? Theme.Header : Theme.Panel)) g.FillRectangle(b, r);
            if (hot) Draw.Border(g, r, Theme.Muted);
            else if (empty) Draw.Dashed(g, r, Theme.Line);
            else Draw.Border(g, r, Theme.Line);

            int pad = S(14), inner = r.Width - pad * 2;
            var nh = Draw.Measure("X", NameFont).Height;
            var sh = Draw.Measure("X", SlotFont).Height;
            int y = S(8);
            Draw.Text(g, Character ?? "", NameFont, empty ? Theme.Muted : Theme.Bone, new Rectangle(pad, y, inner, nh), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            y += nh - S(4);
            if (Kind != SlotKind.Spare) Draw.Text(g, "SLOT " + Slot, SlotFont, Theme.Muted, new Rectangle(pad + S(1), y, inner, sh), TextFormatFlags.Left | TextFormatFlags.SingleLine);

            string who, tag; Color tagFill = Color.Empty, tagBorder = Color.Empty, tagText = Theme.Bone, bar = Color.Empty; bool dashed = false;
            switch (Kind)
            {
                case SlotKind.Host: who = Who ?? "You"; tag = "HOST"; tagFill = Theme.Red; tagText = Theme.BoneText; bar = Theme.Red; break;
                case SlotKind.Player: who = Who; tag = "PLAYER"; tagBorder = Theme.Bone; bar = Theme.Bone; break;
                case SlotKind.Bot: who = "Bot"; tag = "BOT · TAKE OVER"; tagFill = Theme.Line; bar = Theme.Bot; break;
                case SlotKind.Spare: who = "Not in this game"; tag = "NOT USED"; tagBorder = Theme.Line; tagText = Theme.Muted; dashed = true; break;
                default: who = "Open slot"; tag = "EMPTY"; tagBorder = Theme.Muted; tagText = Theme.Muted; dashed = true; break;
            }
            // Wide cards show the long bot label; narrow ones the short one.
            if (Kind == SlotKind.Bot && Draw.TagSize(tag, TagFont).Width > inner) tag = "BOT";
            var tsz = Draw.TagSize(tag, TagFont);
            int barH = S(4);
            var tagRect = new Rectangle(pad, r.Height - barH - S(8) - tsz.Height, Math.Min(tsz.Width, inner), tsz.Height);
            var wh = Draw.Measure("X", WhoFont).Height;
            Draw.Text(g, who ?? "", WhoFont, Kind == SlotKind.Bot || empty ? Theme.Muted : Theme.Bone,
                new Rectangle(pad, tagRect.Y - wh - S(2), inner, wh), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Draw.TagBox(g, tagRect, tag, TagFont, tagFill, tagBorder, tagText, dashed);
            if (bar != Color.Empty) using (var b = new SolidBrush(bar)) g.FillRectangle(b, 0, r.Height - barH, r.Width, barH);
        }
    }

    /// <summary>A big-number summary tile ("2 PROBLEMS").</summary>
    public class Tile : Control, Themed
    {
        int count;
        public Color Fill { get; set; } = Theme.Panel;
        public Color Border { get; set; } = Theme.Line;
        public Color NumberColor { get; set; } = Theme.Bone;
        public Color LabelColor { get; set; } = Theme.Muted;
        public int Count { get => count; set { count = value; AccessibleName = $"{value} {Text}"; Invalidate(); } }
        static Font NumFont => Theme.Display(22f);
        static Font LabelFont => Theme.BodyBold(8.5f);

        public Tile(string label)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Text = label;
            Height = Draw.Measure("0", NumFont).Height + S(12);
            Margin = P(0, 0, 10, 0);
            AccessibleRole = AccessibleRole.StaticText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            using (var b = new SolidBrush(Fill)) g.FillRectangle(b, ClientRectangle);
            if (Border != Color.Empty) Draw.Border(g, ClientRectangle, Border);
            var n = count.ToString();
            var ns = Draw.Measure(n, NumFont);
            Draw.Text(g, n, NumFont, NumberColor, new Rectangle(S(14), 0, ns.Width + S(4), Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int lx = S(22) + ns.Width;
            Draw.Text(g, Text.ToUpperInvariant(), LabelFont, LabelColor, new Rectangle(lx, S(4), Width - lx - S(8), Height - S(4)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>One hop in the "can your friends reach you" route.</summary>
    public class HopCard : Control, Themed
    {
        public string Step { get; set; }
        public string Value { get; set; }
        public string Detail { get; set; }
        public string Status { get; set; }
        public bool Good { get; set; } = true;
        public bool Pending { get; set; }

        static Font StepFont => Theme.BodyBold(8f);
        static Font ValueFont => Theme.Display(14f);
        static Font DetailFont => Theme.Body(9f);
        static Font TagFont => Theme.BodyBold(7f);

        public HopCard(string step)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Step = step;
            Height = PreferredHeight;
            Margin = P(0);
            AccessibleRole = AccessibleRole.StaticText;
        }

        public static int PreferredHeight =>
            Draw.Measure("X", StepFont).Height + Draw.Measure("X", ValueFont).Height + Draw.Measure("X", DetailFont).Height + Draw.TagSize("X", TagFont).Height + S(36);

        public void Set(string value, string detail, string status, bool good, bool pending = false)
        {
            Value = value; Detail = detail; Status = status; Good = good; Pending = pending;
            AccessibleName = $"{Step}: {value}. {detail}. {status}";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Pending ? Theme.Header : Theme.Panel)) g.FillRectangle(b, r);
            if (Pending) Draw.Dashed(g, r, Theme.Bot);
            else Draw.Border(g, r, Good ? Theme.Line : Theme.Red, Good ? 1 : S(2));
            int pad = S(14), inner = r.Width - pad - S(10), y = S(12);
            int h1 = Draw.Measure("X", StepFont).Height, h2 = Draw.Measure("X", ValueFont).Height, h3 = Draw.Measure("X", DetailFont).Height;
            Draw.Text(g, Step.ToUpperInvariant(), StepFont, Theme.Muted, new Rectangle(pad, y, inner, h1), TextFormatFlags.Left | TextFormatFlags.SingleLine);
            y += h1 + S(2);
            Draw.Text(g, Value ?? "…", ValueFont, Pending ? Theme.Muted : Theme.Bone, new Rectangle(pad, y, inner, h2), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            y += h2;
            Draw.Text(g, Detail ?? "", DetailFont, Theme.Muted, new Rectangle(pad, y, inner, h3), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Status))
            {
                var tsz = Draw.TagSize(Status.ToUpperInvariant(), TagFont);
                var tr = new Rectangle(pad, r.Height - tsz.Height - S(12), tsz.Width, tsz.Height);
                Draw.TagBox(g, tr, Status.ToUpperInvariant(), TagFont, Good ? Color.Empty : Theme.Red, Good ? Theme.Bone : Color.Empty, Good ? Theme.Bone : Theme.BoneText);
            }
        }
    }

    /// <summary>The line between two hops: solid when open, broken with a label when blocked.</summary>
    public class Connector : Control, Themed
    {
        public bool Open { get; set; } = true;
        public bool Unknown { get; set; }
        public string Label { get; set; }

        public Connector()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = P(0);
        }

        public void Set(bool open, bool unknown, string label) { Open = open; Unknown = unknown; Label = label; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            int y = Height / 2, w = S(3), d = S(8);
            if (Unknown)
                using (var p = new Pen(Theme.Bot, w) { DashStyle = DashStyle.Dash }) g.DrawLine(p, 0, y, Width, y);
            else if (Open)
                using (var p = new Pen(Theme.Bone, w)) g.DrawLine(p, 0, y, Width, y);
            else
            {
                using (var p = new Pen(Theme.Red, w))
                {
                    g.DrawLine(p, 0, y, Width / 2 - d - S(4), y);
                    g.DrawLine(p, Width / 2 + d + S(4), y, Width, y);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPolygon(p, new[] { new Point(Width / 2, y - d), new Point(Width / 2 + d, y), new Point(Width / 2, y + d), new Point(Width / 2 - d, y) });
                }
            }
            if (!string.IsNullOrEmpty(Label))
                Draw.Text(g, Label.ToUpperInvariant(), Theme.BodyBold(7f), Open && !Unknown ? Theme.Muted : Theme.Amber, new Rectangle(0, y + S(12), Width, Height - y - S(12)), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    // ====================================================================== list views

    /// <summary>Tag for a non-item heading row in a themed ListView ("CORE · REQUIRED").</summary>
    public sealed class SectionRow { }

    /// <summary>Shows one page at a time; NavBar drives it (replaces TabControl, whose frame can't be themed).</summary>
    public class PageHost : Panel, Themed
    {
        readonly List<Control> pages = new List<Control>();
        int selected = -1;
        public event EventHandler SelectedIndexChanged;

        public PageHost()
        {
            BackColor = Theme.Ground;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void Add(Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = pages.Count == 0;
            pages.Add(page);
            Controls.Add(page);
            if (selected < 0) selected = 0;
        }

        public int SelectedIndex
        {
            get => selected;
            set
            {
                if (value == selected || value < 0 || value >= pages.Count) return;
                // the new page is drawn whole, in one pass, instead of box by box over the old one
                Draw.Batch(this, () =>
                {
                    pages[value].Visible = true;
                    pages[value].BringToFront();
                    pages[selected].Visible = false;
                });
                selected = value;
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public int Count => pages.Count;
        public Control Current => selected >= 0 ? pages[selected] : null;
    }

    /// <summary>Column titles drawn above a ListView (the native header can't be themed reliably).</summary>
    public class ListHeader : Control, Themed
    {
        readonly ListView lv;
        static Font HeaderFont => Theme.BodyBold(8f);

        public ListHeader(ListView list)
        {
            lv = list;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Draw.Measure("X", HeaderFont).Height + S(14);
            lv.ColumnWidthChanged += (s, e) => Invalidate();
            lv.ClientSizeChanged += (s, e) => Invalidate();
            AccessibleRole = AccessibleRole.ColumnHeader;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            g.Clear(Theme.Panel2);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            int x = 0;
            foreach (ColumnHeader c in lv.Columns)
            {
                int indent = x == 0 && lv.CheckBoxes ? S(34) : S(8);
                Draw.Text(g, c.Text.ToUpperInvariant(), HeaderFont, Theme.Muted, new Rectangle(x + indent, 0, c.Width - indent - S(2), Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                x += c.Width;
            }
        }
    }

    /// <summary>Owner-draws a ListView in the theme: dark header, themed checkboxes, per-row tints.</summary>
    public static class DarkList
    {
        /// <summary>The list plus its themed header strip, ready to drop into a container.</summary>
        public static Control WithHeader(ListView lv)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = P(0) };
            lv.Dock = DockStyle.Fill;
            host.Controls.Add(lv);
            host.Controls.Add(new ListHeader(lv));
            return host;
        }

        /// <summary>Column widths in logical pixels; scaled here so callers stay simple.</summary>
        public static void Columns(ListView lv, params (string title, int width)[] cols)
        {
            foreach (var (title, width) in cols) lv.Columns.Add(title, S(width));
        }

        public static void Apply(ListView lv)
        {
            lv.HeaderStyle = ColumnHeaderStyle.None;
            lv.OwnerDraw = true;
            lv.BackColor = Theme.Panel;
            lv.ForeColor = Theme.Bone;
            lv.BorderStyle = BorderStyle.None;
            lv.Font = Theme.Body(10f);
            int rowH = Draw.Measure("X", Theme.BodyBold(10f)).Height + S(12);
            lv.SmallImageList = new ImageList { ImageSize = new Size(1, rowH) }; // taller rows
            Theme.DarkScrollbars(lv);
            // drawn off-screen and shown whole: without it the rows appear on screen one by one as they're drawn
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lv, true);

            // The last column takes the spare width; when the columns don't fit, they all shrink in
            // proportion (cells ellipsize and carry tooltips) instead of scrolling sideways.
            int[] design = null;
            void Fit()
            {
                int n = lv.Columns.Count, avail = lv.ClientSize.Width;
                if (n == 0 || avail <= 0) return;
                if (design == null || design.Length != n) design = lv.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
                int total = design.Sum(), others = 0;
                var widths = new int[n];
                for (int i = 0; i < n - 1; i++)
                {
                    widths[i] = total <= avail ? design[i] : design[i] * avail / total;
                    others += widths[i];
                }
                widths[n - 1] = Math.Max(S(40), avail - others);
                if (Enumerable.Range(0, n).All(i => lv.Columns[i].Width == widths[i])) return;
                // every column at once: one width at a time repaints the whole list once per column
                lv.BeginUpdate();
                try { for (int i = 0; i < n; i++) if (lv.Columns[i].Width != widths[i]) lv.Columns[i].Width = widths[i]; }
                finally { lv.EndUpdate(); }
            }
            lv.ClientSizeChanged += (s, e) => Fit();
            lv.HandleCreated += (s, e) => Fit();
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += (s, e) =>
            {
                Audit.Context = lv;
                Audit.InList = true;
                var g = e.Graphics;
                var item = e.Item;
                try
                {
                    if (item.Tag is SectionRow)
                    {
                        using (var b = new SolidBrush(Theme.Ground)) g.FillRectangle(b, e.Bounds);
                        if (e.ColumnIndex == 0)
                            Draw.Text(g, item.Text.ToUpperInvariant(), Theme.Stencil(10f), Theme.Amber, new Rectangle(e.Bounds.X + S(8), e.Bounds.Y + S(4), e.Bounds.Width - S(8), e.Bounds.Height - S(4)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                        return;
                    }
                    bool sel = item.Selected;
                    var bg = sel ? Theme.Selection : item.BackColor == lv.BackColor || item.BackColor == Color.Empty ? Theme.Panel : item.BackColor;
                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
                    using (var p = new Pen(Theme.LineSoft)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    var r = e.Bounds;
                    if (e.ColumnIndex == 0 && lv.CheckBoxes)
                    {
                        int bs = S(16);
                        var box = new Rectangle(r.X + S(8), r.Y + (r.Height - bs) / 2, bs, bs);
                        if (item.Checked) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, box);
                        Draw.Border(g, box, item.Checked ? Theme.Red : Theme.Muted);
                        if (item.Checked)
                            using (var p = new Pen(Theme.BoneText, S(2)))
                                g.DrawLines(p, new[] { new Point(box.X + bs * 3 / 16, box.Y + bs / 2), new Point(box.X + bs * 7 / 16, box.Y + bs * 3 / 4), new Point(box.X + bs * 13 / 16, box.Y + bs / 4) });
                        r.X += S(34); r.Width -= S(34);
                    }
                    else { r.X += S(8); r.Width -= S(8); }
                    // Column 0 uses the item's color; other columns are muted unless the item is tinted (amber/red).
                    Color color;
                    if (e.ColumnIndex == 0) color = item.ForeColor;
                    else if (item.UseItemStyleForSubItems) color = item.ForeColor == Theme.Bone ? Theme.Muted : item.ForeColor;
                    else color = e.SubItem.ForeColor;
                    var font = item.Font != null && item.Font.Bold ? Theme.BodyBold(10f) : e.ColumnIndex == 0 ? Theme.BodySemi(10f) : Theme.Body(10f);
                    Draw.Text(g, e.SubItem.Text, font, color, r, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
                finally { Audit.InList = false; }
            };
        }
    }
}
