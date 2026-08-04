$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$csc64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$csc32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$csc = if (Test-Path $csc64) { $csc64 } else { $csc32 }

$r = @('/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:Microsoft.CSharp.dll')

Write-Host "=== 1. Membuat logo ===" -ForegroundColor Cyan
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\make-logo.ps1"
if (-not (Test-Path "$root\app.ico")) { throw "Logo gagal dibuat." }

Write-Host "=== 2. Membangun DropFile.exe (aplikasi + installer) ===" -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ /win32icon:"$root\app.ico" /res:"$root\app.ico,app.ico" /out:"$root\DropFile.exe" @r "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "DropFile.exe gagal dikompilasi." }

Write-Host ""
Write-Host "Selesai. Satu file paket lengkap:" -ForegroundColor Green
Write-Host "  DropFile.exe  (aplikasi + installer dalam satu file)"
Write-Host ""
Write-Host "Cara pakai:" -ForegroundColor Yellow
Write-Host "  - Jalankan DropFile.exe (dari mana saja) -> menampilkan animasi instalasi, lalu terpasang."
Write-Host "  - Dari folder instalasi (%LOCALAPPDATA%\DropFile) -> berjalan sebagai aplikasi drop."
Write-Host "  - DropFile.exe /uninstall -> membuka animasi penghapusan."