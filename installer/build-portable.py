"""Package a prebuilt portable application; never copy working macros."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
bin_dir = root / 'addin/portable/bin/Release/net48'
files = {
    'SWToolsHub.exe': bin_dir / 'SWToolsHub.exe',
    'SWToolsHub.exe.config': bin_dir / 'SWToolsHub.exe.config',
    'SolidWorks.Interop.sldworks.dll': bin_dir / 'SolidWorks.Interop.sldworks.dll',
    'ИНСТРУКЦИЯ.txt': root / 'addin/portable/ИНСТРУКЦИЯ.txt',
}
for path in files.values():
    if not path.is_file():
        raise SystemExit(f'Missing build output: {path}')
output = root / 'installer/SWToolsHub-Portable.zip'
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name, path in files.items():
        archive.write(path, 'SWToolsHub/' + name)
    archive.writestr('SWToolsHub/Library/Macros/', '')
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None
    assert not any(n.lower().endswith('.swp') for n in archive.namelist())
    assert not any('webview' in n.lower() for n in archive.namelist())
(root / 'installer/PORTABLE-SHA256.txt').write_text(
    hashlib.sha256(output.read_bytes()).hexdigest() + '  SWToolsHub-Portable.zip\n'
)
print(output)
