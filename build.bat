@echo off
setlocal
title Build LiquidMac
cd /d "%~dp0"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

echo Mengompilasi LiquidMac.exe ...
set "RES="
for %%f in ("%~dp0assets\fonts\*.ttf") do call set RES=%%RES%% /resource:"%%f",font.%%~nxf
for %%f in ("%~dp0assets\cursors\regular\*.cur" "%~dp0assets\cursors\regular\*.ani") do call set RES=%%RES%% /resource:"%%f",cur.regular.%%~nxf
for %%f in ("%~dp0assets\cursors\large\*.cur" "%~dp0assets\cursors\large\*.ani") do call set RES=%%RES%% /resource:"%%f",cur.large.%%~nxf
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe ^
  /out:"%~dp0LiquidMac.exe" ^
  /win32manifest:"%~dp0app.manifest" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Xml.dll ^
  /r:"C:\Windows\System32\WinMetadata\Windows.Media.winmd" /r:"C:\Windows\System32\WinMetadata\Windows.Foundation.winmd" /r:"C:\Windows\System32\WinMetadata\Windows.Storage.winmd" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll" ^
  %RES% "%~dp0src\*.cs"

if errorlevel 1 (
  echo.
  echo GAGAL dikompilasi.
  pause
  exit /b 1
)

echo.
echo BERHASIL: %~dp0LiquidMac.exe
echo.
endlocal
