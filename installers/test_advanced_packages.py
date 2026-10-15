"""Run real published CLI/SFX executables against disposable fixtures on Windows/Linux."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

published = Path(sys.argv[1]).resolve()
cli = published / ("luna-cli.exe" if os.name == "nt" else "luna-cli")
env = os.environ.copy()
env.pop("LUNAARC_7ZIP_PATH", None)  # Verify the encoder shipped with this package.


def run(executable, *args, success=True):
    result = subprocess.run([str(executable), *map(str, args)], env=env, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
    if (result.returncode == 0) != success:
        raise AssertionError(f"Unexpected exit {result.returncode}: {executable.name}\n{result.stdout}\n{result.stderr}")
    return result


with tempfile.TemporaryDirectory(prefix="LunaArc_Published_") as temporary:
    root = Path(temporary)
    source = root / "source"
    source.mkdir()
    (source / "日本語.txt").write_text("Synthetic standalone extractor fixture", encoding="utf-8")
    (source / "same.txt").write_text("Synthetic standalone extractor fixture", encoding="utf-8")
    for command, extension in [("pack-zip", ".zip"), ("pack-larc", ".larc"), ("pack-7z", ".7z")]:
        archive = root / (command + extension)
        flags = ["--solid", "on"] if command == "pack-7z" else []
        run(cli, command, source, "-o", archive, "--recovery", "on", "--lock", "on", *flags)
        assert Path(str(archive) + ".lrec").is_file() and Path(str(archive) + ".lunalock").is_file()
        run(cli, "test", archive)
        run(cli, "unlock", archive)
        sfx = root / (command + (".exe" if os.name == "nt" else ".run"))
        run(cli, "make-sfx", archive, "-o", sfx)
        alone = root / (command + "-standalone")
        alone.mkdir()
        standalone = alone / sfx.name
        shutil.copy2(sfx, standalone)
        destination = alone / "extracted"
        run(standalone, "-o", destination)
        assert (destination / "source" / "日本語.txt").read_bytes() == (source / "日本語.txt").read_bytes()
        (destination / "source" / "same.txt").write_text("Keep existing file", encoding="utf-8")
        run(standalone, "-o", destination)
        assert (destination / "source" / "same.txt").read_text(encoding="utf-8") == "Keep existing file"
        expected = hashlib.sha256(archive.read_bytes()).digest()
        with archive.open("r+b") as stream:
            first = stream.read(1)
            stream.seek(0)
            stream.write(bytes([first[0] ^ 0x44]))
        repaired = root / ("repaired-" + archive.name)
        run(cli, "repair", archive, "-o", repaired)
        assert hashlib.sha256(repaired.read_bytes()).digest() == expected
        run(cli, "test", repaired)
        run(cli, "repair", archive, "-o", archive, success=False)
        if command == "pack-zip":
            run(cli, "recovery-record", sfx)
            expected_sfx = hashlib.sha256(sfx.read_bytes()).digest()
            with sfx.open("r+b") as stream:
                stream.seek(-70, 2)
                value = stream.read(1)
                stream.seek(-1, 1)
                stream.write(bytes([value[0] ^ 1]))
            repaired_sfx = root / ("repaired-sfx.exe" if os.name == "nt" else "repaired-sfx.run")
            run(cli, "repair", sfx, "-o", repaired_sfx)
            assert hashlib.sha256(repaired_sfx.read_bytes()).digest() == expected_sfx
            run(repaired_sfx, "-o", root / "repaired-sfx-output")

    # Password creation and two-layer TAR GZip compatibility in actual published packages.
    fake_password = "synthetic-published-暗号"
    for command, extension in [("pack-larc", ".larc"), ("pack-zip", ".zip"), ("pack-7z", ".7z"), ("pack-targz", ".tar.gz.7z")]:
        encrypted = root / (command + "-encrypted" + extension)
        run(cli, command, source, "-o", encrypted, "-p", fake_password)
        run(cli, "test", encrypted, "-p", fake_password)
        run(cli, "test", encrypted, "-p", "wrong-synthetic-password", success=False)
        unpacked = root / (command + "-decrypted")
        run(cli, "extract-here", encrypted, "-o", unpacked, "-p", fake_password)
        assert (unpacked / "source" / "same.txt").read_text() == "Synthetic standalone extractor fixture"
        encrypted_sfx = root / (command + "-encrypted-sfx" + (".exe" if os.name == "nt" else ".run"))
        run(cli, command, source, "-o", encrypted_sfx, "-p", fake_password, "--sfx", "on")
        # pack uses the GUI stub; make-sfx creates a command-line stub for unattended execution.
        cli_sfx = root / (command + "-encrypted-cli-sfx" + (".exe" if os.name == "nt" else ".run"))
        run(cli, "make-sfx", encrypted, "-o", cli_sfx, "-p", fake_password)
        run(cli_sfx, "-o", root / (command + "-sfx-decrypted"), "-p", fake_password)
    print("PASS: published AES ZIP/7z and encrypted TAR GZip wrapper creation, wrong passwords and round trips.")

    # Exercise the published CLI's split option and automatic read path.
    split_source = root / "split-source.bin"
    split_source.write_bytes(os.urandom(2 * 1024 * 1024 + 73))
    for command, extension, key in [("pack-larc", ".larc", fake_password), ("pack-zip", ".zip", None)]:
        split_base = root / ("published-split-" + command + extension)
        password_args = ["-p", key] if key else []
        run(cli, command, split_source, "-o", split_base, "--volume-mib", "1", *password_args)
        first = Path(str(split_base) + ".001")
        assert first.is_file() and Path(str(split_base) + ".lunasplit.json").is_file()
        run(cli, "test", first, *password_args)
        split_out = root / ("split-output-" + command)
        run(cli, "extract-here", first, "-o", split_out, *password_args)
        assert hashlib.sha256((split_out / split_source.name).read_bytes()).digest() == hashlib.sha256(split_source.read_bytes()).digest()
    print("PASS: published LARC v2 passwords/SFX and numbered split-set CLI round trips.")

    # A full GUI extractor is also built and verified without opening an OS window.
    gui_sfx = root / ("gui-extractor.exe" if os.name == "nt" else "gui-extractor.run")
    run(cli, "pack-7z", source, "-o", gui_sfx, "--solid", "on", "--sfx", "on", "--recovery", "on", "--lock", "on")
    run(cli, "test", gui_sfx)
    run(cli, "unlock", gui_sfx)
    # Execute the actual GUI startup/lifetime for extraction-only shell commands.
    # Linux uses a disposable X server; these commands require no interactive dialogs.
    gui = published / ("LunaArc.exe" if os.name == "nt" else "LunaArc")
    xvfb = shutil.which("xvfb-run") if os.name != "nt" else None
    if os.name != "nt" and not xvfb:
        raise RuntimeError("Published GUI extraction checks require xvfb-run on Linux.")
    for action in ("--shell-extract-here", "--shell-extract-folder"):
        directory = root / action.removeprefix("--shell-")
        directory.mkdir()
        archive = directory / "sample.zip"
        with zipfile.ZipFile(archive, "w") as target:
            target.writestr("standalone.txt", "Synthetic GUI extraction startup/lifetime fixture")
        if xvfb:
            run(Path(xvfb), "-a", gui, action, archive)
        else:
            run(gui, action, archive)
        destination = directory if action.endswith("here") else directory / "sample"
        assert (destination / "standalone.txt").read_text() == "Synthetic GUI extraction startup/lifetime fixture"
    print("PASS: published GUI Extract Here/folder completed and exited without user interaction.")
    print("PASS: published encoder, ZIP/LARC/solid 7z standalone SFX execution, skip-existing extraction, recovery, lock/unlock and GUI SFX payload checks.")
