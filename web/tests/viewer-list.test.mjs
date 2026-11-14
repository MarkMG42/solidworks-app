import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import JSZip from 'jszip';
import {SaxesParser} from 'saxes';
import {readSnapshot} from '../src/xlsx-viewer.js';
import {EditSession} from '../src/xlsx-editor.js';
import {readListRules} from '../src/xlsx-validation.js';

export async function listFixture(){
 const b=new ExcelJS.Workbook(),s=b.addWorksheet('Модель'),refs=b.addWorksheet('Справочники',{state:'hidden'});
 s.columns=[{width:6},{width:30},{width:75},{width:22},{width:25}];
 s.mergeCells('A1:E1');s.getCell('A1').value='Чек-лист проверки КД — Модель';s.getCell('A1').font={bold:true,color:{argb:'FFFFFFFF'},size:16};s.getCell('A1').fill={type:'pattern',pattern:'solid',fgColor:{argb:'FF17365D'}};s.getRow(1).height=28;
 ['№','Раздел','Контрольный пункт','Статус','Комментарий'].forEach((v,i)=>{s.getCell(2,i+1).value=v;});
 for(let r=3;r<=80;r++){s.getCell(r,1).value=r-2;s.getCell(r,2).value='Проверка конструкции';s.getCell(r,3).value='Проверка доступности для сборки и обслуживания';s.getRow(r).height=25;}
 ['В работе','Проверено','Не применимо'].forEach((v,i)=>refs.getCell(i+1,1).value=v);
 [0,10,'0012'].forEach((v,i)=>refs.getCell(i+1,2).value=v);
 b.definedNames.add("'Справочники'!$A$1:$A$3",'Статусы');b.definedNames.add("'Справочники'!$B$1:$B$3",'Числа');
 s.dataValidations.add('D3:D80',{type:'list',allowBlank:true,formulae:['Статусы']});
 s.getCell('E3').dataValidation={type:'list',allowBlank:true,formulae:['"Да,Нет"']};
 s.getCell('E4').dataValidation={type:'list',allowBlank:true,formulae:["'Справочники'!$A$1:$A$3"]};
 s.getCell('E5').dataValidation={type:'list',allowBlank:true,formulae:['Числа']};
 s.getCell('E6').value='Статусы';s.getCell('E7').dataValidation={type:'list',allowBlank:true,formulae:['INDIRECT($E$6)']};
 s.getCell('E9').value=0;s.getCell('E9').dataValidation={type:'list',allowBlank:true,formulae:['"0,1,2"']};
 s.getCell('E8').dataValidation={type:'list',allowBlank:true,formulae:['OFFSET(A1,0,0,2,1)']};
 return b.xlsx.writeBuffer();
}
async function session(){const edit=new EditSession(await readSnapshot(await listFixture(),ExcelJS));await edit.prepare();return edit;}

test('Blank list cells resolve named hidden-sheet ranges, literals, direct references, numeric types and INDIRECT',async()=>{
 const edit=await session();assert.equal(edit.sheets[0].cells.some(c=>c.address==='D4'),false);
 assert.deepEqual(edit.list('Модель','D4').options.map(o=>o.text),['В работе','Проверено','Не применимо']);
 assert.deepEqual(edit.list('Модель','E3').options.map(o=>o.text),['Да','Нет']);
 assert.deepEqual(edit.list('Модель','E4').options.map(o=>o.text),['В работе','Проверено','Не применимо']);
 edit.apply('Модель','E5','0012');assert.equal(edit.edits.get(edit.key('Модель','E5')),'0012');
 edit.apply('Модель','E5','0');assert.equal(edit.edits.get(edit.key('Модель','E5')),0);
 edit.apply('Модель','E9','2');assert.equal(edit.edits.get(edit.key('Модель','E9')),2);
 edit.apply('Модель','D4','Проверено');assert.equal(edit.view()[0].cells.find(c=>c.address==='D4').text,'Проверено');
 assert.throws(()=>edit.apply('Модель','D4','Придуманное значение'),/списка/);
 assert.deepEqual(edit.list('Модель','E7').options.map(o=>o.text),['В работе','Проверено','Не применимо']);
 edit.apply('Модель','E6','Числа');assert.deepEqual(edit.list('Модель','E7').options.map(o=>o.text),['0','10','0012']);
 assert.match(edit.list('Модель','E8').reason,/не поддерживается/);
});

test('Choosing a dropdown value survives XLSX export without modifying validation definitions',async()=>{
 const edit=await session();edit.apply('Модель','D4','Проверено');const bytes=await edit.export();
 const again=new EditSession(await readSnapshot(bytes,ExcelJS));assert.equal(again.sheets[0].cells.find(c=>c.address==='D4').text,'Проверено');
 assert.deepEqual(again.list('Модель','D4').options.map(o=>o.text),['В работе','Проверено','Не применимо']);
 const before=await JSZip.loadAsync(edit.source.bytes),after=await JSZip.loadAsync(bytes);
 const rules=async zip=>(await zip.file('xl/worksheets/sheet1.xml').async('string')).match(/<dataValidations[\s\S]*?<\/dataValidations>/)[0];
 assert.equal(await rules(before),await rules(after));
});

test('List reader supports x14 validation and relative rules without expanding full-column validation',async()=>{
 const edit=await session(),sheet=edit.sheets[0];
 edit.source.listNames.push({name:'Статусы',scope:0,text:"'Справочники'!$B$1:$B$3"});
 assert.deepEqual(edit.list('Модель','D4').options.map(o=>o.text),['0','10','0012']);
 sheet.listRules=readListRules('<worksheet xmlns:x14="test" xmlns:xm="formula"><x14:dataValidations><x14:dataValidation type="list" allowBlank="1"><x14:formula1><xm:f>"Да,Нет"</xm:f></x14:formula1><xm:sqref>D4:D1048576</xm:sqref></x14:dataValidation></x14:dataValidations></worksheet>',SaxesParser);
 assert.deepEqual(edit.list('Модель','D4').options.map(o=>o.text),['Да','Нет']);assert.equal(sheet.listRules.length,1);
 sheet.listRules=readListRules('<worksheet><dataValidations><dataValidation type="list" sqref="D3:D4"><formula1>E3:E4</formula1></dataValidation></dataValidations></worksheet>',SaxesParser);
 // Relative references shift with the validated cell (D4 uses E4:E5).
 edit.apply('Модель','E4','В работе');edit.apply('Модель','E5','10');
 assert.deepEqual(edit.list('Модель','D4').options.map(o=>o.text),['В работе','10']);
});
