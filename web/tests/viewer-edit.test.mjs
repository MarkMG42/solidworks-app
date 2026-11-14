import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import JSZip from 'jszip';
import {readSnapshot} from '../src/xlsx-viewer.js';
import {EditSession,patchSheet,parseEdit} from '../src/xlsx-editor.js';

export async function editFixture(){
 const book=new ExcelJS.Workbook(),s=book.addWorksheet('Ввод');book.addWorksheet('Итог');
 s.getCell('A1').value=2;s.getCell('A1').fill={type:'pattern',pattern:'solid',fgColor:{argb:'FFFFF2CC'}};s.getColumn(1).width=20;
 s.getCell('B1').value={formula:'A1*3',result:6};s.getCell('B1').numFmt='0.00';
 s.getCell('C1').value={formula:'UNSUPPORTED(A1)',result:99};s.getCell('D1').value={formula:'C1+1',result:100};
 s.getCell('E1').value={formula:'IFERROR(C1,42)',result:99};
 s.getCell('F1').value={formula:'A1*2',result:999}; // Baseline mismatch must prevent trusting this formula.
 s.getCell('G1').value={formula:'1/(A1-2)',result:{error:'#DIV/0!'}};
 s.getCell('A2').value='Подпись';s.getCell('B2').value=false;s.getCell('A3').value=new Date('2026-11-14T00:00:00Z');s.getCell('A3').numFmt='dd.mm.yyyy';
 s.getCell('C4').value='Край';book.getWorksheet('Итог').getCell('A1').value={formula:"'Ввод'!B1+1",result:7};
 const zip=await JSZip.loadAsync(await book.xlsx.writeBuffer());
 zip.file('custom/keep.xml','<keep>Do not modify this unsupported part</keep>');return zip.generateAsync({type:'nodebuffer'});
}
async function session(){const sheets=await readSnapshot(await editFixture(),ExcelJS);const edit=new EditSession(sheets);await edit.prepare();return edit;}

test('Interactive edits recalculate supported formulas and cross-sheet dependencies; unsupported/mismatching results are blocked',async()=>{
 const edit=await session();edit.apply('Ввод','A1','4,5');const view=edit.view(),get=a=>view[0].cells.find(c=>c.address===a);
 assert.equal(get('A1').display,'4.5');assert.equal(get('B1').display,'13.50');assert.equal(view[1].cells[0].display,'14.5');
 for(const a of ['C1','D1','E1','F1']){assert.equal(get(a).unavailable,true);assert.equal(get(a).display,'Не пересчитано');}
 assert.equal(get('G1').display,'0.4');
 assert.throws(()=>edit.apply('Ввод','B1','9'),/только для чтения/);
 assert.throws(()=>edit.apply('Ввод','Z999','9'),/пределами/);
 edit.undo();assert.equal(edit.dirty,false);assert.equal(edit.view()[0].cells.find(c=>c.address==='B1').display,'6.00');
});

test('Edited XLSX preserves unrelated ZIP parts, style, original formulas, typed values and removes all obsolete caches',async()=>{
 const edit=await session();edit.apply('Ввод','A1','8');edit.apply('Ввод','A2','<b>новая & подпись</b>');edit.apply('Ввод','B2','TRUE');edit.apply('Ввод','B3','25');edit.apply('Ввод','A3','15.11.2026');
 const bytes=await edit.export(),zip=await JSZip.loadAsync(bytes);
 assert.equal(await zip.file('custom/keep.xml').async('string'),'<keep>Do not modify this unsupported part</keep>');
 assert.match(await zip.file('xl/workbook.xml').async('string'),/fullCalcOnLoad="1"/);
 const sheets=await readSnapshot(bytes,ExcelJS),get=a=>sheets[0].cells.find(c=>c.address===a);
 assert.equal(get('A1').value,8);assert.equal(get('A1').css.backgroundColor,'#FFF2CC');
 assert.equal(get('A2').text,'<b>новая & подпись</b>');assert.equal(get('B2').value,true);assert.equal(get('B3').value,25);assert.equal(get('A3').display,'15.11.2026');
 for(const sheet of sheets)for(const c of sheet.cells.filter(c=>c.formula))assert.equal(c.missing,true,c.address);
 assert.equal(get('B1').formula,'=A1*3');assert.equal(get('C1').formula,'=UNSUPPORTED(A1)');
 const original=await JSZip.loadAsync(edit.source.bytes);assert.equal(await zip.file('xl/styles.xml').async('string'),await original.file('xl/styles.xml').async('string'));
});

test('XML patch inserts blank cells/rows in order, handles namespaces/self-closing sheetData and escapes text',()=>{
 const xml='<x:worksheet xmlns:x="urn:test"><x:sheetData><x:row r="1"><x:c r="A1"><x:v>2</x:v></x:c><x:c r="C1"/></x:row><x:row r="3"/></x:sheetData></x:worksheet>';
 const patched=patchSheet(xml,new Map([['B1',' x<& '],['A2',false],['A3',5]]));
 assert.ok(patched.indexOf('r="A1"')<patched.indexOf('r="B1"'));assert.ok(patched.indexOf('r="B1"')<patched.indexOf('r="C1"'));
 assert.match(patched,/<x:row r="2">/);assert.match(patched,/ x&lt;&amp; /);assert.match(patched,/<x:row r="3"><x:c r="A3">/);
 assert.match(patchSheet('<worksheet><sheetData/></worksheet>',new Map([['A1',3]])),/<sheetData><row r="1"><c r="A1"><v>3<\/v>/);
});

test('Editor input handles zero/blank/boolean/dates/percentages/literal formula text and rejects invalid numbers',()=>{
 assert.equal(parseEdit('0',{kind:'number'}),0);assert.equal(parseEdit('',{kind:'number'}),null);
 assert.equal(parseEdit('12,5%',{kind:'number'}),0.125);assert.equal(parseEdit('FALSE',{kind:'boolean'}),false);
 assert.equal(parseEdit('0012',{kind:'string'}),'0012');assert.equal(parseEdit("'=SUM(A1)",{}),'=SUM(A1)');
 assert.throws(()=>parseEdit('=A1',{}),/формул/);assert.throws(()=>parseEdit('1e999',{kind:'number'}),/диапазона/);assert.throws(()=>parseEdit('31.02.2026',{kind:'date'}),/дата/);
 assert.equal(parseEdit('2026-11-14',{kind:'date'},false)-parseEdit('2026-11-14',{kind:'date'},true),1462);
});
