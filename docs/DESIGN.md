# Windock — Desain UI/UX

| | |
|---|---|
| Versi dokumen | 1.0 |
| Tanggal | 2026-10-16 |
| Status | Draft, menunggu review |
| Dokumen terkait | [PRD.md](PRD.md), [ARCHITECTURE.md](ARCHITECTURE.md) |

Semua ukuran di dokumen ini dalam DIP (device-independent pixel, 1 DIP = 1 px pada skala 100%). Ukuran fisik dikalikan dengan skala DPI monitor.

## 1. Prinsip desain

1. **Mirip macOS, bukan salinan piksel.** Ambil rasa dan gerakannya, lalu sesuaikan dengan kebiasaan Windows jika memang lebih nyaman. Contohnya F-06: klik ikon jendela aktif akan me-minimize jendela itu.
2. **Gerak dulu, hiasan belakangan.** Magnification dan memantul harus halus di 60 fps. Blur dan bayangan boleh dikurangi jika mengganggu kehalusan.
3. **Diam itu murah.** Tidak ada animasi atau proses yang jalan saat pengguna tidak berinteraksi dengan dock.
4. **Semua bisa diatur, default sudah bagus.** Pengguna tidak perlu membuka Pengaturan untuk mendapat tampilan yang baik.

## 2. Anatomi dock (posisi bawah)

```
                    ┌───────────────────────────────────────────────┐
                    │ [ikon] [ikon] [ikon] [ikon] │ [ikon] [ikon] │ [🗑] │   ← plate
                    │   •                    •       •                │   ← titik penanda
                    └───────────────────────────────────────────────┘
  ─────────────────────────────── tepi bawah layar ───────────────────────────────
```

| Elemen | Spesifikasi default |
|---|---|
| Plate (latar dock) | Tinggi = ukuran ikon + 2 × 6 (padding vertikal) + 6 (ruang titik penanda). Default 48 + 12 + 6 = **66** |
| Jarak plate ke tepi layar | 4 |
| Lebar plate | Mengikuti isi: jumlah ikon × (ukuran ikon + 8 jarak antarikon) + 2 × 8 padding horizontal + lebar pemisah |
| Sudut plate | Radius 16 |
| Warna plate | Hitam `#1E1E1E` dengan opacity 55% di atas blur. Garis tepi 1 DIP putih dengan opacity 12% |
| Blur | Radius blur bawaan sistem (lihat ARCHITECTURE §4) |
| Ikon | Persegi, ukuran default 48, digambar dari sumber 256 px dengan scaling kualitas tinggi |
| Jarak antarikon | 8 |
| Pemisah | Garis vertikal 1 DIP, tinggi 60% ikon, putih opacity 25%, jarak kiri-kanan 8 |
| Titik penanda | Lingkaran diameter 4, putih opacity 85%, 3 DIP di bawah ikon, rata tengah |
| Ikon Recycle Bin | Ikon sistem Recycle Bin (versi kosong atau penuh), selalu paling kanan setelah pemisah kedua |
| Tooltip nama aplikasi | Muncul 12 DIP di atas ikon setelah kursor diam 300 ms. Plate kecil gelap radius 6, teks 12 pt |

Posisi kiri dan kanan (Fase 2) memakai anatomi yang sama, diputar 90°: plate di tengah vertikal, titik penanda di sisi luar layar, tooltip di sisi dalam.

## 3. Magnification

Ikon membesar mengikuti jarak horizontal kursor ke pusat ikon (`d`, dalam DIP).

```
R      = 2.5 × ukuran ikon dasar          // jangkauan efek
t      = min(|d| / R, 1)
skala  = 1 + (Smax − 1) × cos²(t × π/2)   // 1 → Smax, halus di kedua ujung
Smax   = ukuranMagnification / ukuranIkon
```

- **Dengan default** (ikon 48, magnification 80): Smax ≈ 1,67, dan ikon tepat di bawah kursor tampil 80 DIP.
- **Ikon membesar ke atas**, ditambatkan di garis bawahnya, sehingga ikon "keluar" dari plate. Inilah alasan jendela dock lebih tinggi dari plate-nya (ARCHITECTURE §4).
- **Plate melebar** mengikuti total lebar ikon yang membesar, dengan pusatnya tetap di tengah. Tinggi plate tidak berubah.
- **Masuk dan keluar halus:** saat kursor masuk atau keluar area dock, nilai magnification digeser dari 0 ke 1 (atau sebaliknya) selama 150 ms dengan easing *ease-out cubic*. Ikon tidak boleh melompat.
- **Tanpa magnification:** jika S-03 mati, ikon tetap di ukuran dasar dan hanya ada sorotan tipis (putih opacity 10%) di belakang ikon saat kursor di atasnya.

