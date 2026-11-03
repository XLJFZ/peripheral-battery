# Build the Windows installer: portable single file + Inno Setup wrapper.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$version = '0.3.6'

# 1) Portable base (also produces the sha256 used in the release folder)
& ./build-portable.ps1
if ($LASTEXITCODE) { throw 'Portable build failed' }

# 2) Inno Setup compiler
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path -LiteralPath $iscc)) {
    throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or run the official installer.'
}

# 3) Compile
& $iscc (Join-Path $PSScriptRoot 'installer\PeripheralBattery.iss')
if ($LASTEXITCODE) { throw 'Installer compile failed' }

$setup = Join-Path $PSScriptRoot "releases\v$version\Peripheral-Battery-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Missing installer output: $setup" }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath (Join-Path $PSScriptRoot "releases\v$version\Peripheral-Battery-Setup-$version.sha256") -Value "$hash  Peripheral-Battery-Setup-$version.exe" -Encoding utf8
Write-Host "Installer ready: $setup"
