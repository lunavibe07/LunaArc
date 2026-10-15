# AGENT.md — LunaArc

## Mission

Build and maintain LunaArc, a .NET 8 archive application for Windows and Linux. Prioritize correctness and preservation of user data, security, maintainability, simplicity, then performance. Keep source comments, UI messages, CLI output and documentation in English, encoded as UTF-8.

Read relevant source before editing. Source and project files define current behavior; README claims and comments are not proof that a feature or performance target has been verified.

## Architecture

- `LunaArc.sln`: Core, Shell, CLI, Studio and the smoke-check executable.
- `Directory.Build.props`: shared `net8.0`, C# 12, nullable and product metadata.
- `global.json`: .NET 8 SDK selection, minimum feature band 8.0.400.
- `src/LunaArc.Core`: archive contracts, formats, VFS, pipeline, security and isolated preview files. No Avalonia or OS shell-registration dependency.
- `src/LunaArc.Shell`: Windows Registry and Linux desktop/Nautilus integration; depends on Core. Unsupported operating systems fail explicitly.
- `src/LunaArc.Cli`: executable `luna-cli`; depends on Core and Shell.
- `src/LunaArc.Studio`: executable `LunaArc`; Avalonia views and CommunityToolkit.Mvvm ViewModels; depends on Core and Shell.
- `tests/LunaArc.SmokeTests`: dependency-light integration checks using disposable synthetic fixtures.
- `tests/LunaArc.StudioChecks`: headless Skia rendering and interaction checks for the actual Avalonia views, with PNG captures.
- `installers`: self-contained publish scripts, versioned Windows portable ZIP/setup packaging, Inno Setup definition and CI-only installer lifecycle checks.
- `branding`: logo and EULA; `native_libs`: optional-library notes, not an implemented native adapter.
- `.github/workflows/build.yml`: native Windows/Linux verification and artifact publishing in CI.

Current dependencies: SharpCompress 0.50.5, SharpZipLib 1.4.2 (encrypted ZIP writing), ZstdSharp.Port 0.8.1, Avalonia 11.1.3 and CommunityToolkit.Mvvm 8.2.2. Check `.csproj` for the actual versions. Upgrade only when needed by the task and verify affected APIs and archive fixtures.

## Current behavior and limits

