# 平板输出时间戳核对（2026-08-28）

## 结论

平板的 `fix pts` 确实会影响发给音箱的 PES PTS 和 RTP timestamp，并非只用于日志。
Windows 0.2.4.29 使用按 AAC 帧数递增的网络时间戳；0.2.4.30 为共享多音箱会话
接入了统一的输出 PTS 修正。同一帧只计算一次，再用于全部接收端的 RTP 和 PES。

这里必须区分两个时间：`PresentationTimeMicroseconds` 是 Windows 附带的采集时间，
不是 FFmpeg 输出的编码 PTS。直接用它套用平板公式，在 3 秒预热等待后曾出现
**2580.660 ms** 的候选 PTS 跳变。进一步用真实 `aac_mf -debug_ts` 检查：输入停顿
3 秒，32 帧输出 PTS 仍然是 `0、1024、2048……31744`，每次递增 1024 个采样。
因此新实现按编码样本数建立时间轴，不使用采集时间代替编码 PTS。

这仍是 Windows 适配，不是整个平板时钟链的完整复刻：首次媒体交付位于有序预热队列
之后，不是 Android 的 MediaCodec 回调；PCR、首帧 TIME_OFFSET、启动节奏和缓冲参数
没有在这一版替换。真实音箱的听感、同步和端到端延迟仍需实测。

## 核对对象

- Java：`artifacts/tablet-apks/2026-08-24/MiLinkOS2CN-jadx/sources/com/xiaomi/miplay/mylibrary/mirror/MicRecorder.java`。
- 原生库：`artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so`。
- 原生库 SHA-256：`934840C6ABBF872A7C214AE7C925A0D74ACE1D6B079BD26287FAA4AB9D33FACE`。

地址是这份 ARM64 ELF 的虚拟地址，不是其他版本库的通用偏移。
本次使用本地 `llvm-objdump --disassemble --demangle --no-show-raw-insn`
读取实际指令，核对动态符号及参数传递；没有把函数名或日志文字当作函数行为。
另以 ELF 动态重定位确认 `MultiMirrorDisplay` 虚表（`0x2f9e80`）的 `+0x78`
槽指向 `updateTimeOffset(long)`，对应 JNI 首写分支的虚调用。

## Java 到网络的路径

`MicRecorder.muxAudio()` 在第 396–433 行执行：

```text
首次输出：firstAudioTime = MediaCodec PTS；firstSysTime = elapsedRealtimeNanos / 1000
pts = MediaCodec PTS - firstAudioTime
elapsed = elapsedRealtimeNanos / 1000 - firstSysTime
behind = elapsed - pts
如果 behind > 50：firstAudioTime -= behind；重新计算 pts
WriteStream(false, ADTS AAC, pts)
```

这里的 `50` 是 **50 微秒**，不是 50 毫秒。修正是单向的：只把落后于输出时钟的
PTS 向前推，不会缩短已经超前的 PTS，也不会压平采集时间的前跳。

| 原生位置 | 指令证据与作用 |
| --- | --- |
| `MultiMirrorControl_writeData` `0x159004` | `0x159030` 保存 JNI 的 `jlong` 参数；`0x159090–0x159098` 原样传给 `audioSender::WritePacket`。首写分支 `0x15905c–0x159078` 把同一 PTS 交给显示组更新偏移。 |
| `audioSender::WritePacket` `0x14bc9c` | PTS 保存在 `x21`；主发送器和各 `multiAudioSender` 都收到同一值。对新加入的发送器还会先写历史 AAC，这不等同于 Windows 当前的固定启动组。 |
| `multiAudioSender::WritePacket` `0x14cddc` | 状态允许时将音频数据、大小和 PTS 原样交给媒体输入。 |
| `MirrorIO::Write` `0x1cbc18` | 输入 PTS 从 `x3` 保存在 `x27`，再复制到 `x28`；`0x1cc6c4–0x1cc6cc` 写入 ABuffer 元数据键 `0x11`。另一个键 `0x61` 是当前原生时钟，不是媒体 PTS。 |
| `MediaSender::queueAccessUnit` `0x1d2544` | `0x1d2704–0x1d2774` 从输入元数据取键 `0x11`，原样复制到 TS 输出元数据。 |
| `TSPacketizer::packetize` `0x1f7bb8` | `0x1f7c14–0x1f7c28` 取键 `0x11`；`0x1f8748–0x1f8770` 计算 `ptsUs * 9 / 100`；`0x1f8870` 附近将结果写入 PES PTS 字段。 |
| `RTPSender::queueTSPackets` `0x1e3974` | `0x1e3a00–0x1e3a14` 取同一元数据；`0x1e3ecc–0x1e3f04` 转为 90 kHz 并写入 RTP 头的第 4–7 字节。 |

因此，Java 输出修正后的时间不是被原生库重新按 AAC 帧数覆盖的。

## PCR 和 TIME_OFFSET 的不同来源

`TSPacketizer::packetize` 的 `0x1f860c–0x1f8674` 读取 `ALooper::GetNowUs()`，
乘以 27 得到 27 MHz PCR，再拆为 90 kHz base 和 0–299 的 extension。
这个分支没有使用 AAC 采集时间，也没有在 PCR 上减去播放缓冲。

`ALooper::GetNowUs()`（`0x225f78–0x226004`）读取时钟 ID 4，并减去首次调用时
保存的起点。它是库内的相对单调时钟，不能直接拿其数值与 Android Java 的
`elapsedRealtimeNanos()` 或 Windows Stopwatch 的绝对值比较。

`MultiMirrorDisplay::updateTimeOffset(long pts)`（`0x1d76d8`）则：

1. 读取选项 `0x10003d`；读取失败时使用 `1,000,000 us`。
2. 从 `TimerWork` 的 server clock 取得当前时间。
3. 在 `0x1d7730–0x1d7740` 计算 `serverNow - pts + playbackDelay`。
4. 将相同偏移写入组内各发送器。

继续追到 `ClockSync::GetCurTime()`（`0x224b20`），其输入确实是
`ALooper::GetNowUs()`；`ClockSync::CalculatTime()`（`0x224afc`）执行
`offset + trunc((localNow - localAnchor) * rate)`。因此不能把服务器校准时钟
与未校准 PCR 时钟简单视为同一个数值。

补充核对 `TimerWork::Init`（`0x2245d0–0x2247d8`）：未传入自定义回调时，
`0x2246a4` 后创建默认 ClockSync，初始化 `offset = 0、localAnchor = 0、rate = 1.0`。
server 分支随后创建 TimerService；其 `Init`（`0x2240b8–0x224244`）在 `0x224198`
保存这个回调。因此默认 Server 时钟初始为恒等映射，但这项证据带有“未传入自定义
回调”的条件，不能据此断言实际应用的所有会话都没有额外校准。

注意：默认 1 秒不表示所有设备实际选项都为 1 秒；还未核对完实际会话如何选择
ClockSync 实例、设置 rate/offset、处理后续校准，不能声称整个时钟同步链已对齐。

当前 Windows 在 RTSP PLAY 时传入单调时钟作为 TIME_OFFSET；发送时 PCR 使用
`captureTime - PlaybackDelayMicroseconds`。0.2.4.30 仅将共享 RTP/PES 切到下面的
输出时钟，单音箱仍按原有帧数规则递增。这不能称为上述三条时钟路径的逐项复刻。

## 第一阶段：排除采集元数据作为编码 PTS

参考模型：`tests/DLNACast.Tests/References/MiPlayTabletOutputTimeline.cs`。
它只编入测试，不随应用投送路径执行。

重放使用 Windows 真实 `aac_mf`、当前 `MiPlaySharedAudioSession`、两个独立编码路由，
以及三个订阅者（左 + 左 + 右）。PCM 是合成信号，没有向真实音箱发包。
保留现有 16 帧输入窗口、14 帧有序启动队列和发送节奏，等待 3 秒模拟握手。
候选公式在订阅者消费处运行，甚至把起点延后到正式消费首帧，仍然不能消除采集时间前跳。
这证明直接接线的风险，不是对 Android MediaCodec 回调排程的完整仿真。

```text
225 个同左声道 AAC 一致；225 个左右声道 AAC 不同；225 个共享采集 PTS 一致
启动后 AAC 断供计数：0 / 0 / 0
当前稳态编码内容年龄：378.8–409.2 ms
候选平板 PTS 最大单步：2580.660 ms
候选 PTS 相对当前网络 PTS 最大超前：2550.681 ms
fix 次数：0
```

时间测量会随调度波动。这次不能由此宣称真实音箱不会卡顿、已经同步，或实际延迟
就是上述内容年龄。

复现命令（本地已有的、被 Git 忽略的真实编码器探针）：

```powershell
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only
```

可进 CI 的 8 项确定性测试覆盖实际输入时间、50 微秒严格边界、修正的持续作用、
预热间断不会自动压平、三个路由共享时间戳，以及独立的 PCR/TIME_OFFSET 算术。
该阶段全量测试 746/746 通过，Release 构建为 0 警告、0 错误，未切换投送路径，
也未生成或安装新版本。

## 第二阶段：共享输出时钟（0.2.4.30）

实现位于 `MiPlaySharedOutputTimeline`，由整个共享会话持有，而不是每个发送线程
各持一份。原有 `aac_mf`、16 帧输入信用窗口、14 帧有序启动队列和发送节奏均保留。

1. 有序预热完成后，以第一帧实际交付给发送端的时刻为输出原点。
2. 编码 PTS 按总样本数计算：`floor(AU序号 × 1024 × 1000000 / 48000)`，
   避免长期反复累加截断后的 21333 微秒。
3. 相对输出时间落后超过 50 微秒时，只向前修正；同一 AU 的结果缓存并广播给全部音箱。
4. RTP 使用修正值的低 32 位，PES 使用低 33 位；会话内保留未回绕的完整媒体位置。
5. 发送前的输出修正和发送中的阻塞使用同一个原点。发送完成屏障触发原有卡顿恢复时，
   只补齐完成时仍然存在的 PTS 落后，再更新原有运行期 TIME_OFFSET。
   不把整个 pacing 原点位移再次加到已修正的 PTS 上，也不因发送前发生过一次微小修正
   就忽略随后 socket 内部的长阻塞。

新增 13 项确定性测试覆盖共享修正、50 微秒边界、35 分钟虚拟时钟、发送前后阻塞、
重复恢复幂等性，以及 RTP/PES 回绕。全量测试 759/759 通过。

真实编码探针仍使用左 + 左 + 右三个订阅者和两个 `aac_mf` 实例；它调用生产共享会话、
输出时钟和 packetizer，并检查实际组装出的 RTP/PES，而不是只比较数学模型。
正常运行及两次注入 350 ms 阻塞都验证了接收端时间一致，测量如下：

| 项目 | 正常重放 | 注入两次阻塞 |
| --- | --- | --- |
| 每个接收端的 AAC 帧数 | 225 | 425 |
| RTP/PES 一致的帧数 | 225 | 425 |
| 启动后 AAC 断供 | 0 / 0 / 0 | 0 / 0 / 0 |
| 最大 PTS 单步 | 24.922 ms | 388.778 ms（含主动阻塞） |
| 输出修正累计 | 3.592 ms | 211.304 ms |
| 发送后恢复次数 / 推进累计 | 0 / 0 ms | 2 / 356.989 ms |
| 采集内容年龄范围 | 371.6–396.1 ms | 388.3–912.0 ms（含主动阻塞） |

