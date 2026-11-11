"""Real browser regression for user-created calculations (no server uploads)."""
import argparse, os, tempfile
from pathlib import Path
from openpyxl import Workbook
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument('--url',required=True);args=parser.parse_args()
with tempfile.TemporaryDirectory() as directory:
 path=Path(directory)/'Свой.xlsx';w=Workbook();s=w.active;s.title='Расчёт';s.append(['Количество',2,'=B1*Цены!A1']);w.create_sheet('Цены').append([10]);w.save(path)
 bad=Path(directory)/'bad.xlsx';s['C1']='=UNKNOWN(B1)';w.save(bad)
 with sync_playwright() as p:
  b=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),headless=True,args=['--no-sandbox']);page=b.new_page(viewport={'width':1100,'height':760});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
  page.goto(args.url);page.wait_for_selector('#calculator:not([hidden])');page.locator('#custom-file').set_input_files(str(path));page.locator('#name-dialog').wait_for(state='visible');page.locator('#calculation-name').fill('Мой расчёт');page.locator('#name-dialog button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent === 'Мой расчёт'")
  output=page.locator('[data-result=C1]');assert output.inner_text()=='20';page.locator('input[data-address=B1]').fill('-3');assert output.inner_text()=='-30';page.reload();page.wait_for_selector('[data-result=C1]');assert output.inner_text()=='-30'
  page.select_option('#sheet','Цены');page.locator('input[data-address=A1]').fill('5');page.select_option('#sheet','Расчёт');assert output.inner_text()=='-15'
  page.locator('#rename-calculation').click();page.locator('#calculation-name').fill('Новое имя');page.locator('#name-dialog button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent === 'Новое имя'")
  page.locator('#excel-file').set_input_files(str(bad));page.wait_for_function("document.querySelector('#announcement').textContent.includes('Excel не применён')");assert output.inner_text()=='-15'
  for width in [1440,900,640,390]:
   page.set_viewport_size({'width':width,'height':760});assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
  page.reload();page.wait_for_selector('[data-result=C1]');assert output.inner_text()=='-15';assert page.locator('#title').inner_text()=='Новое имя'
  old_hash=page.evaluate('location.hash')
  page.locator('#custom-file').set_input_files(str(path));page.locator('#name-dialog').wait_for(state='visible');page.locator('#name-dialog button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent === 'Свой'");assert output.inner_text()=='20'
  page.locator('#delete-calculation').click();page.locator('#confirm button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent === 'Стенка трубы'");page.evaluate('(hash)=>location.hash=hash',old_hash);page.wait_for_selector('[data-result=C1]');assert output.inner_text()=='-15'
  page.locator('#delete-calculation').click();page.locator('#confirm button[value=ok]').click();page.wait_for_function("document.querySelector('#title').textContent === 'Стенка трубы'");page.reload();page.wait_for_selector('#calculator:not([hidden])');assert page.locator('#navigation button').count()==6
  assert not errors,errors;b.close();print('Custom calculation browser regression passed: import, formulas, sheets, persistence, rename, failed replacement, responsive layout, delete')
