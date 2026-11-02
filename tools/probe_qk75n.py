"""Passive QK75N vendor-interface capture. No reports are sent; no key input read."""
import argparse
import json
from pathlib import Path
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    import hid
    rows = []
    for info in hid.enumerate(0x05AC, 0x024F):
        if info.get('product_string') != 'QK75N' or info.get('usage_page', 0) not in (0xFF13, 0xFF68, 0xFFFF):
            continue
        row = {k: info.get(k) for k in ('product_string', 'interface_number', 'usage_page', 'usage')}
        row['reports'] = []
        device = hid.device()
        try:
            device.open_path(info['path'])
            end = time.monotonic() + 3
            while time.monotonic() < end and len(row['reports']) < 32:
                data = device.read(512, 200)
                if data:
                    row['reports'].append({'time': time.time(), 'hex': bytes(data).hex()})
        except OSError as exc:
            row['error'] = str(exc)
        finally:
            device.close()
        rows.append(row)
    result = {'mode': 'passive vendor input only; zero commands sent', 'interfaces': rows}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