数值包含本机调度波动；断供统计不把主动暂停消费误记成“编码器无输出”。
构建验证为 Release 0 警告、0 错误。
该探针没有向音箱发送网络数据，不能作为听感或端到端延迟验收。

已生成并安装签名包 `artifacts/DLNACast.Windows_0.2.4.30_x64_with-dotnet.msix`，
包 SHA-256 为 `09CF5712180EEE91144BD076898502D30D842D3E958DEC63C5793F544B4384BC`。
安装后的 Core DLL 与打包暂存区哈希一致，运行进程路径已确认是 0.2.4.30。
0.2.4.29 的旧安装包保留且哈希未变；这些校验不代表新版本已完成音箱听感验收。

安装后的短时运行日志（12:34:03–12:34:14）记录了左右声道开始投送、两路有效 PCM、
约 20.8–21.9 ms 的发送间隔，以及两路 `outputPtsFixes=0`。
应用另记录一次 530 ms 声学延迟结果，相关度 0.888，检测方式为提示音回退。
这不是与旧版本配对的延迟对比，也不是两台音箱的同步测量；十分钟以上长播仍未验收。

```powershell
# 真实编码器：在两批 PCM 之间停顿 3 秒，检查输出采样 PTS
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --encoder-pts-only
# 三接收端共享重放
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only
# 第 300 帧交付前停顿、第 360 帧写入中停顿，均为 350 ms
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --stall-probe
```

## 第三阶段：共享起播不等待初始 TIME_OFFSET ACK（0.2.4.31）

进一步核对了同一份 ELF 的实际调用点：

- JNI `MultiMirrorControl_startMultiMirror` 在 `0x158f6c` 调用
  `mirror::startMirrorTimeSync(unsigned int)`（`0x1d7bfc`）。后者使用
  `GetServerInstance`，以 server 模式调用 `TimerWork::Init`，并明确传入
  `x5 = 0`，也就是没有自定义 ClockSync 回调。这样补齐了上文默认恒等映射的调用条件，
  但没有证明运行期所有校准行为都与 Windows 相同。
- Java 创建 MirrorOption，经 JNI `createMultiMirror`、`getMultiMirrorDisPlayEx`
  到 MultiMirrorDisplay 的已核对路径中，没有发现给 `0x10003d` 写值。
  构造函数 `0x1d6490–0x1d64e0` 和 `updateTimeOffset` 均有 1 秒回退值。
  本轮没有连接平板抓取实际选项，不能把“未发现写值”当作所有会话必定采用默认值。
- `WifiDisplaySource::setTimeOffset(long)`（`0x21dafc`）将偏移写入键 `0x18`，
  在 `0x21dc04` 调用 `AMessage::post(0)`，不在这里等待响应。
  消息分支 `0x210f90–0x211004` 更新 PlaybackSession 并调用 `sendTimeOffset`。
- `sendTimeOffset` 在 `0x216210` 调用 `ANetworkSession::sendRequest`，随后注册响应
  回调并推进 CSeq；`onReceiveTimeOffset`（`0x219d60`）只有 `mov w0, wzr; ret`。
  也就是初始偏移的 ACK 不是媒体起播门槛。

本轮只移除 Windows 共享组起播对初始 CSeq 5 ACK 的等待：

1. 仍先发送 PLAY 响应，等待原有 post-Open 上下文发送完成，再写出 TIME_OFFSET。
2. 写入成功后调用 `MarkInitialTimeOffsetSent`，共享会话才进入 Ready；不能在写入前放行。
3. 仍需所有成员通过共同媒体屏障和 14 帧有序 AAC 预热屏障，不能由某个音箱抢先开播。
4. 初始 ACK 迟到、在运行期 CSeq 6 期间到达、或在 CSeq 6 完成后重复到达，都不结束
   新请求，也不恢复旧偏移值。不回复初始 ACK 也不妨碍后续正确响应的运行期更新。
5. 单音箱默认流程仍等待原有 ACK。运行期 TIME_OFFSET 更新仍保留匹配 ACK 和超时检查；
   本轮并未把整个控制协议改成忽略响应。

这次没有改变 TIME_OFFSET 数值、PCR、输出 PTS、编码器、输入窗口或发送节奏。
Windows 仍在 PLAY 时取偏移，而平板从首个编码输出 PTS 更新；要继续对齐算术，必须
同时弄清输出原点、服务器时钟与 PCR 的关系。不能在现有 PCR 已减去播放缓冲时，
直接再给 TIME_OFFSET 加上原生的 1 秒回退值。

新增 7 项确定性回归测试；全量 766/766 通过，Release 构建 0 警告、0 错误。
另扩展本地探针，直接调用生产 `RunRtspControlAsync`，用本机 TCP 模拟三个接收端：
初始 ACK 分别立即回复、直到 CSeq 6 才回复、完全不回复。三路均在写入成功后进入 Ready；
post-Open 门槛仍有效，CSeq 6 匹配响应均完成，迟到和重复 CSeq 5 后的保活也通过。

同时使用真实 `aac_mf` 及左 + 左 + 右共享路由检查媒体输出：

| 项目 | 普通重放 | 两次 350 ms 阻塞 |
| --- | --- | --- |
| 每路 AAC / 共享 RTP、PES 一致帧数 | 225 / 225 | 425 / 425 |
| 启动后 AAC 断供 | 0 / 0 / 0 | 0 / 0 / 0 |
| 最大 PTS 单步 | 26.789 ms | 386.378 ms（含主动阻塞） |
| 发送后时间线恢复次数 | 0 | 2 |
| 编码内容年龄 | 374.3–396.7 ms | 403.9–895.0 ms（含主动阻塞） |

这是本机控制连接和编码器重放，不包含真实音箱的解码、声学延迟或长期播放验收。
运行期 CSeq 6 使用原偏移验证 ACK 关联；阻塞时的媒体时间线恢复不代表已验证硬件端的
运行期 TIME_OFFSET 接收效果。所有测量包含本机调度波动。

```powershell
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --stall-probe
```

已生成并安装签名包 `artifacts/DLNACast.Windows_0.2.4.31_x64_with-dotnet.msix`，
包 SHA-256 为 `CBFBAF15776938925FFB6376AB21794E3F403BB4C0EC3A025289534EBAB3AC9C`。
签名验证有效；安装后的 Core DLL 与打包暂存区一致，SHA-256 为
`8B10C3B3BBD60A863FE7F899D838CB1C7F15F3426062E04DC5AA3514836F49A6`。
12:49:18 启动的应用进程路径已确认是 0.2.4.31，0.2.4.30 的旧安装包哈希未变。
更新会结束旧投送；尚未完成本版本真实音箱的起播、暂停恢复和长播听感验收。

## 第四阶段：共享 PCR 扩展位精度（0.2.4.32）

本轮尝试读取平板实时会话时，原 ADB 地址 192.168.10.99:37017 返回连接被拒绝，
已连接设备列表为空。因此没有根据默认值修改首帧 TIME_OFFSET；继续使用已保存的
ARM64 原生库，先完成可独立验证的 PCR 数值精度。

再次检查 TSPacketizer::packetize 的 0x1f860c–0x1f8674：
读取 ALooper::GetNowUs() 后乘 27，再拆成除以 300 的商和余数。
商写入 33 位 PCR base，余数写入 9 位 extension，保留位为 0x7e。
Windows 原来的 WritePcr 只写 base，extension 恒为 0。

0.2.4.32 只让共享组保留这部分精度，使用：

~~~text
pcr27 = ((captureUs - existingPlaybackDelayUs) * 27) % (2^33 * 300)
pcrBase = pcr27 / 300
pcrExtension = pcr27 % 300
~~~

这里仍然是 Windows 原有的“采集时钟减播放缓冲”，不是平板的原生本地输出时钟。
原有 90 kHz 整数部分逐项保持相同；改变的时间小于 11.12 微秒，不是几百毫秒延迟的修复。
TIME_OFFSET 数值与时序、RTP/PES、AAC FIFO、输入信用窗口、预热队列及发送节奏均不变。
单音箱继续调用原来的整数转换，extension 默认为 0。

MiPlayMpegTsAudioMuxer 与 MiPlayWfdAudioPacketizer 增加可选 extension 参数，
只在原有 PCR 表包上写入，不增加表包、不改变分片。非法 extension（>= 300）或缺少
base 时携带非零 extension 会在修改序号/连续计数器前被拒绝。
27 MHz 换算使用 UInt128 中间值，避免长时间时钟乘法溢出。

新增 18 项回归，全量 784/784 通过，Release 构建 0 警告、0 错误。覆盖：

- 微秒换算、小数进位、保留位以及 extension 的 255/256/299 边界；
- 33 位 PCR base 回绕、ulong 最大输入及非法时间原点；
- 三个接收端跨回绕的 30 分钟虚拟时间线，共 84,375 帧；这不是实际长播测试；
- 表包刷新、单/双 RTP 分片、PTS 和发送后时间线推进不受小数位影响。

另外实际加载了 0.2.4.31 的 Core DLL，与新代码的默认调用逐包对比。
640 个 AU、700 个网络包全部逐字节一致，覆盖固定/实时 PCR、默认/显式 PTS、
两种 AAC 长度和时间线推进。基线 DLL 已单独保存在本地探针的
baselines/0.2.4.31/，SHA-256 与上一节安装的 .31 DLL 相同。

真实 aac_mf 与本机 TCP 三接收端重放结果如下：

| 项目 | 普通重放 | 两次 350 ms 阻塞 |
| --- | --- | --- |
| 每路 AAC / RTP、PES 一致帧数 | 225 / 225 | 425 / 425 |
| 三路完整 PCR 一致的组数 | 44（全部有小数位） | 84（全部有小数位） |
| 启动后 AAC 断供 | 0 / 0 / 0 | 0 / 0 / 0 |
| 最大 PTS 单步 | 25.978 ms | 382.000 ms（含主动阻塞） |
| 发送后时间线恢复次数 | 0 | 2 |
| 编码内容年龄 | 371.5–394.9 ms | 361.0–883.6 ms（含主动阻塞） |

两轮均将 PCR 扩展位归零后与兼容输出逐字节对比，所有网络字节一致。
三种初始 ACK 时序、后续 CSeq 6 与保活也均通过。
这仍是本机重放，没有证明真实音箱的解码效果、同步听感或端到端延迟。

~~~powershell
# 基线必须是保留的 .31 DLL；探针会先检查其哈希
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --pcr-compatibility-only --legacy-core 'C:\IDE\Windows\DLAN-for-Windows\artifacts\runtime\miplay-window-check\baselines\0.2.4.31\DLNACast.Core.dll'
# 三接收端重放命令与第三阶段相同，现在同时检查完整 PCR 与非 PCR 字节
~~~

