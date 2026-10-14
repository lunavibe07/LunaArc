# LunaArc Archiver

LunaArc is a Windows and Linux archive application built with .NET 8. It includes an Avalonia desktop interface and a command-line tool that share the same archive backend.

## Projects

| Project | Purpose |
| --- | --- |
| `src/LunaArc.Core` | Archive formats, virtual directory index, streaming, encryption and safe extraction paths |
| `src/LunaArc.Shell` | Per-user Windows Explorer integration and Linux desktop/Nautilus scripts |
| `src/LunaArc.Cli` | `luna-cli` command-line executable |
| `src/LunaArc.Studio` | `LunaArc` desktop application using Avalonia and MVVM |
| `tests/LunaArc.SmokeTests` | Executable integration checks using synthetic archive fixtures |

Core uses SharpCompress 0.50.5 and ZstdSharp.Port 0.8.1. Studio uses Avalonia 11.1.3 and CommunityToolkit.Mvvm 8.2.2. Standard TAR and TAR GZip handling uses the .NET TAR APIs.

## Desktop interface

Since version 1.1, LunaArc uses a light, classic desktop layout: menu bar, large toolbar icons, archive path, resizable/sortable file columns and a status bar. The layout is inspired by familiar archive managers while retaining LunaArc branding.

- **Open** loads an archive; **Add** selects files and opens the format, level, password and comment dialog before saving a new archive.
- **Extract To**, **Test** and **View** use the existing archive backend. Archive-only commands stay disabled until an archive is open; View also requires a selection.
- Double-click a folder to browse it; **Up** returns to its parent. Double-click a file to preview it.
- **Find** / **F3** filters names in the current folder without changing archive contents. Size columns sort by numeric byte counts.
- **Info** shows the current archive path, folder/file summary and available comment. **Options** opens compression/password settings; cancelling restores the previous settings.
- **File** includes close archive and exit; **Tools** contains context-menu installation. **Ctrl+O** opens an archive.

The interface lists archive contents; it is not a general system-drive file browser. Delete and Repair commands are not exposed because the backend does not implement archive editing/repair. Both installed and portable Windows editions have this same interface. Toolbar artwork comes from Microsoft Fluent UI System Icons; its MIT notice is included in every published folder.

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

The ZIP is a portable application. Extract the entire `LunaArc-win-x64` artifact into a permanent folder, then double-click **InstallContextMenu.cmd** once. Alternatively, open LunaArc and click **Install Context Menu**. Registration is per-user and does not require administrator privileges.

Right-click a supported archive to use **LunaArc: Open Archive**, **LunaArc: Extract Here**, or **LunaArc: Extract to Folder**. The last command always creates an archive-named destination, even if the archive already contains a root folder. Files and directories also have **Create .larc Archive** and **Create ZIP Archive** commands. Only one selected item is supported by these commands.

