"""Generate 256 mutated copies of the supplied spreadsheets and collect independent results.
Usage: python installer/calculations/verify_spreadsheets.py /tmp/oracle --recalculate
Requires openpyxl and LibreOffice (soffice). Originals are never rewritten.
The checked-in SpreadsheetScenarios.json lets normal tests run without LibreOffice.
"""
from pathlib import Path
import openpyxl,json,warnings,sys,subprocess,concurrent.futures,os
warnings.filterwarnings('ignore',category=UserWarning)
repo=Path(__file__).resolve().parents[2]
root=Path(sys.argv[1]).resolve();root.mkdir(parents=True,exist_ok=True);(root/'in').mkdir(exist_ok=True);(root/'out').mkdir(exist_ok=True)
books=json.load(open(repo/'addin/portable/Calculations/Workbooks.json'))
sources={p.name:p for p in (repo/'installer/calculations/Excel').glob('*.xlsx')}
cases=[]
def add(b,label,inputs):cases.append({'Book':b,'Label':label,'Inputs': [{'Sheet':s,'Address':a,'Value':v} for s,a,v in inputs]})
for b in range(5):add(b,'base',[])
# Every profile, orientation and support; noncentral load plus distributed load.
for p in books[2]['Sheets'][0]['Cells']:
 if p['Address']=='B2':profiles=p['Options']
for profile in profiles:
 for orientation in ['Вертикально — Ix, Wx','Горизонтально — Iy, Wy']:
  for support in ['Шарниры на обоих концах','Консоль: заделка слева','Заделка с двух сторон']:
   add(2,profile+' '+orientation+' '+support,[('Расчёт','B2',profile),('Расчёт','B4',orientation),('Расчёт','B5',support),('Расчёт','B8',237),('Расчёт','B9',125)])
for a in [0,800]:add(2,'load endpoint '+str(a),[('Расчёт','B8',a)])
for temp in [20,20.1,50,50.1,100,100.1]:
 for material in ['AISI 304','Сталь 20','09Г2С']:
  add(0,f'{material} {temp}',[('Расчет по давлению','B2',material),('Расчет по давлению','B5',temp),('Местное смятие','B2',material),('Местное смятие','B3',temp),('Местное смятие','B4',1234)])
for dn in [15,20,25,32,40,50,65,80,100,125,150,200,250,300,350,400]:
 for pn in [16,25]:
  for flange in ['Плоский','Воротниковый']:
   for fastener in ['Болт','Шпилька']:
    add(1,f'{dn} {pn} {flange} {fastener}',[('Длина болта','B2',dn),('Длина болта','B3',pn),('Длина болта','B4',flange),('Длина болта','B5',fastener),('Длина болта','B6',68.5)])
for dn in [15,20,25,32,40,50,65,80,100,125,150,200,250,300,400]:add(4,'DN '+str(dn),[('Высота узла','B2',dn)])
for cls in [5.6,5.8,8.8,10.9,12.9]:
 for accuracy in ['А','В']:
  add(3,f'bolts {cls} {accuracy}',[('Болты','C10',cls),('Болты','C11',accuracy),('Болты','C6',0.8),('Болты','C5',2.5),('Угловые швы','C4',1.2),('Угловые швы','C5',0.2),('Угловые швы','C14',15),('Стыковые швы','C4',0.1)])
for electrode in ['Э42, Э42А','Э46, Э46А','Э50, Э50А','Э60','Э70','Э85','Св-08, Св-08А','Св-08ГА','Св-08Г2С, Св-10ГА, ПП-АН-8, ПП-АН-3','Св-10НМА, Св-10Г2','Св-10ХГ2СМА, Св-08ХН2ГМЮ']:
 add(3,electrode,[('Угловые швы','C10',electrode)])
for material in ['С235','С245','С255','С275','С285','С345','С345К','С375','С390','С390К','С440','С590','С590К']:
 add(3,material,[('Болты','C12',material),('Угловые швы','C9',material),('Стыковые швы','C8',material)])
for i,case in enumerate(cases):
 w=openpyxl.load_workbook(sources[books[case['Book']]['Source']])
 for item in case['Inputs']:w[item['Sheet']][item['Address']]=item['Value']
 w.save(root/'in'/f'case{i:03}.xlsx')
(root/'cases.json').write_text(json.dumps(cases,ensure_ascii=False))
print('Generated',len(cases),'independent spreadsheet scenarios')

if '--recalculate' in sys.argv:
 def recalculate(batch):
  name=batch[0].stem
  process=subprocess.run(['soffice','-env:UserInstallation='+ (root/'profiles'/name).as_uri(),'--headless','--convert-to','xlsx','--outdir',str(root/'out')]+[str(p) for p in batch],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=240)
  (root/(name+'.log')).write_bytes(process.stdout)
  if process.returncode:raise RuntimeError(name+' conversion failed')
 files=sorted((root/'in').glob('*.xlsx'))
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
  list(pool.map(recalculate,[files[i:i+4] for i in range(0,len(files),4)]))
collected=[]
for i,case in enumerate(cases):
 p=root/'out'/f'case{i:03}.xlsx'
 if not p.exists():raise RuntimeError('Missing recalculated workbook: '+str(p)+'; use --recalculate')
 w=openpyxl.load_workbook(p,data_only=True)
 case['Results']=[{'Sheet':s['Name'],'Address':c['Address'],'Value':w[s['Name']][c['Address']].value} for s in books[case['Book']]['Sheets'] if not s['ReferenceOnly'] for c in s['Cells'] if c['Formula']]
 collected.append(case)
fixture=repo/'addin/tests/calculations/SpreadsheetScenarios.json'
fixture.write_text(json.dumps(collected,ensure_ascii=False,separators=(',',':')))
print('Collected',len(collected),'scenarios;',sum(len(c['Results']) for c in collected),'results')
