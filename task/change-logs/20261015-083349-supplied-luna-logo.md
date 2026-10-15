# Supplied Luna logo

## Request and behavior

Use the project owner's supplied `anh.png` as LunaArc's logo. Version 1.3.1 replaces the former crescent/zipper icon with the supplied image in application windows, Windows GUI/CLI executables, Setup and Explorer menu assets. The About dialog displays the logo. Published Linux launchers use the included `LunaArc.png` when present.

## Changes

- Added the unchanged original PNG and resized 256-pixel PNG/multiple-size ICO under `branding`, with asset provenance and usage documentation. Retained the old SVG as a documented legacy asset.
- Embedded the new application icon and About resource in Studio, set a shared Window icon style, and embedded the same Windows icon in CLI.
- Replaced the build-time approximation of the old logo with copies of the committed PNG/ICO for the sparse native menu package.
- Updated Inno Setup and both publish scripts; package lifecycle checks require and compare the logo PNG/ICO between Setup and Portable.
- Updated README branding and shared patch version.

## Verification

- Release solution build: passed, zero warnings/errors.
- StudioChecks: 47 rendered checks passed, including the embedded Window icon and actual About bitmap dimensions. Inspected `.tools/ui-preview/about.png`; the supplied logo is visible and the text/Close button fit.
- Compared SHA-256 of the supplied file and `branding/app_logo.png`: identical.
- Published the self-contained Windows Studio executable to `.tools/logo-publish/win-x64` and extracted its embedded 32-pixel icon successfully.
- The ordinary managed-only publish attempt encountered a locked `dist/win-x64/LunaArc.exe` from a running application. Left that application running and verified the GUI in an isolated output directory instead. CLI publishing succeeded before that lock.

## Limits and release status

Headless rendering does not show native title bars or Explorer. Local checks do not register shell integration or install certificates.

Implementation commit `dd4ed85` passed both native CI jobs in [run 38666513610](https://github.com/lunavibe07/LunaArc/actions/runs/38666513610):

- Windows: Debug/Release builds, 126 archive/filesystem checks, 57 isolated shell checks and 47 rendered UI checks passed. Native menu build/sign/package succeeded with 61 direct COM checks. Setup/Portable lifecycle verification passed, including logo PNG/ICO equality, classic/modern registration, 64-check registered COM runs and installation-scoped cleanup on the disposable runner.
- Ubuntu: Debug/Release builds, 127 archive/filesystem checks, 47 rendered UI checks, Linux self-contained publishing and the published CLI help command passed.
- Uploaded version 1.3.1 Setup, Portable, checksums, raw Windows/Linux packages and UI preview artifacts. Setup artifact `11941541106`; Portable `11941481196`; Linux `11941316300`.

The Windows lifecycle checks establish package/registration behavior, not a visually inspected Windows 11 Explorer menu. Linux CI verifies headless rendering and CLI execution, not a physical desktop launcher session.
