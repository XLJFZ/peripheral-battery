# 外设电量 · Mac 预览版

版本：0.1.0-mac-preview。Apple 芯片与 Intel 两套应用均已在 GitHub 托管的 macOS 15 环境通过构建、签名校验和原生依赖导入检查，19 项逻辑测试在两种架构上均通过。真实外设、蓝牙权限和完整界面流程尚未实测，仍作为预览版交付。

## 直接使用应用

从 [v0.2.3 下载页](https://github.com/XLJFZ/peripheral-battery/releases/tag/v0.2.3) 下载：Apple 芯片选 `preview-arm64.zip`，Intel 选 `preview-x86_64.zip`。解压后将 `PeripheralBattery.app` 放入“应用程序”并打开，不需要另装 Python。系统拦截时，在“系统设置 → 隐私与安全性”核对来源后允许打开；该预览版使用临时签名，未经 Apple 开发者签名或公证。

## 源码启动与重新构建

1. 将本目录完整复制到 Mac，或解压 `Peripheral-Battery-Mac-0.1.0-preview-source.zip`。
2. 安装 Python 3.10 或更新版本。首次启动需要联网安装本目录列出的 Python 依赖，之后正常使用不需要联网。
3. 在终端输入 `bash `，把 `启动Mac版.command` 拖到终端窗口，按回车。也可以先执行 `chmod +x *.command`，再双击启动脚本。
4. 若 macOS 请求蓝牙权限，请允许读取设备状态；在系统蓝牙设置中先配对并连接自己的设备。

源码启动时需要保持终端运行。要生成能独立双击启动的应用，运行 `构建Mac应用.command`；完成后将 `dist/PeripheralBattery.app` 复制到“应用程序”。构建脚本使用本机 Python 的架构，Apple 芯片和 Intel 应分别在对应环境构建，不是通用二进制包。核心构建脚本已在两种架构的云端 Mac 验证；首次安装流程和用户电脑的权限交互仍需实测。

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

开发机为 Windows，另有两种架构的云端 macOS 构建和依赖导入检查。运行 `python -m unittest discover -s . -p 'test_*.py' -v` 可检查解析、身份过滤、断连、失效读数、固定环序和提醒去重。测试样本为人工构造，不是真实 Mac 采样。菜单栏显示、权限请求、真实电量、通知和休眠恢复仍需真实设备实测。云端检查记录见 [构建结果](https://github.com/XLJFZ/peripheral-battery/actions/runs/40932820708)。

参考接口：[Apple IOBluetoothDevice](https://developer.apple.com/documentation/iobluetooth/iobluetoothdevice)、[rumps 文档](https://rumps.readthedocs.io/en/latest/)、[系统报告字段的项目示例](https://github.com/VincentShipsIt/macsweep.dev/blob/master/MacSweep/Sources/Core/Monitoring/ConnectedDevice.swift)。
