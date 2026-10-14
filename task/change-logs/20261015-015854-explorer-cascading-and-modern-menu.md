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

## Limits

Single filesystem selection only; no multi-selection or folder-background menu. Modern menu requires Windows 11 and explicit trust of the preview signer. Production distribution needs a trusted publisher certificate. Native COM checks and CI packaging are not a visual test of Windows 11 Explorer; no native Explorer screenshot has been claimed. Local tests do not import certificates or alter real Explorer registration.
