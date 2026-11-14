import { SaxesParser } from 'saxes';
import { formattedValue } from './xlsx-style.js';

const escapeXML=value=>String(value).replace(/[<>&"']/g,c=>({'<':'&lt;','>':'&gt;','&':'&amp;','"':'&quot;',"'":'&apos;'}[c]));
const local=name=>name.split(':').pop();
const attribute=(name,value)=>` ${name}="${escapeXML(value)}"`;

// Record XML element offsets, then patch only changed cells. Unrelated workbook parts
// (including drawings and unsupported Excel features) remain in the original ZIP.
export function patchSheet(xml,changes,clearCaches=false) {
  const parser=new SaxesParser(),edits=[],found=new Set(),rows=new Map();let cell=null,row=null,sheetData=null;
  parser.on('opentag',node=>{
    const name=local(node.name),start=xml.lastIndexOf('<',parser.position-1);
    if(name==='sheetData')sheetData={name:node.name,start,end:parser.position,self:node.isSelfClosing};
    if(name==='row')row={name:node.name,start,open:parser.position,attributes:node.attributes,self:node.isSelfClosing,cells:[]};
    if(name==='c')cell={name:node.name,start,open:parser.position,attributes:node.attributes,formula:false};
    if(name==='f'&&cell)cell.formula=true;
  });
  parser.on('closetag',node=>{
    const name=local(node.name);
    if(name==='c'&&cell){
      cell.end=parser.position;row?.cells.push(cell);
      const address=cell.attributes.r;
      if(changes.has(address)){
        if(cell.formula)throw new Error('Формульные ячейки доступны только для чтения');
        edits.push({start:cell.start,end:cell.end,text:cellXML(cell.name,cell.attributes,changes.get(address))});found.add(address);
      }else if(clearCaches&&cell.formula){
        const original=xml.slice(cell.start,cell.end);
        const text=original.replace(/<(?:[\w.-]+:)?v\b[^>]*(?:\/>|>[\s\S]*?<\/(?:[\w.-]+:)?v\s*>)/g,'');
        edits.push({start:cell.start,end:cell.end,text});
      }
      cell=null;
    }
    if(name==='row'&&row){row.end=parser.position;row.close=row.self?row.open:xml.lastIndexOf('<',parser.position-1);rows.set(Number(row.attributes.r),row);row=null;}
    if(name==='sheetData'&&sheetData){sheetData.close=sheetData.self?sheetData.end:xml.lastIndexOf('<',parser.position-1);sheetData.end=parser.position;}
  });
  parser.write(xml).close();
  const additions=new Map();
  for(const [address,value] of changes){if(found.has(address))continue;const r=Number(address.match(/\d+$/)[0]);if(!additions.has(r))additions.set(r,[]);additions.get(r).push([address,value]);}
  if(additions.size&&!sheetData)throw new Error('В листе нет области данных');
  const prefix=sheetData?.name.includes(':')?sheetData.name.split(':')[0]+':':'';
  const column=address=>[...address.replace(/\d+$/,'')].reduce((n,c)=>n*26+c.charCodeAt(0)-64,0);
  const inserts=new Map();
  const insert=(at,text)=>inserts.set(at,(inserts.get(at)||'')+text);
  for(const [r,values] of [...additions].sort((a,b)=>a[0]-b[0])){
    const existing=rows.get(r);values.sort((a,b)=>column(a[0])-column(b[0]));
    if(existing&&!existing.self){
      for(const [address,value] of values){const after=existing.cells.find(c=>column(c.attributes.r)>column(address));insert(after?.start??existing.close,cellXML(prefix+'c',{r:address},value));}
    }else if(existing){
      const opening=xml.slice(existing.start,existing.open).replace(/\/>$/,'>');
      edits.push({start:existing.start,end:existing.end,text:opening+values.map(([a,v])=>cellXML(prefix+'c',{r:a},v)).join('')+`</${existing.name}>`});
    }else{
      const after=[...rows].find(([number])=>number>r);
      insert(after?.[1].start??sheetData.close,`<${prefix}row r="${r}">`+values.map(([a,v])=>cellXML(prefix+'c',{r:a},v)).join('')+`</${prefix}row>`);
    }
  }
  if(sheetData?.self&&inserts.size){
    const content=[...inserts.values()].join('');inserts.clear();
    edits.push({start:sheetData.start,end:sheetData.end,text:`<${sheetData.name}>${content}</${sheetData.name}>`});
  }
  for(const [at,text] of inserts)edits.push({start:at,end:at,text});
  edits.sort((a,b)=>b.start-a.start||b.end-a.end);
  for(const edit of edits)xml=xml.slice(0,edit.start)+edit.text+xml.slice(edit.end);
  return xml;
}
function cellXML(name,attributes,value){
  const prefix=name.includes(':')?name.split(':')[0]+':':'';
  const attrs=Object.entries(attributes).filter(([k])=>k!=='t').map(([k,v])=>attribute(k,v)).join('');
  if(value==null)return `<${name}${attrs}/>`;
  if(typeof value==='number')return `<${name}${attrs}><${prefix}v>${value}</${prefix}v></${name}>`;
  if(typeof value==='boolean')return `<${name}${attrs} t="b"><${prefix}v>${value?1:0}</${prefix}v></${name}>`;
  return `<${name}${attrs} t="inlineStr"><${prefix}is><${prefix}t xml:space="preserve">${escapeXML(value)}</${prefix}t></${prefix}is></${name}>`;
}

export function parseEdit(text,cell,date1904=false) {
  if(text.length>32767||/[\x00-\x08\x0b\x0c\x0e-\x1f]/.test(text))throw new Error('Недопустимый текст: максимум 32 767 символов, без управляющих кодов');
  if(text.startsWith("'"))return text.slice(1);
  if(!text.trim())return null;
  if(text.startsWith('='))throw new Error('Редактирование формул пока не поддерживается. Для текста, начинающегося с =, добавьте апостроф.');
  if(cell?.kind==='string'||cell?.kind==='object')return text;
  if(cell?.kind==='date'){
    const match=/^(?:(\d{4})-(\d{2})-(\d{2})|(\d{2})\.(\d{2})\.(\d{4}))$/.exec(text.trim());
    if(!match)throw new Error('Введите дату ДД.ММ.ГГГГ или ГГГГ-ММ-ДД');
    const [year,month,day]=match[1]?[+match[1],+match[2],+match[3]]:[+match[6],+match[5],+match[4]];
    const date=Date.UTC(year,month-1,day);const check=new Date(date);
    if(check.getUTCFullYear()!==year||check.getUTCMonth()!==month-1||check.getUTCDate()!==day)throw new Error('Некорректная дата');
    let serial=(date-Date.UTC(1899,11,31))/86400000;if(date>=Date.UTC(1900,2,1))serial++;
    if(date1904)serial-=1462;return serial;
  }
  if(/^(true|false)$/i.test(text.trim()))return text.trim().toUpperCase()==='TRUE';
  if(cell?.kind==='boolean')throw new Error('Введите TRUE или FALSE');
  if(/^[+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)\s*%$/.test(text.trim())){const value=Number(text.trim().replace('%','').replace(',','.'))/100;if(!Number.isFinite(value))throw new Error('Число вне допустимого диапазона');return value;}
  const number=text.trim().replace(',','.');
  if(/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(number)){
    const value=Number(number);if(!Number.isFinite(value))throw new Error('Число вне допустимого диапазона');return value;
  }
  if(cell?.kind==='number')throw new Error('Введите число; десятичная запятая и точка поддерживаются. Для текста добавьте апостроф.');
  return text;
}

export class EditSession {
  constructor(sheets){this.sheets=sheets;this.source=sheets.source;this.edits=new Map();this.history=[];this.revision=0;this.savedRevision=0;}
  get dirty(){return this.edits.size>0;}
  get unsaved(){return this.dirty&&this.savedRevision!==this.revision;}
  key(sheet,address){return JSON.stringify([sheet,address]);}
  apply(sheetName,address,text){
    const sheet=this.sheets.find(s=>s.name===sheetName);if(!sheet||!/^[A-Z]+[1-9]\d*$/.test(address))throw new Error('Ячейка не найдена');
    const row=Number(address.match(/\d+$/)[0]),column=[...address.replace(/\d+$/,'')].reduce((n,c)=>n*26+c.charCodeAt(0)-64,0);
    if(row>sheet.rows||column>sheet.columns)throw new Error('Ячейка за пределами открытой таблицы');
    const cell=sheet.cells.find(c=>c.address===address);
    if(cell?.formula||cell?.slave||cell?.locked)throw new Error('Ячейка доступна только для чтения');
    if(!cell&&this.source.book.getWorksheet(sheetName).sheetProtection?.sheet)throw new Error('Лист защищён от редактирования');
    const value=parseEdit(text,cell,this.source.date1904),key=this.key(sheetName,address);
    const before=this.edits.has(key)?this.edits.get(key):undefined;
    const original=cell?.value??null;if(Object.is(before===undefined?original:before,value))return false;
    this.history.push({key,before});if(Object.is(original,value))this.edits.delete(key);else this.edits.set(key,value);
    this.revision++;return true;
  }
  undo(){const item=this.history.pop();if(!item)return false;if(item.before===undefined)this.edits.delete(item.key);else this.edits.set(item.key,item.before);this.revision++;return true;}
  value(sheet,cell){const key=this.key(sheet,cell.address);return this.edits.has(key)?this.edits.get(key):cell.value;}
  display(value,cell){return formattedValue(value,cell.numFmt,this.source.date1904,this.source.formatter)??(value==null?'':String(value));}
  editText(cell){return cell?.kind==='date'?this.source.formatter.format('yyyy-mm-dd',cell.value,{date1904:this.source.date1904}):cell?.text||'';}
  async prepare(){
    if(this.engine)return;
    const [{Engine,CalculationError,UnsupportedCalculationError},{parseFormula}]=await Promise.all([import('./engine.js'),import('./import-excel.js')]);
    const names={};for(const item of this.source.book.definedNames.model)if(item.ranges.length===1)names[item.name]=item.ranges[0];
    const model={Custom:true,Names:names,Sheets:[]},blocked=new Map();
    const key=(s,a)=>`${s}!${a}`;
    for(const sheet of this.sheets){
      const cells=[];
      for(const c of sheet.cells){
        const cell={Address:c.address,Row:c.row,Column:c.column,Value:c.slave?null:c.value,Editable:!c.formula};
        if(c.formula){try{cell.Expression=parseFormula(c.formula,names);}catch(error){blocked.set(key(sheet.name,c.address),new UnsupportedCalculationError(error.message));}}
        else if(c.value&&typeof c.value==='object'&&c.value.error)blocked.set(key(sheet.name,c.address),new CalculationError(c.value.error));
        cells.push(cell);
      }
      model.Sheets.push({Name:sheet.name,Cells:cells});
    }
    class ViewerEngine extends Engine {
      raw(s,a){const error=blocked.get(this.key(s,a));if(error)throw error;return super.raw(s,a);}
    }
    const engine=new ViewerEngine(model);
    // Check the original caches before trusting this engine for this workbook.
    for(const sheet of this.sheets)for(const c of sheet.cells.filter(c=>c.formula)){
      const k=engine.key(sheet.name,c.address);if(blocked.has(k))continue;
      try{
        const actual=engine.get(sheet.name,c.address),expected=c.value;
        if(expected!=null&&!expected?.error&&!(typeof expected==='number'&&typeof actual==='number'?Math.abs(actual-expected)<=1e-8*Math.max(1,Math.abs(expected)):Object.is(actual,expected))){
          blocked.set(k,new UnsupportedCalculationError('Исходный результат не совпадает с Excel'));engine.cache.clear();
        }
      }catch(error){
        if(error instanceof UnsupportedCalculationError||c.value!=null&&!c.value?.error){blocked.set(k,new UnsupportedCalculationError(error.message));engine.cache.clear();}
      }
    }
    this.engine=engine;this.UnsupportedCalculationError=UnsupportedCalculationError;
  }
  view(){
    if(!this.dirty)return this.sheets;
    if(this.viewRevision===this.revision)return this.viewCache;
    const engine=this.engine;engine.changes.clear();engine.cache.clear();
    for(const [key,value] of this.edits){const [sheet,address]=JSON.parse(key);engine.changes.set(engine.key(sheet,address),value);}
    let calculated=0,unavailable=0;
    const view=this.sheets.map(sheet=>{
      const cells=sheet.cells.map(cell=>{
        const key=this.key(sheet.name,cell.address);
        if(cell.formula){
          try{const value=engine.get(sheet.name,cell.address);calculated++;return {...cell,value,text:value==null?'':String(value),display:this.display(value,cell),richText:null,missing:false,calculated:true};}
          catch(error){unavailable++;return {...cell,text:error.message,display:error instanceof this.UnsupportedCalculationError?'Не пересчитано':'Ошибка пересчёта',missing:false,unavailable:true,reason:error.message,richText:null};}
        }
        if(!this.edits.has(key))return cell;
        const value=this.edits.get(key);return {...cell,value,text:value==null?'':String(value),display:this.display(value,cell),richText:null,edited:true};
      });
      for(const [key,value] of this.edits){const [name,address]=JSON.parse(key);if(name!==sheet.name||cells.some(c=>c.address===address))continue;
        const original={address,numFmt:'General',css:{},value};cells.push({...original,text:value==null?'':String(value),display:this.display(value,original),edited:true});
      }
      return {...sheet,cells};
    });
    this.counts={calculated,unavailable};this.viewRevision=this.revision;this.viewCache=view;return view;
  }
  async export(){
    const edits=new Map(this.edits);
    const {default:JSZip}=await import('jszip');const zip=await JSZip.loadAsync(this.source.bytes);
    if(!edits.size)return this.source.bytes instanceof Uint8Array?this.source.bytes:new Uint8Array(this.source.bytes);
    for(const [name,path] of this.source.sheetPaths){
      const changes=new Map([...edits].map(([key,value])=>[JSON.parse(key),value]).filter(([[sheet]])=>sheet===name).map(([[,address],value])=>[address,value]));
      zip.file(path,patchSheet(await zip.file(path).async('string'),changes,true));
    }
    let workbook=await zip.file('xl/workbook.xml').async('string');
    const calc=/<(?:[\w.-]+:)?calcPr\b[^>]*(?:\/>|>[\s\S]*?<\/(?:[\w.-]+:)?calcPr>)/;
    const root=/<([\w.-]+:)?workbook\b/.exec(workbook),prefix=root?.[1]||'';
    const settings=`<${prefix}calcPr fullCalcOnLoad="1" forceFullCalc="1" calcMode="auto"/>`;
    const afterCalc=/<(?:[\w.-]+:)?(?:oleSize|customWorkbookViews|pivotCaches|smartTagPr|smartTagTypes|webPublishing|fileRecoveryPr|webPublishObjects|extLst)\b|<\/(?:[\w.-]+:)?workbook>/;
    workbook=calc.test(workbook)?workbook.replace(calc,settings):workbook.replace(afterCalc,settings+'$&');zip.file('xl/workbook.xml',workbook);
    // Excel will rebuild its calculation chain on opening the edited copy.
    zip.remove('xl/calcChain.xml');
    for(const path of ['xl/_rels/workbook.xml.rels','[Content_Types].xml']){
      const xml=await zip.file(path).async('string');
      zip.file(path,xml.replace(/<(?:[\w.-]+:)?(?:Relationship|Override)\b[^>]*(?:calcChain)[^>]*\/>/g,''));
    }
    return zip.generateAsync({type:'uint8array',compression:'DEFLATE',compressionOptions:{level:6}});
  }
}
