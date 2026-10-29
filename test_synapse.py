from datetime import datetime
import io
import json
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from pathlib import Path

from synapse_battery import Snapshot, load_snapshot, match_device, parse_snapshot
from battery_core import read_hid, query_serial
from test_battery import Bus

NOW = datetime(2026, 10, 30, 3, 10, 0).timestamp()
SERIAL = 'TESTMOUSE123'


def device(level=89, state='NoCharge_BatteryFull', identity=SERIAL):
    return {'serialNumber': identity, 'hasBattery': True, 'powerStatus': {'level': level, 'chargingStatus': state}}


def line(devices, age=0):
    date = datetime.fromtimestamp(NOW-age).strftime('%Y/%m/%d %H:%M:%S.%f')[:-3]
    return f'[{date}] devices: {json.dumps(devices)}'


class Log:
    def __init__(self, text, modified=0):
        self.data = text.encode()
        self.modified = modified
    def stat(self): return SimpleNamespace(st_size=len(self.data), st_mtime=self.modified)
    def open(self, mode): return io.BytesIO(self.data)


class SynapseTests(unittest.TestCase):
    def candidates(self, identity=SERIAL): return [{'serial_number': identity}]

    def test_parse_and_exact_serial_match(self):
        snapshot = parse_snapshot(line([device()]))
        self.assertEqual(match_device(snapshot, self.candidates()), (89, False, NOW))
        self.assertIsNone(match_device(snapshot, self.candidates('DIFFERENT123')))

    def test_missing_serial_cannot_fall_back_to_name_or_pid(self):
        snapshot = parse_snapshot(line([device()]))
        self.assertIsNone(match_device(snapshot, [{'product_string': 'Razer Mouse', 'product_id': 183}]))
        self.assertIsNone(match_device(snapshot, self.candidates('000000')))

    def test_conflicting_hardware_serials_rejected(self):
        self.assertIsNone(match_device(Snapshot(NOW, [device()]), self.candidates()+self.candidates('OTHER123')))

    def test_duplicate_log_serial_is_ambiguous(self):
        self.assertIsNone(match_device(Snapshot(NOW, [device(89), device(20)]), self.candidates()))

    def test_off_or_unknown_status_is_not_live_battery(self):
        for status in ('off', 'unknown', None):
            self.assertIsNone(match_device(Snapshot(NOW, [device(state=status)]), self.candidates()))

    def test_invalid_percent_not_accepted(self):
        for level in (None, True, -1, 101, '89', 89.2):
            self.assertIsNone(match_device(Snapshot(NOW, [device(level)]), self.candidates()))
        self.assertEqual(match_device(Snapshot(NOW, [device(0, 'Charging')]), self.candidates())[:2], (0, True))

    def test_stopped_process_prevents_log_use(self):
        with patch.object(Path, 'glob') as glob:
            self.assertIsNone(load_snapshot(now=NOW, is_running=False)[0])
            glob.assert_not_called()

    def test_stale_and_future_records_are_rejected(self):
        for age in (121, -6):
            with patch.object(Path, 'glob', return_value=[Log(line([device()], age))]):
                snapshot, note = load_snapshot(now=NOW, is_running=True)
                self.assertIsNone(snapshot)
                self.assertIn('时间', note)

    def test_latest_empty_snapshot_does_not_resurrect_old_device(self):
        with patch.object(Path, 'glob', return_value=[Log(line([device()], 20), 2), Log(line([], 0), 1)]):
            snapshot, _ = load_snapshot(now=NOW, is_running=True)
            self.assertEqual(snapshot.devices, [])
            self.assertIsNone(match_device(snapshot, self.candidates()))

    def test_latest_off_snapshot_does_not_resurrect_previous_active_state(self):
        with patch.object(Path, 'glob', return_value=[Log(line([device()], 20)), Log(line([device(state='off')]))]):
            snapshot, _ = load_snapshot(now=NOW, is_running=True)
            self.assertIsNone(match_device(snapshot, self.candidates()))

    def test_partial_tail_uses_last_valid_fresh_snapshot(self):
        text = line([device()], 10) + '\n[2026/10/30 03:10:00.000] devices: [{'
        with patch.object(Path, 'glob', return_value=[Log(text)]):
            snapshot, _ = load_snapshot(now=NOW, is_running=True)
            self.assertEqual(snapshot.sampled_at, NOW-10)

    def test_malformed_timestamp_or_json_is_ignored(self):
        for value in ('garbage', '[bad] devices: []', '[2026/99/30 03:10:00.000] devices: []', line({})):
            self.assertIsNone(parse_snapshot(value))

    def test_valid_log_prevents_redundant_hid_query(self):
        bus = Bus()
        original = bus.enumerate
        bus.enumerate = lambda: [dict(row, serial_number=SERIAL) for row in original()]
        catalog = {e['pid']: e for e in json.loads(Path(__file__).with_name('razer_devices.json').read_text())}
        readings = read_hid(bus, catalog, Snapshot(NOW, [device()]))
        self.assertEqual(readings[0].percent, 89)
        self.assertIn('Synapse', readings[0].provider)
        self.assertEqual(readings[0].sampled_at, NOW)
        self.assertEqual(bus.sent, [])

    @patch('battery_core.time.sleep')
    def test_bad_log_falls_back_to_hid(self, _):
        bus = Bus()
        catalog = {e['pid']: e for e in json.loads(Path(__file__).with_name('razer_devices.json').read_text())}
        readings = read_hid(bus, catalog, Snapshot(NOW, [device()]))
        self.assertEqual(readings[0].percent, 50)
        self.assertEqual(readings[0].raw_value, 128)
        self.assertTrue(bus.sent)
        self.assertIn('HID', readings[0].source_note)

    @patch('battery_core.time.sleep')
    def test_zero_usb_serial_can_use_verified_identity_query(self, _):
        from test_battery import FakeDevice, response
        from functools import reduce
        from operator import xor
        bus = Bus()
        class IdentityDevice(FakeDevice):
            def send_feature_report(self, report):
                self.report = report
                return super().send_feature_report(report)
            def get_feature_report(self, rid, size):
                if self.command != 0x82:
                    return super().get_feature_report(rid, size)
                self.assertion = self.report[6:9] == bytes([0x16, 0, 0x82])
                raw = bytearray(self.report)
                raw[1] = 2
                raw[9:9+len(SERIAL)] = SERIAL.encode()
                raw[89] = reduce(xor, raw[3:89], 0)
                return raw
        bus.device = lambda: IdentityDevice(bus)
        original = bus.enumerate
        bus.enumerate = lambda: [dict(row, serial_number='000000000000') for row in original()]
        catalog = {e['pid']: e for e in json.loads(Path(__file__).with_name('razer_devices.json').read_text())}
        readings = read_hid(bus, catalog, Snapshot(NOW, [device()]))
        self.assertEqual(readings[0].percent, 89)
        self.assertIn('Synapse', readings[0].provider)
        self.assertEqual([report[8] for report in bus.sent], [0x82])
        self.assertNotIn(SERIAL, json.dumps(readings[0].__dict__))


if __name__ == '__main__': unittest.main()
