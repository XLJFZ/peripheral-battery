"""Assert the flyout panel hugs its content instead of leaving a card-sized gap.

Runs the published --layout-probe and checks that the window height follows the measured
content height, shrinks again after the empty state, and stops at MaxHeight for long lists.
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

with tempfile.TemporaryDirectory(prefix='peripheral-layout-') as tmp:
    probe = Path(tmp) / 'layout.json'
    subprocess.run([str(exe), '--layout-probe', str(probe)], check=True, timeout=180)
    data = json.loads(probe.read_text(encoding='utf-8-sig'))

cases = {case['name']: case for case in data['cases']}
problems = []

def slack(case_name, key='afterContent'):
    case = cases[case_name][key]
    return abs(case['windowHeight'] - case['contentDesired'])

# One device: the panel must match the content, not keep a spare card of height.
for name in ('device-before-show', 'empty-then-device', 'empty-then-two-devices'):
    remaining = slack(name)
    if remaining > 2:
        problems.append(f'{name}: window {cases[name]["afterContent"]["windowHeight"]} vs content '
                        f'{cases[name]["afterContent"]["contentDesired"]} ({remaining:.1f} px of empty space)')

# Empty state must collapse once devices arrive.
empty = cases['empty-then-device']['emptyState']['windowHeight']
filled = cases['empty-then-device']['afterContent']['windowHeight']
if not filled < empty:
    problems.append(f'empty-then-device: panel did not shrink after devices arrived ({empty} -> {filled})')

# Long list must cap at MaxHeight and scroll instead of growing the window.
crowded = cases['empty-then-ten-devices']['afterContent']
if crowded['windowHeight'] > 660.5:
    problems.append(f'ten devices: window grew to {crowded["windowHeight"]}, above MaxHeight 660')
if crowded['scrollDesired'] > crowded['scrollHeight'] + 1:
    problems.append('ten devices: list is clipped instead of scrollable')

result = {
    'success': not problems,
    'problems': problems,
    'heights': {name: {'window': case['afterContent']['windowHeight'],
                       'content': case['afterContent']['contentDesired']}
                for name, case in cases.items()},
}
report.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
if problems:
    raise SystemExit('Flyout layout check failed:\n- ' + '\n- '.join(problems))
print('Flyout layout check passed: panel height follows content, shrinks after the empty state, '
      'and caps at MaxHeight for long lists.')
