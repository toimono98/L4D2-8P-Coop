using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace L4D2EightPlayer.UI.Themes
{
    /// <summary>
    /// The "safe room" look shared by the host and join apps: near-black panels, bone text,
    /// blood red for the one action that matters, amber for warnings, condensed stencil type.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Ground = Hex("#141110");
        public static readonly Color Header = Hex("#1a1614");
        public static readonly Color Panel = Hex("#1f1b18");
        public static readonly Color Panel2 = Hex("#26211d");
        public static readonly Color Console = Hex("#0f0d0c");
        public static readonly Color Line = Hex("#3d352d");
        public static readonly Color LineSoft = Hex("#2b2521");
        public static readonly Color Bone = Hex("#ebe3d2");
        public static readonly Color BoneText = Hex("#f5efe3");
        public static readonly Color Muted = Hex("#b0a594");
        public static readonly Color Bot = Hex("#5a4f44");
        public static readonly Color Red = Hex("#a8231b");
        public static readonly Color RedHover = Hex("#bf2c22");
        public static readonly Color RedShadow = Hex("#5c120d");
        public static readonly Color Amber = Hex("#e0a23a");
        public static readonly Color ProblemBg = Hex("#2a1715");
        public static readonly Color WarningBg = Hex("#2a2312");
        public static readonly Color Selection = Hex("#3a312a");

        public static Color Hex(string s) => ColorTranslator.FromHtml(s);

        // ------------------------------------------------------------------ fonts

        static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        static readonly List<IntPtr> Pinned = new List<IntPtr>();
        static bool loaded;

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        /// <summary>
        /// The app icon with all its sizes (embedded as "app.ico"), so title bars and the taskbar pick the
        /// hand-tuned small ones instead of a shrunk 32 px image. Falls back to the exe's own icon.
        /// </summary>
        public static Icon AppIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }

        /// <summary>
        /// Loads the embedded fonts for both GDI+ (Graphics.DrawString) and GDI (TextRenderer,
        /// which most WinForms controls use). Missing fonts fall back to Windows' Bahnschrift.
        /// </summary>
        public static void LoadFonts()
        {
            if (loaded) return;
            loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    byte[] data;
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    var ptr = Marshal.AllocCoTaskMem(data.Length);   // must outlive the process' use of the font
                    Marshal.Copy(data, 0, ptr, data.Length);
                    Pinned.Add(ptr);
                    uint count = 0;
                    AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, ref count);
                    Fonts.AddMemoryFont(ptr, data.Length);
                }
                catch { /* keep going: the fallback font covers it */ }
            }
        }

        static FontFamily Family(string name, string fallback)
        {
            LoadFonts();
            return Fonts.Families.FirstOrDefault(f => f.Name == name)
                ?? FontFamily.Families.FirstOrDefault(f => f.Name == fallback)
                ?? FontFamily.GenericSansSerif;
        }

        static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>();

        static Font Make(string family, string fallback, float pt, FontStyle style)
        {
            var key = family + "|" + pt + "|" + style;
            if (Cache.TryGetValue(key, out var f)) return f;
            var fam = Family(family, fallback);
            if (!fam.IsStyleAvailable(style))
                style = new[] { FontStyle.Regular, FontStyle.Bold }.First(fam.IsStyleAvailable);
            // Points already follow the system DPI; Ui.Test adds the audit's simulated scale.
            return Cache[key] = new Font(fam, pt * Ui.Test, style, GraphicsUnit.Point);
        }

        /// <summary>Oswald Bold: headings, buttons, nav.</summary>
        public static Font Display(float pt) => Make("Oswald", "Bahnschrift", pt, FontStyle.Bold);
        /// <summary>Oswald Medium: secondary headings and labels.</summary>
        public static Font Stencil(float pt) => Make("Oswald Medium", "Bahnschrift", pt, FontStyle.Regular);
        public static Font Body(float pt) => Make("Barlow", "Segoe UI", pt, FontStyle.Regular);
        public static Font BodySemi(float pt) => Make("Barlow SemiBold", "Segoe UI Semibold", pt, FontStyle.Regular);
        public static Font BodyBold(float pt) => Make("Barlow", "Segoe UI", pt, FontStyle.Bold);
        /// <summary>Rubik Dirt: the distressed, spray-painted notes.</summary>
        public static Font Graffiti(float pt) => Make("Rubik Dirt", "Impact", pt, FontStyle.Regular);
        public static Font Mono(float pt) => Make("Consolas", "Courier New", pt, FontStyle.Regular);

        /// <summary>Small, widely tracked caps label ("CAMPAIGN", "RADIO LOG").</summary>
        public static string Caps(string s) => string.Join(" ", (s ?? "").ToUpperInvariant().ToCharArray());

        // ------------------------------------------------------------------ styling

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        /// <summary>Windows 10/11 dark scrollbars (and dark ListView group headers) for one control.</summary>
        public static void DarkScrollbars(Control c)
        {
            void Apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) Apply();
            c.HandleCreated += (s, e) => Apply();
        }

        /// <summary>
        /// Dark flat combo. Both styles are owner-drawn with the same item height, so an editable combo and a
        /// DropDownList built from the same font come out the same height; the native light arrow strip is
        /// painted over with a dark one.
        /// </summary>
        public static void StyleCombo(ComboBox cb)
        {
            if (cb.Tag as string == "themed") return;
            cb.Tag = "themed";
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = Console;
            cb.ForeColor = Bone;
            PaintOver.Attach(cb, PaintComboFrame);
            if (cb.DrawMode == DrawMode.Normal)
            {
                // A DropDownList ignores BackColor under visual styles; draw it ourselves.
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = Math.Max(cb.ItemHeight, TextRenderer.MeasureText("Xg", cb.Font).Height + Ui.S(6));
                cb.DrawItem += (s, e) =>
                {
                bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var b = new SolidBrush(hot ? Selection : Console)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, new Rectangle(e.Bounds.X + Ui.S(6), e.Bounds.Y, e.Bounds.Width - Ui.S(6), e.Bounds.Height), cb.Enabled ? Bone : Bot, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct COMBOBOXINFO { public int cbSize; public RECT rcItem, rcButton; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList; }
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

        static void PaintComboFrame(Graphics g, Control c)
        {
            var cb = (ComboBox)c;
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            if (!GetComboBoxInfo(cb.Handle, ref info)) return;
            var r = cb.ClientRectangle;
            var btn = Rectangle.FromLTRB(info.rcButton.Left, info.rcButton.Top, info.rcButton.Right, info.rcButton.Bottom);
            bool hot = cb.Enabled && (cb.Focused || cb.ContainsFocus || r.Contains(cb.PointToClient(Cursor.Position)));
            using (var b = new SolidBrush(Console)) g.FillRectangle(b, Rectangle.FromLTRB(btn.Left - 3, r.Top, r.Right, r.Bottom)); // the flat frame draws its divider just left of rcButton
            Chevron(g, btn, true, !cb.Enabled ? Bot : hot ? Bone : Muted);
            using (var inner = new Pen(Console)) g.DrawRectangle(inner, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            using (var edge = new Pen(hot ? Muted : Line)) g.DrawRectangle(edge, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>The spinner of a NumericUpDown: dark halves with small chevrons instead of light native buttons.</summary>
        static void PaintSpinner(Graphics g, Control buttons)
        {
            var r = buttons.ClientRectangle;
            var up = new Rectangle(r.X, r.Y, r.Width, r.Height / 2);
            var down = Rectangle.FromLTRB(r.X, up.Bottom, r.Right, r.Bottom);
            using (var b = new SolidBrush(Console)) g.FillRectangle(b, r);
            var col = buttons.Enabled ? Muted : Bot;
            Chevron(g, up, false, col);
            Chevron(g, down, true, col);
            using (var p = new Pen(Line)) g.DrawLine(p, r.X, r.Y, r.X, r.Bottom);
        }

        static void Chevron(Graphics g, Rectangle box, bool down, Color color)
        {
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
            float w = Math.Max(3f, Math.Min(box.Width / 2f - 2, Ui.S(4))), h = w / 2f;
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new Pen(color, Math.Max(1.2f, 1.5f * Ui.Factor)))
                g.DrawLines(p, down
                    ? new[] { new PointF(cx - w, cy - h), new PointF(cx, cy + h), new PointF(cx + w, cy - h) }
                    : new[] { new PointF(cx - w, cy + h), new PointF(cx, cy - h), new PointF(cx + w, cy + h) });
            g.SmoothingMode = old;
        }

        static readonly MethodInfo SetStyleMethod = typeof(Control).GetMethod("SetStyle", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Paints a stock panel or label through a back buffer, background included. Without it the box draws
        /// its (often see-through) background and then its content in two passes, which shows as a blink.
        /// </summary>
        public static void Buffer(Control c) =>
            SetStyleMethod.Invoke(c, new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });

        /// <summary>Walks a control tree and applies the theme to stock WinForms controls.</summary>
        public static void Style(Control root)
        {
            foreach (Control c in root.Controls)
            {
                // containers and labels paint in one pass (custom controls buffer themselves; native ones can't)
                if (!(c is Themed) && (c is Panel || c is Label || c is SplitContainer || c is ButtonBase)) Buffer(c);
                switch (c)
                {
                    case Themed _:
                        break; // custom controls paint themselves
                    case TextBox t:
                        // Borderless text areas blend into their card; boxed ones are dark "console" fields.
                        if (t.BorderStyle == BorderStyle.None) t.BackColor = Draw.SolidBack(t);
                        else { t.BackColor = t.ReadOnly && !t.Multiline ? Panel : Console; t.BorderStyle = BorderStyle.FixedSingle; }
                        t.ForeColor = Bone;
                        if (t.Multiline) DarkScrollbars(t);
                        break;
                    case ListView lv:
                        lv.BackColor = Panel;
                        lv.ForeColor = Bone;
                        lv.BorderStyle = BorderStyle.FixedSingle;
                        DarkScrollbars(lv);
                        break;
                    case ComboBox cb:
                        StyleCombo(cb);
                        break;
                    case NumericUpDown n:
                        n.BackColor = Console;
                        n.ForeColor = Bone;
                        n.BorderStyle = BorderStyle.FixedSingle;
                        if (n.Controls.Count > 0 && n.Tag as string != "themed") { n.Tag = "themed"; PaintOver.Attach(n.Controls[0], PaintSpinner); }
                        break;
                    case LinkLabel ll:
                        ll.LinkColor = Amber;
                        ll.ActiveLinkColor = Hex("#f2c374");
                        ll.VisitedLinkColor = Amber;
                        ll.BackColor = Color.Transparent;
                        break;
                    case CheckBox cbx:
                        cbx.ForeColor = Bone;
                        cbx.FlatStyle = FlatStyle.Flat;
                        cbx.FlatAppearance.BorderColor = Line;
                        cbx.FlatAppearance.CheckedBackColor = Red;
                        cbx.BackColor = Color.Transparent;
                        break;
                    case RadioButton rb:
                        rb.ForeColor = Bone;
                        rb.BackColor = Color.Transparent;
                        break;
                    case Label l:
                        if (l.ForeColor == SystemColors.ControlText || l.ForeColor == SystemColors.GrayText) l.ForeColor = Bone;
                        l.BackColor = Color.Transparent;
                        break;
                    case TabPage tp:
                        tp.BackColor = Ground;
                        tp.ForeColor = Bone;
                        break;
                    case SplitContainer sc:
                        sc.BackColor = Line;
                        sc.Panel1.BackColor = Ground;
                        sc.Panel2.BackColor = Header;
                        break;
                    case TableLayoutPanel _:
                    case FlowLayoutPanel _:
                        c.BackColor = Color.Transparent;
                        break;
                }
                Style(c);
            }
        }
    }

    /// <summary>Marker for controls that draw themselves in the theme (skipped by Theme.Style).</summary>
    public interface Themed { }

    /// <summary>
    /// Display scaling. Every pixel size in the UI goes through S() so the layout grows with Windows'
    /// display scaling (125%, 150%...) exactly as the text does. Test multiplies both for audits.
    /// </summary>
    public static class Ui
    {
        /// <summary>Extra scale for the layout audit (simulates a higher DPI on any machine).</summary>
        public static float Test = 1f;
        static float? system;

        public static float System
        {
            get
            {
                if (system == null)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) system = g.DpiX / 96f; }
                    catch { system = 1f; }
                }
                return system.Value;
            }
        }

        public static float Factor => System * Test;
        public static int S(int px) => px == 0 ? 0 : Math.Max(1, (int)Math.Round(px * Factor));
        public static Padding P(int all) => new Padding(S(all));
        public static Padding P(int left, int top, int right, int bottom) => new Padding(S(left), S(top), S(right), S(bottom));
        public static Size Sz(int w, int h) => new Size(S(w), S(h));
    }

    /// <summary>Collects layout problems (clipped text, controls spilling out of their parent) during an audit run.</summary>
    public static class Audit
    {
        public static bool On;
        [ThreadStatic] public static Control Context;
        [ThreadStatic] public static bool InList;
        public static readonly List<string> Issues = new List<string>();
        /// <summary>Text draws per control during an audit (finds controls that repaint endlessly).</summary>
        public static readonly Dictionary<string, int> Draws = new Dictionary<string, int>();
        public static void CountDraw(string text)
        {
            var key = (Context == null ? "?" : Context.GetType().Name + "('" + Short(Context.Text) + "')") + " text '" + Short(text) + "'";
            lock (Draws) Draws[key] = Draws.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        static string Short(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 48 ? s.Substring(0, 45) + "..." : s).Replace("\r", " ").Replace("\n", " ");

        public static string Describe(Control c)
        {
            if (c == null) return "?";
            var chain = new List<string>();
            for (var p = c; p != null && chain.Count < 4; p = p.Parent)
                chain.Add(p.GetType().Name + (string.IsNullOrEmpty(p.Text) || p is Form ? "" : $"('{Short(p.Text)}')"));
            return string.Join(" < ", chain);
        }

        static void Add(string s) { lock (Issues) if (!Issues.Contains(s)) Issues.Add(s); }

        public static void Text(string text, Size need, Size have) =>
            Add($"{(InList ? "LIST CELL" : "TEXT")} clipped: \"{Short(text)}\" needs {need.Width}x{need.Height}, box {have.Width}x{have.Height} · {Describe(Context)}");

        /// <summary>Checks every visible control: inside its parent, and its text fits.</summary>
        public static void Tree(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (!c.Visible) continue;
                var parent = c.Parent;
                bool scrolls = parent is ScrollableControl sc && sc.AutoScroll;
                if (!scrolls && !(parent is ListView))
                {
                    var area = parent.ClientRectangle;
                    if (c.Right > area.Right + 1 || c.Bottom > area.Bottom + 1 || c.Left < -1 || c.Top < -1)
                        Add($"OVERFLOW: {Describe(c)} at {c.Bounds} outside parent {area.Size}");
                    if ((c.Width <= 2 || c.Height <= 2) && !string.IsNullOrEmpty(c.Text))
                        Add($"COLLAPSED: {Describe(c)} is {c.Size}");
                }
                switch (c)
                {
                    case LinkLabel _:
                    case Label _:
                        if (!string.IsNullOrEmpty(c.Text))
                        {
                            var l = (Label)c;
                            // The label's own measurement (same wrapping rules it paints with).
                            var need = l.GetPreferredSize(new Size(l.AutoSize && l.MaximumSize.Width == 0 ? 0 : l.Width, 0));
                            if (need.Height > l.Height + 2 || (!l.AutoSize && need.Width > l.Width + 2 && l.MaximumSize.Width == 0))
                                Add($"TEXT clipped: \"{Short(l.Text)}\" needs {need.Width}x{need.Height}, label {l.Size} · {Describe(l)}");
                        }
                        break;
                    case ComboBox cb:
                        int widest = 0;
                        foreach (var item in cb.Items) widest = Math.Max(widest, TextRenderer.MeasureText(cb.GetItemText(item), cb.Font).Width);
                        if (widest + Ui.S(24) > Math.Max(cb.DropDownWidth, cb.Width))
                            Add($"DROPDOWN too narrow: {Describe(cb)} widest item {widest}px, list {Math.Max(cb.DropDownWidth, cb.Width)}px");
                        if (cb.Height < TextRenderer.MeasureText("Xg", cb.Font).Height)
                            Add($"COMBO too short: {Describe(cb)}");
                        break;
                    case TextBox tb when !tb.Multiline:
                        if (tb.Height + 3 < TextRenderer.MeasureText("Xg", tb.Font).Height)
                            Add($"TEXTBOX too short: {Describe(tb)} height {tb.Height}");
                        break;
                }
                if (!(c is ListView)) Tree(c);
            }
        }
    }
    /// <summary>Lets a stock control paint itself, then draws over it (used for the parts the theme can't recolour).</summary>
    sealed class PaintOver : NativeWindow
    {
        readonly Control control;
        readonly Action<Graphics, Control> draw;
        PaintOver(Control control, Action<Graphics, Control> draw) { this.control = control; this.draw = draw; }

        public static void Attach(Control c, Action<Graphics, Control> draw)
        {
            var p = new PaintOver(c, draw);
            if (c.IsHandleCreated) p.AssignHandle(c.Handle);
            c.HandleCreated += (s, e) => p.AssignHandle(c.Handle);
            c.HandleDestroyed += (s, e) => p.ReleaseHandle();
            // Hover and focus change the frame colour.
            c.MouseEnter += (s, e) => c.Invalidate();
            c.MouseLeave += (s, e) => c.Invalidate();
            c.GotFocus += (s, e) => c.Invalidate();
            c.LostFocus += (s, e) => c.Invalidate();
            c.EnabledChanged += (s, e) => c.Invalidate();
        }

        [DllImport("gdi32.dll")] static extern int SetBkColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);
        static IntPtr consoleBrush;

        protected override void WndProc(ref Message m)
        {
            // A disabled editable combo asks for "static" colours, which WinForms leaves light grey.
            if (m.Msg == 0x0138 /* WM_CTLCOLORSTATIC */ && control is ComboBox)
            {
                if (consoleBrush == IntPtr.Zero) consoleBrush = CreateSolidBrush(ColorTranslator.ToWin32(Theme.Console));
                SetBkColor(m.WParam, ColorTranslator.ToWin32(Theme.Console));
                SetTextColor(m.WParam, ColorTranslator.ToWin32(Theme.Bot));
                m.Result = consoleBrush;
                return;
            }
            base.WndProc(ref m);
            if (!control.IsHandleCreated) return;
            if (m.Msg == 0x000F /* WM_PAINT */)
                using (var g = Graphics.FromHwnd(m.HWnd)) draw(g, control);
            else if ((m.Msg == 0x0317 /* WM_PRINT */ || m.Msg == 0x0318 /* WM_PRINTCLIENT */) && m.WParam != IntPtr.Zero)
                using (var g = Graphics.FromHdc(m.WParam)) draw(g, control); // DrawToBitmap
        }
    }
}
