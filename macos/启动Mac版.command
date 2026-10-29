#!/bin/bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
bash bootstrap.sh
exec .venv/bin/python menu_app.py
