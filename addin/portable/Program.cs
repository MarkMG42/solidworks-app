using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SWToolsHub.Portable
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiTheme.Initialize();
            try { Application.Run(new Hub()); } finally { UiTheme.Shutdown(); }
        }
    }
    internal sealed class Hub : Form
    {
        private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "name" };
        private readonly Button run = new RoundedButton { Dock = DockStyle.Bottom, Height = 66, Text = "Выбрать процедуру и запустить…" };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 135, AutoEllipsis = true };
        private Macro[] macros = new Macro[0];
        private bool busy;
        private string macroFolder;
        private readonly ToolTip tips = new ToolTip();
        private readonly TableLayoutPanel moduleGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(30, 6, 30, 12) };
        private readonly Label folderLabel = new Label { Dock = DockStyle.Top, Height = 63, AutoEllipsis = true };
        private readonly FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 0, 24, 0) };
        private readonly Label heading = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label count = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private Font titleFont;
        private string section;
        private static readonly Color Background = UiTheme.Background;
        private static readonly Color Surface = UiTheme.Surface;
        private static readonly Color Foreground = UiTheme.Text;
        private static readonly Color Muted = UiTheme.Muted;
        private readonly Timer refresh = new Timer { Interval = 2000 };
        private readonly string settingsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SWToolsHub", "portable.json");
        private Settings settings = new Settings();
        public sealed class Settings {
            public string Folder { get; set; }
            public List<string> Favorites { get; set; } = new List<string>();
            public Dictionary<string, string> Entries { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        private void SaveSettings() {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllText(settingsPath, new JavaScriptSerializer().Serialize(settings));
        }
        private void ReloadFolder() {
            if (busy) return;
            try {
                var next = MacroFolder.Scan(macroFolder);
                bool changed = !next.Select(m => m.id).SequenceEqual(macros.Select(m => m.id));
                if (changed) { macros = next; Filter(); }
                if (changed || string.IsNullOrEmpty(status.Text)) status.Text = next.Length == 0 ? "В выбранной папке нет .swp. Добавьте файлы или выберите другую папку." : "Найдено макросов: " + next.Length;
            } catch (Exception ex) { macros = new Macro[0]; Filter(); status.Text = "Папка недоступна: " + ex.Message; }
        }
        public Hub()
        {
            Text = "SW Tools Hub";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1350, 900); MinimumSize = new Size(990, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiTheme.CreateFont(); titleFont = (Font)Font.Clone(); BackColor = Background; ForeColor = Foreground;
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(0) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var height in new[] { 96, 84, 45, 180 }) shell.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(30, 18, 24, 18) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 2; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            var brand = new Label { Text = "SW Tools Hub", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = titleFont };
            var choose = MakeButton("");
            ConfigureIcon(run, "play", "Выбрать процедуру и запустить выбранный макрос");
            ConfigureIcon(choose, "folder", "Выбрать корневую папку библиотеки");
            header.Controls.Add(brand, 0, 0); header.Controls.Add(run, 1, 0); header.Controls.Add(choose, 2, 0);
            var searchBox = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 18, 18, 12), Margin = new Padding(30, 0, 30, 12), BackColor = Surface };
            search.BackColor = Surface; search.ForeColor = Foreground; search.AccessibleName = "Поиск макросов";
            searchBox.Controls.Add(search); tips.SetToolTip(search, "Поиск по выбранному разделу; на главной — по всей библиотеке");
            heading.Padding = new Padding(30, 0, 0, 0);
            heading.Text = "Поиск макросов"; heading.ForeColor = Muted;
            status.Height = 66; status.Padding = new Padding(30, 6, 30, 0); status.ForeColor = Muted;
            folderLabel.Visible = false;
            for (int i = 0; i < 4; i++) moduleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            moduleGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            count.Padding = new Padding(30, 0, 0, 0); count.ForeColor = Muted;
            shell.Controls.Add(header, 0, 0); shell.Controls.Add(searchBox, 0, 1); shell.Controls.Add(heading, 0, 2);
            var libraryFrame = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(30, 15, 30, 30), Padding = new Padding(21, 15, 21, 18) };
            content.BackColor = Surface; content.Padding = new Padding(0);
            libraryFrame.Controls.Add(content);
            shell.Controls.Add(moduleGrid, 0, 3); shell.Controls.Add(libraryFrame, 0, 4);
            status.TextChanged += (s, e) => tips.SetToolTip(run, "Выбрать процедуру и запустить выбранный макрос\n" + status.Text);
            Controls.Add(shell);
            content.SizeChanged += (s, e) => RenderLibrary();
            choose.Click += (s, e) => {
                if (busy) return;
                try {
                    var selectedFolder = FolderPicker.Choose(Handle, macroFolder);
                    if (selectedFolder == null) return;
                    MacroFolder.EnsureModules(selectedFolder);
                    settings.Folder = selectedFolder; SaveSettings(); macroFolder = selectedFolder;
                    tips.SetToolTip(choose, "Библиотека: " + macroFolder + "\nВыбрать другую корневую папку");
                    folderLabel.Text = macroFolder; ReloadFolder();
                } catch (Exception ex) { ShowError("Не удалось выбрать папку: " + ex.Message); }
            };
            refresh.Tick += (s, e) => ReloadFolder();
            FormClosed += (s, e) => { refresh.Dispose(); tips.Dispose(); titleFont.Dispose(); };
            search.TextChanged += (s, e) => Filter();
            list.SelectedIndexChanged += (s, e) => run.Enabled = !busy && list.SelectedItem != null;
            run.Click += (s, e) => RunSelected(true);
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            Shown += (s, e) => {
                try {
                    if (File.Exists(settingsPath)) settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings();
                } catch { settings = new Settings(); }
                if (settings.Favorites == null) settings.Favorites = new List<string>();
                if (settings.Entries == null) settings.Entries = new Dictionary<string, string>();
                macroFolder = string.IsNullOrWhiteSpace(settings.Folder) ? Path.Combine(root, "Library", "Macros") : settings.Folder;
                folderLabel.Text = macroFolder;
                try { MacroFolder.EnsureModules(macroFolder); } catch (Exception ex) { status.Text = "Не удалось создать папки разделов: " + ex.Message; }
                ReloadFolder(); refresh.Start();
            };
        }
        private void Filter()
        {
            var selectedId = (list.SelectedItem as Macro)?.id;
            list.DataSource = VisibleMacros();
            list.SelectedItem = selectedId == null ? null : macros.FirstOrDefault(m => m.id == selectedId);
            run.Enabled = !busy && list.SelectedItem != null;
            RenderLibrary();
        }
        private Macro[] VisibleMacros()
        {
            bool home = section == null && string.IsNullOrWhiteSpace(search.Text);
            return macros.Where(m => m.name.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
                && (section == null || m.category == section)
                && (!(home) || settings.Favorites.Contains(m.id, StringComparer.OrdinalIgnoreCase))).ToArray();
        }
        private void ConfigureIcon(Button button, string icon, string description)
        {
            StyleButton(button); ((RoundedButton)button).IconOnly = true; ((RoundedButton)button).AccentHover = icon == "play"; button.BackColor = UiTheme.IconSurface; button.ForeColor = UiTheme.IconInk; button.Text = ""; button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3); button.Padding = new Padding(0);
            button.Image = DrawIcon(icon); button.ImageAlign = ContentAlignment.MiddleCenter;
            button.AccessibleName = description; tips.SetToolTip(button, description);
            button.Disposed += (s, e) => button.Image.Dispose();
        }
        private static Bitmap DrawIcon(string kind)
        {
            var image = new Bitmap(36, 36);
            using (var g = Graphics.FromImage(image))
            using (var pen = new Pen(kind == "folder" ? UiTheme.IconInk : UiTheme.AccentInk, 1.35f)) {
                g.ScaleTransform(1.5f, 1.5f);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                if (kind == "folder") g.DrawPolygon(pen, new[] { new Point(3, 6), new Point(10, 6), new Point(12, 9), new Point(21, 9), new Point(21, 19), new Point(3, 19) });
                else if (kind == "play") g.DrawPolygon(pen, new[] { new Point(8, 4), new Point(20, 12), new Point(8, 20) });
                else if (kind == "part") {
                    g.DrawPolygon(pen, new[] { new Point(12,3),new Point(21,8),new Point(21,18),new Point(12,23),new Point(3,18),new Point(3,8) });
                    g.DrawLines(pen, new[] {new Point(3,8),new Point(12,13),new Point(21,8)}); g.DrawLine(pen,12,13,12,23);
                }
                else if (kind == "drawing") {
                    g.DrawPolygon(pen,new[] {new Point(5,3),new Point(15,3),new Point(19,7),new Point(19,21),new Point(5,21)});
                    g.DrawLines(pen,new[] {new Point(15,3),new Point(15,8),new Point(19,8)}); g.DrawLine(pen,8,12,16,12);g.DrawLine(pen,8,16,13,16);
                }
                else if (kind == "assembly") {
                    foreach (var center in new[] {new Point(12,6),new Point(6,16),new Point(18,16)}) {
                        var corners = Enumerable.Range(0,6).Select(i=>new PointF(center.X + 5*(float)Math.Sin(i*Math.PI/3),center.Y + 5*(float)Math.Cos(i*Math.PI/3))).ToArray();
                        g.DrawPolygon(pen,corners);
                    }
                }
                else if (kind == "laser") {
                    g.DrawPolygon(pen,new[] {new Point(7,3),new Point(17,3),new Point(17,8),new Point(12,12),new Point(7,8)});
                    g.DrawLine(pen,12,12,12,18);g.DrawLine(pen,3,21,21,21);g.DrawLine(pen,6,13,3,16);g.DrawLine(pen,18,13,21,16);
                }
                else {
                    var points = Enumerable.Range(0, 10).Select(i => {
                        double angle = -Math.PI / 2 + i * Math.PI / 5;
                        double radius = i % 2 == 0 ? 9 : 4;
                        return new PointF(12 + (float)(Math.Cos(angle) * radius), 12 + (float)(Math.Sin(angle) * radius));
                    }).ToArray();
                    if (kind == "starFilled") using (var fill = new SolidBrush(UiTheme.AccentFill)) g.FillPolygon(fill, points);
                    g.DrawPolygon(pen, points);
                }
            }
            return image;
        }
        private static void StyleButton(Button button, bool primary = false)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? UiTheme.Selected : UiTheme.Card;
            button.ForeColor = UiTheme.OnCard;
            button.Cursor = Cursors.Hand; button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(15, 0, 9, 0);
        }
        private Button MakeButton(string text)
        {
            var button = new RoundedButton { Text = text, Height = 66 };
            StyleButton(button); return button;
        }
        private void Navigate(string target)
        {
            section = target; Filter();
        }
        private void RenderLibrary()
        {
            if (content.IsDisposed) return;
            content.SuspendLayout();
            foreach (Control child in content.Controls.Cast<Control>().ToArray()) child.Dispose();
            int width = Math.Max(360, content.ClientSize.Width - 9 - SystemInformation.VerticalScrollBarWidth);
            bool searching = !string.IsNullOrWhiteSpace(search.Text);
            moduleGrid.SuspendLayout();
            foreach (Control child in moduleGrid.Controls.Cast<Control>().ToArray()) child.Dispose();
            var symbols = new[] { "part", "drawing", "assembly", "laser" };
            for (int i = 0; i < MacroFolder.Modules.Length; i++) {
                string module = MacroFolder.Modules[i];
                var tile = MakeButton(module);
                ((RoundedButton)tile).SecondaryText = macros.Count(m => m.category == module) + " макросов";
                tile.Image = DrawIcon(symbols[i]); ((RoundedButton)tile).LeadingIcon = true;
                tile.Disposed += (s, e) => tile.Image.Dispose();
                tile.Dock = DockStyle.Fill; tile.Margin = new Padding(6); tile.AccessibleName = module;
                if (section == module) tile.BackColor = UiTheme.Selected;
                tile.Click += (s, e) => Navigate(section == module ? null : module);
                moduleGrid.Controls.Add(tile, i, 0);
            }
            moduleGrid.ResumeLayout();
            bool home = section == null && !searching;
            string title = home ? "Избранные инструменты" : searching ? "Результаты поиска" : section;
            content.Controls.Add(new Label { Text = title, Width = width, Height = 66, TextAlign = ContentAlignment.MiddleLeft, Font = titleFont });
            var visible = VisibleMacros();
            foreach (var macro in visible) {
                var row = new TableLayoutPanel { Width = width, Height = home ? 126 : 102, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
                row.Paint += (s, e) => { using (var pen = new Pen(UiTheme.Border)) e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
                bool favorite = settings.Favorites.Contains(macro.id, StringComparer.OrdinalIgnoreCase);
                var star = MakeButton("");
                ConfigureIcon(star, favorite ? "starFilled" : "star", (favorite ? "Убрать из избранного: " : "В избранное: ") + macro.name);
                star.Dock = DockStyle.None; star.Anchor = AnchorStyles.None; star.Size = new Size(54, 54);
                star.Click += (s, e) => {
                    var previous = settings.Favorites.ToList();
                    settings.Favorites.RemoveAll(id => string.Equals(id, macro.id, StringComparison.OrdinalIgnoreCase));
                    if (!favorite) settings.Favorites.Add(macro.id);
                    try { SaveSettings(); } catch (Exception ex) { settings.Favorites = previous; ShowError("Не удалось сохранить избранное: " + ex.Message); }
                    Filter();
                };
                var name = MakeButton(macro.name);
                ((RoundedButton)name).Borderless = true;
                if (home) ((RoundedButton)name).SecondaryText = macro.category;
                name.Dock = DockStyle.Fill; name.AutoEllipsis = true;
                if ((list.SelectedItem as Macro)?.id == macro.id) name.BackColor = UiTheme.Selected;
                name.Click += (s, e) => { list.SelectedItem = macro; status.Text = "Выбран: " + macro.name; RenderLibrary(); };
                var play = MakeButton("");
                ConfigureIcon(play, "play", "Выбрать процедуру и запустить: " + macro.name);
                play.Dock = DockStyle.None; play.Anchor = AnchorStyles.None; play.Size = new Size(54, 54);
                play.Click += (s, e) => { list.SelectedItem = macro; RunSelected(true); };
                row.Controls.Add(star, 0, 0); row.Controls.Add(name, 1, 0); row.Controls.Add(play, 2, 0); content.Controls.Add(row);
            }
            if (visible.Length == 0) content.Controls.Add(new Label { Text = home ? "Нажмите ☆ рядом с макросом в разделе, чтобы добавить его в избранное." : "Макросов пока нет. Добавьте .swp в папку раздела или измените запрос поиска.", Width = width, Height = 135, ForeColor = Muted });
            if (home) {
                if (macros.Any(m => m.category == "Без раздела")) {
                    var unsorted = MakeButton("Без раздела →"); unsorted.Width = width; unsorted.Click += (s, e) => Navigate("Без раздела"); content.Controls.Add(unsorted);
                }
            }
            count.Text = macros.Length + " инструментов · Portable";
            content.ResumeLayout();
        }
        private void ShowError(string text) { status.Text = text; MessageBox.Show(this, text, "SW Tools Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void RunSelected(bool chooseEntry = false)
        {
            if (busy) return;
            var selected = list.SelectedItem as Macro;
            if (selected == null) { ShowError("Выберите инструмент."); return; }
            busy = true; run.Enabled = false; list.Enabled = false; content.Enabled = false; moduleGrid.Enabled = false;
            SldWorks sw = null; ModelDoc2 document = null;
            try
            {
                // All automation is on this application's STA UI thread; COM marshals
                // calls into the running SolidWorks process. No worker threads.
                var macro = MacroFolder.Scan(macroFolder).SingleOrDefault(m => m.id == selected.id);
                if (macro == null) throw new InvalidDataException("Инструмент удалён из каталога. Перезапустите приложение.");
                var path = macro.id;
                if (!File.Exists(path)) throw new FileNotFoundException("Файл больше не существует: " + path);
                try { sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                catch (COMException) { throw new InvalidOperationException("Не удалось подключиться к SolidWorks. Откройте SolidWorks обычным способом, без запуска от администратора, затем повторите. При нескольких экземплярах оставьте один."); }
                document = sw.IActiveDoc2;
                if (document == null || (macro.requiredDocument == "drawing" && document.GetType() != 3)) throw new InvalidOperationException("Откройте подходящий чертёж в SolidWorks. Деталь и сборка не подходят.");
                string entry;
                if (!chooseEntry && settings.Entries.TryGetValue(path, out entry)) {
                    int split = entry.LastIndexOf('.');
                    if (split > 0) { macro.module = entry.Substring(0, split); macro.procedure = entry.Substring(split + 1); }
                }
                if (chooseEntry || string.IsNullOrWhiteSpace(macro.module)) {
                    string preferred = null;
                    if (!string.IsNullOrWhiteSpace(macro.module)) preferred = macro.module + "." + macro.procedure;
                    string saved;
                    if (settings.Entries.TryGetValue(path, out saved)) preferred = saved;
                    using (var dialog = new EntryDialog(macro.name, () => sw.GetMacroMethods(path, 0), preferred)) {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        var chosen = MacroEntry.Parse(dialog.Entry);
                        macro.module = chosen.Module; macro.procedure = chosen.Procedure;
                        settings.Entries[path] = chosen.FullName; SaveSettings();
                    }
                }
                status.Text = "Макрос выполняется…"; status.Refresh();
                // The user may switch documents while choosing an entry point.
                Release(document); document = sw.IActiveDoc2;
                if (document == null || (macro.requiredDocument == "drawing" && document.GetType() != 3)) throw new InvalidOperationException("Активный документ изменился. Откройте подходящий документ и повторите запуск.");
                if (!File.Exists(path)) throw new FileNotFoundException("Файл больше не существует: " + path);
                int error;
                bool ok = sw.RunMacro2(path, macro.module, macro.procedure, 1, out error);
                if (!ok) throw new InvalidOperationException("SolidWorks вернул ошибку " + error + ". Проверьте модуль, процедуру и условия VBA.");
                status.Text = "Вызов макроса завершён. Проверьте результат на чертеже.";
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                Release(document); Release(sw);
                busy = false; list.Enabled = true; content.Enabled = true; moduleGrid.Enabled = true; run.Enabled = list.SelectedItem != null;
            }
        }
        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) {
                try { Marshal.ReleaseComObject(value); } catch (InvalidComObjectException) { }
            }
        }
    }
}