On **Windows 11**, choose **Show more options** to see these classic context-menu entries. This release does not implement the modern Windows 11 `IExplorerCommand` menu. See [Microsoft's File Explorer guide](https://support.microsoft.com/en-gb/windows/experience/fileexplorer/file-explorer-in-windows?nochrome=true).

The menus work independently of the current default ZIP/RAR application. Installing them does not replace that application. Keep the published folder in place: the menu commands refer to those executables. Run **UninstallContextMenu.cmd** before deleting or moving the folder; after moving it, run installation again from the new location. You can also remove menus with `luna-cli unregister-shell`.

## Windows setup and portable editions

Both editions contain identical `LunaArc.exe` and `luna-cli.exe` binaries, so archive formats and extraction/compression options are the same.

| Download in GitHub Actions | File inside the artifact | How to use |
| --- | --- | --- |
| `LunaArc-Windows-Setup` | `LunaArc-<version>-setup-win-x64.exe` | Run the wizard; choose Explorer integration and an optional desktop shortcut |
| `LunaArc-Windows-Portable` | `LunaArc-<version>-portable-win-x64.zip` | Extract the inner ZIP, run `LunaArc.exe`, optionally run `InstallContextMenu.cmd` |
| `LunaArc-Windows-Checksums` | `SHA256SUMS.txt` | SHA-256 checksums for both packages |

GitHub Actions wraps each downloaded artifact in a ZIP; extract it to find the setup EXE or portable ZIP listed above. The setup installs for the current user under `%LOCALAPPDATA%\Programs\LunaArc`, creates a Start menu shortcut and an Apps/Installed apps uninstall entry, and does not require administrator privileges. Explorer integration is selected by default and can be unchecked. It provides the same **Extract Here**, **Extract to Folder**, **Open Archive** and compression commands as the portable edition. Windows 11 still uses **Show more options**.

Uninstall removes menus only if they currently point to that installation. If a portable copy has since registered its own menu, uninstalling the installed edition preserves the portable menu. The most recently registered copy owns the common LunaArc commands; the two copies do not create duplicate menus. User-created archives and personal application-data files are not removed by the installer.

To build both Windows packages locally, install [Inno Setup 6.3 or later](https://jrsoftware.org/isdl.php), then run:

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

Quote paths containing spaces. Packing creates `<source>.larc` or `<source>.zip`. Extraction currently overwrites matching destination files by default; choose a separate destination when preserving existing files matters. `test` returns a nonzero exit code for an unhealthy archive. Unknown commands and incomplete arguments also fail.

Shell registration is explicit and changes the current user's OS configuration. Run it from the published directory. Windows registers per-extension Explorer verbs through `SystemFileAssociations` and adds LunaArc as an Open With choice without changing existing defaults. It notifies Explorer after installation/removal. Linux registration creates a desktop launcher and Nautilus scripts under `XDG_DATA_HOME` (or `~/.local/share`); it does not automatically set default MIME associations or install Dolphin menus.

## Formats and current limitations

- Create: `.larc`, `.zip`, `.tar.gz`. Studio exposes all three; CLI packing exposes .larc and ZIP.
- Read/extract: native .larc; `.tar`, `.tar.gz` and `.tgz` through .NET; ZIP, RAR and 7z through SharpCompress. Other compressed inputs depend on SharpCompress's format detection and need representative fixtures before claiming support.
- `.larc` stores a versioned magic header, compressed JSON metadata and per-file payloads. Optional passwords encrypt both metadata and data using AES-256-CBC with PBKDF2-SHA256. CRC32 detects accidental corruption; the format does not provide authenticated encryption.
- ZIP and TAR GZip output do not support passwords. Unsupported encryption is rejected rather than silently ignored. `TarZst` remains in the API enum but creation is explicitly rejected.
- Split-volume output exists for .larc/ZIP, but reading those split outputs is not implemented. Do not use it when an immediately readable archive is required.
- File preview uses an isolated temporary session. Editing a preview does not update the original archive.
- `.larc` currently buffers each file in memory. Large-file memory usage and speed claims require benchmarks.
- `PasswordVault` uses authenticated AES-256-GCM encryption with a master password. Legacy plaintext JSON is not imported automatically; see the recovery/vault section above.
- TAR links and special/device entries are rejected. ZIP/TAR/.larc do not all preserve the same metadata or empty-directory behavior.
- Verified extraction contents are written to temporary files before replacing existing destinations. ZIP and .larc CRC failures preserve the old destination. When `KeepBrokenFiles` is enabled for ZIP/.larc, incomplete content is retained under a unique `.broken-<id>` name.

## Local verification

The current Windows checkout was checked with .NET SDK 8.0.426:

- Debug and Release solution builds passed with zero warnings and zero errors.
- 122 backend/archive smoke checks passed on Windows, including list/mask password recovery, ZIP AES authentication and encrypted-vault failure cases.
- 45 isolated Windows Registry checks verify context-menu registration/removal, executable quoting, existing default applications, other applications' entries and installation-scoped cleanup. They do not modify the real Explorer configuration. Full setup lifecycle verification runs in the Windows CI job.
- 37 rendered Studio checks passed, including recovering a password, opening the archive, testing and extracting with its session secret, and vault operations.
- The version 1.2 `win-x64` self-contained publish succeeded and its CLI help ran successfully.
- Native Linux build, backend checks, rendered UI checks and publish passed in CI for the recovery implementation commit `2f90420`. This Windows machine does not establish native Linux desktop behavior; OS file-picker dialogs and full desktop interaction remain outside the headless checks.

CI results belong to the specific commit/run; review the Actions tab before downloading an artifact.

Build artifacts (`bin`, `obj`, `dist`, `.tools`) are excluded from version control. Project guidance is in [AGENT.md](AGENT.md); implementation and verification notes are in `task/change-logs`.
