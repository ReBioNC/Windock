# Windock — Arsitektur Teknis

| | |
|---|---|
| Versi dokumen | 1.0 |
| Tanggal | 2026-10-16 |
| Status | Draft, menunggu review |
| Dokumen terkait | [PRD.md](PRD.md), [DESIGN.md](DESIGN.md) |

## 1. Ringkasan

Windock adalah satu aplikasi desktop WPF di .NET 8 yang jalan sebagai user biasa. Aplikasi ini menggambar dock di jendela transparan selebar layar, mencadangkan ruang layar lewat API AppBar Windows, dan menyembunyikan taskbar Windows untuk sementara. Daftar jendela yang terbuka didapat dari event Windows (`SetWinEventHook`), bukan polling. Logika inti (model dock, tata letak, animasi, pengaturan) ada di library terpisah yang tidak bergantung pada WPF atau Win32, sehingga bisa diuji dengan unit test.

## 2. Teknologi

| Bagian | Pilihan | Alasan |
|---|---|---|
| Bahasa dan runtime | C# 12, .NET 8 (LTS) | Sudah terpasang (SDK 8.0.400), dan pengguna terbiasa dengan C# |
| UI | WPF | Dirender GPU lewat DirectX, mendukung jendela transparan per-piksel, ringan dibanding Electron |
| Interop Win32 | `Microsoft.Windows.CsWin32` (source generator) | Signature P/Invoke dibuat otomatis dari metadata resmi, sehingga mengurangi bug tanda tangan fungsi |
| Ikon tray | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms`) | Bawaan .NET, tanpa dependensi tambahan |
| Serialisasi | `System.Text.Json` | Bawaan .NET |
| Test | xUnit | Standar dan ringan |
| CI | GitHub Actions (`windows-latest`) | Build dan test setiap push |

Tidak ada library UI pihak ketiga di v1.0. Semua style dibuat sendiri.

## 3. Struktur solution

```
Windock/
├─ Windock.sln
├─ src/
│  ├─ Windock.Core/                  // tanpa WPF, tanpa Win32 — murni logika
│  │  ├─ Apps/          AppId, DockItem, DockModel
│  │  ├─ Layout/        DockLayoutEngine, Magnification, LayoutResult
│  │  ├─ Animation/     BounceAnimator, Tween, Easing
│  │  ├─ Settings/      DockSettings, SettingsStore, SettingsDefaults
│  │  └─ Abstractions/  IWindowSource, ILauncher, IClock, ...
│  └─ Windock/                       // aplikasi WPF
│     ├─ App.xaml(.cs)               // titik masuk, wiring, pemulihan
│     ├─ Interop/                    // satu-satunya tempat panggilan Win32
│     │  ├─ NativeMethods.txt        // daftar API untuk CsWin32
│     │  ├─ WindowTracker.cs
│     │  ├─ IconProvider.cs
│     │  ├─ AppBarService.cs
│     │  ├─ TaskbarService.cs
│     │  ├─ AppLauncher.cs
│     │  ├─ HotkeyService.cs
│     │  └─ WindowEffects.cs         // blur, region, extended style
│     ├─ Dock/          DockWindow, BackdropWindow, EdgeTriggerWindow, DockRenderer
│     ├─ Settings/      SettingsWindow + halaman-halamannya
│     └─ Tray/          TrayIcon
├─ tests/
│  └─ Windock.Core.Tests/
├─ spikes/                           // kode percobaan Fase 0, tidak ikut dirilis
└─ docs/
```

Aturan dependensi: `Windock` boleh bergantung pada `Windock.Core`, tapi tidak sebaliknya. Kode di luar `Interop/` tidak memanggil Win32 langsung.

## 4. Jendela dock

Dock terdiri dari tiga jendela milik Windock:

| Jendela | Ukuran | Isi | Gaya |
|---|---|---|---|
| `DockWindow` | Selebar monitor, tinggi = tinggi plate + jarak tepi + ruang magnification (`ukuranMagnification − ukuranIkon` + 16 untuk tooltip). Default 66 + 4 + 48 = 118 | Plate (warna, opacity, garis tepi), ikon, titik penanda, tooltip | `AllowsTransparency=True`, topmost, `WS_EX_TOOLWINDOW` (tidak muncul di Alt+Tab), `WS_EX_NOACTIVATE` (klik tidak mencuri fokus) |
| `BackdropWindow` | Sebesar plate maksimum | Hanya efek blur | Blur via `SetWindowCompositionAttribute` (`ACCENT_ENABLE_BLURBEHIND` atau `ACCENT_ENABLE_ACRYLICBLURBEHIND`). Bentuk dipotong dengan `SetWindowRgn` rounded-rect yang diperbarui tiap frame. Posisinya tepat di bawah `DockWindow` dalam z-order. Toolwindow dan noactivate |
| `EdgeTriggerWindow` | Strip 2 DIP di tepi layar | Kosong (alpha 1, tidak terlihat) | Hanya aktif saat dock auto-hide, untuk mendeteksi kursor menyentuh tepi |

**Kenapa blur dipisah:** di Windows 10, efek blur sistem selalu mengenai seluruh persegi jendela. Kalau blur dipasang di `DockWindow` yang selebar layar, seluruh strip bawah layar ikut buram. Dengan dipisah, blur hanya ada di bentuk plate, sementara ikon yang membesar tetap bisa keluar ke atas plate di jendela transparan. Ini divalidasi di Spike S1 (§13).

**Klik tembus:** di jendela layered WPF, piksel dengan alpha 0 meneruskan klik ke jendela di belakangnya. Area di luar plate dan ikon dibiarkan alpha 0. Supaya magnification tidak bergetar saat kursor berada di celah antara ikon yang membesar, `DockWindow` menggambar "zona aktif" tak terlihat (alpha 1) yang meliputi plate dan seluruh tinggi ikon yang sedang membesar. Zona ini menyusut kembali saat magnification selesai.

**Fokus:** karena `WS_EX_NOACTIVATE`, jendela aplikasi yang sedang aktif tetap aktif saat dock diklik. Dengan begitu Windock tahu jendela mana yang aktif (dibutuhkan F-06). Windock tetap boleh memanggil `SetForegroundWindow` karena prosesnya menerima input terakhir dari pengguna.

## 5. Tata letak dan loop render

Tata letak dihitung oleh `DockLayoutEngine` di Core sebagai fungsi murni:

```
LayoutResult Compute(
    IReadOnlyList<DockItemVisualState> items,   // per item: slotProgress (0..1), bounceOffset
    DockSettings settings,
    double? cursorAlongAxis,                    // null jika kursor di luar dock
    double magnifyProgress,                     // 0..1, untuk masuk/keluar halus
    Size screenWorkArea)
