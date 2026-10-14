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
- `installers`: portable self-contained publish scripts, not full installer generators.
- `branding`: logo and EULA; `native_libs`: optional-library notes, not an implemented native adapter.
- `.github/workflows/build.yml`: native Windows/Linux verification and artifact publishing in CI.

Current dependencies: SharpCompress 0.50.5, ZstdSharp.Port 0.8.1, Avalonia 11.1.3 and CommunityToolkit.Mvvm 8.2.2. Check `.csproj` for the actual versions. Upgrade only when needed by the task and verify affected APIs and archive fixtures.

## Current behavior and limits

- `StandardArchiveHandler` is shared by CLI and Studio. Standard TAR/TAR GZip processing uses `TarArchiveHandler` and .NET TAR APIs; other standard formats use SharpCompress.
- `.larc` contains magic `LUNAARC\x01`, flags, compressed JSON metadata and individual payloads. It is not a TAR wrapper. Passwords currently encrypt both header and payload.
- .larc creation buffers whole files. The 64 KiB streaming pipeline does not establish bounded memory for every format.
- ZIP/TAR GZip output reject passwords; `TarZst` creation is unsupported and fails explicitly. Contract options such as `EncryptHeader` and `SolidArchive` are not proof of implemented independent behavior.
- Multi-volume output exists, but reading split outputs is not implemented.
- Preview files are extracted into a validated session directory. Watcher notifications do not imply saving edits back to archives.
- `PasswordVault` writes plaintext JSON; never describe it as a secure secret store or include its contents in logs/fixtures.
- AES-256-CBC and CRC32 do not provide authenticated encryption. Format/crypto changes require explicit compatibility decisions.
- Shell registration modifies user configuration and does not guarantee a default-app change on modern Windows or Linux. Linux currently creates a launcher and Nautilus scripts, not Dolphin menus.

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
dotnet run --project src/LunaArc.Cli -- help
dotnet run --project src/LunaArc.Studio
```

Use only relevant checks for a given change. Markdown-only edits do not need an application build. The smoke executable is run with `dotnet run`, not `dotnet test`; report the number of checks actually executed.

Backend checks should include round trips and independent hashes/readers where relevant, Unicode and empty files, wrong passwords, invalid archives/paths, overwrite behavior and cancellation. Do not open extracted files with OS applications during automated tests.

For UI changes, verify the affected rendered flow when a desktop is available. Process startup is not a full UI interaction test. For performance changes, record fixture size/count, hardware, time and memory measurements.

Publish via `installers/build_windows.ps1` (PowerShell) or `installers/build_linux.sh` (Bash). Both support win-x64/linux-x64 targets. Check exit codes and executable artifacts, native-library packaging and trimming settings. Cross-publish success does not establish native Linux runtime success; CI results must be distinguished from a merely added workflow.

## Artifacts, change logs and reports

- Ignore `bin/`, `obj/`, `dist/`, `.tools/`, temporary archives/parts and private user configuration unless a fixture/release purpose is explicit.
- For code, configuration or documentation changes, create/update `task/change-logs/YYYYMMDD-HHMMSS-short-description.md` using the user's session timezone.
- Include request, affected files, before/after behavior, root cause when applicable, actual checks/evidence, limitations/risks and release status.
- Do not upload, sign, deploy or publicly release artifacts outside the requested scope.
- Final reports should lead with the outcome and identify relevant changed files, validation and remaining limits. Distinguish source review, compilation, smoke checks, publish, GUI startup, native OS runtime and CI execution.