已生成并安装签名包 artifacts/DLNACast.Windows_0.2.4.32_x64_with-dotnet.msix，
包 SHA-256 为 9E90132F1A1E36D6F9F986C5C03F1E2B05E7F3E3C6CFB26E138B9DB8228CBC58。
签名有效，安装的 Core DLL 与打包暂存区一致，SHA-256 为
5E110652E6A24420F28E74B27DFEC327EBAE3F3F680851BCFEF507394ED5735F。
0.2.4.31 的旧安装包哈希未变。更新已结束旧投送；新版本真实音箱效果仍待验收。

## 第五阶段：共享 PCM 只保留一个采集节拍（0.2.4.33）

继续检查首帧 TIME_OFFSET 前，先读取了 0.2.4.32 的真实运行日志。
14:55:51 开始的共享投送，到 15:27:40 时仍是约 21 ms 的发送间隔，
AAC 等待计数稳定在 11。15:27:50 开始出现等待，15:27:55 的窗口内记录到
830.949 ms 发送间隔、531.991 ms 接收端发送时差，以及采集中断和时间线恢复。
随后长期呈现约 95 ms / 接近 0 ms 的成批发送；15:45:48 的 AAC 等待计数已达 19632。
左右编码器的输入占用和输出余量也开始分离，但两路仍有有效 PCM。
原始快照保存在本地 artifacts/runtime/miplay-window-check/
longplay-0.2.4.32-20260828-154927.log。

这些日志说明长播遇到扰动后的恢复仍有问题，不说明上一版 PCR 小数精度引起了卡顿。
timelineRebases、captureGaps 等是诊断窗口计数，后面归零不代表从未恢复；
receiverLatencyMs 也不能直接当作声学延迟。

重新核对 MicRecorder.java：采集、4096 字节读取、PTS 计算和恢复静音都在同一条输入路径；
编码输出再交给 MultiMirrorControl.WriteStream。Windows 原先虽然先从共享源分发 PCM，
但每个声道路由仍调用 ReadFrameWithTimingOrSilenceAsync，使用自己的读时钟，
空队列时还会各自生成静音。这会让本来相同的共享帧序列再次按不同节拍采样。

用真实 aac_mf 做左 + 左 + 右三接收端本机重放，在第 300 帧暂停左路 PCM 消费 830 ms，
其余路径继续运行；恢复后的统计从第 450 帧之后开始。修改前后的同一组 900 帧实验：

| 项目 | 修改前 | 修改后 |
| --- | --- | --- |
| 左 / 右路自行生成的额外静音帧 | 2 / 6 | 0 / 0 |
| 恢复后每接收端 AAC 等待次数 | 46 / 46 / 46 | 0 / 0 / 0 |
| 恢复后发送间隔范围 | 14.116–33.582 ms | 14.217–32.991 ms |
| 同左路 AAC 一致、左右 AAC 不同、共享 PTS 一致 | 各 900 帧 | 各 900 帧 |

这复现了不同路由额外补静音以及恢复后持续等待，没有完整复现真实日志中的
95 ms / 0 ms 成批发送，不能据此宣称已确定长播卡顿的全部原因。

本轮改动只针对共享编码器的 PCM 消费：

- 增加有序等待读取，严格消费共享生产者给出的下一帧，不独立计时、不在空队列补零。
- 队列空时异步等待写入；取消和完成会唤醒等待，不改变写端背压或帧顺序。
- 采集中断恢复需要的静音仍由共享采集路径统一提供，连同原 PTS 一起进入各路。
- 原有低频 pcmSignal 日志增加 encoderInputSilenceFrames，便于区分路由额外补零和
  共享采集恢复静音。正常共享路由该计数应保持 0。

未改变 aac_mf、48 kHz/16 bit/双声道格式、16 帧输入窗口、14 帧 AAC 预热、
AAC FIFO、TIME_OFFSET、PCR、共享输出 PTS 和网络发送节拍。
编码器仍在 RTSP 就绪前得到共享 PCM；单音箱采集和电平读取仍使用原有定时读取。

新增 11 项回归，覆盖空队列等待、立即读取已排队帧、两路先后消费、恢复静音/PTS、
背压、取消、完成、默认采集行为、1000 次并发读写以及清空队列后的唤醒。
全量 795/795 通过，Release 构建 0 警告、0 错误。

另外回归了两次 350 ms 交付/网络写入阻塞：每路 425 帧 AAC、RTP/PES 和完整 PCR
均保持共享一致，启动后 AAC 等待为 0/0/0，两次发送后恢复均执行。
三个本机 RTSP 控制接收端的初始 ACK 立即/迟到/不回复，以及运行期 CSeq 6 匹配、
重复旧 ACK 后保活全部通过。这些检查不包含真实音箱的接收与解码。

随后完成约 11 分钟、每路 31000 帧的真实编码重放，同样在第 300 帧暂停左路 830 ms。
同左路 AAC、共享 RTP/PES 在全部 31000 帧上一致，左右测试音不同；
6199 组完整 PCR 一致，其中 6152 组带有非零 extension。
左右路额外补零均为 0，恢复后各接收端仅有 1 次短暂 AAC 等待，之后没有继续累积。
恢复后发送间隔范围为 0.158–43.842 ms，仍包含本机调度波动；最大输入占用 15 帧。
包含主动阻塞时，编码内容年龄为 260.8–1012.1 ms，不能将这个范围当成声学延迟。
本机断供持续性断言通过，但这不是超过 40 分钟的真实音箱播放验收。

最后重跑普通起播：实际共享编码器单路 225 帧没有 AAC 等待，
三接收端 225 帧也没有等待，AAC、RTP/PES 和 PCR 检查均通过。
三接收端普通重放的编码内容年龄为 342.7–365.4 ms，未进行声学延迟测量。

~~~powershell
# 两路编码器、三个本机控制接收端；左路在第 300 帧暂停 830 ms
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --route-pause-probe --assert-recovery
# 加上 --long-probe 重放 31000 帧，约 11 分钟
~~~

已生成并安装签名包 artifacts/DLNACast.Windows_0.2.4.33_x64_with-dotnet.msix，
包 SHA-256 为 0175A4CBA90C0DF2735D84B4C1A0963AA109490CB006253D2FFC03345F1C371E。
签名验证有效。长测加载的、MSIX 内的以及安装后的 Core DLL 均一致，SHA-256 为
EBFD2BF977206135F195F6AA2FEDC1C5EA95DE393F7C7C337BCE4F388616FA3B。
0.2.4.32 的旧安装包哈希未变。更新结束了旧投送，新版本真实音箱效果仍待验证。

## 第六阶段：多音箱 UDP 校时字段顺序（0.2.4.34）

0.2.4.33 的新立体声日志（16:09:46 起播，核对到 16:10:18）记录了两路有效 PCM，
encoderInputSilenceFrames 均为 0，AAC 等待计数稳定在 11，发送间隔约 21 ms。
这是短时运行证据，不能替代上一节要求的长播和声学同步验收。

继续检查首帧偏移的时钟依赖时，在同一份 ARM64 原生库中找到一处独立的报文差异：
TimerService::Sync（0x224244–0x2242f4）以 sp+0x20 作为 40 字节 UDP 缓冲区。
recvfrom 后第一次读取时钟，在 0x2242ac 写入 sp+0x38，也就是包内 +24；
sendto 前第二次读取时钟，在 0x2242c4 写入 sp+0x30，也就是包内 +16。

| 字节偏移 | 平板原生字段 | 原 Windows 响应 |
| --- | --- | --- |
| 0–7 | 客户端发出时间，原样保留 | 相同 |
| 8–15 | 客户端接收时间槽，服务端原样保留 | 相同 |
| 16–23 | 服务端发送时间（第二次读时钟） | 服务端接收时间 |
| 24–31 | 服务端接收时间（第一次读时钟） | 服务端发送时间 |
| 32–39 | 序号及保留字段，原样保留 | 相同 |

TimerClient::SyncEx 在 0x222250 将本次客户端收包时间写入本地的 +8 时间槽。
再从接收端算法交叉核对：TimerAlgorithm::unitRound（0x21faa8–0x21fabc）
计算 packet[8] - packet[0] - packet[16] + packet[24]。
因此 +16/+24 必须分别是服务端发送/接收时间，才能从往返耗时里减掉服务端处理时间。
unitOffset（0x21fa74–0x21faa4）计算：

~~~text
offset = trunc((1-p) * (packet[24] - packet[0])
             + p * (packet[16] - packet[8]))
~~~

旧顺序会将处理耗时加到 RTT 中；当 p 不等于 0.5 时，也可能使偏移估计产生额外误差。
确定性样例中，网络往返 2 ms、服务端处理 50 ms，旧响应算得 RTT=102 ms，
新响应为 2 ms。这是合成输入的算术对照，不是测到真实音箱有 100 ms 的额外延迟。

本轮只增加 CreateTabletResponse，并让共享多音箱的 RunTimerResponderAsync 使用它。
响应仍是 40 字节、小端、同一 Stopwatch 单调微秒时钟，保留请求的其他全部字节，
不改 peer/长度校验。Encode/Decode/CreateResponse 及其旧字段命名保持兼容，
单音箱仍使用旧响应路径。TIME_OFFSET 的数值和发送时机、PCR 来源/精度、AAC、
输入窗口、预热队列和网络发送节奏均未修改。

新增 15 项回归，全量 810/810 通过；Release 构建 0 警告、0 错误。覆盖：

- 不依赖生产解码器的 40 字节固定样例、原始请求不变及 64 位时间边界；
- 严格报文长度与单音箱旧格式；
- 原生 RTT、加权偏移及整数截断，含 0、12、13、50000 us 处理耗时；
- 实际生产 UDP responder 的两种模式，各 32 次本机请求，核对字段位置、
  序号/保留字节、时钟范围及取消退出。

真实 aac_mf 三接收端重放也通过：正常起播每路 225 帧，启动后 AAC 等待为 0/0/0；
暂停左路输入 830 ms 的 900 帧实验，恢复后等待为 0/0/0，额外补零为 0/0。
两次重放的共享 AAC、RTP/PES、完整 PCR、初始 ACK 三种时序及运行期 ACK 检查均通过。
这不是新版本的真实音箱播放或端到端延迟测量。

~~~powershell
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x224244 --stop-address=0x2242f8 artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x21fa74 --stop-address=0x21fac0 artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Release --filter FullyQualifiedName~MiPlayWfdTimerPacketCodecTests
~~~

已生成签名包 artifacts/DLNACast.Windows_0.2.4.34_x64_with-dotnet.msix，
SHA-256 为 DB8CDA2F3FC2035A00E9202534F69762427E14D6D2C80D2C6C671A922078AE1C。
签名有效，包内 Core DLL、暂存区和重放探针使用的 Core 均一致，SHA-256 为
70A604732CBA4670976F72D6D1119BEA000E44822E810EA63B82394804EC8F0C。
本轮未安装，以保留正在进行的 0.2.4.33 投送；.33 旧包哈希未变。
16:22:13 的 .33 日志已到 35001 帧，AAC 等待计数仍为 11，两路额外补零仍为 0。

本轮原平板 ADB 地址 192.168.10.99:37017 被拒绝；随后提供的 44979 端口 TCP 可达，
但两次 ADB 连接均握手失败，已连接设备和 mDNS 服务列表为空。
因此没有取得新的平板实时选项或日志；需确认无线调试连接端口，或完成配对。
这里的原生字段顺序证据仍来自已保存并核对哈希的 ELF，不是本轮实时抓包。

