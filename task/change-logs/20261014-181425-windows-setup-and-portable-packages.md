# Windows setup and portable packages with identical extraction options

- Time: 2026-10-14 18:14:25 (Asia/Bangkok, UTC+07:00).
- Request: add setup and portable editions; both must expose the same WinRAR-style extraction options.
- Implementation: versioned per-user Inno Setup EXE and portable ZIP from the same published payload, with SHA-256 checksums. Setup offers an Explorer context-menu task selected by default, optional desktop shortcut, Start menu entries and normal Windows uninstall support. Portable keeps explicit install/uninstall helpers and the existing GUI button.
- Behavior: both editions share identical GUI/CLI binaries and Open Archive, Extract Here, Extract to Folder and compression commands. Windows 11 classic commands remain under Show more options.
- Coexistence: unregister-shell --if-path removes only commands/ProgIDs pointing at its own installation. Installer and portable cleanup preserve another copy's active registration. Installer does not recursively remove user archives or application-data directories.
- Files: installers/LunaArc.iss, build_windows_packages.ps1, test_windows_packages.ps1, UninstallContextMenu.cmd; workflow; Windows registrar, CLI and isolated registry tests; README.md, AGENT.md; this change log.
- Verification completed locally: Release solution build passed with zero warnings/errors; 63 archive/filesystem and 45 isolated Windows Registry checks passed; package-build and lifecycle-test PowerShell scripts parsed successfully. No actual Explorer registration was changed on the developer machine.
- CI verification prepared: Inno Setup compiler on the Windows runner builds packages; CI-only lifecycle script verifies selected/unselected menu tasks, installation/removal, installed-versus-portable binary hashes and preserving portable registration during installed-edition removal.
- Limit at this point: local Inno compiler is not installed, so compilation and installer lifecycle results must be established by the new Windows CI run before declaring packages verified. Rendered installer wizard and Explorer menu interaction are not visually tested.
- Status at this point: source prepared for the existing authorized GitHub repository; generated packages remain under ignored dist/packages. GitHub Actions artifacts will carry separate Setup, Portable and Checksums names. No public release/tag or production deployment.

References: https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm ; https://jrsoftware.org/ishelp/topic_runsection.htm ; https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md
