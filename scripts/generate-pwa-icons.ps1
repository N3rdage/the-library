# Generates the PWA PNG icons for BookTracker.
#
# The SVG master at BookTracker.Web/wwwroot/icons/icon.svg is the source of
# truth for the design. This script redraws the same design via GDI+ so the
# PNGs are reproducible from the repo without external tools like Inkscape
# or ImageMagick.
#
# Run from the repo root:
#   pwsh ./scripts/generate-pwa-icons.ps1

Add-Type -AssemblyName System.Drawing

function Draw-Icon {
    param(
        [int]$Size,
        [string]$Path
    )

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Background: rounded purple square (theme_color #6750A4). The outer
    # rect is filled corner-to-corner; the "rounded" corners come from
    # browsers applying the mask — for non-maskable contexts the square
    # looks fine.
    $bg = [System.Drawing.Color]::FromArgb(103, 80, 164)
    $g.Clear($bg)

    # Book spine (darker purple stripe on the left side).
    $spine = [System.Drawing.Color]::FromArgb(79, 55, 139)
    $spineBrush = New-Object System.Drawing.SolidBrush($spine)
    # Spine: x=18.75% of width, y=14%, width=19%, height=72%.
    $g.FillRectangle($spineBrush,
        [single]($Size * 0.1875),
        [single]($Size * 0.14),
        [single]($Size * 0.1875),
        [single]($Size * 0.72))
    $spineBrush.Dispose()

    # Book pages: three horizontal white lines, varying widths to suggest text.
    $lineWidth = [single]($Size * 0.028)
    $whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $lineWidth)
    $whitePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $whitePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $leftX  = [single]($Size * 0.41)
    $longR  = [single]($Size * 0.78)
    $shortR = [single]($Size * 0.68)
    $line1Y = [single]($Size * 0.33)
    $line2Y = [single]($Size * 0.49)
    $line3Y = [single]($Size * 0.65)

    $g.DrawLine($whitePen, $leftX, $line1Y, $longR,  $line1Y)
    $g.DrawLine($whitePen, $leftX, $line2Y, $longR,  $line2Y)
    $g.DrawLine($whitePen, $leftX, $line3Y, $shortR, $line3Y)

    $whitePen.Dispose()
    $g.Dispose()

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $bmp.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Host "  wrote $Path ($Size x $Size)"
}

$iconsDir = Join-Path $PSScriptRoot '..' 'BookTracker.Web/wwwroot/icons'

Write-Host "Generating PWA icons -> $iconsDir"

Draw-Icon -Size 192 -Path (Join-Path $iconsDir 'icon-192.png')
Draw-Icon -Size 512 -Path (Join-Path $iconsDir 'icon-512.png')
Draw-Icon -Size 180 -Path (Join-Path $iconsDir 'apple-touch-icon.png')

Write-Host "Done."
