"""Platform-independent parsing and policy for the macOS menu bar app."""
from dataclasses import dataclass
import re


def address(value):
    cleaned = re.sub(r"[:-]", "", str(value)).upper()
    return cleaned if re.fullmatch(r"[0-9A-F]{12}", cleaned) else None


def percent(value):
    if isinstance(value, bool):
        return None
    match = re.fullmatch(r"\s*(\d{1,3})\s*%?\s*", str(value))
    return int(match[1]) if match and int(match[1]) <= 100 else None


@dataclass(frozen=True)
class Device:
    id: str
    name: str
    parts: dict

    @property
    def level(self):
        # Case battery does not lower the earbuds' displayed level.
        ears = [v for k, v in self.parts.items() if k != "充电盒"]
        return min(ears or list(self.parts.values()), default=None)

    @property
    def description(self):
        return " · ".join(f"{key} {value}%" for key, value in self.parts.items()) or "系统未提供电量"


def parse_profiler(payload, paired_addresses):
    """Fail closed: require a valid snapshot, connected section and paired MAC."""
    controllers = payload.get("SPBluetoothDataType") if isinstance(payload, dict) else None
    if not isinstance(controllers, list) or not controllers:
        raise ValueError("系统蓝牙报告格式不受支持")
    paired = {a for value in paired_addresses if (a := address(value))}
    devices = {}
    fields = (("Main", "电量"), ("Left", "左耳"), ("Right", "右耳"), ("Case", "充电盒"))
    for controller in controllers:
        if not isinstance(controller, dict):
            raise ValueError("系统蓝牙报告格式不受支持")
        connected = controller.get("device_connected", [])
        if not isinstance(connected, list):
            raise ValueError("系统连接列表格式不受支持")
        for row in connected:
            if not isinstance(row, dict):
                raise ValueError("系统设备记录格式不受支持")
            for name, info in row.items():
                if not isinstance(info, dict):
                    continue
                device_id = address(info.get("device_address"))
                if not device_id or device_id not in paired:
                    continue
                parts = {label: value for suffix, label in fields
                         if (value := percent(info.get("device_batteryLevel" + suffix))) is not None}
                previous = devices.get(device_id)
                # Merge duplicate rows only by exact address, never by name.
                devices[device_id] = Device(device_id, str(name), {**(previous.parts if previous else {}), **parts})
    return list(devices.values())


def ordered(devices, order, primary):
    positions = {value: i for i, value in enumerate(order)}
    return sorted(devices, key=lambda d: (d.id != primary, positions.get(d.id, len(order)), d.id))


class Snapshot:
    def __init__(self):
        self.devices = []
        self.updated = None
        self.error = "正在读取"

    def success(self, devices, now):
        self.devices, self.updated, self.error = list(devices), now, ""

    def invalidate(self, reason):
        self.devices, self.updated, self.error = [], None, reason

    def expire(self, now, max_age):
        if self.updated is not None and now - self.updated > max_age:
            self.invalidate("读数已过期，等待刷新")
            return True
        return False


def needs_alert(device, threshold, alerted):
    return device.level is not None and device.level <= threshold and device.id not in alerted


def rearm_alert(device, threshold, alerted):
    if device.level is not None and device.level >= threshold + 5:
        alerted.discard(device.id)
