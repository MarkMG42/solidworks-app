"""Interactive worksheet edit, calculation, undo and lossless XLSX-copy checks."""
import argparse,os,subprocess
from pathlib import Path
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument('--url',required=True);args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
fixture=root/'web/test-results/viewer-edit.xlsx';fixture.parent.mkdir(exist_ok=True)
subprocess.run(['node','--input-type=module','-e',"import {editFixture} from './tests/viewer-edit.test.mjs';import fs from 'node:fs';fs.writeFileSync('test-results/viewer-edit.xlsx',await editFixture())"],cwd=root/'web',check=True)
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),args=['--no-sandbox'])
 page=browser.new_page(viewport={'width':1440,'height':1100});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(args.url+'#calc-viewer');page.locator('#xlsx-viewer input[type=file]').set_input_files(str(fixture))
 input=page.locator('#viewer-edit-value')
 page.locator('#viewer-table td[data-address="A1"]').dblclick();assert input.input_value()=='2'
 input.fill('4,5');input.press('Enter');page.locator('#viewer-table td[data-address="B1"]').get_by_text('13.50',exact=True).wait_for()
 assert page.locator('#viewer-table td[data-address="A1"]').evaluate('(e)=>getComputedStyle(e).backgroundColor')=='rgb(255, 242, 204)'
 for cell in ['C1','D1','E1','F1']:assert page.locator(f'#viewer-table td[data-address="{cell}"]').inner_text()=='Не пересчитано'
 page.locator('#viewer-table td[data-address="B1"]').click();assert input.is_disabled()
 assert page.locator('#viewer-cache-status').inner_text()=='Пересчитано'
 page.locator('#viewer-sheet').select_option(label='Итог');assert page.locator('#viewer-table td[data-address="A1"]').inner_text()=='14.5'
 page.locator('#viewer-sheet').select_option(label='Ввод')
 page.locator('#viewer-table td[data-address="A1"]').click();input.fill('not a number');input.press('Enter')
 page.locator('#viewer-edit-status').get_by_text('Введите число',exact=False).wait_for()
 assert page.locator('#viewer-table td[data-address="B1"]').inner_text()=='13.50'
 input.fill('12');input.press('Escape');assert input.input_value()=='4.5'
 page.get_by_role('button',name='Отменить изменение',exact=True).click()
 assert page.locator('#viewer-table td[data-address="B1"]').inner_text()=='6.00'
 assert page.locator('#viewer-table td[data-address="C1"]').inner_text()=='99'
 page.locator('#viewer-table td[data-address="B3"]').click();input.fill('25');input.press('Enter')
 page.locator('#viewer-table td[data-address="B3"]').get_by_text('25',exact=True).wait_for()
 page.locator('#viewer-table td[data-address="A1"]').click();input.fill('8');input.press('Enter')
 page.locator('#viewer-table td[data-address="B1"]').get_by_text('24.00',exact=True).wait_for()
 with page.expect_download() as download:page.get_by_role('button',name='Скачать XLSX',exact=True).click()
 edited=root/'web/test-results/viewer-edited-download.xlsx';download.value.save_as(str(edited))
 subprocess.run(['node','--input-type=module','-e',"import ExcelJS from 'exceljs';import fs from 'node:fs';const b=new ExcelJS.Workbook();await b.xlsx.load(fs.readFileSync('test-results/viewer-edited-download.xlsx'));if(b.worksheets[0].getCell('A1').value!==8||b.worksheets[0].getCell('B3').value!==25||b.worksheets[0].getCell('B1').formula!=='A1*3'||b.worksheets[0].getCell('B1').result!=null)throw Error('Export invalid')"],cwd=root/'web',check=True)
 page.screenshot(path=str(root/'web/test-results/xlsx-interactive.png'),full_page=True)
 for width in [900,640,390]:
  page.set_viewport_size({'width':width,'height':1100});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
 assert not errors,errors
 browser.close()
print('Interactive viewer browser passed: editing, recalculation, unsupported propagation, cross-sheet references, invalid input, undo, blank cells, original styles, XLSX download, responsive layout')
