# MiPlay Cast

把 Windows 正在播放的声音投到局域网音箱。

项目同时支持普通 DLNA 音箱和小米音箱的 MiPlay 音频路径。DLNA 的兼容面更广，MiPlay 的延迟通常更低，但也更依赖具体型号和固件。它目前仍是一个面向自用和实验的 Windows 工具，不是通用投屏 SDK。

单音箱 MiPlay 已在小米音箱 LX06、固件 1.94.13 上实际听到声音。其他型号以及双音箱同步仍需要按设备验证。

## 能做什么

- 投送整个 Windows 输出设备的声音，或者只投送某个应用及其子进程。
- 在 DLNA 和小米妙播（MiPlay）之间切换。
- 选择一台音箱播放；也可以进入双音箱模式，为两台设备分别指定位置。
- 在应用里调节音箱音量，查看连接状态、投送时长和传输诊断。
- 关闭主窗口后继续在通知区运行，并从托盘快速切换音箱。
- 配合项目内的虚拟音频驱动，让电脑本地不出声，只在音箱播放。

## 开始使用

使用条件：

- Windows 11 x64，Build 22000 或更高版本
- 电脑和音箱位于同一局域网

从源码运行还需要 .NET 10 SDK；MSIX 是自包含版本，不需要单独安装 .NET。

如果只用 DLNA，不需要另外安装 FFmpeg。MiPlay 需要一个带 `aac_mf` 编码器的 `ffmpeg.exe`，可以先检查：

```powershell
ffmpeg -hide_banner -encoders | findstr aac_mf
```

程序会依次从 `DLNACAST_FFMPEG` 环境变量、应用目录和 `PATH` 中查找 FFmpeg。

从源码运行：

```powershell
dotnet restore DLNACast.slnx
dotnet run --project src/DLNACast.App/DLNACast.App.csproj -p:Platform=x64
```

进入应用后：

1. 选择 DLNA 或小米妙播。
2. 选择“输出设备全部声音”或“应用及其子进程”。
3. 选择要播放的音箱。双音箱模式下再指定两台设备。
4. 按需开启“仅音箱播放”。
5. 勾选音箱后会自动开始投送；取消选择即可停止对应音箱。

应用不会替你修改 Windows 的网络类型。发现不到音箱时，先确认网络是“专用网络”，再检查电脑和音箱是否真的处于同一网段；手动运行未打包版本时，也要允许 Windows 防火墙放行应用。

## DLNA 和 MiPlay 的区别

DLNA 走标准的 UPnP MediaRenderer 路径，优先发送 PCM/WAV；设备不接受或拉流失败时，可以回退到 MP3。它更容易兼容不同品牌的网络音箱，但最终延迟很大一部分取决于音箱固件自己的缓冲策略。

双音箱 DLNA 会把左、右声道分别转成单声道，发送给两台音箱。这个功能不会把两台独立设备变成严格同步的专业音频系统，同型号音箱通常更合适。

MiPlay 通过小米音箱的私有 AAC/WFD 音频路径发送，依赖 FFmpeg 的 `aac_mf` 编码器。单音箱主路径已经在 LX06 上验证可听；单应用捕获、不同固件以及双音箱仍应视为实验功能。当前 MiPlay 双音箱模式让两台设备共享同一份采集音频，并分别建立会话，不做 DLNA 那样的左右声道拆分。

## “仅音箱播放”需要虚拟驱动

“仅音箱播放”会把声音临时路由到 `DLNA Cast Virtual Speaker`，因此必须先安装项目内的虚拟音频驱动。没有安装驱动时，请关闭这个开关，普通投送仍然可以使用。

系统音频模式会临时切换 Windows 的默认输出；单应用模式只改变所选应用的输出路由。正常停止投送或退出应用时，程序会恢复之前的设置。

驱动目前是测试签名版本，安装需要 Windows 测试签名模式并重启。构建和安装方法见 [虚拟音频驱动说明](drivers/DLNACast.VirtualSpeaker/README.md)。驱动不会随 MSIX 自动安装。

## 构建和测试

```powershell
dotnet restore DLNACast.slnx
dotnet build DLNACast.slnx -c Release -p:Platform=x64
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Release -p:Platform=x64
```

`tools/DLNACast.Probe` 是协议研究和真机排查工具，正常使用应用时不需要运行。它的一些参数会主动连接设备或发送协议帧，不应当作普通的只读诊断命令使用。

## 生成 MSIX

打包脚本会发布自包含的 x64 WinUI 3 应用，并使用开发证书签名：

```powershell
$password = Read-Host '测试 PFX 密码' -AsSecureString
.\scripts\package-msix.ps1 -ExportPassword $password
```

首次安装时，需要在管理员 PowerShell 中信任开发证书，再安装生成的包：

```powershell
Import-Certificate `
  -FilePath .\artifacts\signing\DLNACast.Development.cer `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople

Add-AppxPackage .\artifacts\DLNACast.Windows_0.2.4.0_x64.msix
```

证书如果只导入“当前用户”，安装时可能出现 `0x800B0109` 或 `0x80073CF0`。MSIX 只为专用网络注册 TCP 49555–49565 入站规则，卸载应用后由 Windows 移除。

## 已知边界

- 界面显示的缓冲量不是从电脑到人耳的完整延迟，音箱固件还会继续缓冲。
- MiPlay 是按真实设备行为适配的私有协议，不能保证所有小米音箱和固件都能使用。
- 双音箱播放目前不承诺严格同步，尤其不适合对相位和延迟敏感的场景。
- 项目不支持 AirPlay、QPlay、系统级多房间编组，也没有发布到 Microsoft Store。

配置和滚动日志写入 MSIX 的 `LocalState`；未打包运行时写入 `%LOCALAPPDATA%\DLNACast`。程序不保存原始音频，也不发送遥测。