## 第七阶段：共享运行期 TIME_OFFSET 不再等待 ACK（0.2.4.35）

本轮已确认用户安装并运行 0.2.4.34，进程来自对应 WindowsApps 目录。
16:25:57–16:26:07 的立体声日志仍有两路有效 PCM，额外补零均为 0，
AAC 等待计数保持 11，发送间隔约 21 ms。新平板地址的 ADB 握手仍失败，
所以本轮继续核对已保存的 ARM64 ELF，没有据默认值调整首帧偏移或 PCR。

Windows 的共享恢复路径在 SynchronizeSendCompletionAsync 判断时间线需要恢复后，
会调用并等待 MiPlayRuntimeTimeOffsetControl.UpdateAsync；显式恢复播放也使用这个队列。
原先请求只有收到匹配 ACK 才完成，3 秒内不回复就抛出超时。
这意味着媒体恢复可能因等待校时回包再次停住，不只是起播阶段存在 ACK 门槛。

原生的运行期路径与此不同：

- onMessageReceived 的 0x210fdc–0x211004 先保存新偏移，更新 PlaybackSession，
  然后调用 sendTimeOffset。
- sendTimeOffset 在 0x216210 发出请求，0x21628c–0x2162d0 注册响应回调并推进 CSeq，
  随后在 0x2162f0–0x2162f8 返回，没有在这里等待回应。
- onReceiveTimeOffset（0x219d60–0x219d64）仍是直接返回 0，既用于初始偏移，也用于
  这条运行期发送路径；不是用来放行媒体发送的回调。

本轮把共享运行期请求的完成条件改成 RTSP 字节成功写出：

1. 入队或 PrepareRuntimeTimeOffset 不算成功，仍串行写入；写入失败会传播原异常。
2. MarkRuntimeTimeOffsetSent 在实际写入之后提交新偏移，解除当前请求，媒体才能继续。
3. 只接受已经写出的 CSeq 6 及后续 TIME_OFFSET 回包；迟到、乱序、重复 ACK 不会覆盖
   最新偏移，也不会消费另一个尚未写出的请求。
4. 使用已写 CSeq 的上界记录已发送区间，不为永久丢失的每个 ACK 保留一个对象。
   目前此会话的源端 CSeq >= 6 只用于 TIME_OFFSET，后续新增其他请求时须重新核对这一条件。
5. 单音箱默认仍等待匹配 ACK。RTSP 格式错误、未知 CSeq、非 200/非空 ACK、写入失败和
   连接断开仍按原保护逻辑报错；没有把整个控制协议改为忽略错误。
6. 3 秒保护仍存在：共享模式约束入队到写完的耗时，单音箱模式仍约束收到 ACK 的耗时。

此处没有改变偏移数值、PCR、输出 PTS、AAC、缓冲、发送节奏或上一版 UDP 字段顺序。
平板更新本地状态在发送调用前，Windows 为保留写入失败边界，在写完后提交状态；
这里只对齐不等待 ACK 的恢复行为，不声称实现了完全相同的线程调度。

新增 18 项回归，全量 828/828 通过，Release 构建 0 警告、0 错误。
覆盖写入前不得放行、迟到/乱序/重复回复、1000 次无 ACK 的连续更新、未知或未写 CSeq、
非法回复、单音箱原 ACK 门槛、排队取消以及写入/断开错误传播。

本机探针直接驱动生产 RunRtspControlAsync，并在媒体循环里等待三路偏移更新。
接收端的运行期 ACK 分别立即回复、延迟到下一请求才回复、完全不回复；
经过 3.2 秒窗口后继续检查保活和 CSeq 8 更新。偏移值保持不变，以隔离完成条件。
使用保留的旧 ACK 等待模式作对照（不是运行旧安装包），会复现原 3 秒超时；
新模式正常重放时三路请求在 3.917 ms 内完成写入，后续无 ACK、重复 ACK 和保活检查通过。
这个数值是本机控制写入等待，不是声音延迟。

真实 aac_mf 重放结果：

| 场景 | 每路 AAC / RTP、PES 一致帧数 | 三路更新写入等待 | AAC 等待结果 |
| --- | --- | --- | --- |
| 普通起播 | 225 | 3.917 ms | 启动后 0/0/0 |
| 暂停左路 PCM 消费 830 ms | 900 | 2.440 ms | 恢复后 0/0/0；主动停顿期间各 1 次 |
| 两次 350 ms 交付/网络写入阻塞 | 425 | 3.281 ms | 启动后 0/0/0 |

三种场景的完整 PCR 检查均通过；输入停顿后额外补零仍为 0/0。
两次网络阻塞的共享时间线恢复均执行。另用 ACK 等待模式正常重放 225 帧，
匹配响应、迟到初始 ACK、保活及 AAC 输出也通过。
这些都是本机模拟接收端，没有验证真实音箱的恢复听感、同步或长播效果。

~~~powershell
# 新行为：运行期 ACK 立即/迟到/完全缺失，媒体循环实际等待写入完成
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --runtime-write-probe
# 输入停顿；网络阻塞则把最后两个参数改为 --stall-probe
dotnet run --no-build --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --runtime-write-probe --route-pause-probe --assert-recovery
# 加 --legacy-runtime-ack 可复现旧 ACK 门槛的 3 秒超时（预期失败，仅本机探针）
~~~

已生成签名包 artifacts/DLNACast.Windows_0.2.4.35_x64_with-dotnet.msix，
包 SHA-256 为 EEA9C4459333E091FB66B6F0C034CA1474AE4BF3E2236ABAE0C977E7DB293D81。
签名有效，打包暂存区与重放探针的 Core DLL 一致，SHA-256 为
5AF2907D67449D2361E5AE43F7E663F835ABFCC4562062FD5B08F7B8B3F84B86。
本轮不自动安装，保留正在进行的 0.2.4.34 投送；旧安装包保留。

## 第八阶段：共享控制通道按心跳活动判活（0.2.4.36）

用户运行 0.2.4.35 时报告：

~~~text
MiPlay command 0x001A sequence 0x0022 was not acknowledged.
Shared MiPlay access-unit broadcasting failed.
~~~

本机 LocalState 日志确认 16:44:27 出现第一条错误；16:45:37.719 又出现
0x001A / 0x0015 未确认，约 134 ms 后出现共享分发失败。
此前已记录到发送间隔 256 ms、接收端发送完成差异 234 ms，以及后续更长阻塞。
因此控制超时不是已证明的全部卡顿原因。旧日志只记录异常最外层 Message，
没有保存本次广播失败的 InnerException，不能仅凭先后顺序断言它的具体根因。

0x001A 是 TCP 8899 控制心跳，不是 RTSP TIME_OFFSET。
原 Windows 每 5 秒发送一次，但 ExecuteRuntimeControlAsync 把每次发送当成交易：
3 秒内必须收到相同序号、空载荷的 0x001B；最多只读取 8 个交错帧。
两次交易之间不持续读取控制流，也不回应接收端发来的 0x001A。
一次心跳晚回或其他通知排在 ACK 前面，就可能终止正在播放的接收端会话。

同一份 ARM64 原生库的指令与只读数据提供了另一套判活依据：

- CmdSource 构造函数在 0x160888 / 0x160890 读取 0xfdf10 与 0xfdda0，
  0x1608a0 写入成员 +0xf0、+0xf4、+0xf8，对应发送间隔 5 秒、无活动超时 15 秒、
  reaper 检查间隔 1 秒。相应只读字节为 `05000000 0f000000` 和 `01000000`。
- sendHeartBeat（0x161bd0–0x161c10）发送空载荷 0x001A、推进序号，然后调用
  scheduleKeepAlive。0x161d04–0x161d18 按 +0xf0 秒安排下一次消息，不等待 ACK。
- onMessageReceived 的心跳分支（0x16485c–0x164890）也直接发送并安排下一次心跳。
  reaper 分支在 0x16465c–0x164678 比较 `now > lastHeartbeat + 15 秒`，
  没有用某一条请求序号设置 3 秒门槛。
- onRecvCmd 的跳转表位于 0xfe00a，以 command-1 为索引：0x001A 对应 0x010b，
  从基址 0x1664d8 跳到 0x166904，更新时间 +0xe8 并发送同序号 0x001B；
  0x001B 对应 0x0176，跳到 0x166ab0，仅更新 +0xe8。没有匹配待确认心跳序号。
- 本次检查 CmdSource 代码区间内 +0xf0/+0xf4/+0xf8 的读写，除构造初始化外，
  只发现调度和超时比较处的读取。平板 ADB 44979 仍握手失败，以上是保存的 ELF
  指令证据，不是此次真实平板会话抓包。

共享多音箱现在使用 MiPlaySharedRuntimeControlChannel：

1. 整个运行期只有一个持续收包任务；不因单条 ACK 的等待期限取消半帧读取。
2. 心跳在完整写出后完成，不要求逐条匹配 ACK。迟到、重复的 0x001B 都能更新
   心跳活动；收到空载荷 0x001A 时立即排队发送同序号 0x001B，不占用源端序号。
3. 仍每 5 秒发送心跳，从实际写出时刻安排下一次，不在调度延迟后补发一串旧心跳。
   每秒检查一次心跳活动；严格超过 15 秒无活动仍报错。任意通知或音量 ACK
   不能代替心跳刷新这条期限。
4. 心跳回复与应用命令共用串行写锁，避免 TCP 字节交错。音量仍必须在 3 秒内
   得到匹配命令、序号和载荷的确认；暂停/恢复保留原有完成条件。
5. 运行期不再用“读到 8 个交错帧”作为失败边界。帧 magic、最大长度、完整帧
   以及已验证的空载荷心跳格式仍校验；读写失败、EOF 和取消仍传播。
6. 新心跳收发诊断带接收端地址，使用独立、有界的文字日志队列，在现有媒体报告
   处输出，避免后台心跳把 UI 状态改成等待接收端或清零播放统计。
   这里允许丢旧项的只是文字诊断，不是 PCM、AAC 或控制命令队列。
7. 共享广播等外层错误现在同时显示最内层原因；不更改广播故障处理或音箱移除策略。

单音箱仍使用原来的逐条 ACK 路径。共享会话的首次握手/自动播放就绪等待、
AAC FIFO、采集、缓冲、编码参数、RTP/PES/PCR、TIME_OFFSET 和 UDP 均未改动。
本轮原拟继续核对 UDP 容错，收到实际心跳报错后已暂停，没有把该修改混入本版。
音量/显式恢复仍由原命令循环串行执行，并非整个 CmdSource 线程模型的完整重写。

新增 21 项测试，全量 849/849 通过；Release 构建 0 警告、0 错误。
生产 RunRuntimeControlsAsync 的本机 TCP 测试验证：

- 旧单音箱模式缺失 ACK 时仍复现 3 秒超时；共享模式继续发下一次 5 秒心跳。
- 缺心跳 ACK 时可以调音量，32 条通知不会耗尽读取额度；迟到心跳不会确认音量。
- 将旧心跳 ACK 的前 4 字节先写入，直到下一次心跳发出才补齐剩余字节，
  随后的双向心跳和音量确认仍能正确解析。
