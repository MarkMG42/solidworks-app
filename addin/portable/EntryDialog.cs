using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace SWToolsHub.Portable
{
    internal sealed class EntryDialog : Form
    {
        public string Entry { get; private set; }
        private readonly Font fieldFont = UiTheme.CreateRegularFont();
        public EntryDialog(string name, Func<object> readMethods, string preferred)
        {
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            Font = UiTheme.CreateFont();
            Text = "Выбор модуля и инструмента: " + name;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 520); MinimumSize = SizeFromClientSize(new Size(560, 500));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var height in new[] { 88, 32, 42, 32, 42, 74, 54 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var info = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
            var module = new EntryComboBox { Dock = DockStyle.Fill, AccessibleName = "Модуль", Font = fieldFont };
            var procedure = new EntryComboBox { Dock = DockStyle.Fill, AccessibleName = "Инструмент / процедура", Font = fieldFont };
            var reload = new RoundedButton { Text = "Загрузить процедуры из SolidWorks", AutoSize = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var confirm = new RoundedButton { Text = "Запустить", AutoSize = true };
            var cancel = new RoundedButton { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
            var entries = new List<MacroEntry>();
            Action updateProcedures = () => {
                procedure.Items.Clear();
                procedure.Items.AddRange(entries.Where(e => string.Equals(e.Module, module.Text, StringComparison.OrdinalIgnoreCase)).Select(e => e.Procedure).Distinct().Cast<object>().ToArray());
                procedure.Text = procedure.Items.Count > 0 ? Convert.ToString(procedure.Items[0]) : "";
            };
            module.SelectedIndexChanged += (s, e) => updateProcedures();
            Action load = () => {
                entries.Clear(); string error = null;
                try { entries.AddRange(MacroEntry.FromCom(readMethods())); }
                catch (Exception ex) { error = ex.Message; }
                int discovered = entries.Count;
                var fallback = MacroEntry.Parse(preferred);
                if (fallback != null && !entries.Any(e => e.FullName == fallback.FullName)) entries.Add(fallback);
                module.Items.Clear();
                module.Items.AddRange(entries.Select(e => e.Module).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
                if (module.Items.Count > 0) module.SelectedIndex = 0;
                if (fallback != null) { module.Text = fallback.Module; updateProcedures(); procedure.Text = fallback.Procedure; }
                info.Text = discovered > 0 ? "Процедуры получены из SolidWorks. Выберите модуль и процедуру." :
                    "SolidWorks не вернул процедуры. " + (fallback != null ? "Подставлена известная/сохранённая точка входа." : "Можно ввести модуль и процедуру вручную.") + (error == null ? "" : " Ошибка: " + error);
            };
            reload.Click += (s, e) => load();
            confirm.Click += (s, e) => {
                var entry = MacroEntry.Parse(module.Text.Trim() + "." + procedure.Text.Trim());
                if (entry == null) { MessageBox.Show(this, "Выберите или введите модуль и процедуру."); return; }
                Entry = entry.FullName; DialogResult = DialogResult.OK;
            };
            layout.Controls.Add(info, 0, 0); layout.Controls.Add(new Label { Text = "Модуль", Dock = DockStyle.Fill }, 0, 1);
            layout.Controls.Add(module, 0, 2); layout.Controls.Add(new Label { Text = "Инструмент / процедура", Dock = DockStyle.Fill }, 0, 3);
            layout.Controls.Add(procedure, 0, 4); layout.Controls.Add(reload, 0, 5); layout.Controls.Add(buttons, 0, 6);
            foreach (var field in new[] { module, procedure }) { field.BackColor = UiTheme.Surface; field.ForeColor = ForeColor; field.FlatStyle = FlatStyle.Flat; }
            foreach (var button in new[] { reload, cancel }) UiTheme.StyleAction(button);
            UiTheme.StyleAction(confirm, true);
            Controls.Add(layout); AcceptButton = confirm; CancelButton = cancel;
            Shown += (s, e) => load();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) fieldFont.Dispose();
        }
    }
}
