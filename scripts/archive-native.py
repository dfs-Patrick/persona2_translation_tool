"""Create one distribution ZIP per OS, preserving Unix executable modes."""
import os
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
target = os.environ.get('P2_EXE_TARGET', 'node22-win-x64')
names = {'node22-win-x64': 'Windows', 'node22-linux-x64': 'Linux'}
if target not in names:
    raise SystemExit(f'Unsupported target: {target}')
folder = root / 'release' / f'p2-tool-exe-{target}'
if not folder.is_dir():
    raise SystemExit(f'Build the executable first: {folder}')
archive = root / 'release' / f'Persona2Tool-{names[target]}-x64.zip'
with ZipFile(archive, 'w', ZIP_DEFLATED) as output:
    for source in sorted(folder.rglob('*')):
        output.write(source, Path('Persona2Tool') / source.relative_to(folder))
with ZipFile(archive) as output:
    if output.testzip() is not None:
        raise SystemExit('ZIP validation failed')
print(archive)
