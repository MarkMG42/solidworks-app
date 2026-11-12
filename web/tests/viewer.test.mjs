import test from 'node:test';
import assert from 'node:assert/strict';
import ExcelJS from 'exceljs';
import JSZip from 'jszip';
import { readSnapshot } from '../src/xlsx-viewer.js';
export async function viewerFixture() {
 const book=new ExcelJS.Workbook();const sheet=book.addWorksheet('Данные');book.addWorksheet('Пустой');
 sheet.getCell('A1').value={formula:'UNSUPPORTED(1)',result:123};
 sheet.getCell('A2').value={formula:'1-1',result:0};
 sheet.getCell('A3').value={formula:'1=2',result:false};
 sheet.getCell('A4').value={formula:'UNKNOWN()'};
 sheet.getCell('A5').value={formula:'IF(TRUE,"","")'};
 sheet.getCell('A6').value={formula:'1/0',result:{error:'#DIV/0!'}};
 sheet.getCell('B1').value='<script>alert(1)</script>';
 const zip=await JSZip.loadAsync(await book.xlsx.writeBuffer());
 let xml=await zip.file('xl/worksheets/sheet1.xml').async('string');
 xml=xml.replace(/<c r="A5"[^>]*>.*?<\/c>/,'<c r="A5" t="str"><f>IF(TRUE,"","")</f><v></v></c>');
 zip.file('xl/worksheets/sheet1.xml',xml);return zip.generateAsync({type:'nodebuffer'});
}
test('Viewer cached results distinguish missing, zero, false, blank and errors without recalculation',async()=>{
 const sheets=await readSnapshot(await viewerFixture(),ExcelJS);const cells=new Map(sheets[0].cells.map(c=>[c.address,c]));
 assert.equal(sheets.length,2);assert.equal(sheets[1].cells.length,0);
 assert.equal(cells.get('A1').text,'123');assert.equal(cells.get('A1').formula,'=UNSUPPORTED(1)');
 assert.equal(cells.get('A2').text,'0');assert.equal(cells.get('A3').text,'false');
 assert.equal(cells.get('A4').missing,true);assert.equal(cells.get('A4').text,'Нет сохранённого результата');
 assert.equal(cells.get('A5').missing,false);assert.equal(cells.get('A5').text,'');
 assert.equal(cells.get('A6').text,'#DIV/0!');
});
