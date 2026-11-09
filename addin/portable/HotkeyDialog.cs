using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    // Capture accelerators before menu/dialog processing as well as ordinary KeyDown input.
    internal sealed class HotkeyTextBox : TextBox
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Tab || key == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            OnKeyDown(new KeyEventArgs(keyData)); return true;
        }
    }
    internal sealed class HotkeyDialog : Form
    {
        private readonly Dictionary<string, int> draft;
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Label notice = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(10) };
        private readonly RoundedButton save = new RoundedButton { Text = "Сохранить", AutoSize = true };
        private readonly Font regular = UiTheme.CreateRegularFont();
        private readonly Func<Dictionary<string, int>, string> apply;
        private bool dirty;
        public HotkeyDialog(Macro[] macros, IDictionary<string, int> bindings, Func<Dictionary<string, int>, string> apply)
        {
            this.apply = apply;
            draft = new Dictionary<string, int>(bindings, StringComparer.OrdinalIgnoreCase);
            Text = "Горячие клавиши"; Font = UiTheme.CreateFont(); BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(720, 700); MinimumSize = SizeFromClientSize(new Size(580, 560));
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 42f, 58f, 42f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 86f, 38f, 58f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(new Label { Text = "Горячие клавиши", Dock = DockStyle.Fill }, 0, 0);
            layout.Controls.Add(new Label { Text = "Нажмите поле напротив макроса и введите сочетание клавиш.", Dock = DockStyle.Fill, Font = regular, ForeColor = UiTheme.Muted }, 0, 1);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.Selected, Margin = new Padding(0) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
            heading.Controls.Add(new Label { Text = "Макрос", Dock = DockStyle.Fill, Padding = new Padding(12, 6, 0, 0) }, 0, 0);
            heading.Controls.Add(new Label { Text = "Горячая клавиша", Dock = DockStyle.Fill, Padding = new Padding(12, 6, 0, 0) }, 1, 0);
            layout.Controls.Add(heading, 0, 2);
            var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Surface, Margin = new Padding(0), TabStop = false };
            var rows = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
            var byPath = macros.ToDictionary(m => m.id, StringComparer.OrdinalIgnoreCase);
            var paths = macros.Select(m => m.id).Concat(bindings.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int index = 0;
            foreach (string path in paths) {
                Macro macro; bool found = byPath.TryGetValue(path, out macro);
                string name = found ? macro.name : System.IO.Path.GetFileNameWithoutExtension(path);
                names[path] = name;
                var title = new Label { Text = name + "\n" + (found ? macro.category : "Нет в текущей библиотеке"), Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(12, 8, 4, 4), Margin = new Padding(0) };
                var keyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8, 14, 8, 8), Margin = new Padding(0) };
                keyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
                var input = new HotkeyTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = regular, BackColor = UiTheme.Background, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.None, AccessibleName = "Горячая клавиша: " + name, ShortcutsEnabled = false };
                int value; input.Text = draft.TryGetValue(path, out value) ? Hotkey.Format(value) : "";
                fields[path] = input;
                var fieldFrame = new RoundedPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(10, 6, 10, 4), Margin = new Padding(0) };
                input.Dock = DockStyle.Top; fieldFrame.Controls.Add(input);
                input.Enter += (s, e) => { fieldFrame.BorderColor = UiTheme.AccentInk; fieldFrame.Invalidate(); };
                input.Leave += (s, e) => { fieldFrame.BorderColor = UiTheme.Border; fieldFrame.Invalidate(); };
                // PreviewKeyDown prevents F-keys and Alt combinations becoming dialog commands.
                input.PreviewKeyDown += (s, e) => { if (e.KeyCode != Keys.Tab && e.KeyCode != Keys.Escape) e.IsInputKey = true; };
                input.KeyDown += (s, e) => {
                    if (e.KeyCode == Keys.Tab || e.KeyCode == Keys.Escape) return;
                    e.SuppressKeyPress = true; e.Handled = true;
                    if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
                    int next = (int)e.KeyData;
                    if (!Hotkey.IsValid(next) || (ModifierKeys & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.None) {
                        notice.Text = "Используйте Ctrl, Alt или Shift вместе с буквой, цифрой или F-клавишей.";
                        notice.ForeColor = UiTheme.AccentInk; return;
                    }
                    draft[path] = next; input.Text = Hotkey.Format(next); dirty = true; ValidateDraft();
                };
                var clear = new RoundedButton { Text = "×", Dock = DockStyle.Fill, Padding = new Padding(0), AccessibleName = "Удалить сочетание: " + name };
                StyleButton(clear); clear.Padding = new Padding(0); clear.Borderless = true; clear.BackColor = UiTheme.Surface;
                clear.Click += (s, e) => { draft.Remove(path); input.Text = ""; dirty = true; ValidateDraft(); input.Focus(); };
                keyLayout.Controls.Add(fieldFrame, 0, 0); keyLayout.Controls.Add(clear, 1, 0);
                rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 78)); rows.Controls.Add(title, 0, index); rows.Controls.Add(keyLayout, 1, index++);
                var tip = new ToolTip(); tip.SetToolTip(title, path); title.Disposed += (s, e) => tip.Dispose();
            }
            if (paths.Length == 0) { rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); rows.Controls.Add(new Label { Text = "В библиотеке пока нет макросов. Выберите папку и добавьте .swp.", AutoSize = true, Padding = new Padding(12) }, 0, 0); }
            viewport.Controls.Add(rows);
            Action fit = () => { int width = Math.Max(1, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                rows.MinimumSize = new Size(width, 0); rows.MaximumSize = new Size(width, 0); rows.Width = width; };
            viewport.SizeChanged += (s, e) => fit(); Shown += (s, e) => fit();
            layout.Controls.Add(viewport, 0, 3); layout.Controls.Add(notice, 0, 4);
            layout.Controls.Add(new Label { Text = "Сочетания работают, пока SW Tools Hub открыт.", Font = regular, ForeColor = UiTheme.Muted, Dock = DockStyle.Fill }, 0, 5);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new RoundedButton { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
            StyleButton(cancel); StyleButton(save); save.BackColor = UiTheme.AccentInk; save.ForeColor = Color.White; save.AccentHover = true;
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 6);
            save.Click += (s, e) => {
                if (!ValidateDraft()) return;
                string error = apply(new Dictionary<string, int>(draft, StringComparer.OrdinalIgnoreCase));
                if (error != null) { notice.Text = error; notice.ForeColor = UiTheme.AccentInk; return; }
                DialogResult = DialogResult.OK;
            };
            Controls.Add(layout); CancelButton = cancel; ValidateDraft();
        }
        private static void StyleButton(RoundedButton button)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.BackColor = UiTheme.Card; button.ForeColor = UiTheme.Text;
            button.Cursor = Cursors.Hand; button.Padding = new Padding(10, 4, 10, 4);
        }
        private bool ValidateDraft()
        {
            foreach (var field in fields.Values) { field.BackColor = UiTheme.Background; field.Parent.BackColor = UiTheme.Background; field.Parent.Invalidate(); }
            var duplicate = draft.GroupBy(p => p.Value).FirstOrDefault(g => g.Count() > 1);
            string error = Hotkey.Validate(draft);
            if (duplicate != null) {
                var paths = duplicate.Select(p => p.Key).ToArray();
                foreach (var path in paths) { fields[path].BackColor = Color.FromArgb(255, 240, 232); fields[path].Parent.BackColor = fields[path].BackColor; fields[path].Parent.Invalidate(); }
                error = Hotkey.Format(duplicate.Key) + " уже назначено: " + names[paths[0]] + ". Выберите другое сочетание. Сохранение заблокировано.";
            }
            notice.BackColor = error == null ? UiTheme.Surface : Color.FromArgb(255, 240, 232);
            notice.ForeColor = error == null ? UiTheme.Muted : UiTheme.AccentInk;
            notice.Font = regular;
            notice.Text = error ?? "Используйте Ctrl, Alt или Shift вместе с буквой, цифрой или F-клавишей. Крестик удаляет назначение.";
            save.Enabled = dirty && error == null; return error == null;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) regular.Dispose(); }
    }
}
