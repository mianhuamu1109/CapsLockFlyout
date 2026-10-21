# 1.1.3 发布记录与流程

- 基于已发布的 1.1.2，在 version 1.1/1.1.3 独立开发，旧版保持不变。
- 新增按包族标识和应用 ID 禁用输入法浮窗，应用选择与搜索、旧路径规则显式转换、配置格式兼容与备份。
- 版本号在 VERSION、项目与安装脚本中统一为 1.1.3。
- 用户于 2026-10-21 明确确认已验证，并授权上传 GitHub。
- 验证记录见 VALIDATION-1.1.3.md；正式发布说明为 RELEASE_NOTES.md。
- 运行 Build-Installer.ps1，在干净 payload 中构建，输出 artifacts/installer/CapsLockFlyout-Setup.exe、SHA256SUMS.txt、BUILD-INFO.json。
- 本次上传已验收的原安装包与 SHA256SUMS.txt，不重新构建；安装包 SHA-256 为 1e797a7055dc55988d652aac118a611a95c924c6bf038b68f8844a62af646fac。
- 标签 v1.1.3，标题“CapsLockFlyout 1.1.3 · 按应用身份禁用输入法浮窗”，设为最新正式版。
- 发布后核对源码树、标签与主分支、发布正文、附件大小及服务器端摘要，保留现有发行历史，不强推仓库。
- 不替换用户当前安装、服务或名单。原安装包内的测试版措辞不改动，Release 正文解释其正式发布状态。