→ PlateRect, ActiveZoneRect, daftar ItemRect (posisi, ukuran, posisi titik penanda)
```

Rumus magnification ada di [DESIGN.md §3](DESIGN.md#3-magnification).

`DockRenderer` (WPF) menerima `LayoutResult` lalu memperbarui posisi dan ukuran elemen `Image` di sebuah `Canvas`. Region `BackdropWindow` juga diperbarui dari `PlateRect`.

**Loop render:** `DockRenderer` mendaftar ke `CompositionTarget.Rendering` (sekali per frame) hanya selama ada yang bergerak, yaitu salah satu kondisi berikut:
- kursor di dalam zona aktif,
- `magnifyProgress` sedang berubah,
- ada item yang sedang memantul, masuk, atau keluar,
- auto-hide sedang meluncur.

Saat semuanya diam, handler dilepas, sehingga tidak ada kerja per frame dan CPU kembali ke sekitar 0% (N-02). Waktu animasi memakai `IClock` (berbasis `Stopwatch`), bukan jumlah frame, sehingga kecepatan animasi sama di monitor 60 Hz dan 144 Hz.

## 6. Alur data

```
           Win32 events                                   settings.json
 (SetWinEventHook, TaskbarCreated,                             │
  ABN_*, WM_DISPLAYCHANGE, hotkey)                             ▼
            │                                           SettingsStore ──(SettingsChanged)──┐
            ▼                                                  │                            │
     WindowTracker ──(WindowAdded/Removed/                     │                            │
            │         ForegroundChanged/TitleChanged)          │                            │
            ▼                                                  ▼                            ▼
        DockModel  ◄───────────── pins dari settings ─────────┘                 AppBarService
            │  (ItemsChanged)                                                   TaskbarService
            ▼                                                                   DockWindow (ukuran)
   DockWindow / DockRenderer ◄── IconProvider (ikon, async)
            │
            │ klik / menu
            ▼
       AppLauncher ──► buka aplikasi / aktifkan / minimize / tutup
            │
            └──(LaunchStarted)──► DockModel memulai bounce item tsb
