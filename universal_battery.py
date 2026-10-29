import argparse
import ctypes
import json
import logging
import os
import subprocess
import sys
import threading
from dataclasses import asdict
from pathlib import Path

from battery_core import Monitor

RESOURCES = Path(getattr(sys, '_MEIPASS', Path(__file__).resolve().parent))
DATA = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'UniversalBatteryTray'


def status(r):
    if r.percent is None:
        return '无法读取（休眠、断连或接口被占用）'
    return f'{r.percent}%' + (' · 充电中' if r.charging else '')


def start_command(*args):
    if getattr(sys, 'frozen', False):
        return [sys.executable, *args]
    return [sys.executable, str(Path(__file__).resolve()), *args]


def autostart(enabled=None):
    import winreg
    key = r'Software\Microsoft\Windows\CurrentVersion\Run'
    if enabled is None:
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key, 0, winreg.KEY_READ) as k:
                winreg.QueryValueEx(k, 'UniversalBatteryTray')
            return True
        except OSError:
            return False
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key) as k:
        if enabled is True:
            winreg.SetValueEx(k, 'UniversalBatteryTray', 0, winreg.REG_SZ,
                             subprocess.list2cmdline(start_command()))
        elif enabled is False:
            try:
                winreg.DeleteValue(k, 'UniversalBatteryTray')
            except FileNotFoundError:
                pass
        try:
            winreg.QueryValueEx(k, 'UniversalBatteryTray')
            return True
        except FileNotFoundError:
            return False


def details():
    import tkinter as tk
    from tkinter import ttk
    root = tk.Tk()
    root.title('外设电量 · 设备列表')
    root.geometry('830x430')
    tree = ttk.Treeview(root, columns=('device', 'battery', 'source'), show='headings')
    for col, label, width in [('device', '设备', 340), ('battery', '电量 / 状态', 300), ('source', '来源', 150)]:
        tree.heading(col, text=label)
        tree.column(col, width=width)
    tree.pack(fill='both', expand=True, padx=12, pady=12)
    message = tk.StringVar(value='正在读取…')
    ttk.Label(root, textvariable=message, wraplength=800).pack(padx=12, pady=8)
    pending = []
    def load():
        m = Monitor(RESOURCES)
        try:
            rows = m.scan()
            pending.append((rows, m.errors))
        except Exception as exc:
            pending.append(([], [str(exc)]))
    def render():
        if pending:
            rows, errors = pending.pop()
            for child in tree.get_children():
                tree.delete(child)
            for r in rows:
                tree.insert('', 'end', values=(r.name, status(r), r.provider))
            message.set('；'.join(errors) or ('未发现支持的设备。' if not rows else '灰色 / 无法读取表示本次没有有效电量。'))
        root.after(150, render)
    threading.Thread(target=load, daemon=True).start()
    render()
    root.mainloop()


