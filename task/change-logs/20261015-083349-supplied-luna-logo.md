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

Headless rendering does not show native title bars or Explorer. Complete native menu/Setup packaging and native Linux execution require the Windows/Linux CI jobs; local checks do not register shell integration or install certificates. CI results will be recorded after the source push.
