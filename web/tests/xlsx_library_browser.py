"""Multi-file Excel library, row colors, autosave/reopen and deletion."""
import argparse,os,subprocess
from pathlib import Path
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument('--url',required=True);args=parser.parse_args()
root=Path(__file__).resolve().parents[2];fixtures=root/'web/test-results';fixtures.mkdir(exist_ok=True)
subprocess.run(['node','--input-type=module','-e',"import {coloredFixture} from './tests/viewer-conditional.test.mjs';import fs from 'node:fs';const b=await coloredFixture();for(const n of ['Checklist A','Checklist B','Checklist C'])fs.writeFileSync('test-results/'+n+'.xlsx',b);fs.writeFileSync('test-results/Broken.xlsx','bad zip')"],cwd=root/'web',check=True)
with sync_playwright() as p:
 b=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),args=['--no-sandbox'])
 page=b.new_page(viewport={'width':1440,'height':1000});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(args.url);page.locator('#calculator:not([hidden])').wait_for()
 with page.expect_file_chooser() as chooser:page.locator('#add-calculation').click()
 assert chooser.value.is_multiple()
 chooser.value.set_files([str(fixtures/'Checklist A.xlsx'),str(fixtures/'Checklist B.xlsx')])
 page.wait_for_function("document.querySelector('#title').textContent==='Checklist A'")
 books=page.locator('#navigation button[data-id^="xlsx-"]');assert books.count()==2
 id_a=books.filter(has_text='Checklist A').get_attribute('data-id');id_b=books.filter(has_text='Checklist B').get_attribute('data-id')
 assert page.get_by_role('button',name='Открыть XLSX…',exact=True).count()==0
 # Sheet selection is beneath the scrollable table, with the toolbar above it.
 assert page.locator('#viewer-sheet').bounding_box()['y']>=page.locator('#xlsx-viewer .table-scroll').bounding_box()['y']+page.locator('#xlsx-viewer .table-scroll').bounding_box()['height']
 def choose(address,text):
  page.locator(f'#viewer-table td[data-address="{address}"]').click()
  page.locator(f'#viewer-table td[data-address="{address}"] select').select_option(label=text)
  page.wait_for_function("([a,t])=>document.querySelector('#viewer-table td[data-address=\"'+a+'\"] .saved-value')?.textContent===t",arg=[address,text])
 def color(address):return page.locator(f'#viewer-table td[data-address="{address}"]').evaluate('(e)=>getComputedStyle(e).backgroundColor')
 choose('D3','Проверено');assert color('A3')=='rgb(198, 239, 206)';assert color('E3')=='rgb(198, 239, 206)'
 choose('D4','Не проверено');assert color('B4')=='rgb(255, 199, 206)'
 page.locator('#viewer-edit-status').get_by_text('Сохранено в этом браузере',exact=False).wait_for()
 page.locator('#viewer-sheet').select_option(label='Другой лист')
 page.locator('#viewer-edit-status').get_by_text('Сохранено в этом браузере',exact=False).wait_for()
 page.reload();page.locator('#viewer-sheet').wait_for();assert page.locator('#title').inner_text()=='Checklist A'
 assert page.locator('#viewer-sheet option:checked').inner_text()=='Другой лист'
 page.locator('#viewer-sheet').select_option(label='Модель');assert color('B3')=='rgb(198, 239, 206)';assert color('E4')=='rgb(255, 199, 206)'
 page.locator(f'#navigation button[data-id="{id_b}"]').click();page.wait_for_function("id=>document.querySelector('#xlsx-viewer').dataset.book===id",arg=id_b)
 assert color('B3')=='rgb(255, 255, 255)'
 choose('D3','Не проверено');page.locator('#viewer-edit-status').get_by_text('Сохранено в этом браузере',exact=False).wait_for()
 page.locator(f'#navigation button[data-id="{id_a}"]').click();page.wait_for_function("id=>document.querySelector('#xlsx-viewer').dataset.book===id",arg=id_a)
 assert color('B3')=='rgb(198, 239, 206)'
 page.screenshot(path=str(fixtures/'xlsx-library-conditional.png'),full_page=True)
 page.get_by_role('button',name='Удалить таблицу Checklist A',exact=True).click();page.locator('#confirm button[value=ok]').click()
 page.wait_for_function("id=>document.querySelector('#xlsx-viewer').dataset.book===id",arg=id_b);assert books.count()==1
 page.reload();page.locator('#viewer-table').wait_for();assert books.count()==1;assert color('B3')=='rgb(255, 199, 206)'
 # One malformed file must not roll back the valid file or other stored books.
 page.locator('#xlsx-viewer input[type=file]').set_input_files([str(fixtures/'Checklist C.xlsx'),str(fixtures/'Broken.xlsx')])
 page.wait_for_function("document.querySelector('#title').textContent==='Checklist C'")
 page.locator('#announcement').get_by_text('Broken.xlsx',exact=False).wait_for();assert books.count()==2
 page.locator(f'#navigation button[data-id="{id_b}"]').click();page.wait_for_function("id=>document.querySelector('#xlsx-viewer').dataset.book===id",arg=id_b)
 page.locator('#viewer-delete-book').click();page.locator('#confirm button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent==='Checklist C'")
 assert books.count()==1
 for width in [1100,900,640,390]:
  page.set_viewport_size({'width':width,'height':1000});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
 choose('D3','Проверено')
 # Delete while the last edit can still be waiting for its autosave timer.
 page.locator('#viewer-delete-book').click();page.locator('#confirm button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent==='Стенка трубы'")
 page.reload();page.locator('#calculator:not([hidden])').wait_for();assert books.count()==0
 assert not errors,errors;b.close()
print('Excel library browser passed: multi-file add, independent books, original row colors, autosave, reload, bottom sheet picker, partial import, sidebar/header deletion, persistence and responsive layout')