## 4. Animasi

| Animasi | Spesifikasi |
|---|---|
| Memantul (membuka aplikasi) | Ikon naik setinggi 0,5 × ukuran ikon selama 300 ms (*ease-out quad*), lalu turun 300 ms (*ease-in quad*), jeda 100 ms, ulang. Berhenti saat jendela pertama aplikasi muncul atau setelah 10 detik. Pantulan yang sedang berjalan diselesaikan dulu sampai mendarat, tidak berhenti di udara |
| Ikon baru masuk dock | Lebar slot tumbuh dari 0 ke penuh dalam 200 ms, sementara ikon fade-in dari opacity 0 dan skala 0,6 |
| Ikon keluar dock | Kebalikan animasi masuk, 200 ms |
| Klik ikon | Ikon menggelap (brightness 70%) selama ditekan |
| Dock auto-hide | Plate dan ikon meluncur keluar layar dalam 250 ms (*ease-in cubic*) setelah kursor keluar 500 ms. Masuk kembali dalam 250 ms (*ease-out cubic*) saat kursor menyentuh 2 DIP terakhir di tepi layar |
| Drag urutkan pin | Ikon yang di-drag mengikuti kursor dengan opacity 80%, dan ikon lain bergeser memberi ruang dalam 150 ms |
| Aplikasi layar penuh | Dock langsung disembunyikan tanpa animasi, supaya tidak mengganggu game |

Semua animasi berjalan dari satu loop render (ARCHITECTURE §5) yang aktif hanya selama ada animasi atau kursor di area dock.

## 5. Interaksi

| Aksi | Hasil |
|---|---|
| Klik kiri, aplikasi belum jalan | Buka aplikasi, dan ikon memantul (jika S-08 aktif) |
| Klik kiri, aplikasi jalan, tidak aktif | Aktifkan jendela terakhirnya, dan restore jika minimize |
| Klik kiri, aplikasi jalan dengan satu jendela, sedang aktif | Minimize jendela itu |
| Klik kiri, aplikasi jalan dengan banyak jendela, salah satunya aktif | Berpindah ke jendela berikutnya, bukan minimize (Fase 2, F-07). Sebelum Fase 2: minimize jendela aktif |
| `Shift` + klik kiri | Buka jendela atau instance baru |
| Klik kanan | Menu konteks (lihat di bawah) |
| Drag ikon pin ke posisi lain | Ikon lain bergeser membuka celah; urutan pin berubah dan langsung disimpan (F-11) |
| Drag ikon pin ke atas, keluar dari dock (> 80 DIP) | Label "Hapus dari Dock" muncul; dilepas = pin dihapus dengan animasi memudar. Aplikasi yang masih jalan tetap tampil di bagian aplikasi jalan |
| Drag aplikasi jalan (belum di-pin) ke bagian pin | Aplikasi di-pin di posisi celah |
| Drag program (.exe) atau shortcut (.lnk) dari Explorer/Desktop ke dock | Celah terbuka di posisi kursor; dilepas = di-pin di situ. File asli tidak dipindah |
| Klik kanan program atau shortcut di Explorer/Desktop › **Pin ke Windock** | Di-pin di akhir daftar (opsi di Pengaturan › Isi Dock) |
| Klik Recycle Bin | Buka Recycle Bin di Explorer |
| Klik kanan di area plate kosong | Menu: Pengaturan Dock…, Kunci posisi pin, Keluar |

**Menu konteks ikon aplikasi:**

```
  Nama Aplikasi            (judul, tidak bisa diklik)
  ─────────────────
  Jendela 1 judul…         (daftar jendela yang terbuka, maks 10)
  Jendela 2 judul…
  ─────────────────
  Buka jendela baru
  Simpan di Dock  /  Hapus dari Dock
  Tampilkan di Explorer
  ─────────────────
  Tutup semua jendela      (hanya jika jalan; mengirim perintah close biasa, bukan kill)
```

