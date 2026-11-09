@echo off
cd /d "%~dp0"
if not exist "LiquidMac.exe" call build.bat
start "" "%~dp0LiquidMac.exe"
