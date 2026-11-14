"""Create a separate portable delivery from the freshly rebuilt canonical archive."""
from pathlib import Path
import hashlib,shutil,zipfile
root=Path(__file__).resolve().parents[2]
here=Path(__file__).resolve().parent
source=root/'installer/SW-Assistant-Portable.zip'
target=here/'SW-Assistant-Calculations.zip'
shutil.copy2(source,target)
shutil.rmtree(here/'Portable',ignore_errors=True)
with zipfile.ZipFile(target) as z:
    assert z.testzip() is None
    for entry in z.infolist():
        assert '..' not in Path(entry.filename).parts and not Path(entry.filename).is_absolute()
        dest=here/'Portable'/entry.filename
        if entry.is_dir(): dest.mkdir(parents=True,exist_ok=True)
        else:
            dest.parent.mkdir(parents=True,exist_ok=True)
            dest.write_bytes(z.read(entry.filename))
(here/'SHA256.txt').write_text(hashlib.sha256(target.read_bytes()).hexdigest()+'  '+target.name+'\n')
print(target)
