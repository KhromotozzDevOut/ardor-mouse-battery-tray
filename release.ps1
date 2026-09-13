param(
    [string]$Version = '1.2.0'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $projectDir 'dist'
$stageDir = Join-Path $distDir ("ArdorBatteryTray-v$Version-portable")
$zipPath = "$stageDir.zip"

& (Join-Path $projectDir 'build.ps1')

if (Test-Path -LiteralPath $stageDir) {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageDir)
    $resolvedDist = [System.IO.Path]::GetFullPath($distDir) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStage.StartsWith($resolvedDist, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe staging path: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'ArdorBatteryTray.exe') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'README.ru.md') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'LICENSE.txt') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'THIRD_PARTY_NOTICES.txt') -Destination $stageDir
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $isccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
} else {
    $isccPath = $iscc.Source
}

if ($isccPath) {
    Push-Location $projectDir
    try {
        & $isccPath "/DMyAppVersion=$Version" (Join-Path $projectDir 'installer.iss')
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
} else {
    Write-Warning 'Inno Setup was not found: the portable ZIP was created, but the installer was skipped.'
}

$artifacts = @((Get-Item -LiteralPath $zipPath))
$setupPath = Join-Path $distDir ("ArdorBatteryTray-Setup-v$Version.exe")
if (Test-Path -LiteralPath $setupPath) {
    $artifacts += Get-Item -LiteralPath $setupPath
}
$checksumPath = Join-Path $distDir 'SHA256SUMS.txt'
$lines = foreach ($artifact in $artifacts) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact.FullName
    '{0} *{1}' -f $hash.Hash.ToLowerInvariant(), $artifact.Name
}
$lines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ''
Write-Host 'Release artifacts:'
Get-ChildItem -LiteralPath $distDir -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