- `StandardArchiveHandler` is shared by CLI and Studio. Standard TAR/TAR GZip processing uses `TarArchiveHandler` and .NET TAR APIs; other standard formats use SharpCompress.
- `.larc` contains magic `LUNAARC\x01`, flags, compressed JSON metadata and individual payloads. It is not a TAR wrapper. Passwords currently encrypt both header and payload.
- .larc creation buffers whole files. The 64 KiB streaming pipeline does not establish bounded memory for every format.
- ZIP password creation uses WinZip AES-256 through SharpZipLib and independent SharpCompress/authentication verification; filenames remain visible. Encrypted ZIP64/split creation is rejected; source data is limited to 3 GiB. 7z password creation uses AES-256 with encrypted headers through the pinned encoder; pass secrets via redirected UTF-8 stdin, never arguments or files. Passwords cannot include CR/LF/NUL and are limited to 1024 characters for 7z. TAR GZip has no native encryption: the appended EncryptedTarGz format creates an encrypted 7z containing one normal TAR GZip (.tar.gz.7z), requiring two extraction steps; source deletion is unavailable for that nested wrapper. Preserve existing enum values and LARC v1. `TarZst` remains unsupported. Solid compression is 7z-only; `EncryptHeader` is not an independent option.
- SFX v1 uses a self-contained published GUI/CLI executable plus a bounded, SHA-256 checked archive overlay. It targets the current OS/architecture and never auto-runs extracted files. LARC v1 remains unchanged.
- `.lrec` recovery v1 is a separate bounded block-map/XOR-parity format, not RAR or PAR2: one damaged 1 MiB block per group of 16. Repair verifies the full original hash and writes a new output. `.lunalock` is an advisory companion plus read-only attribute, not encryption or an access-control boundary. Preserve companions and explicit unlock semantics.
- Multi-volume output exists, but reading split outputs is not implemented.
- Preview files are extracted into a validated session directory. Watcher notifications do not imply saving edits back to archives.
- `PasswordVault` uses a versioned AES-256-GCM envelope and PBKDF2-SHA256 (600,000 iterations). Never persist the master password or include secrets in logs. Legacy plaintext JSON is not imported or deleted automatically.
- Password recovery searches explicit lists or lazy masks, limited to one million candidates with at most two workers. Validate decrypted contents, not only a readable catalog. Preserve cancellation; pause applies between candidate attempts. Recovered passwords belong to the archive session, not new-archive settings.
- Standard archive tests use SharpCompress checksum validation and enforce declared output lengths. WinZip AES authentication is verified separately before extraction/testing; AE-2 omits CRC by specification. The additional encrypted ZIP verification rejects ZIP64 and split ZIP explicitly.
- AES-256-CBC and CRC32 do not provide authenticated encryption. Format/crypto changes require explicit compatibility decisions.
- Shell registration modifies user configuration and does not guarantee a default-app change on modern Windows or Linux. Linux currently creates a launcher and Nautilus scripts, not Dolphin menus.
- Explorer extraction and GUI SFX use StandaloneExtractionSession; Explorer Add/AddZip use StandaloneCompressionSession. These startup actions use ownerless options and compact progress/error windows; never instantiate MainWindow for them or open the resulting archive automatically. Keep explicit desktop lifetime while switching root windows, wait for cancellation before disposing the worker, and auto-exit on success/cancel. Open with LunaArc, toolbar Add and toolbar extraction keep the browser workflow. Native published GUI Extract Here/folder lifecycle is checked on Windows and Linux (Xvfb).
- Windows classic menus use a LunaArc cascading group under per-extension SystemFileAssociations; preserve defaults, other applications' verbs and newer installation ownership. Archive commands launch Studio with validated --shell-* requests, not transient CLI windows.
- native_libs/WindowsContextMenu implements a Windows x64 IExplorerCommand COM DLL and sparse identity manifest. Keep menu-construction methods lightweight, never read archive contents inside Explorer, and verify COM lifetime/enumeration/quoting using ContextMenuChecks. This is separate from the managed archive backend.
- Windows 11 registration is optional: CI menu previews use an ephemeral self-signed signer, distributed only as public CER. Test-package trust requires LocalMachine/TrustedPeople, explicit portable consent or an unchecked Setup task, and administrator permission. Elevate only certificate import; register the package as the original current user. Never silently import trust, install a root certificate, change Developer Mode or restart Explorer. Production signing requires a trusted publisher certificate. Unregister only matching installation/package/external location, preserving other copies and certificate trust.
- Full Windows publishing needs CMake, Visual Studio C++ tools and Windows SDK packaging/signing tools. -SkipExplorerExtension is managed-only and cannot produce complete Windows packages. Native builds and contract checks do not prove rendered Windows 11 Explorer behavior.
- Windows setup and portable editions must ship identical GUI/CLI binaries and provide the same archive options. Setup is per-user with an optional Explorer task. Scoped uninstall must preserve menus owned by another copy. Keep the installer AppId stable across upgrades. Do not recursively remove user data during uninstall.

## Workflow

**UNDERSTAND → PLAN → IMPLEMENT → VERIFY → REPORT**

1. Inspect affected contracts and consumers. Core changes may affect both CLI and Studio, old archives and filesystem behavior.
2. State the intended fix and verification briefly. Resolve routine implementation choices autonomously; ask only for missing decisive information or actions outside the user's authorization.
3. Keep changes scoped, follow existing patterns and preserve user edits. Avoid unnecessary abstractions/dependencies.
4. Run checks appropriate to the change and report actual results and limitations. Compile success is not runtime verification.
5. Update related documentation when behavior or build instructions change.

## C# and UI rules

- Keep nullable annotations, file-scoped namespaces and C# 12 compatibility. Do not enable preview language features just to bypass a compile failure.
- Core owns archive/I/O/security logic; Studio owns UI; Shell owns OS integration.
- Keep options, enums, progress, errors and default behavior consistent across consumers.
- Dispose streams, controllers, watchers and crypto resources. Pass cancellation tokens through long operations; cancellation must release pause and remain distinguishable from archive failure.
- Avoid whole-file buffering in new large-file paths. Validate lengths, offsets, overflow and allocation limits before trusting archive metadata.
- Preserve VFS parent/child relationships, full paths, metadata and root detection. Archive paths are case-sensitive, even on a case-insensitive host.
- Keep Studio state/commands in ViewModels and layout/bindings in XAML. Run archive work off the UI thread and dispatch progress/watcher notifications to the UI thread.
- Studio uses the user-selected Nocturne Orbit layout: matte navy, cyan primary extraction action, gold folder accents, original Luna logo, left action rail, archive heading/search/path, DataGrid, responsive selection inspector and an extraction destination dock. Preserve the compact layout at 720 px, existing menu/keyboard access and standalone shell sessions. Default columns are Name/Size/Packed/Modified; Commands can reveal Type/CRC32. Keep unsupported archive editing actions out of the interface. Recovery-record repair and lock/unlock remain under Tools; compression/password/comment options remain modal. Preserve numeric size sorting, current-folder filtering, busy/cancel guards and real destination validation. Unknown packed metadata displays a dash; the archive heading uses the actual file size.
- Help > Help topics / F1 uses the cross-platform offline HelpTopicsWindow, HelpTopicsViewModel and embedded HelpCatalog. Keep its English content aligned with actual supported behavior; never copy WinRAR help text or promise unsupported capabilities. Preserve Contents/Index/Search/Favorites, related links, navigation history and atomic user-scoped topic-ID favorites. Headless checks inject a disposable favorites path and must not modify personal favorites.
- Prevent overlapping jobs; verify busy, error, cancel and selection/navigation behavior. Check generated commands and bindings when changing XAML.

