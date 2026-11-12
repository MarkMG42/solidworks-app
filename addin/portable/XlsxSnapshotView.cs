using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    internal sealed class XlsxSnapshotView : UserControl
    {
        private readonly ComboBox picker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DataGridView grid = new DataGridView {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            BackgroundColor = UiTheme.Background, GridColor = UiTheme.Border, VirtualMode = true
        };
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoSize = true };
        private List<SnapshotSheet> sheets = new List<SnapshotSheet>();
        public XlsxSnapshotView()
        {
            Dock = DockStyle.Fill; Padding = new Padding(16); BackColor = UiTheme.Background; AutoScaleMode = AutoScaleMode.Dpi;
            var viewerFont = UiTheme.CreateRegularFont(); Font = viewerFont; Disposed += (s, e) => viewerFont.Dispose();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var open = new Button { Text = "Открыть XLSX…", AutoSize = true }; open.Click += Open;
            layout.Controls.Add(open, 0, 0);
            layout.Controls.Add(new Label { Text = "Только чтение. Формулы не пересчитываются. Значения — сохранённые результаты Excel, они могут быть устаревшими. Даты показаны как исходные значения XLSX.", Dock = DockStyle.Fill }, 0, 1);
            layout.Controls.Add(picker, 0, 2); layout.Controls.Add(grid, 0, 3); layout.Controls.Add(status, 0, 4); Controls.Add(layout);
            foreach (var name in new[] { "Ячейка", "Сохранённое значение Excel", "Формула" }) grid.Columns.Add(name, name);
            grid.Columns[0].FillWeight = 24;
            grid.DefaultCellStyle.Padding = new Padding(10, 6, 10, 6); grid.RowTemplate.Height = 40;
            grid.CellValueNeeded += (s, e) => { if (picker.SelectedIndex >= 0) e.Value = sheets[picker.SelectedIndex].Cells[e.RowIndex][e.ColumnIndex]; };
            picker.SelectedIndexChanged += (s, e) => { grid.RowCount = 0; if (picker.SelectedIndex >= 0) grid.RowCount = sheets[picker.SelectedIndex].Cells.Count; grid.Invalidate(); };
            status.Text = "Выберите файл .xlsx";
        }
        private void Open(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    var loaded = XlsxSnapshot.Load(dialog.FileName);
                    grid.RowCount = 0; picker.Items.Clear(); sheets = loaded;
                    foreach (var sheet in sheets) picker.Items.Add(sheet.Name);
                    picker.SelectedIndex = 0; status.Text = Path.GetFileName(dialog.FileName);
                } catch (Exception ex) { MessageBox.Show(this, "Файл не открыт: " + ex.Message, "Просмотр XLSX", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}
