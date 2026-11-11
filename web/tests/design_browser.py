import argparse,os
from playwright.sync_api import sync_playwright
parser=argparse.ArgumentParser();parser.add_argument("--url",required=True);args=parser.parse_args()
with sync_playwright() as p:
 b=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH'),args=['--no-sandbox']);page=b.new_page(viewport={'width':1280,'height':820});page.goto(args.url);page.wait_for_selector('.metric-card');page.evaluate('document.fonts.ready');assert page.evaluate('document.fonts.check("16px Nunito")')
 for i in range(5):
  page.locator(f'#navigation button[data-id="{i}"]').click();page.wait_for_selector('.metric-card')
  for width in [1440,1100,900,640,390]:
   page.set_viewport_size({'width':width,'height':820});assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),(i,width)
 page.locator('input[data-address=B3]').fill('3000,5');assert page.locator('[data-summary=B11]').inner_text()=='2496,5';page.locator('input[data-address=B3]').fill('');assert page.locator('[data-summary=B11]').inner_text()=='Проверьте ввод';b.close();print('Nunito loaded; five dashboards fit five widths; result cards update and block invalid input')
