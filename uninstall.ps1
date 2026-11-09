# LiquidMac uninstaller
# Pakai:  irm https://raw.githubusercontent.com/amarashaff/LiquidMac/main/uninstall.ps1 | iex
# Mengembalikan suara, ikon desktop, taskbar, ikon Explorer & notifikasi Windows, lalu menghapus LiquidMac.
# ExplorerBlurMica (jika terpasang) tidak disentuh; copot lewat "C:\Program Files\ExplorerBlurMica\uninstall.cmd".

$ErrorActionPreference = 'SilentlyContinue'
$dir = Join-Path $env:LOCALAPPDATA 'Programs\LiquidMac'
$exe = Join-Path $dir 'LiquidMac.exe'
$p = Get-Process LiquidMac | Select-Object -First 1
if ($p -and $p.Path) { $exe = $p.Path }

if (Test-Path $exe) {
    Write-Host '  - Mengembalikan pengaturan Windows...'
    if (-not (Get-Process LiquidMac)) { Start-Process $exe; Start-Sleep -Seconds 4 }
    Start-Process $exe -ArgumentList '--uninstall'
    $t = 0
    while ((Get-Process LiquidMac) -and $t -lt 60) { Start-Sleep -Seconds 1; $t++ }   # tunggu (mungkin ada dialog izin admin untuk ikon Explorer)
    Get-Process LiquidMac | Stop-Process -Force
}

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LiquidMac'
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\LiquidMac.lnk') -Force
Remove-Item $dir -Recurse -Force
Remove-Item (Join-Path $env:LOCALAPPDATA 'LiquidMac') -Recurse -Force

Write-Host '  LiquidMac sudah dicopot.' -ForegroundColor Green
