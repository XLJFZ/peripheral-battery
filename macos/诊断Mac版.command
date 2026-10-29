#!/bin/bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
bash bootstrap.sh
mkdir -p diagnostics
OUT="diagnostics/bluetooth-$(date +%Y%m%d-%H%M%S).json"
if .venv/bin/python menu_app.py --diagnose > "$OUT"; then
  printf '已保存诊断文件：%s/%s\n' "$PWD" "$OUT"
else
  echo '诊断失败，请查看上方错误信息。' >&2
  exit 1
fi
read -r -p '按回车关闭。' _
