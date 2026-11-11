using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SWToolsHub.Portable
{
    internal sealed class CalculationView : UserControl
    {
        private readonly CalculationEngine engine;
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
        private readonly Label message = new Label { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8), AutoEllipsis = true };
        private readonly Dictionary<CalculationSheet, DataGridView> grids = new Dictionary<CalculationSheet, DataGridView>();
        private bool painting;
        public static CalculationBook[] LoadBooks()
        {
            using (var stream = typeof(CalculationView).Assembly.GetManifestResourceStream("CalculationWorkbooks.json"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer { MaxJsonLength = 24000000, RecursionLimit = 256 }.Deserialize<CalculationBook[]>(reader.ReadToEnd());
        }
        public CalculationView(CalculationBook book)
        {
            engine = new CalculationEngine(book);
            Dock = DockStyle.Fill; BackColor = UiTheme.Background;
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(6), WrapContents = false, AutoScroll = true };
            var reset = new Button { Text = "Сбросить ввод", AutoSize = true, Height = 34 };
            var export = new Button { Text = "Сохранить таблицу…", AutoSize = true, Height = 34 };
            var saveInput = new Button { Text = "Сохранить ввод…", AutoSize = true, Height = 34 };
            var openInput = new Button { Text = "Открыть ввод…", AutoSize = true, Height = 34 };
            toolbar.Controls.Add(reset); toolbar.Controls.Add(saveInput); toolbar.Controls.Add(openInput); toolbar.Controls.Add(export);
            saveInput.Click += (s, e) => SessionFile(false);
            openInput.Click += (s, e) => SessionFile(true);
            toolbar.Controls.Add(new Label { Text = "Изменяйте светлые поля; результаты пересчитываются автоматически", AutoSize = true, Padding = new Padding(8) });
            Controls.Add(tabs); Controls.Add(message); Controls.Add(toolbar);
            foreach (var sheet in book.Sheets) {
                var page = new TabPage(sheet.Name + (sheet.ReferenceOnly ? " (справочно)" : "")) { BackColor = BackColor, Tag = sheet };
                tabs.TabPages.Add(page);
            }
            tabs.SelectedIndexChanged += (s, e) => ShowSheet();
            reset.Click += (s, e) => {
                if (MessageBox.Show(this, "Вернуть исходные значения всех листов этого расчёта?", "Сброс расчёта", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                engine.Reset(); foreach (var pair in grids) FillValues(pair.Key, pair.Value); ShowSheet();
            };
            export.Click += (s, e) => Export();
            ShowSheet();
        }
        private void ShowSheet()
        {
            if (tabs.SelectedTab == null) return;
            var sheet = (CalculationSheet)tabs.SelectedTab.Tag;
            DataGridView grid;
            if (!grids.TryGetValue(sheet, out grid)) {
                grid = CreateGrid(sheet); grids.Add(sheet, grid); tabs.SelectedTab.Controls.Add(grid);
            }
            FillValues(sheet, grid);
        }
        private DataGridView CreateGrid(CalculationSheet sheet)
        {
            var grid = new DataGridView {
                Dock = DockStyle.Fill, BackgroundColor = UiTheme.Background, BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
                AutoGenerateColumns = false, RowHeadersWidth = 50, MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EditMode = DataGridViewEditMode.EditOnEnter, GridColor = UiTheme.Border
            };
            grid.DefaultCellStyle.BackColor = Color.FromArgb(244, 242, 237);
            grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(251, 226, 202);
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            grid.RowTemplate.MinimumHeight = 32;
            int columns = sheet.Cells.Max(c => c.Column), rows = sheet.Cells.Max(c => c.Row);
            bool strength = engine.Book.Source.Contains("прочность");
            for (int col = 1; col <= columns; col++) {
                string address = CalculationEngine.Address(1, col);
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = address.TrimEnd('1'), HeaderText = address.TrimEnd('1'), SortMode = DataGridViewColumnSortMode.NotSortable,
                    Width = col == 1 ? 370 : col == 2 ? (strength ? 220 : 215) : col == 3 ? (strength ? 180 : 90) : 260 });
            }
            grid.Rows.Add(rows);
            for (int r = 0; r < rows; r++) {
                grid.Rows[r].HeaderCell.Value = (r + 1).ToString();
                foreach (DataGridViewCell cell in grid.Rows[r].Cells) cell.ReadOnly = true;
            }
            foreach (var cell in sheet.Cells) {
                var view = grid[cell.Column - 1, cell.Row - 1];
                if (cell.Editable && cell.Options != null && cell.Options.Length > 0) {
                    var combo = new DataGridViewComboBoxCell { DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat };
                    combo.Items.AddRange(cell.Options.Cast<object>().ToArray());
                    grid[cell.Column - 1, cell.Row - 1] = combo; view = combo;
                }
                view.Tag = cell; view.ReadOnly = !cell.Editable;
                if (cell.Editable) view.Style.BackColor = Color.White;
                view.ToolTipText = cell.Formula ?? (cell.Editable ? "Исходное значение · " + cell.Address : "");
            }
            grid.CellValidating += (s, e) => {
                if (painting || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var view = grid[e.ColumnIndex, e.RowIndex]; var cell = view.Tag as CalculationCell;
                if (cell == null || !cell.Editable) return;
                try { engine.SetInput(sheet.Name, cell.Address, e.FormattedValue); view.ErrorText = ""; }
                catch (CalculationError ex) { e.Cancel = true; view.ErrorText = ex.Message; message.Text = ex.Message; message.ForeColor = Color.Firebrick; }
            };
            grid.CellEndEdit += (s, e) => { if (!painting) FillValues(sheet, grid); };
            grid.CurrentCellDirtyStateChanged += (s, e) => {
                if (!painting && grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell) {
                    if (grid.EndEdit()) FillValues(sheet, grid);
                }
            };
            grid.DataError += (s, e) => { e.ThrowException = false; message.Text = "Проверьте введённое значение или выберите вариант из списка."; };
            return grid;
        }
        private void FillValues(CalculationSheet sheet, DataGridView grid)
        {
            painting = true;
            try {
                var invalid = engine.ValidateInputs(sheet.Name);
                int errors = 0;
                foreach (var cell in sheet.Cells) {
                    var view = grid[cell.Column - 1, cell.Row - 1];
                    try {
                        object value = cell.Editable ? engine.Input(sheet.Name, cell) : cell.Expression != null ? engine.Get(sheet.Name, cell.Address) : cell.Value;
                        if (cell.Expression != null && invalid != null) { view.Value = "Проверьте исходные данные"; view.Style.ForeColor = Color.Firebrick; }
                        else {
                            view.Value = view is DataGridViewComboBoxCell ? Convert.ToString(value, CultureInfo.InvariantCulture) : Format(value, cell.Format, cell.Editable);
                            view.Style.ForeColor = UiTheme.Text;
                        }
                        view.ErrorText = "";
                    } catch (CalculationError ex) { view.Value = "Ошибка расчёта"; view.ErrorText = ex.Message; view.Style.ForeColor = Color.Firebrick; errors++; }
                }
                message.ForeColor = invalid != null || errors > 0 ? Color.Firebrick : UiTheme.Muted;
                message.Text = invalid ?? (errors > 0 ? "Есть ошибки расчёта. Наведите на значок ошибки у результата." : "Источник: " + engine.Book.Source + " · Формулы и единицы из Excel. Для копирования: Ctrl+C.");
            } finally { painting = false; }
        }
        private static object Format(object value, string format, bool input)
        {
            if (value == null) return "";
            if (value is bool) return (bool)value ? "Да" : "Нет";
            if (value is string) return value;
            double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (input || format == "General" || format == "@") return n.ToString("G15", CultureInfo.GetCultureInfo("ru-RU"));
            // The imported workbooks use plain decimal formats; retain their display precision.
            if (format.All(c => c == '0' || c == '.')) return n.ToString(format, CultureInfo.GetCultureInfo("ru-RU"));
            return n.ToString("G15", CultureInfo.GetCultureInfo("ru-RU"));
        }
        private void SessionFile(bool load)
        {
            var sheet = (CalculationSheet)tabs.SelectedTab.Tag;
            if (!grids[sheet].EndEdit()) return;
            using (FileDialog dialog = load ? (FileDialog)new OpenFileDialog() : new SaveFileDialog()) {
                dialog.Filter = "Исходные данные расчёта (*.json)|*.json"; dialog.FileName = engine.Book.Title + ".json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 1000000 };
                    if (load) {
                        engine.LoadSession(serializer.Deserialize<CalculationSession>(File.ReadAllText(dialog.FileName)));
                        foreach (var pair in grids) FillValues(pair.Key, pair.Value);
                        ShowSheet();
                    } else SettingsFile.Write(dialog.FileName, serializer.Serialize(engine.SaveSession()));
                } catch (Exception ex) { MessageBox.Show(this, "Не удалось " + (load ? "открыть" : "сохранить") + " исходные данные: " + ex.Message, "Исходные данные", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void Export()
        {
            if (tabs.SelectedTab == null) return;
            var sheet = (CalculationSheet)tabs.SelectedTab.Tag;
            if (!grids[sheet].EndEdit()) return;
            FillValues(sheet, grids[sheet]);
            using (var dialog = new SaveFileDialog { Filter = "Таблица CSV (*.csv)|*.csv", FileName = sheet.Name + ".csv", OverwritePrompt = true }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    using (var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true))) {
                        writer.WriteLine("Источник;" + Csv(engine.Book.Source));
                        writer.WriteLine("Лист;" + Csv(sheet.Name));
                        foreach (DataGridViewRow row in grids[sheet].Rows)
                            writer.WriteLine(string.Join(";", row.Cells.Cast<DataGridViewCell>().Select(c => Csv(Convert.ToString(c.Value)))));
                    }
                } catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить таблицу: " + ex.Message, "Сохранение", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private static string Csv(string value)
        {
            value = value ?? "";
            // Spreadsheet applications may interpret text starting with these characters as formulas.
            if (value.Length > 0 && "=+@".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
