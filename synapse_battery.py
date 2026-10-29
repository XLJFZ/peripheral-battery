"""Read existing Synapse logs only. No private IPC, discovery or device writes."""
from dataclasses import dataclass
from datetime import datetime
import json
import os
from pathlib import Path
import re
import subprocess
import time

MAX_AGE = 120
TAIL_BYTES = 1024 * 1024
TIMESTAMP = re.compile(r"^\[(\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}\.\d{1,6})\]")


@dataclass(frozen=True)
class Snapshot:
    sampled_at: float
    devices: list


def serial(value):
    if not isinstance(value, str):
        return None
    value = value.strip().upper()
    if len(value) < 5 or not re.fullmatch(r"[A-Z0-9-]+", value) or set(value) <= {"0", "-"}:
        return None
    return value


def parse_snapshot(line):
    match = TIMESTAMP.match(line)
    if not match or "devices:" not in line:
        return None
    try:
        stamp = datetime.strptime(match[1], "%Y/%m/%d %H:%M:%S.%f").timestamp()
        devices, _ = json.JSONDecoder().raw_decode(line.split("devices:", 1)[1].strip())
    except (ValueError, OverflowError):
        return None
    return Snapshot(stamp, devices) if isinstance(devices, list) else None


def running():
    if os.name != "nt":
        return False
    try:
        result = subprocess.run(["tasklist.exe", "/FI", "IMAGENAME eq RazerAppEngine.exe", "/FO", "CSV", "/NH"],
                                capture_output=True, timeout=3, creationflags=0x08000000)
        return result.returncode == 0 and b'"razerappengine.exe"' in result.stdout.lower()
    except (OSError, subprocess.SubprocessError):
        return False


def load_snapshot(directory=None, now=None, is_running=None):
    now = time.time() if now is None else now
    if not (running() if is_running is None else is_running):
        return None, "雷云未运行或无法确认进程，使用 HID 直读"
    if directory is None:
        directory = Path.home() / "AppData/Local/Razer/RazerAppEngine/User Data/Logs"
    snapshots = []
    try:
        paths = sorted(Path(directory).glob("systray_systrayv*.log"), key=lambda p: p.stat().st_mtime, reverse=True)[:4]
        for path in paths:
            try:
                with path.open("rb") as stream:
                    stream.seek(max(0, path.stat().st_size - TAIL_BYTES))
                    lines = stream.read(TAIL_BYTES).decode("utf-8", errors="replace").splitlines()
                for line in reversed(lines):
                    snapshot = parse_snapshot(line)
                    if snapshot is not None:
                        snapshots.append(snapshot)
                        break
            except OSError:
                continue
    except OSError:
        return None, "雷云日志不可读，使用 HID 直读"
    if not snapshots:
        return None, "未找到有效雷云日志，使用 HID 直读"
    # Choose the newest whole snapshot, including empty/off snapshots. Never resurrect
    # a device from an older rotated log after its newer snapshot removed it.
    latest = max(snapshots, key=lambda s: s.sampled_at)
    if not -5 <= now - latest.sampled_at <= MAX_AGE:
        return None, "雷云日志已过期或时间异常，使用 HID 直读"
    return latest, ""


def match_device(snapshot, candidates):
    identities = {value for d in candidates if (value := serial(d.get("serial_number")))}
    if snapshot is None or len(identities) != 1:
        return None
    matches = []
    for device in snapshot.devices:
        if not isinstance(device, dict) or device.get("hasBattery") is not True:
            continue
        if serial(device.get("serialNumber")) not in identities:
            continue
        power = device.get("powerStatus")
        if not isinstance(power, dict):
            continue
        level = power.get("level")
        if isinstance(level, bool) or not isinstance(level, int) or not 0 <= level <= 100:
            continue
        status = power.get("chargingStatus")
        if status not in ("NoCharge_BatteryFull", "Charging"):
            continue
        matches.append((level, status == "Charging", snapshot.sampled_at))
    return matches[0] if len(matches) == 1 else None
