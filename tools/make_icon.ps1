<#
Draws the app icon (concept E, "taped label"): a strip of torn bone tape with "8P" in Oswald Bold on the
near-black ground, the same palette as the apps. Every size is drawn separately so small ones stay sharp
(16 and 24 px drop the tilt and the torn ends).

  tools\make_icon.ps1      writes L4D2EightPlayer\Assets\app.ico (16-256 px) and app-256.png
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'L4D2EightPlayer\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Add-Type -AssemblyName System.Drawing

$ground = [Drawing.ColorTranslator]::FromHtml('#141110')
$bone = [Drawing.ColorTranslator]::FromHtml('#ebe3d2')
$fonts = New-Object Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $root 'L4D2EightPlayer\Fonts\Oswald-Bold.ttf'))
$oswald = $fonts.Families[0]

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

# design units are a 128 x 128 canvas, scaled to each size
function Draw([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([Drawing.Color]::Transparent)
    $k = $size / 128.0
    $g.ScaleTransform($k, $k)
    $small = $size -le 24

    $g.FillPath((New-Object Drawing.SolidBrush $ground), (RoundRect 0 0 128 128 ($(if ($small) { 16 } else { 22 }))))

    # the tape: tilted with torn ends, or straight and fuller when tiny
    if ($small) { $x0 = 4; $x1 = 124; $y0 = 26; $y1 = 102; $tilt = 0; $em = 74 }
    else { $x0 = 8; $x1 = 120; $y0 = 34; $y1 = 94; $tilt = -8; $em = 58 }
    $state = $g.Save()
    $g.TranslateTransform(64, 64); $g.RotateTransform($tilt); $g.TranslateTransform(-64, -64)
    $pts = New-Object 'System.Collections.Generic.List[Drawing.PointF]'
    $pts.Add((New-Object Drawing.PointF $x0, $y0)); $pts.Add((New-Object Drawing.PointF $x1, $y0))
    if (-not $small) { for ($y = $y0 + 6; $y -lt $y1; $y += 6) { $pts.Add((New-Object Drawing.PointF ($x1 - (($y - $y0) / 6 % 2) * 4), $y)) } }
    $pts.Add((New-Object Drawing.PointF $x1, $y1)); $pts.Add((New-Object Drawing.PointF $x0, $y1))
    if (-not $small) { for ($y = $y1 - 6; $y -gt $y0; $y -= 6) { $pts.Add((New-Object Drawing.PointF ($x0 + (($y1 - $y) / 6 % 2) * 4), $y)) } }
    $g.FillPolygon((New-Object Drawing.SolidBrush $bone), $pts.ToArray())

    # "8P", centred on the tape by its actual ink, not the font's line box
    $text = New-Object Drawing.Drawing2D.GraphicsPath
    $fmt = [Drawing.StringFormat]::GenericTypographic
    $text.AddString('8P', $oswald, [int][Drawing.FontStyle]::Bold, $em, (New-Object Drawing.PointF 0, 0), $fmt)
    $b = $text.GetBounds()
    $m = New-Object Drawing.Drawing2D.Matrix
    $m.Translate(64 - $b.X - $b.Width / 2, ($y0 + $y1) / 2 - $b.Y - $b.Height / 2)
    $text.Transform($m)
    $g.FillPath((New-Object Drawing.SolidBrush $ground), $text)
    $g.Restore($state)
    $g.Dispose()
    $bmp
}

# .ico: 32-bit DIBs for the small sizes (what every Windows tool reads), PNG for 256
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = Draw $s
    if ($s -eq 256) {
        $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save((Join-Path $assets 'app-256.png'), [Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }
    else {
        $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
        $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
        $stride = [int]([Math]::Ceiling($s / 32.0) * 4)
        for ($y = 0; $y -lt $s; $y++) { $w.Write((New-Object byte[] $stride)) }   # AND mask: alpha does the work
        $w.Flush(); , $ms.ToArray()
    }
    $bmp.Dispose()
}
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$d.Length); $w.Write([int]$offset); $offset += $d.Length
}
foreach ($d in $images) { $w.Write($d) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $ico.ToArray())
"wrote $(Join-Path $assets 'app.ico') ($($sizes -join ', ') px, $([Math]::Round($ico.Length / 1KB)) KB)"
