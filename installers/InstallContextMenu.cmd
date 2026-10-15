@echo off
setlocal
if not exist "%~dp0luna-cli.exe" (
    echo Missing luna-cli.exe. Extract the entire Windows ZIP before running this file.
    pause
    exit /b 1
)
"%~dp0luna-cli.exe" register-shell
if errorlevel 1 (
    echo Context menu installation failed.
    pause
    exit /b 1
)
echo.
echo LunaArc context menus are installed for your Windows account.
echo On Windows 11, you can also enable the main right-click menu below.
echo It asks for consent before trusting its menu signing certificate.
powershell.exe -NoLogo -NoProfile -Command "if ([Environment]::OSVersion.Version.Build -ge 22000) { exit 0 } else { exit 1 }"
if not errorlevel 1 (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0RegisterWindows11Menu.ps1"
    if errorlevel 1 echo The main menu was not enabled. The classic menu remains under Show more options.
)
echo Keep this folder in place. Run UninstallContextMenu.cmd before moving or deleting it.
pause
