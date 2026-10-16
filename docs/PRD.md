# Windock — Product Requirements Document (PRD)

| | |
|---|---|
| Versi dokumen | 1.0 |
| Tanggal | 2026-10-16 |
| Status | Draft, menunggu review |
| Lingkup | Windock v1.0 (Dock) |
| Dokumen terkait | [DESIGN.md](DESIGN.md), [ARCHITECTURE.md](ARCHITECTURE.md) |

## 1. Latar belakang

Pengguna memakai Windows 10 (22H2, build 19045) dan ingin tampilan serta animasi seperti macOS. Pilihan yang ada kurang cocok:

- **Windhawk:** sebagian besar mod-nya untuk Windows 11.
- **Dock pihak ketiga (MyDockFinder, Nexus Dock):** berbayar atau tertutup, dan tidak bisa diatur sebebas yang diinginkan.
- **Theme patcher:** mengubah file sistem, sehingga berisiko membuat Windows rusak.

Windock adalah aplikasi buatan sendiri yang menghadirkan pengalaman ala macOS di Windows 10 secara aman. Windock tidak mengubah file sistem dan tidak butuh akses Administrator. Semua perilakunya bisa diatur dari dalam aplikasi. Proyek ini juga menjadi portfolio di GitHub.

## 2. Tujuan

1. Dock ala macOS yang menggantikan taskbar Windows untuk pemakaian sehari-hari.
2. Terasa "Mac": ikon membesar saat kursor lewat, ikon memantul saat aplikasi dibuka, efek blur, sudut membulat.
3. Ringan: tidak terasa di laptop, CPU hampir 0% saat diam.
4. Aman: tidak bisa merusak Windows atau file pengguna, dan selalu ada jalan untuk kembali ke tampilan normal.
5. Bisa dikustomisasi penuh: setiap perilaku dan tampilan dock diatur dari jendela Pengaturan, dan perubahan langsung terlihat.

## 3. Bukan tujuan v1.0

Bagian-bagian ini sengaja ditunda ke fase berikutnya (lihat bagian 9):

- Menu bar di atas layar
- Launchpad dan Spotlight
- Animasi jendela (Scale atau Genie saat membuka atau minimize)
- Dukungan multi-monitor. v1.0 hanya di monitor utama.
- Pin aplikasi Microsoft Store (UWP) ke dock. Aplikasi UWP yang sedang jalan tetap tampil, tetapi pin-nya belum didukung.
- Drag file dari Explorer ke dock, dan drag ikon keluar dock untuk menghapusnya
- Folder atau stack di dock, serta badge notifikasi
- Mengganti tombol jendela merah-kuning-hijau, font sistem, atau menu aplikasi lain

## 4. Pengguna

**Pengguna utama:** pemilik proyek. Memakai laptop gaming (Ryzen 9 5900HX, RAM 16 GB, RTX 3060) dengan Windows 10, dan sering memakai Chrome, Discord, Steam, dan game layar penuh. Terbiasa dengan C#.

**Pengguna sekunder:** pengunjung GitHub yang ingin mencoba dock ala macOS di Windows 10.

## 5. Kebutuhan fungsional

Prioritas: **P0** = wajib ada di v1.0, **P1** = harus ada di v1.0 tapi boleh menyusul di akhir fase.

### 5.1 Dock

