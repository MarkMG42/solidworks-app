using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace SWToolsHub.Portable
{
    internal sealed class ExcelBookView : UserControl
    {
        private readonly ExcelLibrary library;
        public readonly ExcelEntry Entry;
        private readonly ExcelBook book;
        private readonly ComboBox sheets = new ComboBox
        {
            Name = "ExcelSheetPicker",
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        private readonly DataGridView grid = new DataGridView
        {
            Name = "ExcelBookGrid",
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            MultiSelect = false,
            RowHeadersWidth = 42,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        private readonly Label address = new Label
        {
            AutoSize = true,
            Width = 60,
            TextAlign = ContentAlignment.MiddleLeft
        };
        private readonly TextBox formula = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill
        };
        private readonly TextBox input = new TextBox
        {
            Name = "ExcelInput",
            Dock = DockStyle.Fill
        };
        private readonly ComboBox choices = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false
        };
        private readonly ComboBox cellChoice = new ComboBox
        {
            Name = "ExcelCellList",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            IntegralHeight = false,
            MaxDropDownItems = 12
        };
        private readonly Label note = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted
        };
        private readonly Button apply = new Button
        {
            Name = "ExcelApply",
            Text = "Применить",
            AutoSize = true
        }, undo = new Button
        {
            Text = "Отменить изменение",
            AutoSize = true
        };
        private readonly Dictionary<string, Visual> styles = new Dictionary<string, Visual>();
        private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private bool changing;
        public bool Unsaved { get; private set; }

        public event Action DeleteRequested, ExpandRequested;
        private ExcelSheet Sheet => sheets.SelectedIndex >= 0 ? book.Sheets[sheets.SelectedIndex] : null;

        private string selectedAddress = "A1";
        private sealed class Visual
        {
            public Color Fill = Color.White, Ink = Color.Black;
            public Font Font;
            public DataGridViewContentAlignment Alignment = DataGridViewContentAlignment.BottomLeft;
            public bool Wrap;
            public IXLBorder Border;
        }

        public ExcelBookView(ExcelLibrary library, ExcelEntry entry, ExcelBook loaded = null)
        {
            this.library = library;
            Entry = entry;
            book = loaded ?? new ExcelBook(File.ReadAllBytes(library.OriginalPath(entry)));
            if (entry.Inputs?.Count > 0)
                book.Restore(entry.Inputs);
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Background;
            Padding = new Padding(10);
            using (var themeFont = UiTheme.CreateRegularFont())
                Font = new Font(themeFont.FontFamily, 9f);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            var title = new Label
            {
                Text = entry.Title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };
            var tools = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true,
                Margin = new Padding(0)
            };
            var save = new Button
            {
                Text = "Сохранить XLSX…",
                AutoSize = true
            };
            var delete = new Button
            {
                Text = "Удалить таблицу",
                AutoSize = true
            };
            var expand = new Button
            {
                Text = "Развернуть / свернуть",
                AutoSize = true
            };
            tools.Controls.AddRange(new Control[] { save, undo, delete, expand });
            save.Click += (s, e) => SaveAs();
            undo.Click += (s, e) =>
            {
                book.Undo();
                Changed();
            };
            delete.Click += (s, e) => DeleteRequested?.Invoke();
            expand.Click += (s, e) => ExpandRequested?.Invoke();
            var fx = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0, 3, 0, 3)
            };
            fx.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            fx.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fx.Controls.Add(address, 0, 0);
            fx.Controls.Add(formula, 1, 0);
            var editor = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 4)
            };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var inputHost = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 30,
                MinimumSize = new Size(0, 30)
            };
            inputHost.Controls.Add(input);
            inputHost.Controls.Add(choices);
            editor.Controls.Add(new Label { Text = "Значение", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            editor.Controls.Add(inputHost, 1, 0);
            editor.Controls.Add(apply, 2, 0);
            var heading = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1
            };
            heading.Controls.Add(title);
            heading.Controls.Add(tools);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(fx, 0, 1);
            layout.Controls.Add(editor, 0, 2);
            layout.Controls.Add(note, 0, 3);
            layout.Controls.Add(grid, 0, 4);
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0, 5, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.Controls.Add(new Label { Text = "Лист", AutoSize = true, Anchor = AnchorStyles.Left });
            bottom.Controls.Add(sheets, 1, 0);
            layout.Controls.Add(bottom, 0, 5);
            Controls.Add(layout);
            grid.Controls.Add(cellChoice);
            layout.SizeChanged += (s, e) =>
            {
                int width = Math.Max(1, layout.ClientSize.Width);
                tools.MaximumSize = new Size(width, 0);
                note.MaximumSize = new Size(width, 0);
                title.MaximumSize = new Size(width, 0);
            };
            grid.Font = GetFont("Arial", 9, FontStyle.Regular);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.DimGray;
            grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
            grid.RowHeadersDefaultCellStyle.ForeColor = Color.DimGray;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 237, 228);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
            grid.ColumnHeadersHeight = 24;
            grid.RowTemplate.Height = 20;
            grid.GridColor = Color.FromArgb(225, 225, 225);
            grid.CellValueNeeded += (s, e) =>
            {
                if (Sheet != null)
                    e.Value = book.Display(Sheet.Cell(ExcelBook.Address(e.RowIndex + 1, e.ColumnIndex + 1)));
            };
            grid.CellFormatting += (s, e) =>
            {
                if (Sheet == null)
                    return;
                var visual = Style(ExcelBook.Address(e.RowIndex + 1, e.ColumnIndex + 1));
                e.CellStyle.BackColor = visual.Fill;
                e.CellStyle.ForeColor = visual.Ink;
                e.CellStyle.Font = visual.Font;
                e.CellStyle.Alignment = visual.Alignment;
                e.CellStyle.WrapMode = visual.Wrap ? DataGridViewTriState.True : DataGridViewTriState.False;
                e.CellStyle.SelectionBackColor = visual.Fill;
                e.CellStyle.SelectionForeColor = visual.Ink;
            };
            grid.RowPostPaint += (s,e) => TextRenderer.DrawText(e.Graphics, (e.RowIndex + 1).ToString(ExcelBook.Inv), grid.Font,
                new Rectangle(2,e.RowBounds.Top,grid.RowHeadersWidth-8,e.RowBounds.Height),Color.DimGray,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
            grid.CellPainting += PaintCell;
            grid.SelectionChanged += (s, e) => SelectCell();
            grid.Scroll += (s, e) => PositionChoice();
            grid.ColumnWidthChanged += (s, e) => PositionChoice();
            grid.SizeChanged += (s, e) => PositionChoice();
            grid.CellDoubleClick += (s, e) => FocusInput();
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F2)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    FocusInput();
                }
            };
            apply.Click += (s, e) => Apply();
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Apply();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    input.Text = book.EditText(Sheet?.Cell(selectedAddress));
                    e.SuppressKeyPress = true;
                }
            };
            choices.SelectedIndexChanged += (s, e) =>
            {
                if (!changing && choices.Visible)
                    Apply();
            };
            cellChoice.SelectedIndexChanged += (s, e) =>
            {
                if (!changing && cellChoice.Visible)
                {
                    changing = true;
                    choices.SelectedIndex = cellChoice.SelectedIndex;
                    changing = false;
                    Apply();
                }
            };
            foreach (var sheet in book.Sheets)
                sheets.Items.Add(sheet.Name);
            sheets.SelectedIndexChanged += (s, e) =>
            {
                ShowSheet();
                SaveState();
            };
            int initial = book.Sheets.FindIndex(sh => sh.Name == entry.Sheet);
            sheets.SelectedIndex = initial >= 0 ? initial : 0;
            Disposed += (s, e) =>
            {
                book.Dispose();
                foreach (var font in fonts.Values)
                    font.Dispose();
                Font.Dispose();
                title.Font.Dispose();
            };
        }

        private void ShowSheet()
        {
            changing = true;
            try
            {
                cellChoice.Visible = false;
                input.Enabled = apply.Enabled = false;
                styles.Clear();
                grid.Columns.Clear();
                grid.RowCount = 0;
                var sheet = Sheet;
                if (sheet == null)
                    return;
                grid.CellBorderStyle = sheet.Worksheet.ShowGridLines ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None;
                float scale = grid.DeviceDpi / 96f;
                grid.RowTemplate.Height = Math.Max(4,(int)Math.Round(20 * scale));
                grid.ColumnHeadersHeight = Math.Max(16,(int)Math.Round(24 * scale));
                grid.RowHeadersWidth = Math.Max(24,(int)Math.Round(42 * scale));
                for (int c = 1; c <= sheet.Columns; c++)
                {
                    var column = sheet.Worksheet.Column(c);
                    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ExcelBook.Address(1, c).TrimEnd('1'), HeaderText = ExcelBook.Address(1, c).TrimEnd('1'), Width = Math.Max(12, (int)Math.Round((column.Width * 7 + 5) * scale)), SortMode = DataGridViewColumnSortMode.NotSortable, Visible = !column.IsHidden });
                }

                grid.RowCount = sheet.Rows;
                grid.CurrentCell = null;
                for (int r = 1; r <= sheet.Rows; r++)
                {
                    var originalRow = sheet.Worksheet.Row(r);
                    int height = Math.Max(4, Math.Min(65536, (int)Math.Round(originalRow.Height * 4 / 3 * scale)));
                    if (height != grid.RowTemplate.Height) grid.Rows[r - 1].Height = height;
                    if (originalRow.IsHidden || originalRow.Height <= 0) grid.Rows[r - 1].Visible = false;
                }
                string target = Entry.Sheet == sheet.Name ? Entry.Cell : null;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    var p = ExcelBook.Point(target);
                    if (p.Item1 <= sheet.Rows && p.Item2 <= sheet.Columns && grid.Rows[p.Item1 - 1].Visible && grid.Columns[p.Item2 - 1].Visible)
                        grid.CurrentCell = grid[p.Item2 - 1, p.Item1 - 1];
                }

                if (grid.CurrentCell == null) {
                    int row = grid.Rows.GetFirstRow(DataGridViewElementStates.Visible);
                    var column = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                    if(row >= 0 && column != null) grid.CurrentCell = grid[column.Index,row];
                }
                undo.Enabled = book.CanUndo;
            }
            finally
            {
                changing = false;
            }

            SelectCell();
            Message();
        }

        private void FocusInput()
        {
            if (!apply.Enabled)
                return;
            if (choices.Visible)
            {
                cellChoice.Focus();
                cellChoice.DroppedDown = true;
            }
            else
            {
                input.Focus();
                input.SelectAll();
            }
        }

        private void SelectCell()
        {
            if (changing || Sheet == null || grid.CurrentCell == null)
                return;
            var current = grid.CurrentCell;
            string a = ExcelBook.Address(current.RowIndex + 1, current.ColumnIndex + 1);
            var merge = Sheet.Merges.FirstOrDefault(m => m.Contains(current.RowIndex + 1, current.ColumnIndex + 1));
            selectedAddress = merge?.Master ?? a;
            var cell = Sheet.Cell(selectedAddress);
            address.Text = selectedAddress;
            formula.Text = cell?.Formula != null ? "=" + cell.Formula : book.EditText(cell);
            bool locked = cell?.Formula != null || cell?.Locked == true || cell == null && Sheet.Worksheet.IsProtected;
            input.Enabled = apply.Enabled = !locked;
            input.Text = locked ? "" : book.EditText(cell);
            choices.Visible = cellChoice.Visible = false;
            input.Visible = true;
            if (cell?.Error != null)
                note.Text = cell.Error;
            else if (cell?.Missing == true && !book.Dirty)
                note.Text = "Нет сохранённого результата Excel";
            var list = locked ? null : book.List(Sheet.Name, selectedAddress);
            if (list?.Error != null)
            {
                note.Text = list.Error;
                return;
            }

            if (list == null)
                return;
            changing = true;
            try
            {
                choices.Items.Clear();
                cellChoice.Items.Clear();
                if (list.AllowBlank)
                {
                    var blank = new ExcelChoice
                    {
                        Label = "",
                        Value = null
                    };
                    choices.Items.Add(blank);
                    cellChoice.Items.Add(blank);
                }

                foreach (var option in list.Choices)
                {
                    choices.Items.Add(option);
                    cellChoice.Items.Add(option);
                }

                string value = Convert.ToString(cell?.Value, ExcelBook.Inv) ?? "";
                int index = -1;
                for (int i = 0; i < choices.Items.Count; i++)
                    if (string.Equals(((ExcelChoice)choices.Items[i]).Label, value, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }

                choices.SelectedIndex = cellChoice.SelectedIndex = index;
                choices.Visible = true;
                choices.Enabled = true;
                input.Visible = false;
                cellChoice.Font = Style(selectedAddress).Font;
                cellChoice.BackColor = Style(selectedAddress).Fill;
                cellChoice.ForeColor = Style(selectedAddress).Ink;
            }
            finally
            {
                changing = false;
            }

            PositionChoice();
        }

        private void PositionChoice()
        {
            if (changing || Sheet == null || !choices.Visible || grid.CurrentCell == null)
            {
                cellChoice.Visible = false;
                return;
            }

            var p = ExcelBook.Point(selectedAddress);
            var merge = Sheet.Merges.FirstOrDefault(m => m.Contains(p.Item1, p.Item2));
            var rect = merge == null ? grid.GetCellDisplayRectangle(p.Item2 - 1, p.Item1 - 1, true) : MergeRectangle(merge);
            var area = new Rectangle(grid.RowHeadersWidth, grid.ColumnHeadersHeight, Math.Max(0, grid.ClientSize.Width - grid.RowHeadersWidth), Math.Max(0, grid.ClientSize.Height - grid.ColumnHeadersHeight));
            if (rect.Width < 1 || rect.Height < 1 || !area.IntersectsWith(rect))
            {
                cellChoice.Visible = false;
                return;
            }

            cellChoice.Bounds = Rectangle.Intersect(area, rect);
            cellChoice.Visible = true;
            cellChoice.BringToFront();
        }

        private void Apply()
        {
            if (!apply.Enabled || Sheet == null)
                return;
            try
            {
                string text = choices.Visible ? (choices.SelectedItem as ExcelChoice)?.Label ?? "" : input.Text;
                book.Apply(Sheet.Name, selectedAddress, text);
                Unsaved = true;
                Changed();
            }
            catch (Exception ex)
            {
                note.ForeColor = Color.Firebrick;
                note.Text = ex.Message;
            }
        }

        private void Changed()
        {
            styles.Clear();
            grid.Invalidate();
            undo.Enabled = book.CanUndo;
            SelectCell();
            SaveState();
        }

        private void SaveState()
        {
            if (Sheet == null)
                return;
            try
            {
                Entry.Sheet = Sheet.Name;
                Entry.Cell = selectedAddress;
                Entry.Inputs = book.Edits.Values.ToList();
                library.Save(Entry);
                Unsaved = false;
                Message();
            }
            catch (Exception ex)
            {
                Unsaved = true;
                note.ForeColor = Color.Firebrick;
                note.Text = "Не удалось сохранить в приложении: " + ex.Message + ". Сохраните XLSX в папку.";
            }
        }

        private void Message()
        {
            note.ForeColor = UiTheme.Muted;
            note.Text = book.Dirty ? $"Изменений: {book.Edits.Count}. Пересчитано: {book.Calculated}; не обновлено: {book.Unavailable}. Сохранено в приложении." : "Книга сохранена в приложении. Изменения записываются автоматически.";
        }

        private void SaveAs()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "Книга Excel (*.xlsx)|*.xlsx",
                FileName = Path.GetFileNameWithoutExtension(Entry.FileName) + "-изменено.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true
            }

            )
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        library.SaveAs(Entry, book, dialog.FileName);
                        Unsaved = false;
                        note.ForeColor = UiTheme.Muted;
                        note.Text = "XLSX сохранён: " + dialog.FileName + ". Excel пересчитает формулы при открытии.";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Не удалось сохранить XLSX: " + ex.Message, "Сохранение XLSX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
        }

        private Color Resolve(XLColor color, Color fallback)
        {
            try
            {
                if (color.ColorType == XLColorType.Theme)
                {
                    var source = book.Workbook.Theme.ResolveThemeColor(color.ThemeColor).Color;
                    double tint = color.ThemeTint;
                    Func<byte, int> channel = c => Math.Max(0, Math.Min(255, (int)Math.Round(tint < 0 ? c * (1 + tint) : c + (255 - c) * tint)));
                    return Color.FromArgb(channel(source.R), channel(source.G), channel(source.B));
                }

                return color.Color;
            }
            catch
            {
                return fallback;
            }
        }

        private Font GetFont(string name, float size, FontStyle style)
        {
            size = Math.Max(5, Math.Min(72, size));
            string key = name + "|" + size + "|" + style;
            Font font;
            if (fonts.TryGetValue(key, out font))
                return font;
            if (fonts.Count >= 256)
                return Font;
            try
            {
                font = new Font(name, size, style);
            }
            catch
            {
                font = new Font("Arial", size, style);
            }

            fonts[key] = font;
            return font;
        }

        private Visual Style(string a)
        {
            Visual result;
            if (styles.TryGetValue(a, out result))
                return result;
            if (styles.Count > 10000)
                styles.Clear();
            var cell = Sheet.Worksheet.Cell(a);
            var style = cell.Style;
            FontStyle flags = (style.Font.Bold ? FontStyle.Bold : FontStyle.Regular) | (style.Font.Italic ? FontStyle.Italic : 0) | (style.Font.Strikethrough ? FontStyle.Strikeout : 0) | (style.Font.Underline != XLFontUnderlineValues.None ? FontStyle.Underline : 0);
            string family = style.Font.FontName;
            float size = (float)style.Font.FontSize;
            result = new Visual
            {
                Fill = style.Fill.PatternType == XLFillPatternValues.Solid ? Resolve(style.Fill.BackgroundColor, Color.White) : Color.White,
                Ink = Resolve(style.Font.FontColor, Color.Black),
                Font = GetFont(family, size, flags),
                Wrap = style.Alignment.WrapText,
                Border = style.Border
            };
            var value = Sheet.Cell(a)?.Value;
            var horizontal = style.Alignment.Horizontal;
            bool right = horizontal == XLAlignmentHorizontalValues.Right || horizontal == XLAlignmentHorizontalValues.General && (value is double || value is DateTime);
            bool center = horizontal == XLAlignmentHorizontalValues.Center || horizontal == XLAlignmentHorizontalValues.CenterContinuous;
            var vertical = style.Alignment.Vertical;
            result.Alignment = vertical == XLAlignmentVerticalValues.Top ? (right ? DataGridViewContentAlignment.TopRight : center ? DataGridViewContentAlignment.TopCenter : DataGridViewContentAlignment.TopLeft) : vertical == XLAlignmentVerticalValues.Center ? (right ? DataGridViewContentAlignment.MiddleRight : center ? DataGridViewContentAlignment.MiddleCenter : DataGridViewContentAlignment.MiddleLeft) : (right ? DataGridViewContentAlignment.BottomRight : center ? DataGridViewContentAlignment.BottomCenter : DataGridViewContentAlignment.BottomLeft);
            foreach (var condition in book.ConditionalStyles(Sheet, a))
            {
                var fill = condition.Element(ExcelBook.S + "fill")?.Element(ExcelBook.S + "patternFill");
                if (fill != null)
                {
                    var color = fill.Element(ExcelBook.S + "fgColor") ?? fill.Element(ExcelBook.S + "bgColor");
                    if (color != null)
                        result.Fill = ColorTranslator.FromHtml(book.Color(color, "#FFFFFF"));
                }

                var font = condition.Element(ExcelBook.S + "font");
                if (font != null)
                {
                    var color = font.Element(ExcelBook.S + "color");
                    if (color != null)
                        result.Ink = ColorTranslator.FromHtml(book.Color(color, "#000000"));
                    foreach (var pair in new[]
                    {
                        Tuple.Create("b", FontStyle.Bold),
                        Tuple.Create("i", FontStyle.Italic),
                        Tuple.Create("strike", FontStyle.Strikeout),
                        Tuple.Create("u", FontStyle.Underline)
                    }

                    )
                    {
                        var part = font.Element(ExcelBook.S + pair.Item1);
                        if (part != null)
                        {
                            bool on = (string)part.Attribute("val") != "0" && (string)part.Attribute("val") != "false";
                            flags = on ? flags | pair.Item2 : flags & ~pair.Item2;
                        }
                    }

                    result.Font = GetFont(family, size, flags);
                }
            }

            styles[a] = result;
            return result;
        }

        private Rectangle MergeRectangle(ExcelRange merge)
        {
            var first = grid.GetCellDisplayRectangle(merge.Left - 1, merge.Top - 1, false);
            int width = 0, height = 0;
            for (int c = merge.Left; c <= merge.Right; c++)
                if (grid.Columns[c - 1].Visible)
                    width += grid.Columns[c - 1].Width;
            for (int r = merge.Top; r <= merge.Bottom; r++)
                if (grid.Rows.SharedRow(r - 1).Visible)
                    height += grid.Rows.SharedRow(r - 1).Height;
            return new Rectangle(first.X, first.Y, width, height);
        }

        private void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || Sheet == null)
                return;
            string a = ExcelBook.Address(e.RowIndex + 1, e.ColumnIndex + 1);
            var merge = Sheet.Merges.FirstOrDefault(m => m.Contains(e.RowIndex + 1, e.ColumnIndex + 1));
            var visual = Style(merge?.Master ?? a);
            Rectangle rect = e.CellBounds;
            if (merge != null)
            {
                rect = MergeRectangle(merge);
                var clip = e.Graphics.Save();
                e.Graphics.SetClip(Rectangle.Intersect(rect, new Rectangle(grid.RowHeadersWidth, grid.ColumnHeadersHeight, Math.Max(0, grid.ClientSize.Width - grid.RowHeadersWidth), Math.Max(0, grid.ClientSize.Height - grid.ColumnHeadersHeight))));
                using (var brush = new SolidBrush(visual.Fill))
                    e.Graphics.FillRectangle(brush, rect);
                TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.NoPadding;
                if (visual.Wrap)
                    flags |= TextFormatFlags.WordBreak;
                int align = (int)visual.Alignment;
                if (align == 2 || align == 32 || align == 512)
                    flags |= TextFormatFlags.HorizontalCenter;
                else if (align == 4 || align == 64 || align == 1024)
                    flags |= TextFormatFlags.Right;
                if (align >= 256)
                    flags |= TextFormatFlags.Bottom;
                else if (align >= 16)
                    flags |= TextFormatFlags.VerticalCenter;
                var textRect = Rectangle.Inflate(rect, -3, -1);
                TextRenderer.DrawText(e.Graphics, book.Display(Sheet.Cell(merge.Master)), visual.Font, textRect, visual.Ink, flags);
                using (var pen = new Pen(grid.GridColor))
                    e.Graphics.DrawRectangle(pen, rect.X, rect.Y, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
                e.Graphics.Restore(clip);
                e.Handled = true;
            }
            else
            {
                e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
                e.Handled = true;
            }

            Border(e.Graphics, visual.Border.TopBorder, Resolve(visual.Border.TopBorderColor, Color.Black), rect.Left, rect.Top, rect.Right - 1, rect.Top);
            Border(e.Graphics, visual.Border.BottomBorder, Resolve(visual.Border.BottomBorderColor, Color.Black), rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
            Border(e.Graphics, visual.Border.LeftBorder, Resolve(visual.Border.LeftBorderColor, Color.Black), rect.Left, rect.Top, rect.Left, rect.Bottom - 1);
            Border(e.Graphics, visual.Border.RightBorder, Resolve(visual.Border.RightBorderColor, Color.Black), rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
            if (selectedAddress == (merge?.Master ?? a))
                using (var pen = new Pen(Color.FromArgb(33, 115, 70), 2))
                    e.Graphics.DrawRectangle(pen, rect.Left + 1, rect.Top + 1, Math.Max(0, rect.Width - 3), Math.Max(0, rect.Height - 3));
        }

        private static void Border(Graphics g, XLBorderStyleValues style, Color color, int x1, int y1, int x2, int y2)
        {
            if (style == XLBorderStyleValues.None)
                return;
            using (var pen = new Pen(color, style == XLBorderStyleValues.Thick ? 3 : style == XLBorderStyleValues.Medium || style == XLBorderStyleValues.Double ? 2 : 1))
            {
                string type = style.ToString();
                if (type.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0)
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                else if (type.IndexOf("Dot", StringComparison.OrdinalIgnoreCase) >= 0)
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }
    }
}
