# 1.1 输入法状态查询

## 1.1.2 程序禁用名单

设置页选择 EXE 完整路径，保存到安装目录 Data/ime-exclusions.json。内存中以不可变数组快照供轮询读取，不在轮询中读写配置文件；名单为空时不增加进程路径查询。

有名单时，在现有 120 ms 轮询中查询前台窗口所属进程的完整路径，大小写不敏感地精确匹配。使用 GetWindowThreadProcessId、OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) 和 QueryFullProcessImageNameW；不注入进程、不改变目标输入法、不增加键盘监听。[微软接口说明](https://learn.microsoft.com/zh-cn/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)

命中时跳过 IME 查询，清空基线，进入禁用状态时使排队消息失效并通知 UI 收起旧语言卡片。UI 只收起已失效的输入法卡片，不收起后来出现的硬件提示或新的有效语言卡片。显示前再次校验名单与前台窗口。离开后沿用 400 ms 切窗静默策略，不补弹。

名单更改立即更新内存并作会话重建，最多 1500 ms 静默，避免增删后回放旧状态。路径查询失败时不猜测程序身份、不全局禁用其他应用；受保护进程或由另一 EXE 承载的窗口需单独核实。短于一次轮询的前台切换可能不被采到。这里的禁用只影响本程序语言浮窗，不影响 Windows 自带输入法面板。

配置采用同目录临时文件写入后替换，写入失败不发布新配置；损坏配置显示错误并保留原文件，不静默覆盖。安装器不包含 Data，覆盖升级保留名单；沿用既有卸载行为，卸载会删除 Data。

## 1.1.1 前台窗口静默策略

InputMethodChangeTracker 按前台顶层窗口区分状态。检测到窗口变化后开启 400 ms 固定静默期，期间持续更新基线；到期不补发任何变化。窗口内部输入框焦点变化不单独重置静默时间。

消息携带前台窗口、窗口切换序号和会话序号；进入 UI 显示前再次核对，旧窗口或旧会话的排队消息失效。不增加临时面板识别或输入法键盘触发。

静默期内主动切换也可能不提示；时间以后台轮询检测为起点。验证范围见 VALIDATION-1.1.1.md。

基于用户提供的 keyboard-ime-fluent-indicator-v2.wh.cpp（MIT）的
ReadForegroundSnapshot、QueryLegacyIme、Changed 方法移植。

## 数据来源

- 每 120 ms 在后台读取一次前台线程、焦点窗口和键盘布局。
- 跨进程沿用脚本的 ImmGetDefaultIMEWnd + WM_IME_CONTROL 查询：
  IMC_GETOPENSTATUS=5，IMC_GETCONVERSIONMODE=1；
  SendMessageTimeout 使用 SMTO_ABORTIFHUNG | SMTO_BLOCK，50 ms 超时。
- 同进程使用 ImmGetContext / ImmGetOpenStatus / ImmGetConversionStatus，
  并配对 ImmReleaseContext。
- 查询完整转换标志；内容不依赖键盘按键、按键次数、预测、截图或 OCR。
- 仅观察到布局或显示模式变化时显示。启动和会话恢复静默建立基线。
- 原有 CapsLock 监听仅用于“大写锁定”功能，不触发输入法模块。

## 日语接口调查

脚本已经请求完整转换值，但只保留 IME_CMODE_NATIVE 一位，因而无法在
它的文本输出中区分平假名和片假名。Windows 提供下列标志：

| 标志 | 意义 |
| --- | --- |
| IME_CMODE_NATIVE (0x1) | 本地文字 / 英数字 |
| IME_CMODE_KATAKANA (0x2) | 片假名 / 平假名 |
| IME_CMODE_FULLSHAPE (0x8) | 全角 / 半角 |
| IME_CMODE_ROMAN (0x10) | 罗马字输入方式 |

本版保留完整值，以开闭状态优先区分直接输入，并用 NATIVE/KATAKANA
区分平假名和片假名。片假名全角/半角暂共用“片假名”显示名。
ROMAN 标志不代表“直接输入”，不能用它替代 IME 开闭状态。

官方接口：
- https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetconversionstatus
- https://learn.microsoft.com/en-us/windows/win32/intl/ime-conversion-mode-values
- https://learn.microsoft.com/en-us/windows/win32/tsf/predefined-compartments
- https://learn.microsoft.com/en-us/windows/win32/tsf/compartments

TSF 也提供 GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION 与
GUID_COMPARTMENT_KEYBOARD_OPENCLOSE，但官方定义它们属于线程管理器，
不是任意进程都能直接读取的全局状态。在浮窗进程中创建自己的线程管理器，
不能据此声称拿到了其他前台应用的输入法模式。本版采用用户已验证的 IMM
兼容查询，不引入进程注入或修改输入法状态。

## 行为边界

- 单独 Shift / CapsLock 若没有使输入法状态变化，不显示语言浮窗。
- Win+Space 面板中的暂时高亮可能尚未提交到前台应用。此时等待系统
  实际提交状态后显示，不根据高亮行或按键次数预测。
- 不支持 IMM 兼容查询的应用或输入法可能返回未知或过时状态；本地
  Microsoft IME 的通过结果不等于所有第三方输入法均兼容。
- 固定宽度，中间汉语/英语/日语，右侧模式名居中，48 DIP 高度和原材质沿用。

## 验证

TestFixtures/ImeIntegration 在独立 WinForms 编辑器运行完整应用，并读取
应用实际呈现的诊断消息。覆盖中文 Shift、日语组合键连续三轮、日语切换后
中文仍可切换、状态不变保持静默、Shift+A 不误报，以及 Win 松开提交。
另直接改变测试编辑器自身 IME 状态，不发送键盘事件，验证纯轮询也能发现
平假名、片假名、直接输入三种状态。

应用默认不记录输入法诊断；仅 --ime-diagnostics 开启界面状态日志，
保存在可执行文件旁 Data 目录，不记录输入文本。
