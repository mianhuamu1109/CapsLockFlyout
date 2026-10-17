# CapsLockFlyout 1.1.0 · 输入法状态浮窗

这次更新让原有的 Windows 11 风格浮窗也能展示输入法与当前模式。保留 1.0.5 的大写锁定、G-Helper 状态提示、主题、任务栏自适应与托盘功能。

## 新增：汉语 / 英语 / 日语输入法显示

| 输入法 | 显示的模式 |
| --- | --- |
| 汉语 | 中文模式、英语模式 |
| 英语 | 直接输入 |
| 日语 | 平假名、片假名、直接输入 |

- 输入法浮窗宽度恒定；中间是语言名称，右侧模式名在固定区域居中。
- 语言名称、模式名称分别进行文字过渡，沿用现有窗口滑入滑出、高度与材料。
- 每 120 ms 查询前台应用的实际输入法状态，不用键盘按键次数预测，不改变输入法状态。
- 启动与会话恢复时静默建立基线；观察到变化后才提示，状态不变时不弹回弹提示。
- 日语输入法下不显示大写锁定浮窗，避免模式快捷键触发错误的 Caps Lock 提示。
- 输入法与大写锁定功能均无需 G-Helper 或 Windhawk。

### 实机预览

| 平假名 | 片假名 | 直接输入 |
| --- | --- | --- |
| ![日语平假名](https://raw.githubusercontent.com/mianhuamu1109/CapsLockFlyout/v1.1.0/docs/assets/ime-hiragana-light.png) | ![日语片假名](https://raw.githubusercontent.com/mianhuamu1109/CapsLockFlyout/v1.1.0/docs/assets/ime-katakana-light.png) | ![日语直接输入](https://raw.githubusercontent.com/mianhuamu1109/CapsLockFlyout/v1.1.0/docs/assets/ime-direct-light.png) |

截图来自 1.1.0 浅色模式真实运行画面，三种模式使用相同宽度。

## 验证与边界

本机 Microsoft 输入法 / 独立 WinForms 编辑器测试中，27 项断言通过、0 失败，覆盖中文连续 Shift、日语三组 Caps Lock 组合键、日语操作后返回中文、无按键事件的模式变化和 Win+Space 提交结果。这不代表所有第三方输入法和应用均已兼容。

- Win+Space 系统选择面板中的高亮项不一定已经生效；浮窗只显示前台应用实际提交的状态。在本机验证中，松开 Win 后更新，不在按住 Win 时按 Space 次数预测。
- 全角与半角片假名暂共用“片假名”名称。
- 不支持 IMM 兼容查询的应用或输入法可能返回未知或滞后状态。
- 120 ms 是后台检查间隔，不是端到端延迟保证；尚未做跨设备 CPU、内存和功耗基准，不作具体占用承诺。

## 安装与升级

需要 Windows 11 x64（22000 或更高）。

1. 从本次 Release 附件下载 `CapsLockFlyout-Setup.exe`。
2. 升级前从托盘退出旧程序，安装到原目录；请勿同时运行两版。
3. 安装时可自选位置，并选择 Windows 服务开机自启与开始菜单快捷方式。
4. 安装包包含 .NET 运行时。程序自身日志保存在安装目录 `Data` 中；Windows 系统登记及安装临时文件仍由系统管理。

G-Helper 硬件功能仍取决于设备支持；首次配合使用后建议重启 G-Helper，使其读取 `disable_osd=1`。

安装包未签名。请从本仓库下载，并使用该次 Release 附带的 `SHA256SUMS.txt` 核对文件；旧版本的校验值不适用于本版。
