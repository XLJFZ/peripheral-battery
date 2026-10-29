"""Read-only battery providers. No commands are sent to unknown HID models."""
import json
import re
import subprocess
import time
from dataclasses import asdict, dataclass
from functools import reduce
from operator import xor
from pathlib import Path


@dataclass
class Reading:
    key: str
    name: str
    provider: str
    percent: int | None = None
    charging: bool | None = None
    error: str = ''


def request(tid, command):
    payload = bytearray(90)
    payload[1] = tid
    payload[5:8] = bytes([2, 7, command])
    payload[88] = reduce(xor, payload[2:88], 0)
    return bytes([0]) + payload


def decode(raw, tid, command):
    if len(raw) != 91 or raw[0] != 0:
        raise ValueError('Invalid feature report length or ID')
    p = raw[1:]
    if reduce(xor, p[2:88], 0) != p[88]:
        raise ValueError('Invalid checksum')
    if p[0] != 2:
        raise ValueError(f'Device status 0x{p[0]:02x} (sleeping, busy or unavailable)')
    if p[1] != tid or p[5] != 2 or p[6:8] != bytes([7, command]):
        raise ValueError('Response belongs to another request')
    return p[9]


def query(dev, entry, command):
    error = None
    for _ in range(3):
        try:
            if dev.send_feature_report(request(entry['tid'], command)) != 91:
                raise OSError('Incomplete feature report write')
            time.sleep(entry['wait'])
            return decode(bytes(dev.get_feature_report(0, 91)), entry['tid'], command)
        except (OSError, ValueError) as exc:
            error = exc
            time.sleep(.04)
    raise OSError(str(error))


def collection_key(path):
    s = path.decode(errors='replace') if isinstance(path, bytes) else str(path)
    parts = s.lower().split('#')
    if len(parts) >= 3 and '&col' in parts[1]:
        return '#'.join([parts[0], parts[1].split('&col')[0], parts[2].rsplit('&', 1)[0]])
    return s.lower()


def read_hid(hid, catalog):
    groups = {}
    for d in hid.enumerate():
        vid, pid = d['vendor_id'], d['product_id']
        entry = catalog.get(pid) if vid == 0x1532 else None
        if entry and d.get('interface_number') != entry['interface']:
            continue
        if not entry and (vid, pid, d.get('usage_page')) not in {
                (0x5253, 0x1021, 0xFF01), (0x41E4, 0x2001, 0xFF04)}:
            continue
        key = collection_key(d['path'])
        groups.setdefault((vid, pid, key), []).append(d)
    result = []
    for (vid, pid, key), candidates in groups.items():
        entry = catalog.get(pid) if vid == 0x1532 else None
        name = entry['name'] if entry else ('MCHOSE A7' if vid == 0x5253 else 'MCHOSE G87')
        row = Reading('hid:' + key, name, 'Razer HID' if entry else 'MCHOSE HID')
        candidates.sort(key=lambda d: (d.get('usage_page'), d.get('usage')) != (1, 2))
        for d in candidates:
            dev = hid.device()
            try:
                dev.open_path(d['path'])
                if entry:
                    row.percent = int(query(dev, entry, 0x80) * 100 / 255 + .5)
                    if entry['charging']:
                        try:
                            row.charging = bool(query(dev, entry, 0x84))
                        except OSError:
                            row.charging = None
                elif vid == 0x5253:
                    dev.send_feature_report(bytes([0x11, 0xF9, 0xFF] + [0xFF] * 17))
                    time.sleep(.12)
                    resp = dev.get_feature_report(0x11, 65)
                    if len(resp) < 14:
                        raise ValueError('Short A7 response')
                    row.percent = resp[13] ^ 0xFF
                else:
                    resp = dev.get_feature_report(6, 8)
                    if len(resp) < 4:
                        raise ValueError('Short G87 response')
                    row.percent = resp[3]
                if not 0 <= row.percent <= 100:
                    row.percent = None
                    raise ValueError('Battery value out of range')
                row.error = ''
                break
            except (OSError, ValueError) as exc:
                row.error = str(exc)
            finally:
                dev.close()
        result.append(row)
    return result


def parse_windows(rows):
    if isinstance(rows, dict):
        rows = [rows]
    result = []
    for r in rows:
        value = r.get('percent')
        if isinstance(value, (int, float)) and not isinstance(value, bool) and 0 <= value <= 100:
            result.append(Reading('windows:' + r['id'], r.get('name') or r['id'],
                                  'Windows battery', int(value)))
    return result


def read_windows():
    script = r'''
    $ErrorActionPreference = 'Stop'
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $devices = @(Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^(BTH|HID|USB)' })
    $names = @{}
    foreach ($d in $devices) { $names[$d.InstanceId] = $d.FriendlyName }
    $rows = @()
    if ($devices.Count) {
        $rows = @(Get-PnpDeviceProperty -InstanceId $devices.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' -ErrorAction SilentlyContinue | Where-Object {
            $_.Type -ne 'Empty' -and $null -ne $_.Data
        } | ForEach-Object { @{id=$_.InstanceId; name=$names[$_.InstanceId]; percent=$_.Data} })
    }
    ConvertTo-Json -InputObject $rows -Compress
    '''
    p = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script],
                       capture_output=True, timeout=45, creationflags=0x08000000)
    if p.returncode:
        raise OSError(p.stderr.decode('utf-8', errors='replace').strip())
    return parse_windows(json.loads(p.stdout.decode('utf-8-sig')))


class Monitor:
    def __init__(self, resource_dir):
        self.catalog = {e['pid']: e for e in json.loads(
            (Path(resource_dir) / 'razer_devices.json').read_text(encoding='utf-8'))}
        self.errors = []

    def scan(self):
        self.errors = []
        rows = []
        try:
            import hid
            rows.extend(read_hid(hid, self.catalog))
        except Exception as exc:
            self.errors.append('HID: ' + str(exc))
        try:
            rows.extend(read_windows())
        except Exception as exc:
            self.errors.append('Windows: ' + str(exc))
        return rows

    def diagnostic(self):
        rows = self.scan()
        import hid
        devices = [{k: (v.decode(errors='replace') if isinstance(v, bytes) else v)
                    for k, v in d.items() if k != 'serial_number'} for d in hid.enumerate()]
        return {'readings': [asdict(r) for r in rows], 'errors': self.errors, 'hid_devices': devices}
