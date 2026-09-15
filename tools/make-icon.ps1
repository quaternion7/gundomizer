$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$font = New-Object System.Drawing.Font 'Arial',112,([System.Drawing.FontStyle]::Bold),([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(24,25,30))
    $colors = @('FF5664','FFAC42','FFDE59','67D994','5FC6FA','A786FF')
    for ($i = 0; $i -lt $colors.Count; $i++) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#' + $colors[$i]))
        try { $graphics.FillRectangle($brush, 16 + $i * 37, 208, 32, 14) } finally { $brush.Dispose() }
    }
    $graphics.DrawString('G', $font, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0,20,256,180), $format)
    $bitmap.Save((Join-Path $repoRoot 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $format.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
