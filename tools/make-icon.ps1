# Generates src/WirelessStatus.App/Assets/AppIcon.ico: a white battery (with wireless arcs at larger sizes) on a
# blue rounded square, at every size Windows asks for. Run from the repo root:  powershell -File tools/make-icon.ps1
# (The tray icon is drawn at runtime by TrayIconRenderer; this is the app/exe/toast icon.)
param(
    [string]$Out = "src/WirelessStatus.App/Assets/AppIcon.ico",
    # Toast notifications need a PNG (an .ico IconUri shows the generic app icon instead).
    [string]$PngOut = "src/WirelessStatus.App/Assets/AppIcon.png"
)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    # Background: rounded square with a subtle vertical gradient.
    $bg = New-RoundedRect 0 0 ($s - 0.5) ($s - 0.5) ([Math]::Max(2, $s * 0.22))
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s), ([System.Drawing.Color]::FromArgb(255, 0x2B, 0x88, 0xD8)), ([System.Drawing.Color]::FromArgb(255, 0x0F, 0x5C, 0xA8))
    $g.FillPath($grad, $bg)

    $white = [System.Drawing.Color]::White
    $small = $s -lt 32

    # Battery: centred (lower when there's room for the arcs above it).
    $bw = $s * 0.62; $bh = $s * 0.34
    $bx = ($s - $bw) / 2 - $s * 0.03
    $by = if ($small) { ($s - $bh) / 2 } else { $s * 0.50 }
    $stroke = [Math]::Max(1.0, $s * 0.07)
    $pen = New-Object System.Drawing.Pen $white, $stroke
    $body = New-RoundedRect $bx $by $bw $bh ([Math]::Max(1, $s * 0.06))
    $g.DrawPath($pen, $body)
    $nubW = [Math]::Max(1.0, $s * 0.06); $nubH = $bh * 0.45
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $white), $bx + $bw + $stroke / 2, $by + ($bh - $nubH) / 2, $nubW, $nubH)
    $inset = $stroke * 1.4
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $white), $bx + $inset, $by + $inset, ($bw - 2 * $inset) * 0.7, $bh - 2 * $inset)

    # Wireless arcs above the battery (only where they stay legible).
    if (-not $small) {
        $arcPen = New-Object System.Drawing.Pen $white, ($s * 0.06)
        $arcPen.StartCap = 'Round'; $arcPen.EndCap = 'Round'
        $cx = $s / 2; $cy = $s * 0.47
        foreach ($r in ($s * 0.13), ($s * 0.25)) {
            $g.DrawArc($arcPen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 225, 90)
        }
    }

    $g.Dispose()
    return $bmp
}

function ConvertTo-Png($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Uncompressed 32-bit icon image: BITMAPINFOHEADER (double height), BGRA rows bottom-up, then an all-zero AND mask.
function ConvertTo-Dib($bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int][Math]::Ceiling($s / 32) * 4
    $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2)); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]0); $w.Write([UInt32]($s * $s * 4 + $maskRow * $s)); $w.Write([Int32]0); $w.Write([Int32]0)
    $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

# ICO container: BMP entries for the small sizes, PNG for 256 px (the standard layout).
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    , $(if ($s -ge 256) { ConvertTo-Png $bmp } else { ConvertTo-Dib $bmp })
}
$fs = [System.IO.File]::Create((Join-Path (Get-Location) $Out))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $PngOut), (ConvertTo-Png (New-IconBitmap 64)))
"Wrote $PngOut (64 px)"
"Wrote $Out ($($sizes -join ', ') px)"
