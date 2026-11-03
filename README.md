# Peripheral Battery · 外设电量

当前 Windows 版本 **v0.3.5**，提供“普通版”（默认）和“液态玻璃”两种外观，液态玻璃跟随 Windows 深色/浅色设置。两种交付形式：

- **单文件便携版**：只下载一个 `Peripheral-Battery-0.3.5-x64.exe`，双击即用，无需安装运行时、无需解压。不含 Python HID 后端，其他品牌 HID 一列会提示后端缺失。
- **目录版 ZIP**：解压后双击 `启动托盘版.cmd`，保留全部 DLL 与 `backend` 文件夹，功能完整。

面板大小可在设置里选择紧凑 / 标准 / 宽松。详情见 [发布说明](notes/v0.3.5.md)。

- [下载 Windows 版本](https://github.com/XLJFZ/peripheral-battery/releases/latest)
- [在线网页面板](https://xljfz.github.io/peripheral-battery/)
- [使用说明](使用说明.md)
- [Mac 源码预览版](macos/README.md)

## 功能与兼容范围

原生 Windows 托盘聚合外设电量，支持设备拖拽排序、多环图标、颜色设置、低电量提醒和两种界面外观。设置和关于窗口均提供 GitHub 仓库按钮。

雷蛇使用 C# 原生 HID 读取，本机已验证炼狱蝰蛇 V3 Pro 无线接收器 `1532:00B7`。设备表中其他型号未逐款实测。罗技、迈从、八位堂和 Windows 系统电量继续使用已有读取方式，覆盖范围取决于具体型号、固件和连接方式。QK75N 可识别，USB 电量协议尚未适配。

游戏回报率调度默认关闭，目前仅开放已验证的 `1532:00B7` 接收器及 125–1000Hz，带回读校验和原值恢复记录。4000/8000Hz 尚未开放，真实游戏自动触发仍待实测。网页面板只读取电量，不提供硬件设置功能。

关闭 AirPods / Beats Beacon 和 Google Fast Pair 附近广播发现，避免把陌生耳机加入列表。保留 Windows 已连接设备和其他电量来源。

## 开发

Windows 构建需要 .NET 8 SDK 和 Python 3.13。`build-native.ps1` 构建目录版（原生托盘 + Python 打包后端），`build-portable.ps1` 构建单文件便携 EXE，并用 `tools/check_portable_exe.py`（空目录冒烟）与 `tools/check_flyout_layout.py`（面板高度贴合内容）验证。`build.ps1` 可单独构建 Python 后端和本地网页连接器。

```powershell
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -r requirements.txt
./.venv/Scripts/python.exe -m unittest discover -p 'test_*.py'
```

GitHub Pages 从 `main` 分支的 `/docs` 发布。网页原稿为 `web/index.html`，修改后执行 `python sync_pages.py`。在线网页使用桌面 Chrome / Edge 的 WebHID 授权；浏览器屏蔽鼠标接口时可使用发布包中的本地连接器。

设备数据在本机读取，本地连接器只监听回环地址，并使用每次启动生成的访问令牌。

## 来源与许可

Python 版本基于 [marsuyone/mchose-battery-tray](https://github.com/marsuyone/mchose-battery-tray) 扩展，上游原始代码保存在 `upstream/`，原始提交为 `f8e0e06b9934b8beb48c6d5d884e3330f1188aea`。上游未声明许可证，本仓库不为其添加许可证；公开可见不代表获得额外再许可权。

原生托盘基于 [BluetoothBatteryMonitor](https://github.com/Kaandonmez/BluetoothBatteryMonitor) 改造，保留 `native/LICENSE` 中的 MIT 许可与署名。雷蛇报文与设备表参考 [razer-battery-report](https://github.com/xzeldon/razer-battery-report/tree/master/crates/razer-hid)，回报率报文对照 [OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c) 并在本机验证。本项目没有复制 FerrisPulse 的 GPLv3 源码。