"""Compile supplied Excel formulas/data into a portable, auditable calculation model.
Usage: python import_workbooks.py <directory-with-five-xlsx> <output.json>
Requires openpyxl on the developer machine only; no Excel dependency at runtime.
"""
import sys,json,re,hashlib
from pathlib import Path
from openpyxl import load_workbook
from openpyxl.formula.tokenizer import Tokenizer

class Parser:
    precedence={'=':1,'<>':1,'<':1,'>':1,'<=':1,'>=':1,'&':2,'+':3,'-':3,'*':4,'/':4,'^':5}
    def __init__(self,f):self.ts=[t for t in Tokenizer(f).items if t.type!='WHITE-SPACE'];self.i=0
    def peek(self):return self.ts[self.i] if self.i<len(self.ts) else None
    def pop(self):t=self.peek();self.i+=1;return t
    def expr(self,p=0):
        t=self.pop()
        if t.type=='OPERATOR-PREFIX': n={'Op':'NEG' if t.value=='-' else 'POS','Args':[self.expr(6)]}
        elif t.type=='FUNC' and t.subtype=='OPEN':
            args=[]
            while self.peek().subtype!='CLOSE':
                args.append(self.expr())
                if self.peek().type=='SEP':self.pop()
                else:break
            assert self.pop().subtype=='CLOSE'
            n={'Op':t.value[:-1].upper().replace('_XLFN.',''),'Args':args}
        elif t.type=='PAREN' and t.subtype=='OPEN':
            n=self.expr();assert self.pop().subtype=='CLOSE'
        elif t.type=='OPERAND':
            if t.subtype=='NUMBER':n={'Op':'NUM','Number':float(t.value)}
            elif t.subtype=='TEXT':n={'Op':'STR','Text':t.value[1:-1].replace('""','"')}
            elif t.subtype=='LOGICAL':n={'Op':'BOOL','Number':int(t.value=='TRUE')}
            elif t.subtype=='RANGE':n={'Op':'REF','Text':t.value}
            else:raise ValueError(t)
        else:raise ValueError(t)
        while self.peek():
            t=self.peek()
            if t.type=='OPERATOR-POSTFIX':self.pop();n={'Op':'PERCENT','Args':[n]};continue
            if t.type!='OPERATOR-INFIX' or self.precedence[t.value]<p:break
            self.pop();q=self.precedence[t.value]
            n={'Op':t.value,'Args':[n,self.expr(q+1)]}
        return n

def compile_file(path):
    w=load_workbook(path);cached=load_workbook(path,data_only=True)
    d={'Title':path.stem.replace('Расчет ','',1),'Source':path.name,'Sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'Names':{k:v.attr_text for k,v in w.defined_names.items()},'Sheets':[]}
    reference={'Детали расчета','Справочники','Фланцы','Параметры метизов','Применяемые метизы','Списки','для болтов','для сварки','Материалы','Справочник высот'}
    for s in w:
        main=s.title not in reference
        cells=[]
        for row in s:
            for c in row:
                if c.value is None:continue
                formula=c.data_type=='f'
                item={'Address':c.coordinate,'Row':c.row,'Column':c.column,'Format':c.number_format,'Value':None if formula else c.value,'Formula':c.value if formula else None,'Expected':cached[s.title][c.coordinate].value if formula else None,'Editable':main and not formula and c.column==(3 if 'прочность' in path.name else 2)}
                if formula:
                    parser=Parser(c.value);item['Expression']=parser.expr();assert parser.i==len(parser.ts),c.value
                options=[]
                for v in s.data_validations.dataValidation:
                    if c.coordinate not in v or v.type!='list':continue
                    f=v.formula1
                    if f.startswith('"'):options=f.strip('"').split(',')
                    elif f in w.defined_names:
                        for sn,rg in w.defined_names[f].destinations:
                            options=[str(x.value) for rr in w[sn][rg] for x in rr if x.value is not None]
                if main and 'прочность' in path.name and c.column==3 and isinstance(c.value,str):
                    source='для сварки' if s.title in ['Угловые швы','Стыковые швы'] else 'для болтов'
                    col='A' if c.coordinate=='C10' and s.title=='Угловые швы' else ('F' if c.coordinate=='C11' else ('B' if source=='для сварки' else 'D'))
                    options=list(dict.fromkeys(str(w[source][f'{col}{r}'].value) for r in range(2,15) if w[source][f'{col}{r}'].value is not None))
                if options:item['Options']=options
                cells.append(item)
        d['Sheets'].append({'Name':s.title,'ReferenceOnly':not main,'Cells':cells,'Merged':[str(r) for r in s.merged_cells.ranges]})
    return d
if __name__=='__main__':
    paths=list(Path(sys.argv[1]).rglob('*.xlsx'))
    order=['толщины','длины','балки','прочность','высоты']
    paths=sorted(paths,key=lambda p:next(i for i,x in enumerate(order) if x in p.name))
    books=[compile_file(p) for p in paths]
    assert len(books)==5
    Path(sys.argv[2]).write_text(json.dumps(books,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print('Imported',len(books),'workbooks;',sum(c['Formula'] is not None for b in books for s in b['Sheets'] for c in s['Cells']),'formulas')
