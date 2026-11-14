import {Engine,CalculationError} from './engine.js';
import {parseFormula} from './import-excel.js';
import {cellStyle,fontStyle,excelColor} from './xlsx-style.js';
const point=a=>{const m=/^\$?([A-Z]+)\$?(\d+)$/i.exec(a);if(!m)throw new Error('Неверный диапазон оформления');return {row:+m[2],col:[...m[1].toUpperCase()].reduce((n,c)=>n*26+c.charCodeAt(0)-64,0)};};
const addr=(r,c)=>{let a='';for(;c>0;c=Math.floor((c-1)/26))a=String.fromCharCode(65+(c-1)%26)+a;return a+r;};
export function conditionalRules(ws,theme,xml,Parser){
 const stops=new Map();const parser=new Parser();parser.on('opentag',n=>{if(n.name.split(':').pop()==='cfRule')stops.set(+n.attributes.priority,['1','true'].includes(n.attributes.stopIfTrue));});parser.write(xml).close();
 const rules=[],names={};for(const item of ws.workbook?.definedNames?.model||[])if(item.ranges.length===1)names[item.name]=item.ranges[0];
 for(const block of ws.conditionalFormattings||[]){
  let ranges;try{ranges=block.ref.trim().split(/\s+/).map(ref=>{const [a,b=a]=ref.split(':');return {a:point(a),b:point(b)};});}catch{continue;}
  for(const rule of block.rules||[]){
   const style=rule.style||{},css=fontStyle(style.font||{},theme);
   if(style.fill?.type==='pattern'&&style.fill.pattern==='solid')css.backgroundColor=excelColor(style.fill.fgColor||style.fill.bgColor,theme,'#ffffff');
   const borders=cellStyle({border:style.border},theme);for(const [k,v] of Object.entries(borders))if(k.startsWith('border'))css[k]=v;
   try{rules.push({...rule,css,ranges,stopIfTrue:stops.get(rule.priority)||false,expressions:['expression','cellIs'].includes(rule.type)?(rule.formulae||[]).map(f=>parseFormula(String(f),names)):[]});}catch{/* Unsupported conditional functions do not produce an invented color. */}
  }
 }
 return rules.sort((a,b)=>(a.priority||Infinity)-(b.priority||Infinity));
}
export function conditionalEvaluator(sheets,names={}){
 const cells=new Map(),model={Names:names,Sheets:sheets.map(s=>({Name:s.name,Cells:[]}))};
 for(const s of sheets)for(const c of s.cells)cells.set(`${s.name}!${c.address}`,c);
 class Values extends Engine {
  raw(s,a){if(!this.book.Sheets.some(sheet=>sheet.Name.toUpperCase()===s.toUpperCase()))throw new CalculationError('Лист оформления не найден');const p=point(a);if(p.row<1||p.row>1048576||p.col<1||p.col>16384)throw new CalculationError('Ссылка оформления вне листа');const c=cells.get(this.key(s,a));if(c?.unavailable||c?.missing||c?.value?.error)throw new CalculationError('Источник оформления не рассчитан');return c?.slave?null:c?.value??null;}
  reference(ref,s,depth=0){
   const bang=ref.lastIndexOf('!'),prefix=bang<0?'':ref.slice(0,bang+1),a=ref.slice(bang+1);
   if(/^\$?[a-z]+\$?\d+(?::\$?[a-z]+\$?\d+)?$/i.test(a)){
    const shifted=a.replace(/(\$?)([a-z]+)(\$?)(\d+)/gi,(_,ac,c,ar,r)=>{const p=point(c+r),row=p.row+(ar?0:this.dr),col=p.col+(ac?0:this.dc);if(row<1||col<1)throw new CalculationError('Ссылка за пределами листа');return addr(row,col);});
    return super.reference(prefix+shifted,s,depth);
   }
   return super.reference(ref,s,depth);
  }
 }
 const engine=new Values(model);
 return (sheet,cellAddress)=>{
  const p=point(cellAddress);let result={};
  for(const rule of sheet.conditional||[]){
   if(!rule.ranges.some(r=>p.row>=r.a.row&&p.row<=r.b.row&&p.col>=r.a.col&&p.col<=r.b.col))continue;
   engine.dr=p.row-rule.ranges[0].a.row;engine.dc=p.col-rule.ranges[0].a.col;
   try{
    const left={Op:'REF',Text:cellAddress.replace(/^([A-Z]+)(\d+)$/,'$$$1$$$2')};let expression;
    if(rule.type==='expression')expression=rule.expressions[0];
    else if(rule.type==='cellIs'){
     const ops={equal:'=',notEqual:'<>',greaterThan:'>',lessThan:'<',greaterThanOrEqual:'>=',lessThanOrEqual:'<='};
     if(ops[rule.operator])expression={Op:ops[rule.operator],Args:[left,rule.expressions[0]]};
     else if(['between','notBetween'].includes(rule.operator)){
      expression={Op:'AND',Args:[{Op:'>=',Args:[left,rule.expressions[0]]},{Op:'<=',Args:[left,rule.expressions[1]]}]};if(rule.operator==='notBetween')expression={Op:'NOT',Args:[expression]};
     }
    }
    let matches=false;
    if(expression)matches=!!engine.eval(expression,sheet.name);
    else if(['containsText','beginsWith','endsWith','notContainsText'].includes(rule.type)){
     const text=String(engine.raw(sheet.name,cellAddress)??'').toUpperCase(),needle=String(rule.text??'').toUpperCase();
     const operator=rule.operator||rule.type;if(needle)matches=operator==='beginsWith'?text.startsWith(needle):operator==='endsWith'?text.endsWith(needle):operator==='notContainsText'?!text.includes(needle):text.includes(needle);
    }
    if(matches){result={...rule.css,...result};if(rule.stopIfTrue)break;}
   }catch{/* Missing/unsupported results must not be shown as a successful condition. */}
  }
  return result;
 };
}
