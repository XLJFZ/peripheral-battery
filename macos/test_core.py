import json
from pathlib import Path
import io
import unittest
from unittest.mock import patch

from core import Device, Snapshot, needs_alert, ordered, parse_profiler, percent, rearm_alert

A = "AA:BB:CC:DD:EE:01"
B = "AA:BB:CC:DD:EE:02"


def payload(connected=None, disconnected=None):
    return {"SPBluetoothDataType": [{"device_connected": connected or [],
                                     "device_not_connected": disconnected or []}]}


def row(mac=A, name="耳机", **parts):
    return {name: {"device_address": mac, **{"device_batteryLevel" + k: v for k, v in parts.items()}}}


class CoreTests(unittest.TestCase):
    def test_disconnected_and_unpaired_devices_never_appear(self):
        report = payload([row(A, Main="10%"), row(B, Main="20%")], [row(name="陌生AirPods", Main="1%")])
        devices = parse_profiler(report, [A])
        self.assertEqual([d.id for d in devices], ["AABBCCDDEE01"])

    def test_empty_pairing_list_is_fail_closed(self):
        self.assertEqual(parse_profiler(payload([row(Main="80%")]), []), [])

    def test_same_name_does_not_merge_different_addresses(self):
        devices = parse_profiler(payload([row(A, Main="80%"), row(B, Main="10%")]), [A, B])
        self.assertEqual(len(devices), 2)
        self.assertEqual([d.level for d in devices], [80, 10])

    def test_missing_address_cannot_be_replaced_by_name(self):
        self.assertEqual(parse_profiler(payload([{"耳机": {"device_batteryLevelMain": "20%"}}]), [A]), [])

    def test_parts_and_case_are_independent(self):
        device = parse_profiler(payload([row(Left="80%", Right="60 %", Case="5%")]), [A])[0]
        self.assertEqual(device.level, 60)
        self.assertEqual(device.parts["充电盒"], 5)

    def test_unknown_is_not_zero(self):
        device = parse_profiler(payload([row(Main="Unknown")]), [A])[0]
        self.assertIsNone(device.level)
        self.assertEqual(device.description, "系统未提供电量")

    def test_invalid_percent_rejected(self):
        for value in (True, False, None, -1, 101, "N/A", "0x10", 1.5):
            self.assertIsNone(percent(value))
        self.assertEqual(percent("0 %"), 0)
        self.assertEqual(percent(100), 100)

    def test_malformed_report_is_failure_not_empty_success(self):
        for report in ({}, [], {"SPBluetoothDataType": None}, {"SPBluetoothDataType": []},
                       {"SPBluetoothDataType": [{"device_connected": "bad"}]}):
            with self.assertRaises(ValueError):
                parse_profiler(report, [A])

    def test_disconnect_success_removes_previous_readings(self):
        snapshot = Snapshot()
        snapshot.success([Device(A, "鼠标", {"电量": 80})], 10)
        snapshot.success([], 20)
        self.assertEqual(snapshot.devices, [])

    def test_error_clears_old_readings_and_timestamp(self):
        snapshot = Snapshot()
        snapshot.success([Device(A, "鼠标", {"电量": 80})], 10)
        snapshot.invalidate("读取失败")
        self.assertEqual(snapshot.devices, [])
        self.assertIsNone(snapshot.updated)

    def test_stale_data_expires(self):
        snapshot = Snapshot()
        snapshot.success([Device(A, "鼠标", {"电量": 80})], 10)
        self.assertFalse(snapshot.expire(100, 90))
        self.assertTrue(snapshot.expire(101, 90))
        self.assertEqual(snapshot.devices, [])

    def test_ring_order_does_not_follow_battery(self):
        first = Device(A, "鼠标", {"电量": 80})
        second = Device(B, "键盘", {"电量": 5})
        self.assertEqual(ordered([second, first], [A, B], ""), [first, second])
        self.assertEqual(ordered([first, second], [A, B], B), [second, first])

    def test_low_alert_hysteresis_and_persisted_marker(self):
        device = Device(A, "鼠标", {"电量": 19})
        alerted = set()
        self.assertTrue(needs_alert(device, 20, alerted))
        alerted.add(A)
        restored = set(json.loads(json.dumps(list(alerted))))
        self.assertFalse(needs_alert(device, 20, restored))
        rearm_alert(Device(A, "鼠标", {"电量": 21}), 20, restored)
        self.assertFalse(needs_alert(device, 20, restored))
        rearm_alert(Device(A, "鼠标", {"电量": 25}), 20, restored)
        self.assertTrue(needs_alert(device, 20, restored))

    def test_unknown_never_triggers_alert(self):
        self.assertFalse(needs_alert(Device(A, "鼠标", {}), 20, set()))

    def test_graphics_transparent_and_three_colors(self):
        from graphics import render
        from PIL import Image
        with io.BytesIO() as output:
            render([Device(str(i), "设备", {"电量": n}) for i, n in enumerate((80, 20, 5))],
                   ("#00C98D", "#FFC048", "#FF6577"), output)
            output.seek(0)
            with Image.open(output) as image:
                self.assertEqual(image.size, (72, 72))
                self.assertEqual(image.getpixel((0, 0))[3], 0)
                pixels = [image.getpixel((x, y)) for x in range(72) for y in range(72)]
                for expected in ((0, 201, 141), (255, 192, 72), (255, 101, 119)):
                    self.assertTrue(any(max(abs(p[i] - expected[i]) for i in range(3)) < 35 and p[3] > 100 for p in pixels))


if __name__ == "__main__":
    unittest.main()
