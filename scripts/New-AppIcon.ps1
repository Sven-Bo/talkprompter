<#
.SYNOPSIS
    Generates src/Teleprompter.App/Assets/app.ico - the TalkPrompter logo:
    a dark rounded tile with script lines, the amber reading line, and the
    accent-blue reading arrow.

    Small sizes (16..64) are written as classic 32bpp BMP entries because the
    Windows shell (taskbar, shortcuts, Explorer) does not reliably decode
    PNG-compressed entries below 256px. Only the 256px entry uses PNG.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $repoRoot "src\Teleprompter.App\Assets"
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$icoPath = Join-Path $assetDir "app.ico"

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 27, 38))
    $r = [Math]::Max(2.0, $S * 0.21)
    $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($S - $d, 0, $d, $d, 270, 90)
    $path.AddArc($S - $d, $S - $d, $d, $d, 0, 90)
    $path.AddArc(0, $S - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bg, $path)

    $muted = [System.Drawing.Color]::FromArgb(255, 151, 160, 179)
    $amber = [System.Drawing.Color]::FromArgb(255, 255, 211, 122)
    $blue  = [System.Drawing.Color]::FromArgb(255, 76, 141, 255)

    $penW = [Math]::Max(1.5, $S * 0.085)
    $x0 = $S * 0.20; $x1 = $S * 0.80

    foreach ($spec in @(@{y = 0.32; c = $muted; xe = $x1 }, @{y = 0.68; c = $muted; xe = $x1 }, @{y = 0.50; c = $amber; xe = $S * 0.66 })) {
        $pen = New-Object System.Drawing.Pen($spec.c, $penW)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $y = $S * $spec.y
        $g.DrawLine($pen, [single]$x0, [single]$y, [single]$spec.xe, [single]$y)
        $pen.Dispose()
    }

    if ($S -ge 24) {
        $bb = New-Object System.Drawing.SolidBrush($blue)
        $cx = $S * 0.74; $cy = $S * 0.50; $h = $S * 0.10
        $pts = @(
            (New-Object System.Drawing.PointF([single]$cx, [single]($cy - $h))),
            (New-Object System.Drawing.PointF([single]($cx + $S * 0.14), [single]$cy)),
            (New-Object System.Drawing.PointF([single]$cx, [single]($cy + $h)))
        )
        $g.FillPolygon($bb, $pts)
        $bb.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# 32bpp BGRA DIB entry: BITMAPINFOHEADER + bottom-up pixels + 1bpp AND mask
function New-BmpEntry([System.Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)

    $maskRowBytes = [int](([Math]::Ceiling($S / 8.0) + 3) -band (-bnot 3))
    $w.Write([uint32]40); $w.Write([int]$S); $w.Write([int]($S * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($S * $S * 4 + $maskRowBytes * $S))
    $w.Write([int]0); $w.Write([int]0); $w.Write([uint32]0); $w.Write([uint32]0)

    for ($y = $S - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $S; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }

    # AND mask all zero: transparency comes from the alpha channel
    $w.Write((New-Object byte[] ($maskRowBytes * $S)))

    $w.Flush()
    $bytes = $ms.ToArray()
    $w.Dispose(); $ms.Dispose()
    return , ([byte[]]$bytes)
}

function New-PngEntry([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , ([byte[]]$bytes)
}

$bmpSizes = 16, 20, 24, 32, 40, 48, 64
$entries = @()
foreach ($s in $bmpSizes) {
    $b = New-IconBitmap $s
    $entries += , @{ Size = $s; Data = (New-BmpEntry $b) }
    $b.Dispose()
}
$b256 = New-IconBitmap 256
$entries += , @{ Size = 256; Data = (New-PngEntry $b256) }
$b256.Dispose()

$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    [byte[]]$data = $e.Data
    $sz = $e.Size
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length)
    $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e.Data) }
$w.Dispose(); $fs.Dispose()

Write-Host "Icon written: $icoPath ($((Get-Item $icoPath).Length) bytes, $($entries.Count) entries)" -ForegroundColor Green