```

### 6.1 WindowTracker

- **Event yang didengar:** `SetWinEventHook` dengan `WINEVENT_OUTOFCONTEXT` untuk `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE`, `EVENT_OBJECT_DESTROY`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_SYSTEM_MINIMIZESTART`, `EVENT_SYSTEM_MINIMIZEEND`, `EVENT_OBJECT_CLOAKED`, dan `EVENT_OBJECT_UNCLOAKED`.
- **Saat start,** daftar awal diambil sekali dengan `EnumWindows`.
- **Jendela yang dihitung sebagai "jendela aplikasi"** harus memenuhi semua aturan berikut, mendekati aturan taskbar Windows:
  - top-level dan `IsWindowVisible`
  - tidak punya owner, kecuali punya `WS_EX_APPWINDOW`
  - tidak punya `WS_EX_TOOLWINDOW`, kecuali punya `WS_EX_APPWINDOW`
  - tidak cloaked (`DwmGetWindowAttribute(DWMWA_CLOAKED) == 0`), sehingga jendela UWP tersembunyi dan virtual desktop lain tidak ikut
  - bukan milik proses Windock sendiri
- **Event difilter berdasarkan handle** (`idObject == OBJID_WINDOW`, `idChild == CHILDID_SELF`), supaya event dari kontrol di dalam jendela diabaikan.

### 6.2 Identitas aplikasi (AppId)

Satu ikon dock = satu `AppId`. Urutan penentuannya:

1. **AppUserModelID jendela**, jika ada. Dibaca lewat `SHGetPropertyStoreForWindow` dan `PKEY_AppUserModel_ID`. Ini menangani aplikasi UWP dan aplikasi yang mengatur ID-nya sendiri.
2. **Path exe proses**, dari `QueryFullProcessImageName` dengan akses `PROCESS_QUERY_LIMITED_INFORMATION`, dinormalisasi menjadi huruf kecil dan path penuh.
3. **Khusus UWP:** jendela `ApplicationFrameHost.exe` diambil dari proses jendela anaknya.

**Pin** disimpan sebagai path `.exe` atau `.lnk`. Untuk `.lnk`, target dan AppUserModelID-nya di-resolve lewat `IShellLinkW` dan `IPropertyStore`, sehingga pin cocok dengan jendela yang sedang jalan.

### 6.3 DockModel

`DockModel` (Core) menyimpan daftar `DockItem` dalam urutan tampil: pin, pemisah, aplikasi jalan yang tidak di-pin (urut sesuai waktu muncul), pemisah, lalu Recycle Bin. Model ini diperbarui dari event `IWindowSource` dan perubahan pin, lalu mengeluarkan event `ItemsChanged` berisi diff (ditambah, dihapus, berubah) supaya renderer bisa menganimasikan masuk dan keluar.

