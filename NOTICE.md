# 修改与来源声明

CapsLockFlyout 是基于 FluentFlyout 2.14.0 制作的修改版本。

- 原项目：FluentFlyout，<https://github.com/unchihugo/FluentFlyout>
- 原项目版权：Copyright (C) 2025 Hugo Li
- 原项目许可：GNU General Public License version 3
- 修改时间：2026 年 9 月
- 1.1.0 输入法模块修改时间：2026 年 10 月

输入法查询流程参考用户提供的 `keyboard-ime-fluent-indicator-v2.wh.cpp`，
该脚本头部声明作者 ChatGPT、许可证 MIT；移植其前台焦点和 IMM 兼容查询，
保留完整转换模式值以区分日语平假名和片假名。没有采用脚本的 GDI+ 浮窗绘制。

本版本对原项目进行了大幅精简，只保留并重写了状态浮窗所需的启动、监控、定位、材料、动画、托盘和安装逻辑；加入了 Caps Lock 与 G-Helper 状态监控、G-Helper OSD 抑制、任务栏动态定位、服务自启动及安装器。

这是独立的社区修改版，并非 FluentFlyout 原作者或 G-Helper 官方发布版本。完整许可条款见仓库根目录的 `LICENSE`。

