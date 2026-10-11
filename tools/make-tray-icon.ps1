# 从 08_validation 的实际渲染截图裁脸，生成多尺寸托盘图标 assets/tray.ico
# 用法：pwsh -File make-tray-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path (Split-Path -Parent $root) '08_validation\高清表情修订\neutral.png'
$out  = Join-Path $root 'assets\tray.ico'

$img = [System.Drawing.Image]::FromFile($src)
try {
    # 1200x1000 渲染图里头部大致范围；裁成正方形后两端各留一点发丝
    $cw = 560; $ch = 560; $cx = 180; $cy = 20
    $sizes = 16, 24, 32, 48, 64, 128, 256

    $pngs = @{}
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode    = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($img,
            (New-Object System.Drawing.Rectangle 0, 0, $s, $s),
            (New-Object System.Drawing.Rectangle $cx, $cy, $cw, $ch),
            [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs[$s] = $ms.ToArray()
        $bmp.Dispose(); $ms.Dispose()
    }
} finally { $img.Dispose() }

# ── 组装 ICO ──
# ICONDIR(6) + 每个 ICONDIRENTRY(16) + PNG 数据
$count = $sizes.Count
$head  = New-Object byte[] (6 + 16 * $count)
[void][BitConverter]::GetBytes([uint16]0).CopyTo($head, 0)   # reserved
[void][BitConverter]::GetBytes([uint16]1).CopyTo($head, 2)   # type = icon
[void][BitConverter]::GetBytes([uint16]$count).CopyTo($head, 4)

$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $e = 6 + 16 * $i
    $head[$e]     = if ($s -ge 256) { 0 } else { $s }        # width, 0 表示 256
    $head[$e + 1] = if ($s -ge 256) { 0 } else { $s }        # height
    $head[$e + 2] = 0                                          # 调色板数
    $head[$e + 3] = 0                                          # reserved
    [void][BitConverter]::GetBytes([uint16]1).CopyTo($head, $e + 4)   # planes
    [void][BitConverter]::GetBytes([uint16]32).CopyTo($head, $e + 6)  # bitCount
    [void][BitConverter]::GetBytes([uint32]$pngs[$s].Length).CopyTo($head, $e + 8)
    [void][BitConverter]::GetBytes([uint32]$offset).CopyTo($head, $e + 12)
    $offset += $pngs[$s].Length
}

$all = New-Object System.Collections.Generic.List[byte]
$all.AddRange($head)
foreach ($s in $sizes) { $all.AddRange($pngs[$s]) }
[System.IO.File]::WriteAllBytes($out, $all.ToArray())

"wrote $out  ($([math]::Round((Get-Item $out).Length / 1KB, 1)) KB, sizes: $($sizes -join ','))"

# 同时导一张 256 PNG，方便在别处用
$big = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)
$img2 = [System.Drawing.Image]::FromFile($src)
$g.DrawImage($img2, (New-Object System.Drawing.Rectangle 0, 0, 256, 256),
                    (New-Object System.Drawing.Rectangle 180, 20, 560, 560),
                    [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $img2.Dispose()
$big.Save((Join-Path $root 'assets\tray-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
"wrote tray-256.png"
