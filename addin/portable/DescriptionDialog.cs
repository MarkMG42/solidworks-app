using System;
using System.Drawing;
using System.Windows.Forms;
namespace SWToolsHub.Portable
{
    internal sealed class DescriptionDialog : Form
    {
        private readonly TextBox editor;
        public string Description => editor.Text.Trim();
        public DescriptionDialog(string name, string description)
        {
            Text = "Описание макроса"; Font = UiTheme.CreateRegularFont(); BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text; AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 300); MinimumSize = SizeFromClientSize(new Size(500, 260));
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.Controls.Add(new Label { Text = name, AutoEllipsis = true, Dock = DockStyle.Fill }, 0, 0);
            editor = new TextBox { Text = description ?? "", Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 1000, Dock = DockStyle.Fill, BackColor = UiTheme.Background, ForeColor = UiTheme.Text, AccessibleName = "Описание макроса", AcceptsReturn = true };
            layout.Controls.Add(editor, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var save = new RoundedButton { Text = "Сохранить", DialogResult = DialogResult.OK }; UiTheme.StyleAction(save, true);
            var cancel = new RoundedButton { Text = "Отмена", DialogResult = DialogResult.Cancel }; UiTheme.StyleAction(cancel);
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout); CancelButton = cancel;
        }
    }
}
