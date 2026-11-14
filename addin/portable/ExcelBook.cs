using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelNumberFormat;

namespace SWToolsHub.Portable
{
    internal sealed class ExcelInput
    {
        public string Sheet { get; set; }
        public string Address { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }

        public static ExcelInput From(string sheet, string address, object value) => new ExcelInput
        {
            Sheet = sheet,
            Address = address,
            Kind = value == null ? "blank" : value is bool ? "boolean" : value is DateTime ? "date" : value is double ? "number" : "text",
            Text = value is DateTime ? ((DateTime)value).ToString("o", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)
        };
        public object Value() => Kind == "blank" ? null : Kind == "boolean" ? (object)bool.Parse(Text) : Kind == "number" ? double.Parse(Text, CultureInfo.InvariantCulture) : Kind == "date" ? DateTime.Parse(Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : (object)Text;
    }

    internal sealed class ExcelCell
    {
        public string Address, Formula, Format, Error;
        public int Row, Column;
        public object Original, Value;
        public bool Missing, Locked, Slave;
    }

    internal sealed class ExcelRange
    {
        public int Top, Left, Bottom, Right;
        public bool Contains(int row, int col) => row >= Top && row <= Bottom && col >= Left && col <= Right;
        public string Master => ExcelBook.Address(Top, Left);

        public static ExcelRange Parse(string text)
        {
            var a = text.Replace("$", "").Split(':');
            var p = ExcelBook.Point(a[0]);
            var q = ExcelBook.Point(a.Length > 1 ? a[1] : a[0]);
            return new ExcelRange
            {
                Top = p.Item1,
                Left = p.Item2,
                Bottom = q.Item1,
                Right = q.Item2
            };
        }
    }

    internal sealed class ExcelRule
    {
        public string Type, Formula, Operator;
        public string[] Formulae;
        public List<ExcelRange> Ranges;
        public int Priority;
        public bool Stop, AllowBlank = true;
        public XElement Style;
    }

    internal sealed class ExcelSheet
    {
        public string Name, Path;
        public IXLWorksheet Worksheet;
        public int Rows = 1, Columns = 1;
        public readonly Dictionary<string, ExcelCell> Cells = new Dictionary<string, ExcelCell>(StringComparer.OrdinalIgnoreCase);
        public readonly List<ExcelRange> Merges = new List<ExcelRange>();
        public readonly List<ExcelRule> Lists = new List<ExcelRule>(), Conditions = new List<ExcelRule>();
        public ExcelCell Cell(string address)
        {
            ExcelCell cell;
            return Cells.TryGetValue(address, out cell) ? cell : null;
        }
    }

    internal sealed class ExcelChoice
    {
        public string Label;
        public object Value;
        public bool Literal;
        public override string ToString() => Label;
    }

    internal sealed class ExcelList
    {
        public readonly List<ExcelChoice> Choices = new List<ExcelChoice>();
        public bool AllowBlank;
        public string Error;
    }

