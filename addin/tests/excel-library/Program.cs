using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SWToolsHub.Portable;

internal static class Checks
{
    static int count;
    static void Check(bool value, string label)
    {
        count++;
        if (!value)
            throw new Exception(label);
    }

    static int Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "sw-excel-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            byte[] bytes = ExcelFixture.Create();
            var library = new ExcelLibrary(Path.Combine(temp, "library"));
            var a = library.Add("Чек-лист.xlsx", bytes);
            var b = library.Add("Чек-лист.xlsx", bytes);
            Check(b.Title == "Чек-лист (2)", "Duplicate names");
            using (var book = new ExcelBook(bytes))
            {
                var sheet = book.Sheets[0];
                Check(sheet.Merges.Count == 1 && sheet.Merges[0].Right == 5, "Original merges");
                Check(book.Display(sheet.Cell("B3")) == "6,00", "Cached number format");
                Check(book.Display(sheet.Cell("G5")) == "0", "Zero cache");
                Check(book.Display(sheet.Cell("H5")) == "FALSE", "Boolean cache");
                Check(book.Display(sheet.Cell("F5")) == "", "Blank cache");
                Check(book.Display(sheet.Cell("C4")) == "14.11.2026", "Date format");
                Check(sheet.Worksheet.Row(11).IsHidden && sheet.Worksheet.Column(7).IsHidden, "Hidden dimensions");
                Check(book.List("Модель", "D4").Choices.Count == 3, "Named list on blank cell");
                book.Apply("Модель", "D3", "Проверено");
                Check(book.ConditionalStyles(sheet, "A3").Any(), "Whole row green");
                Check(book.ConditionalStyles(sheet, "E3").Any(), "Blank end of row green");
                Check(!book.ConditionalStyles(sheet, "B4").Any(), "Other row unchanged");
                book.Apply("Модель", "A3", "4,5");
                Check(Math.Abs((double)sheet.Cell("B3").Value - 13.5) < 1e-9, "Supported recalculation");
                foreach (string cell in new[]
                {
                    "C3",
                    "E3",
                    "F3",
                    "E5"
                }

                )
                    Check(sheet.Cell(cell).Error?.StartsWith("Не пересчитано") == true, "Blocked formula/dependency " + cell);
                Check(Math.Abs((double)sheet.Cell("E4").Value - .4) < 1e-9, "Original error resolves");
                book.Apply("Модель", "D4", "Не проверено");
                Check(book.ConditionalStyles(sheet, "A4").Any(), "Red row");
                book.Undo();
                Check(!book.ConditionalStyles(sheet, "A4").Any(), "Undo color");
                book.Apply("Модель", "C4", "15.11.2026");
                book.Apply("Модель", "B6", "25");
                a.Inputs = book.Edits.Values.ToList();
                a.Sheet = "Модель";
                library.Save(a);
                string saved = Path.Combine(temp, "Сохраненная книга.xlsx");
                library.SaveAs(a, book, saved);
                Check(File.Exists(saved), "Save to chosen folder");
                using (var copy = new ExcelBook(File.ReadAllBytes(saved)))
                {
                    Check((double)copy.Sheets[0].Cell("A3").Value == 4.5, "Export value");
                    Check(copy.Sheets[0].Cell("B3").Missing, "Old formula cache removed");
                    Check(copy.Sheets[0].Cell("B3").Formula == "A3*3", "Formula retained");
                    Check(copy.Sheets[0].Conditions.Count == sheet.Conditions.Count, "Conditional definitions retained");
                    Check(copy.List("Модель", "D4").Choices.Count == 3, "List retained");
                    Check(copy.Display(copy.Sheets[0].Cell("C4")) == "15.11.2026", "Export date");
                }

                using (var z = ZipFile.OpenRead(saved))
                using (var reader = new StreamReader(z.GetEntry("custom/unchanged.xml").Open()))
                    Check(reader.ReadToEnd() == "<keep>original</keep>", "Unsupported parts retained");
            }

            var reloaded = new ExcelLibrary(library.Root).Entries().Single(e => e.Id == a.Id);
            using (var reopened = new ExcelBook(File.ReadAllBytes(library.OriginalPath(reloaded))))
            {
                reopened.Restore(reloaded.Inputs);
                Check(reopened.Sheets[0].Cell("D3").Value.Equals("Проверено"), "Input survives restart");
                Check((double)reopened.Sheets[0].Cell("B3").Value == 13.5, "Recalculation survives restart");
                Check(reopened.ConditionalStyles(reopened.Sheets[0], "E3").Any(), "Color survives restart");
            }

            using (var dateBook = new ExcelBook(ExcelFixture.Create(true)))
            {
                Check(dateBook.Display(dateBook.Sheets[0].Cell("C4")) == "14.11.2026", "1904 date read");
                dateBook.Apply("Модель", "C4", "16.11.2026");
                using (var copy = new ExcelBook(dateBook.Export()))
                    Check(copy.Display(copy.Sheets[0].Cell("C4")) == "16.11.2026", "1904 date save");
            }

            library.Delete(a);
            Check(library.Entries().Count() == 1 && library.Entries().Single().Id == b.Id, "Delete only selected book");
            Console.WriteLine(count + " native Excel model/storage/export checks passed.");
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
                Directory.Delete(temp, true);
            }
            catch
            {
            }
        }
    }
}
