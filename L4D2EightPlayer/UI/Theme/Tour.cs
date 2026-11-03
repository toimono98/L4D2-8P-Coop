using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI.Themes
{
    /// <summary>One stop of a <see cref="Tour"/>: what to spotlight, and what to say about it.</summary>
    public sealed class TourStep
    {
        public string Title, Body;
        /// <summary>The controls to spotlight (their bounds are joined); null or nothing visible centres the callout.</summary>
        public Func<Control[]> Targets;
        /// <summary>Runs before the step shows, e.g. to switch to the right tab.</summary>
        public Action Before;
    }

    /// <summary>
    /// A first-run walkthrough over the real window: the window is dimmed except for a red-ringed hole around
    /// the step's controls (which stay clickable), and a callout beside it explains them. Enter or Right
    /// goes on, Left goes back, Esc ends it. Two small borderless windows owned by the form's window (the
    /// form itself, or the app window it sits in), so they follow it, minimise with it and never show in
    /// the taskbar; both change in place from step to step, so nothing blinks. Hiding the form (switching
    /// to the app's other side) ends the tour.
    /// </summary>
    public sealed class Tour
    {
        readonly Form owner;
        Form top;   // the real window: the owner, or the app window the owner is embedded in
        readonly IList<TourStep> steps;
        readonly Shade shade = new Shade();
        Callout callout;
        int index = -1;
        Rectangle hole;   // in the owner's client coordinates; empty for a centred step
        bool closed;

        /// <summary>Raised once when the tour ends: finished, skipped, or its window closed.</summary>
        public event EventHandler Ended;
        public bool Running => index >= 0 && !closed;
        public int Count => steps.Count;

        public Tour(Form owner, IList<TourStep> steps)
        {
            this.owner = owner;
            this.steps = steps;
        }

        public void Start()
        {
            if (Running || steps.Count == 0) return;
            top = owner.TopLevelControl as Form ?? owner;
            top.LocationChanged += Follow;
            top.SizeChanged += Follow;
            owner.SizeChanged += Follow;
            top.FormClosed += OwnerClosed;
            owner.VisibleChanged += OwnerHidden;
            shade.Show(top);
            callout = new Callout();
            callout.Escaped += (s, e) => Close();
            callout.Stepped += (s, d) => { if (index + d >= 0 && index + d < steps.Count) Go(index + d); };
            callout.Back.Click += (s, e) => Go(index - 1);
            callout.Next.Click += (s, e) => { if (index == steps.Count - 1) Close(); else Go(index + 1); };
            callout.Skip.Click += (s, e) => Close();
            Go(0);
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            top.LocationChanged -= Follow;
            top.SizeChanged -= Follow;
            owner.SizeChanged -= Follow;
            top.FormClosed -= OwnerClosed;
            owner.VisibleChanged -= OwnerHidden;
            callout?.Close();
            shade.Close();
            if (!top.IsDisposed) top.Activate();
            Ended?.Invoke(this, EventArgs.Empty);
        }

        void OwnerClosed(object s, FormClosedEventArgs e) => Close();
        void OwnerHidden(object s, EventArgs e) { if (!owner.Visible) Close(); }
        void Follow(object s, EventArgs e) { if (Running && top.WindowState != FormWindowState.Minimized) Place(); }

        /// <summary>Shows step i (0-based).</summary>
        public void Go(int i)
        {
            if (closed || i < 0 || i >= steps.Count) return;
            index = i;
            var step = steps[i];
            step.Before?.Invoke();
            owner.PerformLayout();
            foreach (var t in Visible(step)) ScrollIntoView(t);
            callout.Fill(step, i, steps.Count);
            Place();
            if (!callout.Visible) callout.Show(top);
            callout.Activate();
            callout.Next.Focus();
        }

        static IEnumerable<Control> Visible(TourStep step) =>
            (step.Targets?.Invoke() ?? new Control[0]).Where(c => c != null && !c.IsDisposed && IsShown(c));

        static bool IsShown(Control c)
        {
            for (var p = c; p != null; p = p.Parent) if (!p.Visible) return false;
            return true;
        }

        static void ScrollIntoView(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p is ScrollableControl sc && sc.AutoScroll) sc.ScrollControlIntoView(c);
        }

        /// <summary>Where the step's controls are, in the owner's client area, clipped to what their parents show.</summary>
        Rectangle Hole(TourStep step)
        {
            var r = Rectangle.Empty;
            foreach (var c in Visible(step))
            {
                var b = c.Parent.RectangleToScreen(c.Bounds);
                for (var p = c.Parent; p != null && !(p is Form); p = p.Parent) b.Intersect(p.RectangleToScreen(p.ClientRectangle));
                if (b.Width <= 0 || b.Height <= 0) continue;
                r = r.IsEmpty ? b : Rectangle.Union(r, b);
            }
            if (r.IsEmpty) return r;
            r = owner.RectangleToClient(r);
            r.Inflate(S(8), S(8));
            r.Intersect(owner.ClientRectangle);
            return r;
        }

        /// <summary>The callout beside the hole: right, left, below, above, else tucked into a corner.</summary>
        static Point CalloutAt(Rectangle area, Rectangle hole, Size size)
        {
            int gap = S(18), m = S(14);
            var inner = Rectangle.Inflate(area, -m, -m);
            if (hole.IsEmpty)
                return new Point(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2);
            int ClampY(int y) => Math.Max(inner.Top, Math.Min(y, inner.Bottom - size.Height));
            int ClampX(int x) => Math.Max(inner.Left, Math.Min(x, inner.Right - size.Width));
            if (hole.Right + gap + size.Width <= inner.Right) return new Point(hole.Right + gap, ClampY(hole.Top));
            if (hole.Left - gap - size.Width >= inner.Left) return new Point(hole.Left - gap - size.Width, ClampY(hole.Top));
            if (hole.Bottom + gap + size.Height <= inner.Bottom) return new Point(ClampX(hole.Left), hole.Bottom + gap);
            if (hole.Top - gap - size.Height >= inner.Top) return new Point(ClampX(hole.Left), hole.Top - gap - size.Height);
            return new Point(inner.Right - size.Width, inner.Bottom - size.Height);
        }

        void Place()
        {
            hole = Hole(steps[index]);
            var client = owner.RectangleToScreen(owner.ClientRectangle);
            shade.Present(client, hole, S(3));
            var at = CalloutAt(new Rectangle(Point.Empty, client.Size), hole, callout.Wanted);
            at.Offset(client.Location);
            callout.Bounds = new Rectangle(at, callout.Wanted);   // size and place in one move
            callout.BringToFront();
        }

        /// <summary>The window as the tour shows it now (for the screenshot check): dimmed, ringed, with the callout.</summary>
        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(owner.Width, owner.Height);
            owner.DrawToBitmap(bmp, new Rectangle(Point.Empty, owner.Size));
            // where the bitmap's corner is on screen (a form embedded in the app window has no frame)
            var corner = owner.TopLevel ? owner.Location : owner.Parent.PointToScreen(owner.Location);
            var origin = owner.PointToScreen(Point.Empty);
            int ox = origin.X - corner.X, oy = origin.Y - corner.Y;   // the client area inside the frame
            using (var g = Graphics.FromImage(bmp))
            {
                using (var layer = Shade.Picture(owner.ClientSize, hole, S(3))) g.DrawImage(layer, ox, oy);
                using (var c = new Bitmap(callout.Width, callout.Height))
                {
                    callout.DrawToBitmap(c, new Rectangle(Point.Empty, callout.Size));
                    g.DrawImage(c, callout.Left - corner.X, callout.Top - corner.Y);
                }
            }
            return bmp;
        }

        // ------------------------------------------------------------------ windows

        /// <summary>
        /// The dim and the red ring as one per-pixel-alpha window: dark everywhere, a clear hole that clicks
        /// go straight through, ringed in red. UpdateLayeredWindow swaps its picture, size and place in one
        /// step, so moving the spotlight never shows a half-updated frame. It never takes focus.
        /// </summary>
        sealed class Shade : Form
        {
            const int Dim = 184;   // ~72% black

            public Shade()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Size = new Size(1, 1);
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= 0x00080000 /* WS_EX_LAYERED */ | 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */; return p; }
            }

            /// <summary>The layer itself: dim, a red ring and a clear hole (hole in the picture's coordinates).</summary>
            public static Bitmap Picture(Size size, Rectangle hole, int ring)
            {
                var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    using (var b = new SolidBrush(Color.FromArgb(Dim, Color.Black))) g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
                    if (!hole.IsEmpty)
                    {
                        using (var red = new SolidBrush(Theme.Red)) g.FillRectangle(red, Rectangle.Inflate(hole, ring, ring));
                        using (var clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear, hole);
                    }
                }
                return bmp;
            }

            /// <summary>Covers the screen rectangle with the layer, in one update.</summary>
            public void Present(Rectangle screen, Rectangle hole, int ring)
            {
                if (!IsHandleCreated || screen.Width <= 0 || screen.Height <= 0) return;
                using (var bmp = Picture(screen.Size, hole, ring))
                {
                    IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
                    IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
                    try
                    {
                        var dst = new POINT { X = screen.X, Y = screen.Y };
                        var size = new SIZE { cx = screen.Width, cy = screen.Height };
                        var src = new POINT();
                        var blend = new BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
                        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                    }
                    finally
                    {
                        SelectObject(memDc, old);
                        DeleteObject(hBmp);
                        DeleteDC(memDc);
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }
            }

            [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
            [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
            [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
            [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
            [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
            [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
            [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
            [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
            [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        }

        /// <summary>The callout: step count, title, what it's for, and Skip / Back / Next. One window, refilled for each step.</summary>
        sealed class Callout : Form
        {
            public readonly GameButton Back, Next, Skip;
            readonly Label count, title, body;
            readonly TableLayoutPanel table;
            readonly int width = S(400), bar = S(5), inner;
            public event EventHandler Escaped;
            public event EventHandler<int> Stepped;
            /// <summary>The size the current step needs (Place applies it with the position, in one move).</summary>
            public Size Wanted { get; private set; }

            public Callout()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Theme.Panel;
                ForeColor = Theme.Bone;
                Font = Theme.Body(10f);
                DoubleBuffered = true;
                inner = width - 2 - S(44);

                // inside the 1 px border, under the red bar that OnPaint draws
                table = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(22, 16, 22, 16), Location = new Point(1, bar), BackColor = Theme.Panel };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, inner));
                count = new Label { AutoSize = true, Font = Theme.BodyBold(8f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 6) };
                title = new Label { AutoSize = true, MaximumSize = new Size(inner, 0), Font = Theme.Display(16f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 8) };
                body = new Label { AutoSize = true, MaximumSize = new Size(inner, 0), Font = Theme.Body(10.5f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 16) };
                table.Controls.Add(count);
                table.Controls.Add(title);
                table.Controls.Add(body);

                Next = new GameButton("Next", ButtonKind.Primary, 11f) { Margin = P(8, 0, 0, 0) };
                Back = new GameButton("Back", ButtonKind.Ghost, 10f) { Margin = P(8, 0, 0, 0) };
                Skip = new GameButton("Skip tour", ButtonKind.Ghost, 10f) { Margin = P(0) };
                var buttons = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0), BackColor = Theme.Panel };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.Controls.Add(Skip, 0, 0);
                buttons.Controls.Add(Back, 2, 0);
                buttons.Controls.Add(Next, 3, 0);
                table.Controls.Add(buttons);
                Controls.Add(table);
                foreach (var c in new Control[] { table, buttons, count, title, body }) Theme.Buffer(c);
                AcceptButton = Next;
            }

            /// <summary>The step's text and buttons; Wanted becomes the size it needs.</summary>
            public void Fill(TourStep step, int i, int total)
            {
                bool last = i == total - 1;
                Draw.Batch(this, () =>
                {
                    Text = step.Title;
                    count.Text = Theme.Caps($"Step {i + 1} of {total}");
                    title.Text = step.Title.ToUpperInvariant();
                    body.Text = step.Body;
                    Next.Text = last ? "Done" : "Next";
                    Back.Visible = i > 0;
                    Skip.Visible = !last;
                    table.PerformLayout();
                });
                Wanted = new Size(width, bar + table.GetPreferredSize(new Size(width - 2, 0)).Height + 1);
                // each step starts like a fresh window: no keyboard focus ring until Tab is pressed
                if (IsHandleCreated) SendMessage(Handle, 0x0127 /* WM_CHANGEUISTATE */, (IntPtr)(1 /* UIS_SET */ | (0x1 /* UISF_HIDEFOCUS */ << 16)), IntPtr.Zero);
            }

            [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            protected override bool ShowWithoutActivation => false;
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= 0x80 /* WS_EX_TOOLWINDOW */; return p; }
            }

            // before the buttons see them (arrows would only move focus between them)
            protected override bool ProcessCmdKey(ref Message msg, Keys keys)
            {
                switch (keys)
                {
                    case Keys.Escape: Escaped?.Invoke(this, EventArgs.Empty); return true;
                    case Keys.Right: Stepped?.Invoke(this, 1); return true;
                    case Keys.Left: Stepped?.Invoke(this, -1); return true;
                }
                return base.ProcessCmdKey(ref msg, keys);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Draw.Border(e.Graphics, ClientRectangle, Theme.Bone, 1);
                using (var b = new SolidBrush(Theme.Red)) e.Graphics.FillRectangle(b, 0, 0, Width, S(5));
            }
        }
    }
}