Setiap `DockItem` menyimpan: `AppId`, `DisplayName`, `IconSource` (path), `IsPinned`, `Windows` (daftar handle, urut dari yang terakhir aktif), `IsActive`, dan `LaunchState` (Idle atau Launching, beserta waktu mulai).

### 6.4 AppLauncher

| Aksi | Implementasi |
|---|---|
| Buka | `Process.Start` dengan `UseShellExecute=true` untuk path atau `.lnk`. Untuk AppUserModelID: `explorer.exe shell:AppsFolder\<AUMID>` |
| Aktifkan | `ShowWindow(SW_RESTORE)` jika minimize, lalu `SetForegroundWindow` |
| Minimize | `ShowWindow(SW_MINIMIZE)` |
| Tutup semua | `PostMessage(WM_CLOSE)` ke setiap jendela. Tidak pernah `TerminateProcess` |
| Tampilkan di Explorer | `explorer.exe /select,"<path>"` |
| Recycle Bin | `explorer.exe shell:RecycleBinFolder` |

Jendela milik proses Administrator mungkin menolak sebagian aksi karena UIPI. Kegagalan dicatat ke log dan diabaikan, tanpa crash.

### 6.5 IconProvider

- **Sumber ikon:** `IShellItemImageFactory.GetImage` dengan `SIIGBF_ICONONLY`, pada ukuran 256 px atau ukuran terdekat. Ini bekerja untuk exe, lnk, dan item `shell:AppsFolder`.
- **Thread:** ikon diambil di satu thread worker STA, karena API-nya COM. Hasilnya `BitmapSource` yang sudah `Freeze()` dikirim ke thread UI.
- **Cache:** disimpan di memori dengan kunci `(AppId atau path, ukuran piksel)`. Ikon Launchpad di fase berikutnya akan memakai cache terpisah yang dibuang saat ditutup.
- **Cadangan:** jika gagal, dipakai ikon aplikasi generik.

## 7. Taskbar dan AppBar

### 7.1 Pencadangan ruang layar (AppBarService)

Ruang layar dicadangkan oleh jendela bantu kecil tak terlihat (`AppBarHost`), bukan oleh `DockWindow`. Alasannya, sistem AppBar bisa memindahkan jendela yang terdaftar, dan pergeseran itu tidak boleh mengganggu `DockWindow`.

- **Daftar:** `ABM_NEW` dengan callback message, lalu `ABM_QUERYPOS` dan `ABM_SETPOS` untuk sisi yang sesuai posisi dock. Tebal ruang = tinggi plate + jarak tepi (default 70). Ruang magnification tidak ikut dicadangkan.
- **`ABN_FULLSCREENAPP`:** dock disembunyikan saat ada aplikasi layar penuh (F-14), dan muncul lagi setelah aplikasi itu keluar.
- **`ABN_POSCHANGED`:** posisi dihitung ulang.
- **Dock auto-hide aktif:** `ABM_REMOVE`, sehingga jendela memakai layar penuh seperti di Mac.
- **Keluar:** selalu `ABM_REMOVE`.

### 7.2 Menyembunyikan taskbar (TaskbarService)

| Mode | Yang dilakukan |
|---|---|
| Sembunyikan total | 1. Simpan status asli taskbar (`ABM_GETSTATE`) ke `state.json`. 2. Set taskbar ke auto-hide (`ABM_SETSTATE`, `ABS_AUTOHIDE`) supaya taskbar melepas ruang layarnya. 3. `ShowWindow(SW_HIDE)` pada `Shell_TrayWnd` dan semua `Shell_SecondaryTrayWnd` |
| Auto-hide | Langkah 1 dan 2 saja |
| Biarkan tampil | Tidak menyentuh taskbar. Ruang dock dicadangkan di atas taskbar |

**Langkah 2 adalah perubahan setelan Windows yang tersimpan** (sama seperti mencentang "Automatically hide the taskbar" di Settings). Karena itu perubahan ini dicatat dan selalu dikembalikan:

