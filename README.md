# Windock

Dock ala macOS untuk Windows 10, dibuat dengan C# WPF di .NET 8.

> Status: **v0.6.0**. Bisa dipakai harian sebagai pengganti taskbar.

## Baru di v0.6

Semua fitur baru punya opsi sendiri di Pengaturan dan langsung diterapkan.

- **Area status** di ujung kanan dock (halaman *Area status*): tombol ^ yang memunculkan tray Windows asli sebentar beserta ikon tersembunyinya, jaringan, volume (gulir untuk mengatur, klik tengah untuk bisukan, klik untuk slider), baterai, bahasa input, jam dengan kalender, dan pusat notifikasi. Tiap tombol bisa dimatikan; jam bisa 24/12 jam, dengan tanggal dan detik
- **Launcher** `Alt+Space` (halaman *Launcher*): ketik nama aplikasi, Enter untuk membuka. Bisa ikut mencari file di Desktop dan Documents; shortcut dan jumlah hasil bisa diganti
- **Tema** (halaman *Tema*): warna latar dock (preset atau `#RRGGBB`), ikuti warna wallpaper, kebulatan sudut, garis tepi, warna titik penanda, dan warna aksen untuk menu dan Pengaturan
- **Recycle Bin pintar**: seret file ke ikon Recycle Bin untuk membuangnya (tetap bisa dipulihkan), ikon penuh/kosong, klik kanan › *Kosongkan Recycle Bin* (dengan konfirmasi Windows)
- **Ikon minta perhatian**: saat aplikasi berkedip (mis. pesan masuk), ikonnya memantul atau diberi titik merah sampai dibuka
- **Sembunyikan aplikasi**: klik kanan aplikasi yang jalan › *Jangan tampilkan di dock*; daftar dan tombol *Tampilkan lagi* ada di Isi Dock
- **Ikon memantul saat aplikasi dibuka** dan **jalan otomatis saat Windows nyala** (halaman *Perilaku*)

## Fitur dasar

- Dock mengambang di tengah bawah layar dengan latar blur, ikon yang membesar saat kursor lewat, dan tooltip nama aplikasi
- Aplikasi favorit (pin) dan aplikasi yang sedang jalan, dengan titik penanda per jendela (maks. 3); Recycle Bin di ujung kanan
- Arahkan kursor ke ikon aplikasi yang jalan untuk melihat pratinjau live tiap jendelanya (seperti taskbar Windows); klik pratinjau untuk pindah, ✕ untuk menutup
- Klik untuk membuka, mengaktifkan, atau me-minimize; `Shift`+klik untuk jendela baru; klik kanan untuk menu
- Atur isi dock dengan drag and drop: seret program atau shortcut dari Desktop/Explorer ke dock, seret ikon untuk mengurutkan, seret ke atas keluar dock untuk menghapus
- Klik kanan program atau shortcut di Explorer › **Pin ke Windock**
- Tombol **Mode Windock / Mode Windows** di Pengaturan: Mode Windows mengembalikan tampilan Windows asli (taskbar seperti semula, dock disembunyikan) tanpa menghapus pin atau pengaturan; mode terakhir diingat
- Menggantikan taskbar Windows: taskbar disembunyikan selama Windock jalan, dan dikembalikan saat keluar, crash, atau saat Windock dijalankan lagi setelah dimatikan paksa
- Dock bisa sembunyi otomatis (Pengaturan › Perilaku): **Tidak pernah**, **Saat jendela menutupi dock** (mis. di-maximize), atau **Selalu** seperti Mac; sentuh tepi bawah layar untuk memunculkannya. Saat dock bisa sembunyi, jendela maximize memakai layar penuh
- Dock hilang saat ada game atau video layar penuh
- Semua opsi bisa diatur dari jendela Pengaturan (klik ikon Windock di system tray) dan langsung terlihat
- Ringan: ±50 MB RAM di Task Manager, CPU 0% saat diam
- Aman: jalan sebagai user biasa, tidak mengubah file sistem, dan tidak menghapus file

**Menyusul:** posisi dock kiri/kanan dan berpindah antarjendela dengan klik berulang.

## Instal

Butuh **Windows 10 22H2 (x64)** dan **.NET 8 SDK** (untuk build). Dari folder repo:

```bash
powershell -ExecutionPolicy Bypass -File scripts/install.ps1 -Launch
```

Windock terpasang di `%LocalAppData%\Programs\Windock` (tanpa hak Administrator), dengan shortcut **Windock** dan **Pengaturan Windock** di Desktop dan Start Menu. Menjalankan script yang sama lagi akan memperbarui Windock. Untuk menghapus: `scripts/uninstall.ps1` (ikut menghapus menu klik kanan Explorer dan entri startup; tambah `-RemoveSettings` untuk ikut menghapus pengaturan).

## Membuka Pengaturan

Saat taskbar disembunyikan, ikon tray ikut tersembunyi. Pengaturan bisa dibuka lewat:

- shortcut **Pengaturan Windock** di Desktop atau Start Menu (atau menjalankan `Windock.exe` lagi),
- klik kanan area kosong di dock › **Pengaturan Dock…**,
- ikon Windock di dock saat jendela Pengaturan sedang terbuka.

Semua perubahan langsung diterapkan dan disimpan; tidak ada tombol Simpan.

## Menjalankan dari source

Dari source:

```bash
dotnet run --project src/Windock
```

Build satu file exe:

```bash
dotnet publish src/Windock -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/v0.6.0
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
| [docs/superpowers/specs/2026-10-18-windock-v0.6-design.md](docs/superpowers/specs/2026-10-18-windock-v0.6-design.md) | Desain fitur v0.6 |
| [docs/spikes/](docs/spikes/) | Hasil percobaan teknis (blur, menyembunyikan taskbar) |
| [docs/testing/v0.1-manual-checklist.md](docs/testing/v0.1-manual-checklist.md) | Checklist uji manual v0.1 |

## Struktur

```
src/Windock.Core   logika murni (pengaturan, model dock, tata letak, animasi) — diuji unit test
src/Windock        aplikasi WPF (jendela dock, Pengaturan, tray, panggilan Win32 di Interop/)
tests/             xUnit untuk Windock.Core
spikes/            kode percobaan, tidak ikut dirilis
```
