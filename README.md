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
```

The checks exercise .larc (compressed, uncompressed and encrypted), ZIP and TAR GZip round trips; compare SHA-256 content hashes; check wrong passwords, Unicode paths, empty files, cancellation, preview isolation, unsafe paths, corrupt ZIP data and preservation of an existing extraction destination. A failing check produces a nonzero exit code. These checks do not register shell menus or use personal files.

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
- `PasswordVault` stores plaintext JSON in the user's application-data directory; it is not a secure credential store.
- TAR links and special/device entries are rejected. ZIP/TAR/.larc do not all preserve the same metadata or empty-directory behavior.
- Verified extraction contents are written to temporary files before replacing existing destinations. ZIP and .larc CRC failures preserve the old destination. When `KeepBrokenFiles` is enabled for ZIP/.larc, incomplete content is retained under a unique `.broken-<id>` name.

## Local verification

The current Windows checkout was checked with .NET SDK 8.0.426:

- Debug and Release solution builds passed with zero warnings and zero errors.
- 63 backend/archive smoke checks passed on Windows.
- 41 isolated Windows Registry checks verify context-menu registration/removal, executable quoting, existing default applications and other applications' menu entries. They do not modify the real Explorer configuration.
- Both `win-x64` and `linux-x64` self-contained publishes succeeded.
- 16 checks passed against the packaged Windows CLI; the packaged GUI remained running during a four-second startup check.
- Linux output files were checked for ELF signatures. Native Linux execution, Bash-script execution and full desktop UI interactions were not verified locally because this machine has no Linux runtime or connected desktop automation.

CI results belong to the specific commit/run; review the Actions tab before downloading an artifact.

Build artifacts (`bin`, `obj`, `dist`, `.tools`) are excluded from version control. Project guidance is in [AGENT.md](AGENT.md); implementation and verification notes are in `task/change-logs`.
