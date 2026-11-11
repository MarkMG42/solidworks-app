import { Engine, FUNCTIONS, CalculationError, position, address } from './engine.js';
const precedence = { '=':1, '<>':1, '<':1, '>':1, '<=':1, '>=':1, '&':2, '+':3, '-':3, '*':4, '/':4, '^':5 };
function tokenize(formula) {
  const tokens = []; let source = formula.replace(/^=/, '');
  const patterns = [/^"(?:[^"]|"")*"/, /^'(?:[^']|'')+'!\$?[A-Za-z]+\$?\d+(?::\$?[A-Za-z]+\$?\d+)?/, /^(?:[\p{L}_][\p{L}\p{N}_.]*!)?\$?[A-Za-z]+\$?\d+(?::\$?[A-Za-z]+\$?\d+)?/u, /^(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?/, /^[\p{L}_][\p{L}\p{N}_.]*/u, /^(?:<=|>=|<>|[=<>+*/^&%(),-])/];
  while (source.length) {
    source = source.trimStart(); if (!source) break;
    const token = patterns.map(p => p.exec(source)?.[0]).find(Boolean);
    if (!token) throw new CalculationError(`Неизвестный фрагмент формулы: ${source.slice(0, 30)}`);
    tokens.push(token); source = source.slice(token.length);
    if (tokens.length > 10000) throw new CalculationError('Слишком длинная формула');
  }
  return tokens;
}
export function parseFormula(formula, names = {}) {
  const tokens = tokenize(formula); let i = 0;
  function expr(min = 0, depth = 0) {
    if (depth > 100) throw new CalculationError('Слишком глубокая формула');
    const token = tokens[i++]; let n;
    if (token === '+' || token === '-') n = { Op: token === '+' ? 'POS' : 'NEG', Args: [expr(6, depth + 1)] };
    else if (token === '(') { n = expr(0, depth + 1); if (tokens[i++] !== ')') throw new CalculationError('Ожидается закрывающая скобка'); }
    else if (tokens[i] === '(') {
      const op = token.toUpperCase().replace(/^_XLFN\./, '');
      if (!FUNCTIONS.has(op)) throw new CalculationError(`Функция ${op} пока не поддерживается`);
      i++; const args = [];
      while (tokens[i] !== ')') { args.push(expr(0, depth + 1)); if (tokens[i] === ',') i++; else break; }
      if (tokens[i++] !== ')') throw new CalculationError('Неверные аргументы функции');
      n = { Op: op, Args: args };
    } else if (token?.startsWith('"')) n = { Op:'STR', Text:token.slice(1,-1).replaceAll('""','"') };
    else if (/^(?:\d|\.\d)/.test(token || '')) n = { Op:'NUM', Number:Number(token) };
    else if (['TRUE','FALSE'].includes(token?.toUpperCase())) n = { Op:'BOOL', Number:token.toUpperCase() === 'TRUE' ? 1 : 0 };
    else {
      if (!token || !Object.hasOwn(names, token) && !/^(?:(?:'[^']*(?:''[^']*)*'|[\p{L}_][\p{L}\p{N}_.]*)!)?\$?[a-z]+\$?\d+(?::\$?[a-z]+\$?\d+)?$/iu.test(token)) throw new CalculationError(`Неизвестная ссылка: ${token}`);
      n = { Op:'REF', Text:token };
    }
    while (i < tokens.length) {
      const op = tokens[i];
      if (op === '%') { i++; n = { Op:'PERCENT', Args:[n] }; continue; }
      if (!Object.hasOwn(precedence, op) || precedence[op] < min) break;
      i++; n = { Op:op, Args:[n, expr(precedence[op] + 1, depth + 1)] };
    }
    return n;
  }
  const result = expr(); if (i !== tokens.length) throw new CalculationError(`Лишний фрагмент формулы: ${tokens[i]}`); return result;
}
function plain(value) {
  if (value == null || typeof value !== 'object') return value;
  if (value.richText) return value.richText.map(v => v.text).join('');
  if (value.text != null) return value.text;
  if (value.error) throw new CalculationError(`Ошибка ячейки: ${value.error}`);
  throw new CalculationError('Неподдерживаемый тип ячейки');
}
function listValues(formula, book) {
  if (!formula) return null;
  formula = String(formula).replace(/^=/, '');
  if (formula.startsWith('"')) return formula.slice(1,-1).split(',');
  const reference = book.Names[formula] || formula;
  const bang = reference.lastIndexOf('!'); if (bang < 0) return null;
  const s = book.Sheets.find(s => s.Name === reference.slice(0,bang).replace(/^'|'$/g,'').replaceAll("''", "'")); if (!s) return null;
  const [from,to=from] = reference.slice(bang+1).replaceAll('$','').split(':');
  const [r1,c1] = position(from), [r2,c2] = position(to); const values = [];
  for (const c of s.Cells) if (c.Row >= r1 && c.Row <= r2 && c.Column >= c1 && c.Column <= c2 && c.Value != null) values.push(String(c.Value));
  return [...new Set(values)];
}
export async function importWorkbook(buffer, original, ExcelJS, digest) {
  if (buffer.byteLength > 15 * 1024 * 1024) throw new CalculationError('Максимальный размер Excel — 15 МБ');
  const workbook = new ExcelJS.Workbook(); await workbook.xlsx.load(buffer);
  const result = { Title: original.Title, Source: original.Source, Sha256: digest, Names:{}, Sheets:[] };
  for (const item of workbook.definedNames.model) if (item.ranges.length === 1) result.Names[item.name] = item.ranges[0];
  for (const s of original.Sheets) if (!workbook.getWorksheet(s.Name)) throw new CalculationError(`В Excel отсутствует лист «${s.Name}»`);
  let total = 0;
  for (const ws of workbook.worksheets) {
    const old = original.Sheets.find(s => s.Name === ws.name);
    const sheet = { Name:ws.name, ReferenceOnly:old?.ReferenceOnly ?? true, Cells:[], Merged:ws.model.merges || [] };
    ws.eachRow(row => row.eachCell(cell => {
      if (cell.isMerged && cell.address !== cell.master.address) return;
      const formula = cell.formula, value = formula ? null : plain(cell.value);
      if (!formula && value == null) return;
      if (++total > 50000 || cell.row > 10000 || cell.col > 100) throw new CalculationError('Таблица слишком большая');
      const oldCell = old?.Cells.find(c => c.Address === cell.address);
      const c = { Address:cell.address, Row:cell.row, Column:cell.col, Format:cell.numFmt || 'General', Value:value, Formula:formula ? '=' + formula.replace(/^=/,'') : null, Expected:null, Editable:!sheet.ReferenceOnly && !formula && cell.col === (original.Source.includes('прочность') ? 3 : 2) };
      if (formula) {
        try { c.Expression = parseFormula(formula, result.Names); }
        catch (error) { throw new CalculationError(`${ws.name}!${cell.address}: ${error.message}`); }
      }
      c._validation = cell.dataValidation?.type === 'list' ? cell.dataValidation.formulae?.[0] : null;
      if (oldCell?.Options) c.Options = oldCell.Options;
      sheet.Cells.push(c);
    }));
    if (sheet.Cells.length) result.Sheets.push(sheet);
  }
  for (const sheet of result.Sheets) for (const c of sheet.Cells) {
    const options = listValues(c._validation, result); if (options?.length) c.Options = options;
    if (!options?.length && original.Source.includes('прочность') && c.Editable && typeof c.Value === 'string') {
      const source = ['Угловые швы','Стыковые швы'].includes(sheet.Name) ? 'для сварки' : 'для болтов';
      const col = c.Address === 'C10' && sheet.Name === 'Угловые швы' ? 'A' : c.Address === 'C11' ? 'F' : source === 'для сварки' ? 'B' : 'D';
      const derived = listValues(`'${source}'!${col}2:${col}14`, result);
      if (derived?.length) c.Options = derived;
    }
    delete c._validation;
  }
  const engine = new Engine(result);
  for (const s of result.Sheets) for (const c of s.Cells.filter(c => c.Expression)) {
    try { engine.get(s.Name,c.Address); }
    catch (error) { throw new CalculationError(`${s.Name}!${c.Address}: ${error.message}. Предыдущие формулы сохранены.`); }
  }
  return result;
}
