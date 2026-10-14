# Fix Windows Explorer archive context menus

- Time: 2026-10-14 16:53:05 (Asia/Bangkok, UTC+07:00).
- Request: downloaded Windows build does not show extraction commands like WinRAR.
- Root cause: portable downloads did not register menus automatically, and extraction verbs were attached only to LunaArc ProgIDs. A different default application or Windows UserChoice could prevent those verbs from appearing. Windows 11 also places classic verbs under Show more options.
- Changes: register Open Archive, Extract Here and Extract to Folder under HKCU Software/Classes/SystemFileAssociations for supported extensions; preserve default application values; add OpenWith choices; remove duplicate legacy verbs; quote executable paths; notify Explorer after install/removal; unregister only LunaArc-owned values/verbs.
- Distribution: include InstallContextMenu.cmd and UninstallContextMenu.cmd in both PowerShell and Bash Windows publish outputs. Installation remains explicit, per-user and requires no elevation.
- CLI/UI: add unregister-shell and extract-folder commands; extract-folder always creates an archive-named destination and strips compound TAR suffixes; update GUI button/status and CLI help with Windows 11 instructions.
- Files: Windows registrar, Shell assembly test access, CLI, Studio ViewModel/XAML, both publish scripts, new CMD helpers, .gitattributes, smoke-check project/runner and WindowsShellChecks, README.md, AGENT.md, this change log.
- Verification: Release solution build passed with zero warnings/errors; 63 archive/filesystem checks and 41 isolated Registry checks passed. Registry tests verify supported extensions, executable quoting, existing default app preservation, legacy migration, idempotency and uninstall isolation. No real Explorer registration/default app was changed by these tests.
- Additional verification: both Windows and Linux x64 portable publishes passed. Windows artifact includes install/uninstall helpers. The published extract-folder CLI passed a ZIP round trip with spaces in the source and archive names, including an archive that already has a root folder. CLI help exposes both new commands. Git diff whitespace check passed.
- Limits: actual rendered Explorer menu interaction is not verified in this session; modern Windows 11 IExplorerCommand integration is not implemented. Use Show more options on Windows 11. Paths remain tied to the published folder, so uninstall before moving/deleting it.
- Release status at this point: local changes prepared for publishing to the existing authorized GitHub repository. No force push or production deployment.

Reference: https://learn.microsoft.com/en-us/windows/win32/shell/app-registration (SystemFileAssociations verbs independent of default program).
