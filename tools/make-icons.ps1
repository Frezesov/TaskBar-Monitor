param([string]$OutDir = (Join-Path $PSScriptRoot '..\Assets'))

# App icon: rounded tile with three load bars (like the overlay's columns).
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min(2 * $r, [Math]::Min($w, $h))
    if ($d -le 0) { $p.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h))); return $p }
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $top = [System.Drawing.ColorTranslator]::FromHtml('#2B88D8')
    $bottom = [System.Drawing.ColorTranslator]::FromHtml('#0F5CA8')
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90)
    $g.FillPath($fill, (New-RoundedPath 0 0 $s $s ([Math]::Max(2, $s * 0.22))))

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $soft = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
    $small = $s -le 24
    $barW = [Math]::Max(2, [Math]::Round($s * ($(if ($small) { 0.16 } else { 0.13 }))))
    $gap = [Math]::Max(1, [Math]::Round($s * ($(if ($small) { 0.09 } else { 0.08 }))))
    $total = 3 * $barW + 2 * $gap
    $x0 = [Math]::Round(($s - $total) / 2)
    $base = [Math]::Round($s * 0.76)
    $heights = 0.26, 0.52, 0.38
    for ($i = 0; $i -lt 3; $i++) {
        $h = [Math]::Max(2, [Math]::Round($s * $heights[$i]))
        $x = $x0 + $i * ($barW + $gap)
        $brush = if ($i -eq 1) { $white } else { $soft }
        $g.FillPath($brush, (New-RoundedPath $x ($base - $h) $barW $h ([Math]::Max(0.5, $barW * 0.35))))
    }
    $g.Dispose()
    $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write([int]($s * $s * 4 + $maskStride * $s))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskStride * $s)))
    $bw.Flush()
    , $ms.ToArray()
}

function Write-Ico([string]$path) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $images = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($s in $sizes) {
        $bmp = New-IconBitmap $s
        if ($s -eq 256) {
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($ms.ToArray())
        } else {
            $images.Add([byte[]](Get-DibBytes $bmp))
        }
        $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $images[$i].Length
        $dim = [byte]($(if ($s -ge 256) { 0 } else { $s }))
        $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$len); $bw.Write([int]$offset)
        $offset += $len
    }
    foreach ($img in $images) { $bw.Write($img) }
    $bw.Close()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Write-Ico (Join-Path $OutDir 'app.ico')
if ($env:ICON_PREVIEW) {
    $sheet = New-Object System.Drawing.Bitmap(560, 290)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
    $g.InterpolationMode = 'NearestNeighbor'
    $g.DrawImage((New-IconBitmap 256), 0, 16)
    $x = 272
    foreach ($s in 16, 24, 32) {
        $g.DrawImage((New-IconBitmap $s), $x, 16, $s * 4, $s * 4)
        $x += $s * 4 + 8
    }
    $g.Dispose()
    $sheet.Save($env:ICON_PREVIEW, [System.Drawing.Imaging.ImageFormat]::Png)
}
"Icons written to $OutDir"
