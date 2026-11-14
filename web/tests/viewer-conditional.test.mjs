import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import JSZip from 'jszip';
import {readSnapshot} from '../src/xlsx-viewer.js';
import {EditSession} from '../src/xlsx-editor.js';

export async function coloredFixture(){
 const book=new ExcelJS.Workbook(),sheet=book.addWorksheet('Модель');
 sheet.columns=[{width:6},{width:28},{width:70},{width:22},{width:26}];
 sheet.mergeCells('A1:E1');sheet.getCell('A1').value='Чек-лист проверки КД — Модель';sheet.getCell('A1').fill={type:'pattern',pattern:'solid',fgColor:{argb:'FF17365D'}};sheet.getCell('A1').font={bold:true,color:{argb:'FFFFFFFF'},size:16};sheet.getRow(1).height=28;
 ['№','Раздел','Контрольный пункт','Статус','Комментарий'].forEach((v,i)=>sheet.getCell(2,i+1).value=v);
 for(let r=3;r<=42;r++){sheet.getCell(r,1).value=r-2;sheet.getCell(r,2).value='Проверка конструкции';sheet.getCell(r,3).value='Проверка доступности для сборки и обслуживания';sheet.getRow(r).height=25;}
 sheet.dataValidations.add('D3:D42',{type:'list',allowBlank:true,formulae:['"Проверено,Не проверено,Не применимо"']});
 sheet.addConditionalFormatting({ref:'A3:E42',rules:[
  {type:'expression',priority:1,formulae:['$D3="Проверено"'],style:{fill:{type:'pattern',pattern:'solid',bgColor:{argb:'FFC6EFCE'}},font:{color:{argb:'FF006100'}}}},
  {type:'expression',priority:2,formulae:['$D3="Не проверено"'],style:{fill:{type:'pattern',pattern:'solid',bgColor:{argb:'FFFFC7CE'}},font:{color:{argb:'FF9C0006'}}}}
 ]});
 sheet.getCell('E6').value=5;sheet.addConditionalFormatting({ref:'E6',rules:[{type:'cellIs',operator:'greaterThan',priority:3,formulae:[3],style:{fill:{type:'pattern',pattern:'solid',bgColor:{argb:'FFBDD7EE'}}}}]});
 book.addWorksheet('Другой лист').getCell('A1').value='Второй лист книги';
 return book.xlsx.writeBuffer();
}

test('Excel expression formatting follows dropdown status across the whole row, resets with undo, and supports cellIs',async()=>{
 const sheets=await readSnapshot(await coloredFixture(),ExcelJS),edit=new EditSession(sheets);await edit.prepare();
 let evaluate=sheets.source.conditionalEvaluator(edit.view());assert.deepEqual(evaluate(sheets[0],'B3'),{});
 assert.equal(evaluate(sheets[0],'E6').backgroundColor,'#BDD7EE');
 edit.apply('Модель','D3','Проверено');edit.apply('Модель','D4','Не проверено');
 let view=edit.view();evaluate=sheets.source.conditionalEvaluator(view);
 for(const col of ['A','B','C','D','E']){assert.equal(evaluate(view[0],col+'3').backgroundColor,'#C6EFCE');assert.equal(evaluate(view[0],col+'4').backgroundColor,'#FFC7CE');}
 edit.undo();view=edit.view();evaluate=sheets.source.conditionalEvaluator(view);assert.deepEqual(evaluate(view[0],'B4'),{});assert.equal(evaluate(view[0],'B3').backgroundColor,'#C6EFCE');
});

test('Conditional formatting definitions and row colors survive a downloaded XLSX copy',async()=>{
 const sheets=await readSnapshot(await coloredFixture(),ExcelJS),edit=new EditSession(sheets);await edit.prepare();edit.apply('Модель','D3','Проверено');
 const bytes=await edit.export(),reopened=await readSnapshot(bytes,ExcelJS);assert.equal(reopened.source.conditionalEvaluator(reopened)(reopened[0],'E3').backgroundColor,'#C6EFCE');
 const before=await JSZip.loadAsync(sheets.source.bytes),after=await JSZip.loadAsync(bytes);
 const rules=async zip=>(await zip.file('xl/worksheets/sheet1.xml').async('string')).match(/<conditionalFormatting[\s\S]*?<\/conditionalFormatting>/g);
 assert.deepEqual(await rules(before),await rules(after));
});
