"""Promote verified build outputs to versioned GitHub Release assets; never rebuild binaries."""
import hashlib
import re
import shutil
import sys
import tarfile
import xml.etree.ElementTree as ET
from pathlib import Path


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def prepare(source, output, version):
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Invalid release version')
    windows = [f'LunaArc-{version}-setup-win-x64.exe', f'LunaArc-{version}-portable-win-x64.zip']
    checksums = source / 'LunaArc-Windows-Checksums' / 'SHA256SUMS.txt'
    expected = {}
    for line in checksums.read_text(encoding='utf-8-sig').splitlines():
        if not line.strip():
            continue
        match = re.fullmatch(r'([0-9a-fA-F]{64})\s+\*?([^/\\]+)', line.strip())
        if not match or match[2] in expected:
            raise ValueError('Invalid or duplicate Windows checksum record')
        expected[match[2]] = match[1].lower()
    paths = []
    for edition, name in zip(('Setup', 'Portable'), windows):
        path = source / f'LunaArc-Windows-{edition}' / name
        if expected.get(name) != digest(path):
            raise ValueError(f'Windows checksum mismatch: {name}')
        paths.append(path)
    linux = source / 'linux'
    for name in ('LunaArc', 'luna-cli', 'LunaArc.7zz', 'EULA_License.txt', 'SevenZip_LICENSE.txt'):
        if not (linux / name).is_file():
            raise ValueError(f'Missing Linux package file: {name}')
    if output.exists():
        raise FileExistsError('Release output directory must be new')
    output.mkdir(parents=True)
    for path in paths:
        shutil.copyfile(path, output / path.name)
    linux_name = f'LunaArc-{version}-linux-x64.tar.gz'
    with tarfile.open(output / linux_name, 'w:gz') as archive:
        for path in sorted(linux.iterdir()):
            if not path.is_file() or path.is_symlink():
                raise ValueError('Unexpected Linux artifact entry')
            info = archive.gettarinfo(str(path), arcname=path.name)
            info.mode = 0o755 if path.name in ('LunaArc', 'luna-cli', 'LunaArc.7zz') else 0o644
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            with path.open('rb') as stream:
                archive.addfile(info, stream)
    assets = sorted(output.iterdir())
    (output / 'SHA256SUMS.txt').write_text(''.join(f'{digest(path)}  {path.name}\n' for path in assets), encoding='utf-8')


if __name__ == '__main__':
    prepare(Path(sys.argv[1]), Path(sys.argv[2]), ET.parse('Directory.Build.props').findtext('.//Version'))
