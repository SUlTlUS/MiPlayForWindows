# DLNA / MiPlay Cast for Windows

Windows 11 x64 上的实时音频投送工具。应用保留原有 DLNA PCM/WAV 与 320 kbps MP3 回退路径，同时提供已在 LX06 1.94.13 上验证可听、支持系统总音频与单独应用捕获的实验性 MiPlay 低延迟音频路径。

## 当前功能

- 原生 WinUI 3 主窗口、Mica 背景、远端音量和投送诊断。
- 通知区图标直接通过 Windows Shell API 注册，右键菜单也是 WinUI 3 窗口，不加载 WPF/WinForms UI 栈。
- WASAPI 共享模式系统回环，以及基于 `ActivateAudioInterfaceAsync` 的进程树回环。
- 投送期间自动静音本机输出端点；停止、失败或退出时恢复投送前的静音状态。
- 统一输出 44.1 kHz、16-bit、双声道、20 ms PCM 帧；无声时按时钟补静音。
- PCM 以 60 ms 为抗抖目标、100 ms 为硬上限；音箱发起 GET 时会丢弃过时积压，再以严格的 20 ms 节拍发送 PCM/MP3 输入。
- Rssdp 主动发现 MediaRenderer 1–3，自行解析设备 XML、DIDL-Lite 和 SOAP。
- 仅向所选音箱 IP 提供随机令牌直播 URL，监听由实际路由确定的 LAN 地址和 TCP 49555–49565。
- PCM/WAV 优先，协议协商或拉流失败时回退 MP3；播放异常按 0.5/1/2 秒重试。
- 公用网络安全门：不会提权或自动修改 Windows 网络类别。
- 主界面可在 DLNA 与 MiPlay 间显式选择；设备仍按 SSDP UDN 记忆，再使用本次发现得到的当前 IP，避免把 DHCP 地址当作稳定身份。
- MiPlay 使用 Windows 系统或单独应用回环捕获、Media Foundation AAC-LC 48 kHz 双声道 256 kbps、MPEG-TS/RTP/WFD；单次最长 10 分钟。
- MiPlay 保留真机成功账本，不发送 Pause、Resume 或 AddMirror，不自动重试、回退或切换目标；停止时取消会话并关闭自有连接。

## 开发与验证

需要 Windows 11 Build 22000 或更高版本，以及 .NET 10 SDK。界面使用 Windows App SDK 2.2 / WinUI 3。MiPlay 还需要带 `aac_mf` 的 `ffmpeg.exe`：程序依次检查 `DLNACAST_FFMPEG`、应用目录和 `PATH`。

```powershell
dotnet restore DLNACast.slnx
dotnet build DLNACast.slnx -c Debug -p:Platform=x64
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Debug -p:Platform=x64
dotnet run --project tools/DLNACast.Probe/DLNACast.Probe.csproj -- --capture-smoke
dotnet run --project tools/DLNACast.Probe/DLNACast.Probe.csproj -- --process-smoke
dotnet run --project src/DLNACast.App/DLNACast.App.csproj -p:Platform=x64
```

真实投送前，Windows 中音箱所在的家庭网络必须设为“专用网络”。DLNA 系统混音模式会静音所选输出设备，进程模式会静音默认多媒体输出设备；MiPlay 当前不修改本机静音状态。应用不安装虚拟声卡。Probe 中的真机参数具有显式确认门，不应当作只读命令使用。

## 生成和安装 x64 MSIX

脚本会选择本机最新的 x64 `MakeAppx.exe` / `SignTool.exe`，生成自包含 WinUI 3 应用并签名。

```powershell
$password = Read-Host '测试 PFX 密码' -AsSecureString
.\scripts\package-msix.ps1 -ExportPassword $password
```

首次安装开发签名包时，必须用“管理员 PowerShell”把证书放入本地计算机的“受信任的人”。仅导入“当前用户”会导致 `0x800B0109` / `0x80073CF0`。

```powershell
Import-Certificate `
  -FilePath .\artifacts\signing\DLNACast.Development.cer `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople

Add-AppxPackage .\artifacts\DLNACast.Windows_0.2.4.0_x64.msix
```

清单身份为 `DLNACast.Windows`，版本 `0.2.4.0`，发布者 `CN=DLNACast Development`。安装时只为 Private profile 注册 TCP 49555–49565 入站规则，卸载包时由 Windows 移除。

## 延迟边界

界面中的缓冲数值不是声学端到端延迟。DLNA MediaRenderer 会在固件内再次缓冲 HTTP 媒体；MiPlay 则使用音箱的私有 AAC/WFD 接收路径，已证明可实际发声，但主应用的 10 分钟生命周期、手动停止和错误恢复仍需单独真机验收。

## 数据和隐私

配置与滚动日志写入 MSIX `LocalState`（未打包运行时回退到 `%LOCALAPPDATA%\DLNACast`）。应用不保存原始音频，也不发送遥测。一次只连接一台音箱，不提供多房间同步、AirPlay、QPlay、自启动或 Microsoft Store 发布。
