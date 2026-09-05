$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectDir 'ArdorBatteryTray.cs'
$output = Join-Path $projectDir 'ArdorBatteryTray.exe'
$icon = Join-Path $projectDir 'app.ico'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Не найден встроенный компилятор .NET Framework.'
}

& (Join-Path $projectDir 'generate-icon.ps1')

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /win32icon:$icon `
    /out:$output $source

if ($LASTEXITCODE -ne 0) {
    throw "Сборка завершилась с кодом $LASTEXITCODE"
}

Write-Host "Готово: $output"
