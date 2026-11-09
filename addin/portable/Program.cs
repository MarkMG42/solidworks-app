using System;
using System.IO;
using System.Linq;
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
        public Hub()
        {
            Text = "SW Tools Hub — локальные макросы";
            Width = 440; Height = 520; MinimumSize = new System.Drawing.Size(320, 380);
            var hint = new Label { Dock = DockStyle.Top, Height = 52, Text = "Откройте SolidWorks и подходящий чертёж.\nПоиск инструмента:" };
            var folder = new Button { Dock = DockStyle.Bottom, Height = 36, Text = "Открыть папку для .swp" };
            Controls.Add(list); Controls.Add(search); Controls.Add(hint);
            Controls.Add(status); Controls.Add(run); Controls.Add(folder);
            folder.Click += (s, e) => {
                try { var path = Path.Combine(root, "Library", "Macros"); Directory.CreateDirectory(path); System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\""); }
                catch (Exception ex) { ShowError(ex.Message); }
            };
            search.TextChanged += (s, e) => Filter();
            run.Click += (s, e) => RunSelected();
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            Shown += (s, e) => {
                try { macros = Catalog.Load(root).macros; Filter(); status.Text = "Готово. Макросы лежат в Library\\Macros рядом с приложением."; }
                catch (Exception ex) { run.Enabled = false; ShowError("Не удалось прочитать Library\\macros.json: " + ex.Message); }
            };
        }
        private void Filter()
        {
            list.DataSource = macros.Where(m => m.name.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        }
        private void ShowError(string text) { status.Text = text; MessageBox.Show(this, text, "SW Tools Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void RunSelected()
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
                var macro = Catalog.Load(root).macros.SingleOrDefault(m => m.id == selected.id);
                if (macro == null) throw new InvalidDataException("Инструмент удалён из каталога. Перезапустите приложение.");
                var path = Path.Combine(root, "Library", "Macros", macro.file);
                if (!File.Exists(path)) throw new FileNotFoundException("Скопируйте рабочий файл в Library\\Macros: " + macro.file);
                try { sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                catch (COMException) { throw new InvalidOperationException("Не удалось подключиться к SolidWorks. Откройте SolidWorks обычным способом, без запуска от администратора, затем повторите. При нескольких экземплярах оставьте один."); }
                document = sw.IActiveDoc2;
                if (document == null || document.GetType() != 3) throw new InvalidOperationException("Откройте подходящий чертёж в SolidWorks. Деталь и сборка не подходят.");
                status.Text = "Макрос выполняется…"; status.Refresh();
                int error;
                bool ok = sw.RunMacro2(path, macro.module, macro.procedure, 1, out error);
                if (!ok) throw new InvalidOperationException("SolidWorks вернул ошибку " + error + ". Проверьте модуль, процедуру и условия VBA.");
                status.Text = "Вызов макроса завершён. Проверьте результат на чертеже.";
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                Release(document); Release(sw);
                busy = false; run.Enabled = true; list.Enabled = true;
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
