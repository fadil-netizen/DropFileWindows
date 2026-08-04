# DropFIileWindows

Kotak drop Material You untuk Windows — tarik file/gambar/tautan web ke dalam kotak, lalu seret keluar kapan pun kamu butuh.

## Fitur

- **Drop & simpan sementara** — tarik file, gambar, teks, atau halaman web dari aplikasi mana pun ke kotak drop.
- **Sortir drag-out** — buka menu keranjang (▾) dan centang file mana saja yang boleh ditarik keluar.
- **Seret keluar** — tarik file dari rak ke folder/aplikasi lain untuk memindahkan/menyalin.
- **Tema Material You** — warna kotak mengikuti aksen warna sistem operasi, atau ubah manual lewat menu titik warna di samping judul.
- **Animasi kertas ditempel** — file yang masuk dianimasikan seperti kertas yang ditempelkan ke dinding, tersusun rapi.
- **Buat kotak baru dengan goyangan kursor** — goyangkan mouse cepat bolak-balik untuk membuat kotak drop baru.
- **Berjalan di tray** — mode kotak kecil tanpa bingkai, selalu di atas (opsional), autostart (opsional).

## Cara Build

Tanpa Visual Studio — cukup PowerShell dan .NET Framework (csc) yang sudah terpasang di Windows:

```powershell
.\build.ps1
```

Hasil: `DropFile.exe` (aplikasi + installer dalam satu file).

## Cara Pakai

1. Jalankan `DropFile.exe` → animasi instalasi, lalu terpasang ke `%LOCALAPPDATA%\DropFile`.
2. Goyangkan kursor cepat untuk membuat kotak drop, atau klik ikon tray.
3. Seret file ke kotak. Klik ▾ untuk memilih file yang boleh ditarik keluar.
4. Tarik keluar untuk memindahkan, atau klik ✕ untuk membersihkan.
5. `DropFile.exe /uninstall` untuk menghapus.

## Teknologi

- C# + WinForms (.NET Framework 4.0 / `csc`), satu file `Program.cs`.
- Windows API untuk drag & drop lintas aplikasi (WM_DROPFILES, low-level mouse hook).
