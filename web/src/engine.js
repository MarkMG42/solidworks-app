export class CalculationError extends Error {}
const numeric = v => typeof v === 'number' && Number.isFinite(v);
export function number(v) {
  if (v == null) return 0;
  if (typeof v === 'boolean') return v ? 1 : 0;
  const text = String(v).trim();
  if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(text) || !Number.isFinite(Number(text))) throw new CalculationError('Нечисловое значение');
  return Number(text);
}
const truth = v => typeof v === 'boolean' ? v : v != null && (typeof v === 'string' ? v.length > 0 : number(v) !== 0);
const text = v => v == null ? '' : typeof v === 'boolean' ? (v ? 'TRUE' : 'FALSE') : String(v);
function compare(a, b) {
  if (a == null) a = typeof b === 'string' ? '' : 0;
  if (b == null) b = typeof a === 'string' ? '' : 0;
  if (typeof a === 'string' && typeof b === 'string') return a.toUpperCase() === b.toUpperCase() ? 0 : a.toUpperCase() < b.toUpperCase() ? -1 : 1;
  if (typeof a === 'string' || typeof b === 'string') return typeof a === 'string' ? 1 : -1;
  if (typeof a === 'boolean' || typeof b === 'boolean') return typeof a === typeof b ? Number(a) - Number(b) : typeof a === 'boolean' ? 1 : -1;
  return Math.sign(number(a) - number(b));
}
const finite = v => { if (!Number.isFinite(v)) throw new CalculationError('Недопустимый результат вычисления'); return v; };
class Range { constructor(values, columns = 1) { this.values = values; this.columns = columns; } }
const flat = v => v instanceof Range ? v.values : [v];
const range = v => v instanceof Range ? v : new Range([v]);
export function position(address) {
  const m = /^([a-z]+)(\d+)$/i.exec(address.replaceAll('$', ''));
  if (!m) throw new CalculationError(`Неизвестная ссылка: ${address}`);
  return [Number(m[2]), [...m[1].toUpperCase()].reduce((n, c) => n * 26 + c.charCodeAt(0) - 64, 0)];
}
export function address(row, col) { let s = ''; while (col > 0) { col--; s = String.fromCharCode(65 + col % 26) + s; col = Math.floor(col / 26); } return s + row; }
export const FUNCTIONS = new Set(['IF', 'IFERROR', 'FALSE', 'TRUE', 'ISNUMBER', 'NOT', 'AND', 'OR', 'ABS', 'SQRT', 'ROUND', 'CEILING', 'CEILING.MATH', 'TEXT', 'MAX', 'MIN', 'SUM', 'COUNT', 'COUNTIF', 'SUMPRODUCT', 'INDEX', 'MATCH', 'VLOOKUP']);
export class Engine {
  constructor(book) {
    this.book = book; this.cells = new Map(); this.changes = new Map(); this.cache = new Map(); this.evaluating = new Set();
    for (const s of book.Sheets) for (const c of s.Cells) this.cells.set(this.key(s.Name, c.Address), c);
  }
  key(s, a) { return `${s}!${a.replaceAll('$', '').toUpperCase()}`; }
  reset() { this.changes.clear(); this.cache.clear(); }
  input(s, c) { const k = this.key(s, c.Address); return this.changes.has(k) ? this.changes.get(k) : c.Value; }
  setInput(s, a, v) {
    const k = this.key(s, a), c = this.cells.get(k);
    if (!c?.Editable) throw new CalculationError('Ячейка доступна только для чтения');
    if (this.book.Custom && (v == null || String(v).trim() === '')) { this.changes.set(k, null); this.cache.clear(); return; }
    if (v == null || String(v).trim() === '') throw new CalculationError('Введите значение');
    if (this.book.Custom && typeof c.Value === 'boolean') { if (!['TRUE','FALSE'].includes(String(v).toUpperCase())) throw new CalculationError('Выберите TRUE или FALSE'); v = String(v).toUpperCase() === 'TRUE'; }
    else if (typeof c.Value === 'string') {
      v = String(v); if (c.Options && !c.Options.includes(v)) throw new CalculationError('Выберите значение из списка');
    } else v = number(String(v).replace(',', '.'));
    this.changes.set(k, v); this.cache.clear();
  }
  saveSession() { return { Source: this.book.Source, Sha256: this.book.Sha256, Inputs: this.book.Sheets.flatMap(s => s.Cells.filter(c => c.Editable).map(c => ({ Sheet: s.Name, Address: c.Address, Value: this.input(s.Name, c) }))) }; }
  loadSession(session) {
    if (!session || session.Source !== this.book.Source || session.Sha256 !== this.book.Sha256 || !Array.isArray(session.Inputs)) throw new CalculationError('Файл относится к другому расчёту или версии формул');
    const next = new Engine(this.book);
    for (const c of session.Inputs) next.setInput(c.Sheet, c.Address, c.Value);
    this.changes = next.changes; this.cache.clear();
  }
  get(s, a) { const value = this.raw(s, a); return value == null && this.cells.get(this.key(s, a))?.Expression ? 0 : value; }
  raw(s, a) {
    const k = this.key(s, a);
    if (this.cache.has(k)) return this.cache.get(k);
    if (this.changes.has(k)) return this.changes.get(k);
    const c = this.cells.get(k); if (!c) return null;
    if (!c.Expression) return c.Value ?? null;
    if (this.evaluating.has(k)) throw new CalculationError(`Циклическая ссылка: ${k}`);
    this.evaluating.add(k);
    try { const v = this.eval(c.Expression, s); this.cache.set(k, v); return v; }
    finally { this.evaluating.delete(k); }
  }
  reference(ref, s) {
    if (Object.hasOwn(this.book.Names, ref)) return this.reference(this.book.Names[ref], s);
    const bang = ref.lastIndexOf('!');
    if (bang >= 0) { s = ref.slice(0, bang).replace(/^'|'$/g, '').replaceAll("''", "'"); ref = ref.slice(bang + 1); }
    ref = ref.replaceAll('$', '');
    if (!ref.includes(':')) return this.raw(s, ref);
    const [a, b] = ref.split(':'), [r1, c1] = position(a), [r2, c2] = position(b);
    if ((r2 - r1 + 1) * (c2 - c1 + 1) > 50000) throw new CalculationError('Слишком большой диапазон');
    const values = []; for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) values.push(this.raw(s, address(r, c)));
    return new Range(values, c2 - c1 + 1);
  }
  eval(e, s) {
    const args = e.Args || [], at = i => this.eval(args[i], s);
    switch (e.Op) {
      case 'NUM': return e.Number;
      case 'STR': return e.Text;
      case 'BOOL': return e.Number !== 0;
      case 'REF': return this.reference(e.Text, s);
      case 'IF': return truth(at(0)) ? at(1) : args.length > 2 ? at(2) : false;
      case 'IFERROR': try { return at(0); } catch (error) { if (!(error instanceof CalculationError)) throw error; return at(1); }
      case 'FALSE': return false; case 'TRUE': return true;
      case 'ISNUMBER': return numeric(at(0)); case 'NOT': return !truth(at(0));
      case 'AND': return args.flatMap(a => flat(this.eval(a, s))).every(truth);
      case 'OR': return args.flatMap(a => flat(this.eval(a, s))).some(truth);
      case 'NEG': return -number(at(0)); case 'POS': return number(at(0)); case 'PERCENT': return number(at(0)) / 100;
      case '+': return finite(number(at(0)) + number(at(1)));
      case '-': return finite(number(at(0)) - number(at(1)));
      case '*': return finite(number(at(0)) * number(at(1)));
      case '/': { const d = number(at(1)); if (d === 0) throw new CalculationError('Деление на ноль'); return finite(number(at(0)) / d); }
      case '^': return finite(number(at(0)) ** number(at(1)));
      case '&': return text(at(0)) + text(at(1));
      case '=': return compare(at(0), at(1)) === 0; case '<>': return compare(at(0), at(1)) !== 0;
      case '<': return compare(at(0), at(1)) < 0; case '>': return compare(at(0), at(1)) > 0;
      case '<=': return compare(at(0), at(1)) <= 0; case '>=': return compare(at(0), at(1)) >= 0;
      case 'ABS': return Math.abs(number(at(0))); case 'SQRT': return finite(Math.sqrt(number(at(0))));
      case 'ROUND': { const n = number(at(0)), f = 10 ** number(at(1)); return Math.sign(n) * Math.floor(Math.abs(n * f) + 0.5) / f; }
      case 'CEILING': case 'CEILING.MATH': { const step = args.length > 1 ? Math.abs(number(at(1))) : 1; return step === 0 ? 0 : Math.ceil(number(at(0)) / step) * step; }
      case 'TEXT': { const format = text(at(1)); if (format !== '0') throw new CalculationError(`Неизвестный формат: ${format}`); return Math.round(number(at(0))).toFixed(0); }
      case 'MAX': case 'MIN': case 'SUM': case 'COUNT': {
        const ns = args.flatMap(a => flat(this.eval(a, s))).filter(numeric);
        if (e.Op === 'COUNT') return ns.length; if (!ns.length) return 0;
        return e.Op === 'MAX' ? Math.max(...ns) : e.Op === 'MIN' ? Math.min(...ns) : ns.reduce((a, b) => a + b, 0);
      }
      case 'COUNTIF': { const v = at(1); return flat(at(0)).filter(a => compare(a, v) === 0).length; }
      case 'SUMPRODUCT': {
        const rs = args.map(a => flat(this.eval(a, s)));
        if (rs.some(r => r.length !== rs[0].length)) throw new CalculationError('Размеры диапазонов не совпадают');
        return rs[0].reduce((sum, _, i) => sum + rs.reduce((p, r) => p * (numeric(r[i]) ? r[i] : 0), 1), 0);
      }
      case 'INDEX': {
        const r = range(at(0)), row = Math.trunc(number(at(1))), col = args.length > 2 ? Math.trunc(number(at(2))) : 1, i = (row - 1) * r.columns + col - 1;
        if (row < 1 || col < 1 || col > r.columns || i >= r.values.length) throw new CalculationError('Недопустимая ссылка INDEX');
        return r.values[i];
      }
      case 'MATCH': {
        if (args.length > 2 && number(at(2)) !== 0) throw new CalculationError('Поддерживается только точный MATCH');
        const v = at(0), i = flat(at(1)).findIndex(a => compare(v, a) === 0);
        if (i < 0) throw new CalculationError('Значение не найдено'); return i + 1;
      }
      case 'VLOOKUP': {
        if (args.length < 4 || truth(at(3))) throw new CalculationError('Поддерживается только точный VLOOKUP');
        const v = at(0), r = range(at(1)), col = Math.trunc(number(at(2)));
        if (col < 1 || col > r.columns) throw new CalculationError('Недопустимая ссылка VLOOKUP');
        for (let i = 0; i < r.values.length; i += r.columns) if (compare(v, r.values[i]) === 0) return r.values[i + col - 1];
        throw new CalculationError('Значение не найдено');
      }
      default: throw new CalculationError(`Неизвестная функция: ${e.Op}`);
    }
  }
  validate(sheetName) {
    const s = this.book.Sheets.find(s => s.Name === sheetName); if (this.book.Custom || s.ReferenceOnly) return null;
    const get = a => number(this.get(sheetName, a));
    for (const c of s.Cells.filter(c => c.Editable && typeof c.Value !== 'string')) {
      const n = number(this.input(sheetName, c));
      const temperature = sheetName === 'Расчет по давлению' && c.Address === 'B5' || sheetName === 'Местное смятие' && c.Address === 'B3';
      const signed = ['Угловые швы', 'Стыковые швы'].includes(sheetName) && ['C3','C4','C5'].includes(c.Address) || sheetName === 'Болты' && ['C4','C5','C6','C39'].includes(c.Address);
      if (!temperature && !signed && n < 0) return `${c.Address}: значение не должно быть отрицательным`;
      if (c.Options && !c.Options.some(o => Number(o) === n)) return `${c.Address}: выберите значение из списка`;
    }
    const positives = { 'Расчет по давлению':'B3 B6 B9', 'Местное смятие':'B5 B6 B7 B8', 'Расчёт':'B6 B10 B11 B12', 'Длина болта':'B2 B3', 'Высота узла':'B2 B3', 'Расстояние до обреза':'C2', 'Угловые швы':'C7 C8 C11 C12 C13', 'Стыковые швы':'C6 C7 C9', 'Болты':'C8 C9 C13 C14 C15 C16 C17 C23 C24 C41 C42 C43 C44 C47' };
    for (const a of (positives[sheetName] || '').split(' ').filter(Boolean)) if (get(a) <= 0) return `${a}: значение должно быть больше нуля`;
    if (sheetName === 'Расчет по давлению' && get('B8') >= 100) return 'Допуск стенки должен быть меньше 100%';
    if (sheetName === 'Расчёт' && get('B8') > get('B6')) return 'Положение нагрузки a должно быть в пределах пролёта L';
    if (sheetName === 'Болты') {
      if (['C8','C14','C41'].some(a => get(a) % 1 !== 0)) return 'Введите целое количество болтов/срезов';
      if (get('C44') <= get('C41') * get('C42')) return 'Ширина фасонки должна превышать сумму диаметров отверстий';
      if (![5.6,5.8,8.8,10.9,12.9].includes(get('C10'))) return 'Класс прочности: 5,6 / 5,8 / 8,8 / 10,9 / 12,9';
      if (typeof this.get(sheetName, 'C21') === 'boolean') return 'В исходном Excel нет сопротивления смятию для выбранной стали и класса точности';
    }
    if (sheetName === 'Длина болта' && this.raw(sheetName, 'B14') == null) return 'В справочнике Excel отсутствует высота гровера для выбранного крепежа';
    if (sheetName === 'Длина болта' && get('B9') % 1 !== 0) return 'Число витков должно быть целым';
    return null;
  }
}
