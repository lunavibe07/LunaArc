# Explorer cascading and modern menu

Date: 2026-10-15, Asia/Bangkok.

## Request

Match the supplied WinRAR-style Explorer submenu: app-attributed LunaArc group, Open with LunaArc, Extract files, Extract to an archive-named folder and Extract Here. Support the Windows 11 main context menu as well as the classic fallback, with identical setup/portable features.

## Changes

- WinRegistryMenu migrates old separate verbs into ordered inline cascading groups, preserves default associations and unrelated verbs, and scopes uninstall to the owning GUI path/commands. Generic files/directories have two compression actions. The global file group excludes supported archive extensions.
- ExplorerRequest validates one path and a known GUI action. Studio waits until the owner window opens before showing dialogs. Extract files has a destination/password dialog; Here/folder use exact destinations and skip existing files by default, with options for protected inputs. Compression actions show options/save dialogs. Failed opens cannot accidentally extract a previously opened archive.
- WindowsContextMenu is a standalone x64 C++ COM DLL implementing IExplorerCommand and child enumeration with stable canonical identities, dynamic archive-folder captions, filesystem/type filtering, safe command quoting and lifetime tracking. It delegates work to Studio and does not inspect archive contents or run compression inside Explorer.
- Windows publishing builds/tests the DLL with CMake, creates a menu icon, packs a sparse identity MSIX and signs DLL/package. CI produces preview test certificates with no private-key export. Local full builds require an explicit signing-certificate thumbprint; managed-only local publishing is available separately.
- Portable modern registration verifies package/DLL signers before an explicit trust prompt; Setup offers an unchecked task that describes the same test-certificate trust. No root certificate, Developer Mode change or Explorer termination is used. Registration/unregistration track actual package external location and ownership; equal-version replacement backs up/restores the previous package on failure.
- Package completeness/lifecycle checks include all native menu payloads. README, AGENT and native-library notes describe preview trust, signing/build requirements and limits. Version is 1.3.0.

## Verification so far

- Local Debug build passed with zero errors/warnings; Release checks passed: 126 backend/filesystem, 57 isolated Registry and 45 rendered Studio checks.
- Rendered extraction-options frame inspected at .tools/ui-preview/extract-options.png. It verifies default destination handling, masked password, skip-existing behavior and cancellation.
- Menu-logo PNG/ICO generation and PowerShell parsing checked locally without installing trust or registering Explorer.
- Native C++ compiler/Windows SDK are unavailable locally. Native DLL contract checks, signed package generation and setup/portable lifecycle validation run in CI before delivery.
- CI compiled the native DLL and passed its 61 contract checks. Follow-ups corrected an explicit enum comparison, the DLL output name and PowerShell 5 parameter-default behavior.
- The signed MSIX carries a SHA-256 pin of the full signed native DLL. Registration checks that pin and matching signers; Windows authenticates the complete MSIX payload before package activation. This separates content verification from the user's certificate-trust decision rather than treating an untrusted certificate as a modified DLL.
- CI lifecycle checks now also register the modern package, activate its COM server, transfer equal-version ownership to a portable copy and verify scoped removal.
- Actual registration rejected CurrentUser/TrustedPeople with 0x800B0109. Preview trust therefore uses LocalMachine/TrustedPeople, with explicit user consent and an elevated certificate-only helper; package registration stays with the original user. The Setup task and documentation state this scope/administrator requirement. No local trust was installed.
- The preview sparse package registered successfully on the Windows CI runner with machine peer trust. WinRT ownership checks are delegated to Windows PowerShell 5 because PowerShell 7 does not project those Windows Runtime types.

## Limits

Single filesystem selection only; no multi-selection or folder-background menu. Modern menu requires Windows 11 and explicit trust of the preview signer. Production distribution needs a trusted publisher certificate. Native COM checks and CI packaging are not a visual test of Windows 11 Explorer; no native Explorer screenshot has been claimed. Local tests do not import certificates or alter real Explorer registration.

## Final CI verification and delivery

Run https://github.com/lunavibe07/LunaArc/actions/runs/38628778048 succeeded on both Windows and Linux for implementation commit 3e4fbcc.

- Native Windows and Linux Debug/Release builds, backend checks, rendered UI checks and self-contained publishes passed.
- Windows native extension compiled cleanly and passed 61 direct COM contract checks; registered COM activation/title/submenu flags also passed in the package lifecycle checks.
- Signed MSIX signer/identity and native DLL hash/signer pins passed verification. The preview package registered successfully with explicit machine Trusted People trust on the disposable CI runner.
- Complete setup and portable packages built. Tests verified byte equality, classic/modern registration, equal-version external-folder transfer, effective ownership, removal of the installed copy while preserving the portable menu, and final package/menu cleanup. The CI trust certificate was removed afterwards.
- Download artifacts: LunaArc-Windows-Setup, LunaArc-Windows-Portable and LunaArc-Windows-Checksums; UI preview artifacts and raw Windows/Linux publish folders are also available.

This final update only records completed verification and does not change the shipped implementation. Native Explorer visual interaction and the non-admin UAC consent path remain untested locally.