    // Native, offline workbook model. The source ZIP is retained for lossless export.
    internal sealed class ExcelBook : IDisposable
    {
        internal static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex Strings = new Regex("\"(?:[^\"]|\"\")*\"", RegexOptions.Compiled);
        private static readonly Regex References = new Regex(@"(?<![\p{L}\p{N}_])(?:(?:'(?<quoted>(?:[^']|'')+)'|(?<sheet>[\p{L}_][\p{L}\p{N}_.]*))!)?(?<a>\$?[A-Za-z]+\$?\d+)(?::(?<b>\$?[A-Za-z]+\$?\d+))?(?![\p{L}\p{N}_])", RegexOptions.Compiled);
        private static readonly HashSet<string> Functions = new HashSet<string>(("IF IFERROR TRUE FALSE ISNUMBER ISTEXT ISBLANK ISERROR ISNA NOT AND OR ABS SQRT ROUND ROUNDUP ROUNDDOWN CEILING CEILING.MATH FLOOR FLOOR.MATH TEXT MAX MIN SUM COUNT COUNTA COUNTBLANK COUNTIF COUNTIFS SUMIF SUMIFS SUMPRODUCT AVERAGE INDEX MATCH VLOOKUP HLOOKUP XLOOKUP CONCAT CONCATENATE LEFT RIGHT MID LEN TRIM CLEAN UPPER LOWER SUBSTITUTE REPLACE FIND SEARCH EXACT VALUE INT MOD POWER SIGN PRODUCT DATE YEAR MONTH DAY TODAY NOW TIME HOUR MINUTE SECOND EOMONTH CHOOSE IFNA").Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ConditionFunctions = new HashSet<string>("IF IFERROR TRUE FALSE ISNUMBER ISTEXT ISBLANK ISERROR NOT AND OR ABS SQRT ROUND TEXT MAX MIN SUM COUNT COUNTIF SUMPRODUCT INDEX MATCH VLOOKUP LEN TRIM UPPER LOWER SEARCH EXACT".Split(' '), StringComparer.OrdinalIgnoreCase);
        public readonly List<ExcelSheet> Sheets = new List<ExcelSheet>();
        public readonly Dictionary<string, ExcelInput> Edits = new Dictionary<string, ExcelInput>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<KeyValuePair<string, ExcelInput>> history = new Stack<KeyValuePair<string, ExcelInput>>();
        private readonly Dictionary<string, string> blocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> dependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<XElement> dxfs;
        private readonly string[] theme =
        {
            "FFFFFF",
            "000000",
            "EEECE1",
            "1F497D",
            "4F81BD",
            "C0504D",
            "9BBB59",
            "8064A2",
            "4BACC6",
            "F79646",
            "0000FF",
            "800080"
        };
        private bool prepared;
        private readonly byte[] source;
        public readonly XLWorkbook Workbook;
        public int Calculated { get; private set; }
        public int Unavailable { get; private set; }
        public bool CanUndo => history.Count > 0;
        public bool Dirty => Edits.Count > 0;

        public ExcelBook(byte[] bytes)
        {
            if (bytes.Length > 15 * 1024 * 1024)
                throw new InvalidDataException("Максимальный размер XLSX — 15 МБ");
            source = bytes;
            var documents = new List<Tuple<string, string, XDocument>>();
            XDocument book;
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                if (zip.Entries.Sum(e => e.Length) > 256L * 1024 * 1024)
                    throw new InvalidDataException("Распакованная книга слишком большая");
                book = Read(zip, "xl/workbook.xml");
                var rels = Read(zip, "xl/_rels/workbook.xml.rels").Root.Elements().ToDictionary(e => (string)e.Attribute("Id"));
                foreach (var n in book.Descendants(S + "definedName"))
                    names[((string)n.Attribute("localSheetId") ?? "*") + ":" + (string)n.Attribute("name")] = n.Value;
                var styles = Read(zip, "xl/styles.xml");
                dxfs = styles.Root.Element(S + "dxfs")?.Elements().ToList() ?? new List<XElement>();
                if (zip.GetEntry("xl/theme/theme1.xml") != null)
                {
                    var colors = Read(zip, "xl/theme/theme1.xml").Descendants().FirstOrDefault(e => e.Name.LocalName == "clrScheme");
                    if (colors != null)
                    {
                        var order = new[]
                        {
                            "lt1",
                            "dk1",
                            "lt2",
                            "dk2",
                            "accent1",
                            "accent2",
                            "accent3",
                            "accent4",
                            "accent5",
                            "accent6",
                            "hlink",
                            "folHlink"
                        };
                        for (int i = 0; i < order.Length; i++)
                        {
                            var c = colors.Elements().FirstOrDefault(e => e.Name.LocalName == order[i])?.Elements().FirstOrDefault();
                            var v = (string)c?.Attribute("lastClr") ?? (string)c?.Attribute("val");
                            if (v != null && Regex.IsMatch(v, "^[0-9a-fA-F]{6}$"))
                                theme[i] = v;
                        }
                    }
                }

                int count = 0;
                foreach (var sheet in book.Descendants(S + "sheet"))
                {
                    var rel = rels[(string)sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))];
                    if ((string)rel.Attribute("TargetMode") == "External")
                        throw new InvalidDataException("Внешний лист не поддерживается");
                    var uri = new Uri(new Uri("https://xlsx.local/xl/workbook.xml"), (string)rel.Attribute("Target"));
                    if (uri.Host != "xlsx.local")
                        throw new InvalidDataException("Неверный путь листа");
                    var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                    var xml = Read(zip, path);
                    foreach (var c in xml.Descendants(S + "c"))
                    {
                        var p = Point((string)c.Attribute("r"));
                        if (++count > 50000 || p.Item1 > 10000 || p.Item2 > 100)
                            throw new InvalidDataException("Лимит: 50 000 ячеек, 10 000 строк, 100 столбцов");
                    }

                    documents.Add(Tuple.Create((string)sheet.Attribute("name"), path, xml));
                }
            }

