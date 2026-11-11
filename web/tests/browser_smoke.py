from playwright.sync_api import sync_playwright
from pathlib import Path
import json, argparse, shutil, os
parser=argparse.ArgumentParser();parser.add_argument('--url',default='http://127.0.0.1:4173/solidworks-app/');args=parser.parse_args()
root=Path(__file__).resolve().parents[2];out=root/'web/test-results';out.mkdir(exist_ok=True)
with sync_playwright() as p:
 executable=os.environ.get('CHROMIUM_PATH') or shutil.which('chromium')
 browser=p.chromium.launch(headless=True,args=['--no-sandbox'],**({'executable_path':executable} if executable else {}))
 context=browser.new_context(viewport={'width':1100,'height':760},accept_downloads=True)
 page=context.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(args.url);page.locator('#worksheet tbody tr').first.wait_for()
 assert page.locator('#title').inner_text()=='Стенка трубы'
 page.screenshot(path=str(out/'desktop.png'),full_page=True)
 for i,name in enumerate(['Стенка трубы','Длина болта','Балка','Болты и швы','Высота узла']):
  page.locator(f'nav button[data-id="{i}"]').click();page.wait_for_function('(name)=>document.querySelector("#title").textContent===name && document.querySelector("#calculator").hidden===false',arg=name)
  page.locator('#worksheet tbody tr').first.wait_for();assert page.locator('#worksheet tbody tr').count()>0,name
  assert page.locator('#worksheet [data-address]').count()>0,name
 page.locator('input[data-address="B3"]').fill('3000,5');page.wait_for_function('document.querySelector("[data-result=B11]").textContent==="2496,5"')
 page.reload();page.wait_for_function('document.querySelector("[data-result=B11]")?.textContent==="2496,5"')
 page.select_option('#sheet','Справочник высот');page.locator('#worksheet tbody tr').first.wait_for();page.select_option('#sheet','Высота узла')
 assert page.locator('[data-result="B11"]').inner_text()=='2496,5'
 with page.expect_download() as info:page.locator('#save').click()
 saved=info.value;saved.save_as(str(out/'input.json'));assert json.load(open(out/'input.json'))['Inputs']
 with page.expect_download() as info:page.locator('#csv').click()
 info.value.save_as(str(out/'height.csv'));assert '2496,5' in (out/'height.csv').read_text(encoding='utf-8-sig')
 page.locator('#reset').click();page.locator('#confirm button[value="ok"]').click();page.wait_for_function('document.querySelector("[data-result=B11]").textContent==="1646,0"')
 page.locator('#session-file').set_input_files(str(out/'input.json'));page.wait_for_function('document.querySelector("[data-result=B11]").textContent==="2496,5"')
 # Upload actual XLSX with a modified formula and a changed input.
 import openpyxl
 wb=openpyxl.load_workbook(root/'installer/calculations/Excel/Расчет высоты узла.xlsx');wb['Высота узла']['B11']='=B3-B10-100';wb.save(out/'height-changed.xlsx')
 page.locator('#excel-file').set_input_files(str(out/'height-changed.xlsx'));page.locator('#confirm[open]').wait_for(timeout=30000);page.locator('#confirm button[value="ok"]').click()
 page.wait_for_function('document.querySelector("[data-result=B11]").textContent==="1546,0"')
 assert not page.locator('#original').is_hidden();page.reload();page.wait_for_function('document.querySelector("[data-result=B11]")?.textContent==="1546,0"')
 wb['Высота узла']['B11']='=UNSUPPORTED(B3)';wb.save(out/'height-bad.xlsx');page.locator('#excel-file').set_input_files(str(out/'height-bad.xlsx'))
 page.wait_for_function('document.querySelector("#announcement").textContent.includes("не поддерживается")')
 assert page.locator('[data-result="B11"]').inner_text()=='1546,0'
 page.locator('#original').click();page.locator('#confirm button[value="ok"]').click();page.wait_for_function('document.querySelector("[data-result=B11]").textContent==="2496,5"')
 page.locator('input[data-address="B3"]').fill('');assert not page.locator('#validation').is_hidden();assert page.locator('[data-result="B11"]').inner_text()=='Проверьте ввод'
 page.locator('input[data-address="B3"]').fill('2150');assert page.locator('[data-result="B11"]').inner_text()=='1646,0'
 for width in [1440,1100,900,800,640,480,390]:
  page.set_viewport_size({'width':width,'height':800});page.wait_for_timeout(60)
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),width
  assert page.locator('#sheet').is_visible()
  page.screenshot(path=str(out/f'width-{width}.png'),full_page=True)
 page.set_viewport_size({'width':1100,'height':760});page.locator('nav button[data-id="2"]').click();page.locator('#sheet').select_option('Детали расчета')
 assert not page.locator('#pagination').is_hidden();page.locator('#next').click();assert '51–100' in page.locator('#page-info').inner_text()
 page.locator('nav button[data-id="macros"]').click();assert not page.locator('#macros').is_hidden()
 page.go_back();page.wait_for_function('document.querySelector("#title").textContent==="Балка" && !document.querySelector("#calculator").hidden')
 assert not errors,errors
 print('Browser checks passed: five first loads, editing, reload persistence, sheets, JSON/CSV, XLSX formula replacement, unsupported formula rollback, stored model reload, restoring built-in model, invalid input, reference pagination, seven widths, macro explanation. No browser exceptions.')
 browser.close()
