using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SWToolsHub.Portable;

internal static class NativeExcelUiChecks
{
    static int count;
    static void Check(bool value, string label)
    {
        count++;
        if (!value)
            throw new Exception(label);
    }

    static IEnumerable<Control> All(Control c)
    {
        foreach (Control child in c.Controls)
        {
            yield return child;
            foreach (var item in All(child))
                yield return item;
        }
    }

    static T Find<T>(Control c, string name)
        where T : Control => All(c).OfType<T>().Single(x => x.Name == name);
    static void Pump(Form f)
    {
        f.PerformLayout();
        Application.DoEvents();
        f.PerformLayout();
        Application.DoEvents();
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return 2;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        string root = Path.Combine(Path.GetTempPath(), "sw-excel-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new ExcelLibrary(root);
            var entry = library.Add("Чек-лист.xlsx", ExcelFixture.Create());
            using (var window = new Form
            {
                ClientSize = new Size(1200, 800)
            }

            )
            using (var view = new ExcelBookView(library, entry))
            {
                window.Controls.Add(view);
                window.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 170 });
                window.Show();
                Pump(window);
                var grid = Find<DataGridView>(view, "ExcelBookGrid");
                var picker = Find<ComboBox>(view, "ExcelSheetPicker");
                var overlay = Find<ComboBox>(view, "ExcelCellList");
                var input = Find<TextBox>(view, "ExcelInput");
                var apply = Find<Button>(view, "ExcelApply");
                Check(grid.RowCount == 42 && grid.ColumnCount == 8, "Original worksheet dimensions");
                Check(!grid.Rows[10].Visible && !grid.Columns[6].Visible, "Hidden row/column");
                grid.CurrentCell = grid[3, 3];
                Pump(window);
                Check(overlay.Visible && overlay.Items.Count >= 3, "Dropdown inside blank cell: visible=" + overlay.Visible + "; items=" + overlay.Items.Count + "; bounds=" + overlay.Bounds + "; grid=" + grid.ClientSize);
                for (int i = 0; i < overlay.Items.Count; i++)
                    if (overlay.Items[i].ToString() == "Проверено")
                    {
                        overlay.SelectedIndex = i;
                        break;
                    }

                Pump(window);
                Check(Convert.ToString(grid[3, 3].Value) == "Проверено", "Choice applied immediately");
                using (var image = new Bitmap(grid.Width, grid.Height))
                {
                    grid.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    var rect = grid.GetCellDisplayRectangle(1, 3, true);
                    var color = image.GetPixel(rect.Right - 5, rect.Top + 3);
                    Check(color.R == 198 && color.G == 239 && color.B == 206, "Actual WinForms row color: " + color);
                }

                grid.CurrentCell = grid[0, 2];
                Pump(window);
                input.Text = "4,5";
                apply.PerformClick();
                Pump(window);
                Check(Convert.ToString(grid[1, 2].Value) == "13,50", "Native UI recalculates");
                Check(Convert.ToString(grid[2, 2].Value) == "Не пересчитано", "Unsupported formula explicit");
                Check(picker.PointToScreen(Point.Empty).Y >= grid.PointToScreen(Point.Empty).Y + grid.Height - 1, "Sheet selector below table");
                foreach (var size in new[]
                {
                    new Size(1200, 800),
                    new Size(900, 600),
                    new Size(640, 480),
                    new Size(480, 320)
                }

                )
                {
                    window.ClientSize = size;
                    Pump(window);
                    Check(grid.Width > 0 && grid.Height > 0, "Worksheet stays visible " + size);
                    Check(picker.Width > 0 && picker.Right <= picker.Parent.Width, "Bottom selector fits " + size);
                }

                window.ClientSize = new Size(1200, 800);
                Pump(window);
                if (args.Length > 0)
                {
                    Directory.CreateDirectory(args[0]);
                    using (var image = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.Combine(args[0], "native-excel-library.png"));
                    }
                }

                picker.SelectedIndex = 1;
                Pump(window);
                Check(grid.RowCount == 3, "Other sheet opens");
                picker.SelectedIndex = 0;
                Pump(window);
                Check(Convert.ToString(grid[3, 3].Value) == "Проверено", "Input retained across sheets");
                window.Close();
            }

            var saved = library.Entries().Single();
            using (var window = new Form
            {
                ClientSize = new Size(1000, 700)
            }

            )
            using (var view = new ExcelBookView(library, saved))
            {
                window.Controls.Add(view);
                window.Show();
                Pump(window);
                var grid = Find<DataGridView>(view, "ExcelBookGrid");
                Check(Convert.ToString(grid[1, 2].Value) == "13,50", "Native controls restore after restart");
                window.Close();
            }

            Console.WriteLine(count + " native Excel WinForms checks passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL after " + count + ": " + e);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