- TCP 关闭在写端空闲时也会被持续读取发现；完全无心跳回复的真实 15 秒等待
  仍触发连接超时，不是永久忽略失联。
- 另有确定性时钟边界、15 秒后的刷新、序号回绕、写失败、并发回复串行化、
  非法帧、音量缺 ACK、调用者取消和原异常保留测试。

另外独立执行了真实 aac_mf 与三路生产 RTSP 重放；它们不包含本轮 TCP 8899
心跳模拟，不能把这两类独立检查说成真实音箱端到端验证：

| 重放场景 | 每路 AAC / RTP、PES 一致帧数 | AAC 等待 | 其他结果 |
| --- | --- | --- | --- |
| 普通起播 | 225 | 启动后 0/0/0 | 44 组完整 PCR 一致，编码内容年龄 343.4–365.2 ms |
| 暂停左路 PCM 消费 830 ms | 900 | 恢复后 0/0/0 | 额外补零 0/0，恢复后发送间隔 14.602–33.143 ms |
| 两次 350 ms 网络/交付阻塞 | 425 | 启动后 0/0/0 | 两次共享时间线恢复，84 组完整 PCR 一致 |

三种重放都通过初始和运行期 RTSP ACK 立即/迟到/缺失检查。
编码内容年龄不是声学延迟；尚未验证新版本真实音箱听感、同步与 40 分钟连续播放。

~~~powershell
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Release --filter 'FullyQualifiedName~MiPlayRuntimeControlLoopTests|FullyQualifiedName~MiPlaySharedRuntimeControlChannelTests'
dotnet run --no-build --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --runtime-write-probe
# 输入停顿加 --route-pause-probe --assert-recovery；网络阻塞加 --stall-probe
~~~

已生成签名包 artifacts/DLNACast.Windows_0.2.4.36_x64_with-dotnet.msix，
SHA-256 为 36B472A2723DF9AAB674878B8BF30AFEFCFE0B08C713291D063F0A2EBBB42C1D。
签名验证有效，包内 manifest 为 0.2.4.36；包内、暂存区、849 项测试及 AAC 重放
使用的 Core DLL 哈希均为 BE1E080348589D1C734D055056543F09031F174AB60C1F7AE1A49C3F4E126E24。
本轮未安装或重启应用；核验时安装/运行版本仍是 0.2.4.35，旧安装包哈希未变。

## 第九阶段：共享 UDP 校时丢弃异常报文后继续服务（0.2.4.37）

本轮核验时仍在运行 0.2.4.35，0.2.4.36 尚未安装。
18:03:47 起播后的短时日志有左右两路非零 PCM，额外补零为 0，AAC 等待计数
保持 13，发送间隔约 21 ms；这些日志不能验证尚未安装的心跳修复或本轮变化。
ADB 已连接设备列表仍为空，以下继续使用前文同 SHA-256 的已保存 ARM64 ELF。

这次处理上一轮因实际心跳故障而暂缓的 UDP 校时容错，未修改心跳实现。
原 RunTimerResponderAsync 遇到来源 IP 不符或长度不是 40 字节时，直接抛出
`The MiPlay timer received an unexpected peer or packet.`。媒体循环会检查 timerTask
并传播该异常，因此单个不相关或畸形 UDP 包也可能结束这个接收端的播放。
这是一条代码上可以复现的故障路径，尚未从真实网络捕获到它触发的报文，
不能把它当作此前所有卡顿、失联的已确认根因。

平板处理依据：

- TimerService::Sync 在 0x22428c 调用 recvfrom，接收缓冲区为 40 字节。
  0x2242a8–0x2242b0 检查接收长度，小于 1 时直接跳到返回路径。
  0x2242e0 调用 sendto 后也进入相同返回路径；0x2242ec 固定返回 1。
- TimerWork::threadLoop（0x224874–0x224898）调用已初始化服务对象的 Sync 后，
  在 0x224890 再次固定返回 1。单次收包处理不会使该线程循环返回 false。
- 原生实现会对正长度报文回复，且此函数没有来源 IP 和严格长度校验。
  本轮只对齐“不因单个异常报文终止服务”，不照搬其对任意来源回复、短包补齐或
  长包截断的行为，也不忽略 Windows 上真正的 socket 读写失败。

Windows 共享多音箱现在对来源/长度不符的报文直接丢弃，继续等待下一包。
只有目标音箱 IP 发来的完整 40 字节请求会得到响应，源端口仍允许变化。
正常响应依旧采用 0.2.4.34 的 send@+16、receive@+24 顺序，其他字段原样保留。
私有参数改名为 useTabletTimerBehavior，以同时表达字段顺序及这项容错。
单音箱默认路径仍按原逻辑处理；取消、socket 关闭和读写异常仍传播。

新增 16 项生产 UDP responder 的本机回环测试，连同原字段/时钟测试共 31 项通过：

- 0、1、8、39、41、512、65507 字节异常报文，与带不同序号的正常请求交错发送；
- 从 127.0.0.2 发来的请求不会得到回复；共享模式仍继续响应目标地址；
- 单音箱对错误来源、错误长度仍抛出原异常；
- 两个共享校时服务各完成 32 次正常请求，其中一路混入异常包并切换客户端端口，
  两路均不产生后台任务故障，响应时钟、序号及保留字段保持正确；
- 无效报文排队时仍能取消，关闭 socket 的错误不会被当成可忽略的 UDP 包。

全量 865/865 测试通过，Release 构建 0 警告、0 错误。
独立的真实 aac_mf 左+左+右普通重放完成每路 225 帧，同左路 AAC 一致、左右不同，
RTP/PES 与 44 组完整 PCR 保持共享一致，启动后 AAC 等待为 0/0/0。
本机调度触发过 1 次输出 PTS 修正，共 7.046 ms；没有修改这套既有修正逻辑。
RTSP 初始及运行期 ACK 立即/迟到/缺失重放也通过。
另回归两次 350 ms 网络/交付阻塞：每路 425 帧的 AAC、RTP/PES 及 84 组完整 PCR
均保持共享一致，启动后 AAC 等待为 0/0/0，两次共享时间线恢复均执行。
UDP 和 AAC/RTSP 是独立测试，不是实际音箱上的异常报文注入或声学同步验证。

本轮未调整采集、aac_mf、AAC FIFO、输入窗口、预热数量、TIME_OFFSET 数值、
PCR、输出 PTS 或媒体发送节奏。新包包含上一轮心跳修复，但真实音箱效果及
超过 40 分钟的连续播放仍需安装后验证。

~~~powershell
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Release --filter 'FullyQualifiedName~MiPlayWfdTimerResponderTests|FullyQualifiedName~MiPlayWfdTimerPacketCodecTests'
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x224244 --stop-address=0x2242f8 artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x224874 --stop-address=0x22489c artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
~~~

已生成签名包 artifacts/DLNACast.Windows_0.2.4.37_x64_with-dotnet.msix，
SHA-256 为 2700C49D2740E9727B911EE833DFC361BE2283465B3EF4048AFFA0A35445B9AF。
签名有效，包内版本号为 0.2.4.37；包内、暂存区、全量测试与 AAC 重放使用的
Core DLL 哈希均为 54588A03EC90F2376C6310EDDB320E42674AF3EA3ACF5AE050E77B8D78797A41。
未自动安装或重启应用，核验时安装和运行的仍是 0.2.4.35；0.2.4.36 旧包哈希未变。

## 第十阶段：共享 TIME_OFFSET 回包不因状态码或正文结束播放（0.2.4.38）

本轮确认已安装 0.2.4.37，核验时应用没有运行。最新日志记录了 18:40:08 至
19:03:21 的左右声道播放，约 23 分 13 秒，末次统计为每路 65,251 个 AAC AU。
两台音箱持续返回心跳，没有记录会话错误；统计窗口的最大发送间隔为 28.366 ms，
lateSends 和 timelineRebases 始终为 0。AAC 等待计数从起播后的 11，在末尾升至
24、42，没有伴随发送间隔的明显扩大。这里的等待次数不是丢帧数，发送完成差异
也不是音箱的声学同步误差。这段日志不能替代听感确认或 40 分钟长播验收。

ADB 设备列表仍为空，继续使用前文同 SHA-256 的已保存 ARM64 ELF。
首帧 TIME_OFFSET 数值和 PCR 时钟来源仍未改动；这次补齐的是回包处理，
不是依靠默认播放延迟推算新的时间偏移。

反编译证据：

- sendTimeOffset 在 0x216210 调用 sendRequest；0x21628c–0x2162d0 按会话和
  CSeq 注册响应处理函数并推进序号。0x2162bc–0x2162c4 从 GOT 0x302ad8 取出
  回调指针；该地址的动态重定位明确指向 onReceiveTimeOffset。
- onReceiveClientData 的 0x2141b8–0x214288 解析 CSeq，区分 RTSP 响应并查找
  对应请求。0x214758–0x2147e4 取出并删除响应处理条目，再调用该函数；
  0x2147ec 检查的是回调返回值，没有在此路径检查响应是否为 200 或正文是否为空。
- onReceiveTimeOffset（0x219d60–0x219d64）只有 `mov w0, wzr; ret`，
  不读取状态码、正文或媒体时间偏移。因此“无需等待 ACK”还包括已收到的合法回包
  不作为媒体会话的成功/失败门槛。

0.2.4.37 已经不等待共享 TIME_OFFSET ACK，但 Ready 阶段仍只接受空正文的
`RTSP/1.0 200 OK`。非 200、非空正文或不同原因短语都会让 RTSP 进入 Stopped。
先加入测试后，12 个相关场景均在旧逻辑下复现失败。本次只修改
MiPlayWfdSourceRtspSession 的共享响应分支：

1. 对已写出的初始 CSeq 5，以及已写出的运行期 TIME_OFFSET，合法响应只作观察，
   不因状态码、原因短语或正文结束播放，也不回复响应。
2. 偏移仍只在写入成功后提交。任何迟到或重复回包都不覆盖新偏移，不能完成另一个
   尚未写出的更新。这里的“已提交”不代表已证明接收端应用了该偏移。
3. 保留 RTSP/1.0、三位 100–599 状态码、有效 CSeq 和完整报文边界校验。
   未知、未来及仅准备但尚未写出的序号仍拒绝；OPTIONS 等请求不会被误当作响应。
4. 单音箱仍要求原有空正文 200 ACK，握手、socket 错误、关闭和取消路径均未放宽。

这是有边界的 Windows 适配：没有照搬原生对任意 `RTSP/` 前缀的宽松判断；
原生消费后删除单个响应条目，Windows 继续保留已有的有界序号高水位方式以容纳重复
和迟到回包，没有新增无限增长的待应答集合。

RTSP 会话测试共 56 项通过，全量测试 898/898 通过；新增/替换案例覆盖非 200、
附带正文、原因短语、迟到和重复响应、准备但未写入的新请求、畸形状态行、未知序号、
不完整正文、额外尾部字节，以及单音箱兼容边界。Release 构建 0 警告、0 错误。

