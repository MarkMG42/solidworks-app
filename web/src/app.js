import './style.css';
import { Engine, address, CalculationError } from './engine.js';
const $ = id => document.getElementById(id);
const labels = ['Стенка трубы','Длина болта','Балка','Болты и швы','Высота узла'];
const engines = new Map(), defaults = new Map(), sheets = new Map(), drafts = new Map();
let activeId = 0, activeSheet = null, page = 0, requestId = 0, catalog = [];
const customBooks = new Map();
const isCustom = id => typeof id === 'string' && id.startsWith('custom-');
const PAGE_SIZE = 50, prefix = 'sw-assistant:web:';
const version = import.meta.env.VITE_APP_VERSION || 'dev';
const dataURL = file => new URL(`data/${file}?v=${encodeURIComponent(version)}`, new URL(import.meta.env.BASE_URL, location.href));
function element(tag, className, text) { const e = document.createElement(tag); if (className) e.className = className; if (text != null) e.textContent = text; return e; }
function announce(message, error = false) { $('announcement').textContent = message; $('announcement').classList.toggle('error',error); $('announcement').hidden = !message; }
const sessionKey = e => `${prefix}input:${e.book.Id || e.book.Source}:${e.book.Sha256}`;
function saveLocal(e) {
  try { localStorage.setItem(sessionKey(e),JSON.stringify(e.saveSession())); $('storage-status').textContent = 'Ввод сохранён в этом браузере'; }
  catch { $('storage-status').textContent = 'Автосохранение недоступно. Используйте «Сохранить ввод».'; }
}
function restoreLocal(e) {
  try { const data = localStorage.getItem(sessionKey(e)); if (data) e.loadSession(JSON.parse(data)); }
  catch { announce('Сохранённый ввод не удалось прочитать. Можно открыть ранее сохранённый JSON.',true); }
}
let database;
async function modelStore() {
  if (database) return database;
  database = await new Promise((resolve,reject) => {
    const r = indexedDB.open('sw-assistant-calculations',1);
    r.onupgradeneeded = () => r.result.createObjectStore('models');
    r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
  }); return database;
}
async function storedModel(source, value, remove = false) {
  const db = await modelStore();
  return new Promise((resolve,reject) => {
    const tx = db.transaction('models',value === undefined && !remove ? 'readonly' : 'readwrite'), store = tx.objectStore('models');
    const r = remove ? store.delete(source) : value === undefined ? store.get(source) : store.put(value,source);
    let result; r.onsuccess = () => { result = r.result; };
    tx.oncomplete = () => resolve(result); tx.onerror = () => reject(tx.error); tx.onabort = () => reject(tx.error);
  });
}
async function defaultBook(id) {
  if (!defaults.has(id)) { const r = await fetch(dataURL(`${id}.json`)); if (!r.ok) throw new Error('Не удалось загрузить расчёт. Проверьте интернет и обновите страницу.'); defaults.set(id,await r.json()); }
  return defaults.get(id);
}
async function getEngine(id) {
  if (!engines.has(id)) {
    let book = isCustom(id) ? customBooks.get(id) : await defaultBook(id);
    if (!book) throw new Error('Пользовательский расчёт не найден');
    try { const saved = isCustom(id) ? null : await storedModel(book.Source); if (saved) book = saved; } catch { /* Calculations remain available without browser storage. */ }
    const e = new Engine(book); restoreLocal(e); engines.set(id,e);
  } return engines.get(id);
}
function selectedEngine() { return engines.get(activeId); }
function rememberSheet() { if (activeSheet && activeId !== 'macros') sheets.set(activeId,activeSheet.Name); }
async function navigate(id) {
  rememberSheet(); announce(''); const token = ++requestId; activeId = id; $('loading').hidden=id==='macros';
  location.hash = id === 'macros' ? 'macros' : `calc-${id}`;
  for (const button of $('navigation').querySelectorAll('button')) { const selected = button.dataset.id === String(id); button.classList.toggle('active',selected); if (selected) button.setAttribute('aria-current','page'); else button.removeAttribute('aria-current'); }
  $('macros').hidden = id !== 'macros'; $('calculator').hidden = true; $('main').setAttribute('aria-busy',id !== 'macros');
  if (id === 'macros') { activeSheet = null; $('title').textContent = 'Макросы'; return; }
  $('title').textContent = labels[id];
  $('custom-actions').hidden = !isCustom(id);
  try {
    const engine = await getEngine(id); if (token !== requestId) return;
    const select = $('sheet'); select.replaceChildren();
    for (const s of engine.book.Sheets) { const option = element('option',null,s.Name + (s.ReferenceOnly ? ' · справочник' : '')); option.value = s.Name; select.append(option); }
    select.value = sheets.get(id) || engine.book.Sheets[0].Name; if (!select.value) select.selectedIndex = 0;
    activeSheet = engine.book.Sheets.find(s => s.Name === select.value); page = 0;
    $('calculator').hidden = false; $('loading').hidden=true; $('main').setAttribute('aria-busy','false');
    $('original').hidden = isCustom(id) || engine.book.Sha256 === catalog[id]?.sha256;
    renderSheet();
  } catch (error) { if (token === requestId) { $('loading').hidden=true; announce(error.message,true); $('main').setAttribute('aria-busy','false'); } }
}
function valueText(v, c) {
  if (v == null) return ''; if (typeof v === 'boolean') return v ? 'Да' : 'Нет'; if (typeof v === 'string') return v;
  if (!c.Editable && /^0(?:\.0+)?$/.test(c.Format)) { const places = c.Format.includes('.') ? c.Format.split('.')[1].length : 0; return v.toFixed(places).replace('.',','); }
  return new Intl.NumberFormat('ru-RU',{useGrouping:false,maximumSignificantDigits:15}).format(v);
}
function weights(cols, id) { if (cols === 2) return [63,37]; if (cols === 3) return id === 3 ? [50,24,26] : [55,33,12]; if (cols === 4) return [42,22,9,27]; return Array.from({length:cols},(_,i)=>i===0?1.6:1).map(v=>v*100/(cols+.6)); }
const draftKey = (id,s,a) => `${id}:${s}:${a}`;
function renderSheet() {
  const engine = selectedEngine(), sheet = activeSheet; if (!engine || !sheet) return;
  $('sheet-kind').textContent = sheet.ReferenceOnly ? 'Только чтение' : 'Исходные данные и результаты';
  const table = $('worksheet'); table.replaceChildren();
  const columns = Math.max(1,...sheet.Cells.map(c=>c.Column)), rows = Math.max(1,...sheet.Cells.map(c=>c.Row));
  const wide = columns > 5; table.style.minWidth = wide ? `${columns*100+36}px` : '';
  const colgroup = element('colgroup'); const lineCol = element('col'); lineCol.style.width = '36px'; colgroup.append(lineCol);
  for (const weight of weights(columns,activeId)) { const col = element('col'); col.style.width = `${weight}%`; colgroup.append(col); }
  table.append(colgroup); const thead = element('thead'), tr = element('tr'); tr.append(element('th','row-number',''));
  for (let c=1;c<=columns;c++) tr.append(element('th',null,address(1,c).slice(0,-1))); thead.append(tr); table.append(thead);
  const body = element('tbody'), byAddress = new Map(sheet.Cells.map(c=>[c.Address,c]));
  const paginated = sheet.ReferenceOnly || engine.book.Custom;
  const first = paginated ? page*PAGE_SIZE+1 : 1, last = paginated ? Math.min(rows,first+PAGE_SIZE-1) : rows;
  const capturedId = activeId;
  for (let row=first;row<=last;row++) {
    const tr = element('tr'); const rowHead = element('th','row-number',row); rowHead.scope='row'; tr.append(rowHead);
    const rowCells = sheet.Cells.filter(c=>c.Row===row), section = rowCells.length===1 && !rowCells[0].Expression && !rowCells[0].Editable;
    for (let column=1;column<=columns;column++) {
      const c = byAddress.get(address(row,column)), td = element('td',c?.Editable?'editable':section?'section':'');
      if (c) {
        td.dataset.address = c.Address;
        if (c.Editable) {
          const input = element(c.Options?.length?'select':'input','cell-input');
          input.setAttribute('aria-label',`${byAddress.get(address(row,1))?.Value || 'Значение'} (${c.Address})`);
          input.dataset.address = c.Address;
          if (c.Options?.length) for (const optionValue of c.Options) { const option=element('option',null,optionValue); option.value=optionValue; input.append(option); }
          else { input.type='text'; input.inputMode=typeof c.Value==='number'?'decimal':'text'; input.autocomplete='off'; }
          const dk=draftKey(capturedId,sheet.Name,c.Address), draft=drafts.get(dk);
          input.value = draft?.text ?? (c.Options?.length?typeof c.Value==='boolean'?String(engine.input(sheet.Name,c)).toUpperCase():String(engine.input(sheet.Name,c)):valueText(engine.input(sheet.Name,c),{...c,Editable:true}));
          if (draft?.error) { input.setAttribute('aria-invalid','true'); input.title=draft.error; }
          input.addEventListener(c.Options?.length?'change':'input',()=>{
            try {
              engine.setInput(sheet.Name,c.Address,input.value); drafts.delete(dk); input.removeAttribute('aria-invalid'); input.title=''; saveLocal(engine);
            } catch(error) { drafts.set(dk,{text:input.value,error:error.message}); input.setAttribute('aria-invalid','true'); input.title=error.message; }
            updateResults();
          });
          input.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();input.blur();}});
          td.append(input);
        } else if (c.Expression) { const output=element('span','result'); output.title=c.Formula; output.dataset.result=c.Address; td.append(output); }
        else td.textContent=valueText(c.Value,c);
      } tr.append(td);
    }
    const inputCell=[...tr.cells].find(td=>td.querySelector('.cell-input'));
    if(inputCell){for(const td of [...tr.cells].slice(1)){if(td!==inputCell && td!==tr.cells[1] && !(td.cellIndex===inputCell.cellIndex+1 && td.textContent.trim().length>0 && td.textContent.trim().length<12)){td.classList.add('extra-cell');if(td.textContent.trim())inputCell.title=td.textContent.trim();}}}
    if(rowCells.some(c=>c.Expression) && !rowCells.some(c=>c.Editable))tr.classList.add('output-row');
    if(section || !rowCells.length)tr.classList.add('section-row');
    body.append(tr);
  }
  table.append(body);
  const dashboard=!engine.book.Custom && !sheet.ReferenceOnly && columns<=4;
  $('workspace').classList.toggle('dashboard',dashboard);
  const cards=$('result-cards');cards.replaceChildren();cards.hidden=!dashboard;
  if(dashboard){
    colgroup.replaceChildren();for(const width of columns===2?[60,40]:[55,32,13]){const col=element('col');col.style.width=width+'%';colgroup.append(col);}
    const sectionCell=table.querySelector('.section-row:first-child td');if(sectionCell)sectionCell.colSpan=Math.min(columns,3);
    table.setAttribute('aria-label','Исходные данные и полная таблица расчёта');
    const heading=element('h2',null,'Результаты расчёта');cards.append(heading);
    for(const c of sheet.Cells.filter(c=>c.Expression)){
      const card=element('div','metric-card');
      const label=sheet.Cells.filter(v=>v.Row===c.Row && v.Column<c.Column && !v.Expression).map(v=>v.Value).filter(v=>v!=null).join(' · ');
      card.append(element('div','metric-label',label || c.Address));
      const output=element('span','result');output.dataset.summary=c.Address;card.append(output);
      const unit=sheet.Cells.find(v=>v.Row===c.Row && v.Column===c.Column+1 && !v.Expression && !v.Editable);
      if(unit?.Value)card.append(element('small','metric-unit',unit.Value));
      cards.append(card);
    }
  }

  $('source').textContent = `Источник: ${engine.book.Source}` + (engine.book.Sha256 !== catalog[activeId]?.sha256 ? ' · ваш Excel' : '');
  $('pagination').hidden = !paginated || rows<=PAGE_SIZE;
  $('page-info').textContent=`Строки ${first}–${last} из ${rows}`; $('previous').disabled=page===0; $('next').disabled=last>=rows;
  updateResults(); $('table-scroll').scrollTo(0,0);
}
function workbookIssue(engine){
 if([...drafts.keys()].some(k=>k.startsWith(`${activeId}:`)))return 'Исправьте отмеченные поля на листах расчёта. Результаты не рассчитаны.';
 try{for(const sheet of engine.book.Sheets){const error=engine.validate(sheet.Name);if(error)return `${sheet.Name}: ${error}`;}}catch(error){return error.message;}
 return null;
}
function updateResults() {
  const engine=selectedEngine(),s=activeSheet;if(!engine||!s)return;
  const invalid=workbookIssue(engine);
  const cellMap=new Map(s.Cells.map(c=>[c.Address,c])); let errorMessage='';
  for (const output of $('calculator').querySelectorAll('[data-result],[data-summary]')) {
    const c=cellMap.get(output.dataset.result || output.dataset.summary); output.className='result';if(output.dataset.summary)output.parentElement.classList.remove('status-card','failed-card');
    try {
      if(invalid){output.textContent='Проверьте ввод';output.classList.add('error');continue;}
      const value=engine.get(s.Name,c.Address);output.textContent=valueText(value,c);output.title=c.Formula;
      if(['ПРОХОДИТ','Обеспечено'].includes(value)){output.classList.add('pass');if(output.dataset.summary)output.parentElement.classList.add('status-card');}
      if(['НЕ ПРОХОДИТ','Не обеспечено','Ошибка исходных данных'].includes(value)){output.classList.add('fail');if(output.dataset.summary)output.parentElement.classList.add('status-card','failed-card');}
    }catch(error){output.textContent='Ошибка расчёта';output.title=error.message;output.classList.add('error');errorMessage=`${c.Address}: ${error.message}`;}
  }
  $('validation').hidden=!(invalid||errorMessage);$('validation').textContent=invalid||errorMessage;
}
function download(fileName,content,type) {
  const url=URL.createObjectURL(new Blob([content],{type})),a=element('a');a.href=url;a.download=fileName;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function confirm(title,text) {
  $('confirm-title').textContent=title;$('confirm-text').textContent=text;const dialog=$('confirm');dialog.returnValue='';
  return new Promise(resolve=>{dialog.addEventListener('close',()=>resolve(dialog.returnValue==='ok'),{once:true});dialog.showModal();});
}
$('sheet').addEventListener('change',()=>{activeSheet=selectedEngine().book.Sheets.find(s=>s.Name===$('sheet').value);sheets.set(activeId,activeSheet.Name);page=0;renderSheet();});
$('previous').addEventListener('click',()=>{page--;renderSheet();});$('next').addEventListener('click',()=>{page++;renderSheet();});
$('refresh').addEventListener('click',()=>location.reload());
$('save').addEventListener('click',()=>{if([...drafts.keys()].some(k=>k.startsWith(`${activeId}:`))){announce('Исправьте отмеченные поля перед сохранением ввода.',true);return;}const e=selectedEngine();download(`${labels[activeId]} — ввод.json`,JSON.stringify(e.saveSession(),null,2),'application/json;charset=utf-8');});
$('open').addEventListener('click',()=>$('session-file').click());
$('session-file').addEventListener('change',async event=>{
  const file=event.target.files[0];event.target.value='';if(!file)return;
  try{if(file.size>1024*1024)throw new Error('Файл ввода слишком большой');const e=selectedEngine();e.loadSession(JSON.parse(await file.text()));for(const k of drafts.keys())if(k.startsWith(`${activeId}:`))drafts.delete(k);saveLocal(e);renderSheet();announce('Исходные данные открыты.');}catch(error){announce(error.message,true);}
});
$('reset').addEventListener('click',async()=>{if(!await confirm('Сбросить ввод?','Вернуть исходные значения всех листов этого расчёта? Формулы останутся текущими.'))return;const e=selectedEngine();e.reset();for(const k of drafts.keys())if(k.startsWith(`${activeId}:`))drafts.delete(k);saveLocal(e);renderSheet();announce('Исходные значения восстановлены.');});
$('csv').addEventListener('click',()=>{
  const e=selectedEngine(),s=activeSheet;const rows=Math.max(1,...s.Cells.map(c=>c.Row)),cols=Math.max(1,...s.Cells.map(c=>c.Column)),cells=new Map(s.Cells.map(c=>[c.Address,c]));
  const quote=(v)=>'"'+String(v??'').replaceAll('"','""')+'"';let content=`Источник;${quote(e.book.Source)}\r\nЛист;${quote(s.Name)}\r\n`;
  const invalid=workbookIssue(e);if(invalid){announce('Экспорт отменён: '+invalid,true);return;}
  for(let r=1;r<=rows;r++){
    const fields=[];for(let col=1;col<=cols;col++){
      const c=cells.get(address(r,col));let raw='',value='';
      if(c){try{raw=c.Editable?e.input(s.Name,c):c.Expression?e.get(s.Name,c.Address):c.Value;value=c.Expression&&invalid?'Проверьте ввод':valueText(raw,c);}catch(error){value=`Ошибка: ${error.message}`;}}
      if(typeof raw==='string'&&/^[=+@-]/.test(raw))value="'"+value;fields.push(quote(value));
    }content+=fields.join(';')+'\r\n';
  }download(`${s.Name}.csv`,'\ufeff'+content,'text/csv;charset=utf-8');
});
$('excel').addEventListener('click',()=>$('excel-file').click());
$('excel-file').addEventListener('change',async event=>{
  const file=event.target.files[0];event.target.value='';if(!file)return;
  const id=activeId,original=isCustom(id)?customBooks.get(id):await defaultBook(id);$('excel').disabled=true;for(const b of $('navigation').querySelectorAll('button'))b.disabled=true;announce('Читаю Excel и проверяю формулы…');
  try{
    if(!file.name.toLowerCase().endsWith('.xlsx'))throw new Error('Выберите файл .xlsx');
    if(file.size>15*1024*1024)throw new Error('Максимальный размер Excel — 15 МБ');
    const [{default:ExcelJS},{importWorkbook}]=await Promise.all([import('exceljs'),import('./import-excel.js')]);
    const bytes=await file.arrayBuffer(),digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
    const model=await importWorkbook(bytes,original,ExcelJS,digest);if(isCustom(id))model.Id=id;
    if(!await confirm('Обновить расчёт из Excel?','Будут заменены формулы, справочники и исходные значения выбранного расчёта. Перед заменой можно отменить и сохранить текущий ввод.')){announce('Обновление отменено.');return;}
    let retained=true;try{await storedModel(isCustom(id)?id:original.Source,model);if(isCustom(id))customBooks.set(id,model);}catch{retained=false;}
    const engine=new Engine(model);engines.set(id,engine);for(const k of drafts.keys())if(k.startsWith(`${id}:`))drafts.delete(k);saveLocal(engine);
    await navigate(id);announce(retained?'Формулы и справочники обновлены из Excel и сохранены в этом браузере.':'Excel загружен в текущую вкладку. Браузер не разрешил сохранить модель: после закрытия её нужно загрузить снова.',!retained);
  }catch(error){announce(`Excel не применён: ${error.message}`,true);}finally{$('excel').disabled=false;for(const b of $('navigation').querySelectorAll('button'))b.disabled=false;}
});
$('original').addEventListener('click',async()=>{
  if(!await confirm('Вернуть встроенные формулы?','Загруженная из Excel версия этого расчёта будет заменена встроенной.'))return;
  try{const b=await defaultBook(activeId);await storedModel(b.Source,undefined,true);engines.delete(activeId);for(const k of drafts.keys())if(k.startsWith(`${activeId}:`))drafts.delete(k);await navigate(activeId);announce('Встроенные формулы восстановлены.');}catch(error){announce(error.message,true);}
});
function addNavigation(id,label){
  const button=element('button');button.type='button';button.dataset.id=id;
  button.append(element('span','nav-icon',isCustom(id)?'◇':id==='macros'?'◇':String(id+1).padStart(2,'0')),element('span',null,label));button.addEventListener('click',()=>navigate(id));$('navigation').append(button);
}
for(const [id,label] of [['macros','Макросы'],...labels.map((v,i)=>[i,v])]){
  addNavigation(id,label);
}
function hashId(){const raw=location.hash.replace(/^#calc-/,'');return location.hash==='#macros'?'macros':customBooks.has(raw)?raw:/^[0-4]$/.test(raw)?Number(raw):0;}
async function askName(value){const d=$('name-dialog');$('calculation-name').value=value;d.returnValue='';return new Promise(resolve=>{d.addEventListener('close',()=>resolve(d.returnValue==='ok'?$('calculation-name').value.trim():null),{once:true});d.showModal();$('calculation-name').focus();});}
$('add-calculation').addEventListener('click',()=>$('custom-file').click());
$('custom-file').addEventListener('change',async event=>{
 const file=event.target.files[0];event.target.value='';if(!file)return;
 $('add-calculation').disabled=true;announce('Читаю Excel и проверяю все листы…');
 try{
  if(!file.name.toLowerCase().endsWith('.xlsx'))throw new Error('Выберите файл .xlsx');
  if(file.size>15*1024*1024)throw new Error('Максимальный размер Excel — 15 МБ');
  const [{default:ExcelJS},{importCustomWorkbook}]=await Promise.all([import('exceljs'),import('./import-excel.js')]);
  const bytes=await file.arrayBuffer(),digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
  const model=await importCustomWorkbook(bytes,file.name.replace(/\.xlsx$/i,''),file.name,ExcelJS,digest);
  const title=await askName(model.Title);if(!title){announce('Добавление отменено.');return;}
  const id='custom-'+crypto.randomUUID();model.Title=title;model.Id=id;
  await storedModel(id,model);customBooks.set(id,model);labels[id]=title;addNavigation(id,title);await navigate(id);announce('Расчёт добавлен и сохранён в этом браузере.');
 }catch(error){announce('Расчёт не добавлен: '+error.message,true);}finally{$('add-calculation').disabled=false;}
});
$('rename-calculation').addEventListener('click',async()=>{const id=activeId,book=customBooks.get(id);if(!book)return;const title=await askName(book.Title);if(!title)return;try{const model={...book,Title:title};await storedModel(id,model);customBooks.set(id,model);selectedEngine().book.Title=title;labels[id]=title;$('navigation').querySelector(`[data-id="${id}"] span:last-child`).textContent=title;$('title').textContent=title;}catch(error){announce(error.message,true);}});
$('delete-calculation').addEventListener('click',async()=>{const id=activeId;if(!isCustom(id)||!await confirm('Удалить расчёт?','Удалится пользовательский расчёт из этого браузера. Исходный файл Excel останется у вас.'))return;try{await storedModel(id,undefined,true);const e=engines.get(id);if(e){try{localStorage.removeItem(sessionKey(e));}catch{}}engines.delete(id);customBooks.delete(id);sheets.delete(id);for(const k of drafts.keys())if(k.startsWith(`${id}:`))drafts.delete(k);$('navigation').querySelector(`[data-id="${id}"]`).remove();await navigate(0);}catch(error){announce(error.message,true);}});
$('version').textContent=version==='dev'?'':version.slice(0,7);
window.addEventListener('hashchange',()=>{const id=hashId();if(id!==activeId)navigate(id);});
try{
  const r=await fetch(dataURL('catalog.json'));if(!r.ok)throw new Error('Не удалось загрузить расчёты. Нажмите «Обновить приложение».');catalog=await r.json();
  try { const db=await modelStore(); const models=await new Promise((resolve,reject)=>{const r=db.transaction('models').objectStore('models').getAll();r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});for(const book of models)if(book.Custom && book.Id){customBooks.set(book.Id,book);labels[book.Id]=book.Title;addNavigation(book.Id,book.Title);} } catch { announce('Хранилище пользовательских расчётов недоступно.',true); }
  await navigate(hashId());
}catch(error){$('loading').hidden=true;announce(error.message,true);$('main').setAttribute('aria-busy','false');}
