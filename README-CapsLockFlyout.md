# CapsLockFlyout

这是从 FluentFlyout 2.14.0 精简得到的 Caps Lock 与 G-Helper 状态浮窗。原项目源码仍保留在原目录中供对照，但新工程只会编译以下功能：

- 全局 Caps Lock 开关提示；
- G-Helper 的性能模式、麦克风、键盘背光、触控板；
- G-Helper 的 GPU 模式、刷新率、屏幕响应加速、Fn Lock、Aura、视觉模式、AniMe Matrix、Slash、ScreenPad、Mini LED；
- 电源接入/拔出提示。

系统音量与屏幕亮度继续由 Windows 原生 OSD 显示，新程序不会接管，也不会制造重复提示。

## 外观与定位

浮窗高度固定为 Windows 11 原生 HardwareConfirmator 的 `48 DIP`，宽度则按每条提示的实际内容在原生资源规定的 `129–280 DIP` 范围内自适应。短提示不会留下大片空白，长提示会扩展到上限后再省略。圆角保持 `8 DIP`，描边与阴影统一交由 DWM 合成，避免材料表面与 WPF 描边重叠形成双轮廓。当前机器 200% DPI 下高度为 `96 px`，宽度会在 `258–560 px` 之间变化。

“键盘灯效”浮窗以当前字号实际测量最宽的“GPU 模式”，据此固定右侧名称区域；它与“键盘灯效”之间保留 `8 DIP`，对应图标到标题的视觉间距。其他灯效名称继续在同一固定区域内居中，左侧图标与标题位置不变；其他浮窗仍按内容自适应。

定位遵循 Windows 的 HardwareConfirmator 规则：默认主屏，并读取 `ConfirmatorPosition/PositionIndex`（底部居中、左上、顶部居中）。卡片停靠点与当前可用边缘保持 `12 DIP`；动画过程中会持续读取主任务栏的真实 DWM 边界。Windhawk 将任务栏移入或移出屏幕时，浮窗会同步移动，不依赖失真的系统工作区矩形。

进入和退出移动整个原生窗口（包括 DWM 阴影与材料），而不是只移动窗口内的内容。进入使用 Windows Fluent 的 250 ms `cubic-bezier(0, 0, 0, 1)` 曲线，从屏幕/任务栏边缘外移动一个完整卡片高度；退出使用 83 ms `cubic-bezier(1, 0, 1, 1)` 曲线。坐标由独立的高精度计时器以 120 Hz 更新；文件读取与 G-Helper 状态检查在后台线程完成，不会阻塞动画。

材质改用系统 XAML 实际引用的 `AcrylicInAppFillColorDefaultBrush` 同族合成路径，包含亚克力模糊与噪点层；深色/浅色分别使用 Windows 11 的 `#2C2C2C` / `#FCFCFC` 色板，并使用原 FluentFlyout 的 175 亚克力强度。轮廓由 DWM 按当前系统主题统一合成，并与圆角裁切、阴影共用同一几何边界，不再叠加一层 WPF 描边。关闭 Windows“透明效果”时会自动退回系统使用的 `#2C2C2C` / `#F9F9F9` 实色。

Caps Lock 保留原 FluentFlyout 的锁扣动画：167 ms 内状态条在 `60/36 DIP` 和 `1/0.2` 透明度之间过渡，锁扣在 `0°/25°` 之间开合并带 1 DIP 回弹。触控板、Fn Lock、麦克风、屏幕响应加速、Mini LED 和电源状态采用相同的状态条时序，并通过图标缩放、明暗和回弹明确表示二分状态；麦克风静音、电池供电均按关闭态表现。

## 安装版

运行 `CapsLockFlyout-Setup.exe` 后可以自行选择安装位置；本机存在 D 盘时默认建议 `D:\Programs\CapsLockFlyout`。安装器还会显示“使用 Windows 服务开机自启”和“添加到开始菜单”两个选项，可在安装时分别决定是否启用。安装包已经包含 x64 .NET 运行时及 ReadyToRun 优化，不依赖 C 盘中的共享 .NET。

程序创建的错误日志和服务日志只会写入安装目录的 `Data` 子目录。卸载程序、运行时和说明文件也都留在安装目录；不创建桌面快捷方式，只有勾选对应选项时才创建开始菜单快捷方式。Windows 仍会在注册表中保留卸载和服务所必需的少量系统登记信息。程序会读取并更新 G-Helper 已有的 `%APPDATA%\GHelper\config.json` 与日志，但不会在那里创建 CapsLockFlyout 自己的数据。

勾选开机自启后，自动启动服务会随系统正常启动，并在用户登录、解锁或连接会话时用该用户身份立即拉起前台程序。服务本身不尝试跨会话显示界面，因此托盘、键盘监听和浮窗仍在正确的用户桌面中运行。关闭服务不会强行结束当前浮窗进程；卸载时则会先正常退出前台进程，再停止并删除服务。

## 运行

安装目录中的 `CapsLockFlyout.exe` 可直接启动。普通双击会打开一个小设置窗口，可选择“通过 Windows 服务开机自启”；更改该选项时 Windows 会请求管理员授权。关闭设置窗口后浮窗程序继续在后台运行，再次双击只会唤回设置窗口，不会产生第二个实例。

运行期间会显示系统托盘图标。双击图标可打开设置，右键菜单可以打开设置、显示预览或退出程序。

- `CapsLockFlyout.exe --startup`：静默启动浮窗服务，不显示设置窗口；
- `CapsLockFlyout.exe --preview`：显示一枚预览浮窗；
- `CapsLockFlyout.exe --exit`：安全结束正在运行的实例。

建议先从原 FluentFlyout 的托盘菜单退出旧商店版，再启动本程序。为方便并行验证，本程序也会自动隐藏旧版的 160 × 50 DIP 锁定提示，并精确拦截 G-Helper 当前使用的 `FloatingNativeWindow` OSD。

程序会在真实 G-Helper 配置中维护 `"disable_osd": 1`，使 G-Helper 不再创建自己的 OSD。G-Helper 只在启动时读取该选项，所以第一次使用本程序后需要重启一次 G-Helper；重启前由窗口事件拦截兜底。G-Helper 按键日志由文件变化事件即时读取；键盘灯效的 `Key 179` 会按 G-Helper 当前设备支持的模式顺序立即预测并显示，稍后写出的实际模式只用于校准，不会重复弹窗。

锁屏、休眠和解锁期间会暂停状态提示；回到桌面后重新以当前日志末尾和配置内容建立基线。G-Helper 在恢复硬件时产生的日志重建或批量配置同步只更新内部状态，不会把全部状态依次回放为浮窗。

## 构建

工程不再依赖任何第三方 NuGet 包，目标为 `net10.0-windows`：

```powershell
dotnet build .\FluentFlyout-2.14.0\FluentFlyout-2.14.0\FluentFlyoutWPF\FluentFlyout.csproj -c Release
```

项目内 `TestFixtures/GHelper` 与 `TestFixtures/FakeGHelperOsd` 分别用于验证日志解析和竞争 OSD 拦截，不会更改真实硬件状态。

## 项目来源与许可

本项目基于 [FluentFlyout 2.14.0](https://github.com/unchihugo/FluentFlyout) 精简和修改，属于独立的社区修改版，并非 FluentFlyout 或 G-Helper 官方发布版本。项目依照 GNU GPL v3 发布，完整条款见源码仓库根目录的 `LICENSE`。
