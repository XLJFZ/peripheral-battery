# Peripheral Battery · 外设电量

Mac 菜单栏预览版位于 [`macos/`](macos/README.md)，提供系统已配对且已连接设备的电量读取、多环、固定顺序和低电量提醒。当前交付源码与 Mac 构建脚本，尚未在真实 Mac 上验证；USB 厂商协议尚未移植。

当前 Windows 托盘版为 v0.2.3：关闭 AirPods / Beats Beacon 和 Google Fast Pair 的附近广播发现，避免陌生耳机混入设备列表。Windows 已连接设备与其他电量读取方式保留；部分耳机的左右耳、充电盒独立电量暂时不再显示。本轮增加 HID 失败时清除旧读数、原始值与采样时间提示，详情见 [v0.2.3 验证记录](notes/v0.2.3.md)。

Windows 托盘电量工具与 WebHID 网页面板，基于 [marsuyone/mchose-battery-tray](https://github.com/marsuyone/mchose-battery-tray) 扩展。

v0.2.0 的托盘版直接基于 [BluetoothBatteryMonitor](https://github.com/Kaandonmez/BluetoothBatteryMonitor) 的原生 Fluent UI 和蓝牙模块改造，保留 MIT 许可与署名。新增加粗圆环、可视化调色、自定义三档电量颜色，以及 10 秒无操作自动隐藏。详情见 [新版托盘说明](新版托盘说明.md)。

- [在线网页版](https://xljfz.github.io/peripheral-battery/)
- [下载 Windows 双版本包](https://github.com/XLJFZ/peripheral-battery/releases/latest)
- [使用说明](使用说明.md)
- [验证记录](验证记录.md)

## 使用

下载 Release 中的 ZIP 并完整解压，无需安装 Python。

托盘版双击 `启动托盘版.cmd`。网页版双击 `启动网页版.cmd`，自动打开本地浏览器面板。在线网页使用桌面版 Chrome / Edge 打开，点击“连接设备”授权 WebHID；浏览器可能屏蔽鼠标接口，此时使用下载包内的本地连接器。

## 兼容范围

已在炼狱蝰蛇 V3 Pro 无线接收器 `1532:00B7` 上实测电量与充电状态。设备表还包含其他雷蛇鼠标，保留迈从 A7、G87 协议，并支持读取 Windows 提供电量的部分外设；这些型号未逐款实测。

v0.2.0 已新增八位堂 Ultimate 2 的接收器及工作模式接口查询。QK75N 当前可识别，但其电量协议尚待适配。“泛用”指支持多个设备和扩展协议，不表示所有无线外设即插即用。网页功能用于读取电量，不包含 DPI、按键映射或固件更新。

## 开发

Python 3.13：

```powershell
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -r requirements.txt
./.venv/Scripts/python.exe -m unittest discover -p 'test_*.py'
./.venv/Scripts/python.exe universal_battery.py
```

执行 `build-native.ps1` 构建新版原生托盘及 HID 后端，需要 .NET 8 SDK 与 Python 3.13。`build.ps1` 仍可单独构建旧 Python 后端与本地网页。GitHub Pages 从 `main` 分支的 `/docs` 发布；网页原稿为 `web/index.html`，修改网页后执行 `python sync_pages.py` 同步发布目录。

安装 Node.js 后，可执行 `node test_webhid.cjs` 验证实际网页中的 WebHID 报文处理。测试覆盖带编号和不带编号的回包、偏移、校验、受限接口与访问拒绝。

设备数据在本机读取，网页没有分析脚本或外部数据服务。本地连接器只监听回环地址，并使用每次启动生成的访问令牌。

## 来源

上游原始代码位于 `upstream/mchose_battery.py`，原始提交为 `f8e0e06b9934b8beb48c6d5d884e3330f1188aea`。双环绘图来自上游。雷蛇报文布局及设备参数参考 [razer-battery-report](https://github.com/xzeldon/razer-battery-report/tree/master/crates/razer-hid)。

上游未声明许可证，本仓库不为上游代码添加许可证；公开可见不代表获得了额外的再许可权。

## v0.2.1 多设备托盘

圆环样式会自动为已连接且有电量的设备显示同心环，每个环独立显示进度和颜色。由外向内按电量从低到高排列，悬停可查看对应设备。最多显示三个环；更多设备请点开窗口查看。数字样式继续显示最低电量。
