// Presentation only: these helpers never evaluate a formula.
const defaults = ['FFFFFF','000000','EEECE1','1F497D','4F81BD','C0504D','9BBB59','8064A2','4BACC6','F79646','0000FF','800080'];
const themeNames = ['lt1','dk1','lt2','dk2','accent1','accent2','accent3','accent4','accent5','accent6','hlink','folHlink'];
const indexed = ('000000 FFFFFF FF0000 00FF00 0000FF FFFF00 FF00FF 00FFFF ' +
  '000000 FFFFFF FF0000 00FF00 0000FF FFFF00 FF00FF 00FFFF ' +
  '800000 008000 000080 808000 800080 008080 C0C0C0 808080 ' +
  '9999FF 993366 FFFFCC CCFFFF 660066 FF8080 0066CC CCCCCC ' +
  '000080 FF00FF FFFF00 00FFFF 800080 800000 008080 0000FF ' +
  '00CCFF CCFFFF CCFFCC FFFF99 99CCFF FF99CC CC99FF FFCC99 ' +
  '3366FF 33CCCC 99CC00 FFCC00 FF9900 FF6600 666699 969696 ' +
  '003366 339966 003300 333300 993300 993366 333399 333333').split(' ');

export function readTheme(xml, Parser) {
  const theme={colors:[...defaults],minor:'Calibri',major:'Cambria'};
  if(!xml)return theme;
  let colorIndex=-1, fontGroup=null;
  const parser=new Parser();
  parser.on('opentag',node=>{
    const name=node.name.split(':').pop();
    if(themeNames.includes(name))colorIndex=themeNames.indexOf(name);
    if(colorIndex>=0 && ['srgbClr','sysClr'].includes(name)){
      const value=node.attributes.val==='windowText'?'000000':node.attributes.lastClr||node.attributes.val;
      if(/^[\da-f]{6}$/i.test(value))theme.colors[colorIndex]=value;
    }
    if(name==='minorFont')fontGroup='minor';if(name==='majorFont')fontGroup='major';
    if(name==='latin'&&fontGroup&&node.attributes.typeface)theme[fontGroup]=node.attributes.typeface;
  });
  parser.on('closetag',node=>{
    const name=node.name.split(':').pop();
    if(themeNames.includes(name))colorIndex=-1;
    if(name==='minorFont'||name==='majorFont')fontGroup=null;
  });
  parser.write(xml).close();return theme;
}

export function excelColor(value, theme, fallback='#000000') {
  let hex=value?.argb?.slice(-6) || theme.colors[value?.theme] || indexed[value?.indexed];
  if(!/^[\da-f]{6}$/i.test(hex||''))return fallback;
  // Excel tint changes HSL luminance, keeping hue and saturation intact.
  const tint=Math.max(-1,Math.min(1,Number(value?.tint)||0));
  if(tint){
    const rgb=hex.match(/../g).map(c=>parseInt(c,16)/255), max=Math.max(...rgb), min=Math.min(...rgb);
    const luminance=(max+min)/2, chroma=max-min;
    const next=tint<0?luminance*(1+tint):luminance*(1-tint)+tint;
    const scale=chroma ? (1-Math.abs(2*next-1))/(1-Math.abs(2*luminance-1)) : 0;
    hex=rgb.map(c=>Math.round(255*(next+(c-luminance)*scale)).toString(16).padStart(2,'0')).join('');
  }
  return '#'+hex;
}

export function fontStyle(font={}, theme) {
  const result={};
  if(font.name||font.scheme)result.fontFamily=JSON.stringify(font.scheme?theme[font.scheme]||font.name:font.name)+', Calibri, Arial, sans-serif';
  if(font.size)result.fontSize=font.size+'pt';
  if(font.bold)result.fontWeight='700';if(font.italic)result.fontStyle='italic';
  if(font.color)result.color=excelColor(font.color,theme);
  const decorations=[];if(font.underline)decorations.push('underline');if(font.strike)decorations.push('line-through');
  if(decorations.length)result.textDecoration=decorations.join(' ');
  if(font.vertAlign==='superscript'||font.vertAlign==='subscript'){result.verticalAlign=font.vertAlign==='superscript'?'super':'sub';result.fontSize='smaller';}
  return result;
}

export function cellStyle(style={}, theme) {
  const result=fontStyle(style.font,theme), align=style.alignment||{};
  if(style.fill?.type==='pattern'&&style.fill.pattern==='solid')result.backgroundColor=excelColor(style.fill.fgColor,theme,'#ffffff');
  const horizontal={left:'left',center:'center',right:'right',justify:'justify',distributed:'justify',centerContinuous:'center'}[align.horizontal];
  if(horizontal)result.textAlign=horizontal;
  result.verticalAlign={top:'top',middle:'middle',bottom:'bottom',justify:'middle',distributed:'middle'}[align.vertical]||'bottom';
  result.whiteSpace=align.wrapText?'pre-wrap':'pre';result.overflowWrap=align.wrapText?'anywhere':'normal';
  if(align.indent)result.paddingLeft=(3+align.indent*10)+'px';
  for(const side of ['top','right','bottom','left']){
    const border=style.border?.[side];if(!border?.style)continue;
    const kind=border.style==='double'?'double':/dash/i.test(border.style)?'dashed':/dot|hair/i.test(border.style)?'dotted':'solid';
    const width=border.style==='thick'||border.style==='double'?3:/medium/i.test(border.style)?2:1;
    result['border'+side[0].toUpperCase()+side.slice(1)]=`${width}px ${kind} ${excelColor(border.color,theme)}`;
  }
  return result;
}

export function columnPixels(width=8.43) {
  return width<1?Math.round(width*12):Math.floor(width*7+5);
}

export function formattedValue(raw, numFmt, date1904, formatter) {
  if(!numFmt||numFmt==='General'||typeof raw!=='number')return null;
  // SSF requires literal dots between date tokens to be escaped; Excel does not.
  const format=numFmt.replace(/"[^"]*"|\\.|([dmy])\.(?=[dmy])/gi,(match,date)=>date?date+'\\.':match);
  try{return formatter.format(format,raw,{date1904});}catch{return null;}
}
