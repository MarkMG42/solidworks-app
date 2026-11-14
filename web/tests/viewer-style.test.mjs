import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import { SaxesParser } from 'saxes';
import { readSnapshot } from '../src/xlsx-viewer.js';
import { excelColor, readTheme } from '../src/xlsx-style.js';

export async function styledFixture(date1904=false) {
  const book=new ExcelJS.Workbook();book.properties.date1904=date1904;
  const sheet=book.addWorksheet('Ведомость', {views:[{showGridLines:false}]});
  sheet.columns=[{width:6},{width:29},{width:12},{width:17},{width:19},{width:18},{width:9,hidden:true},{width:12}];
  sheet.mergeCells('A1:F2');sheet.getCell('A1').value='ВЕДОМОСТЬ МАТЕРИАЛОВ';
  sheet.getCell('A1').font={name:'Arial',size:18,bold:true,color:{argb:'FFFFFFFF'}};
  sheet.getCell('A1').fill={type:'pattern',pattern:'solid',fgColor:{argb:'FF1F4E78'}};
  sheet.getCell('A1').alignment={horizontal:'center',vertical:'middle'};
  sheet.getRow(1).height=24;sheet.getRow(2).height=24;
  ['№','Наименование','Кол-во','Цена','Стоимость','Дата'].forEach((value,i)=>{
    const cell=sheet.getCell(4,i+1);cell.value=value;
    cell.font={name:'Arial',size:11,bold:true,color:{argb:'FF17365D'}};
    cell.fill={type:'pattern',pattern:'solid',fgColor:{argb:'FFD9EAF7'}};
    cell.alignment={horizontal:'center',vertical:'middle',wrapText:true};
    cell.border={bottom:{style:'medium',color:{argb:'FF4472C4'}}};
  });sheet.getRow(4).height=30;
  const records=[['Лист стальной',12,1028.805,12345.67],['Профильная труба',3,2500,7500],['Крепёж',24,15.5,372]];
  records.forEach((record,i)=>{
    const r=i+5;sheet.getRow(r).height=24;
    sheet.getCell(r,1).value=i+1;sheet.getCell(r,2).value=record[0];sheet.getCell(r,3).value=record[1];
    sheet.getCell(r,4).value=record[2];sheet.getCell(r,4).numFmt='#,##0.00';
    sheet.getCell(r,5).value={formula:'UNSUPPORTED_PRICE('+i+')',result:record[3]};sheet.getCell(r,5).numFmt='#,##0.00" ₽"';
    sheet.getCell(r,6).value=new Date('2026-11-14T00:00:00Z');sheet.getCell(r,6).numFmt='dd.mm.yyyy';
  });
  sheet.getCell('C8').value=0.125;sheet.getCell('C8').numFmt='0.0%';
  sheet.getCell('D8').value=0;sheet.getCell('D8').numFmt='0.00';
  sheet.getCell('E8').value=-1234.5;sheet.getCell('E8').numFmt='#,##0.00;(#,##0.00)';
  sheet.getCell('B9').value={formula:'NO_CACHED_RESULT()'};
  sheet.getCell('B10').value={richText:[{text:'Важно: ',font:{bold:true,color:{argb:'FFC00000'}}},{text:'проверить размеры',font:{italic:true}}]};
  sheet.getRow(11).hidden=true;sheet.getCell('B11').value='Скрытая строка';
  sheet.getCell('H12').fill={type:'pattern',pattern:'solid',fgColor:{theme:4,tint:0.6}};
  sheet.getCell('H12').border={top:{style:'double',color:{argb:'FFC00000'}}};
  sheet.mergeCells('A49:C52');sheet.getCell('A49').value='Объединение через страницу';
  sheet.getCell('A49').fill={type:'pattern',pattern:'solid',fgColor:{argb:'FFFFF2CC'}};
  sheet.getCell('A49').alignment={wrapText:true,vertical:'middle'};
  return book.xlsx.writeBuffer();
}

test('Workbook appearance retains merges, styled blanks, theme colors, dimensions, hidden rows and rich text',async()=>{
  const [sheet]=await readSnapshot(await styledFixture(),ExcelJS);
  const get=address=>sheet.cells.find(c=>c.address===address);
  assert.equal(sheet.rows,52);assert.equal(sheet.columns,8);assert.equal(sheet.showGridLines,false);
  assert.equal(sheet.columnWidths[1],208);assert.equal(sheet.columnWidths[6],0);
  assert.equal(sheet.rowHeights[0],32);assert.equal(sheet.rowHeights[10],0);
  assert.equal(sheet.merges[0].master,'A1');assert.equal(sheet.merges[0].right,6);assert.equal(sheet.merges[0].bottom,2);
  assert.equal(get('A1').css.backgroundColor,'#1F4E78');assert.equal(get('A1').css.fontSize,'18pt');
  assert.equal(get('A1').css.color,'#FFFFFF');assert.equal(get('A1').css.textAlign,'center');
  assert.equal(get('A4').css.borderBottom,'2px solid #4472C4');
  assert.equal(get('H12').text,'');assert.equal(get('H12').css.borderTop,'3px double #C00000');
  assert.match(get('H12').css.backgroundColor,/^#[\da-f]{6}$/i);
  assert.equal(get('B10').richText[0].css.color,'#C00000');assert.equal(get('B10').richText[1].css.fontStyle,'italic');
});

test('Excel number formats use only saved results, including date1904, percentages, currency, zero and negative values',async()=>{
  for(const date1904 of [false,true]){
    const [sheet]=await readSnapshot(await styledFixture(date1904),ExcelJS);
    const get=address=>sheet.cells.find(c=>c.address===address);
    assert.equal(get('D5').display,'1,028.81');assert.equal(get('E5').display,'12,345.67 ₽');
    assert.equal(get('E5').formula,'=UNSUPPORTED_PRICE(0)');assert.equal(get('E5').text,'12345.67');
    assert.equal(get('F5').display,'14.11.2026');assert.equal(get('C8').display,'12.5%');
    assert.equal(get('D8').display,'0.00');assert.equal(get('E8').display,'(1,234.50)');
    assert.equal(get('B9').display,'Нет сохранённого результата');
  }
});

test('Theme palette and fonts are taken from the workbook, tint preserves color and invalid colors are inert',()=>{
  const theme=readTheme('<a:theme xmlns:a="test"><a:clrScheme><a:accent1><a:srgbClr val="336699"/></a:accent1></a:clrScheme><a:fontScheme><a:minorFont><a:latin typeface="Arial"/></a:minorFont></a:fontScheme></a:theme>',SaxesParser);
  assert.equal(excelColor({theme:4},theme),'#336699');assert.equal(theme.minor,'Arial');
  assert.equal(excelColor({argb:'FF000000',tint:0.5},theme),'#808080');
  assert.equal(excelColor({argb:'url(x)'},theme),'#000000');
});
