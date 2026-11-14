// Excel list validation is presentation/input metadata, never executable JavaScript.
const local=name=>name.split(':').pop();
function point(address){const match=/^\$?([A-Z]+)\$?(\d+)$/i.exec(address);if(!match)throw new Error('Неподдерживаемая ссылка списка');return {row:+match[2],column:[...match[1].toUpperCase()].reduce((n,c)=>n*26+c.charCodeAt(0)-64,0)};}
function bounds(range){const [a,b=a]=range.split(':');const start=point(a),end=point(b);return {top:start.row,left:start.column,bottom:end.row,right:end.column};}
function address(row,column){let value='';for(;column>0;column=Math.floor((column-1)/26))value=String.fromCharCode(65+(column-1)%26)+value;return value+row;}
export function readListRules(xml,Parser){
 const rules=[];let rule=null,field=null;const parser=new Parser();
 parser.on('opentag',node=>{const name=local(node.name);if(name==='dataValidation')rule={...node.attributes,formula:'',sqref:node.attributes.sqref||''};if(rule&&['formula1','sqref'].includes(name))field=name;});
 parser.on('text',text=>{if(rule&&field==='formula1')rule.formula+=text;if(rule&&field==='sqref')rule.sqref+=text;});
 parser.on('closetag',node=>{const name=local(node.name);if(name===field)field=null;if(name==='dataValidation'&&rule){
  if(rule.type==='list'){try{rule.ranges=rule.sqref.trim().split(/\s+/).filter(Boolean).map(bounds);if(rule.ranges.length)rules.push(rule);}catch{/* Malformed ranges cannot be assigned to any cell. */}}
  rule=null;field=null;
 }});parser.write(xml).close();return rules;
}
export function readListNames(xml,Parser){
 const names=[];let current=null;const parser=new Parser();
 parser.on('opentag',node=>{if(local(node.name)==='definedName')current={name:node.attributes.name,scope:node.attributes.localSheetId==null?null:+node.attributes.localSheetId,text:''};});
 parser.on('text',text=>{if(current)current.text+=text;});
 parser.on('closetag',node=>{if(local(node.name)==='definedName'&&current){names.push(current);current=null;}});parser.write(xml).close();return names;
}
export function resolveList(session,sheetName,cellAddress){
 const sheet=session.sheets.find(s=>s.name===sheetName),target=point(cellAddress);
 const rule=[...(sheet.listRules||[])].reverse().find(rule=>rule.ranges.some(r=>target.row>=r.top&&target.row<=r.bottom&&target.column>=r.left&&target.column<=r.right));
 if(!rule)return null;
 const anchor=rule.ranges[0],scope=session.sheets.indexOf(sheet),names=session.source.listNames||[],cellMaps=new Map();
 const getCell=(name,reference)=>{
  const s=session.view().find(s=>s.name.toUpperCase()===name.toUpperCase());if(!s)throw new Error('Лист списка не найден');
  if(!cellMaps.has(s.name))cellMaps.set(s.name,new Map(s.cells.map(c=>[c.address,c])));
  const cell=cellMaps.get(s.name).get(reference.toUpperCase().replaceAll('$',''));
  if(cell?.unavailable||cell?.missing||cell?.value?.error)throw new Error('Источник списка не пересчитан');
  return cell?.value&&typeof cell.value==='object'?cell.text:cell?.value??null;
 };
 const relative=reference=>reference.replace(/(\$?)([a-z]+)(\$?)(\d+)/gi,(_,absCol,col,absRow,row)=>{
  const p=point(col+row),r=p.row+(absRow?0:target.row-anchor.top),c=p.column+(absCol?0:target.column-anchor.left);
  if(r<1||c<1||r>1048576||c>16384)throw new Error('Ссылка списка вне листа');return address(r,c);
 });
 const referenceParts=expression=>{
  const match=/^(?:(?:'((?:[^']|'')+)'|([^'!]+))!)?(\$?[a-z]+\$?\d+(?::\$?[a-z]+\$?\d+)?)$/i.exec(expression);
  if(!match)return null;
  const name=(match[1]?.replaceAll("''", "'")||match[2]||sheetName);if(/[\[\]]/.test(name))throw new Error('Внешние списки не загружаются');
  return {name,reference:match[3]};
 };
 function indirectText(expression){
  const tokens=expression.match(/"(?:[^"]|"")*"|[^&]+/g)||[];let value='';
  for(const token of tokens){const t=token.trim(),quoted=/^"((?:[^"]|"")*)"$/.exec(t);if(quoted){value+=quoted[1].replaceAll('""','"');continue;}
   const ref=referenceParts(t);if(!ref||ref.reference.includes(':'))throw new Error('Такой INDIRECT в списке пока не поддерживается');
   value+=String(getCell(ref.name,relative(ref.reference))??'');
  }
  return value;
 }
 function resolve(expression,shift=true,depth=0){
  if(depth>12)throw new Error('Циклическое имя списка');expression=expression.trim().replace(/^=/,'').trim();
  const literal=/^"((?:[^"]|"")*)"$/.exec(expression);if(literal)return literal[1].replaceAll('""','"').split(',').map(value=>({text:value,value,literal:true}));
  const named=names.find(n=>n.scope===scope&&n.name.toUpperCase()===expression.toUpperCase())||names.find(n=>n.scope==null&&n.name.toUpperCase()===expression.toUpperCase());
  if(named)return resolve(named.text,false,depth+1);
  const indirect=/^(?:_xlfn\.)?INDIRECT\((.*)\)$/i.exec(expression);if(indirect)return resolve(indirectText(indirect[1]),false,depth+1);
  const ref=referenceParts(expression);if(!ref)throw new Error('Источник списка пока не поддерживается');
  const range=bounds(shift?relative(ref.reference):ref.reference);
  if(range.bottom<range.top||range.right<range.left||range.top<1||range.left<1||range.bottom>1048576||range.right>16384||(range.bottom-range.top+1)*(range.right-range.left+1)>5000)throw new Error('Список слишком большой или имеет неверный диапазон');
  const options=[];for(let r=range.top;r<=range.bottom;r++)for(let c=range.left;c<=range.right;c++){
   const value=getCell(ref.name,address(r,c));if(value==null||value==='')continue;
   options.push({text:typeof value==='boolean'?(value?'TRUE':'FALSE'):String(value),value});
  }
  return options;
 }
 try{
  const options=resolve(rule.formula),seen=new Set();
  if(!options.length)return {options:null,reason:'Список недоступен: источник пуст'};
  return {options:options.filter(option=>{if(seen.has(option.text))return false;seen.add(option.text);return true;}),allowBlank:!['0','false',false].includes(rule.allowBlank)};
 }catch(error){return {options:null,reason:'Список недоступен: '+error.message};}
}
