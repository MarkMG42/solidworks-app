"""Verify the workbook's own appearance, independent of the navy app theme."""
import argparse,os,subprocess
from pathlib import Path
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument('--url',required=True);args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
fixture=root/'web/test-results/viewer-styled.xlsx';fixture.parent.mkdir(exist_ok=True)
subprocess.run(['node','--input-type=module','-e',"import {styledFixture} from './tests/viewer-style.test.mjs';import fs from 'node:fs';fs.writeFileSync('test-results/viewer-styled.xlsx',await styledFixture())"],cwd=root/'web',check=True)
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),args=['--no-sandbox'])
 page=browser.new_page(viewport={'width':1440,'height':1000});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(args.url+'#calc-viewer');page.locator('#xlsx-viewer input[type=file]').set_input_files(str(fixture))
 title=page.locator('#viewer-table td[data-address="A1"]');title.wait_for()
 assert title.get_attribute('colspan')=='6';assert title.get_attribute('rowspan')=='2'
 assert title.evaluate('(e)=>getComputedStyle(e).backgroundColor')=='rgb(31, 78, 120)'
 assert title.evaluate('(e)=>getComputedStyle(e).color')=='rgb(255, 255, 255)'
 assert title.evaluate('(e)=>getComputedStyle(e).fontSize')=='24px'
 assert title.evaluate('(e)=>getComputedStyle(e).fontWeight')=='700'
 assert abs(page.locator('#viewer-table col').nth(2).evaluate('(e)=>e.getBoundingClientRect().width')-208)<=1
 assert page.locator('#viewer-table tbody tr').first.evaluate('(e)=>e.getBoundingClientRect().height')>=31
 assert page.locator('#viewer-table td[data-address="E5"]').inner_text()=='12,345.67 ₽'
 assert page.locator('#viewer-table td[data-address="F5"]').inner_text()=='14.11.2026'
 assert page.locator('#viewer-table td[data-address="C8"]').inner_text()=='12.5%'
 assert page.locator('#viewer-table td[data-address="D8"]').inner_text()=='0.00'
 assert page.locator('#viewer-table td[data-address="B11"]').is_hidden()
 assert page.locator('#viewer-table thead th').nth(7).is_hidden()
 assert page.locator('#viewer-table td[data-address="H12"]').evaluate('(e)=>getComputedStyle(e).borderTopStyle')=='double'
 assert page.locator('#viewer-table td[data-address="B10"] span span').first.evaluate('(e)=>getComputedStyle(e).color')=='rgb(192, 0, 0)'
 page.locator('#viewer-table td[data-address="E5"]').click()
 assert page.locator('#viewer-address').inner_text()=='E5'
 assert page.locator('#viewer-formula').input_value()=='=UNSUPPORTED_PRICE(0)'
 assert page.locator('#viewer-formula').get_attribute('readonly') is not None
 page.locator('#viewer-table td[data-address="B9"]').click()
 assert page.locator('#viewer-cache-status').inner_text()=='Нет сохранённого результата'
 # A merge cut by pagination retains the master and clipped spans on both pages.
 assert page.locator('#viewer-table td[data-address="A49"]').get_attribute('rowspan')=='2'
 page.get_by_role('button',name='Следующая страница',exact=True).click()
 assert page.locator('#viewer-table td[data-address="A49"]').inner_text()=='Объединение через страницу'
 assert page.locator('#viewer-table td[data-address="A49"]').get_attribute('rowspan')=='2'
 page.get_by_role('button',name='Предыдущая страница',exact=True).click()
 page.locator('#viewer-table td[data-address="A1"]').click()
 page.screenshot(path=str(root/'web/test-results/xlsx-original-appearance.png'),full_page=True)
 for width in [900,640,390]:
  page.set_viewport_size({'width':width,'height':900})
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
  assert page.locator('#xlsx-viewer .table-scroll').evaluate('(e)=>e.scrollWidth>e.clientWidth')
 page.locator('#viewer-mode').select_option('list')
 assert page.locator('#viewer-table').get_by_text('=UNSUPPORTED_PRICE(0)',exact=True).count()==1
 assert page.locator('#viewer-formula-bar').is_hidden()
 page.set_viewport_size({'width':1440,'height':1000})
 for original in sorted((root/'installer/calculations/Excel').glob('*.xlsx')):
  page.locator('#xlsx-viewer input[type=file]').set_input_files(str(original))
  page.get_by_role('status').filter(has_text=original.name).wait_for()
  page.locator('#viewer-mode').select_option('sheet')
  assert page.locator('#viewer-table tbody td').count()>0
  if 'толщины' in original.name:
   page.screenshot(path=str(root/'web/test-results/xlsx-original-pipe.png'),full_page=True)
 assert not errors,errors
 browser.close()
print('Workbook appearance browser checks passed: styles, merges across pages, dimensions, formats, hidden rows/columns, rich text, formula bar, responsive layout')
