# Regenerates src/EdgeLink/Assets/EdgeLink.ico: two screens joined by a link.
Add-Type -AssemblyName System.Drawing

function New-IconPng([int] $size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    $screen = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 52, 74))
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 96, 205, 255)), ([Math]::Max(1.0, 2 * $s))
    $link = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 140, 66)), ([Math]::Max(1.5, 3 * $s))
    $link.StartCap = 'Round'
    $link.EndCap = 'Round'

    # a wide screen on top, a small one below to the right
    $top = New-Object System.Drawing.RectangleF (2 * $s), (3 * $s), (28 * $s), (13 * $s)
    $bottom = New-Object System.Drawing.RectangleF (14 * $s), (21 * $s), (16 * $s), (9 * $s)
    foreach ($r in $top, $bottom) {
        $g.FillRectangle($screen, $r)
        $g.DrawRectangle($border, $r.X, $r.Y, $r.Width, $r.Height)
    }

    # the link between a short span of the top screen and the bottom screen
    $g.DrawBezier($link, (9 * $s), (16 * $s), (9 * $s), (24 * $s), (22 * $s), (13 * $s), (22 * $s), (21 * $s))

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 64, 256
$images = $sizes | ForEach-Object { , (New-IconPng $_) }

$target = Join-Path $PSScriptRoot '..\src\EdgeLink\Assets\EdgeLink.ico'
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16] 0); $writer.Write([uint16] 1); $writer.Write([uint16] $sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte] $dimension); $writer.Write([byte] $dimension)
    $writer.Write([byte] 0); $writer.Write([byte] 0)
    $writer.Write([uint16] 1); $writer.Write([uint16] 32)
    $writer.Write([uint32] $images[$i].Length); $writer.Write([uint32] $offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]] $image) }

[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Wrote $target ($($out.Length) bytes)"
