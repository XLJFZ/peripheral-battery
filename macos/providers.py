"""macOS-only read-only adapter. Does not discover, connect or pair devices."""
import json
import subprocess
import sys
from core import parse_profiler


def scan():
    if sys.platform != "darwin":
        raise RuntimeError("此程序只能在 macOS 上读取设备")
    from IOBluetooth import IOBluetoothDevice
    paired = [str(device.addressString()) for device in (IOBluetoothDevice.pairedDevices() or [])
              if device.isPaired() and device.isConnected()]
    result = subprocess.run(
        ["/usr/sbin/system_profiler", "SPBluetoothDataType", "-json", "-detailLevel", "basic"],
        capture_output=True, timeout=25, check=True, encoding="utf-8",
    )
    devices = parse_profiler(json.loads(result.stdout), paired)
    # Recheck after system_profiler, which can take several seconds.
    connected = {str(d.addressString()).replace(":", "").replace("-", "").upper()
                 for d in (IOBluetoothDevice.pairedDevices() or []) if d.isPaired() and d.isConnected()}
    return [d for d in devices if d.id in connected]
