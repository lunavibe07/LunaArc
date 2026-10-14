@echo off
setlocal
if not exist "%~dp0luna-cli.exe" (
    echo Missing luna-cli.exe. Keep this file alongside the LunaArc executables.
    pause
    exit /b 1
)
"%~dp0luna-cli.exe" unregister-shell --if-path "%~dp0."
if errorlevel 1 (
    echo Context menu removal failed.
    pause
    exit /b 1
)
echo.
echo LunaArc context menus have been removed.
pause
