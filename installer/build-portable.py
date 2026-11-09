"""Package a prebuilt portable application; never copy working macros."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
bin_dir = root / 'addin/portable/bin/Release/net48'
files = {
    'Fonts/SourceSans3-Bold.ttf': bin_dir / 'Fonts/SourceSans3-Bold.ttf',
    'Fonts/SourceSans3-Regular.ttf': bin_dir / 'Fonts/SourceSans3-Regular.ttf',
    'Fonts/LICENSE.md': bin_dir / 'Fonts/LICENSE.md',
    'SW-Assistant.exe': bin_dir / 'SW-Assistant.exe',
    'SW-Assistant.exe.config': bin_dir / 'SW-Assistant.exe.config',
    'SolidWorks.Interop.sldworks.dll': bin_dir / 'SolidWorks.Interop.sldworks.dll',
    'ИНСТРУКЦИЯ.txt': root / 'addin/portable/ИНСТРУКЦИЯ.txt',
}
for path in files.values():
    if not path.is_file():
        raise SystemExit(f'Missing build output: {path}')
output = root / 'installer/SW-Assistant-Portable.zip'
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name, path in files.items():
        archive.write(path, 'SW-Assistant/' + name)
    archive.writestr('SW-Assistant/Library/Macros/', '')
    for module in ('Избранное', 'Чертежи', 'Сборки', 'Лазер'):
        archive.writestr('SW-Assistant/Library/Macros/' + module + '/', '')
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None
    assert not any(n.lower().endswith('.swp') for n in archive.namelist())
    assert not any('webview' in n.lower() for n in archive.namelist())
# Keep the previously published download URL current.
(root / 'installer/SWToolsHub-Portable.zip').write_bytes(output.read_bytes())
(root / 'installer/PORTABLE-SHA256.txt').write_text(
    hashlib.sha256(output.read_bytes()).hexdigest() + '  SW-Assistant-Portable.zip\n' + hashlib.sha256(output.read_bytes()).hexdigest() + '  SWToolsHub-Portable.zip\n'
)
print(output)
