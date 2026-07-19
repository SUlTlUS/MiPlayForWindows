# DLNA Cast for Windows

Windows 11 x64 上的实时 DLNA 音频投送工具。应用可以捕获某个输出设备的系统混音，或捕获单个进程及其子进程的音频，然后将连续 PCM/WAV 流提供给一台局域网 UPnP MediaRenderer。PCM 无法建立时会自动回退到 320 kbps CBR MP3。

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

## 开发与验证

需要 Windows 11 Build 22000 或更高版本，以及 .NET 10 SDK。界面使用 Windows App SDK 2.2 / WinUI 3。

```powershell
dotnet restore DLNACast.slnx
dotnet build DLNACast.slnx -c Debug -p:Platform=x64
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Debug -p:Platform=x64
dotnet run --project tools/DLNACast.Probe/DLNACast.Probe.csproj -- --capture-smoke
dotnet run --project tools/DLNACast.Probe/DLNACast.Probe.csproj -- --process-smoke
dotnet run --project src/DLNACast.App/DLNACast.App.csproj -p:Platform=x64
```

探针只做只读发现和捕获冒烟验证，不会开始向音箱投送。真实投送前，Windows 中音箱所在的家庭网络必须设为“专用网络”。系统混音模式会静音所选输出设备；进程模式会静音默认多媒体输出设备。应用不安装虚拟声卡，停止投送后会恢复设备原来的静音状态。

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

界面中的延迟数值是应用 PCM 缓冲，不是声学端到端延迟。小爱音箱等 DLNA MediaRenderer 会在固件内再次缓冲 HTTP 媒体，这部分不受应用控制。如果 0.2 版的应用缓冲已稳定在 60–100 ms，但实际声音仍延后数秒，主要瓶颈就在音箱 DLNA 固件；要达到类似 AirPlay 的延迟，需要音箱支持的低延迟私有协议或可控的接收端。

## 数据和隐私

配置与滚动日志写入 MSIX `LocalState`（未打包运行时回退到 `%LOCALAPPDATA%\DLNACast`）。应用不保存原始音频，也不发送遥测。一次只连接一台音箱，不提供多房间同步、AirPlay、QPlay、自启动或 Microsoft Store 发布。
