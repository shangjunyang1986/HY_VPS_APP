# HY_VPS_APP

VPS (Visual Positioning System) AR 应用的 Android 客户端，基于 Unity 构建。

![Unity](https://img.shields.io/badge/Unity-6000.4.1f1-black?logo=unity)
![Platform](https://img.shields.io/badge/Platform-Android-green?logo=android)
![AR](https://img.shields.io/badge/AR-ARCore%20%7C%20EasyAR-blue)

## 功能特性

本应用提供两大核心功能：

### 1. 定位模式 (Localize)
- 通过相机采集图像发送到 VPS 后端
- 获取 6DoF 世界坐标位姿
- 驱动 AR 虚拟物体（立方体、球体、圆柱）进行虚实叠加
- 支持客户端融合平滑插值，实现 60fps 流畅定位

### 2. 扫描模式 (Scan)
- 采集场景帧图像（每 0.5 秒一帧，最多 200 帧）
- 自动 ZIP 打包上传至后端
- 触发建图流程，生成可用于定位的地图
- 实时轮询建图状态

## 技术架构

### 核心组件

| 组件 | 说明 |
|------|------|
| **VpsClient** | HTTP 客户端，负责 `/session/*` 接口通信 |
| **ArPoseHandler** | 核心控制器，VIO 跟踪 + VPS 触发 + 融合 |
| **VpsDebugUI** | 左上角 Debug HUD，显示状态和位姿数据 |
| **EasyARTracker** | EasyAR SDK 封装，相机采集和 VIO 跟踪 |
| **VpsScanClient** | v2 API HTTP 客户端，扫描建图功能 |
| **VpsScanManager** | 扫描流程状态机 |
| **VpsScanUI** | 扫描模式控制面板 |

### 场景结构

```
SampleScene
├── VPS Manager          # 定位模式核心 (VpsClient, ArPoseHandler, VpsDebugUI)
├── VPS Anchor - Cube    # AR 虚拟物体
├── VPS Anchor - Sphere  # AR 虚拟物体
├── VPS Anchor - Cylinder# AR 虚拟物体
├── XR Origin            # ARCore XR 原点
├── AR Session           # ARCore 会话管理
├── EasyAR Session       # EasyAR 会话 (备用)
└── Scan Manager         # 扫描模式核心 (VpsScanClient, VpsScanManager, VpsScanUI)
```

### 相机采集降级策略

```
1. ARCore (ARSession + ARCameraManager)  ← 优先
   ↓ 不可用时
2. EasyAR (MotionTrackerFrameSource - 6DoF VIO)
   ↓ 不可用时
3. WebCam + Gyro (仅旋转，无位移跟踪)
   ↓ 无摄像头时
4. Editor RenderTexture (仅编辑器调试)
```

## 依赖包

| 包名 | 版本 | 用途 |
|------|------|------|
| `com.unity.xr.arfoundation` | 6.5.0 | AR 基础框架 |
| `com.unity.xr.arcore` | 6.5.0 | Google ARCore 支持 |
| `com.easyar.sense` | 本地包 | EasyAR SDK - VIO 跟踪 |
| `com.unity.render-pipelines.universal` | 17.4.0 | URP 渲染管线 |
| `com.unity.inputsystem` | 1.19.0 | 新输入系统 |

## 前置要求

- **Unity**: 6000.4.1f1 或兼容版本
- **Android Build Support**: 已安装
- **EasyAR License Key**: 需在 `Project Settings > EasyAR` 中配置
- **目标设备**: vivo S6 (已验证兼容性)

## 安装步骤

1. **克隆项目**
   ```bash
   git clone https://github.com/shangjunyang1986/HY_VPS_APP.git
   ```

2. **安装依赖**
   - 打开 Unity，等待 Package Manager 自动安装依赖
   - 手动安装 EasyAR SDK (如需)

3. **配置 EasyAR**
   - 打开 `Project Settings > EasyAR`
   - 填入 License Key

4. **配置后端地址**
   - 修改 `VpsClient.serverUrl` 和 `VpsScanClient.serverUrl`
   - 默认：`http://192.168.1.8:8000`

## 构建说明

1. 打开 `File > Build Settings`
2. 切换到 **Android** 平台
3. 选择构建方式：
   - 使用 `BuildAPK.cs` 编辑器脚本一键构建
   - 或手动点击 **Build And Run**

## 使用流程

### 定位流程
```
App 启动 → 自动初始化相机
→ 自动连接 VPS 后端开始 session
→ 每帧 VIO 跟踪 + 融合
→ 定时发送 VPS 请求 → 接收位姿 → 更新 AR 物体
```

### 扫描建图流程
```
点击右上角 [Scan] 切换模式
→ [Start Scan] → 每 0.5s 保存 EasyAR 帧为 JPEG
→ [Stop & Upload] → ZIP 打包 → 创建 capture → 上传 → 建图 → 轮询
→ 建图完成 → [Use This Map] → 设置 mapId → 切回定位模式
```

## 关键配置

| 配置项 | 默认值 | 修改位置 |
|--------|--------|----------|
| VPS 服务器地址 | `http://192.168.1.8:8000` | VpsClient / VpsScanClient |
| 定位地图 ID | `default_map` | VpsClient.mapId |
| VPS 请求间隔 | 2.0s (正常) / 1.0s (快速) | ArPoseHandler |
| 漂移阈值 | 0.5m | ArPoseHandler.driftThreshold |
| 扫描帧间隔 | 0.5s | VpsScanManager.captureIntervalSec |
| 扫描最大帧数 | 200 | VpsScanManager.maxFrames |

## 后端接口

| 功能 | 接口 |
|------|------|
| 开始定位会话 | `POST /session/start` |
| 发送帧定位 | `POST /session/frame` |
| 结束会话 | `POST /session/end` |
| 重定位 | `POST /session/relocalize` |
| 创建采集任务 | `POST /api/v2/captures` |
| 上传素材包 | `POST /api/v2/captures/{id}/upload` |
| 创建地图 | `POST /api/v2/maps` |
| 查询地图状态 | `GET /api/v2/maps/{id}` |

## 项目结构

```
VPS_app/
├── Assets/
│   ├── Scenes/
│   │   └── SampleScene.unity      # 主场景
│   ├── Scripts/
│   │   └── VPS/                   # 核心脚本
│   │       ├── VpsClient.cs
│   │       ├── ArPoseHandler.cs
│   │       ├── VpsDebugUI.cs
│   │       ├── VpsScanClient.cs
│   │       ├── VpsScanManager.cs
│   │       ├── VpsScanUI.cs
│   │       └── EasyARTracker.cs
│   ├── Editor/                    # 编辑器脚本
│   └── Packages/                  # NuGet 包 (非 Unity Packages)
├── Packages/                      # Unity 包 (需通过 UPM 安装)
└── README.md
```

## 注意事项

- **Packages 目录**: 已从 git 跟踪中排除，clone 后需通过 Unity Package Manager 安装依赖
- **EasyAR SDK**: 需单独配置 License Key
- **后端服务**: 确保 VPS 后端服务已启动并可访问
- **真机测试**: 推荐使用 vivo S6 或兼容 ARCore 的设备

## 许可证

Copyright HY_VPS. All rights reserved.
