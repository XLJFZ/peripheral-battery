#!/bin/bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
bash bootstrap.sh
.venv/bin/python -m pip install 'pyinstaller>=6.11,<7'
.venv/bin/python build_macos.py
open dist
