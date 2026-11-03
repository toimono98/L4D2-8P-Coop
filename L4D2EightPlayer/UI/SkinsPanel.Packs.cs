using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The Packs & collections view of the skin picker. Left: every add-on that skins more than one thing, then the
    /// Workshop collections added by link (and a card to add one). Right: what the one clicked skins, with Use and
    /// Turn off for the whole of it. A collection's list comes from Steam; its items are downloaded by the game.
    /// </summary>
    public sealed partial class SkinsPanel
    {
        readonly Pills viewSwitch;
        bool packsView;
        List<Packs.CollectionState> collections = new List<Packs.CollectionState>();
        /// <summary>What's selected on the Packs view: "pack:<key>", "col:<id>" or "add".</summary>
        string packSel;

        void FillPacks()
        {
            var packs = Packs.Of(inv);
            AddBreak("PACKS", packs.Count == 0 ? "No packs installed. A pack is one add-on that skins more than one thing (several survivors, a set of guns, a mix)."
                : "One add-on that skins several things. Using one turns it on for all of them, and the other skins for those things off.", top: false);
            foreach (var m in packs) list.Controls.Add(PackCardFor(m));
            AddBreak("WORKSHOP COLLECTIONS", collections.Count == 0 ? "Add a collection by its link to use all of its skins at once." : "Collections you added by link. Their items download when the game starts.", top: true);
            foreach (var c in collections) list.Controls.Add(CollectionCardFor(c));
            var add = new PackCard("Add a collection", "Paste a Workshop collection link", Theme.Muted, null, "+") { Selected = packSel == "add", Tag = "add" };
            add.Click += (s, e) => SelectPackItem("add");
            list.Controls.Add(add);

            if (packSel == null) packSel = packs.Count > 0 ? "pack:" + packs[0].Key : collections.Count > 0 ? "col:" + collections[0].Saved.Id : "add";
            footnote.Text = (packs.Count == 1 ? "1 pack" : $"{packs.Count} packs") + ", " + (collections.Count == 1 ? "1 collection" : $"{collections.Count} collections") + ". " +
                            "A collection's list is read from Steam (no sign-in). The 8-Player HUD add-on and the lobby mod are never turned off.";
        }

        PackCard PackCardFor(Skins.Mod m)
        {
            var others = m.Targets.Count(t => inv.ByTarget[t.Id].On.Any(o => o != m));
            string status = !m.Enabled ? "Off" : others > 0 ? $"On · {others} of {m.Targets.Count} also have another skin on" : "On";
            var card = new PackCard(m.Title, $"Skins {m.Targets.Count}: {Names(m.Targets, 3)} · {status}", m.Enabled ? (others > 0 ? Theme.Amber : Theme.Bone) : Theme.Muted, Thumb(m), m.Title.Substring(0, 1))
            { Selected = packSel == "pack:" + m.Key, Tag = "pack:" + m.Key, On = m.Enabled };
            card.Click += (s, e) => SelectPackItem("pack:" + m.Key);
            return card;
        }

        PackCard CollectionCardFor(Packs.CollectionState c)
        {
            var line = $"{c.Items.Count} items · {c.Installed.Count} downloaded · {c.On} on";
            thumbs.TryGetValue(Workshop.CollectionPicture(c.Saved.Id), out var pic);
            bool inUse = InUse(c), partly = !inUse && c.On > 0;
            var card = new PackCard(c.Saved.Title, line, inUse ? Theme.Bone : Theme.Muted, pic, c.Saved.Title.Substring(0, 1))
            {
                Selected = packSel == "col:" + c.Saved.Id, Tag = "col:" + c.Saved.Id,
                On = inUse ? true : partly ? (bool?)null : false,
                Badge = inUse ? "IN USE" : partly ? "PARTLY ON" : null, BadgeAmber = partly,
            };
            card.Click += (s, e) => SelectPackItem("col:" + c.Saved.Id);
            return card;
        }

        /// <summary>Using it would change nothing: every downloaded item on, and no other skin on for what its skins cover.</summary>
        bool InUse(Packs.CollectionState c) => c.Installed.Count > 0 && Packs.UseCollection(inv, c).Nothing;

        void SelectPackItem(string sel)
        {
            packSel = sel;
            foreach (var c in list.Controls.OfType<PackCard>()) c.Selected = (string)c.Tag == sel;   // no rebuild: the list keeps its scroll
            ShowOptions();
        }

        static string Names(IEnumerable<Skins.Target> ts, int max)
        {
            var l = ts.Select(t => t.Name).ToList();
            return string.Join(", ", l.Take(max)) + (l.Count > max ? $" +{l.Count - max} more" : "");
        }

        // ================================================================== the right column

        void ShowPackOptions()
        {
            if (inv == null) { options.Controls.Add(new Label { Text = "Looking for packs…", AutoSize = true, Font = Theme.Body(10.5f), ForeColor = Theme.Muted }); return; }
            if (packSel == "add") { ShowAddCollection(); return; }
            if (packSel?.StartsWith("col:") == true)
            {
                var c = collections.FirstOrDefault(x => x.Saved.Id == packSel.Substring(4));
                if (c != null) { ShowCollection(c); return; }
            }
            var m = packSel?.StartsWith("pack:") == true ? inv.Find(packSel.Substring(5)) : null;
            if (m != null && m.Pack) ShowPack(m);
            else ShowAddCollection();
        }

        Label Heading(string text) => new Label { Text = text.ToUpperInvariant(), AutoSize = true, MaximumSize = Sz(380, 0), Font = Theme.Display(17f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 4) };
        Label Line(string text, Color color, int top = 0) => new Label { Text = text, AutoSize = true, MaximumSize = Sz(380, 0), Font = Theme.Body(10f), ForeColor = color, Margin = P(0, top, 0, 6) };
        Label Small(string text) => new Label { Text = text.ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(8f), ForeColor = Theme.Muted, Margin = P(0, 8, 0, 2) };

        FlowLayoutPanel Buttons(params GameButton[] bs)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = P(0, 6, 0, 4), BackColor = Theme.Panel, MaximumSize = Sz(390, 0) };
            foreach (var b in bs) { b.Margin = P(0, 0, 8, 8); b.Enabled = b.Enabled && !GameOpen; row.Controls.Add(b); }
            return row;
        }

        void ShowPack(Skins.Mod m)
        {
            options.Controls.Add(Heading(m.Title));
            var others = m.Targets.Where(t => inv.ByTarget[t.Id].On.Any(o => o != m)).ToList();
            options.Controls.Add(Line(!m.Enabled ? "Off: the things it skins use other skins or the game's own look."
                : others.Count == 0 ? "On, and the only skin on for everything it skins."
                : $"On, but {Names(others, 4)} also {(others.Count == 1 ? "has" : "have")} another skin on, so the game may show that one there.", others.Count > 0 && m.Enabled ? Theme.Amber : Theme.Muted));

            foreach (var section in new[] { Skins.Survivors, Skins.Weapons, Skins.Items, Skins.Infected })
            {
                var ts = m.Targets.Where(t => t.Section == section).ToList();
                if (ts.Count == 0) continue;
                options.Controls.Add(Small(section));
                options.Controls.Add(Line(string.Join(", ", ts.Select(t => t.Name)), Theme.Bone));
            }
            foreach (var n in m.Notes) options.Controls.Add(Line(n.text, n.sev >= Severity.Warning ? Theme.Amber : Theme.Muted, 4));
            var f = findingFor?.Invoke(m.Path);
            if (f != null && f.Severity >= Severity.Warning) options.Controls.Add(Line("Compatibility check: " + (f.Details.FirstOrDefault() ?? f.Summary), Theme.Amber, 4));

            var use = new GameButton(m.Enabled && others.Count == 0 ? "In use" : "Use this pack", ButtonKind.Primary, 10.5f) { Enabled = !(m.Enabled && others.Count == 0) };
            use.Click += async (s, e) => await Run(Packs.UsePack(inv, m), turningOff: false);
            var off = new GameButton("Turn it off", ButtonKind.Outline, 10.5f) { Enabled = m.Enabled && !m.Locked };
            off.Click += async (s, e) => await Run(Packs.TurnOffPack(m), turningOff: true);
            options.Controls.Add(Buttons(use, off));
            if (m.Locked) options.Controls.Add(Line("Part of a campaign or script mod, so it isn't turned off here: use the Mods tab.", Theme.Muted));
        }

        void ShowCollection(Packs.CollectionState c)
        {
            options.Controls.Add(Heading(c.Saved.Title));
            options.Controls.Add(Line($"{c.Items.Count} items: {c.Installed.Count} downloaded ({c.On} on), {c.Missing} not downloaded yet.", Theme.Muted));
            bool inUse = InUse(c);
            var use = new GameButton(inUse ? "In use" : "Use this collection", ButtonKind.Primary, 10.5f) { Enabled = c.Installed.Count > 0 && !inUse };
            use.Click += async (s, e) => await Run(Packs.UseCollection(inv, c), turningOff: false);
            var off = new GameButton("Turn it off", ButtonKind.Outline, 10.5f) { Enabled = c.On > 0 };
            off.Click += async (s, e) => await Run(Packs.TurnOffCollection(c), turningOff: true);
            options.Controls.Add(Buttons(use, off));

            if (c.Missing > 0)
                options.Controls.Add(Line($"{c.Missing} item{(c.Missing == 1 ? " isn't" : "s aren't")} downloaded: open the collection on the Workshop, click Subscribe to all, then start the game once so it downloads them.", Theme.Amber, 4));
            var open = new GameButton("Open on the Workshop", ButtonKind.Ghost, 10f);
            open.Click += (s, e) => Workshop.OpenItem(c.Saved.Id);
            var update = new GameButton("Update the list", ButtonKind.Ghost, 10f);
            update.Click += async (s, e) => await AddCollection(c.Saved.Id, update, null);
            var remove = new GameButton("Remove", ButtonKind.Ghost, 10f);
            remove.Click += async (s, e) =>
            {
                prefs.Collections.RemoveAll(x => x.Id == c.Saved.Id);
                prefs.Save();
                packSel = null;
                await Reload();
            };
            options.Controls.Add(Buttons(open, update, remove));

            if (c.Installed.Count > 0)
            {
                options.Controls.Add(Small("Downloaded"));
                foreach (var i in c.Installed.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).Take(60))
                {
                    var what = i.Skin != null ? Names(i.Skin.Targets, 3) : "not a skin";
                    options.Controls.Add(new Label { Text = $"{(i.Enabled ? "ON " : "off")}   {i.Title}  ·  {what}", AutoSize = true, MaximumSize = Sz(380, 0), Font = Theme.Body(9.5f), ForeColor = i.Enabled ? Theme.Bone : Theme.Muted, Margin = P(0, 0, 0, 3) });
                }
                if (c.Installed.Count > 60) options.Controls.Add(Line($"…and {c.Installed.Count - 60} more.", Theme.Muted));
            }
            options.Controls.Add(Line("List read from Steam " + (DateTime.TryParse(c.Saved.Fetched, out var when) ? when.ToString("d MMM yyyy") : "earlier") + ".", Theme.Muted, 8));
        }

        void ShowAddCollection()
        {
            options.Controls.Add(Heading("Add a collection"));
            options.Controls.Add(Line("Paste the link of a Left 4 Dead 2 Workshop collection (on its Workshop page: Share, or the address bar). The app reads its list of items from Steam, then you can use them all at once.", Theme.Muted));
            var box = new TextBox { Width = S(380), Font = Theme.Body(10.5f), BackColor = Theme.Console, ForeColor = Theme.Bone, BorderStyle = BorderStyle.FixedSingle, Margin = P(0, 4, 0, 4) };
            options.Controls.Add(box);
            var status = Line("", Theme.Muted);
            status.Visible = false;   // until there's something to say
            var add = new GameButton("Add collection", ButtonKind.Primary, 10.5f);
            add.Click += async (s, e) => await AddCollection(box.Text, add, status);
            box.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await AddCollection(box.Text, add, status); } };
            var browse = new GameButton("Find collections", ButtonKind.Ghost, 10f);
            browse.Click += (s, e) => { try { System.Diagnostics.Process.Start("https://steamcommunity.com/workshop/browse/?appid=550&section=collections"); } catch { } };
            options.Controls.Add(Buttons(add, browse));
            options.Controls.Add(status);
            options.Controls.Add(Line("Nothing is downloaded by the app: subscribe to the collection on the Workshop, and the game downloads its items when it starts.", Theme.Muted, 6));
        }

        async Task AddCollection(string link, GameButton button, Label status)
        {
            button.Enabled = false;
            if (status != null) { status.ForeColor = Theme.Muted; status.Text = "Reading the collection from Steam…"; status.Visible = true; }
            try
            {
                var c = await Workshop.FetchCollection(link);
                prefs = SkinPrefs.Load();
                prefs.Collections.RemoveAll(x => x.Id == c.Id);
                prefs.Collections.Add(c);
                prefs.Save();
                packSel = "col:" + c.Id;
                await Reload();
            }
            catch (Exception ex)
            {
                var msg = ex is System.Net.Http.HttpRequestException || ex is System.IO.IOException || ex is TaskCanceledException
                    ? "Couldn't reach Steam. Check the connection and try again." : ex.Message;
                if (status != null) { status.ForeColor = Theme.Amber; status.Text = msg; }
                else MessageBox.Show(FindForm(), msg, "Skins", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                button.Enabled = true;
            }
        }

        /// <summary>Asks (listing what changes), then writes it and reloads.</summary>
        async Task Run(Packs.Plan plan, bool turningOff)
        {
            if (GameOpen) { MessageBox.Show(FindForm(), "Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.", "Skins", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!plan.Nothing)
                using (var d = new PlanDialog(plan, turningOff))
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
            try { Packs.Apply(gameDir(), plan, prefs); }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Skins", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!plan.Nothing) Changed?.Invoke(this, EventArgs.Empty);
            await Reload();
        }

        // ================================================================== for the screenshot check

        internal void ShowPacks(string sel = null)
        {
            if (sel != null) packSel = sel;
            viewSwitch.SelectedIndex = 1;
            Fill();
            ShowOptions();
        }

        internal Form PlanQuestion(string packTitle) => new PlanDialog(Packs.UsePack(inv, inv.Mods.First(m => m.Title == packTitle)), false);
    }

    /// <summary>A pack or collection on the left: picture, name, what it skins and whether it's on.</summary>
    sealed class PackCard : Control, Themed
    {
        readonly string title, line, initial;
        readonly Color lineColor;
        readonly Image thumb;
        bool hover, selected;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }
        /// <summary>On (red edge, ON badge), off (dimmed picture and name), or null for a card that's neither (Add a collection).</summary>
        public bool? On { get; set; }
        /// <summary>The badge's text when it isn't simply ON ("IN USE", "PARTLY ON"), and whether it's the amber kind.</summary>
        public string Badge { get; set; }
        public bool BadgeAmber { get; set; }

        static readonly System.Drawing.Imaging.ImageAttributes Dim = DimAttributes();
        static System.Drawing.Imaging.ImageAttributes DimAttributes()
        {
            var a = new System.Drawing.Imaging.ImageAttributes();
            a.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });   // a third as opaque: off at a glance
            return a;
        }

        public PackCard(string title, string line, Color lineColor, Image thumb, string initial)
        {
            this.title = title; this.line = line; this.lineColor = lineColor; this.thumb = thumb; this.initial = initial;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = Sz(422, 86);
            Margin = P(0, 0, 10, 10);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = title + ": " + line;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Audit.Context = this;
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(hover ? Theme.Panel2 : Theme.Panel)) g.FillRectangle(b, r);
            Draw.Border(g, r, selected ? Theme.Bone : Theme.Line, selected ? S(2) : 1);
            bool off = On == false;
            // on: a red edge like the picked skin on the right
            if (On == true) using (var b = new SolidBrush(Theme.Red)) g.FillRectangle(b, 0, 0, S(4), r.Height);
            int pad = S(10), pic = r.Height - pad * 2;
            var box = new Rectangle(pad + S(4), pad, pic, pic);
            if (thumb != null)
            {
                if (off) g.DrawImage(thumb, box, 0, 0, thumb.Width, thumb.Height, GraphicsUnit.Pixel, Dim);
                else g.DrawImage(thumb, box);
            }
            else using (var b = new SolidBrush(Theme.Header)) g.FillRectangle(b, box);
            Draw.Border(g, box, Theme.Line);
            if (thumb == null) Draw.Text(g, (initial ?? "").ToUpperInvariant(), Theme.Display(16f), Theme.Bot, box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = box.Right + pad, right = r.Width - pad;
            // the badge on the title line: ON, IN USE, PARTLY ON
            var badge = Badge ?? (On == true ? "ON" : null);
            if (badge != null)
            {
                var bf = Theme.BodyBold(7.5f);
                var sz = Draw.TagSize(badge, bf);
                right -= sz.Width;
                if (BadgeAmber) Draw.TagBox(g, new Rectangle(right, S(10), sz.Width, sz.Height), badge, bf, Color.Empty, Theme.Amber, Theme.Amber);
                else Draw.TagBox(g, new Rectangle(right, S(10), sz.Width, sz.Height), badge, bf, Theme.Red, Color.Empty, Theme.BoneText);
                right -= S(10);
            }
            int w = r.Width - x - pad;
            var nf = Theme.Stencil(11.5f);
            int nh = Draw.Measure("X", nf).Height;
            Draw.Text(g, title.ToUpperInvariant(), nf, off ? Theme.Muted : Theme.Bone, new Rectangle(x, S(10), Math.Max(0, right - x), nh), TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Draw.Text(g, line, Theme.Body(9.5f), lineColor, new Rectangle(x, S(10) + nh + S(2), w, r.Height - nh - S(14)), TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>"Use this pack?" / "Turn off this collection?": what gets turned on and off, before it's written.</summary>
    sealed class PlanDialog : Form
    {
        public PlanDialog(Packs.Plan p, bool turningOff)
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

            const int width = 480;
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = P(24, 20, 24, 16), BackColor = Theme.Panel, Location = Point.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(width)));
            t.Controls.Add(new Label { Text = (turningOff ? "Turn off " : "Use ") + p.What + "?", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Display(15f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 10) });
            void Section(string label, IEnumerable<string> names, Color color)
            {
                var l = names.ToList();
                if (l.Count == 0) return;
                t.Controls.Add(new Label { Text = $"{label} ({l.Count})".ToUpperInvariant(), AutoSize = true, Font = Theme.BodyBold(8f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 2) });
                var shown = string.Join(", ", l.Take(12)) + (l.Count > 12 ? $", and {l.Count - 12} more" : "");
                t.Controls.Add(new Label { Text = shown, AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(10.5f), ForeColor = color, Margin = P(0, 0, 0, 6) });
            }
            Section("Turned on", p.TurnOn.Values, Theme.Bone);
            Section("Turned off", p.TurnOff.Values, Theme.Bone);
            Section("Skins it sets", p.Picks.Keys.Select(x => x.Name), Theme.Bone);
            Section("Back to the game's look", p.BackToDefault.Select(x => x.Name), Theme.Bone);
            Section("Two of its own skins for the same thing (asked about when you launch)", p.Shared.Select(x => x.Name), Theme.Amber);
            Section("Stays on (part of a campaign or script mod; turn it off on the Mods tab)", p.StaysOn.Select(m => m.Title), Theme.Amber);
            t.Controls.Add(new Label { Text = "Takes effect the next time Left 4 Dead 2 starts. Turned-off add-ons stay installed.", AutoSize = true, MaximumSize = Sz(width, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 12) });

            var ok = new GameButton(turningOff ? "Turn it off" : "Use it", ButtonKind.Primary, 11f) { DialogResult = DialogResult.OK, Margin = P(10, 0, 0, 0) };
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
}
