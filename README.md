# 取走黄金（Stackline Classic）— PICO VR

一款以雪山寺庙为舞台的金条叠叠乐 Unity 游戏，已适配 PICO XR 与 Android。

## PICO VR 版本

- PICO tracked HMD 相机负责头部姿态；不会由桌面自由相机覆盖追踪。
- HUD 为可交互的 World Space **head-locked UI**：跟随头部的 6DoF 姿态，距视点 2.6m；保留 Y 轴 180° 方向修正与 2 倍物理尺寸。
- 右手控制器射线可操作 UI；右手 Trigger 在菜单开始游戏、游戏中放置金条。
- 原始 `EnvironmentScene` 保留为美术与 Terrain 的权威场景；每次 PICO 构建都生成独立的 `StacklineVR` 场景，不改写手调环境。

## PICO SDK 安装

PICO XR SDK 不随本仓库分发。请从 PICO 官方渠道取得与本项目匹配的版本，并安装到：

`Packages/com.bytedance.pico.xr`

当前项目使用 Unity `6000.5.4f1`、PICO XR `0.13.1` 和 Android IL2CPP/arm64 配置。该 SDK 目录被 Git 排除，避免分发受供应商许可约束的内容。

## 打开与构建

1. 用 Unity Hub 打开本工程，并选择 Android 平台。
2. 安装上述 PICO XR SDK 与项目中已提交的 PICO Interaction Demo sample。
3. 在 Unity 选择 `Tools > Stackline Classic > Build PICO APK`。
4. 输出 APK 为 `outputs/pico/TakeTheGold.apk`（故意不提交到 Git）。

## 验收操作

在 PICO 模拟器或真机中：

1. 启动游戏后确认 HUD 始终随头部移动与转向。
2. 用右手射线指向 UI，按右手 Trigger 开始。
3. 游戏中按右手 Trigger 放置移动中的金条。

## 仓库内容与忽略项

源码包括游戏逻辑、PICO 场景构建器、场景、模型与配置。`Library/`、`Temp/`、`Logs/`、`UserSettings/`、构建产物、APK、签名文件及 `Packages/com.bytedance.pico.xr/` 均不会提交。
