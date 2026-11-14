// Read-only snapshot: no dependency on the calculation engine or formula parser.
import { readTheme, cellStyle, fontStyle, columnPixels, formattedValue } from './xlsx-style.js';
export async function readSnapshot(bytes, ExcelJS) {
  if (bytes.byteLength > 15 * 1024 * 1024) throw new Error('Максимальный размер Excel — 15 МБ');
  const book = new ExcelJS.Workbook(); await book.xlsx.load(bytes);
  // ExcelJS normalizes cached empty strings to null; inspect XML presence separately.
  const [{default:JSZip},saxes,ssf] = await Promise.all([import('jszip'),import('saxes'),import('ssf')]);
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
  const theme=readTheme(await zip.file('xl/theme/theme1.xml')?.async('string'),SaxesParser);
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
      cells.push({ address:cell.address, row:cell.row, column:cell.col, formula:formula ? '=' + formula.replace(/^=/,'') : '', text, display, missing, css, richText, slave });
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
    return { name:ws.name, cells, rows, columns, merges, columnWidths, rowHeights, showGridLines:ws.views?.[0]?.showGridLines!==false };
  });
  if (!sheets.length) throw new Error('В файле нет листов');
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
export function setupViewer(root) {
  const el=(tag,text)=>{const node=document.createElement(tag);if(text!=null)node.textContent=text;return node;};
  let sheets=[],page=0,fileName='';
  const open=el('button','Открыть XLSX…'),file=el('input');file.type='file';file.accept='.xlsx';file.hidden=true;
  const note=el('p','Оформление из файла Excel. Только чтение, без пересчёта. Результаты могут быть устаревшими — обновляйте их в Excel. Файл остаётся в этой вкладке.');note.className='hint';
  const status=el('p','Выберите файл .xlsx');status.setAttribute('role','status');
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
  root.append(open,file,note,status,bar,views,formulaBar,scroll,controls);
  function selectCell(td,cell){
    const old=table.querySelector('.selected-cell');if(old){old.classList.remove('selected-cell');old.tabIndex=-1;}
    td.classList.add('selected-cell');td.tabIndex=0;
    addressLabel.textContent=td.dataset.address;
    formulaInput.value=cell?.formula||cell?.text||'';
    formulaInput.title=cell?.text||'';
    cacheStatus.textContent=cell?.missing?'Нет сохранённого результата':'';
  }
  function render(){
    const sheet=sheets[picker.selectedIndex];if(!sheet)return;
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
      if(sheet.cells.length)for(let r=start;r<=end;r++){
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
            const value=el('span');value.className=c.missing?'excel-cell-content missing-result':'excel-cell-content saved-value';
            const height=merge?sheet.rowHeights.slice(Math.max(start,merge.top)-1,Math.min(end,merge.bottom)).reduce((a,b)=>a+b,0):sheet.rowHeights[r-1];
            value.style.maxHeight=Math.max(0,height-2)+'px';
            if(c.richText){for(const run of c.richText){const part=el('span',run.text);Object.assign(part.style,run.css);value.append(part);}}
            else value.textContent=c.display;
            td.append(value);td.title=[td.dataset.address,c.text,c.formula].filter(Boolean).join(' · ');
          }
          td.addEventListener('click',()=>selectCell(td,c));td.addEventListener('focus',()=>selectCell(td,c));
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
    const total=grid?(sheet.cells.length?sheet.rows:0):listed.length;
    status.textContent=(sheet.cells.length?'Файл: ':'Пустой лист · ')+fileName;
    bar.hidden=false;views.hidden=false;controls.hidden=total<=50;
    previous.disabled=page===0;next.disabled=(page+1)*50>=total;
    info.textContent=`${page+1} / ${Math.max(1,Math.ceil(total/50))}`;
    if(total>50&&grid)info.textContent+=` · Строки ${page*50+1}–${Math.min(total,(page+1)*50)} из ${total}`;
    scroll.scrollTop=0;scroll.scrollLeft=0;
    const first=table.querySelector('tbody tr:not([hidden]) td:not([hidden])');
    if(grid&&first)selectCell(first,sheet.cells.find(c=>c.address===first.dataset.address));
  }
  mode.onchange=()=>{page=0;render();};open.onclick=()=>file.click();picker.onchange=()=>{page=0;render();};
  previous.onclick=()=>{page--;render();};next.onclick=()=>{page++;render();};
  file.onchange=async()=>{
    const selected=file.files[0];file.value='';if(!selected)return;open.disabled=true;
    try{
      if(!/\.xlsx$/i.test(selected.name))throw new Error('Выберите файл .xlsx');
      if(selected.size>15*1024*1024)throw new Error('Максимальный размер Excel — 15 МБ');
      const {default:ExcelJS}=await import('exceljs');const loaded=await readSnapshot(await selected.arrayBuffer(),ExcelJS);
      sheets=loaded;fileName=selected.name;picker.replaceChildren(...sheets.map(s=>el('option',s.name)));page=0;render();
    }catch(error){status.textContent='Файл не открыт: '+error.message;}finally{open.disabled=false;}
  };
}

function columnName(column) {
  let name='';
  for(;column>0;column=Math.floor((column-1)/26))name=String.fromCharCode(65+(column-1)%26)+name;
  return name;
}