- **Keluar normal, Jeda, shortcut darurat, atau mengganti mode:** `ShowWindow(SW_SHOW)`, lalu `ABM_SETSTATE` ke status asli, lalu hapus penanda di `state.json`.
- **Crash dengan exception:** handler global (§9) menjalankan pemulihan yang sama sebelum proses berakhir.
- **Mati paksa** (Task Manager, listrik): `state.json` masih berisi penanda. Saat Windock dijalankan lagi, status asli dipulihkan dulu sebelum mode diterapkan ulang. Jika pengguna tidak menjalankan Windock lagi, cara manualnya ditulis di README: restart Explorer, lalu matikan "Automatically hide the taskbar" di Settings, Personalization, Taskbar.
- **Explorer restart:** Windock mendengarkan pesan `TaskbarCreated` (`RegisterWindowMessage`), lalu menerapkan ulang mode taskbar dan mendaftarkan ulang AppBar.
- **Tombol Windows:** perilaku Start Menu saat taskbar disembunyikan diverifikasi di Spike S2. Jika taskbar ikut muncul, `WindowTracker` mendeteksi `EVENT_OBJECT_SHOW` pada `Shell_TrayWnd` dan menyembunyikannya lagi setelah Start Menu tertutup.

## 8. Pengaturan dan state

Lokasi semua file: `%AppData%\Windock\`

| File | Isi |
|---|---|
| `settings.json` | Pengaturan pengguna (skema di bawah) |
| `settings.json.bak` | Salinan terakhir yang valid |
| `state.json` | Data pemulihan taskbar: `{ "taskbarModified": true, "originalAbState": 0, "pid": 1234 }` |
| `logs/windock-YYYYMMDD.log` | Log teks, disimpan 7 hari |

Skema `settings.json` (versi 1):

```json
{
  "version": 1,
  "appearance": {
    "position": "Bottom",
    "iconSize": 48,
    "magnification": { "enabled": true, "size": 80 },
    "backgroundOpacity": 0.55,
    "blur": "Standard",
    "showRunningIndicators": true
  },
  "behavior": {
    "taskbarMode": "Hidden",
    "autoHide": false,
    "bounceOnLaunch": true,
    "startWithWindows": false
  },
  "pins": [
    { "path": "C:\\Windows\\explorer.exe" },
    { "path": "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Google Chrome.lnk" }
  ],
  "safety": { "emergencyHotkey": "Ctrl+Alt+Shift+W" },
  "firstRunCompleted": true
}
```

Aturan `SettingsStore`:
- **Semua nilai di-clamp ke rentang valid saat dibaca.** Contohnya `iconSize` 32–96, dan `magnification.size` minimal sama dengan `iconSize`.
- **Penulisan atomik:** tulis ke file `.tmp`, lalu ganti file asli. Penyimpanan di-debounce 500 ms.
- **JSON rusak:** coba baca `.bak`. Jika gagal juga, pakai default, dan simpan file yang rusak sebagai `settings.corrupt-<waktu>.json` untuk diperiksa.
- **`version` dipakai untuk migrasi** di versi berikutnya.
- **Event `SettingsChanged`** membawa bagian mana yang berubah, supaya komponen hanya bereaksi pada hal yang relevan. Contohnya, mengganti opacity tidak membuat AppBar mendaftar ulang.

**Jalankan saat Windows nyala:** nilai `Windock` di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` = `"<path exe>" --startup`. Ditulis hanya saat opsi dihidupkan, dan dihapus saat opsi dimatikan.

## 9. Siklus hidup aplikasi, thread, dan error

**Start:**
1. Mutex `Local\Windock.SingleInstance`. Jika sudah ada instance lain, kirim sinyal lewat `EventWaitHandle` bernama supaya instance itu membuka Pengaturan, lalu keluar.
2. Pasang handler error global.
3. Baca `state.json`, dan pulihkan taskbar jika ada sisa dari sesi sebelumnya.
4. Baca pengaturan.
5. Buat `WindowTracker`, `DockModel`, lalu jendela dock.
6. Terapkan AppBar dan mode taskbar.
7. Daftarkan shortcut darurat dan ikon tray.
8. Jika ini pertama kali dijalankan, tawarkan opsi "Jalankan saat Windows nyala".