            Workbook = new XLWorkbook(new MemoryStream(bytes));
            foreach (var document in documents)
            {
                var sheet = new ExcelSheet
                {
                    Name = document.Item1,
                    Path = document.Item2,
                    Worksheet = Workbook.Worksheet(document.Item1)
                };
                var xml = document.Item3;
                foreach (var element in xml.Descendants(S + "c"))
                {
                    string address = (string)element.Attribute("r");
                    var position = Point(address);
                    var cell = sheet.Worksheet.Cell(address);
                    XElement f = element.Element(S + "f"), v = element.Element(S + "v");
                    var model = new ExcelCell
                    {
                        Address = address,
                        Row = position.Item1,
                        Column = position.Item2,
                        Formula = cell.HasFormula ? cell.FormulaA1 : null,
                        Original = ObjectValue(cell.CachedValue),
                        Locked = sheet.Worksheet.IsProtected && cell.Style.Protection.Locked,
                        Format = FormatCode(cell)
                    };
                    model.Missing = f != null && (v == null || string.IsNullOrEmpty(v.Value) && (string)element.Attribute("t") != "str");
                    if (f != null && !model.Missing && (string)element.Attribute("t") == "str" && v != null && v.Value == "")
                        model.Original = "";
                    model.Value = model.Original;
                    sheet.Cells[address] = model;
                    sheet.Rows = Math.Max(sheet.Rows, model.Row);
                    sheet.Columns = Math.Max(sheet.Columns, model.Column);
                }

                foreach (var range in xml.Descendants(S + "mergeCell"))
                {
                    var r = ExcelRange.Parse((string)range.Attribute("ref"));
                    if (r.Bottom > 10000 || r.Right > 100)
                        throw new InvalidDataException("Объединение за пределами лимита");
                    sheet.Merges.Add(r);
                    sheet.Rows = Math.Max(sheet.Rows, r.Bottom);
                    sheet.Columns = Math.Max(sheet.Columns, r.Right);
                    foreach (var c in sheet.Cells.Values)
                        if (r.Contains(c.Row, c.Column) && c.Address != r.Master)
                            c.Slave = true;
                }

                foreach (var validation in xml.Descendants().Where(e => e.Name.LocalName == "dataValidation" && (string)e.Attribute("type") == "list"))
                {
                    var sqref = (string)validation.Attribute("sqref") ?? validation.Descendants().FirstOrDefault(e => e.Name.LocalName == "sqref")?.Value;
                    if (string.IsNullOrWhiteSpace(sqref))
                        continue;
                    sheet.Lists.Add(new ExcelRule { Ranges = Ranges(sqref), Formula = validation.Elements().FirstOrDefault(e => e.Name.LocalName == "formula1")?.Value ?? "", AllowBlank = !new[] { "0", "false" }.Contains((string)validation.Attribute("allowBlank")) });
                }

                foreach (var block in xml.Descendants(S + "conditionalFormatting"))
                    foreach (var rule in block.Elements(S + "cfRule"))
                    {
                        int styleIndex = (int? )rule.Attribute("dxfId") ?? -1;
                        sheet.Conditions.Add(new ExcelRule { Ranges = Ranges((string)block.Attribute("sqref")), Type = (string)rule.Attribute("type"), Operator = (string)rule.Attribute("operator"), Formula = (string)rule.Attribute("text"), Formulae = rule.Elements(S + "formula").Select(e => e.Value).ToArray(), Priority = (int? )rule.Attribute("priority") ?? int.MaxValue, Stop = new[] { "1", "true" }.Contains((string)rule.Attribute("stopIfTrue")), Style = styleIndex >= 0 && styleIndex < dxfs.Count ? dxfs[styleIndex] : null });
                    }

                sheet.Conditions.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                Sheets.Add(sheet);
            }

            if (Sheets.Count == 0)
                throw new InvalidDataException("В книге нет листов");
        }

