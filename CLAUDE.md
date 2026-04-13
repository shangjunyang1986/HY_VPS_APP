# VPS_app Unity 项目文档

## 1. 项目概述

这是 HY_VPS 系统的 **Android 客户端**，基于 Unity 构建，运行在 vivo S6 真机上。  
提供两大功能模式：

1. **定位模式（Localize）**：通过相机采集图像发送到 VPS 后端，获取 6DoF 世界坐标位姿，驱动 AR 虚实叠加
2. **扫描模式（Scan）**：采集场景帧图像，上传至后端触发建图，生成可用于定位的地图

**项目路径**：`F:\Projects\VPS_app`  
**Unity 版本**：6000.4.1f1  
**目标平台**：Android  
**应用名称**：VPS AR  
**公司名**：HY_VPS

---

## 2. 后端依赖

本 App 连接 VPS 后端服务（默认 `http://192.168.1.8:8000`），后端项目位于 `F:\ai-projects\HY_VPS`。

| 功能 | 调用接口 |
|------|---------|
| 开始定位会话 | `POST /session/start` |
| 发送帧定位 | `POST /session/frame` |
| 结束会话 | `POST /session/end` |
| 重定位 | `POST /session/relocalize` |
| 创建采集任务 | `POST /api/v2/captures` |
| 上传素材包 | `POST /api/v2/captures/{id}/upload` |
| 创建地图 | `POST /api/v2/maps` |
| 查询地图状态 | `GET /api/v2/maps/{id}` |

---

## 3. 关键依赖包

| 包 | 版本 | 用途 |
|----|------|------|
| `com.unity.xr.arfoundation` | 6.5.0 | AR 基础框架（ARCore 集成） |
| `com.unity.xr.arcore` | 6.5.0 | Google ARCore 支持 |
| `com.easyar.sense` | 本地包 | EasyAR SDK — VIO 跟踪 + 相机采集（ARCore 不可用时的 fallback） |
| `com.unity.render-pipelines.universal` | 17.4.0 | URP 渲染管线 |
| `com.unity.inputsystem` | 1.19.0 | 新输入系统 |
| `com.coplaydev.unity-mcp` | git | MCP for Unity — 编辑器远程控制 |

---

## 4. 场景结构

只有一个场景：`Assets/Scenes/SampleScene.unity`

### 场景层级

```
SampleScene
├── Directional Light          — 主光源
├── Global Volume              — URP 后处理
├── VPS Manager                — 定位模式核心
│   ├── VpsClient              — 后端 HTTP 通信（session API）
│   ├── ArPoseHandler          — VIO 跟踪 + VPS 触发 + 融合
│   └── VpsDebugUI             — 左上角 Debug HUD
├── VPS Anchor - Cube          — AR 虚拟物体（立方体）
├── VPS Anchor - Sphere        — AR 虚拟物体（球体）
├── VPS Anchor - Cylinder      — AR 虚拟物体（圆柱）
├── XR Origin                  — ARCore XR 原点
│   └── Camera Offset / Main Camera
├── AR Session                 — ARCore 会话管理
├── EasyAR Session             — EasyAR 会话（含 FrameRecorder 等）
│   ├── EasyARController
│   ├── FrameRecorder / FramePlayer
│   ├── CameraImageRenderer
│   └── EasyARTracker          — EasyAR VIO 跟踪器
└── Scan Manager               — 扫描模式核心
    ├── VpsScanClient           — 后端 HTTP 通信（v2 API）
    ├── VpsScanManager          — 扫描流程状态机
    └── VpsScanUI               — 右侧扫描控制面板
```

---

## 5. 脚本架构

所有脚本在 `Assets/Scripts/VPS/` 目录下，命名空间 `VPS`。

### 5.1 定位模式

