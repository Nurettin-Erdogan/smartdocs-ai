@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-smartdocs.ps1" -TarayiciyiAc
if errorlevel 1 pause
