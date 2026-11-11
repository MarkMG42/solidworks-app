using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SWToolsHub.Portable
{
    public sealed class CalculationBook
    {
        public string Title { get; set; }
        public string Source { get; set; }
        public string Sha256 { get; set; }
        public Dictionary<string, string> Names { get; set; }
        public CalculationSheet[] Sheets { get; set; }
    }
    public sealed class CalculationSheet
    {
        public string Name { get; set; }
        public bool ReferenceOnly { get; set; }
        public CalculationCell[] Cells { get; set; }
        public string[] Merged { get; set; }
    }
    public sealed class CalculationCell
    {
        public string Address { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public string Format { get; set; }
        public object Value { get; set; }
        public string Formula { get; set; }
        public object Expected { get; set; }
        public bool Editable { get; set; }
        public string[] Options { get; set; }
        public CalculationExpression Expression { get; set; }
    }
    public sealed class CalculationExpression
    {
        public string Op { get; set; }
        public string Text { get; set; }
        public double Number { get; set; }
        public CalculationExpression[] Args { get; set; }
    }
    public sealed class CalculationInput
    {
        public string Sheet { get; set; }
        public string Address { get; set; }
        public object Value { get; set; }
    }
    public sealed class CalculationSession
    {
        public string Source { get; set; }
        public string Sha256 { get; set; }
        public CalculationInput[] Inputs { get; set; }
    }
    public sealed class CalculationError : Exception
    {
        public CalculationError(string text) : base(text) { }
    }
    // Evaluates only the finite formula vocabulary imported from the five supplied workbooks.
    // No scripting, COM, dynamic compilation or execution of workbook contents.
    public sealed class CalculationEngine
    {
        public CalculationBook Book { get; private set; }
        private readonly Dictionary<string, CalculationCell> cells = new Dictionary<string, CalculationCell>();
        private readonly Dictionary<string, object> changes = new Dictionary<string, object>();
        private readonly Dictionary<string, object> cache = new Dictionary<string, object>();
        private readonly HashSet<string> evaluating = new HashSet<string>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public CalculationEngine(CalculationBook book)
        {
            Book = book;
            foreach (var sheet in book.Sheets) foreach (var cell in sheet.Cells) cells.Add(Key(sheet.Name, cell.Address), cell);
        }
        private static string Key(string sheet, string address) => sheet + "!" + address.Replace("$", "").ToUpperInvariant();
        public void Reset() { changes.Clear(); cache.Clear(); }
        public object Input(string sheet, CalculationCell cell)
        {
            object value; return changes.TryGetValue(Key(sheet, cell.Address), out value) ? value : cell.Value;
        }
        public void SetInput(string sheet, string address, object value)
        {
            var key = Key(sheet, address);
            CalculationCell cell;
            if (!cells.TryGetValue(key, out cell) || !cell.Editable) throw new CalculationError("Ячейка доступна только для чтения");
            if (value == null || value is string && string.IsNullOrWhiteSpace((string)value)) throw new CalculationError("Введите значение");
            if (cell.Value is string) {
                value = Convert.ToString(value, Inv);
                if (cell.Options != null && !cell.Options.Contains((string)value)) throw new CalculationError("Выберите значение из списка");
            } else {
                double n;
                if (!double.TryParse(Convert.ToString(value, Inv).Replace(',', '.'), NumberStyles.Float, Inv, out n) || double.IsNaN(n) || double.IsInfinity(n))
                    throw new CalculationError("Введите конечное число, например 12,5");
                value = n;
            }
            changes[key] = value; cache.Clear();
        }
        public CalculationSession SaveSession()
        {
            return new CalculationSession { Source = Book.Source, Sha256 = Book.Sha256,
                Inputs = Book.Sheets.SelectMany(s => s.Cells.Where(c => c.Editable).Select(c => new CalculationInput { Sheet = s.Name, Address = c.Address, Value = Input(s.Name, c) })).ToArray() };
        }
        public void LoadSession(CalculationSession session)
        {
            if (session == null || session.Source != Book.Source || session.Sha256 != Book.Sha256 || session.Inputs == null)
                throw new CalculationError("Файл относится к другому расчёту или версии формул");
            var next = new CalculationEngine(Book);
            foreach (var input in session.Inputs) next.SetInput(input.Sheet, input.Address, input.Value);
            changes.Clear(); foreach (var pair in next.changes) changes.Add(pair.Key, pair.Value); cache.Clear();
        }
        public string ValidateInputs(string sheetName)
        {
            var sheet = Book.Sheets.First(s => s.Name == sheetName);
            if (sheet.ReferenceOnly) return null;
            foreach (var c in sheet.Cells.Where(c => c.Editable && !(c.Value is string))) {
                double n = Number(Input(sheetName, c));
                bool temperature = (sheetName == "Расчет по давлению" && c.Address == "B5") || (sheetName == "Местное смятие" && c.Address == "B3");
                bool signedLoad = ((sheetName == "Угловые швы" || sheetName == "Стыковые швы") && new[] {"C3", "C4", "C5"}.Contains(c.Address))
                    || (sheetName == "Болты" && new[] {"C4", "C5", "C6", "C39"}.Contains(c.Address));
                if (!temperature && !signedLoad && n < 0) return c.Address + ": значение не должно быть отрицательным";
                if (c.Options != null && !c.Options.Any(o => double.Parse(o, Inv) == n)) return c.Address + ": выберите значение из списка";
            }
            string positive = "";
            if (sheetName == "Расчет по давлению") positive = "B3 B6 B9";
            if (sheetName == "Местное смятие") positive = "B5 B6 B7 B8";
            if (sheetName == "Расчёт") positive = "B6 B10 B11 B12";
            if (sheetName == "Длина болта") positive = "B2 B3";
            if (sheetName == "Высота узла") positive = "B2 B3";
            if (sheetName == "Расстояние до обреза") positive = "C2";
            if (sheetName == "Угловые швы") positive = "C7 C8 C11 C12 C13";
            if (sheetName == "Стыковые швы") positive = "C6 C7 C9";
            if (sheetName == "Болты") positive = "C8 C9 C13 C14 C15 C16 C17 C23 C24 C41 C42 C43 C44 C47";
            foreach (var a in positive.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries))
                if (Number(Get(sheetName, a)) <= 0) return a + ": значение должно быть больше нуля";
            if (sheetName == "Расчет по давлению" && Number(Get(sheetName, "B8")) >= 100) return "Допуск стенки должен быть меньше 100%";
            if (sheetName == "Расчёт" && Number(Get(sheetName, "B8")) > Number(Get(sheetName, "B6"))) return "Положение нагрузки a должно быть в пределах пролёта L";
            if (sheetName == "Болты") {
                foreach (var a in new[] {"C8", "C14", "C41"}) if (Number(Get(sheetName, a)) % 1 != 0) return a + ": введите целое количество";
                if (Number(Get(sheetName, "C44")) <= Number(Get(sheetName, "C41")) * Number(Get(sheetName, "C42"))) return "Ширина фасонки должна превышать сумму диаметров отверстий";
                if (!new[] {5.6,5.8,8.8,10.9,12.9}.Contains(Number(Get(sheetName,"C10")))) return "Класс прочности: 5,6 / 5,8 / 8,8 / 10,9 / 12,9";
                if (Get(sheetName, "C21") is bool) return "В исходном Excel нет сопротивления смятию для выбранной стали и класса точности";
            }
            if (sheetName == "Длина болта" && GetRaw(sheetName, "B14") == null) return "В справочнике Excel отсутствует высота гровера для выбранного крепежа";
            if (sheetName == "Длина болта" && Number(Get(sheetName,"B9")) % 1 != 0) return "Число витков должно быть целым";
            return null;
        }
        public object Get(string sheet, string address)
        {
            var value = GetRaw(sheet, address);
            CalculationCell cell;
            // A formula pointing at a blank lookup cell displays zero. Retain the
            // underlying blank inside expressions, so the workbook's empty-data
            // guards still prevent a recommendation from incomplete tables.
            return value == null && cells.TryGetValue(Key(sheet, address), out cell) && cell.Expression != null ? 0d : value;
        }
        private object GetRaw(string sheet, string address)
        {
            string key = Key(sheet, address); object value;
            if (cache.TryGetValue(key, out value)) return value;
            if (changes.TryGetValue(key, out value)) return value;
            CalculationCell cell;
            if (!cells.TryGetValue(key, out cell)) return null;
            if (cell.Expression == null) return cell.Value;
            if (!evaluating.Add(key)) throw new CalculationError("Циклическая ссылка: " + key);
            try { value = Eval(cell.Expression, sheet); cache[key] = value; return value; }
            finally { evaluating.Remove(key); }
        }
        private sealed class Range
        {
            public object[] Values;
            public int Columns;
        }
        private object Reference(string text, string sheet)
        {
            string defined;
            if (Book.Names.TryGetValue(text, out defined)) return Reference(defined, sheet);
            int bang = text.LastIndexOf('!');
            if (bang >= 0) { sheet = text.Substring(0, bang).Trim('\'').Replace("''", "'"); text = text.Substring(bang + 1); }
            text = text.Replace("$", "");
            if (!text.Contains(":")) return GetRaw(sheet, text);
            string[] parts = text.Split(':');
            int r1, c1, r2, c2; Position(parts[0], out r1, out c1); Position(parts[1], out r2, out c2);
            var values = new List<object>();
            for (int r = r1; r <= r2; r++) for (int c = c1; c <= c2; c++) values.Add(GetRaw(sheet, Address(r, c)));
            return new Range { Values = values.ToArray(), Columns = c2 - c1 + 1 };
        }
        public static void Position(string address, out int row, out int column)
        {
            var m = Regex.Match(address, "^([A-Za-z]+)([0-9]+)$");
            if (!m.Success) throw new CalculationError("Неизвестная ссылка: " + address);
            column = 0; foreach (char ch in m.Groups[1].Value.ToUpperInvariant()) column = column * 26 + ch - 'A' + 1;
            row = int.Parse(m.Groups[2].Value, Inv);
        }
        public static string Address(int row, int column)
        {
            string s = ""; while (column > 0) { column--; s = (char)('A' + column % 26) + s; column /= 26; } return s + row.ToString(Inv);
        }
        private static bool Numeric(object v) => v != null && !(v is string) && !(v is bool) && !(v is Range);
        public static double Number(object v)
        {
            if (v == null) return 0;
            if (v is bool) return (bool)v ? 1 : 0;
            double n;
            if (v is string && !double.TryParse((string)v, NumberStyles.Float, Inv, out n)) throw new CalculationError("#ЗНАЧ! Нечисловое значение");
            return Convert.ToDouble(v, Inv);
        }
        private static bool Truth(object v) => v is bool ? (bool)v : v != null && (v is string ? !string.IsNullOrEmpty((string)v) : Number(v) != 0);
        private static string Text(object v) => v == null ? "" : v is bool ? ((bool)v ? "TRUE" : "FALSE") : Convert.ToString(v, Inv);
        private static int Compare(object a, object b)
        {
            // Excel comparisons do not coerce a numeric cell to text; blank equals empty string/zero.
            if (a == null) a = b is string ? (object)"" : 0d;
            if (b == null) b = a is string ? (object)"" : 0d;
            if (a is string && b is string) return string.Compare((string)a, (string)b, StringComparison.OrdinalIgnoreCase);
            if (a is string || b is string) return a is string ? 1 : -1;
            if (a is bool || b is bool) return a is bool && b is bool ? ((bool)a).CompareTo((bool)b) : a is bool ? 1 : -1;
            return Number(a).CompareTo(Number(b));
        }
        private static object[] Flat(object v) => v is Range ? ((Range)v).Values : new[] { v };
        private static Range AsRange(object v) => v as Range ?? new Range { Values = new[] { v }, Columns = 1 };
        private static double Finite(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new CalculationError("#ЧИСЛО! Недопустимый результат"); return v;
        }
        private object Eval(CalculationExpression e, string sheet)
        {
            var args = e.Args ?? new CalculationExpression[0];
            Func<int, object> at = i => Eval(args[i], sheet);
            switch (e.Op) {
                case "NUM": return e.Number;
                case "STR": return e.Text;
                case "BOOL": return e.Number != 0;
                case "REF": return Reference(e.Text, sheet);
                case "IF": return Truth(at(0)) ? at(1) : args.Length > 2 ? at(2) : (object)false;
                case "IFERROR": try { return at(0); } catch (CalculationError) { return at(1); }
                case "FALSE": return false;
                case "TRUE": return true;
                case "ISNUMBER": return Numeric(at(0));
                case "NOT": return !Truth(at(0));
                case "AND": return args.SelectMany(a => Flat(Eval(a, sheet))).All(Truth);
                case "OR": return args.SelectMany(a => Flat(Eval(a, sheet))).Any(Truth);
                case "NEG": return -Number(at(0));
                case "POS": return Number(at(0));
                case "PERCENT": return Number(at(0)) / 100;
                case "+": return Finite(Number(at(0)) + Number(at(1)));
                case "-": return Finite(Number(at(0)) - Number(at(1)));
                case "*": return Finite(Number(at(0)) * Number(at(1)));
                case "/": var divisor = Number(at(1)); if (divisor == 0) throw new CalculationError("#ДЕЛ/0! Деление на ноль"); return Finite(Number(at(0)) / divisor);
                case "^": return Finite(Math.Pow(Number(at(0)), Number(at(1))));
                case "&": return Text(at(0)) + Text(at(1));
                case "=": return Compare(at(0), at(1)) == 0;
                case "<>": return Compare(at(0), at(1)) != 0;
                case "<": return Compare(at(0), at(1)) < 0;
                case ">": return Compare(at(0), at(1)) > 0;
                case "<=": return Compare(at(0), at(1)) <= 0;
                case ">=": return Compare(at(0), at(1)) >= 0;
                case "ABS": return Math.Abs(Number(at(0)));
                case "SQRT": return Finite(Math.Sqrt(Number(at(0))));
                case "ROUND":
                    double factor = Math.Pow(10, Number(at(1)));
                    return Math.Round(Number(at(0)) * factor, 0, MidpointRounding.AwayFromZero) / factor;
                case "CEILING": case "CEILING.MATH":
                    double step = args.Length > 1 ? Math.Abs(Number(at(1))) : 1;
                    return step == 0 ? 0 : Math.Ceiling(Number(at(0)) / step) * step;
                case "TEXT": return Number(at(0)).ToString(Text(at(1)), Inv);
                case "MAX": case "MIN": case "SUM": case "COUNT":
                    var numbers = args.SelectMany(a => Flat(Eval(a, sheet))).Where(Numeric).Select(Number).ToArray();
                    if (e.Op == "COUNT") return (double)numbers.Length;
                    if (numbers.Length == 0) return 0d;
                    return e.Op == "MAX" ? numbers.Max() : e.Op == "MIN" ? numbers.Min() : numbers.Sum();
                case "COUNTIF": return (double)Flat(at(0)).Count(v => Compare(v, at(1)) == 0);
                case "SUMPRODUCT":
                    var ranges = args.Select(a => Flat(Eval(a, sheet))).ToArray();
                    if (ranges.Any(a => a.Length != ranges[0].Length)) throw new CalculationError("Размеры диапазонов не совпадают");
                    return Enumerable.Range(0, ranges[0].Length).Sum(i => ranges.Aggregate(1d, (p, a) => p * (Numeric(a[i]) ? Number(a[i]) : 0)));
                case "INDEX":
                    var range = AsRange(at(0)); int row = (int)Number(at(1)), col = args.Length > 2 ? (int)Number(at(2)) : 1;
                    int index = (row - 1) * range.Columns + col - 1;
                    if (row < 1 || col < 1 || col > range.Columns || index >= range.Values.Length) throw new CalculationError("#ССЫЛКА! INDEX");
                    return range.Values[index];
                case "MATCH":
                    if (args.Length > 2 && Number(at(2)) != 0) throw new CalculationError("Поддерживается только точный MATCH");
                    object needle = at(0); var hay = Flat(at(1));
                    for (int i = 0; i < hay.Length; i++) if (Compare(needle, hay[i]) == 0) return (double)(i + 1);
                    throw new CalculationError("#Н/Д: значение не найдено");
                case "VLOOKUP":
                    if (args.Length < 4 || Truth(at(3))) throw new CalculationError("Поддерживается только точный VLOOKUP");
                    object lookup = at(0); var table = AsRange(at(1)); int column = (int)Number(at(2));
                    if (column < 1 || column > table.Columns) throw new CalculationError("#ССЫЛКА! VLOOKUP");
                    for (int i = 0; i < table.Values.Length; i += table.Columns) if (Compare(lookup, table.Values[i]) == 0) return table.Values[i + column - 1];
                    throw new CalculationError("#Н/Д: значение не найдено");
                default: throw new CalculationError("Неизвестная функция: " + e.Op);
            }
        }
    }
}