真实 aac_mf 重放继续使用左 + 左 + 右和生产 RTSP TCP 控制循环。两个模拟接收端
共发送 11 个初始/运行期回包（500、带正文的 200、451、204），每包分三次写入，
在状态行中间和正文/报文末尾保留 20 ms 间隔；第三个接收端始终不回复 ACK。
正文还包含类似 CSeq 的文本，检查其不会破坏下一条 RTSP 消息的边界。
迟到、重复回包、3.2 秒无 ACK 窗口和随后保活均通过。

| 重放场景 | 每路 AAC / RTP、PES 一致帧数 | AAC 等待 | 其他结果 |
| --- | --- | --- | --- |
| 普通起播 | 225 | 启动后 0/0/0 | 44 组完整 PCR 一致，编码内容年龄 352.9–375.1 ms |
| 两次 350 ms 交付/发送阻塞 | 425 | 启动后 0/0/0 | 84 组完整 PCR 一致，两次共享时间线恢复 |

普通重放有一次既有输出 PTS 修正，共 10.768 ms；阻塞重放有 11 次，共
206.766 ms，传输恢复累计推进 352.100 ms。未更改这些修正算法或恢复策略。
采集、aac_mf、16 帧输入窗口、14 帧预热、AAC FIFO、21.333 ms 基本节奏、
首帧 TIME_OFFSET 算术、PCR 和心跳实现均保持 0.2.4.37 的行为。
上述是合成 PCM、本机 TCP 和实际编码器重放，没有向真实音箱发送这些测试回包，
也没有证据表明此前实机卡顿曾由非 200 TIME_OFFSET 回包触发。

~~~powershell
dotnet test tests/DLNACast.Tests/DLNACast.Tests.csproj -c Release --filter FullyQualifiedName~MiPlayWfdSourceRtspSessionTests
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only --rtsp-ack-probe --runtime-write-probe --time-offset-response-probe
# 阻塞重放另加 --stall-probe；不加 --time-offset-response-probe 则保持原来的空正文 200 重放。
llvm-objdump --dynamic-reloc --demangle artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so | Select-String '0000000000302ad8'
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x214758 --stop-address=0x214850 artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
llvm-objdump --disassemble --demangle --no-show-raw-insn --start-address=0x219d60 --stop-address=0x219d68 artifacts/tablet-apks/2026-08-24/native-compare/lib/arm64-v8a/libaudiomirror-jni.so
~~~

已生成签名包 artifacts/DLNACast.Windows_0.2.4.38_x64_with-dotnet.msix，
SHA-256 为 F14A4842EEE4F2FB51D8184246C744D51CF68762C70D800F197CA68E548646F2。
签名有效，包内版本号为 0.2.4.38；包内、暂存区、898 项测试与 AAC 重放使用的
Core DLL 哈希均为 5460AF861B9CA8614E572D9F90427C0E6E1CFFD24EA4B2119AC199F2ABC79521。
未自动安装或启动应用，核验时安装版本仍为 0.2.4.37；该版旧安装包哈希未变。

## 第十一阶段：首帧 TIME_OFFSET 与 PCR 使用平板共同原点（0.2.4.39）

本轮开始时已安装 0.2.4.38，应用未运行；ADB 设备列表为空。继续使用已保存的
ARM64 ELF，SHA-256 仍为
934840C6ABBF872A7C214AE7C925A0D74ACE1D6B079BD26287FAA4AB9D33FACE。
这次只调整共享多音箱的时钟链；单音箱、采集、aac_mf、AAC FIFO、16 帧输入窗口、
14 帧有序预热和 21.333 ms 基本节奏保持不变。

平板完整依据：

- MicRecorder.muxAudio 首次输出保存 MediaCodec PTS 和 elapsedRealtimeNanos；首次
  `pts = presentationTimeUs - firstAudioTime` 为 0，随后执行既有 50 us 单向修正，
  再把该 PTS 交给 MultiMirrorControl.WriteStream。
- JNI MultiMirrorControl_writeData 在 0x15905c–0x159078 只对首次音频写入调用
  MultiMirrorDisplay 的 updateTimeOffset，然后清除一次性标志；0x159090–0x159098
  把同一 PTS 交给 audioSender::WritePacket。
- MultiMirrorDisplay::updateTimeOffset 的 0x1d7700–0x1d7720 读取选项 0x10003d，
  失败时使用 1,000,000 us；0x1d7724–0x1d7740 计算
  `TimerWork.serverNow - firstAudioPts + playbackDelay`，再把同一结果写给组内发送器。
- TSPacketizer::packetize 的 0x1f860c 直接读取 ALooper::GetNowUs；
  0x1f8610–0x1f8648 计算 `nowUs * 27` 并拆成 PCR base/extension。此路径不读取
  AAC 采集时间，也不减播放延迟。

此前 Windows 在 PLAY 时直接把 `Stopwatch now` 作为 TIME_OFFSET，PCR 则使用
`旧采集时间 - 1 秒`。编码内容有约 300–400 ms 年龄时，这两个值并不是平板的
共同输出原点。现在共享启动顺序调整为：

1. 保持编码器预热；三个反向连接建立后，每个订阅者读取原有 14 帧有序 AAC runway，
   不丢弃、替换或重新编码任何 AAC。
2. PLAY 响应和 post-Open 媒体信息仍按原顺序发送。runway 就绪后，以首个归一化
   AAC PTS 0 计算 `serverNow - 0 + 1,000,000 us`；所有参与者取同一个较晚的
   server clock 候选值。
3. RTSP CSeq 5 在实际写出前替换为该共享 TIME_OFFSET。发送成功后才进入 Ready，
   继续保持 0.2.4.31 起不等待共享 ACK 的行为。
4. 每个 AU 的输出 PTS 与打包时单调时钟在同一共享锁内只计算一次。
   PCR 使用 `packetizationNow * 27`，同一 AU 的 base 和 extension 由所有音箱复用；
   capture timestamp 只保留为编码内容年龄诊断。
5. 阻塞后的运行期 TIME_OFFSET 原有公式已经是
   `currentServerNow - currentMediaPts + playbackDelay`，现在委托给同一生产函数，
   避免初始和恢复路径以后再次分叉。

新增的确定性检查覆盖平板公式、首次 PTS 0、非零首 PTS、负值/溢出边界、
原始 PCR 时钟、同 AU 多接收端缓存、下一 AU 前进、旧 capture-minus-delay 值不再
进入共享输出，以及 CSeq 5 写出前替换。保存的 RTSP/PCR 抓包反算结果与新公式相差
5.290 ms，小于一帧 AAC；抓包只有 90 kHz PCR base，且两条报文并非同一时刻，
因此不把它误写成逐 tick 完全相等。

全量 909/909 测试通过，Release 构建 0 警告、0 错误。真实 aac_mf 本机重放结果：

| 场景 | AAC / RTP、PES | PCR | 恢复结果 |
| --- | --- | --- | --- |
| 普通左 + 左 + 右 | 每路 225 帧一致 | 44/44 组一致且含 27 MHz 小数 | 启动后 AAC 等待 0/0/0 |
| 两次 350 ms 阻塞 | 每路 425 帧一致 | 84 组一致，82 组含小数 | 2 次共享恢复，等待 0/0/0 |
| 左路消费暂停 830 ms | 每路 900 帧一致 | 179 组一致，178 组含小数 | 恢复后等待 0/0/0，补零 0/0 |

普通重放编码内容年龄 354.0–375.4 ms，PTS/PCR 三路一致；生产 RTSP 循环继续通过
初始/运行期 ACK 立即、迟到、缺失，以及 11 个分片非 200/带正文回包。
这些仍是本机真实编码器和 TCP 重放，没有向真实音箱发送媒体，不能代替起播声音、
声学延迟、暂停恢复及 40 分钟连续播放的验收。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.39_x64_with-dotnet.msix`，签名状态为
Valid，包内版本为 0.2.4.39。包内、暂存目录和 Release 构建的 DLNACast.Core.dll
SHA-256 均为 5BCAD544476B7E9A09222415A47910C258452CF42C16E090FA8D6EF02ABA6165；
安装包 SHA-256 为 C38B281B07DBAA8F2A3FE20F08CB378C6F55AC9D594AA286C28A0EFFE25A87EB。
没有自动安装；本机当前仍为 0.2.4.38。

~~~powershell
dotnet test .\DLNACast.slnx -c Release --no-restore
dotnet run --project artifacts/runtime/miplay-window-check/WindowCheck.csproj -c Release -- 'D:\Application\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe' --shared-only
# RTSP 回包和阻塞重放分别使用前文参数，再加 --stall-probe；输入恢复使用 --route-pause-probe --assert-recovery。
~~~

## 后续顺序

1. 在真实音箱验证共享 PCM 有序消费：起播、立体声、暂停后恢复，以及超过 40 分钟
   的连续播放。上一版异常约在第 32 分钟开始，短时正常不足以完成这一项验收。
2. 首帧 TIME_OFFSET 与共享 PCR 的公式、调用时点和组内复用已按平板实现完成；
   若能取得新的平板实际会话抓包，只用于交叉验证接收端实际播放延迟选项和声学结果，
   不再以旧的 PLAY 时钟或 capture-minus-delay PCR 作为回退方案。
3. 最后再处理动态加入音箱及真实网络长期阻塞。当前启动组不等同于平板的历史 AAC
   回填，注入阻塞测试也不等同于长时间硬件播放。

## 第十二阶段：动态加入、阻塞成员回组和运行期控制（0.2.4.40）

这轮继续核对平板 Java 层和同一份 `libaudiomirror-jni.so`。没有改回旧编码器、
最新帧覆盖或整体重启方案；采集、aac_mf、有序 AAC FIFO、初始 14 帧预热、
21.333 ms 基本节奏、共享 PTS/PCR 和已验证的 TIME_OFFSET 公式保持不变。

平板侧确认了三组此前未完整移植的行为：

- `MiplayMultiDisplayManage.onPlay` 在组已经播放时调用 `addMirror`，不会重建
  MultiMirrorControl；`stopSingleMirror` 在成员数大于 1 时只删除指定 sender。
  `onMultiDisplayError` 对 `-6` 最多执行五次 `reConnectMirror`，其他成员继续播放；
  设备随后重新被发现时，`MiplaySessionCallbackManage.handleDevice` 会再次把它加入
  原组。
- native `audioSender::WritePacket` 在新 sender 第一次写入前，先遍历组内历史包队列，
  然后再写当前包。队列在跨度达到 1,000,000 us 时淘汰最旧元素。48 kHz、每帧
  1024 个采样点时稳定保留 47 个 AAC AU，即约 1 秒，而不是起播使用的 14 帧。
  这些历史包是连续突发预灌；新 sender 使用自己的 RTP 序号和 TS 连续状态，进入
  下一个共享实时 AU 后才参与组内发送完成屏障。
- 运行期 Pause `0x0004`、Resume `0x0006`、Close `0x0002`、Heartbeat 和音量命令
  共用同一序号分配器。Resume 按抓包相隔 29 ms 连发两次。接收端 `0x0022` Notify
  中的 `state=3/2` 分别更新暂停/播放状态；停止投送先尝试 Close，再取消传输。

Windows 端现在允许在播放中逐台加入、移除和改变左右声道路由，最多 63 个活动
sender。新成员先突发获得 47 帧历史 AAC，再从明确的未来 AU 边界进入实时共享流；
原有成员不等待这些历史帧，因此不会因新音箱加入而停顿。某一 sender 写入超过
3 秒被判为传输阻塞时，只移除这一台，按 0.5、1、2、3、5 秒进行五次回组；仍失败
则保留其声道分配，每 10 秒执行 SSDP 重新发现，拿到同一 UDN 的最新地址后加入原
共享会话。其他音箱、共享采集、编码器和时间线在整个过程中不重启。

确定性测试新增了 47 帧历史窗口、迟到成员独立 RTP/TS 连续性、未来 AU 加入屏障、
成员替换、Pause/Resume/Close 和接收端状态 Notify。全量 925/925 测试通过，Release
构建 0 警告、0 错误。真实 aac_mf 本机重放使用左 + 左 + 右、三个生产 RTSP TCP
控制循环、830 ms 单路消费暂停和注入阻塞：每路 900 个 AAC/RTP/PES 对齐，左右
声道内容区分正确，179 组 PCR 全部对齐，其中 174 组保留 27 MHz 小数；暂停恢复后
没有 AAC 等待或补零，发送间隔为 14.284–32.668 ms。三路完成运行期控制写入并通过
立即、迟到、永不 ACK、3.2 秒缺失 ACK、重复 ACK 和后续保活。阻塞组合重放记录
3 次传输时间线重定位，累计推进 352.522 ms；测试最终通过恢复断言。

这些结果仍是本机编码器和 TCP 回环，并未发送到真实音箱。本轮已确认的共享音频范围
内，当前尚未闭环的是三项实机证据：播放中动态加入/离线重新发现、暂停后恢复的声学结果，
以及超过 40 分钟连续播放。平板接收端实际采用的播放延迟选项也需要新的实机会话抓包
才能交叉验证；在取得证据前不再改动现有 TIME_OFFSET 和共享 PCR。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.40_x64_with-dotnet.msix`，包内版本为
0.2.4.40，签名状态 Valid，签名证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
73971DD7AD40114F21D4DF61CA053ABDF0E28B3FAFAB8799D4354445196A2F4B；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
3B7BA6775A0C11862EF351D509BB729B91F2B7E9BDA34C8C5FB54FE50ADD4878。没有自动安装，
本机当前安装版本仍为 0.2.4.39。

