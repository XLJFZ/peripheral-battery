$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& ./build.ps1
if ($LASTEXITCODE) { throw 'Backend build failed' }
dotnet publish native/src/BluetoothBatteryMonitor.App/BluetoothBatteryMonitor.App.csproj -c Release -r win-x64 --self-contained true -o dist/PeripheralBattery-v0.2.4
if ($LASTEXITCODE) { throw 'Native build failed' }
& ./.venv/Scripts/python.exe -c "import shutil; shutil.copytree('dist/UniversalBatteryTray', 'dist/PeripheralBattery-v0.2.4/backend', dirs_exist_ok=True)"
Copy-Item -LiteralPath native/LICENSE -Destination dist/PeripheralBattery-v0.2.4/LICENSE-BluetoothBatteryMonitor.txt
Copy-Item -LiteralPath native/UPSTREAM.md -Destination dist/PeripheralBattery-v0.2.4/UPSTREAM.md
