import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import ExcelJS from 'exceljs';
import {Engine,UnsupportedCalculationError} from '../src/engine.js';
import {parseFormula,importCustomWorkbook} from '../src/import-excel.js';
const make=()=>new Engine({Names:{Rate:"'Цены'!A1"},Sheets:[{Name:'Расчёт',Cells:[{Address:'A30',Value:2},{Address:'A31',Value:'2'},{Address:'A32',Value:true}]},{Name:'Цены',Cells:[{Address:'A1',Value:7}]},{Name:'Пустой',Cells:[]}]});
const evaluate=f=>make().eval(parseFormula(f,{Rate:1}),'Расчёт');
test('17 independently recalculated formula boundaries',async()=>{
 const fixture=JSON.parse(await readFile(new URL('./fixtures/formula-boundaries.json',import.meta.url)));
 assert.equal(fixture.cases.length,17);for(const c of fixture.cases)assert.equal(evaluate(c.formula),c.expected,c.formula);
});
test('Excel literal versus cell aggregation and signed rounding modes',()=>{
 for(const [formula,expected] of [['SUM("3",TRUE,2)',6],['MAX("3",TRUE,2)',3],['MIN("3",TRUE,2)',1],['SUM(A30:A32)',2],['COUNT(A30:A32)',1],['CEILING.MATH(-4.3,2,1)',-6],['CEILING.MATH(-4.3,2,0)',-4],['rate+1',8],["'Пустой'!A1+1",1]])assert.equal(evaluate(formula),expected,formula);
});
test('Do not silently simplify unsupported variants or hide them through IFERROR',()=>{
 for(const f of ['MATCH(2,A30:A32)','IFERROR(MATCH(2,A30:A32),999)','IFERROR(COUNTIF(A30:A32,">1"),999)','IFERROR(TEXT(1,"0.00"),999)','IFERROR(VLOOKUP(2,A30:A32,1,TRUE),999)'])assert.throws(()=>evaluate(f),UnsupportedCalculationError,f);
 for(const f of ['SUM()','ROUND(1,2,3)','TRUE(1)','IF(1)','CEILING(1)','ABS(1,2)'])assert.throws(()=>parseFormula(f),/аргументов/,f);
 assert.equal(evaluate('IFERROR(1/0,123)'),123);
});
test('Reject missing sheets, bad references and overflowing results',()=>{
 for(const f of ["'Удалён'!A1+1",'A0+1','ROUND(1e308,100)','SUM(1e308,1e308)']){
 const e=make();e.book.Sheets[0].Cells.push({Address:'B1',Expression:parseFormula(f)});e.cells.set(e.key('Расчёт','B1'),e.book.Sheets[0].Cells.at(-1));assert.throws(()=>e.get('Расчёт','B1'),undefined,f);
 }
});
test('Import compares cached formula results and preserves valid empty sheet references',async()=>{
 const w=new ExcelJS.Workbook(),s=w.addWorksheet('Лист');w.addWorksheet('Пустой');s.getCell('A1').value={formula:"'Пустой'!A1+1",result:1};
 await importCustomWorkbook(await w.xlsx.writeBuffer(),'t','t.xlsx',ExcelJS,'x');
 s.getCell('A1').value={formula:'1+1',result:3};await assert.rejects(async()=>importCustomWorkbook(await w.xlsx.writeBuffer(),'t','t.xlsx',ExcelJS,'x'),/отличается/);
 s.getCell('A1').value={formula:'1+1',result:2};const b=await importCustomWorkbook(await w.xlsx.writeBuffer(),'t','t.xlsx',ExcelJS,'x');assert.equal(new Engine(b).get('Лист','A1'),2);
});

test('Blank references compare correctly with Boolean, string and zero',()=>{for(const f of ["'Пустой'!A1=FALSE","'Пустой'!A1=0","'Пустой'!A1=\"\""])assert.equal(evaluate(f),true,f);assert.throws(()=>parseFormula('COUNT(1e999)'),/диапазона/);});

test('Range shape mismatch and array operations cannot produce plausible scalar results',()=>{
 assert.throws(()=>evaluate('SUMPRODUCT(A30:A31,A30:B30)'),/Размеры/);
 assert.throws(()=>evaluate('MATCH(2,A30:B31,0)'),/одну строку/);
 assert.throws(()=>evaluate('IFERROR(SUMPRODUCT(A30:A31+1),999)'),UnsupportedCalculationError);
});
