using System;
using System.IO;
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
            Application.Run(new Hub());
        }
    }
    internal sealed class Hub : Form
    {
        private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        private readonly TextBox search = new TextBox { Dock = DockStyle.Top };
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "name" };
        private readonly Button run = new Button { Dock = DockStyle.Bottom, Height = 44, Text = "Запустить в SolidWorks" };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 90, AutoEllipsis = true };
        private Macro[] macros = new Macro[0];
        private bool busy;
        private string macroFolder;
        private readonly Label folderLabel = new Label { Dock = DockStyle.Top, Height = 42, AutoEllipsis = true };
        private readonly Timer refresh = new Timer { Interval = 2000 };
        private readonly string settingsPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SWToolsHub", "portable.json");
        private Settings settings = new Settings();
        public sealed class Settings {
            public string Folder { get; set; }
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
            Text = "SW Tools Hub — локальные макросы";
            Width = 440; Height = 520; MinimumSize = new System.Drawing.Size(320, 380);
            var hint = new Label { Dock = DockStyle.Top, Height = 52, Text = "Откройте SolidWorks и подходящий чертёж.\nПоиск инструмента:" };
            var choose = new Button { Dock = DockStyle.Top, Height = 34, Text = "Выбрать корневую папку макросов…" };
            var folder = new Button { Dock = DockStyle.Bottom, Height = 36, Text = "Открыть папку для .swp" };
            Controls.Add(list); Controls.Add(search); Controls.Add(hint); Controls.Add(folderLabel); Controls.Add(choose);
            var entryButton = new Button { Dock = DockStyle.Bottom, Height = 32, Text = "Выбрать процедуру и запустить…" };
            entryButton.Click += (s, e) => RunSelected(true);
            Controls.Add(status); Controls.Add(entryButton); Controls.Add(run); Controls.Add(folder);
            folder.Click += (s, e) => {
                try { var path = macroFolder; if (string.IsNullOrEmpty(path)) return; Directory.CreateDirectory(path); System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\""); }
                catch (Exception ex) { ShowError(ex.Message); }
            };
            choose.Click += (s, e) => {
                if (busy) return;
                using (var dialog = new FolderBrowserDialog { Description = "Папка с рабочими .swp (без вложенных папок)", SelectedPath = macroFolder }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { settings.Folder = dialog.SelectedPath; SaveSettings(); macroFolder = settings.Folder; folderLabel.Text = macroFolder; ReloadFolder(); }
                    catch (Exception ex) { ShowError("Не удалось сохранить выбор папки: " + ex.Message); }
                }
            };
            refresh.Tick += (s, e) => ReloadFolder();
            FormClosed += (s, e) => refresh.Dispose();
            search.TextChanged += (s, e) => Filter();
            list.SelectedIndexChanged += (s, e) => run.Enabled = !busy && list.SelectedItem != null;
            run.Click += (s, e) => RunSelected();
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            Shown += (s, e) => {
                try {
                    if (File.Exists(settingsPath)) settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new Settings();
                } catch { settings = new Settings(); }
                if (settings.Entries == null) settings.Entries = new Dictionary<string, string>();
                macroFolder = string.IsNullOrWhiteSpace(settings.Folder) ? Path.Combine(root, "Library", "Macros") : settings.Folder;
                folderLabel.Text = macroFolder;
                ReloadFolder(); refresh.Start();
            };
        }
        private void Filter()
        {
            var selectedId = (list.SelectedItem as Macro)?.id;
            list.DataSource = macros.Where(m => m.name.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (selectedId != null) list.SelectedItem = macros.FirstOrDefault(m => m.id == selectedId);
            run.Enabled = !busy && list.SelectedItem != null;
        }
        private void ShowError(string text) { status.Text = text; MessageBox.Show(this, text, "SW Tools Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void RunSelected(bool chooseEntry = false)
        {
            if (busy) return;
            var selected = list.SelectedItem as Macro;
            if (selected == null) { ShowError("Выберите инструмент."); return; }
            busy = true; run.Enabled = false; list.Enabled = false;
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
                    object[] methods = null;
                    try { methods = sw.GetMacroMethods(path, 0) as object[]; } catch (COMException) { /* Manual entry remains available. */ }
                    var choices = methods == null ? new string[0] : methods.Select(m => Convert.ToString(m)).Where(m => m.Contains(".")).ToArray();
                    using (var dialog = new Form { Text = "Точка входа: " + macro.name, Width = 420, Height = 180, StartPosition = FormStartPosition.CenterParent }) {
                        var input = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown };
                        input.Items.AddRange(choices); if (choices.Length > 0) input.SelectedIndex = 0;
                        var hint = new Label { Dock = DockStyle.Top, Height = 44, Text = "Выберите процедуру или введите Модуль.Процедура.\nВыбор сохранится для этого файла." };
                        var confirm = new Button { Text = "Запустить", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                        dialog.Controls.Add(input); dialog.Controls.Add(hint); dialog.Controls.Add(confirm); dialog.AcceptButton = confirm;
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        entry = input.Text.Trim(); int split = entry.LastIndexOf('.');
                        if (split <= 0 || split == entry.Length - 1) throw new InvalidDataException("Укажите точку входа в формате Модуль.Процедура.");
                        macro.module = entry.Substring(0, split); macro.procedure = entry.Substring(split + 1);
                        settings.Entries[path] = entry; SaveSettings();
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
                busy = false; list.Enabled = true; run.Enabled = list.SelectedItem != null;
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
