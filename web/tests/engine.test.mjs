import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { Engine } from '../src/engine.js';
const books = JSON.parse(await readFile(new URL('../../addin/portable/Calculations/Workbooks.json', import.meta.url)));
const cases = JSON.parse(await readFile(new URL('../../addin/tests/calculations/SpreadsheetScenarios.json', import.meta.url)));
function same(actual, expected, label) {
  if (typeof expected === 'number') assert.ok(typeof actual === 'number' && Math.abs(actual - expected) <= 1e-8 * Math.max(1, Math.abs(expected)), `${label}: ${actual} != ${expected}`);
  else assert.ok(Object.is(actual, expected) || expected == null && actual === '', `${label}: ${actual} != ${expected}`);
}
for (const book of books) test(`Excel cached values: ${book.Source}`, () => {
  const e = new Engine(book); let count = 0;
  for (const s of book.Sheets) for (const c of s.Cells.filter(c => c.Expression)) { same(e.get(s.Name, c.Address), c.Expected, `${s.Name}!${c.Address}`); count++; }
  console.log(`${book.Source}: ${count} formula cells matched`);
});
test('256 independent spreadsheet scenarios', () => {
  assert.equal(cases.length, 256); let count = 0;
  for (const scenario of cases) {
    const e = new Engine(books[scenario.Book]);
    for (const c of scenario.Inputs) e.setInput(c.Sheet, c.Address, c.Value);
    for (const c of scenario.Results) { same(e.get(c.Sheet, c.Address), c.Value, `${scenario.Label} ${c.Sheet}!${c.Address}`); count++; }
  }
  assert.equal(count, 4363); console.log(`${cases.length} independent scenarios / ${count} results matched`);
});
test('Sessions, Russian numbers, reset, and atomic import', () => {
  const e = new Engine(books[4]); e.setInput('Высота узла','B3','3000,5'); assert.equal(e.get('Высота узла','B11'),2496.5);
  const saved = JSON.parse(JSON.stringify(e.saveSession())); e.reset(); assert.equal(e.get('Высота узла','B11'),1646);
  e.loadSession(saved); assert.equal(e.get('Высота узла','B11'),2496.5);
  saved.Inputs.push({Sheet:'Высота узла',Address:'B11',Value:999}); assert.throws(() => e.loadSession(saved)); assert.equal(e.get('Высота узла','B11'),2496.5);
  for (const v of ['', 'text', 'NaN', 'Infinity', '1e999']) assert.throws(() => e.setInput('Высота узла','B3',v));
});
test('Invalid dimensions and incomplete source data', () => {
  const beam = new Engine(books[2]); beam.setInput('Расчёт','B8',801); assert.ok(beam.validate('Расчёт'));
  const bolts = new Engine(books[3]); bolts.setInput('Болты','C12','С590'); assert.ok(bolts.validate('Болты'));
  const pipe = new Engine(books[0]); pipe.setInput('Местное смятие','B6',0); assert.ok(pipe.validate('Местное смятие'));
  for (const b of books) { const e = new Engine(b); for (const s of b.Sheets) if (!s.ReferenceOnly) assert.equal(e.validate(s.Name), null); }
});
