# LiquidMac installer
# Pakai:  irm https://raw.githubusercontent.com/amarashaff/LiquidMac/main/install.ps1 | iex
# Opsi (set sebelum menjalankan):
#   $env:LIQUIDMAC_BLUR = "1"   -> sekalian pasang ExplorerBlurMica (Explorer transparan, butuh admin)
#   $env:LIQUIDMAC_BLUR = "0"   -> lewati tanpa bertanya

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

$repo = 'amarashaff/LiquidMac'
$dir  = Join-Path $env:LOCALAPPDATA 'Programs\LiquidMac'
$exe  = Join-Path $dir 'LiquidMac.exe'

Write-Host ''
Write-Host '  LiquidMac - menu bar, Dynamic Island & Dock gaya macOS untuk Windows 11' -ForegroundColor Magenta
Write-Host ''

if ([Environment]::OSVersion.Version.Build -lt 22000) {
    Write-Host '  Peringatan: LiquidMac dirancang untuk Windows 11. Di Windows 10 sebagian fitur tidak berjalan.' -ForegroundColor Yellow
}

# 1. hentikan versi lama dengan rapi
$running = Get-Process LiquidMac -ErrorAction SilentlyContinue
if ($running) {
    Write-Host '  - Menutup LiquidMac yang sedang berjalan...'
    $old = $running[0].Path
    if ($old) { Start-Process $old -ArgumentList '--exit' -Wait -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    Get-Process LiquidMac -ErrorAction SilentlyContinue | Stop-Process -Force
}

# 2. unduh rilis terbaru
Write-Host '  - Mengunduh rilis terbaru dari GitHub...'
$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'LiquidMac-installer' }
$asset = $rel.assets | Where-Object { $_.name -eq 'LiquidMac.exe' } | Select-Object -First 1
if (-not $asset) { throw 'Berkas LiquidMac.exe tidak ditemukan di rilis terbaru.' }
New-Item -ItemType Directory -Force $dir | Out-Null
Invoke-WebRequest $asset.browser_download_url -OutFile $exe -UseBasicParsing
Unblock-File $exe
Write-Host "    versi $($rel.tag_name) -> $dir"

# 3. pintasan Start menu
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\LiquidMac.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.Arguments = '--settings'
$sc.WorkingDirectory = $dir
$sc.Description = 'Pengaturan LiquidMac'
$sc.Save()

# 4. jalan otomatis saat Windows mulai
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LiquidMac' -Value "`"$exe`""

# 5. opsional: ExplorerBlurMica (proyek terpisah, github.com/Maplespe/ExplorerBlurMica)
$blur = $env:LIQUIDMAC_BLUR
if ($blur -ne '0' -and $blur -ne '1') {
    try { $ans = Read-Host '  Pasang juga Explorer transparan (ExplorerBlurMica, butuh izin admin)? [y/N]' } catch { $ans = 'n' }
    $blur = if ($ans -match '^(y|ya)$') { '1' } else { '0' }
}
if ($blur -eq '1' -and -not (Test-Path 'C:\Program Files\ExplorerBlurMica\ExplorerBlurMica.dll')) {
    Write-Host '  - Memasang ExplorerBlurMica...'
    $zip = Join-Path $env:TEMP 'ExplorerBlurMica.zip'
    $ex  = Join-Path $env:TEMP 'ExplorerBlurMica'
    $er  = Invoke-RestMethod 'https://api.github.com/repos/Maplespe/ExplorerBlurMica/releases/latest' -Headers @{ 'User-Agent' = 'LiquidMac-installer' }
    $ea  = $er.assets | Where-Object { $_.name -like '*x64*.zip' } | Select-Object -First 1
    Invoke-WebRequest $ea.browser_download_url -OutFile $zip -UseBasicParsing
    if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
    Expand-Archive $zip $ex -Force
    $dll = Get-ChildItem $ex -Recurse -Filter 'ExplorerBlurMica.dll' | Select-Object -First 1
    $src = $dll.DirectoryName
    $cmd = "New-Item -ItemType Directory -Force 'C:\Program Files\ExplorerBlurMica' | Out-Null; " +
           "Copy-Item '$src\*' 'C:\Program Files\ExplorerBlurMica' -Recurse -Force; " +
           "Start-Process regsvr32.exe -ArgumentList '/s','`"C:\Program Files\ExplorerBlurMica\ExplorerBlurMica.dll`"' -Wait; " +
           "Stop-Process -Name explorer -Force"
    try { Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile', '-Command', $cmd }
    catch { Write-Host '    Dilewati (izin admin ditolak).' -ForegroundColor Yellow }
}

# 6. jalankan
Start-Process $exe
Write-Host ''
Write-Host '  Selesai! LiquidMac sudah berjalan dan akan aktif otomatis setiap Windows menyala.' -ForegroundColor Green
Write-Host '  Pengaturan: klik ikon LiquidMac di menu Start, atau ikon di system tray.'
Write-Host '  Copot: irm https://raw.githubusercontent.com/amarashaff/LiquidMac/main/uninstall.ps1 | iex'
Write-Host ''
