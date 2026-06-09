Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot "apertureneo.ico"
$svgOutPath = Join-Path $PSScriptRoot "apertureneo.svg"
$sizes = @(16, 32, 48, 64, 128, 256)

$bgColor = [System.Drawing.Color]::FromArgb(30, 30, 46)
$bladeColor = [System.Drawing.Color]::FromArgb(137, 180, 250)
$centerColor = [System.Drawing.Color]::FromArgb(30, 30, 46)
$bladeCount = 8

function Draw-ApertureIcon {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $radius = [int]($size * 0.18)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $radius * 2, $radius * 2, 180, 90)
    $path.AddArc($size - $radius * 2, 0, $radius * 2, $radius * 2, 270, 90)
    $path.AddArc($size - $radius * 2, $size - $radius * 2, $radius * 2, $radius * 2, 0, 90)
    $path.AddArc(0, $size - $radius * 2, $radius * 2, $radius * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush($bgColor)), $path)

    $cx = $size / 2.0
    $cy = $size / 2.0
    $outerR = $size * 0.38
    $innerR = $size * 0.10
    $bladeBrush = New-Object System.Drawing.SolidBrush($bladeColor)

    for ($i = 0; $i -lt $bladeCount; $i++) {
        $angle1 = ($i * 2.0 * [Math]::PI / $bladeCount) - ([Math]::PI / 2.0)
        $angle2 = (($i + 1) * 2.0 * [Math]::PI / $bladeCount) - ([Math]::PI / 2.0)
        $angleMid = (($i + 0.5) * 2.0 * [Math]::PI / $bladeCount) - ([Math]::PI / 2.0)

        $p1x = $cx + $outerR * [Math]::Cos($angle1)
        $p1y = $cy + $outerR * [Math]::Sin($angle1)
        $p2x = $cx + $outerR * [Math]::Cos($angle2)
        $p2y = $cy + $outerR * [Math]::Sin($angle2)
        $p3x = $cx + $innerR * [Math]::Cos($angleMid)
        $p3y = $cy + $innerR * [Math]::Sin($angleMid)

        $blade = New-Object System.Drawing.Drawing2D.GraphicsPath
        $blade.AddLine([int]$p1x, [int]$p1y, [int]$p2x, [int]$p2y)
        $blade.AddLine([int]$p2x, [int]$p2y, [int]$p3x, [int]$p3y)
        $blade.AddLine([int]$p3x, [int]$p3y, [int]$p1x, [int]$p1y)
        $blade.CloseFigure()
        $g.FillPath($bladeBrush, $blade)
    }

    $centerBrush = New-Object System.Drawing.SolidBrush($centerColor)
    $centerR = $size * 0.07
    $g.FillEllipse($centerBrush, $cx - $centerR, $cy - $centerR, $centerR * 2, $centerR * 2)

    $g.Dispose()
    return $bmp
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$imageData = @()
$offset = 6 + (16 * $sizes.Count)

foreach ($size in $sizes) {
    $bmp = Draw-ApertureIcon $size
    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
    $imageData += @{Size=$size; Data=$pngBytes; Offset=$offset}
    $offset += $pngBytes.Length
}

foreach ($img in $imageData) {
    $s = $img.Size
    $w = if ($s -ge 256) { 0 } else { $s }
    $h = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$img.Data.Length)
    $bw.Write([UInt32]$img.Offset)
}

foreach ($img in $imageData) {
    $bw.Write($img.Data)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host "Created: $outPath"
