# Windock

Dock ala macOS untuk Windows 10, dibuat dengan C# WPF di .NET 8.

> Status: tahap perencanaan. Belum ada build yang bisa dipakai.

## Fitur yang direncanakan (v1.0)

- Dock mengambang di tengah bawah layar, dengan ikon yang membesar saat kursor lewat dan memantul saat aplikasi dibuka
- Aplikasi favorit (pin) dan aplikasi yang sedang jalan, dengan titik penanda
- Menggantikan taskbar Windows: taskbar disembunyikan selama Windock jalan, dan dikembalikan saat keluar
- Semua tampilan dan perilaku bisa diatur dari jendela Pengaturan, dan langsung terlihat tanpa restart
- Ringan: target di bawah 100 MB RAM, dan CPU sekitar 0% saat diam
- Aman: jalan sebagai user biasa, tidak mengubah file sistem, dan tidak menghapus file

Fase berikutnya: menu bar atas, Launchpad, Spotlight, dan animasi jendela.

## Dokumentasi

| Dokumen | Isi |
|---|---|
| [docs/PRD.md](docs/PRD.md) | Tujuan, kebutuhan fungsional dan non-fungsional, risiko, roadmap |
| [docs/DESIGN.md](docs/DESIGN.md) | Spesifikasi visual, animasi, interaksi, nilai default, jendela Pengaturan |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Komponen, alur data, taskbar dan AppBar, pengaturan, error handling, testing |

## Kalau taskbar Windows tidak muncul lagi

Windock mengembalikan taskbar saat keluar, saat crash, dan saat dijalankan lagi setelah dimatikan paksa. Kalau taskbar tetap tidak muncul:

1. Tekan `Ctrl+Alt+Shift+W` (shortcut darurat Windock).
2. Kalau belum muncul juga, buka Task Manager (`Ctrl+Shift+Esc`), cari **Windows Explorer**, lalu klik **Restart**.
3. Kalau taskbar masih auto-hide, buka **Settings › Personalization › Taskbar**, lalu matikan **Automatically hide the taskbar in desktop mode**.

## Syarat

- Windows 10 22H2 (x64)
- .NET 8 Desktop Runtime
