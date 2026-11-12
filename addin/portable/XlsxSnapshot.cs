using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SWToolsHub.Portable
{
    internal sealed class SnapshotSheet
    {
        public string Name;
        public readonly List<string[]> Cells = new List<string[]>();
    }
    // Reads cached XML values only. Never invokes Excel or CalculationEngine.
    internal static class XlsxSnapshot
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static XDocument Read(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path);
            if (entry == null) throw new InvalidDataException("Отсутствует часть XLSX: " + path);
            if (entry.Length > 32000000) throw new InvalidDataException("Распакованный XML слишком большой");
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32000000 }))
                return XDocument.Load(reader);
        }
        public static List<SnapshotSheet> Load(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Выберите файл .xlsx");
            if (new FileInfo(path).Length > 15 * 1024 * 1024) throw new InvalidDataException("Максимальный размер Excel — 15 МБ");
            using (var zip = ZipFile.OpenRead(path)) {
                var book = Read(zip, "xl/workbook.xml");
                var relationships = Read(zip, "xl/_rels/workbook.xml.rels").Root.Elements().ToDictionary(x => (string)x.Attribute("Id"));
                var shared = zip.GetEntry("xl/sharedStrings.xml") == null ? new string[0] : Read(zip, "xl/sharedStrings.xml").Root.Elements(S + "si").Select(x => string.Concat(x.Descendants(S + "t").Select(t => t.Value))).ToArray();
                var result = new List<SnapshotSheet>(); int count = 0;
                foreach (var sheet in book.Descendants(S + "sheet")) {
                    var model = new SnapshotSheet { Name = (string)sheet.Attribute("name") };
                    var relation = relationships[(string)sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))];
                    if ((string)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("Внешний лист не поддерживается");
                    var uri = new Uri(new Uri("https://xlsx.local/xl/workbook.xml"), (string)relation.Attribute("Target"));
                    if (uri.Host != "xlsx.local") throw new InvalidDataException("Неверный путь листа");
                    var xml = Read(zip, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
                    var masters = xml.Descendants(S + "c").Where(c => c.Element(S + "f") != null && (string)c.Element(S + "f").Attribute("si") != null && c.Element(S + "f").Value.Length > 0)
                        .ToDictionary(c => (string)c.Element(S + "f").Attribute("si"), c => (string)c.Attribute("r") + ": =" + c.Element(S + "f").Value);
                    foreach (var c in xml.Descendants(S + "c")) {
                        var f = c.Element(S + "f"); var v = c.Element(S + "v"); var inline = c.Element(S + "is");
                        if (f == null && v == null && inline == null) continue;
                        if (++count > 50000) throw new InvalidDataException("Лимит — 50 000 заполненных ячеек");
                        string formula = "", value = v == null ? "" : v.Value, type = (string)c.Attribute("t");
                        if (f != null) {
                            formula = f.Value;
                            string si = (string)f.Attribute("si");
                            if (formula.Length == 0 && si != null) { string master; formula = masters.TryGetValue(si, out master) ? "Общая формула из " + master + " (относительные ссылки не развёрнуты)" : "Общая формула #" + si + ": исходная формула отсутствует"; }
                            else formula = "=" + formula;
                            if (v == null || (value.Length == 0 && type != "str")) value = "Нет сохранённого результата";
                        }
                        if (type == "s" && v != null) value = shared[int.Parse(v.Value)];
                        else if (type == "b" && v != null) value = v.Value == "1" ? "true" : "false";
                        else if (type == "inlineStr") value = inline == null ? "" : string.Concat(inline.Descendants(S + "t").Select(t => t.Value));
                        model.Cells.Add(new[] { (string)c.Attribute("r"), value, formula });
                    }
                    result.Add(model);
                }
                if (result.Count == 0) throw new InvalidDataException("В файле нет листов");
                return result;
            }
        }
    }
}
