# يبني BuNgaim TV.exe من الكود بمترجم C# الموجود أصلاً في ويندوز (بدون تحميل أي شي).
# الأصول (الأيقونة + اسم البرنامج المرسوم) تتولّد من make-assets.ps1 لو ناقصة.
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot; $app = Split-Path $src -Parent
if (-not (Test-Path "$src\name.bin") -or -not (Test-Path "$src\NextBoost.ico")) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$src\make-assets.ps1" }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$rsp = Join-Path $env:TEMP 'nextboost-build.rsp'   # ملف أوامر عشان المسار فيه حروف عربية ومسافات
$lines = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll')
$lines += ('/win32icon:"' + "$src\NextBoost.ico" + '"')
$lines += ('/resource:"' + "$src\name.bin" + '",name.bin')
if (Test-Path "$src\icon-256.png") { $lines += ('/resource:"' + "$src\icon-256.png" + '",icon.png') }
foreach ($n in 'f1.bin', 'f2.bin') { if (Test-Path "$src\fonts\$n") { $lines += ('/resource:"' + "$src\fonts\$n" + '",' + $n) } }
$lines += ('/out:"' + "$app\BuNgaim TV.exe" + '"')
$lines += ('"' + "$src\NextBoost.cs" + '"'); $lines += ('"' + "$src\Theme.cs" + '"')
[IO.File]::WriteAllLines($rsp, $lines, (New-Object Text.UTF8Encoding $true))
& $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Write-Host "OK -> $app\BuNgaim TV.exe"