# make-icons.ps1
# Generates original Go-themed icon PNGs using System.Drawing (GDI+).
# No downloaded artwork: the mark is an original rounded-rect badge in Go cyan
# (#00ADD8) with bold white "GO" text; the file variant adds a document-page
# outline with a folded corner. Re-run to regenerate deterministically.
#
# Outputs (in this script's directory):
#   go-project-16.png, go-project-32.png  - badge only
#   go-file-16.png,    go-file-32.png     - badge + document page glyph
#   template-icon-32.png                  - badge (32px, template dialog icon)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

$goCyan = [System.Drawing.Color]::FromArgb(255, 0x00, 0xAD, 0xD8)
$white  = [System.Drawing.Color]::White

function New-RoundedRectanglePath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-GoBadge {
    # Draws the rounded-rect GO badge into graphics context $Graphics covering
    # the square region (x, y, size, size).
    param(
        [System.Drawing.Graphics]$Graphics,
        [float]$X, [float]$Y, [float]$Size
    )
    $radius = [Math]::Max(2.0, $Size * 0.2)
    $path = New-RoundedRectanglePath -X $X -Y $Y -Width $Size -Height $Size -Radius $radius
    $brush = New-Object System.Drawing.SolidBrush $goCyan
    $Graphics.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()

    # Bold white "GO", centered. Use GenericTypographic-ish centering via
    # StringFormat; pick font size relative to badge size.
    $fontSize = $Size * 0.42
    $font = New-Object System.Drawing.Font(
        'Segoe UI',
        $fontSize,
        [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    $stringFormat = New-Object System.Drawing.StringFormat
    $stringFormat.Alignment = [System.Drawing.StringAlignment]::Center
    $stringFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textBrush = New-Object System.Drawing.SolidBrush $white
    $rectangle = New-Object System.Drawing.RectangleF($X, $Y, $Size, $Size)
    $Graphics.DrawString('GO', $font, $textBrush, $rectangle, $stringFormat)
    $textBrush.Dispose(); $stringFormat.Dispose(); $font.Dispose()
}

function New-Icon {
    param(
        [int]$PixelSize,
        [string]$OutFile,
        [switch]$FileVariant
    )
    $pixelFormat = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $bitmap = New-Object System.Drawing.Bitmap($PixelSize, $PixelSize, $pixelFormat)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    if ($FileVariant) {
        # Document page outline (upper-left) with folded corner, plus a smaller
        # GO badge anchored bottom-right.
        $penWidth = [Math]::Max(1.0, $PixelSize / 16.0)
        $pagePen = New-Object System.Drawing.Pen($goCyan, $penWidth)
        $pagePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        # Page geometry: margin, page size and the folded corner size.
        $margin     = $PixelSize * 0.06
        $pageWidth  = $PixelSize * 0.56
        $pageHeight = $PixelSize * 0.78
        $fold       = $pageWidth * 0.32
        $left = $margin; $top = $margin

        # Page outline with cut corner (top-right fold)
        [System.Drawing.PointF[]]$points = @(
            (New-Object System.Drawing.PointF($left, $top)),
            (New-Object System.Drawing.PointF(($left + $pageWidth - $fold), $top)),
            (New-Object System.Drawing.PointF(($left + $pageWidth), ($top + $fold))),
            (New-Object System.Drawing.PointF(($left + $pageWidth), ($top + $pageHeight))),
            (New-Object System.Drawing.PointF($left, ($top + $pageHeight)))
        )
        $graphics.DrawPolygon($pagePen, $points)
        # Fold crease
        $graphics.DrawLine($pagePen,
            ($left + $pageWidth - $fold), $top,
            ($left + $pageWidth - $fold), ($top + $fold))
        $graphics.DrawLine($pagePen,
            ($left + $pageWidth - $fold), ($top + $fold),
            ($left + $pageWidth), ($top + $fold))
        $pagePen.Dispose()

        if ($PixelSize -ge 32) {
            # 32px and up: small text lines on the page for legibility (the
            # 16px icon skips this detail).
            $linePenWidth = [Math]::Max(1.0, $penWidth * 0.75)
            $linePen = New-Object System.Drawing.Pen($goCyan, $linePenWidth)
            $lineLeft = $left + $pageWidth * 0.18
            $lineLength = $pageWidth * 0.55
            foreach ($lineFraction in 0.38, 0.54) {
                $lineTop = $top + $pageHeight * $lineFraction
                $graphics.DrawLine($linePen,
                    $lineLeft, $lineTop,
                    ($lineLeft + $lineLength), $lineTop)
            }
            $linePen.Dispose()
        }

        # Badge bottom-right. At tiny sizes the badge must dominate so the
        # "GO" text stays readable; at 32px+ leave more of the page visible.
        $badgeFraction = if ($PixelSize -lt 24) { 0.78 } else { 0.62 }
        $badgeSize = $PixelSize * $badgeFraction
        $badgeOffset = $PixelSize - $badgeSize
        Draw-GoBadge -Graphics $graphics -X $badgeOffset -Y $badgeOffset -Size $badgeSize
    }
    else {
        # Full-canvas badge with tiny margin
        $margin = [Math]::Max(0.0, $PixelSize * 0.03)
        Draw-GoBadge -Graphics $graphics -X $margin -Y $margin -Size ($PixelSize - 2 * $margin)
    }

    $graphics.Dispose()
    $bitmap.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "Wrote $OutFile"
}

New-Icon -PixelSize 16 -OutFile (Join-Path $outputDirectory 'go-project-16.png')
New-Icon -PixelSize 32 -OutFile (Join-Path $outputDirectory 'go-project-32.png')
New-Icon -PixelSize 16 -OutFile (Join-Path $outputDirectory 'go-file-16.png') -FileVariant
New-Icon -PixelSize 32 -OutFile (Join-Path $outputDirectory 'go-file-32.png') -FileVariant
New-Icon -PixelSize 32 -OutFile (Join-Path $outputDirectory 'template-icon-32.png')