| ID | Kebutuhan | Prioritas |
|---|---|---|
| F-01 | Dock tampil mengambang di tengah bawah monitor utama, dengan sudut membulat dan latar semi-transparan | P0 |
| F-02 | Dock berisi aplikasi yang di-pin (urutan tetap) diikuti aplikasi yang sedang jalan tapi tidak di-pin, dipisah garis pemisah. Ikon Recycle Bin selalu di ujung kanan | P0 |
| F-03 | Aplikasi yang sedang jalan ditandai titik kecil di bawah ikonnya | P0 |
| F-04 | Klik ikon aplikasi yang belum jalan akan membuka aplikasi itu | P0 |
| F-05 | Klik ikon aplikasi yang sedang jalan tapi tidak aktif akan memunculkan dan mengaktifkan jendela terakhirnya, termasuk me-restore jika sedang minimize | P0 |
| F-06 | Klik ikon aplikasi yang jendelanya sedang aktif akan me-minimize jendela itu (perilaku Windows, yang lebih dikenal pengguna) | P0 |
| F-07 | Beberapa jendela dari satu aplikasi dikelompokkan dalam satu ikon. Jika aplikasi punya lebih dari satu jendela dan salah satunya aktif, klik berpindah ke jendela berikutnya, bukan minimize | P1 |
| F-08 | Klik kanan ikon membuka menu: Buka jendela baru, Simpan di Dock / Hapus dari Dock, Tampilkan di Explorer, Tutup semua jendela | P0 |
| F-09 | Ikon membesar halus mengikuti posisi kursor (magnification) | P0 |
| F-10 | Ikon memantul saat aplikasi sedang dibuka, sampai jendelanya muncul (maksimal 10 detik) | P1 |
| F-11 | Ikon yang di-pin bisa diurutkan ulang dengan drag di dalam dock | P1 |
| F-12 | Klik Recycle Bin membuka Recycle Bin di Explorer. Windock tidak pernah menghapus atau mengosongkan apa pun | P0 |
| F-13 | Jendela yang di-maximize tidak menutupi dock, karena ruang layar dicadangkan | P0 |
| F-14 | Dock otomatis tersembunyi saat ada aplikasi layar penuh (game, video) di monitor utama | P0 |
| F-15 | Dock tetap tampil di atas jendela biasa, tapi tidak muncul di Alt+Tab | P0 |

### 5.2 Taskbar Windows

| ID | Kebutuhan | Prioritas |
|---|---|---|
| T-01 | Mode "Sembunyikan total" (default): taskbar Windows disembunyikan selama Windock jalan | P0 |
| T-02 | Mode "Auto-hide": memakai auto-hide bawaan Windows | P1 |
| T-03 | Mode "Biarkan tampil": taskbar tidak disentuh | P0 |
| T-04 | Saat Windock keluar, baik normal maupun crash, taskbar dikembalikan ke kondisi semula | P0 |
| T-05 | Jika Windows Explorer di-restart, Windock menerapkan ulang mode taskbar dan pencadangan ruang layar | P0 |
| T-06 | Tombol Windows di keyboard tetap membuka Start Menu | P0 |

### 5.3 Pengaturan

