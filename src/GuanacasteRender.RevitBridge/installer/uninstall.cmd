@echo off
rem Removes the Revit add-in after all Revit windows have been closed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
