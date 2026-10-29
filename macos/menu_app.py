"""Peripheral Battery macOS menu bar app; workers never touch AppKit UI."""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time

from core import Snapshot, needs_alert, ordered, rearm_alert
from providers import scan

VERSION = "0.1.0-mac-preview"
DATA = Path.home() / "Library" / "Application Support" / "PeripheralBattery"


def run_app():
    import fcntl
    import rumps
    from graphics import render
    DATA.mkdir(parents=True, exist_ok=True)
    lock = (DATA / "instance.lock").open("w")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        print("外设电量已在运行")
        return

    class BatteryApp(rumps.App):
        def __init__(self):
            super().__init__("外设电量", title="—", quit_button=None)
            self.snapshot = Snapshot()
            self.config = {"order": [], "primary": "", "threshold": 20,
                           "notifications": True, "alerted": [], "palette": "自动三色"}
            try:
                saved = json.loads((DATA / "settings.json").read_text())
                for field in self.config:
                    if field in saved and type(saved[field]) is type(self.config[field]):
                        self.config[field] = saved[field]
            except (OSError, ValueError, TypeError):
                pass
            self.config["order"] = [x for x in self.config["order"] if isinstance(x, str)]
            self.config["alerted"] = [x for x in self.config["alerted"] if isinstance(x, str)]
            if self.config["threshold"] not in (10, 15, 20, 30):
                self.config["threshold"] = 20
            self.alerted = set(self.config["alerted"])
            self.results = queue.Queue()
            self.busy = False
            self.next_scan = 0
            self.last_tick = time.time()
            self.invalid_before = 0
            self.tick_timer = rumps.Timer(self.tick, 1)
            self.redraw()
            self.tick_timer.start()

        def save(self):
            self.config["alerted"] = sorted(self.alerted)
            temporary = DATA / "settings.tmp"
            try:
                temporary.write_text(json.dumps(self.config, ensure_ascii=False, indent=2), encoding="utf-8")
                temporary.replace(DATA / "settings.json")
            except OSError:
                self.notice = "设置未能保存，请检查用户目录权限"

        def refresh(self, _=None):
            if self.busy:
                return
            self.busy = True
            started = time.time()
            def worker():
                try:
                    self.results.put((started, scan(), ""))
                except Exception as error:
                    self.results.put((started, [], f"读取失败（{type(error).__name__}），旧读数已清除"))
            threading.Thread(target=worker, daemon=True).start()

        def tick(self, _):
            now = time.time()
            if now - self.last_tick > 15 or now < self.last_tick:
                self.snapshot.invalidate("系统唤醒，等待重新读取")
                self.invalid_before = now
                self.next_scan = 0
                self.redraw()
            self.last_tick = now
            if self.snapshot.expire(now, 90):
                self.redraw()
            try:
                started, devices, error = self.results.get_nowait()
            except queue.Empty:
                pass
            else:
                self.busy = False
                if started < self.invalid_before or now - started > 35:
                    self.next_scan = 0
                    self.refresh()
                    return
                self.next_scan = now + 60
                if error:
                    self.snapshot.invalidate(error)
                else:
                    self.snapshot.success(devices, now)
                    for device in sorted(devices, key=lambda d: d.id):
                        if device.id not in self.config["order"]:
                            self.config["order"].append(device.id)
                        rearm_alert(device, self.config["threshold"], self.alerted)
                        if self.config["notifications"] and needs_alert(device, self.config["threshold"], self.alerted):
                            try:
                                rumps.notification("外设电量偏低", device.name, device.description)
                            except Exception:
                                self.notice = "提醒发送失败，可在系统设置中检查通知权限"
                            else:
                                self.alerted.add(device.id)
                    self.save()
                self.redraw()
            if not self.busy and now >= self.next_scan:
                self.refresh()

        def redraw(self):
            self.menu.clear()
            self.menu.add(rumps.MenuItem(f"外设电量 · {VERSION}"))
            devices = ordered(self.snapshot.devices, self.config["order"], self.config["primary"])
            rings = [d for d in devices if d.level is not None][:3]
            self.title = f"{min(d.level for d in rings)}%" if rings else "—"
            palette = {"自动三色": ("#00C98D", "#FFC048", "#FF6577"),
                       "蓝色": ("#61B8FF",) * 3, "白色": ("#FFFFFF",) * 3}
            icon = DATA / "menu-icon.png"
            render(rings, palette.get(self.config["palette"], palette["自动三色"]), icon)
            self.icon, self.template = str(icon), False
            if self.snapshot.error:
                self.menu.add(rumps.MenuItem(self.snapshot.error))
            elif not devices:
                self.menu.add(rumps.MenuItem("暂无系统已连接且已配对的设备"))
            for device in devices:
                index = next((i for i, d in enumerate(rings) if d.id == device.id), None)
                prefix = f"环{index + 1} " if index is not None else ""
                self.menu.add(rumps.MenuItem(f"{prefix}{device.name} [{device.id[-4:]}] · {device.description}"))
            if self.snapshot.updated is not None:
                stamp = time.strftime("%H:%M:%S", time.localtime(self.snapshot.updated))
                self.menu.add(rumps.MenuItem(f"系统报告读取于 {stamp} · 每60秒刷新"))
            if getattr(self, "notice", ""):
                self.menu.add(rumps.MenuItem(self.notice))
            self.menu.add(None)
            self.menu.add(rumps.MenuItem("立即刷新", callback=self.refresh))
            main = rumps.MenuItem("主设备（外环优先）")
            reset = rumps.MenuItem("无（按固定顺序）", callback=lambda _: self.set_primary(""))
            reset.state = not self.config["primary"]
            main.add(reset)
            for device in devices:
                item = rumps.MenuItem(f"{device.name} [{device.id[-4:]}]",
                                      callback=lambda _, key=device.id: self.set_primary(key))
                item.state = device.id == self.config["primary"]
                main.add(item)
            self.menu.add(main)
            thresholds = rumps.MenuItem("低电量阈值")
            for value in (10, 15, 20, 30):
                item = rumps.MenuItem(f"{value}%", callback=lambda _, v=value: self.set_threshold(v))
                item.state = value == self.config["threshold"]
                thresholds.add(item)
            self.menu.add(thresholds)
            notifications = rumps.MenuItem("低电量提醒", callback=self.toggle_notifications)
            notifications.state = self.config["notifications"]
            self.menu.add(notifications)
            colors = rumps.MenuItem("圆环颜色")
            for name in palette:
                item = rumps.MenuItem(name, callback=lambda _, n=name: self.set_palette(n))
                item.state = name == self.config["palette"]
                colors.add(item)
            self.menu.add(colors)
            self.menu.add(rumps.MenuItem("打开蓝牙设置", callback=lambda _: subprocess.Popen(
                ["/usr/bin/open", "x-apple.systempreferences:com.apple.BluetoothSettings"])))
            self.menu.add(rumps.MenuItem("打开配置文件夹", callback=lambda _: subprocess.Popen(["/usr/bin/open", str(DATA)])))
            self.menu.add(None)
            self.menu.add(rumps.MenuItem("退出", callback=lambda _: rumps.quit_application()))

        def set_primary(self, key):
            self.config["primary"] = key
            self.save()
            self.redraw()

        def set_threshold(self, value):
            self.config["threshold"] = value
            self.save()
            self.redraw()
            self.refresh()

        def set_palette(self, value):
            self.config["palette"] = value
            self.save()
            self.redraw()

        def toggle_notifications(self, _):
            self.config["notifications"] = not self.config["notifications"]
            self.save()
            self.redraw()

    app = BatteryApp()
    app.run()
    lock.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--diagnose", action="store_true")
    parser.add_argument("--self-check", action="store_true", help="检查原生依赖，不读取设备或打开界面")
    arguments = parser.parse_args()
    if sys.platform != "darwin":
        parser.exit(1, "此版本需要 macOS；Windows 请使用根目录的启动托盘版.cmd。\n")
    if arguments.self_check:
        import rumps
        import IOBluetooth
        from PIL import Image
        from AppKit import NSApplication
        print("macOS native dependencies loaded successfully")
    elif arguments.diagnose:
        from dataclasses import asdict
        try:
            print(json.dumps([asdict(device) for device in scan()], ensure_ascii=False, indent=2))
        except Exception as error:
            parser.exit(1, f"读取失败：{error}\n")
    else:
        run_app()
