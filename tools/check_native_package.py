"""Verify the actual self-contained package, including WPF popup dependencies."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

parser=argparse.ArgumentParser()
parser.add_argument('folder',type=Path)
parser.add_argument('--report',type=Path,required=True)
args=parser.parse_args()
if sys.platform!='win32':parser.exit(1,'This check requires Windows.\n')
folder=args.folder.resolve()
report=args.report.resolve()
report.parent.mkdir(parents=True,exist_ok=True)
for name in ('BluetoothBatteryMonitor.App.exe','BluetoothBatteryMonitor.App.dll',
             'BluetoothBatteryMonitor.App.runtimeconfig.json','Accessibility.dll'):
    if not (folder/name).is_file():raise SystemExit('Missing package dependency: '+name)
subprocess.run([str(folder/'BluetoothBatteryMonitor.App.exe'),'--ui-smoke',str(report)],check=True,timeout=30)
result=json.loads(report.read_text())
assert result['success'] and result['menuOpened'] and result['tooltipOpened'],result
assert Path(result['accessibilityAssembly']).resolve()==folder/'Accessibility.dll',result
print('Published UI check passed: Accessibility, context menu and tooltip loaded from package.')
