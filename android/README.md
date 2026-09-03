# MiPlay Cast Android

[Android MiPlay 投送协议完整移植计划](../docs/android-miplay-full-port-plan.md)

这是 Windows 版的 Android 移植工程。应用默认走 MiPlay，DLNA 作为兼容模式保留。

## 已完成

- 同时发送 MiPlay `_mi-connect._udp.local` / `_lyra-mdns._udp.local` mDNS 和 DLNA SSDP 查询；纯 MiPlay 音箱不再依赖 DLNA 描述才能出现
- 按 Windows 端已经验证的 Legacy S12 顺序连接 8899，完成安全挑战、设备信息、播放源和 Open 控制
- 在手机上提供反向 RTSP、辅助连接、音频连接和 40 字节 UDP 时钟同步
- 使用 Android MediaCodec 编码 48 kHz 双声道 AAC-LC，并按既有实现封装为 ADTS、MPEG-TS、RTP 和 MiPlay TCP 帧
- 保留抓包得到的首包突发、21.333 ms 稳态节奏、90 kHz 时间戳和 PAT/PMT 刷新位置
- 读取设备描述并解析 AVTransport、RenderingControl 和 ConnectionManager 地址
- 使用 Android 10 起提供的 AudioPlaybackCapture 捕获 48 kHz、16 位、立体声播放音频
- 可捕获全部可录制媒体，也可按应用 UID 只捕获一个应用；DRM、通话或主动禁止捕获的应用仍会保持静音
- MiPlay 组会话支持动态加入和退出；共享一条 AAC/PTS 时间线，成员各自维护发送队列和封包状态。协议上限为 63 台，真实可用数量仍需按设备和网络验收
- 支持 0–2000 ms MiPlay 时间偏移、暂停/继续、停止、总音量和每台音箱独立音量，控制项同时出现在应用和前台服务通知中
- 协议、音源、音箱、声道路由和播放延迟会在本机保存
- DLNA 模式在手机上启动 PCM/WAV HTTP 直播，并通过 UPnP `SetAVTransportURI`、`Play`、`Stop` 控制音箱
- 使用前台服务维持投送，通知栏可以暂停、继续和停止
- 适配浅色、深色主题以及手机/平板宽度

## 系统限制

Android 不允许应用无条件录制所有系统声音。每次投送都要由用户确认系统捕获授权，并授予录音权限。只能捕获用途为媒体、游戏或未知，且播放应用允许被捕获的音频；DRM 内容、通话、通知以及主动禁止捕获的应用会是静音。

当前实际播放入口与 Windows 版相同，使用已经在 LX06 上验证的 legacy-clear 8899/WFD 路径。仓库里的现代 SafetyAuth/S12、Continuity Binder 取证代码并不是 Windows 主界面当前使用的可播放传输，因此 Android 端没有把这些静态探针伪装成播放功能。

Windows 的声学延迟按钮会主动向音箱播放探测声。Android 版已删除自动启动测试音，本版本不会在开始投送时插入任何测试信号；延迟只提供手动时间偏移设置。Windows 的本地扬声器静音依赖 Windows 音频策略，Android 的公开 AudioPlaybackCapture API不能替其他应用静音本机输出。

## SystemUI 投送设置

在应用首页右上角打开“设置”，可以配置从系统媒体输出面板发起的 MiPlay 投送：

- 显示音箱：关闭后不再搜索或连接新音箱，已连接的音箱仍保留音量和停止操作。
- 缓冲延迟：0–2000 ms，默认 1000 ms。这是发送端时间偏移，不是实测播放延迟。
- 声道输出：原始立体声、仅左声道、仅右声道。各音箱使用相同输出；分别播放左右声道仍在主界面的立体声拆分中配置。
- 模块与 Root：查看投送状态、作用域说明，并手动请求 Root 重启 SystemUI。重启前会二次确认。

这些参数与主界面分别保存。开关不需要重启，音频参数在下次新建投送时生效；向正在投送的会话加入第二台音箱时，沿用该会话的参数。

当前入口使用 Root Remote Submix 系统内录，不弹出 MediaProjection 授权框，也不支持按应用 UID 筛选。首次使用需要在 Root 管理器中允许本应用。首次启用 LSPosed 模块或更新 APK 后，需要重新加载“系统界面”作用域。

## 构建

在 `android` 目录运行：

```powershell
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug
```

调试 APK 输出到 `app\build\outputs\apk\debug\app-debug.apk`。

## 真机验收

1. 手机和音箱连接同一 Wi-Fi，关闭访客网络/AP 隔离。
2. 安装 APK，选择一台或多台搜索到的音箱；需要左右声道拆分时再开启“立体声拆分”。
3. 保持默认的 MiPlay，在“播放设备”中勾选音箱；应用会像 Windows 版一样立即进入投送流程，并依次请求录音和系统音频捕获授权。
4. 投送后，暂停、停止、整组音量和单音箱音量都在“播放设备”区域操作。MiPlay 播放中勾选或取消音箱会在当前会话动态加入或移除，不重新申请系统音频捕获授权。
5. 在支持被捕获的音乐或视频应用里播放内容。
6. 确认状态依次经过 8899 初始化、反向 RTSP、辅助连接、音频连接和首个 AAC 单元。
7. 状态里的 `PCM nonZero` 必须持续增加且 `peak` 大于 0；如果两项一直为 0，说明所选应用不允许 Android 捕获，不是 MiPlay 网络链路已坏。
8. 实际听音并确认播放至少持续 30 秒，再验证暂停/继续和音量；回到应用或点击通知里的“停止”结束。

静态构建和单元测试会校验首个 MiPlay 媒体包与 Windows 端字节向量一致，但连接成功、`state=2` 或持续发送都不能代替实际听音。每次改动后仍要用真实音箱验收声音和延迟。
