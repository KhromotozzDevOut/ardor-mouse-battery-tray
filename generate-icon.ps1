$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$iconPath = Join-Path $projectDir 'app.ico'
$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(35, 205, 95))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$dark = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 15, 20, 18)), 4
$graphics.FillRectangle($green, 5, 10, 48, 44)
$graphics.DrawRectangle($dark, 5, 10, 48, 44)
$graphics.FillRectangle($green, 53, 23, 7, 18)
$graphics.FillPolygon($white, @(
    (New-Object System.Drawing.Point 34, 15),
    (New-Object System.Drawing.Point 19, 34),
    (New-Object System.Drawing.Point 30, 34),
    (New-Object System.Drawing.Point 24, 50),
    (New-Object System.Drawing.Point 43, 28),
    (New-Object System.Drawing.Point 32, 28)
))

$handle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($handle)
$stream = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
try {
    $icon.Save($stream)
}
finally {
    $stream.Dispose()
    $icon.Dispose()
    $graphics.Dispose()
    $green.Dispose()
    $white.Dispose()
    $dark.Dispose()
    $bitmap.Dispose()
}
