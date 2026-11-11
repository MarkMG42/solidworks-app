import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import ExcelJS from 'exceljs';
import { importWorkbook, parseFormula } from '../src/import-excel.js';
import { Engine } from '../src/engine.js';
const books = JSON.parse(await readFile(new URL('../../addin/portable/Calculations/Workbooks.json', import.meta.url)));
for (const original of books) test(`Excel upload: ${original.Source}`, async () => {
  const bytes = await readFile(new URL(`../../installer/calculations/Excel/${original.Source}`, import.meta.url));
  const imported = await importWorkbook(bytes,original,ExcelJS,createHash('sha256').update(bytes).digest('hex'));
  const engine = new Engine(imported); let count = 0;
  for (const s of original.Sheets) for (const c of s.Cells.filter(c => c.Expression)) {
    const actual = engine.get(s.Name,c.Address), expected = c.Expected;
    if (typeof expected === 'number') assert.ok(typeof actual === 'number' && Math.abs(actual-expected)<=1e-8*Math.max(1,Math.abs(expected)), `${s.Name}!${c.Address}: ${actual} != ${expected}`);
    else assert.ok(Object.is(actual,expected) || expected == null && actual === '',`${s.Name}!${c.Address}: ${actual} != ${expected}`);
    count++;
  }
  console.log(`Imported ${original.Source}: ${count} formulas matched`);
});
test('Modified Excel formula is evaluated; unsupported functions are rejected', async () => {
  const original = books[4], bytes = await readFile(new URL(`../../installer/calculations/Excel/${original.Source}`,import.meta.url));
  const w = new ExcelJS.Workbook(); await w.xlsx.load(bytes);
  w.getWorksheet('Высота узла').getCell('B11').value = { formula:'B3-B10-100' };
  const changed = await importWorkbook(await w.xlsx.writeBuffer(),original,ExcelJS,'test-version');
  assert.equal(new Engine(changed).get('Высота узла','B11'),1546);
  assert.throws(()=>parseFormula('WEBSERVICE("https://example.com")'),/не поддерживается/);
  assert.throws(()=>parseFormula('SUM(1,2);globalThis.alert(1)'),/Неизвестный фрагмент/);
  w.getWorksheet('Высота узла').getCell('B11').value = {formula:'UNKNOWN(B3)'};
  const bad = await w.xlsx.writeBuffer();
  await assert.rejects(()=>importWorkbook(bad,original,ExcelJS,'bad'),/не поддерживается/);
});
