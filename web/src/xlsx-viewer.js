// The snapshot reader preserves Excel presentation and saved values independently of calculation.
import { readTheme, cellStyle, fontStyle, columnPixels, formattedValue } from './xlsx-style.js';
import { readListRules, readListNames } from './xlsx-validation.js';
export async function readSnapshot(bytes, ExcelJS) {
  if (bytes.byteLength > 15 * 1024 * 1024) throw new Error('Максимальный размер Excel — 15 МБ');
  const book = new ExcelJS.Workbook(); await book.xlsx.load(bytes);
  // ExcelJS normalizes cached empty strings to null; inspect XML presence separately.
  const [{default:JSZip},saxes,ssf] = await Promise.all([import('jszip'),import('saxes'),import('ssf')]);
  const SaxesParser=saxes.SaxesParser || saxes.default.SaxesParser;
  const zip = await JSZip.loadAsync(bytes); const caches = new Map(),listRules=new Map(),worksheetXML=new Map();
  for (const [path,entry] of Object.entries(zip.files)) {
    if (!/^xl\/worksheets\/[^/]+\.xml$/.test(path)) continue;
    const xml = await entry.async('string');
    worksheetXML.set(path,xml);
    if (xml.length > 32000000) throw new Error('Распакованный XML слишком большой');
    const parser = new SaxesParser(); let current=null, inValue=false;
    const values = new Map();
    parser.on('opentag',node=>{if(node.name==='c')current={address:node.attributes.r,type:node.attributes.t,hasValue:false,text:''};if(node.name==='v'&&current){current.hasValue=true;inValue=true;}});
    parser.on('text',text=>{if(current&&inValue)current.text+=text;});
    parser.on('closetag',node=>{const name=typeof node==='string'?node:node.name;if(name==='v')inValue=false;if(name==='c'&&current){values.set(current.address,current);current=null;}});
    parser.write(xml).close();caches.set(path,values);
    listRules.set(path,readListRules(xml,SaxesParser));
  }
  const relationships=new Map(), sheetPaths=new Map();
  const relParser=new SaxesParser();relParser.on('opentag',n=>{if(n.name==='Relationship' && n.attributes.TargetMode!=='External')relationships.set(n.attributes.Id,new URL(n.attributes.Target,'https://xlsx.local/xl/workbook.xml').pathname.slice(1));});
  relParser.write(await zip.file('xl/_rels/workbook.xml.rels').async('string')).close();
  const bookParser=new SaxesParser();bookParser.on('opentag',n=>{if(n.name==='sheet')sheetPaths.set(n.attributes.name,relationships.get(n.attributes['r:id']));});
  bookParser.write(await zip.file('xl/workbook.xml').async('string')).close();
  const theme=readTheme(await zip.file('xl/theme/theme1.xml')?.async('string'),SaxesParser);
  const conditional=book.worksheets.some(ws=>ws.conditionalFormattings?.length)?await import('./xlsx-conditional.js'):null;
  let count = 0;
  const sheets = book.worksheets.map(ws => {
    const cells = []; let rows = 1, columns = 1;
    const merges=(ws.model.merges||[]).map(range=>{
      const [start,end=start]=range.split(':');const a=coordinates(start),b=coordinates(end);
      return {top:a.row,left:a.column,bottom:b.row,right:b.column,master:start};
    });
    // _rows/_cells contain existing styled blanks too; do not allocate the used rectangle.
    // ExcelJS eachRow/eachCell without includeEmpty omit those cells.
    for(const row of ws._rows){if(!row)continue;for(const cell of row._cells){
      if(!cell)continue;
      if(cell.value==null&&!Object.keys(cell.style).length)continue;
      if (++count > 50000 || cell.row > 10000 || cell.col > 100) throw new Error('Лимит: 50 000 ячеек, 10 000 строк, 100 столбцов');
      const slave=cell.isMerged && cell.address !== cell.master.address;
      const formula = slave ? null : cell.formula;
      const cached = caches.get(sheetPaths.get(ws.name))?.get(cell.address);
      const raw = slave ? null : formula && cached?.type==='str' && cached.hasValue ? cached.text : formula ? cell.result : cell.value;
      const missing = !!formula && raw == null;
      const text = missing ? 'Нет сохранённого результата' : snapshotText(raw);
      const numeric=raw instanceof Date && cached?.text ? Number(cached.text) : raw;
      const display=missing?text:formattedValue(numeric,cell.numFmt,!!book.properties.date1904,ssf.default||ssf)??text;
      const css=cellStyle(cell.style,theme);
      if(!css.textAlign)css.textAlign=typeof raw==='number'||raw instanceof Date?'right':typeof raw==='boolean'?'center':'left';
      const richText=raw?.richText?.map(run=>({text:run.text,css:fontStyle(run.font,theme)}));
      cells.push({ address:cell.address, row:cell.row, column:cell.col, formula:formula ? '=' + formula.replace(/^=/,'') : '', text, display, missing, css, richText, slave, value:numeric, kind:raw instanceof Date?'date':typeof raw, numFmt:cell.numFmt||'General', locked:!!ws.sheetProtection?.sheet&&cell.protection?.locked!==false });
      rows = Math.max(rows,cell.row); columns = Math.max(columns,cell.col);
    }}
    for(const merge of merges){rows=Math.max(rows,merge.bottom);columns=Math.max(columns,merge.right);}
    if(rows>10000||columns>100)throw new Error('Лимит: 50 000 ячеек, 10 000 строк, 100 столбцов');
    const columnWidths=Array.from({length:columns},(_,i)=>{
      const col=ws.getColumn(i+1);return col.hidden?0:columnPixels(col.width??ws.properties.defaultColWidth??8.43);
    });
    const rowHeights=Array.from({length:rows},(_,i)=>{
      const row=ws.findRow(i+1);return row?.hidden?0:(row?.height??ws.properties.defaultRowHeight??15)*4/3;
    });
    return { name:ws.name, cells, rows, columns, merges, columnWidths, rowHeights, conditional:conditional?.conditionalRules(ws,theme,worksheetXML.get(sheetPaths.get(ws.name))||'',SaxesParser)||[], listRules:listRules.get(sheetPaths.get(ws.name))||[], showGridLines:ws.views?.[0]?.showGridLines!==false };
  });
  if (!sheets.length) throw new Error('В файле нет листов');
  const listNames=readListNames(await zip.file('xl/workbook.xml').async('string'),SaxesParser);
  Object.defineProperty(sheets,'source',{value:{bytes,zip,sheetPaths,book,listNames,conditionalEvaluator:conditional?.conditionalEvaluator,date1904:!!book.properties.date1904,formatter:ssf.default||ssf}});
  return sheets;
}
function coordinates(address) {
  const match=/^([A-Z]+)(\d+)$/.exec(address);if(!match)throw new Error('Некорректный адрес ячейки');
  let column=0;for(const letter of match[1])column=column*26+letter.charCodeAt(0)-64;
  return {row:Number(match[2]),column};
}
function snapshotText(value) {
  if (value == null) return '';
  if (value instanceof Date) return value.toISOString();
  if (typeof value === 'object') {
    if (value.error) return value.error;
    if (value.richText) return value.richText.map(v=>v.text).join('');
    if (value.text != null) return String(value.text);
    throw new Error('Неподдерживаемый тип сохранённого значения');
  }
  return String(value);
}
export function setupViewer(root,hooks={}) {
  const el=(tag,text)=>{const node=document.createElement(tag);if(text!=null)node.textContent=text;return node;};
  let sheets=[],page=0,fileName='',session=null,selectedAddress='',selectedCell=null,applying=false;
  let currentRecord=null,loadToken=0;const cache=new Map(),saveTimers=new Map();
  const file=el('input');file.type='file';file.accept='.xlsx';file.multiple=true;file.hidden=true;
  const note=el('p','Выберите ячейку и измените значение ниже. Оформление Excel сохраняется. Поддерживаемые формулы пересчитываются после ввода, остальные явно помечаются. Книги и изменения сохраняются в этом браузере.');note.className='hint';
  const status=el('p','Добавьте XLSX кнопкой «Добавить расчёт» слева');status.setAttribute('role','status');
  const bar=el('div');bar.className='sheet-bar';const label=el('label','Лист');label.htmlFor='viewer-sheet';
  const picker=el('select');picker.id='viewer-sheet';bar.append(label,picker);bar.hidden=true;
  const views=el('div');views.className='sheet-bar viewer-views';views.hidden=true;
  const viewLabel=el('label','Вид');viewLabel.htmlFor='viewer-mode';const mode=el('select');mode.id='viewer-mode';
  for(const [value,text] of [['sheet','Оформление Excel'],['list','Список ячеек']]){const option=el('option',text);option.value=value;mode.append(option);}
  views.append(viewLabel,mode);
  const formulaBar=el('div');formulaBar.id='viewer-formula-bar';formulaBar.hidden=true;
  const addressLabel=el('output','—');addressLabel.id='viewer-address';addressLabel.setAttribute('aria-label','Выбранная ячейка');
  const formulaLabel=el('label','fx');formulaLabel.htmlFor='viewer-formula';
  const formulaInput=el('input');formulaInput.id='viewer-formula';formulaInput.readOnly=true;formulaInput.setAttribute('aria-label','Формула или значение выбранной ячейки');
  const cacheStatus=el('span');cacheStatus.id='viewer-cache-status';
  formulaBar.append(addressLabel,formulaLabel,formulaInput,cacheStatus);
  const scroll=el('div');scroll.className='table-scroll';scroll.tabIndex=0;scroll.setAttribute('aria-label','Сохранённые данные Excel');
  const table=el('table');table.id='viewer-table';scroll.append(table);
  const controls=el('div');controls.className='table-footer';controls.hidden=true;
  const previous=el('button','←'),next=el('button','→'),info=el('span');
  previous.setAttribute('aria-label','Предыдущая страница');next.setAttribute('aria-label','Следующая страница');controls.append(previous,info,next);
  const editBar=el('form');editBar.id='viewer-edit-bar';editBar.hidden=true;
  const editLabel=el('label','Значение');editLabel.htmlFor='viewer-edit-value';
  const editChoice=el('select');editChoice.id='viewer-edit-choice';editChoice.setAttribute('aria-label','Выбор значения ячейки');editChoice.hidden=true;
  const inlineChoice=el('select');inlineChoice.className='excel-list-choice';inlineChoice.setAttribute('aria-label','Выпадающий список ячейки');inlineChoice.onclick=event=>event.stopPropagation();
  const editInput=el('input');editInput.id='viewer-edit-value';editInput.autocomplete='off';editInput.setAttribute('aria-label','Новое значение ячейки');
  const apply=el('button','Применить');apply.type='submit';
  const undo=el('button','Отменить изменение');undo.type='button';undo.disabled=true;
  const download=el('button','Скачать XLSX');download.type='button';
  editBar.append(editLabel,editInput,editChoice,apply,undo,download);
  const editStatus=el('p');editStatus.id='viewer-edit-status';editStatus.setAttribute('role','status');editStatus.hidden=true;
  const toolbar=el('div');toolbar.id='viewer-toolbar';
  const help=el('details'),summary=el('summary','Справка');help.className='viewer-help';help.append(summary,note);
  const expand=el('button','Развернуть');expand.id='viewer-expand';expand.type='button';expand.setAttribute('aria-pressed','false');
  const app=root.closest('.app');expand.onclick=()=>{
    const expanded=app.classList.toggle('viewer-expanded');expand.textContent=expanded?'Свернуть':'Развернуть';expand.setAttribute('aria-pressed',String(expanded));
  };
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&app.classList.contains('viewer-expanded')&&!event.target.matches('input,select,textarea'))expand.click();});
  const remove=el('button','Удалить таблицу');remove.id='viewer-delete-book';remove.type='button';remove.hidden=true;remove.onclick=()=>{if(currentRecord)hooks.onDelete?.(currentRecord.id);};
  toolbar.append(views,status,help,expand,remove);
  const bottom=el('div');bottom.id='viewer-bottom';bottom.append(bar,controls);
  root.append(toolbar,file,formulaBar,editBar,editStatus,scroll,bottom);
  function state(){return {edits:[...session.edits],history:session.history.map(item=>({...item})),sheet:picker.selectedIndex,page,mode:mode.value,selectedAddress};}
  function remember(){if(currentRecord&&session)cache.set(currentRecord.id,{sheets,session,state:state()});}
  function statsText(){return session?.dirty?`Изменений: ${session.edits.size}. Пересчитано формул: ${session.counts?.calculated||0}; не обновлено: ${session.counts?.unavailable||0}. `:'';}
  function queueSave(){
    if(!currentRecord||!hooks.onSave)return;
    const record={...currentRecord,state:state()},editing=session,revision=session.revision;
    remember();clearTimeout(saveTimers.get(record.id));
    if(editing.dirty)setEditStatus(statsText()+'Сохраняю в браузере…');
    saveTimers.set(record.id,setTimeout(async()=>{
      saveTimers.delete(record.id);
      try{await hooks.onSave(record);if(editing.revision===revision)editing.savedRevision=revision;
        if(currentRecord?.id===record.id&&session===editing&&editing.revision===revision){currentRecord=record;if(editing.dirty)setEditStatus(statsText()+'Сохранено в этом браузере.');}
      }catch(error){if(currentRecord?.id===record.id)setEditStatus('Автосохранение не удалось: '+error.message+'. Скачайте XLSX перед закрытием.');}
    },150));
  }
  function setEditStatus(text){editStatus.textContent=text;editStatus.hidden=!text;}
  function fillChoices(select,list,cell){
    const current=cell?.text||'',match=list.options.find(option=>option.text.toUpperCase()===current.toUpperCase());
    select.replaceChildren();
    if(!list.options.some(option=>option.text==='')){const blank=el('option','Выберите…');blank.value='';blank.disabled=!list.allowBlank;select.append(blank);}
    if(current&&!match){const previous=el('option',current+' (текущее)');previous.value=current;previous.disabled=true;select.append(previous);}
    for(const item of list.options){const option=el('option',item.text);option.value=item.text;select.append(option);}
    select.value=match?.text??current;
  }
  function selectCell(td,cell){
    const old=table.querySelector('.selected-cell');if(old){old.classList.remove('selected-cell','has-excel-list');old.tabIndex=-1;}inlineChoice.remove();
    td.classList.add('selected-cell');td.tabIndex=0;
    addressLabel.textContent=td.dataset.address;
    formulaInput.value=cell?.formula||cell?.text||'';
    formulaInput.title=cell?.text||'';
    cacheStatus.textContent=cell?.unavailable?cell.display+': '+cell.reason:cell?.calculated?'Пересчитано':cell?.missing?'Нет сохранённого результата':'';
    selectedAddress=td.dataset.address;selectedCell=cell;
    const protectedSheet=session?.source.book.getWorksheet(sheets[picker.selectedIndex]?.name)?.sheetProtection?.sheet;
    const blocked=!!cell?.formula||!!cell?.locked||!cell&&!!protectedSheet;
    editInput.disabled=blocked;apply.disabled=blocked||applying;
    editInput.value=blocked?'':session?.editText(cell)||'';
    editInput.placeholder=cell?.formula?'Формула — только чтение':blocked?'Ячейка защищена':'Введите значение';
    const list=blocked?null:session?.list(sheets[picker.selectedIndex].name,selectedAddress);
    const hasList=!!list?.options?.length;editChoice.hidden=!hasList;editInput.hidden=hasList;editLabel.htmlFor=hasList?'viewer-edit-choice':'viewer-edit-value';
    editChoice.disabled=blocked||applying;inlineChoice.disabled=blocked||applying;
    if(hasList){fillChoices(editChoice,list,cell);fillChoices(inlineChoice,list,cell);td.classList.add('has-excel-list');td.append(inlineChoice);}
    if(list?.reason)cacheStatus.textContent=list.reason;
  }
  function render(){
    const displayed=session?session.view():sheets,sheet=displayed[picker.selectedIndex];if(!sheet)return;
    const names={};for(const item of sheets.source.book.definedNames.model)if(item.ranges.length===1)names[item.name]=item.ranges[0];
    const conditional=sheets.source.conditionalEvaluator?.(displayed,names);
    table.replaceChildren();addressLabel.textContent='—';formulaInput.value='';cacheStatus.textContent='';
    const grid=mode.value==='sheet',listed=sheet.cells.filter(c=>!c.slave);
    table.classList.toggle('sheet-grid',grid);table.classList.toggle('without-gridlines',!sheet.showGridLines);
    scroll.classList.toggle('excel-paper',grid);formulaBar.hidden=!grid;
    table.setAttribute('aria-label',sheet.name);
    const head=el('thead'),hr=el('tr');
    const titles=grid?['',...Array.from({length:sheet.columns},(_,i)=>columnName(i+1))]:['Ячейка','Сохранённое значение Excel','Формула'];
    for(const [i,title] of titles.entries()){
      const th=el('th',title);th.scope='col';
      if(grid&&i&&sheet.columnWidths[i-1]===0)th.hidden=true;
      hr.append(th);
    }
    head.append(hr);const body=el('tbody');
    editBar.hidden=!grid;undo.disabled=!session?.history.length;
    setEditStatus(session?.dirty?statsText()+(currentRecord?(session.unsaved?'Сохраняю в браузере…':'Сохранено в этом браузере.'):'Скачайте XLSX, чтобы сохранить ввод.'):'');
    if(grid){
      const cols=el('colgroup'),gutter=el('col');gutter.style.width='42px';cols.append(gutter);
      for(const width of sheet.columnWidths){const col=el('col');col.style.width=width+'px';if(!width)col.style.display='none';cols.append(col);}
      table.append(cols);table.style.width=(42+sheet.columnWidths.reduce((a,b)=>a+b,0))+'px';
      const cells=new Map(sheet.cells.map(c=>[c.address,c]));
      const start=page*50+1,end=Math.min(sheet.rows,(page+1)*50),covered=new Set(),mergeStarts=new Map();
      for(const merge of sheet.merges){
        const top=Math.max(start,merge.top),bottom=Math.min(end,merge.bottom);
        if(top>bottom)continue;
        // Clip a merge at page boundaries, retaining its master value on each page.
        const visibleRows=Array.from({length:bottom-top+1},(_,i)=>top+i).filter(r=>sheet.rowHeights[r-1]>0);
        const visibleCols=Array.from({length:merge.right-merge.left+1},(_,i)=>merge.left+i).filter(c=>sheet.columnWidths[c-1]>0);
        if(!visibleRows.length||!visibleCols.length)continue;
        const first=columnName(visibleCols[0])+visibleRows[0];
        mergeStarts.set(first,{...merge,rowSpan:visibleRows.length,colSpan:visibleCols.length});
        for(const r of visibleRows)for(const c of visibleCols){const addr=columnName(c)+r;if(addr!==first)covered.add(addr);}
      }
      for(let r=start;r<=end;r++){
        const row=el('tr');row.style.height=sheet.rowHeights[r-1]+'px';if(!sheet.rowHeights[r-1])row.hidden=true;
        const number=el('th',r);number.scope='row';row.append(number);
        for(let col=1;col<=sheet.columns;col++){
          const address=columnName(col)+r;if(covered.has(address))continue;
          const merge=mergeStarts.get(address),c=cells.get(merge?.master||address),td=el('td');
          td.dataset.address=merge?.master||address;td.setAttribute('aria-label',td.dataset.address);td.tabIndex=-1;
          if(!sheet.columnWidths[col-1])td.hidden=true;
          if(merge){td.rowSpan=merge.rowSpan;td.colSpan=merge.colSpan;}
          if(c){
            Object.assign(td.style,c.css);
            const value=el('span');value.className=c.missing||c.unavailable?'excel-cell-content missing-result':'excel-cell-content saved-value';
            if(c.edited)td.classList.add('edited-cell');
            const height=merge?sheet.rowHeights.slice(Math.max(start,merge.top)-1,Math.min(end,merge.bottom)).reduce((a,b)=>a+b,0):sheet.rowHeights[r-1];
            value.style.maxHeight=Math.max(0,height-2)+'px';
            if(c.richText){for(const run of c.richText){const part=el('span',run.text);Object.assign(part.style,run.css);value.append(part);}}
            else value.textContent=c.display;
            td.append(value);td.title=[td.dataset.address,c.text,c.formula].filter(Boolean).join(' · ');
          }
          if(conditional)Object.assign(td.style,conditional(sheet,td.dataset.address));
          td.addEventListener('click',()=>selectCell(td,c));td.addEventListener('focus',()=>selectCell(td,c));
          td.addEventListener('dblclick',()=>{selectCell(td,c);if(!editInput.disabled){if(!editChoice.hidden)inlineChoice.focus();else{editInput.focus();editInput.select();}}});
          td.addEventListener('keydown',event=>{if(event.target!==td)return;if(['Enter','F2'].includes(event.key)&&!editInput.disabled){event.preventDefault();if(!editChoice.hidden)inlineChoice.focus();else{editInput.focus();editInput.select();}}});
          row.append(td);
        }
        body.append(row);
      }
    }else{
      table.style.width='';
      for(const c of listed.slice(page*50,(page+1)*50)){
        const row=el('tr'),address=el('th',c.address),value=el('td',c.display);address.scope='row';
        if(c.missing)value.className='missing-result';row.append(address,value,el('td',c.formula));body.append(row);
      }
    }
    table.append(head,body);
    const total=grid?sheet.rows:listed.length;
    status.textContent=(sheet.cells.length?'':'Пустой лист · ')+fileName;status.title=fileName;
    bar.hidden=false;views.hidden=false;controls.hidden=total<=50;
    previous.disabled=page===0;next.disabled=(page+1)*50>=total;
    info.textContent=`${page+1} / ${Math.max(1,Math.ceil(total/50))}`;
    if(total>50&&grid)info.textContent+=` · Строки ${page*50+1}–${Math.min(total,(page+1)*50)} из ${total}`;
    scroll.scrollTop=0;scroll.scrollLeft=0;
    const first=table.querySelector(`td[data-address="${selectedAddress}"]:not([hidden])`)||table.querySelector('tbody tr:not([hidden]) td:not([hidden])');
    if(grid&&first)selectCell(first,sheet.cells.find(c=>c.address===first.dataset.address));
  }
  function refresh(){const top=scroll.scrollTop,left=scroll.scrollLeft;render();scroll.scrollTop=top;scroll.scrollLeft=left;}
  editBar.onsubmit=async event=>{
    event.preventDefault();if(!session||apply.disabled||applying)return;
    const editing=session,sheetName=sheets[picker.selectedIndex].name,address=selectedAddress,text=editChoice.hidden?editInput.value:editChoice.value;
    applying=true;apply.disabled=true;
    try{
      await editing.prepare();if(session!==editing)return;
      editing.apply(sheetName,address,text);refresh();queueSave();
    }catch(error){setEditStatus(error.message);}
    finally{applying=false;apply.disabled=editInput.disabled;editChoice.disabled=editInput.disabled;inlineChoice.disabled=editInput.disabled;}
  };
  editChoice.onchange=()=>{inlineChoice.value=editChoice.value;editBar.requestSubmit();};
  inlineChoice.onchange=()=>{editChoice.value=inlineChoice.value;editBar.requestSubmit();};
  editInput.onkeydown=event=>{if(event.key==='Escape'){editInput.value=session.editText(selectedCell);editInput.blur();}};
  undo.onclick=()=>{if(session?.undo()){refresh();queueSave();}};
  download.onclick=async()=>{
    if(!session)return;download.disabled=true;
    try{
      const exporting=session,revision=exporting.revision;
      const bytes=await exporting.export(),blob=new Blob([bytes],{type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'});
      const url=URL.createObjectURL(blob),link=el('a');link.href=url;link.download=fileName.replace(/\.xlsx$/i,'')+'-изменено.xlsx';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
      if(session===exporting&&exporting.revision===revision)exporting.savedRevision=revision;
      setEditStatus(exporting.dirty?'XLSX скачан. Excel пересчитает формулы при открытии; старый кэш результатов в копии удалён.':'XLSX скачан без изменений.');
    }catch(error){setEditStatus('Не удалось сохранить XLSX: '+error.message);}finally{download.disabled=false;}
  };
  mode.onchange=()=>{page=0;render();queueSave();};picker.onchange=()=>{page=0;selectedAddress='';render();queueSave();};
  previous.onclick=()=>{page--;render();queueSave();};next.onclick=()=>{page++;render();queueSave();};
  window.addEventListener('beforeunload',event=>{if(session?.unsaved||[...cache.values()].some(entry=>entry.session.unsaved)){event.preventDefault();event.returnValue='';}});
  file.onchange=async()=>{
    const files=[...file.files],selected=files[0];file.value='';if(!selected)return;
    if(hooks.onFiles){await hooks.onFiles(files);return;}file.disabled=true;
    try{
      if(!/\.xlsx$/i.test(selected.name))throw new Error('Выберите файл .xlsx');
      if(selected.size>15*1024*1024)throw new Error('Максимальный размер Excel — 15 МБ');
      if(session?.unsaved&&!window.confirm('Открыть другой файл? Нескачанные изменения текущей книги будут потеряны.'))return;
      const [{default:ExcelJS},{EditSession}]=await Promise.all([import('exceljs'),import('./xlsx-editor.js')]);const loaded=await readSnapshot(await selected.arrayBuffer(),ExcelJS);
      sheets=loaded;session=new EditSession(sheets);selectedAddress='';fileName=selected.name;picker.replaceChildren(...sheets.map(s=>el('option',s.name)));page=0;render();
    }catch(error){status.textContent='Файл не открыт: '+error.message;}finally{file.disabled=false;}
  };
  async function register(record,loaded){const {EditSession}=await import('./xlsx-editor.js');cache.set(record.id,{sheets:loaded,session:new EditSession(loaded),state:record.state||{}});}
  async function openBook(record){
    remember();const token=++loadToken;
    let entry=cache.get(record.id);
    if(!entry){
      const [{default:ExcelJS},{EditSession}]=await Promise.all([import('exceljs'),import('./xlsx-editor.js')]);
      const loaded=await readSnapshot(record.bytes,ExcelJS),editing=new EditSession(loaded),saved=record.state||{};
      editing.edits=new Map(saved.edits||[]);editing.history=saved.history||[];editing.revision=editing.edits.size?1:0;editing.savedRevision=editing.revision;
      if(editing.dirty)await editing.prepare();entry={sheets:loaded,session:editing,state:saved};cache.set(record.id,entry);
    }
    if(token!==loadToken)return;
    currentRecord=record;sheets=entry.sheets;session=entry.session;fileName=record.fileName;
    picker.replaceChildren(...sheets.map(s=>el('option',s.name)));picker.selectedIndex=Math.min(entry.state.sheet||0,sheets.length-1);
    page=entry.state.page||0;mode.value=entry.state.mode||'sheet';selectedAddress=entry.state.selectedAddress||'';remove.hidden=false;render();root.dataset.book=record.id;
  }
  return {
    chooseFiles:()=>file.click(),register,openBook,
    showError:message=>{status.textContent=message;status.title=message;},
    forget:id=>{clearTimeout(saveTimers.get(id));saveTimers.delete(id);cache.delete(id);if(currentRecord?.id===id){loadToken++;currentRecord=null;session=null;sheets=[];delete root.dataset.book;table.replaceChildren();bar.hidden=true;controls.hidden=true;views.hidden=true;formulaBar.hidden=true;editBar.hidden=true;remove.hidden=true;setEditStatus('');status.textContent='Добавьте XLSX кнопкой «Добавить расчёт» слева';}}
  };

}

function columnName(column) {
  let name='';
  for(;column>0;column=Math.floor((column-1)/26))name=String.fromCharCode(65+(column-1)%26)+name;
  return name;
}