Menu konteks memakai gaya gelap yang serasi dengan dock (radius 8, latar `#2A2A2A`, teks putih).

## 6. Nilai default pengaturan

| Opsi | Default | Rentang atau pilihan |
|---|---|---|
| Posisi dock | Bawah | Bawah, Kiri, Kanan |
| Ukuran ikon | 48 | 32–96 |
| Magnification | Hidup | Hidup / Mati |
| Ukuran magnification | 80 | ukuran ikon – 128 |
| Opacity latar | 55% | 0–100%, minimal 30% saat Blur = Mati |
| Blur | Blur standar | Mati, Blur standar (Acrylic tidak ditawarkan: mengabaikan sudut membulat, lihat spike S1) |
| Titik penanda | Hidup | Hidup / Mati |
| Mode taskbar Windows | Sembunyikan total | Sembunyikan total, Auto-hide, Biarkan tampil |
| Dock auto-hide | Mati | Hidup / Mati |
| Memantul saat membuka | Hidup | Hidup / Mati |
| Jalankan saat Windows nyala | Mati, bisa dihidupkan lewat ajakan saat pertama kali dijalankan | Hidup / Mati |
| Shortcut darurat | `Ctrl+Alt+Shift+W` | kombinasi tombol apa pun dengan minimal dua modifier |
| Pin awal | File Explorer, ditambah aplikasi yang terdeteksi dari daftar: Chrome, Edge, Discord, Steam, Spotify | — |

## 7. Jendela Pengaturan

Jendela biasa (bisa dipindah dan ditutup), 760 × 520, bertema gelap, dan dibuka dari ikon tray, menu dock, atau dengan menjalankan Windock lagi.

```
┌──────────────────────────────────────────────────────────────────┐
│  Pengaturan Windock                                       [ — ✕ ]│
├────────────────┬─────────────────────────────────────────────────┤
│  ▸ Tampilan    │  Tampilan                                       │
│    Perilaku    │                                                 │
│    Isi Dock    │  Posisi            ( Bawah ▾ )                  │
│    Pengaman    │  Ukuran ikon       ──────●──────────  48        │
│    Tentang     │  Magnification     [✓]                          │
│                │  Ukuran maksimal   ─────────●───────  80        │
│                │  Opacity latar     ───────●─────────  55%       │
│                │  Blur              ( Blur standar ▾ )           │
│                │  Titik penanda     [✓]                          │
│                │                                                 │
│                │                       [ Kembalikan ke default ] │
└────────────────┴─────────────────────────────────────────────────┘
```

| Halaman | Isi |
|---|---|
| Tampilan | S-01, S-02, S-03, S-04, S-05 |
| Perilaku | S-06, S-07, S-08, S-09 |
| Isi Dock | Daftar pin yang bisa di-drag untuk mengurutkan, dengan tombol Tambah (dialog pilih .exe atau .lnk) dan Hapus. S-10 |
| Pengaman | S-11 (kotak perekam shortcut), tombol "Pulihkan taskbar sekarang", tombol "Buka folder data" |
| Tentang | Versi, link GitHub, lokasi file pengaturan dan log |

Aturan perilaku:
- **Setiap perubahan langsung diterapkan ke dock** dan disimpan 500 ms setelah perubahan terakhir. Tidak ada tombol "Simpan".
- **Slider memperbarui dock secara live** selama digeser.
- **"Kembalikan ke default" meminta konfirmasi dulu.** Daftar pin tidak ikut direset; ada tombol terpisah di halaman Isi Dock.
- **Saat mode taskbar diubah ke "Sembunyikan total" untuk pertama kali,** muncul catatan singkat berisi shortcut darurat.

## 8. Ikon tray

Ikon Windock monokrom di system tray. Klik kiri membuka Pengaturan. Klik kanan membuka menu:

```
  Pengaturan…
  Jeda dock            (sembunyikan dock dan kembalikan taskbar sampai dilanjutkan)
  ─────────────
  Keluar
```

## 9. Pertimbangan aksesibilitas

- **Teks tooltip dan menu mengikuti ukuran teks sistem.**
- **Opacity latar minimal 30%** saat blur dimatikan, supaya ikon tetap terbaca di atas wallpaper yang terang.
- **Saat setelan Windows "Show animations" mati,** magnification tetap jalan, tetapi animasi memantul, auto-hide, dan masuk-keluar ikon menjadi instan.
