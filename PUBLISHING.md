# GitHub 发布检查清单

## 仓库

- 将当前目录作为仓库根目录上传，确保 `README.md`、`LICENSE`、`NOTICE.md` 与 `docs/assets` 均已包含。
- 不提交 `artifacts`、`dist`、`.tools`、`Installer/Tools`、`bin`、`obj`、录制原片或本机捕获工具。
- 建议仓库简介：`Windows 11 原生风格的 Caps Lock 与 G-Helper 状态浮窗`。
- 建议 Topics：`windows-11`、`osd`、`capslock`、`g-helper`、`asus`、`wpf`、`fluent-design`。

## Release 1.0.5

1. 创建标签 `v1.0.5`。
2. Release 标题填写 `CapsLockFlyout 1.0.5`。
3. 将 `RELEASE_NOTES.md` 的内容粘贴为版本说明。
4. 上传 `artifacts/installer/CapsLockFlyout-Setup.exe`。
5. 同时上传 `artifacts/installer/SHA256SUMS.txt`。
6. 发布前下载一次安装包并核对 SHA-256，确认安装位置、自启与开始菜单三个选项均正常。
