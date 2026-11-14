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
        private Macro selectedMacro;
        private readonly Control connection = new ConnectionIndicator { Dock = DockStyle.Fill };
        private readonly Label activeDocument = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, UseMnemonic = false };
        private bool connectionReady;
        private bool checkingConnection;
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 90, AutoEllipsis = true };
        private Macro[] macros = new Macro[0];
        private bool busy;
        private bool closing;
        private bool editingHotkeys;
        private HotkeyService hotkeys;
        private string macroFolder;
        private readonly ToolTip tips = new ToolTip();
        private readonly TableLayoutPanel moduleGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(20, 4, 20, 8) };
        private readonly Label folderLabel = new Label { Dock = DockStyle.Top, Height = 42, AutoEllipsis = true };
        private readonly FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Padding = new Padding(16, 0, 16, 0) };
        private readonly Label heading = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label count = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Panel viewport = new StableViewport { Dock = DockStyle.Fill, AutoScroll = true };
        private TableLayoutPanel shell;
        private bool calculationsActive;
        private readonly Panel calculationHost = new Panel { Dock = DockStyle.Fill, Visible = false };
        private readonly Dictionary<string,ExcelBookView> excelViews = new Dictionary<string,ExcelBookView>();

        private TableLayoutPanel header;
        private RoundedPanel searchBox;
        private RoundedPanel libraryFrame;
        private bool updatingLayout;
        private readonly LibraryViewState viewState = new LibraryViewState();
        private float uiScale = 1;
        private int Px(float value) => Math.Max(1, (int)Math.Round(value * uiScale * DeviceDpi / 96f));
        private Padding Space(int left, int top, int right, int bottom) => new Padding(left == 0 ? 0 : Px(left), top == 0 ? 0 : Px(top), right == 0 ? 0 : Px(right), bottom == 0 ? 0 : Px(bottom));
        private Font titleFont, connectionFont;
        private string section;
        private static readonly Color Background = UiTheme.Background;
        private static readonly Color Surface = UiTheme.Surface;
        private static readonly Color Foreground = UiTheme.Text;
        private static readonly Color Muted = UiTheme.Muted;
        private readonly Timer refresh = new Timer { Interval = 2000 };
        private readonly string settingsPath = SettingsFile.PortablePath(AppDomain.CurrentDomain.BaseDirectory, System.Security.Principal.WindowsIdentity.GetCurrent().User.Value);
        private Settings settings = new Settings();
        public sealed class Settings {
            public string Folder { get; set; }
            // Retain legacy data when saving other settings; no description UI remains.
            public Dictionary<string, string> Descriptions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> Hotkeys { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<string> Favorites { get; set; } = new List<string>();
            public Dictionary<string, string> Entries { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        private void SaveSettings() {
            SettingsFile.Write(settingsPath, new JavaScriptSerializer().Serialize(settings));
        }
        private void UpdateLibraryTip()
        {
            var choose = header.GetControlFromPosition(3, 0);
            string tip = "Текущая библиотека: " + macroFolder + "\nВыбрать другую папку";
            if (tips.GetToolTip(choose) != tip) tips.SetToolTip(choose, tip);
        }
        private void ReloadFolder() {
            if (busy || editingHotkeys) return;
            try {
                string resolved = MacroFolder.ResolveStartup(settings.Folder, root);
                if (!string.Equals(macroFolder, resolved, StringComparison.OrdinalIgnoreCase)) {
                    macroFolder = resolved; settings.Folder = resolved;
                    viewState.Invalidate();
                }
                UpdateLibraryTip();
                var next = MacroFolder.Scan(macroFolder);
                bool changed = !next.Select(m => m.id).SequenceEqual(macros.Select(m => m.id));
                if (changed) { macros = next; viewState.Invalidate(); Filter(); }
                if (changed || string.IsNullOrEmpty(status.Text)) status.Text = next.Length == 0 ? "В выбранной папке нет .swp. Добавьте файлы или выберите другую папку." : "Найдено макросов: " + next.Length;
            } catch (Exception ex) { macros = new Macro[0]; Filter(); status.Text = "Папка недоступна: " + ex.Message; }
        }
        public Hub()
        {
            Text = "SW-Assistant";
            using (var stream = typeof(Hub).Assembly.GetManifestResourceStream("AppIcon.ico"))
            using (var icon = new Icon(stream)) Icon = (Icon)icon.Clone();
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(900, 600); MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiTheme.CreateFont(); titleFont = UiTheme.CreateSemiBoldFont(); connectionFont = new Font(Font.FontFamily, 11f);
            activeDocument.Font = connectionFont; BackColor = Background; ForeColor = Foreground;
            shell = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, Padding = new Padding(0) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var height in new[] { 60, 50, 26, 106 }) shell.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(20, 6, 16, 6) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 2; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(52)));
            var choose = MakeButton("");
            ConfigureIcon(choose, "folder", "Выбрать корневую папку библиотеки");
            var shortcuts = MakeButton("");
            ConfigureIcon(shortcuts, "keyboard", "Горячие клавиши");
            shortcuts.Click += (s, e) => EditHotkeys();
            header.Controls.Add(connection, 0, 0); header.Controls.Add(activeDocument, 1, 0); header.Controls.Add(shortcuts, 2, 0); header.Controls.Add(choose, 3, 0);
            searchBox = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 8), Margin = new Padding(20, 0, 20, 8), BackColor = Surface };
            search.BackColor = Surface; search.ForeColor = Foreground; search.AccessibleName = "Поиск макросов";
            searchBox.Controls.Add(search); tips.SetToolTip(search, "Поиск по выбранному разделу; на главной — по всей библиотеке");
            heading.Padding = new Padding(20, 0, 0, 0);
            heading.Text = "Поиск макросов"; heading.ForeColor = Muted;
            status.Height = 44; status.Padding = new Padding(20, 4, 20, 0); status.ForeColor = Muted;
            folderLabel.Visible = false;
            for (int i = 0; i < 4; i++) moduleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            moduleGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            count.Padding = new Padding(20, 0, 0, 0); count.ForeColor = Muted;
            connection.ForeColor = Color.FromArgb(190, 65, 50);
            activeDocument.TextAlign = ContentAlignment.MiddleLeft;
            activeDocument.ForeColor = Muted;
            shell.Controls.Add(header, 0, 0); shell.Controls.Add(searchBox, 0, 1); shell.Controls.Add(heading, 0, 2);
            libraryFrame = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(20, 10, 20, 20), Padding = new Padding(14, 10, 14, 12) };
            content.BackColor = Surface; content.Padding = new Padding(0);
            libraryFrame.Controls.Add(content);
            shell.Controls.Add(moduleGrid, 0, 3); shell.Controls.Add(libraryFrame, 0, 4);
            viewport.Controls.Add(shell); Controls.Add(viewport);
            InitializeCalculations();
            viewport.SizeChanged += (s, e) => RefreshLayout();
            choose.Click += (s, e) => {
                if (busy) return;
                try {
                    var selectedFolder = FolderPicker.Choose(Handle, macroFolder);
                    if (selectedFolder == null) return;
                    selectedFolder = MacroFolder.NormalizeRoot(selectedFolder);
                    MacroFolder.EnsureModules(selectedFolder);
                    settings.Folder = selectedFolder; SaveSettings(); macroFolder = selectedFolder;
                    tips.SetToolTip(choose, "Библиотека: " + macroFolder + "\nВыбрать другую корневую папку");
                    folderLabel.Text = macroFolder; ReloadFolder();
                } catch (Exception ex) { ShowError("Не удалось выбрать папку: " + ex.Message); }
            };
            refresh.Tick += (s, e) => { if (!calculationsActive) { ReloadFolder(); RefreshConnection(); } };
            FormClosed += (s, e) => { refresh.Dispose(); hotkeys?.Dispose(); tips.Dispose(); titleFont.Dispose(); connectionFont.Dispose(); };
            search.TextChanged += (s, e) => Filter();
            FormClosing += (s, e) => { if (busy) e.Cancel = true; else closing = true; };
            Shown += (s, e) => {
                try { SettingsFile.MigrateUserSettings(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settingsPath); }
                catch (Exception ex) { ShowError("Не удалось перенести настройки в SW-Assistant: " + ex.Message); Close(); return; }
                try {
                    if (File.Exists(settingsPath)) settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings();
                } catch { settings = new Settings(); }
                if (settings.Favorites == null) settings.Favorites = new List<string>();
                settings.Descriptions = new Dictionary<string, string>(settings.Descriptions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                settings.Entries = new Dictionary<string, string>(settings.Entries ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                settings.Hotkeys = new Dictionary<string, int>(settings.Hotkeys ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
                macroFolder = MacroFolder.ResolveStartup(settings.Folder, root);
                folderLabel.Text = macroFolder;
                try { if (string.Equals(Path.GetFullPath(macroFolder), Path.GetFullPath(Path.Combine(root, "Library", "Macros")), StringComparison.OrdinalIgnoreCase)) MacroFolder.EnsureModules(macroFolder); } catch (Exception ex) { status.Text = "Не удалось создать папки разделов: " + ex.Message; }
                ReloadFolder(); RefreshLayout(); refresh.Start();
                RestoreHotkeys(); RefreshConnection();
            };
        }
        private void InitializeCalculations()
        {
            // Keep macro UI intact; the new navigation occupies only the left edge.
            var navigation = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 218, Padding = new Padding(18, 28, 18, 12), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.FromArgb(10,22,42) };
            Controls.Add(calculationHost); Controls.Add(navigation);
            var macroButton = MakeButton("Макросы"); macroButton.Width = 182;
            navigation.Controls.Add(new Label { Text = "◇  SW-Assistant", AutoSize = true, ForeColor = Color.White, Font = UiTheme.CreateSemiBoldFont(16), Margin = new Padding(0,0,0,24) });
            navigation.Controls.Add(macroButton);
            macroButton.Click += (s, e) => {
                if (busy || editingHotkeys) return;
                calculationsActive = false; calculationHost.Visible = false; viewport.Visible = true;
                ReloadFolder(); RefreshLayout(); RefreshConnection();
            };
            ExcelLibrary library;
            try { library = new ExcelLibrary(); }
            catch (Exception ex) { status.Text = "Библиотека Excel недоступна: " + ex.Message; return; }
            Action<ExcelEntry,ExcelBook> openBook = (entry, loaded) => {
                if (busy || editingHotkeys) { loaded?.Dispose(); return; }
                try {
                    ExcelBookView view;
                    if (!excelViews.TryGetValue(entry.Id, out view)) {
                        view = new ExcelBookView(library, entry, loaded);
                        excelViews.Add(entry.Id, view); calculationHost.Controls.Add(view);
                        view.ExpandRequested += () => navigation.Visible = !navigation.Visible;
                        view.DeleteRequested += () => {
                            if (MessageBox.Show(this, "Удалить сохранённую копию «" + entry.Title + "»? Исходный XLSX останется.", "Удалить таблицу", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                            try {
                                library.Delete(entry); calculationHost.Controls.Remove(view); excelViews.Remove(entry.Id); view.Dispose();
                                calculationsActive = false; calculationHost.Visible = false; viewport.Visible = true; navigation.Visible = true; ReloadFolder(); RefreshLayout();
                            } catch (Exception ex) { ShowError("Не удалось удалить книгу: " + ex.Message); }
                        };
                    } else loaded?.Dispose();
                    foreach (Control child in calculationHost.Controls) child.Visible = child == view;
                    calculationsActive = true; viewport.Visible = false; calculationHost.Visible = true; view.BringToFront();
                } catch (Exception ex) { loaded?.Dispose(); ShowError("Не удалось открыть книгу: " + ex.Message); }
            };
            var addExcel = MakeButton("＋ Добавить файл"); addExcel.Width = 182; addExcel.Height = 54;
            addExcel.Click += (s,e) => {
                if (busy || editingHotkeys) return;
                using (var dialog = new OpenFileDialog { Filter = "Книги Excel (*.xlsx)|*.xlsx", Multiselect = false }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ExcelBook loaded = null;
                    try {
                        string file = dialog.FileName;
                        if (new FileInfo(file).Length > 15 * 1024 * 1024) throw new InvalidDataException("Максимальный размер XLSX — 15 МБ");
                        byte[] bytes = File.ReadAllBytes(file);
                        var entry = library.Entries().FirstOrDefault(e => e.FileName == Path.GetFileName(file) && new FileInfo(library.OriginalPath(e)).Length == bytes.Length && File.ReadAllBytes(library.OriginalPath(e)).SequenceEqual(bytes));
                        loaded = new ExcelBook(bytes);
                        if (entry == null) entry = library.Add(Path.GetFileName(file), bytes);
                        openBook(entry, loaded); loaded = null;
                    } catch (Exception ex) { ShowError("Не удалось добавить файл: " + ex.Message); }
                    finally { loaded?.Dispose(); }
                }
            };
            navigation.Controls.Add(addExcel);
            FormClosing += (s,e) => { if(!busy && excelViews.Values.Any(v => v.Unsaved) && MessageBox.Show(this,"Есть несохранённые изменения Excel. Закрыть приложение?","Несохранённый ввод",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes){e.Cancel=true;closing=false;} };
        }
        private void Filter()
        {
            var selectedId = selectedMacro?.id;
            selectedMacro = VisibleMacros().FirstOrDefault(m => m.id == selectedId);
            if (!viewState.NeedsUpdate(section, !string.IsNullOrWhiteSpace(search.Text), VisibleMacros().Select(m => m.id))) return;
            RefreshLayout();
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
            StyleButton(button); ((RoundedButton)button).IconOnly = true; ((RoundedButton)button).AccentHover = true; button.BackColor = UiTheme.IconSurface; button.ForeColor = UiTheme.IconInk; button.Text = ""; button.Dock = DockStyle.Fill;
            ((RoundedButton)button).IconSize = Px(20.4f);
            button.Margin = new Padding(Px(2)); button.Padding = new Padding(0);
            button.Image = DrawIcon(icon); button.ImageAlign = ContentAlignment.MiddleCenter;
            button.AccessibleName = description; tips.SetToolTip(button, description);
            button.Disposed += (s, e) => button.Image.Dispose();
        }
        private static Bitmap DrawIcon(string kind)
        {
            var image = new Bitmap(72, 72);
            using (var g = Graphics.FromImage(image))
            using (var pen = new Pen(UiTheme.AccentInk, 1.35f)) {
                g.ScaleTransform(3, 3);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                if (kind == "keyboard") {
                    g.DrawRectangle(pen, 2, 5, 20, 14);
                    foreach (int y in new[] { 9, 12 }) foreach (int x in new[] { 6, 10, 14, 18 }) g.DrawLine(pen, x, y, x + .2f, y);
                    g.DrawLine(pen, 7, 16, 17, 16);
                }
                else if (kind == "folder") g.DrawPolygon(pen, new[] { new Point(3, 6), new Point(10, 6), new Point(12, 9), new Point(21, 9), new Point(21, 19), new Point(3, 19) });
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
        private void StyleButton(Button button, bool primary = false)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? UiTheme.Selected : UiTheme.Card;
            button.ForeColor = UiTheme.OnCard;
            button.Cursor = Cursors.Hand; button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = Space(10, 0, 6, 0);
        }
        private Button MakeButton(string text)
        {
            var button = new RoundedButton { Text = text, Height = Px(44) };
            StyleButton(button); return button;
        }
        private void Navigate(string target)
        {
            section = target; Filter();
        }
        private void RefreshLayout()
        {
            if (updatingLayout || shell == null || IsDisposed) return;
            updatingLayout = true;
            using (var redraw = new RedrawBatch(viewport))
            try {
                int reservedScrollbar = SystemInformation.VerticalScrollBarWidth;
                int viewportWidth = LibraryViewState.FullWidth(viewport.ClientSize.Width, viewport.VerticalScroll.Visible, reservedScrollbar);
                float logicalWidth = viewportWidth * 96f / DeviceDpi;
                uiScale = Math.Max(.8f, Math.Min(1.5f, logicalWidth / 900f));
                bool narrow = logicalWidth < 720;
                shell.SuspendLayout();
                shell.Width = Math.Max(1, viewportWidth - reservedScrollbar);
                shell.Margin = new Padding(0);
                var heights = new[] { Px(60), Math.Max(Px(50), Font.Height + Px(22)), Math.Max(Px(26), Font.Height + Px(4)), Px(narrow ? 212 : 106) };
                for (int i = 0; i < heights.Length; i++) shell.RowStyles[i].Height = heights[i];
                header.Padding = Space(20, 6, 16, 6);
                header.ColumnStyles[0].Width = Px(22);
                for (int i = 2; i < header.ColumnStyles.Count; i++) header.ColumnStyles[i].Width = Px(52);
                foreach (var button in header.Controls.OfType<RoundedButton>()) {
                    button.Dock = DockStyle.None; button.Anchor = AnchorStyles.None;
                    button.Size = new Size(Px(46), Px(46)); button.IconSize = Px(20.4f); button.Margin = new Padding(Px(2));
                }
                searchBox.Padding = Space(12, 12, 12, 8); searchBox.Margin = Space(20, 0, 20, 8);
                heading.Padding = Space(20, 0, 0, 0);
                moduleGrid.Margin = Space(20, 4, 20, 8);
                libraryFrame.Margin = Space(20, 10, 20, 20); libraryFrame.Padding = Space(14, 10, 14, 12);
                int columns = narrow ? 2 : 4, rows = narrow ? 2 : 1;
                if (moduleGrid.ColumnCount != columns || moduleGrid.RowCount != rows) {
                    foreach (Control child in moduleGrid.Controls.Cast<Control>().ToArray()) child.Dispose();
                    moduleGrid.ColumnCount = columns; moduleGrid.RowCount = rows;
                    moduleGrid.ColumnStyles.Clear(); moduleGrid.RowStyles.Clear();
                    for (int i = 0; i < columns; i++) moduleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
                    for (int i = 0; i < rows; i++) moduleGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
                }
                shell.ResumeLayout(true);
                int visibleCount = VisibleMacros().Length;
                bool home = section == null && string.IsNullOrWhiteSpace(search.Text);
                int listHeight = Px(44) + (visibleCount == 0 ? Px(100) : visibleCount * (Px(72) + Px(4)));
                if (home && macros.Any(m => m.category == "Без раздела")) listHeight += Px(48);
                int frameHeight = Math.Max(Px(240), listHeight + libraryFrame.Padding.Vertical + libraryFrame.Margin.Vertical);
                shell.RowStyles[4].SizeType = SizeType.Absolute;
                int naturalHeight = heights.Sum() + frameHeight;
                int pageHeight = Math.Max(viewport.ClientSize.Height, naturalHeight);
                shell.RowStyles[4].Height = frameHeight + pageHeight - naturalHeight;
                shell.Height = pageHeight;
                viewport.AutoScrollMinSize = new Size(0, pageHeight);
                shell.Location = new Point(0, viewport.AutoScrollPosition.Y);
                shell.PerformLayout();
                RenderLibrary();
            } finally { updatingLayout = false; }
        }
        private static string MacroCount(int value)
        {
            int last = value % 10, pair = value % 100;
            string word = pair >= 11 && pair <= 14 ? "макросов" : last == 1 ? "макрос" : last >= 2 && last <= 4 ? "макроса" : "макросов";
            return value + " " + word;
        }
        private void RenderLibrary()
        {
            if (content.IsDisposed) return;
            content.SuspendLayout();
            foreach (Control child in content.Controls.Cast<Control>().ToArray()) child.Dispose();
            int width = Math.Max(1, content.ClientSize.Width - Px(6));
            bool searching = !string.IsNullOrWhiteSpace(search.Text);
            moduleGrid.SuspendLayout();
            var symbols = new[] { "star", "drawing", "assembly", "laser" };
            var views = new[] { "Избранное", "Чертежи", "Сборки", "Лазер" };
            for (int i = 0; i < views.Length; i++) {
                string module = views[i];
                bool favoritesView = i == 0;
                var tile = i < moduleGrid.Controls.Count ? (RoundedButton)moduleGrid.Controls[i] : (RoundedButton)MakeButton(module);
                bool newTile = tile.Parent == null;
                ((RoundedButton)tile).SecondaryText = MacroCount(macros.Count(m => favoritesView ? settings.Favorites.Contains(m.id, StringComparer.OrdinalIgnoreCase) : m.category == module));
                ((RoundedButton)tile).AccentHover = true;
                if (newTile) tile.Image = DrawIcon(symbols[i]); ((RoundedButton)tile).LeadingIcon = true; ((RoundedButton)tile).IconSize = Px(20.4f);
                if (newTile) tile.Disposed += (s, e) => tile.Image.Dispose();
                tile.Dock = DockStyle.Fill; tile.Margin = new Padding(Px(4)); tile.AccessibleName = module;
                tile.BackColor = (favoritesView ? section == null : section == module) ? UiTheme.Selected : UiTheme.Card;
                if (newTile) tile.Click += (s, e) => { if (favoritesView) { search.Clear(); Navigate(null); } else Navigate(module); };
                if (newTile) moduleGrid.Controls.Add(tile, i % moduleGrid.ColumnCount, i / moduleGrid.ColumnCount);
            }
            moduleGrid.ResumeLayout();
            bool home = section == null && !searching;
            string title = home ? "Избранные инструменты" : searching ? "Результаты поиска" : section;
            content.Controls.Add(new Label { Text = title, Margin = new Padding(0), Width = width, Height = Px(44), TextAlign = ContentAlignment.MiddleLeft, Font = titleFont });
            var visible = VisibleMacros();
            foreach (var macro in visible) {
                var row = new TableLayoutPanel { Width = width, Height = Px(72), ColumnCount = 4, RowCount = 1, Margin = Space(0, 0, 0, 4) };
                row.Paint += (s, e) => { using (var pen = new Pen(UiTheme.Border)) e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(52))); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(180))); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(52)));
                bool favorite = settings.Favorites.Contains(macro.id, StringComparer.OrdinalIgnoreCase);
                var star = MakeButton("");
                ConfigureIcon(star, favorite ? "starFilled" : "star", (favorite ? "Убрать из избранного: " : "В избранное: ") + macro.name);
                star.Dock = DockStyle.None; star.Anchor = AnchorStyles.None; star.Size = new Size(Px(46), Px(46));
                star.Click += (s, e) => {
                    var previous = settings.Favorites.ToList();
                    settings.Favorites.RemoveAll(id => string.Equals(id, macro.id, StringComparison.OrdinalIgnoreCase));
                    if (!favorite) settings.Favorites.Add(macro.id);
                    try { SaveSettings(); } catch (Exception ex) { settings.Favorites = previous; ShowError("Не удалось сохранить избранное: " + ex.Message); }
                    viewState.Invalidate(); Filter();
                };
                var name = MakeButton(macro.name);
                ((RoundedButton)name).Borderless = true;
                ((RoundedButton)name).AccentHover = false; name.Cursor = Cursors.Default;
                ((RoundedButton)name).SecondaryText = macro.category;
                tips.SetToolTip(name, macro.name);
                var shortcutField = CreateInlineHotkey(macro);
                name.Dock = DockStyle.Fill; name.AutoEllipsis = true;
                if (selectedMacro?.id == macro.id) name.BackColor = UiTheme.Selected;
                name.Click += (s, e) => { selectedMacro = macro; status.Text = "Выбран: " + macro.name; };
                var play = MakeButton("");
                ConfigureIcon(play, "play", "Запустить: " + macro.name);
                play.Tag = "run"; play.Enabled = connectionReady;
                play.Dock = DockStyle.None; play.Anchor = AnchorStyles.None; play.Size = new Size(Px(46), Px(46));
                play.Click += (s, e) => { selectedMacro = macro; RunSelected(macro); };
                row.Controls.Add(play, 0, 0); row.Controls.Add(name, 1, 0); row.Controls.Add(shortcutField, 2, 0); row.Controls.Add(star, 3, 0); content.Controls.Add(row);
            }
            if (visible.Length == 0) content.Controls.Add(new Label { Text = macros.Length == 0 ? "Библиотека макросов пуста\n" + macroFolder + "\nВыберите папку библиотеки кнопкой папки." : home ? "Нажмите ☆ рядом с макросом в разделе, чтобы добавить его в избранное." : "Макросов пока нет. Добавьте .swp в папку раздела или измените запрос поиска.", Margin = new Padding(0), Width = width, Height = Px(100), ForeColor = Muted });
            if (home) {
                if (macros.Any(m => m.category == "Без раздела")) {
                    var unsorted = MakeButton("Без раздела →"); unsorted.Width = width; unsorted.Margin = Space(0, 0, 0, 4); unsorted.Click += (s, e) => Navigate("Без раздела"); content.Controls.Add(unsorted);
                }
            }
            count.Text = macros.Length + " инструментов · Portable";
            content.ResumeLayout();
        }
        private Control CreateInlineHotkey(Macro macro)
        {
            var frame = new RoundedPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = UiTheme.ControlHeight, CornerRadius = UiTheme.ControlRadius, BackColor = Background, Margin = Space(4, 0, 4, 0) };
            var field = new HotkeyTextBox { ReadOnly = true, ShortcutsEnabled = false, BorderStyle = BorderStyle.None, BackColor = Background, ForeColor = Foreground, CueText = "Назначить клавиши", AccessibleName = "Горячая клавиша: " + macro.name };
            frame.Controls.Add(field);
            Action layoutField = () => { int inset = Math.Max(1, (int)Math.Round(12f * frame.DeviceDpi / 96f)); field.SetBounds(inset, Math.Max(0, (frame.Height - field.PreferredHeight) / 2), Math.Max(1, frame.Width - inset * 2), field.PreferredHeight); };
            frame.SizeChanged += (s, e) => layoutField(); field.FontChanged += (s, e) => layoutField(); field.HandleCreated += (s, e) => layoutField();
            Action showSaved = () => { int key; field.Text = settings.Hotkeys.TryGetValue(macro.id, out key) ? Hotkey.Format(key) : ""; };
            showSaved();
            Dictionary<string, int> restore = null;
            Action resetError = () => { frame.BorderColor = field.Focused ? UiTheme.AccentInk : UiTheme.Border; frame.Invalidate(); tips.Hide(field); };
            Action<string> error = text => { frame.BorderColor = Color.FromArgb(190, 65, 50); frame.Invalidate(); tips.Show(text, field, 0, field.Height + 4, 5000); };
            field.Enter += (s, e) => {
                if (busy || editingHotkeys || hotkeys == null) { BeginInvoke((Action)(() => search.Focus())); return; }
                restore = hotkeys.Snapshot(); string ignored; hotkeys.TryApply(new Dictionary<string, int>(), out ignored);
                editingHotkeys = true; resetError();
            };
            Action finish = () => {
                if (restore == null) return;
                var previous = restore; restore = null;
                string failure;
                if (!closing && !IsDisposed && !hotkeys.TryApply(previous, out failure)) ShowError(failure);
                editingHotkeys = false;
                if (!field.IsDisposed) { showSaved(); frame.BorderColor = UiTheme.Border; frame.Invalidate(); tips.Hide(field); }
            };
            field.Leave += (s, e) => finish(); field.Disposed += (s, e) => finish();
            frame.MouseDown += (s, e) => field.Focus();
            field.PreviewKeyDown += (s, e) => { if (e.KeyCode != Keys.Tab) e.IsInputKey = true; };
            field.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Tab) return;
                e.SuppressKeyPress = true; e.Handled = true;
                if (e.KeyCode == Keys.Escape) { showSaved(); resetError(); search.Focus(); return; }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || restore == null) return;
                bool clear = (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && e.Modifiers == Keys.None;
                int value = (int)e.KeyData;
                if (!clear && !Hotkey.IsValid(value)) { error("Используйте Ctrl, Alt или Shift вместе с буквой, цифрой или F-клавишей."); return; }
                var next = new Dictionary<string, int>(settings.Hotkeys, StringComparer.OrdinalIgnoreCase);
                next.Remove(macro.id);
                if (!clear) {
                    field.Text = Hotkey.Format(value);
                    var duplicate = next.FirstOrDefault(p => p.Value == value);
                    if (duplicate.Key != null) { error("Уже назначено: " + Path.GetFileNameWithoutExtension(duplicate.Key) + ". Выберите другое сочетание."); return; }
                    next[macro.id] = value;
                }
                string failure;
                if (!hotkeys.TryApply(next, out failure)) { error(failure); return; }
                var old = settings.Hotkeys; settings.Hotkeys = next;
                try { SaveSettings(); restore = new Dictionary<string, int>(next, StringComparer.OrdinalIgnoreCase); showSaved(); resetError(); }
                catch (Exception ex) { settings.Hotkeys = old; showSaved(); error("Не удалось сохранить сочетание: " + ex.Message); }
                finally { string ignored; hotkeys.TryApply(new Dictionary<string, int>(), out ignored); }
            };
            tips.SetToolTip(field, "Нажмите сочетание. Backspace — удалить; Escape — отменить незавершённый ввод.");
            return frame;
        }
        private void RefreshConnection()
        {
            if (busy || editingHotkeys || !Enabled || checkingConnection) return;
            checkingConnection = true;
            SldWorks sw = null; ModelDoc2 document = null;
            string state = "SolidWorks не найден", documentText = "Откройте SolidWorks", fullPath = "";
            bool ready = false, connected = false;
            try {
                sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                connected = true; state = "SolidWorks подключён"; document = sw.IActiveDoc2;
                documentText = "Документ не открыт";
                if (document != null) {
                    fullPath = document.GetPathName();
                    documentText = DocumentStatus.TypeName(document.GetType()) + " · " + DocumentStatus.Name(fullPath, document.GetTitle());
                    ready = true;
                }
            } catch (COMException ex) {
                connected = false; state = "SolidWorks не найден"; documentText = "Откройте SolidWorks";
                if (ex.ErrorCode == unchecked((int)0x80010001) || ex.ErrorCode == unchecked((int)0x8001010A)) { connected = true; state = "SolidWorks занят"; documentText = "Ожидание ответа…"; }
            } catch { connected = false; state = "Нет ответа SolidWorks"; documentText = "Повторная проверка через 2 секунды"; }
            finally { Release(document); Release(sw); checkingConnection = false; }
            if (tips.GetToolTip(connection) != state) tips.SetToolTip(connection, state);
            if (activeDocument.Text != documentText) activeDocument.Text = documentText;
            string documentTip = string.IsNullOrEmpty(fullPath) ? documentText : fullPath;
            if (tips.GetToolTip(activeDocument) != documentTip) tips.SetToolTip(activeDocument, documentTip);
            connection.ForeColor = connected ? Color.FromArgb(65, 145, 80) : Color.FromArgb(190, 65, 50);
            if (connectionReady != ready) {
                connectionReady = ready;
                foreach (var row in content.Controls.OfType<TableLayoutPanel>())
                    foreach (var button in row.Controls.OfType<Button>().Where(b => Equals(b.Tag, "run"))) button.Enabled = ready;
            }
        }
        private void RestoreHotkeys()
        {
            hotkeys = new HotkeyService(new NativeHotkeys(Handle));
            var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            foreach (var pair in settings.Hotkeys) {
                available[pair.Key] = pair.Value;
                string error;
                if (!hotkeys.TryApply(available, out error)) {
                    available.Remove(pair.Key); failures.Add(Path.GetFileNameWithoutExtension(pair.Key) + ": " + error);
                }
            }
            if (failures.Count > 0) ShowError("Не все горячие клавиши включены. Назначения сохранены; исправьте их кнопкой клавиатуры.\n\n" + string.Join("\n", failures));
        }
        private void EditHotkeys()
        {
            if (busy || editingHotkeys || hotkeys == null) return;
            editingHotkeys = true;
            var active = hotkeys.Snapshot(); string ignored;
            hotkeys.TryApply(new Dictionary<string, int>(), out ignored);
            bool saved = false;
            try {
                // Freeze the table while it is open; new files appear on the next opening.
                var current = MacroFolder.Scan(macroFolder);
                using (var dialog = new HotkeyDialog(current, settings.Hotkeys, next => {
                    string error;
                    if (!hotkeys.TryApply(next, out error)) return error;
                    var previous = settings.Hotkeys;
                    settings.Hotkeys = next;
                    try { SaveSettings(); }
                    catch (Exception ex) {
                        settings.Hotkeys = previous;
                        hotkeys.TryApply(new Dictionary<string, int>(), out ignored);
                        return "Не удалось сохранить назначения: " + ex.Message;
                    }
                    saved = true; viewState.Invalidate(); Filter(); return null;
                })) dialog.ShowDialog(this);
            } catch (Exception ex) { ShowError("Не удалось открыть горячие клавиши: " + ex.Message); }
            finally {
                if (!saved && !hotkeys.TryApply(active, out ignored)) ShowError(ignored);
                editingHotkeys = false;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) { // WM_HOTKEY arrives on the main STA UI thread.
                string path = hotkeys?.Resolve(m.WParam.ToInt32());
                if (path != null && !busy && !editingHotkeys && !calculationsActive && Enabled) {
                    // Do not queue a run: a stale registration or changed library cannot redirect it.
                    RunSelected(new Macro { id = path });
                }
                return;
            }
            base.WndProc(ref m);
        }
        private void ShowError(string text) { status.Text = text; MessageBox.Show(this, text, "SW-Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void RunSelected(Macro selected)
        {
            if (busy) return;
            if (selected == null) { ShowError("Выберите инструмент."); return; }
            busy = true; content.Enabled = false; moduleGrid.Enabled = false;
            SldWorks sw = null; ModelDoc2 document = null;
            string launchContext = "";
            try
            {
                // All automation is on this application's STA UI thread; COM marshals
                // calls into the running SolidWorks process. No worker threads.
                var macro = MacroFolder.Scan(macroFolder).SingleOrDefault(m => string.Equals(m.id, selected.id, StringComparison.OrdinalIgnoreCase));
                if (macro == null) throw new InvalidDataException("Макрос недоступен в выбранной библиотеке. Проверьте файл и папку библиотеки; назначение можно удалить кнопкой клавиатуры.");
                var path = macro.id;
                if (!File.Exists(path)) throw new FileNotFoundException("Файл больше не существует: " + path);
                try { sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                catch (COMException) { throw new InvalidOperationException("Не удалось подключиться к SolidWorks. Откройте SolidWorks обычным способом, без запуска от администратора, затем повторите. При нескольких экземплярах оставьте один."); }
                document = sw.IActiveDoc2;
                if (macro.requiredDocument == "drawing" && (document == null || document.GetType() != 3)) throw new InvalidOperationException("Откройте подходящий чертёж в SolidWorks. Деталь и сборка не подходят.");
                string preferred = string.IsNullOrWhiteSpace(macro.module) ? null : macro.module + "." + macro.procedure;
                string saved;
                if (settings.Entries.TryGetValue(path, out saved)) preferred = saved;
                MacroEntry[] methods;
                try { methods = MacroEntry.FromCom(sw.GetMacroMethods(path, 0)); }
                catch (COMException) { methods = new MacroEntry[0]; }
                var chosen = MacroEntry.AutoSelect(methods, preferred);
                if (methods.Length != 1) {
                    using (var dialog = new EntryDialog(macro.name, () => sw.GetMacroMethods(path, 0), preferred)) {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        chosen = MacroEntry.Parse(dialog.Entry);
                    }
                }
                if (chosen == null) throw new InvalidOperationException("SolidWorks не вернул точку входа макроса, а сохранённой или известной процедуры нет. Запуск невозможен без имени модуля и процедуры.");
                macro.module = chosen.Module; macro.procedure = chosen.Procedure;
                status.Text = "Макрос выполняется…"; status.Refresh();
                // The user may switch documents while choosing an entry point.
                Release(document); document = sw.IActiveDoc2;
                if (macro.requiredDocument == "drawing" && (document == null || document.GetType() != 3)) throw new InvalidOperationException("Активный документ изменился. Откройте подходящий документ и повторите запуск.");
                if (!File.Exists(path)) throw new FileNotFoundException("Файл больше не существует: " + path);
                launchContext = "\nФайл: " + path + "\nТочка входа: " + chosen.FullName;
                int error;
                // Use the standard run mode: forced unload can close modeless VBA forms.
                bool ok = sw.RunMacro2(path, macro.module, macro.procedure, 0, out error);
                if (!ok) throw new InvalidOperationException("SolidWorks не запустил макрос. Код: " + error + " (0x" + error.ToString("X8") + ").\nФайл: " + path + "\nТочка входа: " + chosen.FullName + "\nПроверьте, что выбрана та же процедура, что при ручном запуске. В редакторе VBA проверьте Debug → Compile и Tools → References (MISSING). Автоматический повтор не выполнялся.");
                settings.Entries[path] = chosen.FullName; SaveSettings();
                status.Text = "Вызов макроса завершён. Проверьте результат на чертеже.";
            }
            catch (COMException ex) { ShowError("Ошибка COM 0x" + ex.ErrorCode.ToString("X8") + ": " + ex.Message + launchContext + "\nSolidWorks и SW-Assistant должны работать с одинаковыми правами; завершите диалог в SolidWorks и повторите."); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                Release(document); Release(sw);
                busy = false; content.Enabled = true; moduleGrid.Enabled = true; RefreshConnection();
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
