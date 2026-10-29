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
    connected: bool | None = None
    raw_value: int | None = None
    sampled_at: float | None = None
    source_note: str = ''


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


def query_serial(dev, entry):
    """Known read-only 0x00/0x82 identity command, 22 ASCII argument bytes."""
    message = bytearray(request(entry['tid'], 0x80))
    message[6:9] = bytes([0x16, 0x00, 0x82])
    message[89] = reduce(xor, message[3:89], 0)
    for _ in range(3):
        try:
            if dev.send_feature_report(bytes(message)) != 91:
                raise OSError('Incomplete serial query')
            time.sleep(entry['wait'])
            raw = bytes(dev.get_feature_report(0, 91))
            if len(raw) != 91 or raw[0] != 0 or raw[1] != 2 or raw[2] != entry['tid']:
                raise ValueError('Invalid serial response')
            if raw[6:9] != bytes([0x16, 0, 0x82]) or reduce(xor, raw[3:89], 0) != raw[89]:
                raise ValueError('Serial response belongs to another query or has invalid checksum')
            identity = raw[9:31].split(b'\0', 1)[0].decode('ascii')
            if not re.fullmatch(r'[A-Z0-9]{5,22}', identity) or set(identity) == {'0'}:
                raise ValueError('Invalid device serial')
            return identity
        except (OSError, ValueError):
            time.sleep(.04)
    return None


def apply_synapse(row, official):
    row.percent, row.charging, row.sampled_at = official
    row.connected = True
    row.error = ''
    row.raw_value = None
    row.provider = 'Razer Synapse 日志'
    row.source_note = '雷云本地日志电量；按硬件序列号匹配，软件记录不保证与界面即时同步'


def collection_key(path):
    s = path.decode(errors='replace') if isinstance(path, bytes) else str(path)
    parts = s.lower().split('#')
    if len(parts) >= 3 and '&col' in parts[1]:
        return '#'.join([parts[0], parts[1].split('&col')[0], parts[2].rsplit('&', 1)[0]])
    return s.lower()


def read_hid(hid, catalog, synapse_snapshot=None, synapse_note=''):
    groups = {}
    detected = []
    for d in hid.enumerate():
        vid, pid = d['vendor_id'], d['product_id']
        if (vid,pid,d.get('usage_page'))==(0x05AC,0x024F,0xFF13) and d.get('product_string')=='QK75N':
            detected.append(Reading('hid:'+collection_key(d['path']),'QK75N',
                '设备已识别 · 电量协议待适配',error='No verified battery query is available for this firmware',connected=True))
            continue
        entry = catalog.get(pid) if vid == 0x1532 else None
        if entry and d.get('interface_number') != entry['interface']:
            continue
        if not entry and (vid, pid, d.get('usage_page')) not in {
                (0x5253, 0x1021, 0xFF01), (0x41E4, 0x2001, 0xFF04), (0x2DC8, 0x6013, 0xFFA0), (0x2DC8, 0x310B, 0xFF7A)}:
            continue
        key = collection_key(d['path'])
        groups.setdefault((vid, pid, key), []).append(d)
    result = []
    for (vid, pid, key), candidates in groups.items():
        entry = catalog.get(pid) if vid == 0x1532 else None
        name = entry['name'] if entry else ('8BitDo Ultimate 2' if vid == 0x2DC8 else 'MCHOSE A7' if vid == 0x5253 else 'MCHOSE G87')
        row = Reading('hid:' + key, name, 'Razer HID' if entry else '8BitDo HID' if vid == 0x2DC8 else 'MCHOSE HID')
        if entry:
            from synapse_battery import match_device
            official = match_device(synapse_snapshot, candidates)
            if official is not None:
                apply_synapse(row, official)
                result.append(row)
                continue
            row.source_note = synapse_note or '雷云日志无唯一匹配、设备状态不可用或序列号缺失，使用 HID 直读'
        candidates.sort(key=lambda d: (d.get('usage_page'), d.get('usage')) != (1, 2))
        for d in candidates:
            dev = hid.device()
            try:
                dev.open_path(d['path'])
                if entry:
                    # Receiver USB serials can be all-zero placeholders. Only use
                    # verified identity queries for the listed DeathAdder receivers.
                    if synapse_snapshot is not None and pid in {0x00B3, 0x00B6, 0x00B7, 0x00C2, 0x00C3}:
                        identity = query_serial(dev, entry)
                        official = match_device(synapse_snapshot, [{'serial_number': identity}]) if identity else None
                        if official is not None:
                            apply_synapse(row, official)
                            break
                    row.raw_value = query(dev, entry, 0x80)
                    row.percent = int(row.raw_value * 100 / 255 + .5)
                    row.sampled_at = time.time()
                    if entry['charging']:
                        try:
                            row.charging = bool(query(dev, entry, 0x84))
                        except OSError:
                            row.charging = None
                elif vid == 0x2DC8:
                    # Read-only query documented for Ultimate 2 dongle 2dc8:6013.
                    # No profile/flash writes, report-mode changes or rumble commands.
                    dev.write(bytes([0x81, 0x04, 0x21, 0x01, 0, 0, 0x06] + [0] * 57))
                    response = None
                    for _ in range(3):
                        candidate = bytes(dev.read(64, 400))
                        if len(candidate) >= 24 and candidate[:6] == bytes([2, 4, 4, 0, 0x21, 1]):
                            response = candidate
                            break
                    if response is None:
                        raise OSError('Receiver did not return controller battery; wake the controller')
                    row.percent = response[18]
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
    return result + detected


def read_peripherals(hid, catalog):
    from synapse_battery import load_snapshot
    snapshot, note = load_snapshot()
    return read_hid(hid, catalog, snapshot, note)


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
            rows.extend(read_peripherals(hid, self.catalog))
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
