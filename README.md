# 取走黄金（Stackline Classic）— PICO VR

一款以雪山寺庙为舞台的金条叠叠乐 Unity 游戏，已适配 PICO XR 与 Android。

## PICO VR 版本

- PICO tracked HMD 相机负责头部姿态；不会由桌面自由相机覆盖追踪。
- 开始页、局内 HUD、结束页、续命页和弹窗均为固定在场景中的 World Space UI；使用独立世界锚点，移动或转头时位置不跟随头显。左侧面板已左移以减少遮挡。
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
4. 正式版输出为 `outputs/pico/TakeTheGold-audio-fx.apk`；选择 `Build PICO Diagnostics APK` 可生成开启 Development 与 Frame Timing Stats 的诊断版 `outputs/pico/TakeTheGold-audio-fx-diagnostics.apk`。APK 不提交到 Git，通过 GitHub Releases 分发。

构建前退出 Play Mode，并等待 Android 平台切换和脚本编译完成。构建器会绑定 `Assets/StacklineClassic/Art/UI/StacklineLauncherIcon.png` 中的金条游戏图标，同时配置 Android 普通图标和自适应图标。当前发布版本为 `1.2.1-audio-fx.1`，versionCode 为 `6`。

## 世界空间 UI 与音效（2026-09-09）

- 开始、局内、结算与续命界面使用金色寺庙风格面板；装饰图形不拦截按钮输入。
- 添加落块、Perfect、切落、失败和救援短音效；静音开关同步控制音源，Perfect 不再误播切落音。音频由 Unity 程序生成，无额外在线下载。
- 保留有界的金色火花、轮廓与碎片特效池；沿用游戏金条启动图标。

## 性能优化与结束页修复（2026-09-07）

- 生成的 PICO 场景使用独立 Forward URP 配置：渲染比例 0.9、关闭 SSAO/HDR/额外深度与不透明纹理、缩减阴影距离与实时阴影光源。保留原始环境场景。
- 金条火花、碎片与轮廓使用有上限的对象池，共享网格和材质，减少运行中的实例创建、销毁及物理开销。
- 装饰动画使用独立 Canvas；资源文字、菜单内容及存档在状态变化时更新，减少重复刷新。
- 修复 World Space UI 关闭普通 GraphicRaycaster 后，结束页无法接受鼠标/触摸射线的问题；同时保留 XR 控制器射线，菜单操作按帧防止重复执行。

Unity Editor 已完成鼠标与 tracked 输入模型的结束页回归（8 个场景、69 个断言），以及结束后设置/重新开始流程检查；游戏循环压力检查完成 20 次重新开始、100 次 Perfect、983 个断言。正式版与诊断版 Android ARM64 APK 均构建成功，签名及包内图标已核验。

上述为本机 Editor 与构建验证；PICO 真机手柄操作和帧率尚未实测，不能据此宣称已达到目标帧率。进入 `StacklineVR` 的 Play Mode 后，可通过 `Tools > Stackline Classic > Performance > Start Play Checks` 重跑游戏循环检查。

## 验收操作

在 PICO 模拟器或真机中：

1. 启动游戏后移动和转动头部，确认所有界面保持在场景中的固定位置。
2. 用右手射线指向 UI，按右手 Trigger 开始。
3. 游戏中按右手 Trigger 放置移动中的金条。
4. 失败后用射线操作复活与结束按钮，结束后打开设置并重新开始；确认每次点击只执行一次，重新开始后高度归零。
5. 在相同设备和测试场景下对比旧版与新版帧率；性能采样使用诊断版，最终体验复测使用正式版。

## 仓库内容与忽略项

源码包括游戏逻辑、PICO 场景构建器、场景、模型与配置。`Library/`、`Temp/`、`Logs/`、`UserSettings/`、构建产物、APK、签名文件及 `Packages/com.bytedance.pico.xr/` 均不会提交。
