# Spike S1 — Blur dan overlay transparan

Tanggal: 2026-10-16 · Mesin: Windows 10 22H2 (19045), 1920×1080, skala 100%, layar ±200 Hz
Kode: `spikes/S1.BlurOverlay` · Probe otomatis: script PowerShell (screenshot strip bawah, `WindowFromPoint`, klik `mouse_event`, CPU via `Get-Process`)

## Hasil per kriteria (ARCHITECTURE §13)

| Kriteria | Hasil | Bukti |
|---|---|---|
| Klik di area transparan tembus ke jendela belakang | **Lolos** | `WindowFromPoint` 110 px di atas plate → jendela aplikasi di belakang; di plate → overlay |
| Klik plate tidak mencuri fokus | **Lolos** | Notepad tetap foreground setelah klik di plate (2/2 percobaan). Kegagalan awal ternyata klik meleset ke taskbar/area transparan |
| Tidak muncul di Alt+Tab | Lolos berdasarkan style | `WS_EX_TOOLWINDOW` terpasang di kedua jendela; tidak dicek lewat UI Alt+Tab |
| CPU diam ±0% | **Lolos** | 0,000 detik CPU dalam 5 detik setelah handler `Rendering` dilepas |
| Region blur mengikuti plate di 60 fps tanpa tertinggal | **Lolos dengan perubahan desain** (lihat bawah) | ±198 fps, 0 frame > 8 ms, max kerja per frame 2,8 ms |
| Skala DPI 125% | **Belum diuji** | Mengubah skala tampilan adalah setelan sistem; diuji pengguna saat uji akhir |

## Temuan

1. **Blur sistem mengabaikan `SetWindowRgn` jika jendela tidak berubah ukuran.** Backdrop selebar 640 dengan region rounded-rect tetap ter-blur penuh 640 px (region hanya memotong konten, bukan efek DWM). Berlaku untuk jendela WPF layered maupun non-layered.
2. **Blur mengikuti ukuran jendela.** Memindah dan me-resize jendela backdrop ke persegi plate setiap frame (`SetWindowPos`) memberi blur yang pas, dan dengan region rounded-rect sudutnya ikut membulat (`ACCENT_ENABLE_BLURBEHIND = 3`).
3. **Me-resize jendela WPF tiap frame terlalu mahal:** 676 dari 916 frame > 8 ms, puncak 245 ms, karena WPF me-layout dan me-render ulang jendela itu di setiap `WM_SIZE`.
4. **Jendela Win32 mentah (`CreateWindowEx`, `WS_POPUP`, tanpa WPF) sebagai backdrop menyelesaikannya:** 0 frame > 8 ms, tampilan sama.
5. **Acrylic (`ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`) mengabaikan region:** sudut persegi terlihat di luar plate yang membulat. Tidak layak tampil.
6. Working set spike WPF kosong: ±116–123 MB (Task Manager "Memory" = private working set, biasanya lebih kecil). Angka sungguhan dicek di Task 18 terhadap N-01.

## Keputusan

- `BackdropWindow` diimplementasikan sebagai **jendela Win32 mentah** (bukan `Window` WPF), dipindah/di-resize ke persegi plate setiap frame dengan `SetWindowPos(..., hWndInsertAfter = DockWindow, SWP_NOACTIVATE)`, lalu `SetWindowRgn` rounded-rect radius 16.
- Mode blur v0.1: **Mati** dan **Blur standar** saja. Nilai `Acrylic` di `settings.json` tetap dibaca (kompatibilitas) tetapi diperlakukan sebagai Blur standar dan tidak ditawarkan di Pengaturan.
- Overlay (`DockWindow`): WPF `AllowsTransparency=True` + `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, terbukti sesuai.
