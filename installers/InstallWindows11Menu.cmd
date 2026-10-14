@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0RegisterWindows11Menu.ps1"
if errorlevel 1 (
    echo Windows 11 menu registration was cancelled or failed. The classic LunaArc submenu remains available.
    pause
    exit /b 1
)
pause
