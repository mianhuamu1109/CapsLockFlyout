<div align="center">

# CapsLockFlyout

**为 Windows 11 与 G-Helper 打造的轻量原生风格状态浮窗**

Windows 11 x64 · 中文界面 · 当前版本 1.0.5

[下载最新版本](../../releases/latest) · [安装说明](#安装) · [支持的状态](#支持的状态) · [自行构建](#自行构建)

<img src="docs/assets/hero-light.webp" width="960" alt="CapsLockFlyout 功能演示">

</div>

CapsLockFlyout 用统一的 Windows 11 原生风格展示大写锁定和 G-Helper 硬件状态。它保留系统音量、亮度浮窗不动，只接管容易缺失、延迟或样式不统一的状态提示。

## 主要特点

<img src="docs/assets/features-light.png" width="960" alt="CapsLockFlyout 功能总览">

- **接近系统原生观感**：48 DIP 高度、自适应宽度、圆角、亚克力材料、DWM 描边与阴影。
- **完整窗口动画**：浮窗连同材料和阴影整体滑入、滑出，动画位置以 120 Hz 更新。
- **状态变化立即显示**：对常用 G-Helper 快捷键先预测、后校准，避免等原 OSD 消失后才出现。
- **二分状态更醒目**：大写锁定、Fn 锁定、麦克风、触控板等带状态条与图标动画。
- **适配自动隐藏任务栏**：每次出现和动画过程中都会重新读取任务栏边界，可配合 Windhawk。
- **自动跟随主题**：支持 Windows 浅色与深色模式；关闭透明效果时自动使用对应实色背景。
- **安静驻留**：托盘运行，监控以事件驱动为主；120 Hz 计时器只在浮窗动画期间启用。

## 支持的状态

| 类别 | 状态提示 |
| --- | --- |
| Windows | 大写锁定开启/关闭 |
| 性能与显卡 | 性能模式、GPU 模式 |
| 显示 | 刷新率、屏幕响应加速、屏幕色彩模式、Mini LED |
| 键盘与输入 | 键盘背光、键盘灯效、Fn 锁定、触控板、麦克风 |
| 设备扩展 | AniMe Matrix、Slash 灯效、ScreenPad |
| 电源 | 接入电源、使用电池 |

除大写锁定外，其余项目依赖 G-Helper；实际可用项目取决于电脑型号和 G-Helper 提供的配置。

## 状态动画

<img src="docs/assets/binary-states-light.webp" width="960" alt="二分状态动画演示">

大写锁定沿用锁扣开合动画。Fn 锁定、麦克风、触控板、屏幕响应加速、Mini LED 和电源状态使用相同节奏的状态条、明暗、缩放和回弹反馈。

## 任务栏自适应

<img src="docs/assets/taskbar-adaptive-light.webp" width="960" alt="任务栏显示与隐藏时的浮窗定位">

浮窗不会只按系统工作区推测位置，而会读取任务栏当前的真实窗口边界。任务栏显示、隐藏或在动画中移动时，浮窗都会同步调整停靠点。

## 浅色与深色模式

<table>
  <tr>
    <td width="50%"><img src="docs/assets/light-caps-lock-on.png" alt="浅色模式"></td>
    <td width="50%"><img src="docs/assets/dark-mode-preview.png" alt="深色模式"></td>
  </tr>
  <tr>
    <td align="center">浅色模式</td>
    <td align="center">深色模式</td>
  </tr>
</table>

## 安装

1. 前往 [Releases](../../releases/latest)，下载唯一需要的文件 `CapsLockFlyout-Setup.exe`。
2. 运行安装器并选择安装位置。
3. 按需勾选“使用 Windows 服务开机自启”和“添加到开始菜单”。
4. 安装完成后启动 CapsLockFlyout。

<table>
  <tr>
    <td width="50%"><img src="docs/assets/installer-location-light.png" alt="选择安装位置"></td>
    <td width="50%"><img src="docs/assets/installer-options-light.png" alt="选择附加任务"></td>
  </tr>
  <tr>
    <td align="center">安装位置可自定义</td>
    <td align="center">自启与开始菜单均可选</td>
  </tr>
</table>

安装包已包含 x64 .NET 运行时，不需要另行安装 .NET。程序自身产生的日志位于安装目录的 `Data` 子目录；不会把自身日志写到用户配置目录。

> 当前安装包未使用代码签名证书，Windows 可能显示“未知发布者”或 SmartScreen 提示。请只从本项目 Releases 下载，并核对随版本提供的 SHA-256 校验值。

## 与 G-Helper 配合

- CapsLockFlyout 会把 G-Helper 的 `disable_osd` 设为 `1`，避免两套 OSD 重复显示。
- G-Helper 只在启动时读取该选项，因此首次运行 CapsLockFlyout 后建议重启一次 G-Helper。
- 程序读取 G-Helper 的现有配置与日志来识别状态，不会代替 G-Helper 控制硬件。
- 锁屏、休眠和解锁期间会暂停提示；回到桌面后只重建状态基线，不会把所有状态逐个弹出。

## 托盘与自启动

<img src="docs/assets/tray-menu-light.png" width="720" alt="CapsLockFlyout 托盘菜单">

双击托盘图标可打开设置；右键可选择“设置”“显示预览”或“退出程序”。开机自启使用 Windows 服务，在用户登录或解锁后启动当前用户会话中的托盘程序。

## 系统要求

- Windows 11 64 位，版本 22000 或更高。
- G-Helper：仅在需要华硕设备状态提示时必需。
- 亚克力效果需要启用 Windows“透明效果”；关闭后会自动使用系统同色实心背景。

## 自行构建

需要 .NET 10 SDK。主程序只编译精简后的浮窗核心，不依赖第三方 NuGet 包：

```powershell
dotnet build .\FluentFlyout-2.14.0\FluentFlyout-2.14.0\FluentFlyoutWPF\FluentFlyout.csproj -c Release
```

如需生成自包含安装包，可先发布 `win-x64` 文件，再使用 `Installer/CapsLockFlyout.iss` 构建 Inno Setup 安装器。

## 项目来源与许可

本项目基于 [FluentFlyout 2.14.0](https://github.com/unchihugo/FluentFlyout) 精简和修改，属于与原项目不同的社区修改版。项目依照 [GNU GPL v3](LICENSE) 发布，修改说明见 [NOTICE.md](NOTICE.md)。

