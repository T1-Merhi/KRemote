@echo off
REM KRemote Installer Build Script (Batch Wrapper)
REM This batch file makes it easier to run the PowerShell build script

setlocal enabledelayedexpansion

echo.
echo ========================================
echo KRemote Installer Builder
echo ========================================
echo.

REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0

REM Run the PowerShell script
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!SCRIPT_DIR!Build-Installer.ps1" %*

pause