| 脚本 | 职责 |
|------|------|
| **VpsClient.cs** | HTTP 客户端，负责 `/session/*` 接口通信。管理 session 生命周期（start/frame/end）。发送 base64 编码图像 + 内参 + VIO 位姿，接收 VPS pose。通过事件 `OnPoseReceived` / `OnFusedPoseReceived` / `OnTrackingStateChanged` 通知上层。 |
| **ArPoseHandler.cs** | 核心控制器。每帧读 VIO 位姿 → 应用客户端融合 → 检查 VPS 触发条件 → 发送帧。三级相机采集策略：ARCore → EasyAR → WebCam+Gyro 自动降级。负责相机内参计算（按来源区分 ARCore/EasyAR/WebCam/Editor）。 |
| **VpsTriggerController.cs** | VPS 请求触发策略。基于状态机（INIT → VPS_LOCKED → VPS_DEGRADED → RELOCALIZING）和漂移估计动态调整请求间隔（正常 2s / 快速 1s）。连续失败 3 次自动降级。 |
| **ClientFusion.cs** | 客户端融合。计算 VPS 世界坐标与 VIO 本地坐标的偏移量，每帧用指数平滑插值应用到 VIO 位姿上，实现 60fps 平滑定位。 |
| **VpsAnchor.cs** | AR 锚点。将虚拟物体放置在 VPS 世界坐标上，支持平滑插值避免跳变。挂在场景中的 Cube/Sphere/Cylinder 上。 |
| **VpsDebugUI.cs** | 左上角 OnGUI Debug HUD。显示连接状态、跟踪状态、位姿数据、内参来源与数值、延迟/FPS。DPI 自适应（基于 160 DPI 基线缩放）。 |

### 5.2 相机采集

| 脚本 | 职责 |
|------|------|
| **EasyARTracker.cs** | EasyAR SDK 封装。订阅 `easyar.ARSession.InputFrameUpdate` 获取帧数据。提供 `TryGetCameraImage()`（缩放到 captureWidth）和 `TryGetIntrinsics()`。支持多种像素格式转换（RGBA/RGB/BGR/BGRA/Gray/NV21/NV12/I420/YV12）。还维护 `BackgroundTexture` 用于 AR 背景显示。 |
| **CameraBackground.cs** | 将 EasyAR 相机画面显示在 UI RawImage 上作为 AR 背景。 |

### 5.3 扫描模式

| 脚本 | 职责 |
|------|------|
| **VpsScanClient.cs** | v2 API HTTP 客户端。负责 `/api/v2/captures` 和 `/api/v2/maps` 系列接口。支持 multipart 上传（ZIP + intrinsics + meta）。解析结构化 v2 错误响应。 |
| **VpsScanManager.cs** | 扫描流程状态机（Idle → Scanning → Packaging → Uploading → Building → Done/Error）。每 0.5 秒从 EasyARTracker 获取帧存为 JPEG（最大 200 帧）。停止后自动 ZIP 打包 → 创建 capture → 上传 → 创建 map → 轮询状态。 |
| **VpsScanUI.cs** | 右侧 OnGUI 扫描控制面板。右上角 Localize/Scan 模式切换按钮。显示扫描状态、帧数、进度消息。按钮：Start Scan / Stop & Upload / Cancel / Use This Map。切换模式时自动暂停/恢复定位。 |

### 5.4 数据模型（定义在 VpsClient.cs 和 VpsScanClient.cs 中）

**定位相关**：
- `VpsPose` — position[3] + rotation[4] + timestamp + confidence
- `Intrinsics` — fx, fy, cx, cy
- `VioPoseInput` — position[3] + rotation[4] + timestamp
- `SessionFrameRequest/Response` — 帧请求与响应

**扫描相关**：
- `CaptureCreateResp` / `CaptureUploadResp` — v2 采集接口响应
- `MapCreateResp` / `MapStatusResp` — v2 地图接口响应
- `V2Error` / `V2ErrorResponse` — v2 结构化错误

---

## 6. Editor 脚本

位于 `Assets/Editor/`，用于构建配置：

| 脚本 | 用途 |
|------|------|
| `AndroidBuildSetup.cs` | Android 构建参数设置 |
| `BuildAPK.cs` | 一键构建 APK |
| `EnableARCoreSetup.cs` | 启用 ARCore |
| `SetupXRLoader.cs` | 配置 XR Loader |
| `SetupEasyARSettings.cs` | 配置 EasyAR License Key |
| `FixGameActivity.cs` | 修复 GameActivity 兼容性 |

