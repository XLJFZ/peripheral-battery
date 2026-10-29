$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
python -m venv .venv
if ($LASTEXITCODE) { throw 'Cannot create Python environment' }
& ./.venv/Scripts/python.exe -m pip install -r requirements.txt pyinstaller==6.22.3
if ($LASTEXITCODE) { throw 'Cannot install dependencies' }
& ./.venv/Scripts/python.exe -m PyInstaller --noconfirm --clean --windowed --onedir --name UniversalBatteryTray --add-data 'razer_devices.json;.' --add-data 'web;web' universal_battery.py
if ($LASTEXITCODE) { throw 'Build failed' }
