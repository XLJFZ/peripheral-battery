import json
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
from providers import scan


def device(mac, paired=True, connected=True):
    return SimpleNamespace(addressString=lambda: mac, isPaired=lambda: paired, isConnected=lambda: connected)


class ProviderTests(unittest.TestCase):
    def test_connection_is_rechecked_after_slow_system_report(self):
        source = Mock()
        source.pairedDevices.side_effect = [[device("AA-BB-CC-DD-EE-01")], []]
        report = {"SPBluetoothDataType": [{"device_connected": [{"鼠标": {
            "device_address": "AA:BB:CC:DD:EE:01", "device_batteryLevelMain": "80%"}}]}]}
        with patch("providers.sys.platform", "darwin"), patch.dict(sys.modules, {"IOBluetooth": SimpleNamespace(IOBluetoothDevice=source)}), patch("providers.subprocess.run", return_value=SimpleNamespace(stdout=json.dumps(report))) as run:
            self.assertEqual(scan(), [])
            self.assertEqual(run.call_args.kwargs["timeout"], 25)

    def test_timeout_propagates_instead_of_returning_cached_battery(self):
        source = Mock()
        source.pairedDevices.return_value = []
        with patch("providers.sys.platform", "darwin"), patch.dict(sys.modules, {"IOBluetooth": SimpleNamespace(IOBluetoothDevice=source)}), patch("providers.subprocess.run", side_effect=subprocess.TimeoutExpired("system_profiler", 25)):
            with self.assertRaises(subprocess.TimeoutExpired):
                scan()

    def test_live_pairing_and_connection_both_required(self):
        source = Mock()
        source.pairedDevices.return_value = [device("AA-BB-CC-DD-EE-01"),
            device("AA-BB-CC-DD-EE-02", paired=False), device("AA-BB-CC-DD-EE-03", connected=False)]
        report = {"SPBluetoothDataType": [{"device_connected": [{str(i): {
            "device_address": f"AA:BB:CC:DD:EE:0{i}", "device_batteryLevelMain": "80%"}} for i in (1, 2, 3)]}]}
        with patch("providers.sys.platform", "darwin"), patch.dict(sys.modules, {"IOBluetooth": SimpleNamespace(IOBluetoothDevice=source)}), patch("providers.subprocess.run", return_value=SimpleNamespace(stdout=json.dumps(report))):
            self.assertEqual([d.id for d in scan()], ["AABBCCDDEE01"])

    def test_windows_does_not_attempt_mac_commands(self):
        with patch("providers.sys.platform", "win32"), patch("providers.subprocess.run") as run:
            with self.assertRaises(RuntimeError):
                scan()
            run.assert_not_called()
