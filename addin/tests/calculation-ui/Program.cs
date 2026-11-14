using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SWToolsHub.Portable;

internal static class UiChecks
{
    private static int checks;
    private static void Check(bool value, string description)
    {
        checks++; if (!value) throw new Exception(description);
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static T Find<T>(Control control, string name) where T : Control => Descendants(control).OfType<T>().Single(c => c.Name == name);
    private static void Pump(Form window)
    {
        window.PerformLayout(); Application.DoEvents(); window.PerformLayout(); Application.DoEvents();
    }
    private static DataGridView ActiveGrid(CalculationView view) => Find<Panel>(view, "CalculationSheetHost").Controls.OfType<DataGridView>().Single(g => g.Visible);
    [STAThread]
    private static int Main(string[] args)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) { Console.Error.WriteLine("Windows required; no native UI checks executed."); return 2; }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try {
            foreach (var book in CalculationView.LoadBooks()) {
                using (var window = new Form { ClientSize = new Size(900, 600), StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
                using (var view = new CalculationView(book)) {
                    var host = Find<Panel>(view, "CalculationSheetHost");
                    var first = host.Controls.OfType<DataGridView>().Single();
                    Check(first.RowCount == book.Sheets[0].Cells.Max(c => c.Row), "First sheet must exist before any native handle or tab switch: " + book.Title);
                    Check(!string.IsNullOrEmpty(Convert.ToString(first[0, 0].Value)), "First sheet values populated before showing");
                    window.Controls.Add(view);
                    window.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 170 });
                    window.Show(); view.ActivateSheet(); Pump(window);
                    Check(ActiveGrid(view) == first, "First table visible on first activation");
                    var picker = Find<ComboBox>(view, "CalculationSheetPicker");
                    Check(picker.Items.Count == book.Sheets.Length, "All workbook sheets available");
                    var actions = Find<FlowLayoutPanel>(view, "CalculationActions");
                    foreach (var size in new[] { new Size(900,600), new Size(800,600), new Size(640,480), new Size(480,320), new Size(1200,800) }) {
                        window.ClientSize = size; Pump(window);
                        Check(!actions.HorizontalScroll.Visible, "Toolbar has no horizontal scrolling at " + size);
                        Check(actions.Controls.Cast<Control>().All(c => c.Right <= actions.ClientSize.Width && c.Bottom <= actions.ClientSize.Height), "Every action fits in wrapped toolbar at " + size);
                        Check(picker.Left >= 0 && picker.Right <= picker.Parent.ClientSize.Width, "Sheet selector fits parent at " + size);
                        Check(host.Width > 0 && host.Height > 0, "Table viewport remains available at " + size);
                        Check(ActiveGrid(view).ClientSize.Width <= host.ClientSize.Width, "Grid viewport fits host");
                        if (size.Width >= 800) {
                            var grid = ActiveGrid(view);
                            Check(grid.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width) + grid.RowHeadersWidth <= grid.ClientSize.Width, "Main table fits standard window horizontally");
                        }
                    }
                    window.ClientSize = new Size(900, 600); Pump(window);
                    for (int i = 0; i < book.Sheets.Length; i++) {
                        picker.SelectedIndex = i; Pump(window);
                        Check(ActiveGrid(view).RowCount == book.Sheets[i].Cells.Max(c => c.Row), "Selected sheet populated: " + book.Sheets[i].Name);
                    }
                    picker.SelectedIndex = 0; Pump(window);
                    Check(ActiveGrid(view) == first, "Switching sheets retains original table");
                    if (book.Source.Contains("высоты")) {
                        first.CurrentCell = first[1, 2]; first.Focus(); Check(first.BeginEdit(true), "Height input starts editing");
                        ((TextBox)first.EditingControl).Text = "3000,5";
                        // Programmatic Text assignment is not a keyboard edit. Tell
                        // DataGridView that this harness changed its editing control.
                        ((IDataGridViewEditingControl)first.EditingControl).EditingControlValueChanged = true;
                        first.NotifyCurrentCellDirty(true);
                        Check(first.IsCurrentCellDirty, "Harness input is marked changed");
                        window.ClientSize = new Size(800, 600); Pump(window);
                        Check(first.EndEdit(), "Input commits after resize"); Pump(window);
                        Check(Convert.ToString(first[1, 10].Value) == "2496,5", "Resizing preserves input and recalculation: input=" + Convert.ToString(first[1, 2].Value) + "; result=" + Convert.ToString(first[1, 10].Value) + "; error=" + first[1, 2].ErrorText);
                        picker.SelectedIndex = 1; Pump(window); picker.SelectedIndex = 0; Pump(window);
                        Check(Convert.ToString(first[1, 10].Value) == "2496,5", "Switching sheets preserves edited values");
                    }
                    if (args.Length > 0) {
                        Directory.CreateDirectory(args[0]);
                        using (var bitmap = new Bitmap(window.Width, window.Height)) {
                            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(args[0], book.Title + ".png"));
                        }
                    }
                    window.Close();
                }
            }
            Console.WriteLine(checks + " native WinForms checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine("FAIL after " + checks + " checks: " + ex); return 1; }
    }
}
