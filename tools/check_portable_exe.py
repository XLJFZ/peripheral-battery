"""Verify the portable single-file build: one EXE copied alone must still pass the popup smoke test."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('exe', type=Path)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
if sys.platform != 'win32':
    parser.exit(1, 'This check requires Windows.\n')

exe = args.exe.resolve()
report = args.report.resolve()
report.parent.mkdir(parents=True, exist_ok=True)
if not exe.is_file():
    raise SystemExit(f'Missing portable executable: {exe}')

siblings = [p.name for p in exe.parent.iterdir() if p.is_file() and p.name != exe.name]
if siblings:
    raise SystemExit(f'Portable folder must only contain the executable, found: {siblings}')

workdir = Path(tempfile.mkdtemp(prefix='peripheral-portable-'))
try:
    target = workdir / exe.name
    shutil.copy2(exe, target)
    smoke = workdir / 'ui-smoke.json'
    subprocess.run([str(target), '--ui-smoke', str(smoke)], check=True, timeout=180, cwd=workdir)
    result = json.loads(smoke.read_text(encoding='utf-8-sig'))
    assert result['success'], result
    assert result['menuOpened'] and result['tooltipOpened'], result
    assert result.get('singleFile') is True, result
    result['sizeBytes'] = target.stat().st_size
    report.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"Portable single-file check passed: {target.stat().st_size / 1048576:.1f} MiB, "
          f"menu/tooltip/polling render from the bundle.")
finally:
    shutil.rmtree(workdir, ignore_errors=True)
