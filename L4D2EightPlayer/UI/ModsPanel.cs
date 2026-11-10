using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The Join side's mod selector: every add-on in your game, ticked when it's on. Ticking one turns it on, unticking
    /// turns it off (in the game's add-on list, so it takes effect the next time the game starts). Each has its
    /// rating (the color key), what kind it is and why it got its color; a filter shows one kind at a time.
    /// Disable conflicting settles clashing pairs. Skins turned on or off here also update the skin picks, so the
    /// next launch keeps what you chose. The add-ons the app needs (HUD add-on, lobby mod) can't be turned off here.
    /// </summary>
    public sealed class ModsPanel : Panel
    {
        readonly Func<string> gameDir;
        readonly Func<Manifest> manifest;
        /// <summary>After add-ons were turned on or off here (the skin picker reads them again).</summary>
        public event EventHandler Changed;

        static readonly string[] Kinds = { "all", "campaigns", "gameplay", "skins", "other" };
        readonly ListView list;
        readonly Pills filter;
        readonly Label banner, footnote, detail;
        readonly GameButton conflictsButton;
        Clashes.Survey survey;
        Dictionary<string, Rated> rated = new Dictionary<string, Rated>(StringComparer.OrdinalIgnoreCase);
        List<Clashes.Clash> conflicts = new List<Clashes.Clash>();
        bool loading, filling;

        public ModsPanel(Func<string> gameDir, Func<Manifest> manifest)
        {
            this.gameDir = gameDir;
            this.manifest = manifest;
            BackColor = Theme.Ground;
            Dock = DockStyle.Fill;
            Theme.Buffer(this);

            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = P(26, 20, 26, 12), Margin = P(0) };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            g.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = P(0, 0, 0, 6) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.Controls.Add(new TapeLabel("Add-ons") { Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 14, 6) }, 0, 0);
            var title = new Label { Text = "PICK YOUR MODS", AutoSize = true, Font = Theme.Display(26f), ForeColor = Theme.Bone, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = P(0, 0, 16, 0) };
            head.Controls.Add(title, 1, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = P(0, 0, 0, 4) };
            conflictsButton = new GameButton("Disable conflicting", ButtonKind.Primary, 10f) { Margin = P(0, 0, 10, 0), Visible = false };
            conflictsButton.Click += async (s, e) =>
            {
                int n = ConflictsForm.Run(FindForm(), gameDir(), manifest());
                if (n > 0) Changed?.Invoke(this, EventArgs.Empty);
                if (n >= 0) await Reload();
            };
            new ToolTip().SetToolTip(conflictsButton, "Add-ons that are on and replace the same files with different copies (the game uses only one).\n" +
                "Lists each pair with a suggested one to turn off. Harmless overlaps (the same copy in both) are left alone.");
            var browse = new GameButton("Open Workshop", ButtonKind.Ghost, 10f) { Margin = P(0, 0, 10, 0) };
            browse.Click += (s, e) => Workshop.Open();
            new ToolTip().SetToolTip(browse, "Left 4 Dead 2's Steam Workshop: campaigns, skins and other add-ons. Subscribe, start the game once so it downloads them, then click Look again.");
            var refresh = new GameButton("Look again", ButtonKind.Ghost, 10f) { Margin = P(0) };
            refresh.Click += async (s, e) => await Reload();
            buttons.Controls.Add(conflictsButton);
            buttons.Controls.Add(browse);
            buttons.Controls.Add(refresh);
            head.Controls.Add(buttons, 3, 0);
            TitleRow.Wrap(head, buttons, title);
            g.Controls.Add(head, 0, 0);

            var sub = new Label
            {
                AutoSize = true, Font = Theme.Body(10f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 10),
                Text = "Every add-on in your game. Tick one to turn it on, untick it to turn it off: the change takes effect the next time Left 4 Dead 2 starts. " +
                       "Hover a row to see why it got its color. Skins have their own tab for picking one per survivor, weapon, item and infected.",
            };
            banner = new Label { AutoSize = true, Font = Theme.BodySemi(10.5f), ForeColor = Theme.Amber, Margin = P(0, 0, 0, 10), Visible = false };
            var subStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = P(0) };
            subStack.Controls.Add(sub);
            subStack.Controls.Add(banner);
            subStack.Controls.Add(RatingStyle.Key(P(0, 0, 0, 10)));
            g.Controls.Add(subStack, 0, 1);

            filter = new Pills(Kinds, new[] { "All", "Campaigns", "Gameplay", "Skins", "Other" }, 620) { Margin = P(0, 0, 0, 10) };
            filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += (s, e) => Fill();
            g.Controls.Add(filter, 0, 2);

            list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            list.Columns.Add("Rating", S(168));   // the tick boxes share this column
            list.Columns.Add("Add-on", S(400));
            list.Columns.Add("Kind", S(220));
            list.Columns.Add("Why", S(420));
            HoverTips.Attach(list);
            DarkList.Apply(list);
            list.ItemCheck += OnItemCheck;
            list.SelectedIndexChanged += (s, e) => ShowDetail();
            var border = new Card { Dock = DockStyle.Fill, Padding = P(1), Margin = P(0, 0, 0, 8) };
            border.Controls.Add(DarkList.WithHeader(list));
            g.Controls.Add(border, 0, 3);

            detail = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Bone, Margin = P(0, 0, 0, 4), UseMnemonic = false };
            g.Controls.Add(detail, 0, 4);
            footnote = new Label { AutoSize = true, Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 4, 0, 0) };
            g.Controls.Add(footnote, 0, 5);
            g.Resize += (s, e) =>
            {
                var max = new Size(Math.Max(S(300), g.ClientSize.Width - g.Padding.Horizontal), 0);
                sub.MaximumSize = banner.MaximumSize = footnote.MaximumSize = detail.MaximumSize = max;
            };
            Controls.Add(g);
        }

        bool GameOpen => PretendGameOpen || Launcher.GameRunning();
        /// <summary>For the screenshot check: the read-only look while the game runs.</summary>
        internal static bool PretendGameOpen;

        /// <summary>Reads the add-ons again (in the background) and redraws the list.</summary>
        public async Task Reload()
        {
            if (loading) return;
            loading = true;
            try
            {
                var dir = gameDir();
                var man = manifest();
                var sv = await Task.Run(() => Clashes.Look(dir, man));
                void Show()
                {
                    survey = sv;
                    rated = Ratings.Rate(sv);
                    try { conflicts = Clashes.Conflicts(sv, SkinPrefs.Load()); } catch { conflicts = new List<Clashes.Clash>(); }
                    conflictsButton.Text = $"Disable conflicting ({conflicts.Count})";
                    conflictsButton.Size = conflictsButton.GetPreferredSize(Size.Empty);
                    conflictsButton.Visible = conflicts.Count > 0;
                    Fill();
                }
                if (InvokeRequired) Invoke((Action)Show); else Show();
            }
            finally { loading = false; }
        }

        static string Kind(Clashes.Addon a)
        {
            var i = a.Info;
            if (i == null || i.Error != null) return "other";
            if (i.Campaign) return "campaigns";
            if (i.Mutation || i.Gameplay || i.WeaponScripts || i.Talker) return a.Targets.Count > 0 && !i.Mutation && !i.Talker && !(i.Gameplay && !i.IsSkin) ? "skins" : "gameplay";
            if (a.Targets.Count > 0) return "skins";
            return "other";
        }

        static string Describe(Clashes.Addon a)
        {
            var i = a.Info;
            if (i == null || i.Error != null) return "can't be read";
            var parts = new List<string>();
            if (i.Campaign) parts.Add("campaign");
            if (i.Mutation) parts.Add("game mode");
            if (i.Gameplay && !i.Campaign) parts.Add("scripts");
            if (i.WeaponScripts) parts.Add("weapon stats");
            if (i.Talker && !i.Campaign) parts.Add("dialogue");
            if (a.Targets.Count > 0) parts.Add("skin: " + string.Join(", ", a.Targets.Take(3).Select(t => t.Name)) + (a.Targets.Count > 3 ? $" +{a.Targets.Count - 3}" : ""));
            if (i.Ui) parts.Add("menus/HUD");
            if (i.Sounds && parts.Count == 0) parts.Add("sounds");
            return parts.Count == 0 ? "other" : string.Join(" · ", parts);
        }

        void Fill()
        {
            filling = true;
            try
            {
                var dir = gameDir();
                bool open = GameOpen;
                banner.Visible = dir == null || open;
                banner.Text = dir == null ? "Left 4 Dead 2 wasn't found, so there are no add-ons to show. Set the game folder first (Change game folder at the bottom)."
                            : "Left 4 Dead 2 is running: close it to turn add-ons on or off (it rewrites its add-on list when it exits).";
                list.Enabled = !open;
                var keep = (list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Clashes.Addon : null)?.Path;
                list.BeginUpdate();
                list.Items.Clear();
                var kind = Kinds[Math.Max(0, filter.SelectedIndex)];
                var shown = (survey?.Addons ?? new List<Clashes.Addon>())
                    .Where(a => kind == "all" || Kind(a) == kind)
                    .OrderBy(a => Array.IndexOf(Kinds, Kind(a))).ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var a in shown)
                {
                    rated.TryGetValue(a.Path, out var r);
                    var why = a.Protected ? "needed by the app: turn it off in the game's Add-ons menu if you must" : r?.Why ?? "";
                    var item = new ListViewItem(new[] { r == null ? "" : RatingStyle.Dot(r.Rating), a.Title, Describe(a), why })
                    {
                        Tag = a, Checked = a.Enabled, UseItemStyleForSubItems = false,
                        ToolTipText = a.Title + Environment.NewLine + Describe(a) + (r == null ? "" : Environment.NewLine + r.Name + ": " + r.Why),
                    };
                    item.ForeColor = r == null ? Theme.Muted : RatingStyle.Color(r.Rating);
                    item.SubItems[1].ForeColor = a.Enabled ? Theme.Bone : Theme.Muted;
                    item.SubItems[2].ForeColor = Theme.Muted;
                    item.SubItems[3].ForeColor = Theme.Muted;
                    list.Items.Add(item);
                    if (keep != null && string.Equals(a.Path, keep, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
                }
                list.EndUpdate();
                int all = survey?.Addons.Count ?? 0, on = survey?.Addons.Count(a => a.Enabled) ?? 0;
                footnote.Text = dir == null ? "" : $"{all} add-on{(all == 1 ? "" : "s")}, {on} on." + (shown.Count != all ? $" Showing {shown.Count}." : "") +
                    " The app's own copies of a server's mods are the 8p_<number> ones. The 8-Player HUD add-on and the lobby mod stay on.";
                ShowDetail();
            }
            finally { filling = false; }
        }

        void ShowDetail()
        {
            var a = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as Clashes.Addon : null;
            if (a == null) { detail.Text = "Click an add-on to see where it is; tick or untick it to turn it on or off."; detail.ForeColor = Theme.Muted; return; }
            rated.TryGetValue(a.Path, out var r);
            detail.Text = a.Title + (r == null ? "" : $" · {r.Name}: {r.Why}") + Environment.NewLine + a.Path;
            detail.ForeColor = Theme.Bone;
        }

        /// <summary>
        /// A tick or untick by the user. The list's window is made after the items are added, and Windows reports each
        /// tick again then: those aren't changes (the list isn't Created yet, or Fill is adding them).
        /// </summary>
        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling || !list.Created || e.Index < 0 || e.Index >= list.Items.Count) return;
            var item = list.Items[e.Index];
            if (!(item?.Tag is Clashes.Addon a)) return;
            bool on = e.NewValue == CheckState.Checked;
            if (on == a.Enabled) return;
            if (GameOpen) { e.NewValue = e.CurrentValue; MessageBox.Show(FindForm(), "Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.", "Mods", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (a.Protected && !on) { e.NewValue = e.CurrentValue; return; }
            try { Turn(gameDir(), a, on); }
            catch (Exception ex) { e.NewValue = e.CurrentValue; MessageBox.Show(FindForm(), ex.Message, "Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Changed?.Invoke(this, EventArgs.Empty);
            BeginInvoke((Action)(async () => await Reload()));   // ratings and conflicts change with it
        }

        /// <summary>
        /// Turns one add-on on or off in the game's list. A skin also updates the picks: turned on, it's the pick for what
        /// it skins (the next launch turns the other skins for those off); turned off, picks that named it go back to the
        /// game's own look, so a launch doesn't turn it on again. Campaign and script mods with skins aren't picks.
        /// </summary>
        public static void Turn(string gameDir, Clashes.Addon a, bool on)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.");
            Compat.SetAddonsEnabled(gameDir, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [a.Key] = on });
            a.Enabled = on;
            bool skin = a.Targets.Count > 0 && a.Info != null && !(a.Info.Campaign || a.Info.Mutation || a.Info.Scripts) && !a.Protected;
            if (!skin) return;
            var prefs = SkinPrefs.Load();
            if (on) foreach (var t in a.Targets) prefs.Picks[t.Id] = a.Key;
            else foreach (var p in prefs.Picks.Where(p => string.Equals(p.Value, a.Key, StringComparison.OrdinalIgnoreCase)).ToList()) prefs.Picks[p.Key] = "";
            prefs.Save();
        }

        /// <summary>For the screenshot check: one kind of add-on.</summary>
        internal void ShowKind(string kind) => filter.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, kind));
        /// <summary>For the screenshot check: select a row.</summary>
        internal void SelectFirst() { if (list.Items.Count > 0) { list.Items[0].Selected = true; list.Select(); } }
    }
}
