# Build the portable single-file release: one EXE, no installer, no runtime prerequisite.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$version = '0.3.6'
$out = "dist/portable-$version"
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish native/src/BluetoothBatteryMonitor.App/BluetoothBatteryMonitor.App.csproj `
    -c Release -r win-x64 --self-contained true -o $out `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE) { throw 'Portable publish failed' }
& ./.venv/Scripts/python.exe tools/check_portable_exe.py "$out/BluetoothBatteryMonitor.App.exe" --report "build/portable-smoke.json"
if ($LASTEXITCODE) { throw 'Portable verification failed' }
& ./.venv/Scripts/python.exe tools/check_flyout_layout.py --exe "$out/BluetoothBatteryMonitor.App.exe" --report "build/flyout-layout.json"
if ($LASTEXITCODE) { throw 'Flyout layout verification failed' }
$release = "releases/v$version"
New-Item -ItemType Directory -Force -Path $release | Out-Null
$name = "Peripheral-Battery-$version-x64.exe"
Copy-Item -LiteralPath "$out/BluetoothBatteryMonitor.App.exe" -Destination "$release/$name" -Force
$hash = (Get-FileHash -LiteralPath "$release/$name" -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath "$release/Peripheral-Battery-$version-x64.sha256" -Value "$hash  $name" -Encoding utf8
Write-Host "Portable build ready: $release/$name"
