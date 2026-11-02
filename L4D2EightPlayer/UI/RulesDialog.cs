using System.Drawing;
using System.Windows.Forms;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The Custom rules for item or infected scaling: one "% more per survivor beyond 4" stepper per rule.
    /// </summary>
    sealed class RulesDialog : Form
    {
        public sealed class Rule
        {
            public string Title, Detail;
            public int Value, Max = 200;
            internal Stepper Stepper;
        }

        readonly Rule[] rules;

        public RulesDialog(string title, string intro, Rule[] rules)
        {
            this.rules = rules;
            Text = title;
            Font = Theme.Body(10f);
            BackColor = Theme.Ground;
            ForeColor = Theme.Bone;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = P(18);

            var stack = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var head = new Label { Text = intro, AutoSize = true, MaximumSize = new Size(S(460), 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 12) };
            stack.Controls.Add(head, 0, 0);
            stack.SetColumnSpan(head, 2);

            stack.Controls.Add(new Label { Text = "% PER SURVIVOR OVER 4", AutoSize = true, Font = Theme.Stencil(9f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 6), Anchor = AnchorStyles.Right }, 1, 1);

            int row = 2;
            foreach (var r in rules)
            {
                var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = P(0, 0, 18, 10), Anchor = AnchorStyles.Left };
                text.Controls.Add(new Label { Text = r.Title.ToUpperInvariant(), AutoSize = true, Font = Theme.Stencil(11f), ForeColor = Theme.Bone, Margin = P(0) });
                text.Controls.Add(new Label { Text = r.Detail, AutoSize = true, Font = Theme.Body(9f), ForeColor = Theme.Muted, Margin = P(0, 2, 0, 0) });
                // sized for three digits first so every row's stepper lines up
                r.Stepper = new Stepper(r.Title) { Minimum = 0, Maximum = 200, Step = 5, Margin = P(0, 0, 0, 10), Anchor = AnchorStyles.Right };
                r.Stepper.Maximum = r.Max;
                r.Stepper.Value = r.Value;
                stack.Controls.Add(text, 0, row);
                stack.Controls.Add(r.Stepper, 1, row);
                row++;
            }

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = P(0, 8, 0, 0) };
            var ok = new GameButton("Save", ButtonKind.Primary, 11f) { Margin = P(10, 0, 0, 0) };
            var cancel = new GameButton("Cancel", ButtonKind.Ghost, 11f) { Margin = P(0) };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            stack.Controls.Add(buttons, 0, row);
            stack.SetColumnSpan(buttons, 2);

            Controls.Add(stack);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>Copies the steppers back into the rules (after OK).</summary>
        public void Save()
        {
            foreach (var r in rules) r.Value = (int)r.Stepper.Value;
        }
    }
}
