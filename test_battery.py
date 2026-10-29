import json
import unittest
from pathlib import Path
from unittest.mock import patch
from battery_core import request, decode, read_hid, parse_windows, collection_key
from functools import reduce
from operator import xor


def response(value=128, status=2, command=0x80):
    p = bytearray(request(0x1f, command))
    p[1] = status
    p[10] = value
    p[89] = reduce(xor, p[3:89], 0)
    return bytes(p)


class FakeDevice:
    def __init__(self, bus): self.bus = bus
    def open_path(self, path): self.path = path
    def send_feature_report(self, report):
        self.bus.sent.append(report)
        self.command = report[8]
        return len(report)
    def get_feature_report(self, rid, size):
        if self.bus.fail: raise OSError('disconnected')
        return response(128 if self.command == 0x80 else 1, command=self.command)
    def close(self): self.bus.closed += 1


class Bus:
    def __init__(self):
        self.sent = []
        self.closed = 0
        self.fail = False
    def enumerate(self):
        return [dict(vendor_id=0x1532, product_id=0xb7, interface_number=0,
                     usage_page=1, usage=2, path=b'mouse'),
                dict(vendor_id=0x1234, product_id=1, interface_number=0,
                     usage_page=1, usage=2, path=b'unknown')]
    def device(self): return FakeDevice(self)


class Tests(unittest.TestCase):
    def setUp(self):
        self.catalog = {e['pid']: e for e in json.loads(Path(__file__).with_name('razer_devices.json').read_text())}
    def test_request(self):
        p = request(0x1f, 0x80)
        self.assertEqual(len(p), 91)
        self.assertEqual(p[:9], bytes([0,0,0x1f,0,0,0,2,7,0x80]))
        self.assertEqual(p[89], 0x85)
    def test_battery_endpoints(self):
        self.assertEqual(decode(response(0), 0x1f, 0x80), 0)
        self.assertEqual(decode(response(255), 0x1f, 0x80), 255)
    def test_checksum(self):
        p = bytearray(response()); p[10] ^= 1
        with self.assertRaises(ValueError): decode(p, 0x1f, 0x80)
    def test_length(self):
        with self.assertRaises(ValueError): decode(response()[:-1], 0x1f, 0x80)
    def test_status(self):
        for status in [0,1,3,4,5]:
            with self.assertRaises(ValueError): decode(response(status=status), 0x1f, 0x80)
    def test_wrong_command(self):
        with self.assertRaises(ValueError): decode(response(command=0x84), 0x1f, 0x80)
    def test_wrong_transaction(self):
        with self.assertRaises(ValueError): decode(response(), 0x3f, 0x80)
    @patch('battery_core.time.sleep')
    def test_hid_and_disconnect(self, sleep):
        bus = Bus()
        rows = read_hid(bus, self.catalog)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0].percent, 50)
        self.assertTrue(rows[0].charging)
        bus.fail = True
        rows = read_hid(bus, self.catalog)
        self.assertIsNone(rows[0].percent)
        self.assertTrue(rows[0].error)
        self.assertEqual(bus.closed, 2)
    def test_windows_values(self):
        rows = parse_windows([{'id':'a','percent':0}, {'id':'b','percent':100},
                             {'id':'c','percent':255}, {'id':'d','percent':None}])
        self.assertEqual([r.percent for r in rows], [0,100])
    def test_grouping(self):
        a = b'hid#vid_1532&pid_00b7&mi_00&col01#8&aaa&0&0000#{guid}'
        b = b'hid#vid_1532&pid_00b7&mi_00&col02#8&aaa&0&0001#{guid}\\kbd'
        self.assertEqual(collection_key(a), collection_key(b))
    def test_deathadder_variants(self):
        for pid in (0xb6,0xb7,0xc2,0xc3,0xb3):
            self.assertEqual(self.catalog[pid]['tid'], 0x1f)

    def test_8bitdo_read_only_query_and_reply(self):
        class Pad:
            def open_path(self,path): pass
            def write(self,data):
                assert len(data)==64 and data[:7]==bytes([0x81,4,0x21,1,0,0,6])
            def read(self,size,timeout):
                data=bytearray(64);data[:6]=bytes([2,4,4,0,0x21,1]);data[18]=76;return data
            def close(self): pass
        class PadBus:
            def enumerate(self):
                return [dict(vendor_id=0x2dc8,product_id=0x310b,usage_page=0xff7a,path=b'pad')]
            def device(self): return Pad()
        rows=read_hid(PadBus(),self.catalog)
        self.assertEqual(rows[0].percent,76)
        self.assertEqual(rows[0].provider,'8BitDo HID')


if __name__ == '__main__': unittest.main()
