# 1.1.0 发布检查清单

本文件面向维护者，不是 Release 正文。发布文案使用根目录 RELEASE_NOTES.md。

## 本轮完成与待办

- [x] 更新中文 README：新增输入法功能、实际行为、截图与兼容边界。
- [x] 准备三张 1.1.0 真实输入法截图，保留已有功能动图、深色模式与安装示意。
- [x] 准备 1.1.0 Release 正文、版本变更记录和安装后使用说明。
- [x] 保留 1.0.5 原版本，所有本轮修改仅位于 version 1.1/1.1.0。
- [x] 1.1.0 自包含运行包已构建；输入法已有本机 27 项断言通过记录。
- [x] 构建新的 1.1.0 自包含安装器，核对版本、运行时和打包输入。
- [x] 对本次安装器生成 SHA-256，不沿用 1.0.5 校验值。
- [x] 用户已安装本次安装包，并反馈验证无问题（2026-10-17）；未提供逐项安装场景记录，不据此声称已覆盖全部安装、卸载组合。
- [x] 用户确认本版可以正式上传，授权更新远端、创建标签与 Release。

本次发布使用用户已验收的原安装包，不重新生成二进制；上传前后均核对 SHA-256。发布操作不改动当前正在运行的程序。
已从 [官方来源](https://jrsoftware.org/isdl.php) 下载 Inno Setup 7.1.0 x64，验证有效的 Pyrsys B.V. 数字签名后，以便携模式部署到项目父级工具目录。新的构建脚本在独立临时打包目录中发布，不覆盖正在运行的 package-win-x64。

## 仓库与主页

将本版本目录作为仓库根目录发布，不要把父目录中的全部历史版本混在同一个源码根目录中。
保留现有 GitHub 历史；推送前确认远端分支与本地版本的共同历史，必要时仅移植本版变更，禁止强推覆盖旧提交。

必需内容：README.md、RELEASE_NOTES.md、CHANGELOG.md、LICENSE、NOTICE.md、源码、Installer、Build-1.1.ps1、Build-Installer.ps1、docs/assets。
不提交 artifacts、dist、.tools、bin、obj、Data 日志、录制原片或本机捕获工具。

建议仓库简介：
“Windows 11 原生风格的输入法、大写锁定与 G-Helper 状态浮窗，支持汉语 / 英语 / 日语模式显示。”

建议 Topics：
windows-11、osd、ime、capslock、g-helper、asus、wpf、fluent-design。

正式发布时：
1. 删除 README 中“发布材料已整理，尚未正式上传”的提示。
2. 确认 VERSION、项目 Version、安装器 MyAppVersion 和正文均为 1.1.0。
3. 将资料与源码一起提交后，才创建 v1.1.0 标签。
4. RELEASE_NOTES.md 使用标签固定的 raw.githubusercontent.com 图片地址；标签及素材推送后才会生效。发布前可在 README 查看相同本地图片。
5. 旧安装器截图标明来自 1.0.5，不能据此声称新版安装器已实测。

## 构建安装器

先安装官方 Inno Setup 编译器，并提供 Installer/CapsLockFlyout.iss 引用的简体中文语言文件。
在本版本根目录执行：

```powershell
.\Build-Installer.ps1
# 其他机器可明确指定编译器与 SDK 路径：
# .\Build-Installer.ps1 -CompilerPath 'D:\Tools\Inno Setup\ISCC.exe' -DotnetPath 'D:\Tools\dotnet\dotnet.exe'
```

脚本会新建独立的 artifacts/installer-build-时间-唯一标识/payload，发布自包含 x64 运行包，并校验版本与必要运行时。它不会从正在使用的 package-win-x64 复制数据，也不会停止现有进程。

安装脚本额外排除 Data 内容、PDB、日志和 JSONL。只使用本次新生成的 artifacts/installer/CapsLockFlyout-Setup.exe，不要重命名旧版安装包冒充新版。

脚本自动生成 SHA256SUMS.txt 和 BUILD-INFO.json（构建信息）。构建后可以独立复核：

```powershell
Get-FileHash -LiteralPath '.\artifacts\installer\CapsLockFlyout-Setup.exe' -Algorithm SHA256
Get-Content -LiteralPath '.\artifacts\installer\SHA256SUMS.txt'
```

构建日志保留在独立构建目录中，BUILD-INFO.json 的 installationTested=false 记录的是构建完成当时尚未执行安装测试；用户随后已自行安装并反馈验证无问题。此反馈与自动编译、静态检查分开记录，不修改历史构建信息。

## Release

- 标签：v1.1.0。
- 标题：CapsLockFlyout 1.1.0 · 输入法状态浮窗。
- 正文：直接使用 RELEASE_NOTES.md。
- 附件：CapsLockFlyout-Setup.exe、SHA256SUMS.txt。
- 正式发布前先用草稿检查三张图片、下载文件、许可文件及版本号；所有未完成检查通过后再发布。
- 发布后下载一次安装器，核对 SHA-256，再确认 README 下载按钮指向 1.1.0。