---

## 7. 相机采集降级策略

App 启动时按优先级尝试：

```
1. ARCore（ARSession + ARCameraManager）
   ↓ 不可用或 3 次采集失败
2. EasyAR（MotionTrackerFrameSource — 6DoF VIO）
   ↓ 不可用
3. WebCam + Gyro（仅旋转，无位移跟踪）
   ↓ 无摄像头
4. Editor RenderTexture（仅编辑器内调试）
```

在 vivo S6 上实际运行使用 **EasyAR 模式**。

---

## 8. 坐标系约定

- VPS 后端使用**世界坐标系**，四元数顺序 `[qw, qx, qy, qz]`
- Unity 使用 `Quaternion(qx, qy, qz, qw)` — 转换时需交换
- EasyAR 输出的图像为**横向（landscape）**但内容旋转了 90° CCW，后端会做 90° CW 旋转 + 水平翻转修正
- 客户端内参优先，后端仅在缺失时 fallback 到 COLMAP 内参

---

## 9. 关键配置

| 配置项 | 位置 | 默认值 |
|--------|------|--------|
| VPS 服务器地址 | VpsClient.serverUrl / VpsScanClient.serverUrl | `http://192.168.1.8:8000` |
| 定位地图 ID | VpsClient.mapId | `default_map` |
| VPS 请求间隔 | ArPoseHandler.normalInterval / fastInterval | 2.0s / 1.0s |
| 漂移阈值 | ArPoseHandler.driftThreshold | 0.5m |
| 融合平滑速度 | ArPoseHandler.fusionSmoothSpeed | 8.0 |
| 采集图像宽度 | ArPoseHandler.captureWidth / EasyARTracker.captureWidth | 640 |
| 扫描帧间隔 | VpsScanManager.captureIntervalSec | 0.5s |
| 扫描最大帧数 | VpsScanManager.maxFrames | 200 |
| 扫描 JPEG 质量 | VpsScanManager.jpegQuality | 85 |
| 设备型号 | VpsScanManager.deviceModel | `vivo_s6` |

---

## 10. 构建说明

1. 确保 Unity 6000.4.x 已安装 Android Build Support
2. 通过菜单 `File > Build Settings` 切换到 Android 平台
3. EasyAR License Key 需在 `Project Settings > EasyAR` 中配置
4. 使用 `BuildAPK.cs` 编辑器脚本或手动 Build And Run
5. 目标设备：vivo S6（Android，已验证 EasyAR 兼容性）

---

## 11. 完整使用流程

### 定位流程
```
App 启动 → 自动初始化相机（ARCore/EasyAR/WebCam）
→ 自动连接 VPS 后端开始 session
→ 每帧 VIO 跟踪 + 融合
→ 定时发送 VPS 请求 → 接收位姿 → 更新 AR 物体
```

### 扫描建图流程
```
点击右上角 [Scan] 切换模式
→ [Start Scan] → 每 0.5s 保存 EasyAR 帧为 JPEG + 记录内参
→ [Stop & Upload] → ZIP 打包 → 创建 capture → 上传 → 建图 → 轮询
→ 建图完成 → [Use This Map] → 设置 mapId → 切回定位模式
```

---

## 12. 修改须知

- 所有脚本在 `VPS` 命名空间下
- UI 统一使用 OnGUI + DPI 缩放模式（不使用 UGUI Canvas）
- 组件间通过 `FindAnyObjectByType<T>()` 自动关联，Inspector 中也可手动赋值
- 定位相关配置在 `VPS Manager` GameObject 上修改
- 扫描相关配置在 `Scan Manager` GameObject 上修改
- 服务器地址需在 **两个** 组件上同时修改（VpsClient + VpsScanClient）
- 不要修改 Pose 定义（position[3] + rotation[4]）和 API 请求/响应结构
