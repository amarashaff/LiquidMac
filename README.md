# LiquidMac

[![Donasi via Saweria](https://img.shields.io/badge/Donasi-Saweria-faae2b?style=for-the-badge)](https://saweria.co/aashaf)

Menu bar, Dynamic Island, Dock, Launchpad, dan tombol jendela "traffic light" gaya macOS (Liquid Glass) untuk **Windows 11**. Satu berkas `.exe` kecil (~250 KB) dan hemat RAM (sekitar 45 MB).

## Pasang

**Cara 1: satu perintah (disarankan)**

Buka **PowerShell** (tidak perlu admin), tempel, lalu tekan Enter:

```powershell
irm https://raw.githubusercontent.com/amarashaff/LiquidMac/main/install.ps1 | iex
```

Perintah ini mengunduh rilis terbaru, membuat pintasan di menu Start, mengaktifkan autostart, lalu menjalankan LiquidMac. Pemasang juga akan bertanya apakah Anda ingin Explorer transparan ([ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica)). Opsi itu butuh izin admin.

**Cara 2: unduh exe**

Unduh `LiquidMac.exe` dari halaman [Releases](https://github.com/amarashaff/LiquidMac/releases/latest), simpan di folder tetap (misalnya `Documents\LiquidMac`), lalu klik dua kali. Saat pertama kali dijalankan, LiquidMac membuka jendela Pengaturan dan mengaktifkan autostart.

> Windows SmartScreen bisa memperingatkan karena exe tidak ditandatangani. Klik **More info → Run anyway**.

## Fitur

- **Menu bar atas**: bisa sembunyi otomatis. Panel Wi-Fi, suara, baterai, dan Control Center dibuat sendiri, tidak memakai panel bawaan Windows.
- **Dynamic Island** dengan kartu yang bisa digeser:
  - Kartu: media (YouTube, Spotify, dan lain-lain), jam & statistik, notifikasi, timer, cuaca.
  - Slider volume.
  - HUD volume, kecerahan, dan pengisian daya. Pop-up bawaan Windows disembunyikan.
- **Dock**: ikon bisa ditambah atau dihapus, tiap profil Chrome punya ikon sendiri, dan jendela yang diminimalkan bisa dipulihkan.
- **Launchpad**: dibuka dengan tombol Windows. Berisi semua aplikasi plus pencarian.
- **Traffic light**: tombol merah/kuning/hijau menggantikan tombol jendela bawaan, warnanya menyesuaikan baris judul. Juga bekerja di aplikasi Electron (VS Code, Discord, dan sejenisnya).
- **Notifikasi** gaya macOS, ikon Explorer & suara sistem gaya macOS, dan opsi desktop bersih.
- **Pembaruan otomatis**: LiquidMac memeriksa rilis baru di GitHub dan memasangnya sendiri (bisa dimatikan).
- Semua fitur bisa dinyalakan atau dimatikan di jendela **Pengaturan**: menu Start → LiquidMac, atau ikon di system tray.

## Dukung pengembangan

LiquidMac gratis. Kalau bermanfaat, Anda bisa mentraktir pengembangnya lewat **[Saweria](https://saweria.co/aashaf)** (QRIS, GoPay, OVO, DANA, transfer bank). Tautan yang sama ada di menu tray dan di jendela Pengaturan LiquidMac.

## Copot

```powershell
irm https://raw.githubusercontent.com/amarashaff/LiquidMac/main/uninstall.ps1 | iex
```

Perintah ini mengembalikan suara, ikon, taskbar, dan notifikasi Windows, lalu menghapus LiquidMac.

## Build sendiri

Tidak perlu Visual Studio: cukup .NET Framework 4 yang sudah ada di Windows.

```bat
build.bat
```

Hasil build-nya adalah `LiquidMac.exe` di folder yang sama.

## Catatan

- Hanya diuji di Windows 11 (24H2/25H2) dengan skala layar 100–150%.
- Ikon, suara, dan grafik dibuat sendiri oleh program. Tidak ada aset milik Apple.
- ExplorerBlurMica adalah proyek terpisah milik Maplespe (LGPL-3.0). Pemasang hanya mengunduhnya dari rilis resmi.