Semua opsi ada di jendela Pengaturan dan langsung diterapkan tanpa restart. Nilai default ada di [DESIGN.md §6](DESIGN.md#6-nilai-default-pengaturan).

| ID | Opsi | Prioritas |
|---|---|---|
| S-01 | Posisi dock: bawah, kiri, kanan | P0 untuk bawah, P1 untuk kiri dan kanan |
| S-02 | Ukuran ikon (slider) | P0 |
| S-03 | Magnification: hidup/mati, dan ukuran maksimal | P0 |
| S-04 | Transparansi latar dock, dan mode blur: mati atau blur standar | P0 |
| S-05 | Titik penanda aplikasi jalan: hidup/mati | P0 |
| S-06 | Mode taskbar Windows: sembunyikan total, auto-hide, biarkan tampil | P0 |
| S-07 | Dock auto-hide (muncul saat kursor ke tepi layar) | P1 |
| S-08 | Animasi memantul saat membuka aplikasi: hidup/mati | P1 |
| S-09 | Jalankan saat Windows nyala | P1 |
| S-10 | Kelola pin: tambah (pilih .exe atau .lnk), hapus, dan atur urutan | P0 |
| S-11 | Shortcut darurat untuk mematikan dock, bisa diganti | P0 |
| S-12 | Tombol "Kembalikan ke default" | P0 |

### 5.4 Pengaman dan kontrol

| ID | Kebutuhan | Prioritas |
|---|---|---|
| A-01 | Ikon system tray dengan menu: Pengaturan, Jeda dock, Keluar | P0 |
| A-02 | Shortcut darurat global (default `Ctrl+Alt+Shift+W`) langsung mematikan Windock dan mengembalikan taskbar | P0 |
| A-03 | Hanya satu instance Windock yang bisa jalan. Membuka Windock kedua kali akan membuka Pengaturan | P0 |
| A-04 | Jika Windock mati paksa, misalnya lewat Task Manager, kondisi taskbar dipulihkan saat Windock dijalankan berikutnya. Ada juga cara manual yang tertulis di README | P0 |

## 6. Kebutuhan non-fungsional

| ID | Kebutuhan | Target |
|---|---|---|
| N-01 | RAM saat diam | di bawah 100 MB (target sekitar 60 MB) |
| N-02 | CPU saat diam | sekitar 0%, tanpa timer atau polling saat tidak ada interaksi |
| N-03 | Kehalusan animasi | 60 fps di monitor 60 Hz, tanpa kedip |
| N-04 | Waktu muncul setelah dijalankan | dock tampil kurang dari 2 detik |
| N-05 | Hak akses | selalu sebagai user biasa (`asInvoker`), tidak pernah meminta Administrator |
| N-06 | Lokasi penulisan | hanya `%AppData%\Windock\` dan `HKCU\...\Run`, yang kedua hanya jika S-09 aktif. Pengaturan auto-hide taskbar bisa ikut diubah sementara (lihat ARCHITECTURE §7) |
| N-07 | Penghapusan file | tidak pernah menghapus file di luar `%AppData%\Windock\` |
| N-08 | DPI | tajam di skala 100%, 125%, 150%, dan 175% (Per-Monitor DPI Aware v2) |
| N-09 | Platform | Windows 10 22H2 (build 19045) x64. Windows 11 belum diuji di v1.0 |
| N-10 | Runtime | .NET 8 Desktop Runtime |

## 7. Ukuran keberhasilan

1. Pengguna memakai Windock sebagai pengganti taskbar selama 7 hari berturut-turut tanpa kembali ke taskbar.
2. Tidak ada kejadian taskbar "hilang permanen" atau harus restart untuk pulih dalam pemakaian normal.
3. Angka di Task Manager memenuhi N-01 dan N-02 setelah 8 jam pemakaian.
4. Semua opsi S-01 sampai S-12 bisa diubah dan langsung terlihat tanpa restart.

## 8. Risiko dan asumsi

| Risiko | Dampak | Mitigasi |
|---|---|---|
| Blur di Windows 10 tidak bisa dibatasi ke bentuk dock, karena blur selalu mengenai seluruh jendela | Tampilan | Jendela latar blur dipisah dari jendela ikon (ARCHITECTURE §4). Spike S1 membuktikan ini sebelum dock dibangun penuh |
| Acrylic mengabaikan region (sudut persegi) dan bisa membuat geser jendela lambat | Tampilan | Hanya blur standar yang ditawarkan, dan blur bisa dimatikan (spike S1) |
| Menyembunyikan taskbar meninggalkan ruang kosong di layar, atau taskbar muncul lagi saat tombol Windows ditekan | Fungsi inti | Spike S2 menguji perilaku ini di Windows 10 22H2 sebelum implementasi |
| Windock mati paksa saat taskbar disembunyikan | Pengguna "terkunci" | Pemulihan saat Windock dijalankan lagi, restart Explorer, dan langkah manual di README |
| Antivirus atau SmartScreen memberi peringatan karena exe tidak ditandatangani | Kepercayaan | Kode terbuka di GitHub, dan tidak ada injeksi ke proses lain |
| Mengelompokkan jendela aplikasi UWP (ApplicationFrameHost) | Ikon salah | Kelompokkan berdasarkan AppUserModelID, dengan ikon generik sebagai cadangan |

## 9. Roadmap

| Fase | Isi | Rilis |
|---|---|---|
| 0. Spike | S1: blur dan jendela transparan yang bisa diklik tembus. S2: menyembunyikan taskbar dan pencadangan ruang layar di Windows 10 | — |
| 1. Dock inti | F-01–F-06, F-08, F-09, F-12–F-15, T-01, T-03–T-06, S-02–S-06, S-10–S-12, A-01–A-04 | v0.1 |
| 2. Pelengkap | F-07, F-10, F-11, T-02, S-01 (kiri dan kanan), S-07–S-09 | v1.0 |
| 3. Menu bar | Menu bar atas: nama aplikasi aktif, jam, baterai, wifi, volume | v1.1 |
| 4. Launchpad dan Spotlight | Grid aplikasi layar penuh dan pencarian cepat | v1.2 |
| 5. Animasi jendela | Scale saat membuka atau minimize, lalu Genie (opsional) | v2.0 |

Setiap fase 3 sampai 5 akan punya PRD dan desain sendiri sebelum dikerjakan.
