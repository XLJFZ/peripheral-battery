# Build the portable single-file release: one EXE, no installer, no runtime prerequisite.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$version = '0.3.18'
$python = if (Test-Path -LiteralPath './.venv/Scripts/python.exe') { './.venv/Scripts/python.exe' } else { 'python' }
$out = "dist/portable-$version"
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish native/src/BluetoothBatteryMonitor.App/BluetoothBatteryMonitor.App.csproj `
    -c Release -r win-x64 --self-contained true -o $out `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE) { throw 'Portable publish failed' }
& $python tools/check_portable_exe.py "$out/BluetoothBatteryMonitor.App.exe" --report "build/portable-smoke.json"
if ($LASTEXITCODE) { throw 'Portable verification failed' }
& $python tools/check_flyout_layout.py --exe "$out/BluetoothBatteryMonitor.App.exe" --report "build/flyout-layout.json"
if ($LASTEXITCODE) { throw 'Flyout layout verification failed' }
$release = "releases/v$version"
New-Item -ItemType Directory -Force -Path $release | Out-Null
$name = "Peripheral-Battery-$version-x64.exe"
Copy-Item -LiteralPath "$out/BluetoothBatteryMonitor.App.exe" -Destination "$release/$name" -Force
$hash = (Get-FileHash -LiteralPath "$release/$name" -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath "$release/Peripheral-Battery-$version-x64.sha256" -Value "$hash  $name" -Encoding utf8
Write-Host "Portable build ready: $release/$name"
