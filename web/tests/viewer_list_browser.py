"""Dropdowns in blank cells and compact/expanded worksheet workspace."""
import argparse,os,subprocess
from pathlib import Path
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument('--url',required=True);args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
fixture=root/'web/test-results/viewer-list.xlsx';fixture.parent.mkdir(exist_ok=True)
subprocess.run(['node','--input-type=module','-e',"import {listFixture} from './tests/viewer-list.test.mjs';import fs from 'node:fs';fs.writeFileSync('test-results/viewer-list.xlsx',await listFixture())"],cwd=root/'web',check=True)
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),args=['--no-sandbox'])
 page=browser.new_page(viewport={'width':1670,'height':900});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(args.url+'#calc-viewer');page.locator('#xlsx-viewer input[type=file]').set_input_files(str(fixture))
 page.locator('#viewer-table td[data-address="D4"]').click()
 dropdown=page.locator('#viewer-table td[data-address="D4"] select')
 assert dropdown.is_visible()
 assert dropdown.locator('option').all_text_contents()==['Выберите…','В работе','Проверено','Не применимо']
 assert page.locator('#viewer-edit-value').is_hidden()
 assert page.locator('#viewer-edit-choice').is_visible()
 dropdown.select_option(label='Проверено')
 page.wait_for_function("document.querySelector('#viewer-table td[data-address=\"D4\"] .saved-value')?.textContent==='Проверено'")
 assert page.locator('#viewer-edit-choice').input_value()=='Проверено'
 assert not page.locator('#viewer-table td[data-address="D4"] select').is_disabled()
 # Compact controls should leave at least 65% of the desktop height to the table.
 area=page.locator('#xlsx-viewer .table-scroll').bounding_box()
 assert area['y']<240,area
 assert area['height']>900*.65,area
 page.get_by_role('button',name='Отменить изменение',exact=True).click()
 assert page.locator('#viewer-table td[data-address="D4"] select').input_value()==''
 page.locator('#viewer-table td[data-address="D4"] select').focus()
 page.locator('#viewer-table td[data-address="D4"] select').press('ArrowDown')
 page.wait_for_function("document.querySelector('#viewer-table td[data-address=\"D4\"] .saved-value')?.textContent==='В работе'")
 page.get_by_role('button',name='Развернуть',exact=True).click()
 assert page.locator('.sidebar').is_hidden()
 assert page.locator('#xlsx-viewer .table-scroll').bounding_box()['width']>1600
 page.screenshot(path=str(root/'web/test-results/xlsx-dropdown-expanded.png'),full_page=True)
 page.get_by_role('button',name='Свернуть',exact=True).click();assert page.locator('.sidebar').is_visible()
 for width in [1440,900,640,390]:
  page.set_viewport_size({'width':width,'height':1000})
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),width
  assert page.locator('#viewer-table td[data-address="D4"] select').is_visible()
 page.set_viewport_size({'width':1670,'height':900})
 page.get_by_role('button',name='Скачать XLSX',exact=True).click()
 page.screenshot(path=str(root/'web/test-results/xlsx-dropdown-compact.png'),full_page=True)
 assert not errors,errors
 browser.close()
print('Dropdown browser passed: blank validated cell, named hidden-sheet list, inline selection, automatic apply, undo, editor list, compact height, expand, responsive widths')
