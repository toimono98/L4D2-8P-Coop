using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The skin picker, shared by the Host side's Skins tab and the Join side's Skins window. Left: every
    /// survivor, then each weapon, item and infected that has a skin installed, by section. Right: the skins for
    /// the one clicked, as cards; clicking one turns it on (and the others for that target off). A pack asks
    /// first, listing everything it changes. Picks are remembered (SkinPrefs) and put back each launch.
    /// </summary>
    public sealed partial class SkinsPanel : Panel
    {
        readonly Func<string> gameDir;
        readonly Func<Manifest> manifest;
        readonly Func<string, Finding> findingFor;
        /// <summary>After a pick changes add-ons (the Mods tab rescans).</summary>
        public event EventHandler Changed;

        Skins.Inventory inv;
        SkinPrefs prefs = SkinPrefs.Load();
        Skins.Target selected;
        readonly Dictionary<string, Image> thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly FlowLayoutPanel list;
        readonly TableLayoutPanel options;
        readonly Label banner, footnote;
        bool loading;

        /// <param name="findingFor">The Mods tab's scan finding for an add-on path, if there is one (Host side).</param>
        public SkinsPanel(Func<string> gameDir, Func<Manifest> manifest, Func<string, Finding> findingFor = null)
        {
            this.gameDir = gameDir;
            this.manifest = manifest;
            this.findingFor = findingFor;
            BackColor = Theme.Ground;
            Dock = DockStyle.Fill;
            Theme.Buffer(this);

            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = P(26, 20, 26, 12), Margin = P(0) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(440)));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = P(0, 0, 0, 6) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.Controls.Add(new TapeLabel("Wardrobe") { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 6) }, 0, 0);
            head.Controls.Add(new Label { Text = "PICK YOUR SKINS", AutoSize = true, Font = Theme.Display(26f), ForeColor = Theme.Bone, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 16, 0) }, 1, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = P(0, 0, 0, 4) };
            var browse = new GameButton("Open Workshop", ButtonKind.Ghost, 10f) { Margin = P(0, 0, 10, 0) };
            browse.Click += (s, e) => Workshop.Open();
            new ToolTip().SetToolTip(browse, "Left 4 Dead 2's Steam Workshop. Subscribe to a skin, then click Look again.");
            var refresh = new GameButton("Look again", ButtonKind.Ghost, 10f) { Margin = P(0) };
            refresh.Click += async (s, e) => await Reload();
            buttons.Controls.Add(browse);
            buttons.Controls.Add(refresh);
            head.Controls.Add(buttons, 3, 0);
            g.Controls.Add(head, 0, 0);
            g.SetColumnSpan(head, 2);

            var sub = new Label
            {
                AutoSize = true, Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 10),
                Text = "Skin add-ons you have, sorted by what they change. Click one to see your choices; picking a skin turns it on and the others for the same thing off. " +
                       "Skins show only on your screen. Servers that enforce pure files can block them; 8-player servers made with this app allow them.",
            };
            banner = new Label { AutoSize = true, Font = Theme.BodySemi(10.5f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 10), Visible = false };
            var subStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = P(0) };
            subStack.Controls.Add(sub);
            subStack.Controls.Add(banner);
            // one target at a time, or whole packs and Workshop collections
            viewSwitch = new Pills(new[] { "skins", "packs" }, new[] { "By what they change", "Packs & collections" }, 440) { Margin = P(0, 0, 0, 8) };
            viewSwitch.SelectedIndex = 0;
            viewSwitch.SelectedIndexChanged += (s, e) => { packsView = viewSwitch.SelectedIndex == 1; Fill(); ShowOptions(); };
            subStack.Controls.Add(viewSwitch);
            g.Controls.Add(subStack, 0, 1);
            g.SetColumnSpan(subStack, 2);

            list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, Margin = P(0, 0, 18, 0), Padding = P(0, 0, 6, 0) };
            Theme.DarkScrollbars(list);
            Theme.Buffer(list);
            list.Resize += (s, e) => SizeSectionBreaks();
            g.Controls.Add(list, 0, 2);

            var optionsCard = new Card { Dock = DockStyle.Fill, Padding = P(16, 14, 16, 14), Margin = P(0) };
            options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = P(0), BackColor = Theme.Panel };
            // scrolls down only: the cards follow the column's width
            options.HorizontalScroll.Maximum = 0;
            options.HorizontalScroll.Visible = false;
            options.AutoScroll = true;
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Theme.DarkScrollbars(options);
            Theme.Buffer(options);
            optionsCard.Controls.Add(options);
            g.Controls.Add(optionsCard, 1, 2);

            footnote = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 8, 0, 0) };
            g.Controls.Add(footnote, 0, 3);
            g.SetColumnSpan(footnote, 2);
            // the long lines wrap at the panel's width, so a narrow window doesn't push them past the edge
            g.Resize += (s, e) =>
            {
                var max = new Size(Math.Max(S(300), g.ClientSize.Width - g.Padding.Horizontal), 0);
                sub.MaximumSize = banner.MaximumSize = footnote.MaximumSize = max;
            };
            Controls.Add(g);
            ShowOptions();
        }

        /// <summary>Reads the add-ons again (in the background) and redraws the lists.</summary>
        public async Task Reload()
        {
            if (loading) return;
            loading = true;
            try
            {
                var dir = gameDir();
                var man = manifest();
                var (scanned, images, cols) = await Task.Run(() =>
                {
                    var i = Skins.Scan(dir, man);
                    var pics = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in i.Mods) if (!thumbs.ContainsKey(m.Path)) pics[m.Path] = Thumbnail(m);
                    var c = SkinPrefs.Load().Collections.Select(x => Packs.State(dir, i, man, x)).ToList();
                    foreach (var x in c) { var pic = Workshop.CollectionPicture(x.Saved.Id); if (!thumbs.ContainsKey(pic)) pics[pic] = Thumbnail(pic); }
                    return (i, pics, c);
                });
                // back on the window's thread before touching any control (a caller without a message loop's
                // context would otherwise get the rest of this on a pool thread)
                void Show()
                {
                    foreach (var kv in images) thumbs[kv.Key] = kv.Value;
                    inv = scanned;
                    collections = cols;
                    prefs = SkinPrefs.Load();
                    if (selected == null) selected = Skins.Targets[0];
                    SuspendLayout();
                    Fill();
                    ShowOptions();
                    ResumeLayout();
                }
                if (InvokeRequired) Invoke((Action)Show); else Show();
            }
            finally { loading = false; }
        }

        bool GameOpen => PretendGameOpen || Launcher.GameRunning();
        /// <summary>The list of targets (the tour spotlights it).</summary>
        internal Control TargetList => list;
        /// <summary>For the screenshot check: show the panel as it looks while the game runs.</summary>
        internal static bool PretendGameOpen;

        /// <summary>For the screenshot check: show one target's choices.</summary>
        internal void SelectTarget(string id)
        {
            selected = Skins.Get(id);
            foreach (var c in list.Controls.OfType<TargetCard>()) c.Selected = c.Target == selected;
            ShowOptions();
        }

        /// <summary>For the screenshot check: the question a pick would ask, without applying it.</summary>
        internal Form QuestionFor(string id, string modTitle) => new SkinsDialog(Skins.Plan(inv, Skins.Get(id), inv.Mods.First(m => m.Title == modTitle)), null);

        void Fill()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            bool none = inv == null || inv.Mods.Count == 0;
            var dir = gameDir();
            banner.Visible = dir == null || GameOpen;
            banner.Text = dir == null ? "Left 4 Dead 2 wasn't found, so there are no skins to show. Set the game folder first."
                        : "Left 4 Dead 2 is running: close it to change skins (it rewrites its add-on list when it exits).";
            if (packsView) { FillPacks(); list.ResumeLayout(); SizeSectionBreaks(); return; }

            foreach (var section in new[] { Skins.Survivors, Skins.Weapons, Skins.Items, Skins.Infected })
            {
                var targets = Skins.Targets.Where(t => t.Section == section).ToList();
                var shown = section == Skins.Survivors ? targets : targets.Where(t => inv != null && inv.ByTarget[t.Id].Mods.Count > 0).ToList();
                AddBreak(section.ToUpperInvariant(), shown.Count == 0 ? $"No {(section == Skins.Weapons ? "weapon" : section == Skins.Items ? "item" : section.ToLowerInvariant())} skins installed." : null, top: section != Skins.Survivors);
                if (section == Skins.Infected)
                    foreach (var subName in new[] { Skins.Special, Skins.Common })
                    {
                        var part = shown.Where(t => t.Sub == subName).ToList();
                        if (part.Count == 0) continue;
                        AddBreak(subName.ToUpperInvariant() + " INFECTED", null, top: false, small: true);
                        foreach (var t in part) list.Controls.Add(Card(t));
                    }
                else foreach (var t in shown) list.Controls.Add(Card(t));
            }
            list.ResumeLayout();
            SizeSectionBreaks();
            footnote.Text = inv == null ? "" : (none ? "No skin add-ons found. " : $"{inv.Mods.Count} skin add-on{(inv.Mods.Count == 1 ? "" : "s")}. ")
                + (inv.Others == 1 ? "1 other add-on isn't a skin (a map, sounds or scripts), so it's left alone. " : inv.Others > 1 ? $"{inv.Others} other add-ons aren't skins (maps, sounds, scripts), so they're left alone. " : "")
                + "The 8-Player HUD add-on and the lobby mod are never touched.";
        }

        /// <summary>A section header that takes a whole row in the flowing list.</summary>
        void AddBreak(string title, string note, bool top, bool small = false)
        {
            var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = P(0, top ? 16 : 6, 0, 4), Tag = "break" };
            box.Controls.Add(new Label { Text = title, AutoSize = true, Font = small ? Theme.BodyBold(8.5f) : Theme.Stencil(13f), ForeColor = small ? Theme.Muted : Theme.Bone, Margin = P(0) });
            if (note != null) box.Controls.Add(new Label { Text = note, AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 2, 0, 0) });
            list.Controls.Add(box);
            list.SetFlowBreak(box, true);
        }

        void SizeSectionBreaks()
        {
            foreach (Control c in list.Controls)
                if (c.Tag as string == "break")
                {
                    c.MinimumSize = new Size(list.ClientSize.Width - S(12), 0);
                    foreach (var l in c.Controls.OfType<Label>()) l.MaximumSize = new Size(Math.Max(S(200), list.ClientSize.Width - S(16)), 0);   // long notes wrap
                }
            // pack cards take a whole row: their names are long
            foreach (var c in list.Controls.OfType<PackCard>()) c.Width = Math.Max(S(220), list.ClientSize.Width - S(12));
        }

        TargetCard Card(Skins.Target t)
        {
            var state = inv?.ByTarget[t.Id];
            var card = new TargetCard(t, state, state?.Active != null ? Thumb(state.Active) : null) { Selected = t == selected };
            card.Click += (s, e) =>
            {
                selected = t;
                foreach (var c in list.Controls.OfType<TargetCard>()) c.Selected = c.Target == t;
                ShowOptions();
            };
            return card;
        }

        void ShowOptions()
        {
            options.SuspendLayout();
            options.Controls.Clear();
            options.RowStyles.Clear();
            if (packsView) { ShowPackOptions(); options.ResumeLayout(); return; }
            if (selected == null || inv == null)
            {
                options.Controls.Add(new Label { Text = "Looking for skins…", AutoSize = true, Font = Theme.Body(10.5f), ForeColor = Theme.Muted });
                options.ResumeLayout();
                return;
            }
            var state = inv.ByTarget[selected.Id];
            options.Controls.Add(new Label { Text = selected.Name.ToUpperInvariant(), AutoSize = true, Font = Theme.Display(19f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 2) });
            var what = state.Mods.Count == 0 ? "No skins installed for this one: it uses the game's own look."
                : state.Conflict ? $"{state.On.Count} skins are on for this, and the game uses only one of them (which isn't predictable). Pick the one you want."
                : $"{state.Mods.Count} skin{(state.Mods.Count == 1 ? "" : "s")} installed. Click one to use it.";
            options.Controls.Add(new Label { Text = what, AutoSize = true, MaximumSize = Sz(360, 0), Font = Theme.Body(10f), ForeColor = state.Conflict ? Theme.Amber : Theme.Muted, Margin = P(0, 0, 0, 10) });

            bool none = state.On.Count == 0;
            options.Controls.Add(Option(null, "Game default", "The look the game ships with.", none, null, Severity.Ok));
            foreach (var m in state.Mods)
            {
                var detail = m.Pack ? "Also skins: " + string.Join(", ", m.Targets.Where(t => t != selected).Select(t => t.Name)) : "Only " + selected.Name;
                if (m.Notes.Count > 0) detail += "\n" + string.Join("\n", m.Notes.Select(n => n.text));
                var f = findingFor?.Invoke(m.Path);
                var sev = f?.Severity ?? (m.Notes.Count > 0 ? m.Notes.Max(n => n.sev) : Severity.Ok);
                options.Controls.Add(Option(m, m.Title, detail, m.Enabled, Thumb(m), sev));
            }
            options.ResumeLayout();
        }

        OptionCard Option(Skins.Mod mod, string title, string detail, bool on, Image thumb, Severity sev)
        {
            var card = new OptionCard(title, detail, on, thumb, sev, mod?.Locked == true) { Dock = DockStyle.Top, Margin = P(0, 0, 0, 8), Enabled = !GameOpen };
            card.Click += async (s, e) => await Pick(mod);
            return card;
        }

        async Task Pick(Skins.Mod mod)
        {
            if (inv == null || selected == null) return;
            if (GameOpen) { MessageBox.Show(FindForm(), "Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.", "Skins", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var cs = Skins.Plan(inv, selected, mod);
            var finding = mod == null ? null : findingFor?.Invoke(mod.Path);
            bool risky = finding != null && finding.Severity >= Severity.Problem;
            bool ask = risky || (cs.Wider && !(mod != null && prefs.NoAsk.Contains(mod.Key)));
            bool dontAsk = false;
            if (ask)
                using (var d = new SkinsDialog(cs, finding))
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                    dontAsk = d.DontAskAgain;
                }
            try
            {
                Skins.Apply(inv, cs, prefs);
                if (dontAsk && mod != null && !prefs.NoAsk.Contains(mod.Key)) { prefs.NoAsk.Add(mod.Key); prefs.Save(); }
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Skins", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!cs.Nothing) Changed?.Invoke(this, EventArgs.Empty);
            await Reload();
        }

        Image Thumb(Skins.Mod m) => m != null && thumbs.TryGetValue(m.Path, out var i) ? i : null;

        /// <summary>The add-on's picture: Steam's Workshop thumbnail next to it, else addonimage.jpg inside, else none.</summary>
        static Image Thumbnail(Skins.Mod m)
        {
            try
            {
                var jpg = Path.ChangeExtension(m.Path, ".jpg");
                return File.Exists(jpg) ? Thumbnail(jpg) : Picture(Vpk.Open(m.Path).Read("addonimage.jpg", 4 * 1024 * 1024));
            }
            catch { return null; }
        }

        /// <summary>A picture file (a collection's), scaled for a card; null if it's missing or unreadable.</summary>
        static Image Thumbnail(string file)
        {
            try { return File.Exists(file) ? Picture(File.ReadAllBytes(file)) : null; }
            catch { return null; }
        }

        static Image Picture(byte[] bytes)
        {
            try
            {
                if (bytes == null) return null;
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img, S(72), S(72) * img.Height / Math.Max(1, img.Width));
            }
            catch { return null; }   // not a picture we can read (addonimage.vtf, a placeholder file)
        }
    }

    // ====================================================================== pieces

    /// <summary>One target on the left: its name, the skin in use (or Game default, or "2 on" in amber) and that skin's picture.</summary>
    sealed class TargetCard : Control, Themed
    {
        public readonly Skins.Target Target;
        readonly Skins.TargetState state;
        readonly Image thumb;
        bool hover, selected;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public TargetCard(Skins.Target target, Skins.TargetState state, Image thumb)
        {
            Target = target; this.state = state; this.thumb = thumb;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = Sz(206, 86);
            Margin = P(0, 0, 10, 10);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = target.Name + ": " + Status;
        }

        string Status => state == null ? "" : state.Conflict ? $"{state.On.Count} skins on · pick one" : state.Active?.Title ?? (state.Mods.Count > 0 ? "Game default" : "No skins");

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(hover ? Theme.Panel2 : Theme.Panel)) g.FillRectangle(b, r);
            Draw.Border(g, r, selected ? Theme.Bone : state?.Conflict == true ? Theme.Amber : Theme.Line, selected ? S(2) : 1);
            int pad = S(10), pic = r.Height - pad * 2, x = pad;
            var box = new Rectangle(x, pad, pic, pic);
            if (thumb != null) g.DrawImage(thumb, box);
            else using (var b = new SolidBrush(Theme.Header)) g.FillRectangle(b, box);
            Draw.Border(g, box, Theme.Line);
            if (thumb == null) Draw.Text(g, Target.Name.Substring(0, 1), Theme.Display(16f), Theme.Bot, box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += pic + S(10);
            int w = r.Width - x - pad;
            var nf = Theme.Stencil(11.5f);
            int nh = Draw.Measure("X", nf).Height;
            Draw.Text(g, Target.Name.ToUpperInvariant(), nf, Theme.Bone, new Rectangle(x, S(10), w, nh), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            var sf = Theme.Body(9.5f);
            Draw.Text(g, Status, sf, state?.Conflict == true ? Theme.Amber : state?.Active != null ? Theme.Bone : Theme.Muted,
                new Rectangle(x, S(10) + nh + S(2), w, r.Height - nh - S(14)), TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>One choice on the right: picture, title, what else it skins and any notes, and ON when it's in use.</summary>
    sealed class OptionCard : Control, Themed
    {
        readonly string title, detail;
        readonly bool on, locked;
        readonly Image thumb;
        readonly Severity sev;
        bool hover;

        public OptionCard(string title, string detail, bool on, Image thumb, Severity sev, bool locked)
        {
            this.title = title; this.detail = detail; this.on = on; this.thumb = thumb; this.sev = sev; this.locked = locked;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = title + (on ? ", on" : "");
            AccessibleDescription = detail;
            Height = Sz(0, 86).Height;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // tall enough for the wrapped detail lines
            int textW = Width - S(72) - S(34);
            if (textW <= 0) return;
            int need = S(10) + Draw.Measure("X", Theme.Stencil(11f)).Height + S(4)
                     + TextRenderer.MeasureText(detail ?? "", Theme.Body(9f), new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height + S(12);
            int h = Math.Max(S(86), need);
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(hover && Enabled ? Theme.Panel2 : Theme.Header)) g.FillRectangle(b, r);
            Draw.Border(g, r, on ? Theme.Bone : Theme.Line, on ? S(2) : 1);
            if (on) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, 0, 0, S(4), r.Height);
            int pad = S(10), pic = S(66);
            var box = new Rectangle(pad + S(4), pad, pic, pic);
            if (thumb != null) g.DrawImage(thumb, box.X, box.Y, pic, Math.Min(pic, thumb.Height * pic / Math.Max(1, thumb.Width)));
            else using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, box);
            Draw.Border(g, box, Theme.Line);
            int x = box.Right + S(12), w = r.Width - x - pad;
            var tf = Theme.Stencil(11f);
            int th = Draw.Measure("X", tf).Height;
            // the badges on the title line: ON, a scan warning, "stays on" for campaign mods
            var badges = new List<(string text, Color fill, Color border, Color fg)>();
            if (on) badges.Add(("ON", Theme.Red, Color.Empty, Theme.BoneText));
            if (sev >= Severity.Problem) badges.Add(("PROBLEM", Theme.Red, Color.Empty, Theme.BoneText));
            else if (sev == Severity.Warning) badges.Add(("WARNING", Theme.Amber, Color.Empty, Theme.Ground));
            if (locked) badges.Add(("STAYS ON", Color.Empty, Theme.Muted, Theme.Muted));
            int bx = r.Width - pad;
            var bfont = Theme.BodyBold(7f);
            foreach (var bd in badges)
            {
                var sz = Draw.TagSize(bd.text, bfont);
                bx -= sz.Width;
                Draw.TagBox(g, new Rectangle(bx, S(10), sz.Width, sz.Height), bd.text, bfont, bd.fill, bd.border, bd.fg);
                bx -= S(6);
            }
            Draw.Text(g, title.ToUpperInvariant(), tf, Enabled ? Theme.Bone : Theme.Muted, new Rectangle(x, S(9), Math.Max(0, bx - x), th), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Draw.Text(g, detail ?? "", Theme.Body(9f), Theme.Muted, new Rectangle(x, S(9) + th + S(4), w, r.Height - th - S(16)), TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>"Use this pack for all of these?": what a pick turns on and off, and what goes back to the game's look.</summary>
    sealed class SkinsDialog : Form
    {
        readonly CheckBox dontAsk;
        public bool DontAskAgain => dontAsk.Checked;

        public SkinsDialog(Skins.ChangeSet cs, Finding finding)
        {
            Text = "Skins";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Bone;
            Font = Theme.Body(10f);
            Icon = Theme.AppIcon();

            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(24, 20, 24, 16), BackColor = Theme.Panel, Location = Point.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(460)));
            string Join(IEnumerable<string> names) => string.Join(", ", names);
            var heading = cs.Pick == null ? $"Back to the game's look for {cs.Target.Name}?"
                : cs.Pick.Pack ? $"Use {cs.Pick.Title} for all of these?" : $"Use {cs.Pick.Title}?";
            t.Controls.Add(new Label { Text = heading, AutoSize = true, MaximumSize = Sz(460, 0), Font = Theme.Display(15f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 10) });
            void Line(string label, string text, Color color)
            {
                if (string.IsNullOrEmpty(text)) return;
                t.Controls.Add(new Label { Text = label.ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(8f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 2) });
                t.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = Sz(460, 0), Font = Theme.Body(10.5f), ForeColor = color, Margin = P(0, 0, 0, 6) });
            }
            if (cs.Pick != null && cs.Pick.Pack) Line("An add-on is all or nothing, so it skins", Join(cs.Sets.Select(x => x.Name)), Theme.Bone);
            Line("Turned off", Join(cs.TurnOff.Select(m => m.Title)), Theme.Bone);
            Line("Back to the game's look", Join(cs.BackToDefault.Select(x => x.Name)), Theme.Bone);
            Line("Stays on (part of a campaign or script mod; turn it off on the Mods tab)", Join(cs.StaysOn.Select(m => m.Title)), Theme.Amber);
            if (finding != null && finding.Severity >= Severity.Problem)
                Line("The compatibility check found a problem", finding.Details.FirstOrDefault() ?? finding.Summary, Theme.Amber);
            t.Controls.Add(new Label { Text = "Takes effect the next time Left 4 Dead 2 starts. You can change it here any time.", AutoSize = true, MaximumSize = Sz(460, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 10) });
            dontAsk = new GameCheck { Text = "Don't ask again for this add-on", AutoSize = true, Font = Theme.Body(10f), Visible = cs.Pick != null, Margin = P(0, 0, 0, 12) };
            t.Controls.Add(dontAsk);

            var ok = new GameButton(cs.Pick == null ? "Use the game's look" : cs.Pick.Pack ? "Use it for all of these" : "Use it", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0) };
            var cancel = new GameButton("Cancel", ButtonKind.Ghost, 10.5f) { DialogResult = DialogResult.Cancel, Margin = P(0) };
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0), BackColor = Theme.Panel };
            row.Controls.Add(ok);
            row.Controls.Add(cancel);
            t.Controls.Add(row);
            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = t.GetPreferredSize(Size.Empty);
            Theme.Style(this);
        }
    }

    /// <summary>The Join side's Skins window: the same picker, for the Join side's game folder.</summary>
    public sealed class SkinsForm : Form
    {
        public readonly SkinsPanel Panel;

        public SkinsForm(Func<string> gameDir)
        {
            Theme.LoadFonts();
            Text = "L4D2 8-Player Co-op · Skins";
            Icon = Theme.AppIcon();
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            Font = Theme.Body(10f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = Sz(900, 600);
            Size = Sz(1180, 820);
            MainForm.FitToScreen(this);
            Panel = new SkinsPanel(gameDir, Manifest.BuiltIn);
            Controls.Add(Panel);
            Controls.Add(new NavBar { Title = "SKINS", Subtitle = "L4D2 8-PLAYER CO-OP" });
            Theme.Style(this);
            Shown += async (s, e) => await Panel.Reload();
        }
    }
}
