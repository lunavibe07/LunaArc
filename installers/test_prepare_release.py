import tempfile
import unittest
from pathlib import Path
import tarfile
from prepare_release import prepare, digest


class ReleaseChecks(unittest.TestCase):
    def test_verified_assets_and_executable_modes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for edition, suffix in (('Setup', 'setup-win-x64.exe'), ('Portable', 'portable-win-x64.zip')):
                path = root / f'LunaArc-Windows-{edition}' / f'LunaArc-2.1.0-{suffix}'
                path.parent.mkdir(); path.write_bytes(b'synthetic package ' + edition.encode())
            sums = root / 'LunaArc-Windows-Checksums' / 'SHA256SUMS.txt'
            sums.parent.mkdir()
            sums.write_text(''.join(f'{digest(path)}  {path.name}\n' for path in root.glob('LunaArc-Windows-*/*')))
            linux = root / 'linux'; linux.mkdir()
            for name in ('LunaArc', 'luna-cli', 'LunaArc.7zz', 'EULA_License.txt', 'SevenZip_LICENSE.txt'):
                (linux / name).write_bytes(b'synthetic Linux file')
            output = root / 'ready'; prepare(root, output, '2.1.0')
            self.assertEqual(len(list(output.iterdir())), 4)
            with tarfile.open(output / 'LunaArc-2.1.0-linux-x64.tar.gz') as archive:
                self.assertEqual(archive.getmember('LunaArc').mode, 0o755)
                self.assertEqual(archive.getmember('EULA_License.txt').mode, 0o644)
            self.assertEqual(len((output / 'SHA256SUMS.txt').read_text().splitlines()), 3)
            with self.assertRaises(FileExistsError): prepare(root, output, '2.1.0')
            setup = next(root.glob('LunaArc-Windows-Setup/*')); setup.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'checksum mismatch'): prepare(root, root / 'tampered', '2.1.0')
            self.assertFalse((root / 'tampered').exists())
            with self.assertRaises(ValueError): prepare(root, root / 'invalid', '../bad-version')


if __name__ == '__main__':
    unittest.main()
