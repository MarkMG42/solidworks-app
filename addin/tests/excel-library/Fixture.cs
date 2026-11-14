using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using ClosedXML.Excel;
using SWToolsHub.Portable;

internal static class ExcelFixture
{
    public static byte[] Create(bool date1904 = false)
    {
        byte[] bytes;
        using (var b = new XLWorkbook())
        {
            b.Use1904DateSystem = date1904;
            var s = b.AddWorksheet("Модель");
            var refs = b.AddWorksheet("Списки");
            refs.Visibility = XLWorksheetVisibility.Hidden;
            refs.Cell("A1").Value = "Проверено";
            refs.Cell("A2").Value = "Не проверено";
            refs.Cell("A3").Value = "Не применимо";
            b.DefinedNames.Add("Statuses", refs.Range("A1:A3"));
            s.Range("A1:E1").Merge();
            s.Cell("A1").Value = "Чек-лист проверки КД";
            s.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#17365D");
            s.Cell("A1").Style.Font.FontColor = XLColor.White;
            s.Column(1).Width = 28;
            s.Column(3).Width = 45;
            s.Cell("A3").Value = 2;
            s.Cell("B3").FormulaA1 = "A3*3";
            s.Cell("B3").Style.NumberFormat.Format = "0.00";
            s.Cell("C3").FormulaA1 = "UNSUPPORTED(A3)";
            s.Cell("E3").FormulaA1 = "C3+1";
            s.Cell("F3").FormulaA1 = "IFERROR(C3,42)";
            s.Cell("A4").Value = "Подпись";
            s.Cell("B4").Value = false;
            s.Cell("C4").Value = new DateTime(2026, 11, 14);
            s.Cell("C4").Style.NumberFormat.Format = "dd.mm.yyyy";
            s.Cell("E4").FormulaA1 = "1/(A3-2)";
            s.Cell("E5").FormulaA1 = "A3*2";
            s.Cell("F5").FormulaA1 = "IF(TRUE,\"\",\"\")";
            s.Cell("G5").FormulaA1 = "A3-A3";
            s.Cell("H5").FormulaA1 = "A3<0";
            s.Range("D3:D42").CreateDataValidation().List("Statuses");
            s.Range("D3:D42").GetDataValidation().IgnoreBlanks = true;
            s.Range("A3:E42").AddConditionalFormat().WhenIsTrue("$D3=\"Проверено\"").Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
            s.Range("A3:E42").AddConditionalFormat().WhenIsTrue("$D3=\"Не проверено\"").Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE");
            s.Cell("H42").Value = "Край";
            s.Row(11).Hide();
            s.Column(7).Hide();
            s.Row(3).Height = 28;
            using (var m = new MemoryStream())
            {
                b.SaveAs(m, new ClosedXML.Excel.SaveOptions { EvaluateFormulasBeforeSaving = false });
                bytes = m.ToArray();
            }
        }

        using (var m = new MemoryStream())
        {
            m.Write(bytes, 0, bytes.Length);
            m.Position = 0;
            using (var z = new ZipArchive(m, ZipArchiveMode.Update, true))
            {
                var x = ExcelBook.Read(z, "xl/worksheets/sheet1.xml");
                var ns = ExcelBook.S;
                foreach (var item in new[]
                {
                    new[]
                    {
                        "B3",
                        "6",
                        ""
                    },
                    new[]
                    {
                        "C3",
                        "99",
                        ""
                    },
                    new[]
                    {
                        "E3",
                        "100",
                        ""
                    },
                    new[]
                    {
                        "F3",
                        "99",
                        ""
                    },
                    new[]
                    {
                        "E4",
                        "#DIV/0!",
                        "e"
                    },
                    new[]
                    {
                        "E5",
                        "999",
                        ""
                    },
                    new[]
                    {
                        "F5",
                        "",
                        "str"
                    },
                    new[]
                    {
                        "G5",
                        "0",
                        ""
                    },
                    new[]
                    {
                        "H5",
                        "0",
                        "b"
                    }
                }

                )
                {
                    var c = x.Descendants(ns + "c").Single(v => (string)v.Attribute("r") == item[0]);
                    c.Element(ns + "v")?.Remove();
                    if (item[2] != "")
                        c.SetAttributeValue("t", item[2]);
                    c.Add(new XElement(ns + "v", item[1]));
                }

                z.GetEntry("xl/worksheets/sheet1.xml").Delete();
                using (var stream = z.CreateEntry("xl/worksheets/sheet1.xml").Open())
                    x.Save(stream);
                using (var writer = new StreamWriter(z.CreateEntry("custom/unchanged.xml").Open()))
                    writer.Write("<keep>original</keep>");
            }

            return m.ToArray();
        }
    }
}
