# CapsLockFlyout 1.0.5

首个面向公开发布整理的安装版本。

## 功能

- 提供大写锁定开启/关闭浮窗。
- 接管 G-Helper 的性能模式、GPU 模式、刷新率、屏幕响应加速、屏幕色彩模式、键盘背光、键盘灯效、Fn 锁定、触控板、麦克风、AniMe Matrix、Slash、ScreenPad、Mini LED 与电源状态提示。
- 使用接近 Windows 11 原生音量/亮度 OSD 的高度、材料、圆角、描边、阴影和整体滑入/滑出动画。
- 浮窗宽度按内容自适应；键盘灯效使用固定名称区域，切换效果时布局不跳动。
- 自动跟随浅色/深色主题，并适配 Windhawk 控制的任务栏显示与隐藏。
- 常用 G-Helper 快捷键即时预测显示，随后由实际配置校准且不重复弹窗。
- 锁屏、休眠与解锁后只重建状态，不回放全部状态。

## 安装与运行

- 下载并运行 `CapsLockFlyout-Setup.exe`。
- 安装位置可自定义。
- 安装时可选择 Windows 服务开机自启和开始菜单快捷方式。
- 安装包包含 x64 .NET 运行时。
- 程序日志保存在安装目录的 `Data` 子目录。
- 托盘菜单提供设置、显示预览和退出程序。

## 使用提示

- 需要 Windows 11 x64（版本 22000 或更高）。
- G-Helper 功能取决于电脑型号；仅使用大写锁定提示时不需要 G-Helper。
- 首次运行后建议重启一次 G-Helper，使其读取由本程序写入的 `disable_osd=1`。
- 安装包当前未签名，Windows 可能显示“未知发布者”或 SmartScreen 提示。

## 文件校验

`CapsLockFlyout-Setup.exe`

```text
03E1056C367E0BB300FA350E85D582515ADAA7FF2E1FBEE3B6DF2DAA30240806
```

同一校验值也写入随安装包发布的 `SHA256SUMS.txt`。
