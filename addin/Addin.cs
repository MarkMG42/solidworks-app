using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SolidWorks.Interop.swpublished;

[assembly: ComVisible(false)]
namespace SWToolsHub
{
    [ComVisible(true)]
    [Guid("13B0B588-6E90-46B3-B76A-13DC794957A6")]
    [ProgId("SWToolsHub.Addin")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class Addin : ISwAddin
    {
        private SolidWorks.Interop.sldworks.SldWorks sw;
        private SolidWorks.Interop.sldworks.TaskpaneView taskPane;
        private UserControl panel;
        private WebView2 browser;
        private int swThread;
        private bool disposed;
        private int generation;
        private bool busy;
        private readonly string root = Path.GetDirectoryName(typeof(Addin).Assembly.Location);
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private const string Origin = "https://swtoolshub.local";

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                disposed = false;
                generation++;
                swThread = Thread.CurrentThread.ManagedThreadId;
                sw = (SolidWorks.Interop.sldworks.SldWorks)ThisSW;
                if (!sw.SetAddinCallbackInfo2(0, this, Cookie))
                    throw new InvalidOperationException("SolidWorks не зарегистрировал дополнение.");
                panel = new UserControl { Dock = DockStyle.Fill };
                // Create the HWND on the SolidWorks STA thread before any async operation.
                var handle = panel.Handle;

                taskPane = sw.CreateTaskpaneView2("", "SW Tools Hub");
                if (taskPane == null || !taskPane.DisplayWindowFromHandlex64(handle.ToInt64()))
                    throw new InvalidOperationException("Не удалось создать панель SolidWorks.");
                browser = new WebView2 { Dock = DockStyle.Fill };
                panel.Controls.Add(browser);
                var previousContext = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    InitializeBrowser(generation);
                }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("SW Tools Hub: " + ex.Message, "Ошибка загрузки");
                DisconnectFromSW();
                return false;
            }
        }

        private async void InitializeBrowser(int session)
        {
            try
            {
                var ui = Path.Combine(root, "ui");
                if (!File.Exists(Path.Combine(ui, "index.html")))
                    throw new FileNotFoundException("Не найдена сборка интерфейса ui/index.html.");
                var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWToolsHub", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                if (disposed || session != generation) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (disposed || session != generation) return;
                var core = browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.SetVirtualHostNameToFolderMapping("swtoolshub.local", ui, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += OnNavigation;
                core.NewWindowRequested += OnNewWindow;
                core.WebMessageReceived += OnMessage;
                core.Navigate(Origin + "/index.html");
            }
            catch (Exception ex)
            {
                if (!disposed && session == generation) MessageBox.Show("Не удалось открыть интерфейс. Проверьте WebView2 Runtime и файлы ui.\n" + ex.Message, "SW Tools Hub");
            }
        }

        private static bool IsLocal(string uri)
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out var value) &&
                value.Scheme == "https" && value.Host == "swtoolshub.local" && value.IsDefaultPort;
        }
        private void OnNavigation(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!IsLocal(e.Uri)) e.Cancel = true;
        }
        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (disposed || !IsLocal(e.Source)) return;
            // WebView2 callbacks must stay on the HWND owner / SolidWorks thread.
            if (Thread.CurrentThread.ManagedThreadId != swThread)
            {
                var json = e.WebMessageAsJson;
                panel.BeginInvoke(new Action(() => HandleMessage(json)));
                return;
            }
            HandleMessage(e.WebMessageAsJson);
        }

        private void HandleMessage(string json)
        {
            if (disposed) return;
            string id = null;
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != swThread)
                    throw new InvalidOperationException("Запуск разрешён только в потоке SolidWorks.");
                if (json.Length > 4096) throw new InvalidDataException("Слишком длинное сообщение.");
                var message = serializer.Deserialize<BridgeMessage>(json);
                if (message == null) throw new InvalidDataException("Пустое сообщение.");
                id = message.id;
                if (message.type == "ready")
                {
                    Send(new { type = "catalog", macros = Catalog.Load(root).macros });
                    return;
                }
                if (message.type != "run") throw new InvalidDataException("Неизвестная команда.");
                if (busy) return; // The original request owns the eventual result.
                busy = true;
                try { Run(id); }
                finally { busy = false; }
            }
            catch (Exception ex) { Result(id, false, "Не удалось выполнить запрос: " + ex.Message); }
        }

        private void Run(string id)
        {
            var macro = Catalog.Load(root).macros.SingleOrDefault(m => m.id == id);
            if (macro == null) throw new InvalidDataException("Инструмент не найден в локальном каталоге.");
            var path = Path.Combine(root, "Library", "Macros", macro.file);
            if (!File.Exists(path)) throw new FileNotFoundException("Добавьте рабочий файл в Library\\Macros: " + macro.file);
            SolidWorks.Interop.sldworks.ModelDoc2 document = null;
            try
            {
                document = (SolidWorks.Interop.sldworks.ModelDoc2)sw.IActiveDoc2;
                if (document == null) throw new InvalidOperationException("Откройте подходящий чертёж перед запуском.");
                if ((int)document.GetType() != 3)
                    throw new InvalidOperationException("Макрос предназначен для чертежа. Откройте подходящий чертёж.");
                int error = 0;
                bool ok = sw.RunMacro2(path, macro.module, macro.procedure, 1, out error);
                Result(id, ok, ok ? "SolidWorks завершил вызов макроса. Проверьте результат на чертеже." :
                    "SolidWorks не выполнил макрос. Код ошибки: " + error + ". Проверьте модуль, процедуру, VBA и исходный чертёж.");
            }
            finally { Release(document); }
        }

        private void Result(string id, bool ok, string message) { Send(new { type = "result", id, ok, message }); }
        private void Send(object message)
        {
            if (!disposed && browser?.CoreWebView2 != null)
                browser.CoreWebView2.PostWebMessageAsJson(serializer.Serialize(message));
        }
        public bool DisconnectFromSW()
        {
            if (busy) return false;
            disposed = true;
            generation++;
            try
            {
                if (browser?.CoreWebView2 != null)
                {
                    browser.CoreWebView2.WebMessageReceived -= OnMessage;
                    browser.CoreWebView2.NavigationStarting -= OnNavigation;
                    browser.CoreWebView2.NewWindowRequested -= OnNewWindow;
                }
            }
            catch { }
            try { browser?.Dispose(); } catch { }
            browser = null;
            try { if (taskPane != null) taskPane.DeleteView(); } catch { }
            try { panel?.Dispose(); } catch { }
            panel = null;
            Release(taskPane); taskPane = null;
            // The application RCW is owned by SolidWorks; do not FinalRelease it.
            sw = null;
            return true;
        }
        private static void Release(object value)
        {
            try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
        public sealed class BridgeMessage
        {
            public string type { get; set; }
            public string id { get; set; }
        }
    }
}