**Thread:** semua logika jalan di thread UI. Hook `WINEVENT_OUTOFCONTEXT` mengirim event lewat message loop thread yang memasangnya, yaitu thread UI. Satu-satunya thread tambahan adalah worker STA untuk ikon. Tidak ada `Timer` yang jalan terus.

**Handler error global:** `AppDomain.UnhandledException`, `Application.DispatcherUnhandledException`, dan `TaskScheduler.UnobservedTaskException`. Semuanya mencatat ke log, lalu menjalankan `RestoreAll()` (taskbar, `ABM_REMOVE`, lepas hotkey). Error di thread UI yang bisa dipulihkan, misalnya gagal menggambar satu ikon, ditandai handled supaya dock tetap jalan.

| Situasi | Penanganan |
|---|---|
| Ikon gagal diambil | Ikon generik, catat di log |
| Aplikasi gagal dibuka | Bounce dihentikan, notifikasi balon dari ikon tray, catat di log |
| `settings.json` rusak | Lihat §8 |
| Explorer restart | Terapkan ulang (§7.2) |
| Resolusi, DPI, atau monitor berubah (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`) | Hitung ulang ukuran jendela dan daftarkan ulang AppBar |
| Bangun dari sleep (`PowerModeChanged`) | Terapkan ulang mode taskbar |
| Shortcut darurat sudah dipakai aplikasi lain | Tampilkan peringatan di Pengaturan, dan minta pengguna memilih shortcut lain |

**Keluar:** `RestoreAll()`, simpan pengaturan yang tertunda, lalu tutup jendela.

## 10. Aturan keamanan (wajib di semua kode)

1. **Manifest `requestedExecutionLevel="asInvoker"`.** Tidak pernah meminta Administrator.
2. **Tidak ada injeksi DLL**, hook in-process, atau penulisan memori proses lain. Hook yang dipakai hanya `WINEVENT_OUTOFCONTEXT`.
3. **Tidak ada penghapusan file** di luar `%AppData%\Windock\`. Menghapus pin hanya mengubah `settings.json`. Recycle Bin hanya dibuka, tidak pernah dikosongkan.
4. **Registry hanya ditulis di `HKCU\...\Run`.** Setelan taskbar hanya diubah lewat API `ABM_SETSTATE`, selalu dicatat di `state.json` sebelum diubah, dan selalu dikembalikan.
5. **Menutup aplikasi hanya lewat `WM_CLOSE`**, sehingga aplikasi tetap bisa menanyakan "simpan perubahan?".
6. **Tidak ada koneksi jaringan** di v1.0.

## 11. Strategi testing

**Unit test (`Windock.Core.Tests`):**
- `DockModel`: urutan pin, pemisah, dan aplikasi jalan; pengelompokan jendela per `AppId`; status aktif; diff `ItemsChanged`; bounce dimulai dan berhenti saat jendela muncul atau batas 10 detik.
- `DockLayoutEngine`: skala pada jarak 0, R/2, dan ≥ R; lebar plate dengan dan tanpa magnification; ikon ditambatkan di garis bawah; posisi kiri dan kanan.
- `BounceAnimator`, `Tween`, `Easing`: nilai di titik waktu tertentu dengan `IClock` palsu.
- `SettingsStore`: default, clamp, JSON rusak, `.bak`, penulisan atomik (di folder sementara), dan debounce.

Komponen Win32 berada di balik interface (`IWindowSource`, `ILauncher`, `ITaskbarController`, `IAppBarController`, `IClock`), sehingga `DockModel` dan logika lain diuji dengan implementasi palsu.

**Checklist uji manual** (dijalankan sebelum setiap rilis):
- [ ] Maximize jendela: berhenti tepat di atas dock, tanpa celah atau tumpang tindih
- [ ] Game atau video layar penuh: dock hilang, lalu muncul lagi setelah keluar
- [ ] Restart Explorer lewat Task Manager: dock dan mode taskbar pulih sendiri
- [ ] End task Windock lewat Task Manager, lalu jalankan lagi: taskbar dipulihkan dulu
- [ ] Shortcut darurat: dock mati dan taskbar kembali
- [ ] Tombol Windows: Start Menu terbuka, lalu taskbar tersembunyi lagi setelah Start ditutup
- [ ] Skala DPI 100%, 125%, dan 150%: ikon tajam dan ukuran benar
- [ ] Sleep lalu bangun: semua tetap benar
- [ ] Aplikasi UWP (Settings, Calculator): tampil sebagai ikon yang benar
- [ ] Jendela aplikasi Administrator: tampil, dan kegagalan aksi tidak membuat crash
- [ ] Task Manager setelah 8 jam: RAM di bawah 100 MB, CPU diam sekitar 0%

## 12. Build dan rilis

- **Build:** `dotnet build`, dan `dotnet test` untuk Core.
- **CI:** GitHub Actions di `windows-latest` menjalankan build dan test di setiap push dan pull request.
- **Rilis:** `dotnet publish src/Windock -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` menghasilkan satu exe yang butuh .NET 8 Desktop Runtime. Exe dirilis sebagai zip di GitHub Releases.
- **Versi** memakai SemVer (`v0.1.0`, ...), dan commit memakai Conventional Commits (`feat:`, `fix:`, `docs:`, ...).

## 13. Spike Fase 0

Kode spike ada di `spikes/`. Kode ini hanya untuk menjawab pertanyaan teknis, bukan untuk dipakai langsung di aplikasi.

| Spike | Pertanyaan | Lolos jika |
|---|---|---|
| S1 — Blur dan overlay | Bisakah plate ber-blur dengan bentuk rounded-rect mengikuti lebar yang beranimasi, sementara ikon di jendela transparan di atasnya? | Region blur mengikuti plate di 60 fps tanpa tertinggal yang terlihat; klik di area transparan tembus ke jendela belakang; tidak muncul di Alt+Tab; klik tidak mencuri fokus |
| S2 — Taskbar dan AppBar | Apakah kombinasi `ABS_AUTOHIDE` dan `SW_HIDE` menyembunyikan taskbar tanpa meninggalkan celah, dan bagaimana perilaku tombol Windows? | Jendela maximize mengisi sampai batas dock; Start Menu bisa dibuka; Explorer restart pulih; status asli taskbar kembali setelah keluar dan setelah dimatikan paksa |

Jika S1 gagal, cadangannya: tanpa blur sistem, dengan plate semi-opaque yang digambar di `DockWindow` (opsi Blur = Mati). Jika S2 menunjukkan Start Menu selalu memunculkan taskbar, perilakunya didokumentasikan dan disembunyikan ulang lewat event (§7.2).

## 14. Pemetaan kebutuhan ke komponen

| Kebutuhan (PRD) | Komponen utama |
|---|---|
| F-01, F-09, F-15 | DockWindow, BackdropWindow, DockLayoutEngine, DockRenderer |
| F-02, F-03, F-07 | DockModel, WindowTracker |
| F-04–F-06, F-08, F-12 | AppLauncher, DockWindow (menu konteks) |
| F-10 | DockModel (LaunchState), BounceAnimator |
| F-11 | DockWindow (drag), SettingsStore |
| F-13, F-14 | AppBarService |
| T-01–T-06 | TaskbarService, AppBarService |
| S-01–S-12 | SettingsWindow, SettingsStore |
| A-01 | TrayIcon |
| A-02 | HotkeyService |
| A-03, A-04 | App (siklus hidup), TaskbarService |
