# Generates src\icon.ico — a multi-resolution icon (16/20/24/32/40/48/64/128 as 32-bpp DIB, 256 as PNG)
# drawn with GDI+ at every size. Run: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\icon.ico'))
Add-Type -AssemblyName System.Drawing

function Draw-Glyph([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$size
    $bg    = [System.Drawing.Color]::FromArgb(255, 32, 38, 52)      # slate
    $white = [System.Drawing.Color]::FromArgb(255, 245, 247, 250)
    $cyan  = [System.Drawing.Color]::FromArgb(255, 0, 181, 255)     # same cyan as the crosshair
    # background: rounded square
    $r = $s * 0.22; $m = $s * 0.03
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2*$r, 2*$r, 180, 90); $path.AddArc($s-$m-2*$r, $m, 2*$r, 2*$r, 270, 90)
    $path.AddArc($s-$m-2*$r, $s-$m-2*$r, 2*$r, 2*$r, 0, 90); $path.AddArc($m, $s-$m-2*$r, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $bg), $path)
    # headphones: headband arc + two ear cups (shifted left to leave room for the waves)
    $bandPen = New-Object System.Drawing.Pen $white, ([Math]::Max(1.5, $s * 0.10))
    $bandPen.StartCap = 'Round'; $bandPen.EndCap = 'Round'
    $g.DrawArc($bandPen, $s*0.13, $s*0.20, $s*0.50, $s*0.56, 190, 160)
    $cupW = $s*0.16; $cupH = $s*0.24; $cupY = $s*0.50; $cr = $cupW*0.35
    foreach ($cx in @(($s*0.09), ($s*0.51))) {
        $cp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cp.AddArc($cx, $cupY, 2*$cr, 2*$cr, 180, 90); $cp.AddArc($cx+$cupW-2*$cr, $cupY, 2*$cr, 2*$cr, 270, 90)
        $cp.AddArc($cx+$cupW-2*$cr, $cupY+$cupH-2*$cr, 2*$cr, 2*$cr, 0, 90); $cp.AddArc($cx, $cupY+$cupH-2*$cr, 2*$cr, 2*$cr, 90, 90); $cp.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush $white), $cp)
    }
    # spatial sound waves to the right of the right cup (fewer at small sizes)
    $wavePen = New-Object System.Drawing.Pen $cyan, ([Math]::Max(1.2, $s * 0.075)); $wavePen.StartCap = 'Round'; $wavePen.EndCap = 'Round'
    $cxw = $s*0.60; $cyw = $s*0.62
    $radii = if ($size -le 20) { @(0.19) } elseif ($size -le 32) { @(0.16, 0.27) } else { @(0.14, 0.24, 0.34) }
    foreach ($rr in $radii) { $rad = $s*$rr; $g.DrawArc($wavePen, $cxw-$rad, $cyw-$rad, 2*$rad, 2*$rad, -42, 84) }
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $maskStride = [int][Math]::Ceiling($w / 32.0) * 4
    $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h*2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($w*$h*4 + $maskStride*$h)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h-1; $y -ge 0; $y--) { for ($x = 0; $x -lt $w; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) } }
    for ($y = $h-1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $w; $x++) { if ($bmp.GetPixel($x, $y).A -eq 0) { $bi = [int][Math]::Floor($x / 8); $row[$bi] = $row[$bi] -bor (0x80 -shr ($x % 8)) } }
        $bw.Write($row)
    }
    $bw.Flush(); return ,([byte[]]$ms.ToArray())   # leading comma: hand back ONE byte[] instead of unrolling it
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($sz in $sizes) {
    $bmp = Draw-Glyph $sz
    if ($sz -ge 256) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); [byte[]]$bytes = $ms.ToArray() }
    else { [byte[]]$bytes = Get-DibBytes $bmp }
    [PSCustomObject]@{ Size = $sz; Bytes = $bytes }
    $bmp.Dispose()
}
foreach ($img in $images) { if ($img.Bytes.Length -lt 100) { throw "image data for size $($img.Size) is empty ($($img.Bytes.Length) bytes)" } }
$fs = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Bytes.Length); $w.Write([int]$offset); $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Bytes) }
$w.Flush(); $fs.Close()
Write-Host ("icon written: {0} ({1} sizes, {2} KB)" -f $Out, $images.Count, [Math]::Round((Get-Item $Out).Length / 1KB))
# self-check: every DIB size must load through GDI+; the 256 px entry is PNG (GDI+ can't read those, Windows can),
# so it is validated structurally: directory entry present and payload starts with the PNG signature.
foreach ($sz in $sizes) {
    if ($sz -ge 256) { continue }
    $probe = New-Object System.Drawing.Icon ($Out, $sz, $sz)
    if ($probe.Width -ne $sz) { throw "self-check failed: requested $sz px, got $($probe.Width) px" }
    $probe.Dispose()
}
$raw = [System.IO.File]::ReadAllBytes($Out)
$count = [BitConverter]::ToInt16($raw, 4); $ok256 = $false
for ($e = 0; $e -lt $count; $e++) {
    $base = 6 + 16*$e
    if ($raw[$base] -eq 0 -and $raw[$base+1] -eq 0) {
        $off = [BitConverter]::ToInt32($raw, $base + 12)
        if ($raw[$off] -eq 0x89 -and $raw[$off+1] -eq 0x50 -and $raw[$off+2] -eq 0x4E -and $raw[$off+3] -eq 0x47) { $ok256 = $true }
    }
}
if (-not $ok256) { throw "self-check failed: 256 px PNG entry missing or malformed" }
Write-Host "self-check passed: all sizes load (256 px PNG entry verified structurally)"