## 第十三阶段：修正共享起播 PTS 就绪顺序（0.2.4.41）

0.2.4.40 的共享输出时间线保护条件放在了错误的调用点：初始成员必须先取得首个
规范化 AAC PTS，才能计算 TIME_OFFSET，随后才能完成 media/runway 屏障；原代码却
在屏障完成前调用只允许运行期使用的 `GetOutputTimestamp`，因此直接抛出
`The shared media session must be ready before output PTS starts.`，立体声无法起播。

现在初始成员直接根据首个 AAC AU 的 48 kHz/1024 样本位置计算规范化 PTS，只把它
用于 TIME_OFFSET。共享输出时间线仍在 media/runway 屏障完成后的首个实际媒体交付时
启动，PTS/PCR 公式没有改变。迟到加入的 sender 则被动等待原组完成屏障，再读取保留
的 47 帧历史 PTS；取消迟到 sender 不会让原组进入失败状态。

新增取消等待不破坏原组的回归案例。全量 926/926 测试通过。重新编译后的真实
aac_mf 左 + 左 + 右回放完成 900 帧三路 PTS、RTP/PES 对齐，179 组 PCR 全部对齐；
830 ms 单路暂停恢复后的 AAC 等待为 0/0/0，没有补零，运行期 RTSP 控制和注入阻塞
恢复断言继续通过。以上仍是本机 TCP 回环，实机声音需要安装新版后确认。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.41_x64_with-dotnet.msix`，包内版本为
0.2.4.41，签名状态 Valid。安装包 SHA-256 为
9D22EB25C4B49BED6CAF45411B4FB3BC6186B191FA92518DF21C552EABE0C544；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
F1576759D0759D78011CA7E5AAC95AB0FBF094466190DC15AD9DD552CB8B4160。本机当前安装的
仍是会触发该异常的 0.2.4.40，没有自动替换正在使用的应用。

## 第十四阶段：动态加入复用组 TIME_OFFSET（0.2.4.42）

继续核对同一份平板 `libaudiomirror-jni.so` 后，确认动态加入 sender 的时间偏移仍有
一处关键差异。`MultiMirrorDisplay::addMirror`（`0x1D68D0`）在
`0x1D6AEC–0x1D6AFC` 读取组对象 `+0x48` 中已经建立的 `timeOffset`，再通过新 sender
的虚表 `+0x60` 写入；它不会按照回填历史 AAC 的首个 PTS 重新计算。平板的
`resume`（`0x1D73A8`）在 `0x1D745C` 更新同一个组字段，并在
`0x1D747C–0x1D748C` 把同一值传播给每个 sender；`setTimeOffset`（`0x1D75D0`）也是
先更新组字段，再遍历全部 sender。

Windows 端此前让迟到成员用 47 帧历史缓存的首帧 PTS 重新算 TIME_OFFSET，并在历史
回放切到实时 AU 时再次写 TIME_OFFSET。这会让新音箱的接收时钟相对原组跳动约一个
历史窗口，是动态加入后延迟增大或多音箱错位的直接风险。现在共享会话保存唯一的组
TIME_OFFSET：首批成员完成起播同步后初始化；阻塞恢复和 Resume 写入成功后更新；
迟到成员只读取当前组值。历史转实时边界不再二次改时钟。0.2.4.41 修复的共享媒体
就绪顺序、aac_mf、有序 AAC、47 帧历史回填、21.333 ms 基本节奏及 PTS/PCR 公式均未
回退。

新增检查覆盖首批成员一致性、未初始化保护、运行期更新以及迟到 sender 忽略历史
首帧候选值。全量 929/929 测试通过。真实 aac_mf 本机回放完成左 + 左 + 右三路
900 个 AAC AU，RTP/PES 全部对齐，179 组 PCR 全部对齐；830 ms 单路暂停恢复后的
等待为 0/0/0，没有补零。组合阻塞产生 3 次共享时间线重定位并通过恢复断言。以上
仍是本机编码器和 TCP 回环；播放中加入真实音箱、声学同步和长时间播放仍需实机验收。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.42_x64_with-dotnet.msix`，包内版本为
0.2.4.42，签名状态 Valid，证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
DACC8A52A3889DEE1B0D3D9F21F2F415A7EF48AE7A5B8E8A8739E1FFDAAC542A；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
DCD0D26838B411620DD1D5C8D433DD588762C3628995FC8BD2C108CC7D245974。本机当前安装
版本为 0.2.4.41，没有自动安装 0.2.4.42。

## 第十五阶段：运行期组 TIME_OFFSET 单次选值和提交（0.2.4.43）

0.2.4.42 已让动态加入 sender 读取组 `timeOffset`，但阻塞恢复和显式 Resume 仍由
每个 Windows 发送线程各自调用单调时钟并计算候选值。平板
`MultiMirrorDisplay::resume/setTimeOffset` 是在组对象上计算或接收一次值，再把同一值
遍历写给所有 sender；即使 Windows 的共享发送屏障通常给出相同参考时刻，也不应继续
依赖线程恰好同时到达。

现在每次运行期更新使用“操作类型 + 共享媒体位置”作为组更新标识。第一个到达的成员
确定候选 TIME_OFFSET，其他成员即使稍后读取了不同的本地时钟，也复用完全相同的值。
候选值不会立即成为动态加入 sender 可见的组值；至少一条对应 RTSP TIME_OFFSET 已经
实际写出后才提交。每次更新还有组内顺序号，较旧音箱的 socket 写入晚完成时不能把
已经提交的新组值回滚。静音继续发送 AAC，不会触发 live 自动 Pause；这轮没有增加
新的静音控制或更改编码、PTS/PCR、历史回填和发送节奏。

新增确定性检查覆盖同代不同候选只采用首值、写出前不提交、写出后更新，以及旧写入
晚完成不能回滚。全量 930/930 测试通过。真实 aac_mf 左 + 左 + 右回放仍完成三路
900 个 AAC AU，RTP/PES 全部对齐，179 组 PCR 全部对齐；830 ms 单路暂停后的 AAC
等待为 0/0/0，没有补零，三次组合阻塞恢复通过断言。以上是本机编码器和 TCP 回环，
不是实际音箱的声学同步或长时间播放证明。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.43_x64_with-dotnet.msix`，包内版本为
0.2.4.43，签名状态 Valid，证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
FCDB30C9360D10009B8F5CB5724EEE5CB429752921A09D0F947C167F4341D0D0；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
F3E77BB139DEF3784B1946C4D3D840EBAF4B0E52D850866220FEDC5744A5AA22。本机当前安装
版本为 0.2.4.42，没有自动安装 0.2.4.43。

## 第十六阶段：显式 Pause/Resume 按组广播（0.2.4.44）

继续对照平板 `MultiMirrorDisplay::pause/resume` 后，补齐控制范围差异。Windows 原先
虽然共享采集、AAC 和媒体时钟，但公开的 Pause/Resume 仍只进入被调用 transmitter
自己的运行期队列；平板则在组对象上遍历当前 sender。现在共享会话为已经进入播放态
的每个成员注册控制入口，一次显式 Pause 或 Resume 会先快照当前成员、分配一个组代号，
再等待所有成员各自通过原 RTSP 控制连接写出对应命令。播放中移除的成员会立即注销，
不会进入下一次广播。

组 Resume 的同一代号也作为运行期 TIME_OFFSET 更新标识，因此即使不同成员读取媒体
位置和单调时钟的瞬间略有差异，仍只采用第一份候选值并向所有成员写入同一个偏移。
Close 保持逐成员行为，以对应平板 `stopSingleMirror/deleteMirror`；移除一台音箱不会
关闭整个组。静音仍持续发送 AAC，不会恢复旧的自动 Pause。

新增检查覆盖三成员 Pause/Resume 同代广播、代号递增、动态移除后不再广播，以及空组、
重复注册和组 Resume 候选复用。全量 933/933 测试通过。真实 aac_mf 左 + 左 + 右
回放继续完成 900 个对齐 AAC AU 和 179 组对齐 PCR。单独运行 830 ms 路由暂停恢复时
等待为 0/0/0、补零为 0/0；把 RTSP 缺 ACK、三次网络阻塞和路由暂停同时注入时曾出现
同步的 1/1/1、2/2/2 短等待，但帧、PTS、RTP/PES 未分叉且恢复断言均通过。这些仍是
本机调度和 TCP 回环证据，实际 Pause/Resume 声音和多音箱同步需要实机确认。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.44_x64_with-dotnet.msix`，包内版本为
0.2.4.44，签名状态 Valid，证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
BEC57F9A90DF3B2F91B5F7024C321D1A62FA239B7E811B3ED6F333537EAF6840；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
D2A64A48CC243DE38DA1E30B953F5483D07AC04772DACA1DB09E50CD25793AB5。本机当前安装
版本为 0.2.4.43，没有自动安装 0.2.4.44。