class TrayApp:
    def __init__(self):
        import pystray
        from tray_graphics import make_icon
        self.ps = pystray
        self.monitor = Monitor(RESOURCES)
        self.rows = []
        self.notified = set()
        self.lock = threading.Lock()
        self.wake = threading.Event()
        self.stop = threading.Event()
        self.icon = pystray.Icon('universal_battery', make_icon(None, None), '外设电量 · 正在读取')
        self.update_menu()

    def update_menu(self):
        P = self.ps
        entries = [P.MenuItem(f'{r.name}: {status(r)}', None, enabled=False) for r in self.rows]
        if not entries:
            entries = [P.MenuItem('未发现支持的设备', None, enabled=False)]
        self.icon.menu = P.Menu(*entries, P.Menu.SEPARATOR,
            P.MenuItem('查看完整设备列表', self.show_details, default=True),
            P.MenuItem('立即刷新', lambda icon, item: self.wake.set()),
            P.MenuItem('保存诊断报告', self.diagnostic),
            P.MenuItem('开机自启', self.toggle_start, checked=lambda item: autostart()),
            P.MenuItem('退出', self.quit))

    def show_details(self, icon=None, item=None):
        # A child process owns its Tk event loop; no cross-thread Tk calls.
        subprocess.Popen(start_command('--details'), creationflags=0x08000000)

    def toggle_start(self, icon=None, item=None):
        try:
            autostart(not autostart())
            self.icon.update_menu()
        except Exception as exc:
            self.icon.notify(str(exc), '无法更改开机自启')

    def diagnostic(self, icon=None, item=None):
        def save():
            with self.lock:
                try:
                    DATA.mkdir(parents=True, exist_ok=True)
                    path = DATA / 'diagnostic.json'
                    path.write_text(json.dumps(self.monitor.diagnostic(), ensure_ascii=False, indent=2), encoding='utf-8')
                    self.icon.notify(str(path), '诊断报告已保存')
                    os.startfile(str(DATA))
                except Exception as exc:
                    logging.exception('Diagnostic failed')
                    self.icon.notify(str(exc), '诊断失败')
        threading.Thread(target=save, daemon=True).start()

    def refresh(self):
        from tray_graphics import make_icon
        with self.lock:
            self.rows = self.monitor.scan()
            for error in self.monitor.errors:
                logging.warning(error)
            valid = sorted((r for r in self.rows if r.percent is not None), key=lambda r: r.percent)
            # Rings show the two lowest batteries; menu contains every device.
            values = [r.percent for r in valid[:2]] + [None, None]
            self.icon.icon = make_icon(*values[:2])
            title = '\n'.join(f'{r.name}: {status(r)}' for r in valid[:2]) or '外设电量 · 暂无有效读数'
            self.icon.title = title[:127]
            self.update_menu()
            self.icon.update_menu()
            present = {r.key for r in self.rows}
            self.notified.intersection_update(present)
            for r in self.rows:
                if r.percent is None:
                    continue
                if r.percent > 15:
                    self.notified.discard(r.key)
                elif r.percent <= 10 and not r.charging and r.key not in self.notified:
                    self.notified.add(r.key)
                    self.icon.notify(f'{r.name} 剩余 {r.percent}%', '外设电量偏低')

    def loop(self):
        while not self.stop.is_set():
            self.wake.clear()
            try:
                self.refresh()
            except Exception:
                logging.exception('Refresh failed')
            self.wake.wait(60)

    def quit(self, icon=None, item=None):
        self.stop.set()
        self.wake.set()
        self.icon.stop()

    def run(self):
        def setup(icon):
            icon.visible = True
            threading.Thread(target=self.loop, daemon=True).start()
        self.icon.run(setup=setup)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--scan', metavar='JSON_PATH')
    parser.add_argument('--hid-scan', metavar='JSON_PATH')
    parser.add_argument('--details', action='store_true')
    parser.add_argument('--web', action='store_true')
    args = parser.parse_args()
    if args.hid_scan:
        import hid
        from battery_core import read_peripherals
        monitor = Monitor(RESOURCES)
        rows = read_peripherals(hid, monitor.catalog)
        Path(args.hid_scan).write_text(json.dumps([asdict(r) for r in rows]), encoding='utf-8')
        return
    if args.web:
        from web_bridge import serve
        serve(RESOURCES)
        return
    if args.scan:
        Path(args.scan).write_text(json.dumps(Monitor(RESOURCES).diagnostic(), ensure_ascii=False, indent=2), encoding='utf-8')
        return
    if args.details:
        details()
        return
    from ctypes import wintypes
    k32 = ctypes.WinDLL('kernel32', use_last_error=True)
    k32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    k32.CreateMutexW.restype = wintypes.HANDLE
    handle = k32.CreateMutexW(None, False, 'Local\\UniversalBatteryTray')
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    if ctypes.get_last_error() == 183:
        return
    DATA.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(filename=DATA / 'battery.log', level=logging.INFO, encoding='utf-8')
    TrayApp().run()


if __name__ == '__main__':
    main()
