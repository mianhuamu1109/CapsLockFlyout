# 1.1.1 发布记录与流程

用户于 2026-10-18 实际试用后确认效果良好，并授权发布 1.1.1、更新项目主页与更新日志。

- 代码基线：本地 1.1.0；新版本独立保存在 version 1.1/1.1.1。
- 变化：切换前台窗口后 400 ms 内只更新输入法基线，不显示或补弹提示。
- 版本号：VERSION、主项目和 Installer/CapsLockFlyout.iss 均为 1.1.1。
- 验证：43 项状态序列检查通过；用户实际试用通过。自动实机聚焦测试的环境限制另行记录，详见 VALIDATION-1.1.1.md。
- 构建：运行 Build-Installer.ps1，使用独立干净目录，不覆盖当前安装。
- 附件输出：artifacts/installer/CapsLockFlyout-Setup.exe、SHA256SUMS.txt。
- 本次上传原安装包及对应 SHA256SUMS.txt，不重新构建；校验值为 3d3e65f90a43eee53fc71395bcebf2847189bfed46ebffdb75972c65dfcd63b2。
- 标签：v1.1.1；标题：CapsLockFlyout 1.1.1 · 切换窗口时静默同步。
- 正式发布后设为 Latest，保留 1.1.0、1.0.5 的发行文件与标签。

主页和文档已去除当前版本的“本地测试版”标识。原安装包不修改，内附说明保留构建时文本；Release 正文已注明这一点。
发布正文使用 RELEASE_NOTES.md，保留 GitHub 现有历史，不以本地独立分支强推覆盖。上传后核对服务器端附件 SHA-256、标签指向及源码树。
