#!/bin/bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if [[ "$(uname -s)" != Darwin ]]; then
  echo '此脚本需要在 Mac 上运行。' >&2
  exit 1
fi
PYTHON="${PYTHON:-python3}"
if ! command -v "$PYTHON" >/dev/null 2>&1; then
  echo '请先安装 Python 3.10 或更新版本，再重新运行。' >&2
  exit 1
fi
"$PYTHON" -c 'import sys; assert sys.version_info >= (3,10), "需要 Python 3.10 或更新版本"'
if [[ ! -x .venv/bin/python ]]; then
  "$PYTHON" -m venv .venv
fi
if ! .venv/bin/python -c 'import rumps, IOBluetooth, PIL' >/dev/null 2>&1; then
  .venv/bin/python -m pip install -r requirements.txt
fi
