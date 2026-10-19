# 1.1.2 发布记录与流程

- 基于已验收的 1.1.1，在 version 1.1/1.1.2 独立开发。
- 新增按前台程序禁用输入法浮窗，版本号已统一为 1.1.2。
- 用户于 2026-10-19 反馈认可，并授权更新文档及上传新版本。
- 不覆盖 1.1.1 源码或安装包，不更改用户当前运行程序和服务。
- 测试记录见 VALIDATION-1.1.2.md；正式发布说明为 RELEASE_NOTES.md。
- Build-Installer.ps1 从独立干净目录生成自包含安装包。
- 输出 artifacts/installer/CapsLockFlyout-Setup.exe、SHA256SUMS.txt 和 BUILD-INFO.json。
- 本次上传用户已验收的原安装包和对应 SHA256SUMS.txt，不重新打包；SHA-256 为 70ac1220458a1c1b2307a44ec0d05e2e28fb6a75fe814b6fc00798d98184be3c。
- 标签 v1.1.2，标题“CapsLockFlyout 1.1.2 · 指定程序禁用输入法浮窗”；上传后核对服务器端附件摘要、标签与源码树，设为最新正式版。
- 保留现有发行历史，不强推 GitHub 主分支。

项目主页补充了资源管理器示例。发布文档已去除待验收标记；原安装包内文本不变，Release 正文解释包内旧标记与本次正式发行状态的区别。
