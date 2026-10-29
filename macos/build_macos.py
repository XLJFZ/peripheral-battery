"""Run on each target Mac architecture to produce a standalone .app bundle."""
import pathlib
import platform
import plistlib
import subprocess
import sys

if sys.platform != "darwin":
    raise SystemExit("请在 Mac 上构建；Windows 无法生成已验证的 macOS 应用。")
root = pathlib.Path(__file__).resolve().parent
subprocess.run([
    sys.executable, "-m", "PyInstaller", "--noconfirm", "--windowed", "--onedir",
    "--name", "PeripheralBattery", "--osx-bundle-identifier", "local.peripheralbattery.mac",
    "--hidden-import", "IOBluetooth", "--collect-all", "rumps",
    "--distpath", str(root / "dist"), "--workpath", str(root / "build"),
    "--specpath", str(root / "build"), str(root / "menu_app.py"),
], cwd=root, check=True)
bundle = root / "dist" / "PeripheralBattery.app"
plist = bundle / "Contents" / "Info.plist"
info = plistlib.loads(plist.read_bytes())
info.update({
    "LSUIElement": True,
    "CFBundleDisplayName": "外设电量",
    "CFBundleShortVersionString": "0.1.0",
    "CFBundleVersion": "1",
    "NSBluetoothAlwaysUsageDescription": "仅读取本机已配对且已连接设备的状态和系统提供的电量。",
    "NSBluetoothPeripheralUsageDescription": "仅读取已配对设备状态，不扫描附近广播。",
})
plist.write_bytes(plistlib.dumps(info))
# Editing Info.plist invalidates PyInstaller's existing ad-hoc signature.
subprocess.run(["/usr/bin/codesign", "--force", "--deep", "--sign", "-", str(bundle)], check=True)
subprocess.run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
print(f"已构建 {platform.machine()} 应用：{bundle}")
