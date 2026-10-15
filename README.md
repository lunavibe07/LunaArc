# LunaArc Archiver

<img src="branding/app_logo_256.png" alt="LunaArc logo" width="96" height="96" />

LunaArc is a Windows and Linux archive application built with .NET 8. It includes an Avalonia desktop interface and a command-line tool that share the same archive backend.

## Download

Version **1.4.0**, adding SFX, solid 7z, recovery companions and advisory archive locks. Published standalone SFX execution and archive/UI checks passed natively on Windows and Linux; Windows Setup/Portable lifecycle checks also passed. See the [verified build run](https://github.com/lunavibe07/LunaArc/actions/runs/38682293226).

| Edition | Download | Getting started |
| --- | --- | --- |
| Windows x64 Setup | [Download installer](https://github.com/lunavibe07/LunaArc/actions/runs/38682293226/artifacts/11948309029) | Extract the downloaded artifact ZIP, then run `LunaArc-1.4.0-setup-win-x64.exe`. |
| Windows x64 Portable | [Download portable](https://github.com/lunavibe07/LunaArc/actions/runs/38682293226/artifacts/11947939168) | Extract the artifact ZIP and the portable ZIP inside it, then run `LunaArc.exe`. |
| Linux x64 | [Download Linux](https://github.com/lunavibe07/LunaArc/actions/runs/38682293226/artifacts/11947943960) | Extract the ZIP, run `chmod +x LunaArc luna-cli LunaArc.7zz`, then start `./LunaArc`. |
| Windows SHA-256 checksums | [Download checksums](https://github.com/lunavibe07/LunaArc/actions/runs/38682293226/artifacts/11948438834) | Extract `SHA256SUMS.txt` to verify the Setup EXE or portable ZIP. |

Sign in to GitHub to download Actions artifacts. These links are currently scheduled to expire on **January 13, 2027**. For newer builds, open [Build and verify](https://github.com/lunavibe07/LunaArc/actions/workflows/build.yml), choose a successful run and scroll to **Artifacts**. Linux Studio requires a graphical desktop and the system libraries listed under [Requirements](#requirements).

## Projects

| Project | Purpose |
| --- | --- |
| `src/LunaArc.Core` | Archive formats, virtual directory index, streaming, encryption and safe extraction paths |
| `src/LunaArc.Shell` | Per-user Windows Explorer integration and Linux desktop/Nautilus scripts |
| `src/LunaArc.Cli` | `luna-cli` command-line executable |
| `src/LunaArc.Studio` | `LunaArc` desktop application using Avalonia and MVVM |
| `tests/LunaArc.SmokeTests` | Executable integration checks using synthetic archive fixtures |

Core uses SharpCompress 0.50.5 and ZstdSharp.Port 0.8.1. Published packages include the separate 7-Zip 26.04 encoder; see [license](branding/SevenZip_LICENSE.txt), [LGPL](branding/SevenZip_COPYING.txt) and [source/notice](branding/SevenZip_NOTICE.txt). Studio uses Avalonia 11.1.3 and CommunityToolkit.Mvvm 8.2.2. Standard TAR and TAR GZip handling uses the .NET TAR APIs.

## Desktop interface

Since version 1.1, LunaArc uses a light, classic desktop layout: menu bar, large toolbar icons, archive path, resizable/sortable file columns and a status bar. The layout is inspired by familiar archive managers while retaining LunaArc branding.

The supplied Luna logo is used for application windows, Windows executable/Setup/Explorer icons and the Linux launcher. Open **Help > About LunaArc** to see it in the application. See [branding assets](branding/README.md) for the source and derived icon files.

- **Open** loads an archive; **Add** selects files and opens the [General compression dialog](#create-an-archive) with the output name, format, method, password and comment controls.
- **Extract To**, **Test** and **View** use the existing archive backend. Archive-only commands stay disabled until an archive is open; View also requires a selection.
- **Extract To** and Explorer's **Extract files...** share the [extraction path/options dialog](#extract-files), with destination browsing, a folder tree and password controls.
- Double-click a folder to browse it; **Up** returns to its parent. Double-click a file to preview it.
- **Find** / **F3** filters names in the current folder without changing archive contents. Size columns sort by numeric byte counts.
- **Info** shows the current archive path, folder/file summary and available comment. **Options** opens compression/password settings; cancelling restores the previous settings.
- **File** includes close archive and exit; **Tools** contains context-menu installation. **Ctrl+O** opens an archive.

The interface lists archive contents; it is not a general system-drive file browser. Tools includes repair using a LunaArc .lrec companion and explicit lock/unlock. Archive entry editing is not implemented. Both installed and portable Windows editions have this same interface. Toolbar artwork comes from Microsoft Fluent UI System Icons; its MIT notice is included in every published folder.

## Create an archive

Version **1.3.4** introduces a dark **Archive name and parameters** dialog containing only **General**, shared by toolbar **Add** and Explorer's **Add to archive... / Add to ZIP archive...**.

1. Select files using **Add**, or right-click a file/folder in Explorer and choose a LunaArc compression command.
2. Enter a full **Archive name**, or use **Browse...**. The suggested name is unused; select another name if the destination already exists. Save outside selected source folders.
3. Choose **LARC**, **ZIP**, **TAR GZip** or **7z** and a **Compression method** from Store through Best. Selecting a format updates the file extension.
4. Choose the supported **Archiving options** below. **Test archived files** verifies contents before the completed file is placed at the requested output path.
5. Use **Set password...** for LARC encryption. Enter and confirm the password; **Show password** reveals the fields. Both fields can be left blank to remove the password. ZIP/TAR GZip/7z password creation is not supported and is rejected.
6. Expand **Archive comment** to enter an optional LARC/ZIP comment. TAR GZip and 7z comments are not supported.
7. Select **OK** to create the archive and open it in LunaArc. **Cancel** discards draft changes. Sources are kept unless deletion was explicitly selected for this creation. **Help** explains the controls.

Version **1.4.0** implements all six archiving options, with format/platform limits shown in General:

| Archiving option | Availability |
| --- | --- |
| Delete files after archiving | Supported for actual creation; off by default for every operation. Forces archive testing. |
| Create SFX archive | Published self-contained packages; embeds a LunaArc extractor for the current OS and architecture. |
| Create solid archive | 7z output only; genuine solid LZMA2 compression via the packaged 7-Zip 26.04 encoder. |
| Add recovery record | Writes a .lrec companion with SHA-256 block checks and XOR parity. |
| Test archived files | Supported; required and locked on while source deletion is selected. |
| Lock archive | Writes a .lunalock companion and marks the archive read-only. Explicit unlock is under Tools. |

**Delete files after archiving** first captures the selected sources, verifies and publishes the archive, extracts a verification copy and compares SHA-256 content hashes with the captured and current sources. Matching files are moved to private sibling names, checked again, then deleted. Changed, new or unarchived sources are kept; only represented empty directories are removed. This requires extra time and temporary extraction space. Links/reparse points, overlapping inputs, duplicate archive names and filesystem-root deletion are rejected. If a changed source cannot be restored to its original path, its retained location is reported. Cancelling during cleanup stops further deletion; previously removed files remain available in the completed archive.

**Archive mode** currently creates new archives. Updating/synchronizing existing archives, RAR creation and custom dictionary controls are not available. Compression methods use each format's presets; ZIP and GZip map several presets to the same engine level.

When opened from **Options**, General edits defaults for the next compression operation; output editing, source deletion, SFX, recovery and locking are disabled, and OK does not modify an archive already open. New archive creation in Studio uses a temporary output, optionally verifies it, and moves it into place without replacing an existing file. Failure/cancellation before publication cleans up that temporary output and preserves sources. Source deletion is a one-operation opt-in and is never saved as a default.

## SFX, solid compression, recovery and locking

- **Solid:** choose **7z**, then **Create solid archive**. Related files share a compression dictionary. Non-solid 7z remains available. The separate unmodified 7-Zip encoder is pinned by SHA-256 in the publish scripts; managed SharpCompress performs catalog reading, testing and guarded extraction. Staging uses additional disk space; LZMA2 uses a 32 MiB dictionary and two encoder threads. Pause applies before/after the external encoding phase; Cancel terminates the encoder. 7z creation currently has no password, comment or split-volume option. LARC v1 bytes and encryption are unchanged.
- **SFX:** produces a Windows `.exe` or Linux `.run` containing the self-contained extractor plus an archive payload. It adds roughly the size of the published executable. Recipients need no LunaArc installation or external 7-Zip encoder, but the executable must match their OS/architecture and Linux GUI dependencies still apply. Running the GUI SFX shows the extraction options; it never launches extracted files. Use only executables from a trusted sender. The versioned footer validates offsets, format and SHA-256 payload integrity; the hash is not a publisher signature. On Linux, downloaded/copied executables may need `chmod +x`. Development builds disable SFX creation because their executable is not self-contained.
- **Recovery:** keep `<archive>.lrec` with the exact archive it was generated from. **Tools > Repair archive using .lrec...** selects a damaged archive, verifies the companion, reconstructs content and saves to a **new path**, preserving the original. It can repair **one damaged or missing 1 MiB block per group of 16 blocks**, including a truncated final block, and verifies the final original SHA-256 hash. Two damaged blocks in the same group exceed capacity; insertion/deletion that shifts later bytes can also exceed capacity. The record costs about 6.25% plus block hashes and rounding (small archives need at least a 1 MiB parity block). Limits: 1,000,000 data blocks; repair uses about 17 MiB of block buffers plus up to 32 MiB of block hashes. Corrupt/truncated records are rejected. This is LunaArc's versioned sidecar format, not WinRAR/RAR recovery records or PAR2, and hashes do not authenticate the sender.
- **Lock:** keep `<archive>.lunalock` beside the archive. If the archive is renamed, rename its companion to match; move both files together. The record stores a SHA-256 fingerprint and blocks LunaArc writers even if the archive itself was removed. **Tools > Unlock current archive** or `luna-cli unlock` explicitly removes it and clears read-only. Reading/extracting remains available. This is advisory protection in LunaArc, **not encryption or an access-control boundary**; other applications can clear attributes or remove companions. LunaArc creation always requires a new output name, including after unlocking.

Recovery and lock companions are separate files; keep and distribute them with the archive. These features do not create RAR archives or modify existing ZIP/TAR/LARC layouts. SFX/recovery creation forces archive testing. Requested companions are prepared before completion; a failed creation preserves sources. Source deletion starts only after the completed output and requested companions are published.

```text
luna-cli pack-7z ./source -o ./archive.7z --solid on --recovery on --lock on
luna-cli pack-zip ./source -o ./extractor.exe --sfx on
luna-cli recovery-record ./archive.zip
luna-cli repair ./archive.zip -o ./repaired.zip
luna-cli unlock ./archive.zip
luna-cli make-sfx ./archive.zip -o ./extractor.exe
extractor.exe -o ./destination
```

`--sfx on` in pack commands embeds the GUI extractor. `make-sfx` uses the published CLI itself for a standalone command-line extractor; the last example applies to that CLI extractor. Use `.run` and `./extractor.run` on Linux. Pack options accept explicit `on` / `off` values. Existing destinations are never overwritten.

## Extract files

The dark extraction dialog includes a folder tree and **General / Password** tabs in both Setup and Portable editions. Version **1.3.3** adds a separate **Update mode** group with all three file-selection policies below.

1. Open an archive and select **Extract To**, or right-click an archive in Windows Explorer and choose **LunaArc > Extract files...**. On Windows 11, the classic menu may be under **Show more options**.
2. In **General**, enter a full destination path, select a folder in the tree, or use **Browse...**. Expand a tree item to load its subfolders.
3. Use **New folder** to create a folder inside the current destination. A destination that does not yet exist is created during extraction.
4. Select an **Update mode** to choose eligible files, then an **Overwrite mode** to handle eligible conflicts. **Extract and replace files** and **Skip existing files** are the defaults. Select **Ask before overwrite** or **Overwrite without prompt** when you want to update older copies.
5. For an encrypted archive, enter its password in the **Password** tab. Leave it blank to reuse the password already unlocked for this archive. **Show password** reveals the input; closing the dialog clears it.
6. Optionally enable **Display files after extraction** to open the destination in the system file manager after a successful extraction.
7. Select **OK** to start, **Cancel** to close the options, or **Help** for a short explanation of the controls.

| Update mode | Eligible files |
| --- | --- |
| Extract and replace files | All archive files; conflicts are handled by Overwrite mode. |
| Extract and update files | Missing files and files whose archive copy is newer than the destination copy. |
| Freshen existing files only | Only files already present at the destination whose archive copy is newer. Missing files and directories are not created. |

Update and Freshen preserve files with equal or newer destination timestamps. If an archive file has no usable modification timestamp, these modes preserve its existing destination copy. The source archive is preserved. Comparison follows the timestamp precision available in the format; ZIPs created by LunaArc retain source modification times using local DOS timestamps with two-second precision and the ZIP date range of 1980–2107.

| Overwrite mode | Behavior for eligible conflicts |
| --- | --- |
| Ask before overwrite | For each conflict, choose **Overwrite**, **Skip**, **Rename** or **Cancel**. Cancel stops the extraction job. |
| Overwrite without prompt | Replace existing destination files. |
| Skip existing files | Preserve existing files and extract files that are not already present. |
| Rename automatically | Keep both copies, using a name such as `readme (1).txt` for the extracted copy. |

**Skip existing files** also skips older copies selected by Update or Freshen. **Rename automatically** writes a renamed copy of each eligible conflicting file. Update mode filters files before prompting or renaming.

**New folder** takes effect immediately. Cancelling does not delete folders you created or roll back files already extracted. The source archive is preserved.

## Password recovery and encrypted vault

Version 1.2 adds **Tools > Recover password...**, available even when a protected archive cannot be opened.

1. Browse to the ZIP, RAR, 7z or LARC archive.
2. Choose **Password list / unlocked vault** and enter one password per line, or choose **Mask pattern**.
3. In a mask, `?` means a digit (0-9), `#` means a lowercase ASCII letter (a-z), and other characters are literal. Backslash escapes the next character: `Luna202?` tries ten candidates; `Luna\?` tries the literal password `Luna?`.
4. Select **Start**. The dialog shows attempts and average speed, with **Pause / Resume** and **Cancel**. Pause stops new attempts; attempts already running may finish. A maximum of two workers process candidates without allocating the whole mask space.
5. A verified match enables **Open archive**. The password is hidden until **Show** is selected and is retained only for this archive session, including testing, extraction and preview. It does not populate the new-archive compression password.

Search is limited to 1,000,000 candidates and verification to 10,000 catalog entries / 256 MiB of declared uncompressed output. These metadata limits are not comprehensive archive-bomb protection. Recovery can be slow because each candidate is checked against archive contents; LARC integrity testing uses disposable temporary extraction files. No candidate match can also mean damaged data. Recovery is not a general encryption bypass, does not guarantee a result, and currently has no GPU acceleration or saved search checkpoints.

To use the vault, enter a master password and select **Save list** to save the current list, or **Load vault** to replace the list with saved candidates. Select **Start** to try the unlocked list. Vault operations clear the master-password input afterwards; closing the dialog clears the candidate and recovered-password fields. Managed strings cannot be guaranteed to disappear immediately from process memory.

The vault is stored at `ApplicationData/LunaArc/password_vault.enc`, using a versioned AES-256-GCM envelope, a fresh salt/nonce for every save, a 16-byte authentication tag and PBKDF2-SHA256 with 600,000 iterations. A wrong master password or altered data fails authentication. Saving an existing vault first verifies its master password and uses a temporary file plus replacement to preserve the original on failure. Linux files are created with owner-only read/write permissions. There is no master-password recovery. Any old `password_vault.json` is left untouched and is not read automatically.

The archive format remains compatible: no eight-byte password-check tag has been inserted into existing LARC headers. A quick password verifier cannot replace integrity validation. ZIP AES AE-2 has no payload CRC; LunaArc now validates its authentication code before testing or extracting. The additional encrypted ZIP validation currently rejects ZIP64 and split ZIP. See the [WinZip AES specification](https://www.winzip.com/en/support/aes-encryption/) and [.NET AES-GCM documentation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-8.0).

## Requirements

- .NET 8 SDK, version 8.0.400 or later in the 8.0 SDK family; `global.json` selects the latest installed compatible feature band.
- Network access for the first NuGet restore and runtime-specific publish.
- Windows x64 or Linux x64 for the provided publish targets.
- A graphical desktop and Avalonia's native system dependencies to run Studio on Linux. Self-contained publishing includes .NET, but does not replace operating-system libraries. See [Avalonia platform requirements](https://docs.avaloniaui.net/docs/overview/supported-platforms).

Run all commands from the repository root. The publish scripts resolve the root themselves and can also be called from another directory.

## Build and run

```text
dotnet restore LunaArc.sln
dotnet build LunaArc.sln -c Release --no-restore
dotnet run --project src/LunaArc.Cli -- help
dotnet run --project src/LunaArc.Studio
```

Run the integration checks:

```text
dotnet run --project tests/LunaArc.SmokeTests -c Release
dotnet run --project tests/LunaArc.StudioChecks -c Release
```

The checks exercise .larc (compressed, uncompressed and encrypted), ZIP and TAR GZip round trips; compare SHA-256 content hashes; check wrong passwords, Unicode paths, empty files, cancellation, preview isolation, unsafe paths, corrupt ZIP data and preservation of an existing extraction destination. A failing check produces a nonzero exit code. These checks do not register shell menus or use personal files.

StudioChecks uses Avalonia's headless Skia renderer to capture real UI frames and verify theme, archive loading, command binding, filtering, folder navigation, dialogs, cancellation of option edits and minimum-width layout. Screenshots default to the ignored `.tools/ui-preview` directory; CI uploads separate `LunaArc-UI-Preview-win-x64` / `linux-x64` artifacts. Native OS file-picker dialogs and OS window chrome are outside these checks.

## Publish Windows and Linux

On Windows, using PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installers/build_windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File installers/build_windows.ps1 -Runtime linux-x64
```

On Linux, using Bash:

```bash
bash installers/build_linux.sh
bash installers/build_linux.sh win-x64
```

Both scripts publish self-contained executables, include native libraries in the single-file bundles, keep trimming disabled, and stop on failure. Outputs are `dist/win-x64` and `dist/linux-x64`, containing the GUI, CLI, README and EULA. Keep both executables together for shell integration. These are portable publish folders, not installer packages.

To use a specific SDK executable, pass `-DotNet /path/to/dotnet` to the PowerShell script or set `DOTNET_COMMAND=/path/to/dotnet` for Bash. Scripts also use `.tools/dotnet` when no SDK is available in PATH. This checkout has a local Windows SDK at `.tools/dotnet/dotnet.exe`; it is ignored by version control. For local build commands in this environment, use `& ./.tools/dotnet/dotnet.exe` in place of `dotnet`.

Cross-publishing Linux binaries on Windows verifies packaging, not Linux execution. The GitHub Actions workflow runs native Windows and Ubuntu build, smoke-test and publish jobs. Check the run for your downloaded artifact in the repository's Actions tab.

## Windows right-click menus

Extract the portable package into a permanent folder, then double-click **InstallContextMenu.cmd** once, or use **Tools > Install Explorer context menu** in LunaArc. Classic registration is per-user and preserves the default archive application.

Version 1.3 groups commands under **LunaArc**, with **Open with LunaArc**, **Extract files...**, **Extract to folder**, and **Extract Here** in that order. The modern menu names the destination dynamically, such as **Extract to "Sample archive\\"**. Other files and directories offer **Add to archive...** and **Add to ZIP archive...**. Only one selected filesystem item is supported; multi-selection and folder backgrounds are not supported.

**Extract files...** opens the extraction path/options dialog. The other extraction actions open LunaArc with progress and use either the archive's parent directory or an archive-named folder; protected archives also open the password/options dialog. Existing files are skipped by default. Select **Overwrite without prompt** only when replacement is intended, or use **Ask before overwrite** to decide per file. Compression commands open the General creation dialog with the output path and an optional Browse picker.

The optional **Windows 11 menu preview** uses a native C++ `IExplorerCommand` DLL and a sparse identity package, following [Microsoft's Explorer extension model](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer). Enable it using **InstallWindows11Menu.cmd**, or select the additional Windows 11 menu task in Setup. Portable registration shows the signer and certificate thumbprint and asks for **YES** before adding trust. The Setup task explicitly opts into the same certificate trust.

CI preview builds use a disposable self-signed test certificate; its public CER is included, and its private key is never distributed. Enabling this preview adds the certificate to **LocalMachine/TrustedPeople** (all Windows users), requiring an explicit consent prompt/Setup task and administrator permission. The elevated helper only imports that certificate; the original process registers the sparse package for the current user. It does not install a root certificate or change Developer Mode. A production release should use a publisher certificate already trusted by Windows. If you decline certificate trust/UAC, the classic LunaArc submenu remains accessible through **Show more options**. See [Microsoft's MSIX troubleshooting guidance](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide).

Keep the folder in place while its menu is registered. **UninstallContextMenu.cmd** removes classic and modern registrations owned by this copy; run it before moving/deleting a portable folder. A newer registered copy is preserved when another installation is removed. Certificate trust is not automatically removed because another copy may still use it. Close/reopen Explorer windows or sign out if the shell has cached a prior registration; the scripts do not kill Explorer.

## Windows setup and portable editions

Both editions contain identical `LunaArc.exe` and `luna-cli.exe` binaries, so archive formats and extraction/compression options are the same.

| Download in GitHub Actions | File inside the artifact | How to use |
| --- | --- | --- |
| `LunaArc-Windows-Setup` | `LunaArc-<version>-setup-win-x64.exe` | Run the wizard; choose Explorer integration and an optional desktop shortcut |
| `LunaArc-Windows-Portable` | `LunaArc-<version>-portable-win-x64.zip` | Extract the inner ZIP, run `LunaArc.exe`, optionally run `InstallContextMenu.cmd` |
| `LunaArc-Windows-Checksums` | `SHA256SUMS.txt` | SHA-256 checksums for both packages |

GitHub Actions wraps each downloaded artifact in a ZIP; extract it to find the setup EXE or portable ZIP listed above. Setup installs per-user under `%LOCALAPPDATA%\Programs\LunaArc`, with Start menu and uninstall entries. The classic Explorer task is selected by default; the modern Windows 11 preview task is unchecked and explicitly describes certificate trust. Both editions ship the same GUI, CLI, native menu DLL, menu icon, sparse package, public certificate and registration scripts.

Uninstall removes menus only if they currently point to that installation. If a portable copy has since registered its own menu, uninstalling the installed edition preserves the portable menu. The most recently registered copy owns the common LunaArc commands; the two copies do not create duplicate menus. User-created archives and personal application-data files are not removed by the installer.

Full Windows publishing also requires Visual Studio C++ x64 build tools, CMake/CTest and Windows SDK MakeAppx/SignTool. Set `LUNAARC_MENU_SIGNING_THUMBPRINT` to a certificate with a private key in CurrentUser/My for local menu signing. CI generates an ephemeral preview signer. Use `build_windows.ps1 -SkipExplorerExtension` for a managed-only local publish; it cannot produce the full setup/portable packages. Cross-publishing Windows from Linux also produces managed binaries without the native menu.

To build both complete Windows packages locally, install these tools and [Inno Setup 6.3 or later](https://jrsoftware.org/isdl.php), then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installers/build_windows_packages.ps1
```

Use `-SkipPublish` to package an existing `dist/win-x64` build, or `-Iscc "C:\path\to\ISCC.exe"` for a custom compiler location. Versioned packages and checksums are written to `dist/packages`. The Windows CI job compiles both packages and tests installation/removal, the optional Explorer task, equality of installed/portable binaries and menu ownership during coexistence. Installer lifecycle tests are restricted to the disposable CI runner and do not run against a developer's Windows profile.

## CLI usage

```text
luna-cli list <archive> [-p password]
luna-cli extract-here <archive> [-o directory] [-p password]
luna-cli extract-smart <archive> [-p password]
luna-cli extract-folder <archive> [-p password]
luna-cli pack-larc <file-or-directory> [-p password]
luna-cli pack-zip <file-or-directory>
luna-cli test <archive> [-p password]
luna-cli register-shell
luna-cli unregister-shell
```

Quote paths containing spaces. Packing creates `<source>.larc`, `<source>.zip` or `<source>.7z`, or uses the explicit new path from `-o`. Extraction currently overwrites matching destination files by default; choose a separate destination when preserving existing files matters. `test` returns a nonzero exit code for an unhealthy archive. Unknown commands and incomplete arguments also fail.

Shell registration is explicit and changes the current user's OS configuration. Run it from the published directory. Windows registers per-extension Explorer verbs through `SystemFileAssociations` and adds LunaArc as an Open With choice without changing existing defaults. It notifies Explorer after installation/removal. Linux registration creates a desktop launcher and Nautilus scripts under `XDG_DATA_HOME` (or `~/.local/share`); it does not automatically set default MIME associations or install Dolphin menus.

## Formats and current limitations

- Create: `.larc`, `.zip`, `.tar.gz`, `.7z`. Studio exposes all four; CLI packing exposes LARC, ZIP and 7z. Published GUI/CLI packages support SFX overlays and LunaArc recovery/lock companions.
- Read/extract: native .larc; `.tar`, `.tar.gz` and `.tgz` through .NET; ZIP, RAR and 7z through SharpCompress. Other compressed inputs depend on SharpCompress's format detection and need representative fixtures before claiming support.
- `.larc` stores a versioned magic header, compressed JSON metadata and per-file payloads. Optional passwords encrypt both metadata and data using AES-256-CBC with PBKDF2-SHA256. CRC32 detects accidental corruption; the format does not provide authenticated encryption.
- ZIP, TAR GZip and 7z output do not support passwords. Unsupported encryption is rejected rather than silently ignored. `TarZst` remains in the API enum but creation is explicitly rejected.
- Split-volume output exists for .larc/ZIP, but reading those split outputs is not implemented. Do not use it when an immediately readable archive is required.
- File preview uses an isolated temporary session. Editing a preview does not update the original archive.
- `.larc` currently buffers each file in memory. Large-file memory usage and speed claims require benchmarks.
- `PasswordVault` uses authenticated AES-256-GCM encryption with a master password. Legacy plaintext JSON is not imported automatically; see the recovery/vault section above.
- TAR links and special/device entries are rejected. ZIP/TAR/.larc do not all preserve the same metadata or empty-directory behavior.
- Verified extraction contents are written to temporary files before replacing existing destinations. ZIP and .larc CRC failures preserve the old destination. When `KeepBrokenFiles` is enabled for ZIP/.larc, incomplete content is retained under a unique `.broken-<id>` name.

## Local verification

The current Windows checkout was checked with .NET SDK 8.0.426:

- Debug and Release solution builds passed with zero warnings and zero errors.
- 291 backend/archive checks passed on Windows; 293 passed on Ubuntu, including solid/non-solid 7z, SFX footer validation, recovery reconstruction/refusal, cancellation, locks and combined verified source deletion.
- 57 isolated Registry checks verify cascading groups, command order/quoting, default applications, other applications' entries and scoped cleanup without changing Explorer configuration.
- 92 rendered Studio checks passed locally, including General option bindings, 7z creation, recovery/lock companions, explicit unlock and exact-hash repair.
- The version 1.4.0 `win-x64` managed-only self-contained publish succeeded. Published ZIP/LARC/7z CLI SFX files ran independently; recovery and lock/unlock checks passed. Complete Windows setup/menu packaging is verified separately by CI.
- Native Ubuntu 24.04 build, 293 backend checks, 92 rendered UI checks and published standalone SFX execution passed in CI for version 1.4.0. Native OS file-picker dialogs and full GUI SFX desktop interaction remain outside the headless checks.

CI results belong to the specific commit/run; review the Actions tab before downloading an artifact.

Build artifacts (`bin`, `obj`, `dist`, `.tools`) and local task notes (`task`) are excluded from version control. Project guidance is in [AGENT.md](AGENT.md).

### Advanced feature verification

Before running smoke/UI checks from source, prepare the pinned encoder. On Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installers/prepare_sevenzip.ps1 -OutputDirectory .tools/check-encoder
$env:LUNAARC_7ZIP_PATH = (Resolve-Path .tools/check-encoder/LunaArc.7zr.exe).Path
```

On Linux:

```bash
bash installers/prepare_sevenzip.sh "$PWD/.tools/check-encoder"
export LUNAARC_7ZIP_PATH="$PWD/.tools/check-encoder/LunaArc.7zz"
```

The Linux preparation script requires curl, sha256sum and tar with xz support. Windows PowerShell preparation also supports `-Runtime linux-x64` for cross-publishing (tar required). Publish scripts include the encoder and notices automatically; ordinary runtime use requires no download. The advanced smoke checks verify parity recovery/refusal, footer corruption, locks, safe failures and independently confirmed solid/non-solid 7z. `python installers/test_advanced_packages.py dist/win-x64` (or `dist/linux-x64`) executes actual published standalone CLI SFX files without a neighboring LunaArc installation. CI runs these natively on Windows and Linux; it does not automate the OS-level GUI SFX dialog.
