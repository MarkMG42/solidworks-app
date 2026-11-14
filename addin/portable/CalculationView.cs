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
        private readonly string inputPath;
        private string saveError;
        private readonly ComboBox sheetPicker = new ComboBox { Name = "CalculationSheetPicker", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, MaxDropDownItems = 12 };
        private readonly Panel sheetHost = new Panel { Name = "CalculationSheetHost", Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly Label message = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(4, 6, 4, 6), Margin = new Padding(0) };
        private readonly Label hint = new Label { Text = "Светлые ячейки — ввод. Enter или Tab — применить.", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Name = "CalculationActions", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, AutoScroll = false, Margin = new Padding(0) };
        private CalculationSheet selectedSheet;
        private bool switchingSheet;
        private readonly Font compactFont;
        private sealed class SheetChoice
        {
            public CalculationSheet Sheet;
            public override string ToString() => Sheet.Name + (Sheet.ReferenceOnly ? " · справочник" : "");
        }
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
            inputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SW-Assistant", "CalculationInputs", book.Sha256 + ".json");
            try { if (File.Exists(inputPath)) engine.LoadSession(new JavaScriptSerializer().Deserialize<CalculationSession>(File.ReadAllText(inputPath))); }
            catch { saveError = "Сохранённый ввод не прочитан; показаны исходные значения."; }
            Dock = DockStyle.Fill; BackColor = UiTheme.Background; AutoScaleMode = AutoScaleMode.Dpi;
            using (var themeFont = UiTheme.CreateFont()) compactFont = new Font(themeFont.FontFamily, 9f, FontStyle.Regular);
            Font = compactFont;
            Disposed += (s, e) => compactFont.Dispose();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8), Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var reset = ActionButton("Сбросить");
            var export = ActionButton("Экспорт CSV…");
            var saveInput = ActionButton("Сохранить ввод…");
            var openInput = ActionButton("Открыть ввод…");
            toolbar.Controls.AddRange(new Control[] { reset, saveInput, openInput, export });
            saveInput.Click += (s, e) => SessionFile(false);
            openInput.Click += (s, e) => SessionFile(true);
            var selector = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 8) };
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selector.Controls.Add(new Label { Text = "Лист", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
            selector.Controls.Add(sheetPicker, 1, 0);
            sheetPicker.Margin = new Padding(0); sheetPicker.AccessibleName = "Лист расчёта";
            hint.ForeColor = UiTheme.Muted;
            layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(hint, 0, 1);
            layout.Controls.Add(selector, 0, 2); layout.Controls.Add(sheetHost, 0, 3); layout.Controls.Add(message, 0, 4);
            Controls.Add(layout);
            layout.SizeChanged += (s, e) => {
                // Bound preferred sizes before measuring wrapped rows. Nothing above
                // the grid owns a horizontal scrollbar or expands the page width.
                int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
                toolbar.MaximumSize = new Size(width, 0);
                hint.MaximumSize = new Size(width, 0); message.MaximumSize = new Size(width, 0);
                sheetPicker.DropDownWidth = Math.Max(1, sheetPicker.Width);
            };
            foreach (var sheet in book.Sheets) sheetPicker.Items.Add(new SheetChoice { Sheet = sheet });
            sheetPicker.SelectedIndexChanged += (s, e) => ShowSheet();
            reset.Click += (s, e) => {
                if (MessageBox.Show(this, "Вернуть исходные значения всех листов этого расчёта?", "Сброс расчёта", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                engine.Reset(); SaveInputs(); foreach (var pair in grids) FillValues(pair.Key, pair.Value); ShowSheet();
            };
            export.Click += (s, e) => Export();
            // ComboBox selection is managed and available before native handles exist.
            // TabControl.SelectedTab could be null in the old constructor, leaving
            // the first page unpopulated until the user selected another tab.
            if (sheetPicker.Items.Count > 0) sheetPicker.SelectedIndex = 0;
        }
        private static Button ActionButton(string text) => new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 6, 4) };
        public void ActivateSheet()
        {
            ShowSheet();
            PerformLayout(); sheetHost.PerformLayout();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e); ActivateSheet();
        }
        private void ShowSheet()
        {
            var choice = sheetPicker.SelectedItem as SheetChoice;
            if (choice == null || switchingSheet) return;
            var sheet = choice.Sheet;
            DataGridView previous;
            if (selectedSheet != null && selectedSheet != sheet && grids.TryGetValue(selectedSheet, out previous) && !previous.EndEdit()) {
                switchingSheet = true;
                try { sheetPicker.SelectedItem = sheetPicker.Items.Cast<SheetChoice>().First(c => c.Sheet == selectedSheet); }
                finally { switchingSheet = false; }
                return;
            }
            DataGridView grid;
            if (!grids.TryGetValue(sheet, out grid)) {
                grid = CreateGrid(sheet); grid.Visible = false;
                grids.Add(sheet, grid); sheetHost.Controls.Add(grid);
            }
            selectedSheet = sheet;
            foreach (var pair in grids) pair.Value.Visible = pair.Key == sheet;
            FillValues(sheet, grid);
            grid.BringToFront(); sheetHost.PerformLayout();
        }
        private DataGridView CreateGrid(CalculationSheet sheet)
        {
            var grid = new DataGridView {
                Name = "CalculationGrid", Dock = DockStyle.Fill, Font = Font, BackgroundColor = UiTheme.Background, BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
                AutoGenerateColumns = false, RowHeadersWidth = 36, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EditMode = DataGridViewEditMode.EditOnEnter, GridColor = UiTheme.Border
            };
            grid.DefaultCellStyle.BackColor = UiTheme.Background;
            grid.DefaultCellStyle.Padding = new Padding(10, 6, 10, 6);
            grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(251, 226, 202);
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            grid.RowTemplate.MinimumHeight = 26; grid.RowTemplate.Height = 26;
            int columns = sheet.Cells.Max(c => c.Column), rows = sheet.Cells.Max(c => c.Row);
            bool strength = engine.Book.Source.Contains("прочность");
            for (int col = 1; col <= columns; col++) {
                string address = CalculationEngine.Address(1, col);
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = address.TrimEnd('1'), HeaderText = address.TrimEnd('1'), SortMode = DataGridViewColumnSortMode.NotSortable,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    FillWeight = ColumnWeight(columns, col, strength),
                    MinimumWidth = sheet.ReferenceOnly ? (col == 1 ? 100 : 64) : col == 1 ? 128 : col == 2 ? 76 : col == 3 ? 48 : 90 });
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
                try { engine.SetInput(sheet.Name, cell.Address, e.FormattedValue); SaveInputs(); view.ErrorText = ""; }
                catch (CalculationError ex) { e.Cancel = true; view.ErrorText = ex.Message; message.Text = ex.Message; message.ForeColor = Color.Firebrick; }
            };
            grid.CellEndEdit += (s, e) => {
                if (painting || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var view = grid[e.ColumnIndex, e.RowIndex]; var cell = view.Tag as CalculationCell;
                // EndEdit commits the control value without necessarily moving focus.
                // CellValidating alone therefore misses Enter/programmatic commits.
                if (cell != null && cell.Editable) {
                    try { engine.SetInput(sheet.Name, cell.Address, view.Value); SaveInputs(); }
                    catch (CalculationError ex) { view.ErrorText = ex.Message; message.Text = ex.Message; message.ForeColor = Color.Firebrick; return; }
                }
                FillValues(sheet, grid);
            };
            grid.CurrentCellDirtyStateChanged += (s, e) => {
                if (!painting && grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell) {
                    if (grid.EndEdit()) FillValues(sheet, grid);
                }
            };
            grid.DataError += (s, e) => { e.ThrowException = false; message.Text = "Проверьте введённое значение или выберите вариант из списка."; };
            return grid;
        }
        private static float ColumnWeight(int count, int column, bool strength)
        {
            if (count == 2) return column == 1 ? 63 : 37;
            if (count == 3) return strength ? new[] { 50f, 24f, 26f }[column - 1] : new[] { 55f, 33f, 12f }[column - 1];
            if (count == 4) return new[] { 42f, 22f, 9f, 27f }[column - 1];
            return column == 1 ? 180 : 110;
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
                if (engine.Book.Source.Contains("длины болта") && sheet.Name == "Длина болта") {
                    hint.Text = "Расчётная длина болта, мм: " + (invalid != null ? "Проверьте исходные данные" : Convert.ToString(Format(engine.Get(sheet.Name, "B19"), "0", false)));
                    hint.ForeColor = invalid != null ? Color.Firebrick : UiTheme.Text;
                } else hint.Text = "Светлые ячейки — ввод. Enter или Tab — применить.";
                message.ForeColor = saveError != null || invalid != null || errors > 0 ? Color.Firebrick : UiTheme.Muted;
                message.Text = saveError ?? invalid ?? (errors > 0 ? "Есть ошибки расчёта. Наведите на значок ошибки у результата." : "Источник: " + engine.Book.Source + " · Ctrl+C — копировать.");
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
            var sheet = selectedSheet;
            if (!grids[sheet].EndEdit()) return;
            using (FileDialog dialog = load ? (FileDialog)new OpenFileDialog() : new SaveFileDialog()) {
                dialog.Filter = "Исходные данные расчёта (*.json)|*.json"; dialog.FileName = engine.Book.Title + ".json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 1000000 };
                    if (load) {
                        engine.LoadSession(serializer.Deserialize<CalculationSession>(File.ReadAllText(dialog.FileName)));
                        SaveInputs(); foreach (var pair in grids) FillValues(pair.Key, pair.Value);
                        ShowSheet();
                    } else SettingsFile.Write(dialog.FileName, serializer.Serialize(engine.SaveSession()));
                } catch (Exception ex) { MessageBox.Show(this, "Не удалось " + (load ? "открыть" : "сохранить") + " исходные данные: " + ex.Message, "Исходные данные", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void Export()
        {
            if (selectedSheet == null) return;
            var sheet = selectedSheet;
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
        private void SaveInputs()
        {
            try { SettingsFile.Write(inputPath, new JavaScriptSerializer().Serialize(engine.SaveSession())); saveError = null; }
            catch (Exception ex) { saveError = "Автосохранение ввода не удалось: " + ex.Message; }
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
