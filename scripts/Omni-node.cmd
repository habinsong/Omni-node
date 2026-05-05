@echo off
set SCRIPT_DIR=%~dp0
pwsh -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Omni-node.ps1" %*
if %ERRORLEVEL% EQU 9009 powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Omni-node.ps1" %*