## 第十七阶段：确认校准归属并补齐 TimerService 报文边界（0.2.4.45）

继续反汇编同一份 `libaudiomirror-jni.so`，这次把此前保留的 ClockSync 条件补全。
`startMirrorTimeSync`（`0x1D7BFC`）调用 `GetServerInstance`，随后以 `w1 = 1` 调用
`TimerWork::Init`，并传入 `x5 = 0`。`TimerWork::Init` 在 server 分支创建
`TimerService`；空回调时创建默认 `ClockSync`，字段为 offset 0、rate 1.0、anchor 0。
相反，`WifiDisplaySink` 和 `startMirrorTimeSyncClient` 明确调用 `GetClientInstance`，以
`w1 = 0` 创建 `TimerClient`。`TimerClient::updateOutOffset` 才会把算法得到的
offset/rate/anchor 交给 ClockSync。因此投送端只提供同一单调时钟的时间戳服务，速率和
偏移拟合由接收端消费；Windows 不应在播放过程中凭 UDP 往返结果重新调速或连续改写
TIME_OFFSET/PCR。

另一个报文边界仍与原生不同。`TimerService::Sync`（`0x224244–0x2242F4`）先清零
接收缓冲区，以 40 字节上限调用 `recvfrom`；只在返回值小于 1 时不回复。任意正长度
报文都会截断或补零成 40 字节，在 +24 写接收时间、+16 写发送时间，再发送完整 40
字节。共享 Windows 响应器现已采用同样行为；零长度报文仍忽略，单音箱兼容路径仍严格
要求 40 字节。会话仍限定已协商的接收端 IP，避免其他局域网主机的报文终止或占用该
会话的时钟服务。

新增检查覆盖 1、8、15、39、40、41、512 和 65507 字节输入的补零/截断回复，零长度
忽略、来源端口变化、错误来源和旧单音箱失败语义。全量 938/938 测试通过。真实
`aac_mf` 左 + 左 + 右本机回放完成 900 个对齐 AAC AU 和 179 组对齐 PCR；830 ms
路由暂停后的等待为 0/0/0、补零为 0/0。组合 RTSP 缺 ACK、运行期控制、路由暂停和
三次网络阻塞也通过恢复断言，RTP/PES 未分叉。以上仍是本机编码器与 TCP/UDP 回环，
不是实际音箱长播、延迟或声学同步证明。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.45_x64_with-dotnet.msix`，包内版本为
0.2.4.45，签名状态 Valid，证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
FEA5BDEFB55F464CAEA09DC810F534FB83900700E97CB7892A0090EE8B7A7A52；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
C3CB9F97227F269978D9ACE6B585DB5D6747DA3B843720018F3BF9E9DE6FBD42。本机当前安装
版本为 0.2.4.44，没有自动安装 0.2.4.45。

## 第十八阶段：UDP 时钟单次发送失败不再结束共享投送（0.2.4.46）

平板 `TimerService::Sync` 在 `0x2242E0` 调用 `sendto` 后没有检查返回值，随后在
`0x2242EC` 固定返回 1；`TimerWork::threadLoop` 在调用 `Sync` 后同样固定返回 1。
也就是说，一次 UDP 时钟回包发送失败不会结束时钟线程，更不会让媒体组退出。Windows
共享路径此前直接等待 `UdpClient.SendAsync`，任何 `SocketException` 都会使 timerTask
失败，随后被后台任务检查提升成整个投送故障。这是局域网瞬时不可达或接口切换时不必要
的长播中断点。

现在仅在共享平板行为下忽略未取消状态的一次 UDP `SocketException`，下一次接收端请求
会重新取得一对单调时间戳。主动取消、套接字关闭、非 SocketException、接收失败以及
单音箱旧路径仍保持原有失败语义；没有放宽媒体 TCP 写入、RTSP 控制或成员移除规则。
AAC、PTS、PCR、TIME_OFFSET、47 帧历史回填和 21.333 ms 基本节奏均未修改。

新增策略检查覆盖共享/单音箱、已取消/未取消及非 UDP 异常。全量 939/939 测试通过。
真实 `aac_mf` 左 + 左 + 右组合回放完成 900 个对齐 AAC AU 和 179 组对齐 PCR；830 ms
路由暂停后的等待为 0/0/0、补零为 0/0，RTSP 缺 ACK、运行期控制和三次注入阻塞全部
通过恢复断言，RTP/PES 未分叉。该验证仍是本机 TCP/UDP 回环，不是实际网络故障或音箱
长播证明。

已生成签名包 `artifacts/DLNACast.Windows_0.2.4.46_x64_with-dotnet.msix`，包内版本为
0.2.4.46，签名状态 Valid，证书指纹为
CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包 SHA-256 为
A82829E313CC31920A6C5A353D3C80B9EE8CD9B42330307F41A29EAD71495828；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
1658D7E97548A64797CD377B66447F2C7B606B7E9163EB71B9CE48B36E77282B。本机当前安装
版本为 0.2.4.45，没有自动安装 0.2.4.46。

## 第十九阶段：显式 Pause/Resume 同时对齐采集、编码和媒体状态（0.2.4.47）

完整复查平板显式暂停路径时发现，0.2.4.46 只对齐了组控制广播，没有对齐音频源
状态。平板 `CaptureService.PauseMirror` 先调用 `PauseCaptureAudio`，
`MicRecorder.pause` 设置暂停状态并停止 `AudioRecord`，之后才进入
`MultiMirrorDisplay::pause`。恢复顺序相反：先重新启动采集并唤醒编码回调，再更新组
TIME_OFFSET 和恢复各 sender。普通输入静音不进入这条显式控制路径。

原生 `multiAudioSender::pause`（`0x14CB74`）把状态改为 3，`resume`
（`0x14CC08`）改回 2；`WritePacket`（`0x14CDDC`）只在状态 2 时交付 AAC。Windows
此前在显式 Pause 后仍让采集和编码继续运行，也会继续消费已经排队的 AAC，这与平板
不同，容易让接收端暂停期间累积旧音频并在恢复后增加延迟。

现在单音箱和共享组都使用两级显式门控。Pause 先关闭编码输入和媒体输出，再停止并
释放当前 Windows 采集，清除尚未消费的 PCM，最后发送接收端 Pause；Resume 先在原
PCM 目标上重建采集并恢复编码输入，完成组 TIME_OFFSET 和接收端 Resume 后才重新
开放媒体输出。共享组只执行一次采集切换，所有成员仍共用同一代控制和同一组
TIME_OFFSET。播放中的普通静音、采集短缺和两帧以上的采集中断恢复保持原行为：继续
发送 AAC 静音，不会触发显式 Pause。

新增门控取消、采集释放/重建及暂停期间切换采集源的检查。全量 943/943 测试通过。
真实 `aac_mf` 左 + 左 + 右组合回放完成 900 帧无压力回归和 31000 帧、约 11 分钟的
控制缺 ACK、830 ms 路由暂停及三次传输阻塞组合回归。长回放三路 31000 个 AAC、PTS、
RTP/PES 全部对齐，6199 组 PCR 全部一致，左右声道内容不同，同左声道内容一致；所有
AAC 等待均以 12/12/12 同步出现，左右路额外补零为 0。该结果证明本机编码、控制和
传输在组合压力下没有分叉，但仍不是实际音箱的声学延迟或连续播放听感证明。

Release 整体构建为 0 警告、0 错误。已生成签名包
`artifacts/DLNACast.Windows_0.2.4.47_x64_with-dotnet.msix`，包内版本为 0.2.4.47，
签名状态 Valid，证书指纹为 CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。
安装包 SHA-256 为
CD25CDE07FB7DD77B9C55EA2064E49D0F3686BA92604D2C1CD7F23EB76ADBB7A；包内与 Release
构建的 DLNACast.Core.dll SHA-256 均为
57067809A8502D4F266AE568435454E5E9195D141ECF98CD74808CDB5CCA1FF7。47 个包图标文件的
四角 alpha 均为 0，没有重新引入黑色底板。本机当前安装版本为 0.2.4.46，没有自动
替换正在运行的应用。

## 第二十阶段：缩小 PCM 到 AAC 的隐藏输入窗口（0.2.4.48）

这轮只优化 MiPlay 编码排队，不修改已经可听的 `aac_mf -> RTP/TCP`、48 kHz 双声道
4096 字节输入帧、14 帧起播 runway、47 帧动态加入历史、21.333 ms 基本发送节奏、
PTS/PCR、TIME_OFFSET、Pause/Resume 或多音箱发送屏障。已有长播记录中网络发送间隔稳定、
AAC FIFO 仅 0--1 帧，但编码内容年龄长期约 396--435 ms，说明主要积压位于发送之前的
PCM 到 AAC 管线，而不是网络 socket。

`MiPlayOrderedAccessUnitBuffer.DefaultCapacity` 和共享路由的
`MiPlayEncoderInputWindow.DefaultCapacity` 原来同为 16。每个槽对应 1024/48000 秒，16
帧最多允许约 341 ms 未消费输入。使用真实 `aac_mf` 逐档测试后，4、6、8 帧都无法在
期限内产生足够的首批 AAC，10 帧虽可起播但稳态出现 18 次饥饿；12 帧首包约 84--97 ms，
稳态无饥饿，16 帧首包约 87 ms。生产默认值因此只从 16 缩到 12，保留启动余量，不采用
更激进的 10 帧。

修改后的左 + 左 + 右本机真实编码回放把最大待处理输入从 16 降为 12，普通 225 帧回放
的稳态编码内容年龄从约 355--376 ms 降为 292--328 ms，减少约 47--63 ms；三路内容、
PTS、RTP/PES 和 PCR 仍全部对齐，饥饿为 0/0/0。带缺失 ACK、830 ms 单路暂停和三次
传输阻塞的 900 帧组合回放同样通过，没有路由独立补零。

约 11 分钟、31000 帧组合长回放中，31000 个 AAC/PTS/RTP/PES 全部对齐，6199 组 PCR
全部一致，最大待处理输入保持 12。总饥饿只同步出现 2/2/2，左右路额外补零为 0/0；
830 ms 暂停后的恢复间隔为 1.180--45.547 ms，三次传输阻塞、碎片/迟到/缺失 RTSP ACK
和后续保活全部恢复。这仍是本机编码器与 TCP/UDP 回环验证，实际音箱能减少多少声学
延迟以及超过 40 分钟的听感仍需安装后实测。

全量 943/943 测试通过，Release 整体构建为 0 警告、0 错误。已生成签名包
`artifacts/DLNACast.Windows_0.2.4.48_x64_with-dotnet.msix`，包内版本为 0.2.4.48，
签名状态 Valid，证书指纹为 CD31FF9C6FBEDC3B07D5FE6DA61C60257C21CEA5。安装包
SHA-256 为 4C9FC1AE8791C6D5E0C2C995424EFCCEFAE55EB98FD75D040BE61B10B053E6；包内与
Release 构建的 DLNACast.Core.dll SHA-256 均为
105C052EED32217E0F6708436BA5925A03C080C141697B51FD31692546C72FD6。包内包含 47 个
PNG 图标资源。本机没有自动安装或替换正在运行的应用。
