$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$font = New-Object System.Drawing.Font 'Georgia',142,([System.Drawing.FontStyle]::Bold),([System.Drawing.GraphicsUnit]::Pixel)
$titleFont = New-Object System.Drawing.Font 'Georgia',20,([System.Drawing.FontStyle]::Bold),([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center

function Rounded-Rectangle([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $Radius
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$frame = Rounded-Rectangle 7 7 242 242 15
$face = Rounded-Rectangle 13 13 230 230 11
$metal = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(7,7,242,242),
    [System.Drawing.Color]::FromArgb(99,101,99), [System.Drawing.Color]::FromArgb(29,31,29), 90)
$rainbow = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(13,13,230,230),
    [System.Drawing.Color]::Red, [System.Drawing.Color]::Purple, 0)
$blend = New-Object System.Drawing.Drawing2D.ColorBlend 7
$blend.Colors = @('B33854','B16D30','999537','369466','32878D','405C99','815089') |
    ForEach-Object { [System.Drawing.ColorTranslator]::FromHtml('#' + $_) }
$blend.Positions = [single[]]@(0, 0.1667, 0.3333, 0.5, 0.6667, 0.8333, 1)
$rainbow.InterpolationColors = $blend
$shade = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(13,13,230,230),
    [System.Drawing.Color]::FromArgb(12,255,255,255), [System.Drawing.Color]::FromArgb(82,0,0,0), 90)
$caption = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(225,33,35,33))
$ink = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(245,243,231))
$shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(110,14,17,14))
$pip = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(45,48,43))
$die = Rounded-Rectangle 180 145 44 44 7
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::FromArgb(25,27,25))
    $graphics.FillPath($metal, $frame)
    $graphics.FillPath($rainbow, $face)
    $graphics.FillPath($shade, $face)
    $graphics.DrawString('G', $font, $shadow, [System.Drawing.RectangleF]::new(2,11,254,185), $format)
    $graphics.DrawString('G', $font, $ink, [System.Drawing.RectangleF]::new(0,8,254,185), $format)
    $graphics.FillPath($ink, $die)
    foreach ($point in @(@(191,156), @(213,156), @(202,167), @(191,178), @(213,178))) {
        $graphics.FillEllipse($pip, [single]($point[0] - 3.5), [single]($point[1] - 3.5), [single]7, [single]7)
    }
    $graphics.SetClip($face)
    $graphics.FillRectangle($caption, 13, 203, 230, 40)
    $graphics.ResetClip()
    $graphics.DrawString('GUNDOMIZER', $titleFont, $ink, [System.Drawing.RectangleF]::new(13,203,230,38), $format)
    $bitmap.Save((Join-Path $repoRoot 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($resource in @($format,$font,$titleFont,$frame,$face,$metal,$rainbow,$shade,$caption,$ink,$shadow,$pip,$die,$graphics,$bitmap)) {
        $resource.Dispose()
    }
}