## Archive and filesystem rules

- Treat entry paths, headers, sizes, offsets and compressed bytes as untrusted input.
- Validate extraction and preview destinations with `ZipSlipGuard`. Reject traversal, absolute/UNC/drive paths and existing link/reparse-point escapes. Account for Windows/Linux path differences.
- Apply appropriate entry-count, output-size and resource limits when implementing untrusted archive handling. Do not claim complete archive-bomb protection unless tested.
- Honor selection and overwrite options. `Ask` must not silently overwrite when no confirmation callback exists.
- Preserve originals on failure/cancellation. Prefer temporary output and controlled replacement. Cleanup must only remove artifacts created by the operation.
- Delete sources only when explicitly requested and after a closed, suitably verified archive exists. Never use personal files for smoke tests.
- Format/crypto changes must consider old archives, versioning, malformed input and truncated files; do not silently change .larc magic or layout.
- Never log passwords or encryption keys. Fixtures use synthetic data and fake passwords.
- OS shell registration is not part of an ordinary build check. Only register/unregister when authorized; quote executable paths and check that both published executables exist.

## Verification commands

Run from the repository root. Confirm the SDK with `dotnet --info`; use an explicit SDK executable if PATH is missing. A local ignored Windows SDK may be available at `.tools/dotnet/dotnet.exe`.

```text
dotnet restore LunaArc.sln
dotnet build LunaArc.sln -c Debug --no-restore
dotnet build LunaArc.sln -c Release --no-restore
dotnet run --project tests/LunaArc.SmokeTests -c Release
dotnet run --project tests/LunaArc.StudioChecks -c Release
dotnet run --project src/LunaArc.Cli -- help
dotnet run --project src/LunaArc.Studio
```

Use only relevant checks for a given change. Markdown-only edits do not need an application build. The smoke executable is run with `dotnet run`, not `dotnet test`; report the number of checks actually executed.

Backend checks should include round trips and independent hashes/readers where relevant, Unicode and empty files, wrong passwords, invalid archives/paths, overwrite behavior and cancellation. Do not open extracted files with OS applications during automated tests.

For UI changes, verify the affected rendered flow when a desktop is available. Process startup is not a full UI interaction test. For performance changes, record fixture size/count, hardware, time and memory measurements.

For Studio layout changes, run StudioChecks, inspect its PNG frames, and distinguish headless rendering from native OS chrome/file-picker testing. Imported toolbar geometries come from Microsoft Fluent UI System Icons; retain branding/Fluent_Icons_LICENSE.txt and publish its notice with both editions.

Publish via `installers/build_windows.ps1` (PowerShell) or `installers/build_linux.sh` (Bash). Both support win-x64/linux-x64 targets. Check exit codes and executable artifacts, native-library packaging and trimming settings. Cross-publish success does not establish native Linux runtime success; CI results must be distinguished from a merely added workflow.

Build Windows setup/portable packages via `installers/build_windows_packages.ps1`; Inno Setup 6.3+ is required. Use the product version in Directory.Build.props for output names and include checksums. Installer lifecycle testing (`test_windows_packages.ps1`) is CI-only because it writes the disposable runner's real per-user registration; local shell checks use an isolated test key.

## Artifacts, change logs and reports

- Ignore `bin/`, `obj/`, `dist/`, `.tools/`, temporary archives/parts and private user configuration unless a fixture/release purpose is explicit.
- For code, configuration or documentation changes, create/update `task/change-logs/YYYYMMDD-HHMMSS-short-description.md` using the user's session timezone.
- Keep `task/` local and ignored by Git. Never stage or upload task notes; the project owner explicitly requested this.
- Include request, affected files, before/after behavior, root cause when applicable, actual checks/evidence, limitations/risks and release status.
- Do not upload, sign, deploy or publicly release artifacts outside the requested scope.
- Final reports should lead with the outcome and identify relevant changed files, validation and remaining limits. Distinguish source review, compilation, smoke checks, publish, GUI startup, native OS runtime and CI execution.
