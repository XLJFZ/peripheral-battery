# Build the Windows installer: portable single file + Inno Setup wrapper.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$version = '0.4.2'

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

$built = Join-Path $PSScriptRoot "dist\installer\Peripheral-Battery-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $built)) { throw "Missing installer output: $built" }
$releaseDir = Join-Path $PSScriptRoot "releases\v$version"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$setup = Join-Path $releaseDir "Peripheral-Battery-Setup-$version.exe"
Copy-Item -LiteralPath $built -Destination $setup -Force
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath (Join-Path $PSScriptRoot "releases\v$version\Peripheral-Battery-Setup-$version.sha256") -Value "$hash  Peripheral-Battery-Setup-$version.exe" -Encoding utf8

# 4) Combined sha256.txt — the in-app updater fetches this asset and refuses to install
#    anything whose hash is missing from it. Write without BOM: Setup is often line 1.
$portable = Join-Path $releaseDir "Peripheral-Battery-$version-x64.exe"
$portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLower()
$lines = @(
    "$portableHash *Peripheral-Battery-$version-x64.exe"
    "$hash *Peripheral-Battery-Setup-$version.exe"
)
$checksum = Join-Path $releaseDir "sha256.txt"
[System.IO.File]::WriteAllText($checksum, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Installer ready: $setup"
Write-Host "Checksums ready: $checksum"