        public static XDocument Read(ZipArchive zip, string path)
        {
            var e = zip.GetEntry(path);
            if (e == null)
                throw new InvalidDataException("В XLSX отсутствует " + path);
            if (e.Length > 32000000)
                throw new InvalidDataException("XML книги слишком большой");
            using (var stream = e.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32000000 }))
                return XDocument.Load(reader);
        }

        private static List<ExcelRange> Ranges(string text) => Regex.Split(text.Trim(), @"\s+").Select(ExcelRange.Parse).ToList();
        public static Tuple<int, int> Point(string address)
        {
            var m = Regex.Match(address ?? "", @"^\$?([A-Za-z]+)\$?(\d+)$");
            if (!m.Success)
                throw new InvalidDataException("Некорректный адрес ячейки");
            int col = 0;
            foreach (char c in m.Groups[1].Value.ToUpperInvariant())
                col = checked(col * 26 + c - 'A' + 1);
            int row = int.Parse(m.Groups[2].Value, Inv);
            if (row < 1 || col < 1)
                throw new InvalidDataException("Некорректный адрес ячейки");
            return Tuple.Create(row, col);
        }

        public static string Address(int row, int column)
        {
            string a = "";
            while (column > 0)
            {
                column--;
                a = (char)('A' + column % 26) + a;
                column /= 26;
            }

            return a + row;
        }

        public static string Key(string sheet, string address) => sheet + "\u001f" + address;
        public static object ObjectValue(XLCellValue value)
        {
            switch (value.Type)
            {
                case XLDataType.Blank:
                    return null;
                case XLDataType.Boolean:
                    return value.GetBoolean();
                case XLDataType.Number:
                    return value.GetNumber();
                case XLDataType.DateTime:
                    return value.GetDateTime();
                case XLDataType.TimeSpan:
                    return value.GetTimeSpan().TotalDays;
                case XLDataType.Error:
                    return value.GetError();
                default:
                    return value.GetText();
            }
        }

        private static string FormatCode(IXLCell cell)
        {
            var code = cell.Style.NumberFormat.Format;
            if (!string.IsNullOrEmpty(code))
                return code;
            var codes = new Dictionary<int, string>
            {
                {
                    0,
                    "General"
                },
                {
                    1,
                    "0"
                },
                {
                    2,
                    "0.00"
                },
                {
                    3,
                    "#,##0"
                },
                {
                    4,
                    "#,##0.00"
                },
                {
                    9,
                    "0%"
                },
                {
                    10,
                    "0.00%"
                },
                {
                    11,
                    "0.00E+00"
                },
                {
                    14,
                    "dd.mm.yyyy"
                },
                {
                    15,
                    "d-mmm-yy"
                },
                {
                    16,
                    "d-mmm"
                },
                {
                    17,
                    "mmm-yy"
                },
                {
                    18,
                    "h:mm AM/PM"
                },
                {
                    19,
                    "h:mm:ss AM/PM"
                },
                {
                    20,
                    "h:mm"
                },
                {
                    21,
                    "h:mm:ss"
                },
                {
                    22,
                    "dd.mm.yyyy h:mm"
                },
                {
                    49,
                    "@"
                }
            };
            string result;
            return codes.TryGetValue(cell.Style.NumberFormat.NumberFormatId, out result) ? result : "General";
        }

        public string Display(ExcelCell cell)
        {
            if (cell == null)
                return "";
            if (!Dirty && cell.Formula != null && cell.Missing)
                return "Нет сохранённого результата";
            if (cell.Error != null)
                return cell.Error.StartsWith("Не пересчитано") ? "Не пересчитано" : "Ошибка пересчёта";
            return Format(cell.Value, cell.Format);
        }

        public static string ErrorText(XLError error)
        {
            switch (error.ToString())
            {
                case "DivisionByZero":
                    return "#DIV/0!";
                case "IncompatibleValue":
                    return "#VALUE!";
                case "NoValueAvailable":
                    return "#N/A";
                case "NumberInvalid":
                    return "#NUM!";
                case "NameNotRecognized":
                    return "#NAME?";
                case "CellReference":
                    return "#REF!";
                default:
                    return "#NULL!";
            }
        }

        public static string Format(object value, string format)
        {
            if (value == null)
                return "";
            if (value is XLError)
                return ErrorText((XLError)value);
            if (value is bool)
                return (bool)value ? "TRUE" : "FALSE";
            if (value is string)
                return (string)value;
            try
            {
                if (format != "General" && format != "@")
                    return new NumberFormat(format).Format(value, CultureInfo.GetCultureInfo("ru-RU"));
            }
            catch
            {
            }

            return value is DateTime ? ((DateTime)value).ToString("dd.MM.yyyy", Inv) : Convert.ToString(value, CultureInfo.GetCultureInfo("ru-RU"));
        }

        public object Current(string sheetName, string address)
        {
            var sheet = Sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
                throw new InvalidDataException("Лист не найден");
            var cell = sheet.Cell(address);
            if (cell?.Error != null || cell?.Formula != null && !Dirty && cell.Missing)
                throw new InvalidDataException("Источник не пересчитан");
            return cell?.Value;
        }

        public string EditText(ExcelCell cell) => cell?.Value is DateTime ? ((DateTime)cell.Value).ToString("yyyy-MM-dd", Inv) : Convert.ToString(cell?.Value, Inv) ?? "";
        private static object Parse(string text, ExcelCell cell)
        {
            if (text.Length > 32767 || Regex.IsMatch(text, @"[\x00-\x08\x0b\x0c\x0e-\x1f]"))
                throw new InvalidDataException("Недопустимый текст");
            if (text.StartsWith("'"))
                return text.Substring(1);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (text.StartsWith("="))
                throw new InvalidDataException("Формулы доступны только для чтения; для буквального текста добавьте апостроф");
            if (cell?.Original is DateTime)
            {
                DateTime date;
                if (!DateTime.TryParseExact(text.Trim(), new[] { "dd.MM.yyyy", "yyyy-MM-dd" }, Inv, DateTimeStyles.None, out date))
                    throw new InvalidDataException("Введите дату ДД.ММ.ГГГГ или ГГГГ-ММ-ДД");
                return date;
            }

            if (cell?.Original is string)
                return text;
            bool boolean;
            if (bool.TryParse(text, out boolean))
                return boolean;
            if (cell?.Original is bool)
                throw new InvalidDataException("Введите TRUE или FALSE");
            double n;
            bool percent = text.Trim().EndsWith("%");
            if (double.TryParse(text.Trim().TrimEnd('%').Replace(',', '.'), NumberStyles.Float, Inv, out n) && !double.IsNaN(n) && !double.IsInfinity(n))
                return percent ? n / 100 : n;
            if (cell?.Original is double)
                throw new InvalidDataException("Введите конечное число");
            return text;
        }

        public void Apply(string sheetName, string address, string text)
        {
            var sheet = Sheets.First(s => s.Name == sheetName);
            var point = Point(address);
            var merge = sheet.Merges.FirstOrDefault(m => m.Contains(point.Item1, point.Item2));
            if (merge != null)
                address = merge.Master;
            var cell = sheet.Cell(address);
            if (point.Item1 > sheet.Rows || point.Item2 > sheet.Columns)
                throw new InvalidDataException("Ячейка вне таблицы");
            if (cell?.Formula != null || cell?.Locked == true || cell == null && sheet.Worksheet.IsProtected)
                throw new InvalidDataException("Ячейка доступна только для чтения");
            var list = List(sheetName, address);
            object value;
            if (list != null && list.Error == null)
            {
                var choice = list.Choices.FirstOrDefault(c => string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase));
                if (choice != null)
                {
                    value = choice.Value;
                    if (choice.Literal && (cell?.Original is double || cell?.Original is bool || cell?.Original is DateTime))
                    {
                        try
                        {
                            value = Parse(text, cell);
                        }
                        catch
                        {
                        }
                    }
                }
                else if (text == "" && list.AllowBlank)
                    value = null;
                else
                    throw new InvalidDataException("Выберите значение из списка");
            }
            else
                value = Parse(text, cell);
            Prepare();
            var key = Key(sheetName, address);
            ExcelInput previous;
            Edits.TryGetValue(key, out previous);
            var original = cell?.Original;
            if (Equals(previous == null ? original : previous.Value(), value))
                return;
            history.Push(new KeyValuePair<string, ExcelInput>(key, previous));
            if (Equals(original, value))
                Edits.Remove(key);
            else
                Edits[key] = ExcelInput.From(sheetName, address, value);
            sheet.Worksheet.Cell(address).Value = XLCellValue.FromObject(value, Inv);
            Recalculate();
        }

        public void Restore(IEnumerable<ExcelInput> inputs)
        {
            var values = (inputs ?? new ExcelInput[0]).ToArray();
            if (values.Length == 0)
                return;
            Prepare();
            foreach (var input in values)
            {
                var sheet = Sheets.First(s => s.Name == input.Sheet);
                var point = Point(input.Address);
                if (point.Item1 > sheet.Rows || point.Item2 > sheet.Columns || sheet.Cell(input.Address)?.Formula != null)
                    throw new InvalidDataException("Некорректный сохранённый ввод");
                Edits[Key(input.Sheet, input.Address)] = input;
                sheet.Worksheet.Cell(input.Address).Value = XLCellValue.FromObject(input.Value(), Inv);
            }

            Recalculate();
        }

        public void Undo()
        {
            if (!CanUndo)
                return;
            var item = history.Pop();
            var cell = Find(item.Key);
            if (item.Value == null)
                Edits.Remove(item.Key);
            else
                Edits[item.Key] = item.Value;
            var sheet = Sheets.First(s => item.Key.StartsWith(s.Name + "\u001f", StringComparison.OrdinalIgnoreCase));
            sheet.Worksheet.Cell(item.Key.Substring(sheet.Name.Length + 1)).Value = XLCellValue.FromObject(item.Value == null ? cell?.Original : item.Value.Value(), Inv);
            Recalculate();
        }

        private ExcelCell Find(string key)
        {
            foreach (var s in Sheets)
            {
                string prefix = s.Name + "\u001f";
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return s.Cell(key.Substring(prefix.Length));
            }

            return null;
        }

        private IEnumerable<string> ReferencesTo(string formula, string sheet, int depth = 0)
        {
            if (depth > 10)
                throw new InvalidDataException("Циклическое имя");
            var plain = Strings.Replace(formula, "\"\"");
            foreach (Match m in References.Matches(plain))
            {
                string name = m.Groups["quoted"].Success ? m.Groups["quoted"].Value.Replace("''", "'") : m.Groups["sheet"].Success ? m.Groups["sheet"].Value : sheet;
                var r = ExcelRange.Parse(m.Groups["a"].Value + (m.Groups["b"].Success ? ":" + m.Groups["b"].Value : ""));
                if ((long)(r.Bottom - r.Top + 1) * (r.Right - r.Left + 1) > 50000)
                    throw new InvalidDataException("Слишком большой диапазон");
                for (int row = r.Top; row <= r.Bottom; row++)
                    for (int col = r.Left; col <= r.Right; col++)
                        yield return Key(name, Address(row, col));
            }

            foreach (var name in names)
            {
                string label = name.Key.Substring(name.Key.IndexOf(':') + 1);
                if (Regex.IsMatch(plain, @"(?<![\p{L}\p{N}_])" + Regex.Escape(label) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase))
                    foreach (var reference in ReferencesTo(name.Value, sheet, depth + 1))
                        yield return reference;
            }
        }

        private void Prepare()
        {
            if (prepared)
                return;
            prepared = true;
            foreach (var sheet in Sheets)
                foreach (var cell in sheet.Cells.Values.Where(c => c.Formula != null))
                {
                    string key = Key(sheet.Name, cell.Address);
                    try
                    {
                        var plain = Strings.Replace(cell.Formula, "");
                        if (plain.Contains("[") || plain.Contains("{") || plain.Contains("#"))
                            throw new InvalidDataException("Такой тип формулы пока не поддерживается");
                        foreach (Match f in Regex.Matches(plain, @"([\p{L}_][\p{L}\p{N}_.]*)\s*\("))
                            if (!Functions.Contains(f.Groups[1].Value.Replace("_xlfn.", "")))
                                throw new InvalidDataException("Функция " + f.Groups[1].Value + " пока не поддерживается");
                        dependencies[key] = ReferencesTo(cell.Formula, sheet.Name).Distinct().ToList();
                        var lib = sheet.Worksheet.Cell(cell.Address);
                        lib.InvalidateFormula();
                        object actual = ObjectValue(lib.Value);
                        if (actual is XLError && (XLError)actual == XLError.NameNotRecognized)
                            throw new InvalidDataException("Функция или имя не поддерживается");
                        if (!cell.Missing && !(cell.Original is XLError) && !Same(cell.Original, actual))
                            throw new InvalidDataException("Исходный результат не совпадает с Excel");
                    }
                    catch (Exception e)
                    {
                        blocked[key] = "Не пересчитано: " + e.Message;
                        try
                        {
                            dependencies[key] = ReferencesTo(cell.Formula, sheet.Name).Distinct().ToList();
                        }
                        catch
                        {
                            dependencies[key] = new List<string>();
                        }
                    }
                }

            Propagate(blocked);
        }

        private static bool Same(object expected, object actual)
        {
            if (expected is double && actual is double)
                return Math.Abs((double)expected - (double)actual) <= 1e-8 * Math.Max(1, Math.Abs((double)expected));
            return Equals(expected, actual);
        }

        private void Propagate(Dictionary<string, string> errors)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var pair in dependencies)
                    if (!errors.ContainsKey(pair.Key) && pair.Value.Any(errors.ContainsKey))
                    {
                        errors[pair.Key] = "Не пересчитано: зависимая формула недоступна";
                        changed = true;
                    }
            }
            while (changed);
        }

        private void Recalculate()
        {
            Calculated = Unavailable = 0;
            if (!Dirty)
            {
                foreach (var sheet in Sheets)
                    foreach (var c in sheet.Cells.Values)
                    {
                        c.Value = c.Original;
                        c.Error = null;
                    }

                return;
            }

            var errors = new Dictionary<string, string>(blocked, StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in Sheets)
                foreach (var cell in sheet.Cells.Values.Where(c => c.Formula != null))
                    sheet.Worksheet.Cell(cell.Address).InvalidateFormula();
            foreach (var sheet in Sheets)
            {
                foreach (var cell in sheet.Cells.Values)
                {
                    cell.Error = null;
                    if (cell.Formula == null)
                    {
                        ExcelInput input;
                        cell.Value = Edits.TryGetValue(Key(sheet.Name, cell.Address), out input) ? input.Value() : cell.Original;
                        continue;
                    }

                    string key = Key(sheet.Name, cell.Address);
                    if (errors.ContainsKey(key))
                        continue;
                    try
                    {
                        var value = ObjectValue(sheet.Worksheet.Cell(cell.Address).Value);
                        if (value is XLError)
                        {
                            cell.Error = "Ошибка пересчёта: " + ErrorText((XLError)value);
                            cell.Value = null;
                        }
                        else
                            cell.Value = value;
                    }
                    catch (Exception e)
                    {
                        errors[key] = "Не пересчитано: " + e.Message;
                    }
                }

                foreach (var input in Edits.Values.Where(v => v.Sheet == sheet.Name))
                    if (!sheet.Cells.ContainsKey(input.Address))
                    {
                        var p = Point(input.Address);
                        sheet.Cells[input.Address] = new ExcelCell
                        {
                            Address = input.Address,
                            Row = p.Item1,
                            Column = p.Item2,
                            Original = null,
                            Value = input.Value(),
                            Format = FormatCode(sheet.Worksheet.Cell(input.Address))
                        };
                    }
            }

            Propagate(errors);
            foreach (var sheet in Sheets)
                foreach (var cell in sheet.Cells.Values.Where(c => c.Formula != null))
                {
                    string error;
                    if (errors.TryGetValue(Key(sheet.Name, cell.Address), out error))
                    {
                        cell.Error = error;
                        cell.Value = null;
                    }

                    if (cell.Error != null)
                        Unavailable++;
                    else
                        Calculated++;
                }
        }

        private string NameFormula(string text, ExcelSheet sheet)
        {
            string value;
            return names.TryGetValue(Sheets.IndexOf(sheet) + ":" + text, out value) || names.TryGetValue("*:" + text, out value) ? value : null;
        }

        private static string Shift(string formula, int rows, int columns)
        {
            return Strings.Replace(formula, m => m.Value);
        }

        public static string Translate(string formula, int rows, int columns)
        {
            var pieces = Regex.Split(formula, "(\"(?:[^\"]|\"\")*\")");
            for (int i = 0; i < pieces.Length; i += 2)
                pieces[i] = References.Replace(pieces[i], m =>
                {
                    string prefix = m.Value.Substring(0, m.Groups["a"].Index - m.Index);
                    Func<string, string> move = a =>
                    {
                        var p = Point(a);
                        var n = Regex.Match(a, @"^(\$?)[A-Za-z]+(\$?)\d+$");
                        int r = p.Item1 + (n.Groups[2].Value == "$" ? 0 : rows), c = p.Item2 + (n.Groups[1].Value == "$" ? 0 : columns);
                        if (r < 1 || c < 1 || r > 1048576 || c > 16384)
                            throw new InvalidDataException("Ссылка за пределами листа");
                        return Address(r, c);
                    };
                    return prefix + move(m.Groups["a"].Value) + (m.Groups["b"].Success ? ":" + move(m.Groups["b"].Value) : "");
                });
            return string.Concat(pieces);
        }

        private Tuple<ExcelSheet, string> Reference(string text, ExcelSheet current)
        {
            int bang = text.LastIndexOf('!');
            string name = bang < 0 ? current.Name : text.Substring(0, bang).Trim('\'').Replace("''", "'");
            if (name.Contains("["))
                throw new InvalidDataException("Внешний список недоступен");
            return Tuple.Create(Sheets.First(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)), bang < 0 ? text : text.Substring(bang + 1));
        }

        private IEnumerable<ExcelChoice> ListValues(string formula, ExcelSheet sheet, int rowDelta, int colDelta, int depth = 0)
        {
            if (depth > 10)
                throw new InvalidDataException("Циклическое имя списка");
            formula = formula.Trim().TrimStart('=');
            if (formula.StartsWith("\"") && formula.EndsWith("\""))
                return formula.Substring(1, formula.Length - 2).Replace("\"\"", "\"").Split(',').Select(v => new ExcelChoice { Label = v, Value = v, Literal = true }).ToArray();
            var named = NameFormula(formula, sheet);
            if (named != null)
                return ListValues(named, sheet, 0, 0, depth + 1);
            var indirect = Regex.Match(formula, @"^(?:_xlfn\.)?INDIRECT\((.*)\)$", RegexOptions.IgnoreCase);
            if (indirect.Success)
            {
                string result = "";
                foreach (Match part in Regex.Matches(indirect.Groups[1].Value, "\"(?:[^\"]|\"\")*\"|[^&]+"))
                {
                    string text = part.Value.Trim();
                    if (text.StartsWith("\"") && text.EndsWith("\""))
                        result += text.Substring(1, text.Length - 2);
                    else
                    {
                        var r = Reference(Translate(text, rowDelta, colDelta), sheet);
                        result += Convert.ToString(Current(r.Item1.Name, r.Item2.Replace("$", "")), Inv);
                    }
                }

                return ListValues(result, sheet, 0, 0, depth + 1);
            }

            var reference = Reference(Translate(formula, rowDelta, colDelta), sheet);
            var range = ExcelRange.Parse(reference.Item2);
            if ((long)(range.Bottom - range.Top + 1) * (range.Right - range.Left + 1) > 5000)
                throw new InvalidDataException("Список слишком большой");
            var values = new List<ExcelChoice>();
            for (int r = range.Top; r <= range.Bottom; r++)
                for (int c = range.Left; c <= range.Right; c++)
                {
                    object value = Current(reference.Item1.Name, Address(r, c));
                    if (value == null || Equals(value, ""))
                        continue;
                    values.Add(new ExcelChoice { Label = Convert.ToString(value, Inv), Value = value });
                }

            return values;
        }

        public ExcelList List(string sheetName, string address)
        {
            var sheet = Sheets.First(s => s.Name == sheetName);
            var p = Point(address);
            var rule = sheet.Lists.LastOrDefault(r => r.Ranges.Any(a => a.Contains(p.Item1, p.Item2)));
            if (rule == null)
                return null;
            var result = new ExcelList
            {
                AllowBlank = rule.AllowBlank
            };
            try
            {
                foreach (var value in ListValues(rule.Formula, sheet, p.Item1 - rule.Ranges[0].Top, p.Item2 - rule.Ranges[0].Left).GroupBy(v => v.Label).Select(g => g.First()))
                    result.Choices.Add(value);
                if (result.Choices.Count == 0)
                    result.Error = "Источник списка пуст";
            }
            catch (Exception e)
            {
                result.Error = "Список недоступен: " + e.Message;
            }

            return result;
        }

        public IEnumerable<XElement> ConditionalStyles(ExcelSheet sheet, string address)
        {
            var p = Point(address);
            var matched = new List<XElement>();
            foreach (var rule in sheet.Conditions)
            {
                if (!rule.Ranges.Any(r => r.Contains(p.Item1, p.Item2)))
                    continue;
                try
                {
                    Func<string, string> translate = f => Translate(f, p.Item1 - rule.Ranges[0].Top, p.Item2 - rule.Ranges[0].Left);
                    string formula = null;
                    if (rule.Type == "expression")
                        formula = translate(rule.Formulae.FirstOrDefault() ?? "");
                    else if (rule.Type == "cellIs" && rule.Formulae.Length > 0)
                    {
                        var ops = new Dictionary<string, string>
                        {
                            {
                                "equal",
                                "="
                            },
                            {
                                "notEqual",
                                "<>"
                            },
                            {
                                "greaterThan",
                                ">"
                            },
                            {
                                "lessThan",
                                "<"
                            },
                            {
                                "greaterThanOrEqual",
                                ">="
                            },
                            {
                                "lessThanOrEqual",
                                "<="
                            }
                        };
                        string op;
                        if (ops.TryGetValue(rule.Operator ?? "", out op))
                            formula = address + op + translate(rule.Formulae[0]);
                        else if (rule.Formulae.Length > 1)
                        {
                            formula = "AND(" + address + ">=" + translate(rule.Formulae[0]) + "," + address + "<=" + translate(rule.Formulae[1]) + ")";
                            if (rule.Operator == "notBetween")
                                formula = "NOT(" + formula + ")";
                        }
                    }

                    bool yes = false;
                    if (formula != null)
                    {
                        foreach (Match function in Regex.Matches(Strings.Replace(formula, ""), @"([\p{L}_][\p{L}\p{N}_.]*)\s*\("))
                            if (!ConditionFunctions.Contains(function.Groups[1].Value)) throw new InvalidDataException("Функция условия не поддерживается");
                        if (ReferencesTo(formula, sheet.Name).Any(k => Find(k)?.Error != null || Find(k)?.Formula != null && !Dirty && Find(k).Missing))
                            continue;
                        var value = sheet.Worksheet.Evaluate(formula, address);
                        yes = value.IsBoolean ? value.GetBoolean() : value.IsNumber && value.GetNumber() != 0;
                    }
                    else if (new[]
                    {
                        "containsText",
                        "notContainsText",
                        "beginsWith",
                        "endsWith"
                    }.Contains(rule.Type))
                    {
                        string value = Convert.ToString(Current(sheet.Name, address), Inv) ?? "", needle = rule.Formula ?? "";
                        yes = rule.Type == "beginsWith" ? value.StartsWith(needle, StringComparison.OrdinalIgnoreCase) : rule.Type == "endsWith" ? value.EndsWith(needle, StringComparison.OrdinalIgnoreCase) : rule.Type == "notContainsText" ? value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 : value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (yes)
                    {
                        if (rule.Style != null) matched.Add(rule.Style);
                        if (rule.Stop)
                            break;
                    }
                }
                catch
                { /* Unsupported conditions do not invent a successful status. */
                }
            }

            matched.Reverse();
            return matched;
        }

        public string Color(XElement element, string fallback)
        {
            if (element == null)
                return fallback;
            string rgb = (string)element.Attribute("rgb");
            if (rgb != null && rgb.Length >= 6)
                return "#" + rgb.Substring(rgb.Length - 6);
            int index;
            if (int.TryParse((string)element.Attribute("theme"), out index) && index >= 0 && index < theme.Length)
            {
                rgb = theme[index];
                double tint;
                if (double.TryParse((string)element.Attribute("tint"), NumberStyles.Float, Inv, out tint))
                {
                    int n = Convert.ToInt32(rgb, 16);
                    var channels = new[]
                    {
                        n >> 16 & 255,
                        n >> 8 & 255,
                        n & 255
                    };
                    return "#" + string.Concat(channels.Select(c => Math.Max(0, Math.Min(255, (int)Math.Round(tint < 0 ? c * (1 + tint) : c + (255 - c) * tint))).ToString("X2")));
                }

                return "#" + rgb;
            }

            return fallback;
        }

        public byte[] Export()
        {
            if (!Dirty)
                return (byte[])source.Clone();
            using (var stream = new MemoryStream())
            {
                stream.Write(source, 0, source.Length);
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
                {
                    foreach (var sheet in Sheets)
                    {
                        var xml = Read(zip, sheet.Path);
                        foreach (var c in xml.Descendants(S + "c").Where(c => c.Element(S + "f") != null))
                            c.Element(S + "v")?.Remove();
                        foreach (var input in Edits.Values.Where(i => i.Sheet == sheet.Name))
                        {
                            var data = xml.Root.Element(S + "sheetData");
                            if (data == null)
                            {
                                data = new XElement(S + "sheetData");
                                xml.Root.Add(data);
                            }

                            var p = Point(input.Address);
                            var row = data.Elements(S + "row").FirstOrDefault(r => (int)r.Attribute("r") == p.Item1);
                            if (row == null)
                            {
                                row = new XElement(S + "row", new XAttribute("r", p.Item1));
                                var after = data.Elements(S + "row").FirstOrDefault(r => (int)r.Attribute("r") > p.Item1);
                                if (after != null)
                                    after.AddBeforeSelf(row);
                                else
                                    data.Add(row);
                            }

                            var cell = row.Elements(S + "c").FirstOrDefault(c => (string)c.Attribute("r") == input.Address);
                            if (cell == null)
                            {
                                cell = new XElement(S + "c", new XAttribute("r", input.Address));
                                var after = row.Elements(S + "c").FirstOrDefault(c => Point((string)c.Attribute("r")).Item2 > p.Item2);
                                if (after != null)
                                    after.AddBeforeSelf(cell);
                                else
                                    row.Add(cell);
                            }

                            cell.RemoveNodes();
                            cell.Attribute("t")?.Remove();
                            object value = input.Value();
                            if (value is DateTime)
                            {
                                var date = (DateTime)value;
                                value = date.ToOADate();
                                if (Workbook.Use1904DateSystem)
                                    value = (double)value - 1462;
                                else if (date < new DateTime(1900, 3, 1))
                                    value = (double)value - 1;
                            }

                            if (value is string)
                            {
                                cell.SetAttributeValue("t", "inlineStr");
                                cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), (string)value)));
                            }
                            else if (value != null)
                            {
                                if (value is bool)
                                    cell.SetAttributeValue("t", "b");
                                cell.Add(new XElement(S + "v", value is bool ? ((bool)value ? "1" : "0") : Convert.ToString(value, Inv)));
                            }
                        }

                        Write(zip, sheet.Path, xml);
                    }

                    var book = Read(zip, "xl/workbook.xml");
                    var calc = book.Root.Element(S + "calcPr");
                    if (calc == null)
                    {
                        calc = new XElement(S + "calcPr");
                        var after = book.Root.Elements().FirstOrDefault(e => new[] { "oleSize", "customWorkbookViews", "pivotCaches", "smartTagPr", "smartTagTypes", "webPublishing", "fileRecoveryPr", "webPublishObjects", "extLst" }.Contains(e.Name.LocalName));
                        if (after != null)
                            after.AddBeforeSelf(calc);
                        else
                            book.Root.Add(calc);
                    }

                    calc.SetAttributeValue("fullCalcOnLoad", "1");
                    calc.SetAttributeValue("forceFullCalc", "1");
                    calc.SetAttributeValue("calcMode", "auto");
                    Write(zip, "xl/workbook.xml", book);
                    zip.GetEntry("xl/calcChain.xml")?.Delete();
                    foreach (string path in new[]
                    {
                        "xl/_rels/workbook.xml.rels",
                        "[Content_Types].xml"
                    }

                    )
                    {
                        var doc = Read(zip, path);
                        foreach (var element in doc.Root.Elements().Where(e => e.Attributes().Any(a => a.Value.Contains("calcChain"))).ToArray())
                            element.Remove();
                        Write(zip, path, doc);
                    }
                }

                return stream.ToArray();
            }
        }

        private static void Write(ZipArchive zip, string path, XDocument xml)
        {
            zip.GetEntry(path)?.Delete();
            using (var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open())
                xml.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        public void Dispose() => Workbook.Dispose();
    }
}
