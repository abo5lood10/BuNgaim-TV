# يولّد أصول الهوية لبرنامج BuNgaim TV (مطابقة لهوية DsMaN — theme\default theme):
#   NextBoost.ico  أيقونة: مربع مدوّر بتدرّج أخضر قطري + رمز التشغيل بالأخضر الغامق + نجمة صفراء
#   name.bin       اسم البرنامج بخط الكرتون (Snap ITC) كمخطط مرسوم (نقاط + أنواع)، نفس صيغة name.bin في DsMaN
# خط Snap ITC لازم يكون منزّل على الجهاز اللي يشغّل هذا السكربت. ملف الخط نفسه لا يُنسخ؛ فقط مخطط الاسم.
param([string]$Text = 'BuNgaim TV')
Add-Type -AssemblyName System.Drawing
$src = $PSScriptRoot

# ---------- name.bin ----------
$fam = New-Object Drawing.FontFamily 'Snap ITC'
$gp = New-Object Drawing.Drawing2D.GraphicsPath
$gp.AddString($Text, $fam, 0, [single]100, (New-Object Drawing.PointF 0, 0), [Drawing.StringFormat]::GenericTypographic)
$pts = $gp.PathPoints; $ty = $gp.PathTypes
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
$w.Write([int]$pts.Length); foreach ($p in $pts) { $w.Write([single]$p.X); $w.Write([single]$p.Y) }; $w.Write([byte[]]$ty)
[IO.File]::WriteAllBytes("$src\name.bin", $ms.ToArray()); $b = $gp.GetBounds(); "name.bin: $($pts.Length) points, bounds $([int]$b.Width)x$([int]$b.Height)"

# ---------- الأيقونة ----------
function Draw([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
    $k = $s / 1024.0
    $rr = New-Object Drawing.Drawing2D.GraphicsPath; $d = 440 * $k
    $rr.AddArc(0, 0, $d, $d, 180, 90); $rr.AddArc($s - $d - 1, 0, $d, $d, 270, 90); $rr.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $rr.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $rr.CloseFigure()
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 0, 0), (New-Object Drawing.PointF $s, $s), ([Drawing.Color]::FromArgb(96, 234, 130)), ([Drawing.Color]::FromArgb(22, 120, 46)))
    $g.FillPath($br, $rr)
    # رمز التشغيل (مثلث بزوايا مدوّرة) بالأخضر الغامق #0A381C
    $tri = New-Object Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon(@((New-Object Drawing.PointF (330 * $k), (250 * $k)), (New-Object Drawing.PointF (330 * $k), (774 * $k)), (New-Object Drawing.PointF (790 * $k), (512 * $k))))
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(10, 56, 28)), ([single](70 * $k)); $pen.LineJoin = 'Round'
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(10, 56, 28))), $tri); $g.DrawPath($pen, $tri)
    # النجمة الصفراء (نفس شكل الأيقونة الأصلية، أعلى اليمين)
    $st = @(); $cx = 800 * $k; $cy = 224 * $k; $r = 92 * $k
    for ($i = 0; $i -lt 8; $i++) { $rad = if ($i % 2 -eq 0) { $r } else { $r * 0.3 }; $a = [math]::PI / 4 * $i - [math]::PI / 2; $st += New-Object Drawing.PointF ($cx + $rad * [math]::Cos($a)), ($cy + $rad * [math]::Sin($a)) }
    $g.FillPolygon((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 224, 102))), [Drawing.PointF[]]$st)
    $g.Dispose(); return $bmp
}
$sizes = 256, 64, 48, 32, 24, 16
$pngs = foreach ($s in $sizes) { $bm = Draw $s; $m = New-Object IO.MemoryStream; $bm.Save($m, [Drawing.Imaging.ImageFormat]::Png); if ($s -eq 256) { $bm.Save("$src\icon-preview.png") }; $bm.Dispose(); , $m.ToArray() }
$fs = [IO.File]::Create("$src\NextBoost.ico"); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count); $off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) { $dm = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }; $bw.Write([byte]$dm); $bw.Write([byte]$dm); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$pngs[$i].Length); $bw.Write([int]$off); $off += $pngs[$i].Length }
foreach ($p in $pngs) { $bw.Write($p) }; $bw.Close(); $fs.Close(); "NextBoost.ico written"
