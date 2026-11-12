using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SWToolsHub.Portable;
class Checks
{
    static void Main()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                Add(zip, "xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Данные' r:id='r1'/><sheet name='Пустой' r:id='r2'/></sheets></workbook>");
                Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id='r1' Target='worksheets/test.xml'/><Relationship Id='r2' Target='worksheets/empty.xml'/></Relationships>");
                Add(zip, "xl/worksheets/test.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1'><f>UNKNOWN()</f><v>123</v></c><c r='B1'><f>1-1</f><v>0</v></c><c r='C1' t='b'><f>1=2</f><v>0</v></c><c r='D1'><f>MISSING()</f></c><c r='E1' t='str'><f>TEXT()</f><v></v></c><c r='F1' t='e'><f>1/0</f><v>#DIV/0!</v></c></row></sheetData></worksheet>");
                Add(zip, "xl/worksheets/empty.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData/></worksheet>");
            }
            var sheets = XlsxSnapshot.Load(path);
            var expected = new[] { "123", "0", "false", "Нет сохранённого результата", "", "#DIV/0!" };
            if (sheets.Count != 2 || sheets[1].Cells.Count != 0 || !sheets[0].Cells.Select(c => c[1]).SequenceEqual(expected) || sheets[0].Cells[0][2] != "=UNKNOWN()") throw new Exception("Cached snapshot mismatch");
            Console.WriteLine("XLSX snapshot: cached, missing, zero, false, blank, error, arbitrary paths and empty sheet passed");
        } finally { File.Delete(path); }
    }
    static void Add(ZipArchive zip, string name, string value) { using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(value); }
}
