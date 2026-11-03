"""Assert the settings footer fits inside the window in every interface language.

Runs the published --settings-probe, which measures the four footer buttons' desired
width per language (labels differ a lot; German is historically the widest), and fails
if any language would overflow the window's content area.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--exe', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
if sys.platform != 'win32':
    parser.exit(1, 'This check requires Windows.\n')

exe = args.exe.resolve()
report = args.report.resolve()
report.parent.mkdir(parents=True, exist_ok=True)
if not exe.is_file():
    raise SystemExit(f'Missing executable: {exe}')

with tempfile.TemporaryDirectory(prefix='peripheral-footer-') as tmp:
    probe = Path(tmp) / 'footer.json'
    subprocess.run([str(exe), '--settings-probe', str(probe)], check=True, timeout=180)
    data = json.loads(probe.read_text(encoding='utf-8-sig'))

problems = []
widths = {}
for entry in data['languages']:
    lang = entry['language']
    desired = entry['footerDesiredWidth']
    available = entry['contentActualWidth']
    widths[lang] = {'footer': desired, 'content': available}
    # 2 px tolerance for rounding; the footer is right-aligned inside the content grid.
    if desired > available + 2:
        problems.append(f'{lang}: footer needs {desired} px but only {available} px available')

result = {
    'success': not problems,
    'problems': problems,
    'widths': widths,
}
report.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
if problems:
    raise SystemExit('Settings footer check failed:\n- ' + '\n- '.join(problems))
widest = max(widths, key=lambda k: widths[k]['footer'])
print(f'Settings footer check passed: all {len(widths)} languages fit '
      f'(widest: {widest} at {widths[widest]["footer"]} px).')
