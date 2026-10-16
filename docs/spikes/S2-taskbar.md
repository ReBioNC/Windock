# Spike S2 — Menyembunyikan taskbar dan AppBar

Tanggal: 2026-10-16 · Mesin: Windows 10 22H2 (19045), 1920×1080, skala 100%, taskbar asli: bawah, tidak auto-hide (`ABM_GETSTATE = 0`)
Kode: `spikes/S2.Taskbar` (`--scenario=N`, `--restore`, `--fullscreen`) · Probe otomatis: script PowerShell (status taskbar, work area, Notepad maximize, tombol Windows via `keybd_event`, kill paksa, screenshot)

## Jawaban lima pertanyaan

| # | Pertanyaan | Hasil |
|---|---|---|
| 1 | Hide + reserve 70, maximize: ada celah/tumpang tindih? | **Tidak.** Work area `(0,0)-(1920,1010)`; Notepad maximize berhenti di 1010 (+8 px bayangan bingkai standar) |
| 2 | Tombol Windows: Start terbuka? Taskbar ikut muncul? | Start **terbuka normal**. Dengan urutan final (lihat bawah) taskbar tetap tersembunyi; hanya kotak "Type here to search" yang terlihat selama Start terbuka. Setelah Start ditutup tidak ada sisa |
| 3 | Restart Explorer: `TaskbarCreated` diterima? | **Belum diuji** — restart Explorer menutup jendela File Explorer pengguna. Perilaku `TaskbarCreated` terdokumentasi; diuji pengguna di checklist akhir |
| 4 | Kill paksa lalu `--restore`: setelan asli kembali? | **Ya.** Setelah kill: taskbar tetap tersembunyi, `state=1`, work area masih 1010 (AppBar milik proses mati belum dilepas). Setelah `--restore`: `state=0`, taskbar tampil, work area kembali 1040 |
| 5 | `ABN_FULLSCREENAPP` saat ada jendela layar penuh? | **Ya:** `lParam=1` saat jendela layar penuh muncul, `lParam=0` saat tertutup |

## Temuan

1. **`SW_HIDE` langsung setelah `ABM_SETSTATE(ABS_AUTOHIDE)` dibatalkan Windows.** Transisi auto-hide memunculkan lagi `Shell_TrayWnd` (garis 2 px di tepi bawah, yang memunculkan taskbar saat kursor ke bawah — bentrok dengan dock). Menyembunyikan **±700 ms setelah** `ABM_SETSTATE` membuatnya tetap tersembunyi.
2. **Saat taskbar terlihat lagi,** sembunyikan ulang jika jendela foreground bukan Start/Search (`Windows.UI.Core.CoreWindow`) dan bukan taskbar itu sendiri. Dengan aturan ini Start tetap bisa dipakai.
3. **Badai `ABN_POSCHANGED`:** memanggil `ABM_SETPOS` di setiap `ABN_POSCHANGED` memicu ±20 notifikasi beruntun. `ABM_SETPOS` hanya dipanggil jika persegi hasil `ABM_QUERYPOS` berubah.
4. Jendela `HwndSource` tersembunyi (`WS_POPUP`, `WS_EX_TOOLWINDOW`, tidak pernah ditampilkan) bisa menjadi pemilik AppBar dan menerima callback.
5. Proses yang mati tanpa `ABM_REMOVE` meninggalkan ruang cadangan sampai `ABM_SETSTATE` berikutnya atau Explorer restart. Pemulihan saat start (Task 8/16) menanganinya.

## Urutan final (dipakai Task 15–16)

1. `RecoveryStateStore.Write(original)` → `ABM_SETSTATE(original | ABS_AUTOHIDE)`.
2. `AppBarHost` `ABM_NEW` + `ABM_QUERYPOS`/`ABM_SETPOS` (hanya jika persegi berubah).
3. Setelah 700 ms: `ShowWindow(SW_HIDE)` untuk `Shell_TrayWnd` + semua `Shell_SecondaryTrayWnd`, lalu aktifkan "jaga tetap tersembunyi".
4. Jaga tetap tersembunyi: saat `EVENT_OBJECT_SHOW` pada taskbar atau `EVENT_SYSTEM_FOREGROUND`, jika taskbar terlihat dan foreground bukan `Windows.UI.Core.CoreWindow`/`Shell_TrayWnd` → `SW_HIDE`.
5. Restore: matikan "jaga", `SW_SHOW`, `ABM_SETSTATE(original)`, `ABM_REMOVE`, hapus state.

Penundaan 700 ms dan logika "jaga tetap tersembunyi" berada di `Win32TaskbarController` (App), sehingga `TaskbarModeCoordinator` di Core tidak berubah.
