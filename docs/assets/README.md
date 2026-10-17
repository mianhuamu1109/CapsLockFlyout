# 展示素材索引

本目录保存 GitHub 中文主页与发布说明使用的成品素材。截图来自程序真实运行画面，不用重绘界面代替实机结果。

## 1.1.0 新增截图

| 文件 | 内容 | 来源 |
| --- | --- | --- |
| `ime-hiragana-light.png` | 日语 / 平假名 | 实机验证 Japanese-API-only-Hiragana.png |
| `ime-katakana-light.png` | 日语 / 片假名 | 实机验证 Japanese-API-only-Katakana.png |
| `ime-direct-light.png` | 日语 / 直接输入 | 实机验证 Japanese-API-only-Direct.png |

三张图片来自 2026-10-17 的 1.1.0 集成验证，复制原始 PNG，未重绘、未替换文字。测试编辑器修改自己的 IME 状态，程序实际查询并显示；采集画面只包含浮窗及邻近壁纸，无输入文本。

原始证据保存在本地 `artifacts/ime-polling-confirmed`，不作为发行附件。公开的测试范围与结论见根目录 `VALIDATION-1.1.md`。

这组图片仅展示静态模式与固定宽度，不应描述为切换动画录屏。主页按原生 230 px 宽度并排展示；Release 正文使用 `v1.1.0` 标签固定的图片地址，推送标签后才可访问。

## 沿用的 1.0.5 素材

这些素材用于展示本版保留的功能，未作为 1.1 输入法功能或新版安装器验收的证据。

| 文件 | 建议用途 |
| --- | --- |
| `hero-light.webp` | README 顶部原有功能演示，不包含输入法功能 |
| `features-light.png` | 功能总览六宫格 |
| `binary-states-light.webp` | 大写锁定、Fn 锁定、触控板与麦克风的开关状态动画 |
| `taskbar-adaptive-light.webp` | 任务栏显示/隐藏时的浮窗位置适配 |
| `light-caps-lock-on.png` | 大写锁定高清静态图 |
| `light-keyboard-effect.png` | 固定宽度键盘灯效高清静态图 |
| `dark-mode-preview.png` | 深色模式代表性浮窗图 |
| `installer-location-light.png` | 可选择安装位置的安装器页面 |
| `installer-options-light.png` | 安装器附加任务页面，展示服务自启与开始菜单选项 |
| `tray-menu-light.png` | 系统托盘图标及设置、显示预览、退出程序菜单 |

原有动图尺寸为 960×440、30 fps；功能总览图为 960×660。演示素材的 30 fps 不代表程序动画更新频率。安装器截图中的版本号为 1.0.5，主页注明为选项示意；深色图片仅用于主题展示，不代表已补录深色输入法截图。
