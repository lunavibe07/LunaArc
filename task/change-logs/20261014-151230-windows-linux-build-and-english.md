# Windows/Linux build readiness and English project text

- Time: 2026-10-14 15:12:30 (Asia/Bangkok, UTC+07:00).
- Request: check and fix the project for Windows/Linux builds; convert all project text to English.

## Root causes

The source contained invalid backslash character literals, unescaped quotes in shell command strings, literal line breaks in normal C# strings, and ref/stackalloc usage incompatible with C# 12 async methods. Publish scripts assumed the working directory and did not reliably propagate native command failures. The original SharpCompress version produced a NuGet security warning. Runtime checks also exposed compressed-TAR reader failures, stream ownership/file-lock issues and missing ZIP CRC comparison.

## Changes and affected files

- All C# source and Avalonia XAML under src: translated comments, messages, labels and dialogs into English; corrected malformed literals. Existing APIs and architecture were retained where possible.
- Directory.Build.props and global.json: explicit C# 12 and .NET 8 SDK selection.
- src/LunaArc.Core/LunaArc.Core.csproj: SharpCompress 0.50.5, with corresponding OpenArchive API usage and explicit file-stream ownership.
- Core Formats: .NET TAR/PAX reader/writer path for .tar/.tar.gz/.tgz; shared handler rejects unsupported password/TarZst output, missing sources and output inside the input tree; checks ZIP CRC; preserves cancellation and correct .larc file counts; verifies archives before explicitly requested source deletion.
- Core Pipeline/AtomicExtractFile.cs: temporary extraction output and verified replacement; preserve pre-existing files on CRC/error/cancel; optionally retain broken content under a unique name.
- Core Security/ZipSlipGuard.cs: reject traversal, absolute/drive/UNC paths and existing reparse-point/link escapes.
- Core VfsIndex/FlatVfsArena.cs: retain case-sensitive archive folder identities and reject invalid initial capacity.
- Core LiveEdit and Studio ViewModels/Views: isolated validated preview directory, watcher cleanup, worker-thread archive jobs, UI-thread progress, busy-state gating and English preview limitations.
- Shell: valid quoted Windows commands and executable checks; Linux desktop/Nautilus generation uses LF, quoted arguments, XDG data directories, multiple selected arguments and cleanup of its generated scripts. Unsupported OS selection fails explicitly.
- CLI: argument/option validation, unknown-command and unhealthy-archive exit codes, password support for smart extraction, published executable paths for shell registration.
- installers/build_windows.ps1 and installers/build_linux.sh: root-relative publishing, explicit error handling, win-x64/linux-x64 selection, SDK override/local fallback, self-contained single-file native-library bundling, disabled trimming, executable checks and README/EULA copying.
- tests/LunaArc.SmokeTests and LunaArc.sln: executable integration checks with disposable synthetic fixtures.
- .github/workflows/build.yml: native Windows/Ubuntu build, smoke-check, publish and packaged-CLI jobs; workflow is local and was not executed remotely.
- .gitignore and .gitattributes: exclude generated files and use LF for Bash scripts.
- README.md, AGENT.md, native_libs/README_NATIVE_LIBS.txt and the previous change log: English documentation reflecting actual functionality and limits. Historical verification results remain distinguished from this task.

## Verification and evidence

- Toolchain: local .NET SDK 8.0.426 installed in ignored .tools/dotnet using Microsoft's dotnet-install script; no persistent PATH change. WSL was checked and is not installed.
- Debug and Release solution builds: passed, zero warnings and zero errors.
- Smoke executable: 63 checks passed on Windows, including five archive modes, independent SHA-256 comparisons, Unicode/empty files, wrong passwords, unsafe paths, cancellation, VFS case identity, preview confinement, corrupted ZIP CRC, destination preservation, cleanup and malformed .larc header bounds.
- Windows self-contained GUI and CLI publish: passed.
- Linux x64 self-contained GUI and CLI cross-publish from Windows: passed. One transient file-copy retry occurred while local verification overlapped; a subsequent sequential Linux publish completed without warnings.
- Packaged Windows CLI: 16 checks passed, including help, invalid/missing input, encrypted .larc and ZIP round trips, content hashes and failure exit codes.
- Packaged Windows GUI: remained running during a four-second hidden startup check, then the test-owned process was terminated. This is process-startup evidence, not rendered UI interaction verification.
- Linux artifacts: both executables have ELF signatures; native Linux runtime execution was not performed.
- English/UTF-8: source, XAML, scripts and documentation scanned for remaining Vietnamese text and validated as UTF-8. Bash source uses LF line endings; PowerShell script parsed successfully.

## Limits and release status

No native Linux runtime, Bash execution, full desktop UI interactions, real Registry/desktop registration, CI execution, RAR/7z fixtures or performance benchmarks were verified. The .larc format still buffers whole files and does not provide authenticated encryption; split-volume reading and save-back editing remain unsupported. PasswordVault remains plaintext. Unsupported capabilities are documented rather than advertised as working.

Outputs are local dist/win-x64 and dist/linux-x64 publish folders. No production deployment, public release, remote repository changes or OS shell registration was performed.

## References used during repair

- Microsoft .NET install script: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script
- SharpCompress advisory surfaced by NuGet: https://github.com/advisories/GHSA-6c8g-7p36-r338
- Upstream archive API: https://github.com/adamhathcock/sharpcompress/blob/0.48.0/src/SharpCompress/Archives/ArchiveFactory.cs
