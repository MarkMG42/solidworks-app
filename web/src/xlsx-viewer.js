// Read-only snapshot: no dependency on the calculation engine or formula parser.
export async function readSnapshot(bytes, ExcelJS) {
  if (bytes.byteLength > 15 * 1024 * 1024) throw new Error('Максимальный размер Excel — 15 МБ');
  const book = new ExcelJS.Workbook(); await book.xlsx.load(bytes);
  // ExcelJS normalizes cached empty strings to null; inspect XML presence separately.
  const [{default:JSZip},saxes] = await Promise.all([import('jszip'),import('saxes')]);
  const SaxesParser=saxes.SaxesParser || saxes.default.SaxesParser;
  const zip = await JSZip.loadAsync(bytes); const caches = new Map();
  for (const [path,entry] of Object.entries(zip.files)) {
    if (!/^xl\/worksheets\/[^/]+\.xml$/.test(path)) continue;
    const xml = await entry.async('string');
    if (xml.length > 32000000) throw new Error('Распакованный XML слишком большой');
    const parser = new SaxesParser(); let current=null, inValue=false;
    const values = new Map();
    parser.on('opentag',node=>{if(node.name==='c')current={address:node.attributes.r,type:node.attributes.t,hasValue:false,text:''};if(node.name==='v'&&current){current.hasValue=true;inValue=true;}});
    parser.on('text',text=>{if(current&&inValue)current.text+=text;});
    parser.on('closetag',node=>{const name=typeof node==='string'?node:node.name;if(name==='v')inValue=false;if(name==='c'&&current){values.set(current.address,current);current=null;}});
    parser.write(xml).close();caches.set(path,values);
  }
  const relationships=new Map(), sheetPaths=new Map();
  const relParser=new SaxesParser();relParser.on('opentag',n=>{if(n.name==='Relationship' && n.attributes.TargetMode!=='External')relationships.set(n.attributes.Id,new URL(n.attributes.Target,'https://xlsx.local/xl/workbook.xml').pathname.slice(1));});
  relParser.write(await zip.file('xl/_rels/workbook.xml.rels').async('string')).close();
  const bookParser=new SaxesParser();bookParser.on('opentag',n=>{if(n.name==='sheet')sheetPaths.set(n.attributes.name,relationships.get(n.attributes['r:id']));});
  bookParser.write(await zip.file('xl/workbook.xml').async('string')).close();
  let count = 0;
  const sheets = book.worksheets.map(ws => {
    const cells = []; let rows = 1, columns = 1;
    ws.eachRow(row => row.eachCell(cell => {
      if (cell.isMerged && cell.address !== cell.master.address) return;
      if (++count > 50000 || cell.row > 10000 || cell.col > 100) throw new Error('Лимит: 50 000 ячеек, 10 000 строк, 100 столбцов');
      const formula = cell.formula;
      const cached = caches.get(sheetPaths.get(ws.name))?.get(cell.address);
      const raw = formula && cached?.type==='str' && cached.hasValue ? cached.text : formula ? cell.result : cell.value;
      const missing = !!formula && raw == null;
      const text = missing ? 'Нет сохранённого результата' : snapshotText(raw);
      cells.push({ address:cell.address, row:cell.row, column:cell.col, formula:formula ? '=' + formula.replace(/^=/,'') : '', text, missing });
      rows = Math.max(rows,cell.row); columns = Math.max(columns,cell.col);
    }));
    return { name:ws.name, cells, rows, columns };
  });
  if (!sheets.length) throw new Error('В файле нет листов');
  return sheets;
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
export function setupViewer(root) {
  const el = (tag,text) => { const node=document.createElement(tag); if(text!=null)node.textContent=text;return node; };
  let sheets=[], page=0;
  const open=el('button','Открыть XLSX…'), file=el('input');file.type='file';file.accept='.xlsx';file.hidden=true;
  const note=el('p','Только чтение. Формулы не пересчитываются. Сохранённые результаты могут быть устаревшими: для обновления пересчитайте и сохраните книгу в Excel. Файл остаётся в этой вкладке браузера.');note.className='hint';
  const status=el('p','Выберите файл .xlsx'); status.setAttribute('role','status');
  const bar=el('div');bar.className='sheet-bar';const label=el('label','Лист');label.htmlFor='viewer-sheet';const picker=el('select');picker.id='viewer-sheet';bar.append(label,picker);bar.hidden=true;
  const viewLabel=el('label','Вид');viewLabel.htmlFor='viewer-mode';
  const mode=el('select');mode.id='viewer-mode';
  for(const [value,text] of [['sheet','Лист Excel'],['list','Список ячеек']]){const option=el('option',text);option.value=value;mode.append(option);}
  const views=el('div');views.className='sheet-bar viewer-views';views.append(viewLabel,mode);views.hidden=true;
  const scroll=el('div');scroll.className='table-scroll';scroll.tabIndex=0;scroll.setAttribute('aria-label','Сохранённые данные Excel');const table=el('table');table.id='viewer-table';scroll.append(table);
  const controls=el('div');controls.className='table-footer';const previous=el('button','←'),next=el('button','→'),info=el('span');previous.setAttribute('aria-label','Предыдущая страница');next.setAttribute('aria-label','Следующая страница');controls.append(previous,info,next);controls.hidden=true;
  root.append(open,file,note,status,bar,views,scroll,controls);
  function render(){
    const sheet=sheets[picker.selectedIndex];if(!sheet)return;table.replaceChildren();
    const grid=mode.value==='sheet';table.classList.toggle('sheet-grid',grid);
    table.setAttribute('aria-label',sheet.name);
    const head=el('thead'),hr=el('tr');
    const titles=grid ? ['',...Array.from({length:sheet.columns},(_,i)=>columnName(i+1))] : ['Ячейка','Сохранённое значение Excel','Формула'];
    for(const title of titles){const th=el('th',title);th.scope='col';hr.append(th);}head.append(hr);table.append(head);
    const body=el('tbody');
    if(grid && sheet.cells.length){
      const cells=new Map(sheet.cells.map(c=>[c.address,c]));
      table.style.width=`${Math.max(360,56+sheet.columns*180)}px`;
      for(let r=page*50+1;r<=Math.min(sheet.rows,(page+1)*50);r++){
        const row=el('tr'),number=el('th',r);number.scope='row';row.append(number);
        for(let col=1;col<=sheet.columns;col++){
          const address=columnName(col)+r,c=cells.get(address),td=el('td');td.dataset.address=address;
          td.setAttribute('aria-label',address);
          if(c){
            const value=el('span',c.text);value.className=c.missing?'missing-result':'saved-value';td.append(value);
            if(c.formula){const formula=el('small',c.formula);formula.className='snapshot-formula';td.append(formula);}
            td.title=[address,c.text,c.formula].filter(Boolean).join(' · ');
          }
          row.append(td);
        }
        body.append(row);
      }
    }else if(!grid){
      table.style.width='';
      for(const c of sheet.cells.slice(page*50,(page+1)*50)){const row=el('tr'),address=el('th',c.address);address.scope='row';row.append(address);const value=el('td',c.text);if(c.missing)value.className='missing-result';row.append(value,el('td',c.formula));body.append(row);}
    }else table.style.width='';
    table.append(body);
    const total=grid ? (sheet.cells.length?sheet.rows:0) : sheet.cells.length;
    status.textContent=sheet.cells.length ? 'Снимок файла: '+fileName : 'Пустой лист · '+fileName;
    bar.hidden=false;views.hidden=false;controls.hidden=total<=50;previous.disabled=page===0;next.disabled=(page+1)*50>=total;
    info.textContent=`${page+1} / ${Math.max(1,Math.ceil(total/50))}`;
    if(total>50 && grid)info.textContent+=` · Строки ${page*50+1}–${Math.min(total,(page+1)*50)} из ${total}`;
    scroll.scrollTop=0;
  }
  mode.onchange=()=>{page=0;render();};
  let fileName='';open.onclick=()=>file.click();picker.onchange=()=>{page=0;render();};previous.onclick=()=>{page--;render();};next.onclick=()=>{page++;render();};
  file.onchange=async()=>{const selected=file.files[0];file.value='';if(!selected)return;open.disabled=true;
    try {if(!/\.xlsx$/i.test(selected.name))throw new Error('Выберите файл .xlsx');if(selected.size>15*1024*1024)throw new Error('Максимальный размер Excel — 15 МБ');const {default:ExcelJS}=await import('exceljs');const loaded=await readSnapshot(await selected.arrayBuffer(),ExcelJS);sheets=loaded;fileName=selected.name;picker.replaceChildren(...sheets.map(s=>el('option',s.name)));page=0;render();}
    catch(error){status.textContent='Файл не открыт: '+error.message;}finally{open.disabled=false;}
  };
}

function columnName(column) {
  let name='';
  for(;column>0;column=Math.floor((column-1)/26))name=String.fromCharCode(65+(column-1)%26)+name;
  return name;
}
