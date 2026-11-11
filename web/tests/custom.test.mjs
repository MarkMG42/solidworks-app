import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import { importCustomWorkbook } from '../src/import-excel.js';
import { Engine } from '../src/engine.js';
test('Custom workbook: arbitrary sheets, signed inputs, cross-sheet formulas, lists and sessions',async()=>{
 const w=new ExcelJS.Workbook(),s=w.addWorksheet('Расчёт'),r=w.addWorksheet('Цены');
 s.getCell('A1').value='Количество';s.getCell('B1').value=-2;s.getCell('C1').value={formula:"B1*'Цены'!A1"};r.getCell('A1').value=10;
 s.getCell('B2').value='Сталь';s.getCell('B2').dataValidation={type:'list',formulae:['"Сталь,Медь"']};
 const b=await importCustomWorkbook(await w.xlsx.writeBuffer(),'Мой','mine.xlsx',ExcelJS,'sha');const e=new Engine(b);
 assert.equal(b.Sheets.length,2);assert.equal(e.get('Расчёт','C1'),-20);assert.equal(e.validate('Расчёт'),null);
 e.setInput('Расчёт','B1','-3');assert.equal(e.get('Расчёт','C1'),-30);
 assert.deepEqual(b.Sheets[0].Cells.find(c=>c.Address==='B2').Options,['Сталь','Медь']);
 const copy=new Engine(b);copy.loadSession(e.saveSession());assert.equal(copy.get('Расчёт','C1'),-30);
 assert.throws(()=>e.setInput('Расчёт','C1','10'),/только для чтения/);
 e.setInput('Расчёт','B1','');assert.equal(e.get('Расчёт','C1'),0);
});
test('Custom workbook rejects empty, unsupported and cyclic formulas',async()=>{
 const w=new ExcelJS.Workbook();w.addWorksheet('Пусто');await assert.rejects(async()=>importCustomWorkbook(await w.xlsx.writeBuffer(),'x','x.xlsx',ExcelJS,'x'));
 const s=w.getWorksheet('Пусто');s.getCell('A1').value={formula:'UNKNOWN(1)'};await assert.rejects(async()=>importCustomWorkbook(await w.xlsx.writeBuffer(),'x','x.xlsx',ExcelJS,'x'));
 s.getCell('A1').value={formula:'A1+1'};await assert.rejects(async()=>importCustomWorkbook(await w.xlsx.writeBuffer(),'x','x.xlsx',ExcelJS,'x'),/Циклическая/);
});
test('Custom replacement allows changed sheet structure and Boolean inputs',async()=>{
 const w=new ExcelJS.Workbook(),s=w.addWorksheet('Новый лист');s.getCell('A1').value=true;s.getCell('B1').value={formula:'IF(A1,1,2)'};
 const {importWorkbook}=await import('../src/import-excel.js');
 const b=await importWorkbook(await w.xlsx.writeBuffer(),{Custom:true,Title:'x',Source:'x.xlsx',Sheets:[{Name:'Старый лист'}]},ExcelJS,'new');
 const e=new Engine(b);e.setInput('Новый лист','A1','FALSE');assert.equal(e.get('Новый лист','B1'),2);const copy=new Engine(b);copy.loadSession(e.saveSession());assert.equal(copy.get('Новый лист','B1'),2);
});
