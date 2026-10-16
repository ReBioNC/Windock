# Windock

Dock ala macOS untuk Windows 10, dibuat dengan C# WPF di .NET 8.

> Status: **v0.1 (dock inti)**. Bisa dipakai harian; beberapa fitur masih menunggu Fase 2 (lihat di bawah).

## Fitur v0.1

- Dock mengambang di tengah bawah layar dengan latar blur, ikon yang membesar saat kursor lewat, dan tooltip nama aplikasi
- Aplikasi favorit (pin) dan aplikasi yang sedang jalan, dengan titik penanda; Recycle Bin di ujung kanan
- Klik untuk membuka, mengaktifkan, atau me-minimize; `Shift`+klik untuk jendela baru; klik kanan untuk menu
- Menggantikan taskbar Windows: taskbar disembunyikan selama Windock jalan, dan dikembalikan saat keluar, crash, atau saat Windock dijalankan lagi setelah dimatikan paksa
- Jendela yang di-maximize berhenti di atas dock; dock hilang saat ada game atau video layar penuh
- Semua opsi bisa diatur dari jendela Pengaturan (klik ikon Windock di system tray) dan langsung terlihat
- Ringan: ±55 MB RAM di Task Manager, CPU 0% saat diam
- Aman: jalan sebagai user biasa, tidak mengubah file sistem, dan tidak menghapus file

**Menyusul di Fase 2:** ikon memantul saat aplikasi dibuka, dock auto-hide, posisi kiri/kanan, menggeser urutan ikon langsung di dock, berpindah antarjendela dengan klik berulang, dan jalan otomatis saat Windows nyala.

## Menjalankan

Butuh **Windows 10 22H2 (x64)** dan **.NET 8 Desktop Runtime**.

Dari source:

```bash
dotnet run --project src/Windock
```

Build satu file exe:

```bash
dotnet publish src/Windock -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/v0.1.0
```

Saat pertama kali dijalankan, Windock mem-pin File Explorer dan aplikasi umum yang terpasang (Chrome, Edge, Discord, Steam, Spotify). Pengaturan dan log tersimpan di `%AppData%\Windock`.

## Kalau taskbar Windows tidak muncul lagi

Windock mengembalikan taskbar saat keluar, saat crash, dan saat dijalankan lagi setelah dimatikan paksa. Kalau taskbar tetap tidak muncul:

1. Tekan `Ctrl+Alt+Shift+W` (shortcut darurat Windock).
2. Kalau belum muncul juga, buka Task Manager (`Ctrl+Shift+Esc`), cari **Windows Explorer**, lalu klik **Restart**.
3. Kalau taskbar masih auto-hide, buka **Settings › Personalization › Taskbar**, lalu matikan **Automatically hide the taskbar in desktop mode**.

## Dokumentasi

| Dokumen | Isi |
|---|---|
| [docs/PRD.md](docs/PRD.md) | Tujuan, kebutuhan, risiko, roadmap |
| [docs/DESIGN.md](docs/DESIGN.md) | Spesifikasi visual, animasi, interaksi, nilai default, jendela Pengaturan |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Komponen, alur data, taskbar dan AppBar, error handling, testing |
| [docs/spikes/](docs/spikes/) | Hasil percobaan teknis (blur, menyembunyikan taskbar) |
| [docs/testing/v0.1-manual-checklist.md](docs/testing/v0.1-manual-checklist.md) | Checklist uji manual v0.1 |

## Struktur

```
src/Windock.Core   logika murni (pengaturan, model dock, tata letak, animasi) — diuji unit test
src/Windock        aplikasi WPF (jendela dock, Pengaturan, tray, panggilan Win32 di Interop/)
tests/             xUnit untuk Windock.Core
spikes/            kode percobaan, tidak ikut dirilis
```
