@echo off
rem SupporterGate Bot launcher. Double-click to start. See run.ps1 for details.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
