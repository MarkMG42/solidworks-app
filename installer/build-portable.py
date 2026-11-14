"""Package a freshly built portable application, runtime DLLs and licenses."""
from pathlib import Path
import argparse,hashlib,subprocess,zipfile
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=root/'installer/SW-Assistant-Portable.zip');args=parser.parse_args()
bin_dir=root/'addin/portable/bin/Release/net48'
files={
 'РАСЧЕТЫ.txt':root/'installer/calculations/РАСЧЕТЫ.txt',
 'Fonts/SourceSans3-Semibold.ttf':bin_dir/'Fonts/SourceSans3-Semibold.ttf',
 'Fonts/SourceSans3-Regular.ttf':bin_dir/'Fonts/SourceSans3-Regular.ttf',
 'Fonts/LICENSE.md':bin_dir/'Fonts/LICENSE.md',
 'SW-Assistant.exe':bin_dir/'SW-Assistant.exe',
 'SW-Assistant.exe.config':bin_dir/'SW-Assistant.exe.config',
 'ИНСТРУКЦИЯ.txt':root/'addin/portable/ИНСТРУКЦИЯ.txt',
}
for file in sorted(bin_dir.glob('*.dll')):files[file.name]=file
for file in sorted((root/'installer/licenses').glob('*.txt')):files['Licenses/'+file.name]=file
for name in ['ClosedXML.dll','ClosedXML.Parser.dll','DocumentFormat.OpenXml.dll','Newtonsoft.Json.dll','SolidWorks.Interop.sldworks.dll']:
 if name not in files:raise SystemExit('Missing runtime DLL: '+name)
for path in files.values():
 if not path.is_file():raise SystemExit('Missing build output: '+str(path))
if not any(name.startswith('Licenses/') for name in files):raise SystemExit('Runtime licenses are missing')
output=args.output;output.parent.mkdir(parents=True,exist_ok=True)
commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
 for name,path in files.items():archive.write(path,'SW-Assistant/'+name)
 archive.writestr('SW-Assistant/VERSION.txt','SW-Assistant Portable\nSource: '+commit+'\nWindows x64, .NET Framework 4.8\n')
 archive.writestr('SW-Assistant/Library/Macros/','')
 for module in ('Избранное','Чертежи','Сборки','Лазер'):archive.writestr('SW-Assistant/Library/Macros/'+module+'/','')
with zipfile.ZipFile(output) as archive:
 assert archive.testzip() is None
 assert not any(name.lower().endswith('.swp') for name in archive.namelist())
 assert not any('webview' in name.lower() for name in archive.namelist())
 assert archive.read('SW-Assistant/SW-Assistant.exe')==(bin_dir/'SW-Assistant.exe').read_bytes()
 for name,path in files.items():assert archive.read('SW-Assistant/'+name)==path.read_bytes(),name
checksum=output.parent/('PORTABLE-SHA256.txt' if output.name=='SW-Assistant-Portable.zip' else output.name+'.sha256')
checksum.write_text(hashlib.sha256(output.read_bytes()).hexdigest()+'  '+output.name+'\n')
print(output)
print(checksum.read_text().strip())
