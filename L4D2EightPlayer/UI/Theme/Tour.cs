using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    /// A first-run walkthrough over the real window: the window is dimmed except for a hole around the
    /// step's controls (which stay clickable), and a callout beside it explains them. Enter or Right
    /// goes on, Left goes back, Esc ends it. Three small borderless windows owned by the form's window
    /// (the form itself, or the app window it sits in), so they follow it, minimise with it and never
    /// show in the taskbar. Hiding the form (switching to the app's other side) ends the tour.
    /// </summary>
    public sealed class Tour
    {
        readonly Form owner;
        Form top;   // the real window: the owner, or the app window the owner is embedded in
        readonly IList<TourStep> steps;
        readonly Shade shade = new Shade();
        readonly Shade ring = new Shade { BackColor = Theme.Red, Opacity = 1 };
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
            ring.Show(top);
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
            ring.Close();
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

            var old = callout;
            callout = new Callout(step, i, steps.Count);
            callout.Back.Click += (s, e) => Go(index - 1);
            callout.Next.Click += (s, e) => { if (index == steps.Count - 1) Close(); else Go(index + 1); };
            callout.Skip.Click += (s, e) => Close();
            callout.Escaped += (s, e) => Close();
            callout.Stepped += (s, d) => { if (index + d >= 0 && index + d < steps.Count) Go(index + d); };
            Place();
            callout.Show(top);
            old?.Close();
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
            var step = steps[index];
            hole = Hole(step);
            var client = owner.RectangleToScreen(owner.ClientRectangle);
            shade.Bounds = client;
            var dim = new Region(new Rectangle(Point.Empty, client.Size));
            if (!hole.IsEmpty) dim.Exclude(hole);
            var was = shade.Region;
            shade.Region = dim;
            was?.Dispose();

            ring.Visible = !hole.IsEmpty;
            if (!hole.IsEmpty)
            {
                int w = S(3);
                var outer = Rectangle.Inflate(hole, w, w);
                outer.Offset(client.Location);
                ring.Bounds = outer;
                var frame = new Region(new Rectangle(Point.Empty, outer.Size));
                frame.Exclude(new Rectangle(w, w, hole.Width, hole.Height));
                was = ring.Region;
                ring.Region = frame;
                was?.Dispose();
            }

            var at = CalloutAt(new Rectangle(Point.Empty, client.Size), hole, callout.Size);
            at.Offset(client.Location);
            callout.Location = at;
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
                var client = new Rectangle(ox, oy, owner.ClientSize.Width, owner.ClientSize.Height);
                bool spot = !hole.IsEmpty;   // (an offset empty rectangle is no longer IsEmpty)
                var h = hole; h.Offset(ox, oy);
                using (var dim = new Region(client))
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * shade.Opacity), Color.Black)))
                {
                    if (spot) dim.Exclude(h);
                    g.FillRegion(b, dim);
                }
                if (spot) Draw.Border(g, Rectangle.Inflate(h, S(3), S(3)), Theme.Red, S(3));
                using (var c = new Bitmap(callout.Width, callout.Height))
                {
                    callout.DrawToBitmap(c, new Rectangle(Point.Empty, callout.Size));
                    g.DrawImage(c, callout.Left - corner.X, callout.Top - corner.Y);
                }
            }
            return bmp;
        }

        // ------------------------------------------------------------------ windows

        /// <summary>A borderless, click-blocking layer that never takes focus (the dim, and the red ring).</summary>
        sealed class Shade : Form
        {
            public Shade()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                Opacity = 0.72;
                AutoScaleMode = AutoScaleMode.None;
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */; return p; }
            }
        }

        /// <summary>The callout: step count, title, what it's for, and Skip / Back / Next.</summary>
        sealed class Callout : Form
        {
            public readonly GameButton Back, Next, Skip;
            public event EventHandler Escaped;
            public event EventHandler<int> Stepped;

            public Callout(TourStep step, int i, int count)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Theme.Panel;
                ForeColor = Theme.Bone;
                Font = Theme.Body(10f);
                Text = step.Title;
                int width = S(400);

                // inside the 1 px border, under the red bar that OnPaint draws
                int bar = S(5), inner = width - 2 - S(44);
                var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(22, 16, 22, 16), Location = new Point(1, bar), BackColor = Theme.Panel };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, inner));
                t.Controls.Add(new Label { Text = Theme.Caps($"Step {i + 1} of {count}"), AutoSize = true, Font = Theme.BodyBold(8f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 6) });
                t.Controls.Add(new Label { Text = step.Title.ToUpperInvariant(), AutoSize = true, MaximumSize = new Size(inner, 0), Font = Theme.Display(16f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 8) });
                t.Controls.Add(new Label { Text = step.Body, AutoSize = true, MaximumSize = new Size(inner, 0), Font = Theme.Body(10.5f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 16) });

                bool last = i == count - 1;
                Next = new GameButton(last ? "Done" : "Next", ButtonKind.Primary, 11f) { Margin = P(8, 0, 0, 0) };
                Back = new GameButton("Back", ButtonKind.Ghost, 10f) { Margin = P(8, 0, 0, 0), Visible = i > 0 };
                Skip = new GameButton(last ? "Close" : "Skip tour", ButtonKind.Ghost, 10f) { Margin = P(0), Visible = !last };
                var buttons = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0), BackColor = Theme.Panel };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                buttons.Controls.Add(Skip, 0, 0);
                buttons.Controls.Add(Back, 2, 0);
                buttons.Controls.Add(Next, 3, 0);
                t.Controls.Add(buttons);
                Controls.Add(t);
                ClientSize = new Size(width, bar + t.GetPreferredSize(new Size(width - 2, 0)).Height + 1);
                AcceptButton = Next;
            }

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
