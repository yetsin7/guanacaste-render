@echo off
rem Installs the Revit exporter; unsafe in-process Explorer thumbnails remain disabled.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
