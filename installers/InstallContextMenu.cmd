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
echo On Windows 11, right-click an archive and select Show more options.
echo Keep this folder in place. Run UninstallContextMenu.cmd before moving or deleting it.
pause
