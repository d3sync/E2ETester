# Format/size conversion of the approved generated artwork; does not redraw the logo.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$brand = Join-Path $root 'assets/branding'
$desktop = Join-Path $root 'src/E2ETester.Desktop/Assets'
New-Item -ItemType Directory -Path $desktop -Force | Out-Null
$master = Join-Path $brand 'e2e-tester-icon-master.png'
$source = [Drawing.Image]::FromFile($master)
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
try {
    foreach ($size in $sizes + @(512)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $bytes = $stream.ToArray()
            [IO.File]::WriteAllBytes((Join-Path $brand "e2e-tester-icon-$size.png"), $bytes)
            if ($size -le 256) { $frames.Add($bytes) }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$iconPath = Join-Path $brand 'e2e-tester.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $desktop 'e2e-tester.ico') -Force
Copy-Item -LiteralPath (Join-Path $brand 'e2e-tester-icon-128.png') -Destination (Join-Path $desktop 'e2e-tester.png') -Force
$encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $brand 'e2e-tester-icon-256.png')))
foreach ($theme in @('light', 'dark')) {
    $ink = if ($theme -eq 'dark') { '#f0f6fc' } else { '#1f2328' }
    # Editable typography + embedded raster symbol, with a transparent background.
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="640" height="144" viewBox="0 0 640 144" role="img" aria-label="E2E Tester">
<image x="0" y="0" width="144" height="144" href="data:image/png;base64,$encoded"/>
<text x="168" y="92" fill="$ink" font-family="Segoe UI,Arial,sans-serif" font-size="66" font-weight="600" letter-spacing="-2">E2E Tester</text>
</svg>
"@
    [IO.File]::WriteAllText((Join-Path $brand "e2e-tester-wordmark-$theme.svg"), $svg)
}
Write-Output 'Brand exports ready: PNG sizes, multi-resolution ICO, light/dark SVG wordmarks, and embedded WPF assets.'
