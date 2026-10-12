@echo off
rem Double-click to install the Outatime test build (see Install.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if errorlevel 1 pause
