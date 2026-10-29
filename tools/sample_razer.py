"""Bounded read-only battery sampling; never sends configuration commands."""
import argparse
from dataclasses import asdict
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from battery_core import read_hid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=int, default=3, choices=range(1, 13))
    parser.add_argument("--interval", type=int, default=5, choices=range(1, 61))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    import hid
    catalog = {e["pid"]: e for e in json.loads((ROOT / "razer_devices.json").read_text())}
    class RazerOnly:
        @staticmethod
        def enumerate():
            return [d for d in hid.enumerate() if d["vendor_id"] == 0x1532 and d["product_id"] in catalog]
        device = staticmethod(hid.device)
    samples = []
    for i in range(args.samples):
        readings = [asdict(r) for r in read_hid(RazerOnly, catalog)]
        samples.append({"captured_at": time.time(), "readings": readings})
        if i + 1 < args.samples:
            time.sleep(args.interval)
    report = {"formula": "round-half-up(raw * 100 / 255)", "samples": samples}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    for sample in samples:
        print(json.dumps({"time": sample["captured_at"], "devices": [
            {k: r[k] for k in ("name", "raw_value", "percent", "charging", "error")} for r in sample["readings"]]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
