# 外设电量 · Mac 预览版

版本：0.1.0-mac-preview。此版本提供 macOS 菜单栏界面，源码和跨平台逻辑测试已完成；真实外设、蓝牙权限和完整界面流程尚未实测。仓库包含 Apple 芯片与 Intel 两套 macOS 自动构建任务，只有任务成功后才会生成 `.app` 下载包，见仓库 Actions 页面。

## 在 Mac 上启动

1. 将本目录完整复制到 Mac，或解压 `Peripheral-Battery-Mac-0.1.0-preview-source.zip`。
2. 安装 Python 3.10 或更新版本。首次启动需要联网安装本目录列出的 Python 依赖，之后正常使用不需要联网。
3. 在终端输入 `bash `，把 `启动Mac版.command` 拖到终端窗口，按回车。也可以先执行 `chmod +x *.command`，再双击启动脚本。
4. 若 macOS 请求蓝牙权限，请允许读取设备状态；在系统蓝牙设置中先配对并连接自己的设备。

源码启动时需要保持终端运行。要生成能独立双击启动的应用，运行 `构建Mac应用.command`；完成后将 `dist/PeripheralBattery.app` 复制到“应用程序”。构建脚本使用本机 Python 的架构，Apple 芯片和 Intel 应分别在对应环境构建，不是通用二进制包。此脚本尚未在 Mac 上验证。

应用仅做本地临时签名，未经开发者证书签名或 Apple 公证。系统若拦截，请通过“系统设置 → 隐私与安全性”核对并允许自己构建的应用。无需关闭系统安全保护。需登录时启动的，可在系统“登录项”中添加构建后的应用。

## 显示和提醒

- 最多三个同心环，点击菜单可看全部设备。环位按首次发现的固定顺序保存，不按电量重排；可指定主设备优先占外环。断连设备会退出当前环位，剩余设备按原相对顺序补位。
- 菜单栏数字显示当前三个圆环中的最低电量。电量未知显示“系统未提供电量”，不伪造百分比。
- 系统提供左右耳和充电盒电量时分别显示；耳机圆环取主电量、左右耳的最低值，不把充电盒低电量当成耳机低电量。只提供充电盒电量时用充电盒读数。
- 默认每 60 秒读取一次，可手动刷新。读取失败立即清除旧读数，超过 90 秒未更新也会清除；唤醒或时钟变化后重新读取，并丢弃唤醒前启动的读取结果。
- 阈值可选 10%、15%、20%、30%。每轮低电量只提醒一次，记录会保存到配置文件；读数恢复至阈值以上至少 5 个百分点才重新允许提醒。充电状态尚未适配，可能在低电量充电期间提醒一次。
- 可选择自动三色、蓝色或白色圆环。通知是否展示还受 macOS 通知权限与勿扰模式影响。

## 设备范围

程序只读取 `system_profiler` 的已连接列表，并与 IOBluetooth 返回的已配对、已连接地址交叉核对。不会扫描附近 AirPods 广播，不会发起连接、配对或发送厂商 HID 命令。

macOS 没有为所有外设提供电量；仅 BLE、未被 IOBluetooth 列出的设备，以及未上报电量的型号可能不显示或只有名称。这里是对本机系统报告的读取，不保证系统底层电量缓存本身实时。Windows 版雷蛇、迈从、八位堂的 USB 协议尚未移植，QK75N 和 Redmi 音箱适配继续暂停。

数据保存在 `~/Library/Application Support/PeripheralBattery/`。普通使用不上传数据；`诊断Mac版.command` 会在本目录 `diagnostics/` 中保存设备名称、蓝牙地址和系统电量。分享前可先隐去名称与地址。

## 验证

开发机为 Windows。运行 `python -m unittest discover -s . -p 'test_*.py' -v` 可检查解析、身份过滤、断连、失效读数、固定环序和提醒去重。测试样本为人工构造，不是真实 Mac 采样。菜单栏显示、权限请求、真实电量、通知、休眠恢复及 `.app` 构建仍需 Mac 实测。

参考接口：[Apple IOBluetoothDevice](https://developer.apple.com/documentation/iobluetooth/iobluetoothdevice)、[rumps 文档](https://rumps.readthedocs.io/en/latest/)、[系统报告字段的项目示例](https://github.com/VincentShipsIt/macsweep.dev/blob/master/MacSweep/Sources/Core/Monitoring/ConnectedDevice.swift)。
