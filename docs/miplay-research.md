# MiPlay / MiConnect 实验协议研究

> 更新：2026-07-19  
> 范围：本记录只覆盖本项目的离线协议实现、两台小爱音箱 S12 的被动发现/连接观察，以及对本地 Android 样本的静态分析。它不表示 Windows 端已经具备可用的 MiPlay 投送能力。

`DLNACast.Core.MiPlay` 与正常 DLNA 投送流程隔离。目前代码只包含可离线测试的发现、帧编解码和诊断原语；在完整握手得到验证前，不会主动向真实音箱发送猜测性的 MiPlay 报文。

## 结论状态与重要更正

| 状态 | 含义 |
| --- | --- |
| 已实测 | 在本局域网的 S12 上被动观察或无副作用连接确认。 |
| 静态确认 | 能从指定 APK 的原生代码、调用关系或反汇编直接证明。 |
| 推断 / 待验证 | 有迹象但尚不能作为互操作实现依据。 |

**更正：**早期记录把 `appsData` 中应用 5 的前两个字节（样本为 `0x0483`，即 1155）当作可直接连接的控制端口，并把 TCP 8899 的首帧命令 `0x0028` 直接称作 SafetyAuth。两项结论都过度推断，现已撤回：

- `1155/TCP` 以及大小端互换值 `33540/TCP` 在两台 S12 上均不可用，而 `8899/TCP` 可以接受连接。因此 `0x0483` 目前只能视为 `appsData` 中观察到的应用元数据/旧值，不能作为 S12 的公开可用端口。
- TCP 8899 会先发出一个命令为 `0x0028`、负载是 16–17 位十进制字符串的帧；它**不等同于**现代 JSON/OPack SafetyAuth（`0x1402`）。在同一原生样本的 `CmdSource::onRecvCmd` 中已恢复其旧式挑战路径：收到 `0x0028` 后，以同序号发送 `0x0029` 的 HMAC-SHA1 文本，随后才发起现代 `0x1400` SafetyInfo。尚未通过实机确认挑战由哪一端生成、是否所有固件版本一致，或此后会话能否完成。

## 实机发现：MiConnect 与设备归属

两台小爱音箱 S12 可通过 `_mi-connect._udp.local` 被动发现（本轮样本地址为 `192.168.10.7` 和 `192.168.10.4`）。其 TXT 记录表明：

- `apps=[5]`：应用 5 是本次 MiPlay 音频研究关联的应用条目；
- `sec=2`：设备声明了受保护的传输/会话能力，不能据此假定存在匿名控制协议；
- MiConnect 使用 UDP `56666` 上的 CoAP 端点 `/32`，其消息负载为 protobuf；
- `appsData` 是多应用二进制容器，须先按应用编号提取数据，不能把整体或固定偏移解释为单一端口。

`appsData` 中的 JSON 还包含 `mico.device_id`。实机中它与同一音箱的 SSDP/UPnP UDN 一致，可用于把 mDNS 发现结果与 DLNA MediaRenderer 稳定去重：

- `小爱音箱-6333` → `db1ea062-7563-4604-8349-dac605303a5e`
- `小爱音箱-7503` → `759c0613-5052-4a81-a189-ca76d3432438`

这只是设备归属标识，**不是** Lyra/Continuity 身份凭据，也不是可派生会话密钥的材料。

## TCP 8899 的被动观察

早期连接只用于确认服务器行为：服务器先发出符合下述 9 字节控制帧格式的消息，魔数 `$`（`0x24`）、命令 `0x0028`、序号、长度与一个 16–17 位十进制负载。它证明 8899 是一个实际可达的旧式控制入口，但当时尚未证明后续认证。

2026-07-19 在用户明确授权下，实验探针对两台 S12 仅发送了一次由本地原生逻辑复原的 `0x0029` 应答；两台设备均未断连，并各自回发 `0x0022`。这确认 `0x0028 → 0x0029` 的实机帧、HMAC 和序号匹配，但仍**不**代表已建立完整 MiPlay 信任通道或可播放。

因此 8899 仍标为“实验性认证已部分验证的可达端口”，而非“已完成认证的 MiPlay 控制通道”。默认 `--miplay-safety-probe` 不会回应未知命令、不会发送 `0x1400` 或媒体数据；单独的 `--miplay-safety-offer` 仅在本节记录的严格条件下额外发送一次已静态核对的 offer，且绝不回应其后的未知帧。任何扩大实机发包范围仍须基于下一条静态证据。

## 已确认的旧式控制帧原语

原生 `CmdSource::getCmdData`（虚拟地址 `0x17b23c`）直接写入如下帧头，现有 C# `MiPlayCommandFrameCodec` 与其一致：

```text
offset  size  内容
0       1     0x24 ('$')
1       2     命令号，u16 big-endian
3       2     序号，u16 big-endian
5       4     负载长度，u32 big-endian
9       n     负载
```

`OpenDevice` 的命令号为 `0`。静态样本构造的载荷为：

```text
wfd://<sender-ip>:<dynamic-media-port>?mirrorMode=1
```

这说明控制帧格式和打开媒体回连的字符串是可验证原语；不意味着 Windows 已具备成功打开实际会话的前置认证。

## 原生样本中确认的现代安全通路

分析样本：

- 包名：`com.milink.service`
- 版本：`18.0.0.3.2606041114`
- APK SHA-256：`0dfc04fd549e38d4c7b174e5eff739d41907acab51d3a4662ed006fb428ac7a0`
- Xiaomi/MIUI 签名证书 SHA-256：`c9009d01ebf9f5d0302bc71b2fe9aa9a47a432bba17308a3111b75d7b2149025`
- 原生库：`lib/arm64-v8a/libaudiomirror-jni.so`

以下结论来自这个样本的静态反汇编。除下面单列的旧式挑战分支外，现代命令通路的结论不应自动投射到 8899 的任意设备或固件版本。

### 命令号与分发

`CmdSource::onRecvCmd`（`0x1802bc`）的分发确认：

| 命令 | 已确认的处理函数 | 含义边界 |
| --- | --- | --- |
| `0x1400` | `sendSafetyInfo` | 发送本端支持的安全类型集合。 |
| `0x1401` | `dealSafetyInfoAck` | 安全信息应答；成功返回 `0` 后，`onRecvCmd` 会继续触发 `sendSafetyAuth` 发出本端 `0x1402`。 |
| `0x1402` | `dealSafetyAuth` | 接收对端认证挑战并生成应答。 |
| `0x1403` | `dealSafetyAuthAck` | 接收本端挑战的认证应答。 |

`sendSafetyAuth`（`0x17d1d0`）在当前认证消息为空时，取微秒时间戳的十进制文本，计算 MD5，并发出 JSON：

```json
{"authMsg":"<MD5(timestamp-microseconds-as-decimal)>"}
```

它以命令 `0x1402`、值类型 `30` 发送。接收端的 `dealSafetyAuth` 会解析 `authMsg`，按照协商的算法用当前 `mAuthKey` 计算 HMAC，然后以 `0x1403` 返回包含 `result` 和 `authMsgAck` 的 JSON。样本明确支持的 HMAC 算法号为：`1 = MD5`、`2 = SHA-1`、`4 = SHA-256`。

### 旧式 `0x0028` 挑战与 `0x0029` 应答

`CmdSource::onRecvCmd` 的跳转表将命令 `0x0028` 分派到 `0x180b24`。该分支逐字节复制挑战负载（并非 JSON 或 OPack），计算：

```text
legacyKey = lowercaseHex(MD5(UTF-8("0.0.0.0")))
response  = lowercaseHex(HMAC-SHA1(key = UTF-8(legacyKey), message = rawChallengeBytes))
```

随后它以**相同的 16 位序号**通过旧式 `$` 帧发送命令 `0x0029`，负载为 response 的 ASCII 十六进制文本。若当前会话已有 `SafetyKeyDeal`，同一分支会紧接着调用 `sendSafetyInfo`，发出 `0x1400`；它不会把 `0x0028` 当作现代 `0x1402`。

项目中的 `MiPlayLegacySafetyChallengeCodec` 仅实现这一纯离线计算与帧编码。示例挑战 `legacy-challenge` 的应答为 `1bfbbecf1244c16add4362959aa0ccc7b6e8a0c4`。它不连接 8899，也不自动串联后续安全协商。

### 2026-07-19 真机最小验证

探针运行在 WLAN `192.168.10.9`，仅连接 `8899/TCP`、读取第一帧、严格校验其为 `0x0028` 后回写一次 `0x0029`；每次最多观察后续一帧，且不发送任何其他命令。

| 设备 | 收到的挑战 | 发送的应答 | 随后收到的帧 | 结论 |
| --- | --- | --- | --- | --- |
| `192.168.10.4`（小爱音箱-7503） | `0x0028`，seq `0x00F7`，`6399403323514405` | `0x0029`，同 seq，`1bf4ec8cd0686ec6f9f57729e453ef75907fc68e` | `0x0022`，seq `0x00F8`，`04 6d 6f 64 65 03 02` | 应答被接受并推进；没有断连。 |
| `192.168.10.7`（小爱音箱-6333） | `0x0028`，seq `0x009B`，`15056381675087147` | `0x0029`，同 seq，`c32613f30b7ac82d45f59af376acf6c879dc2d30` | `0x0022`，seq `0x009C`，`04 6d 6f 64 65 03 02` | 与第一台行为一致。 |

`CmdSource::onRecvCmd` 将 `0x0022` 分发给 `onRecvNotify`；该载荷可解析为标签 `mode`、值类型 `3`、单字节值 `2`。原生接收分支只把它上报给会话监听器，不构造回复帧。因此探针记录后关闭连接，未对它发送猜测性的确认或控制命令。

#### 2026-07-19 SafetyInfo offer 实测

在对 `192.168.10.4` 的一条新连接中，探针先按上述限制处理 `0x0028`（seq `0x0100`，挑战 `643009625691733`）并发送同序号的 `0x0029`（`35044ee1314a5290ddd6fbc7043e87daa4ba1398`）。随后仅额外发送一次由当前原生 `CmdSource` 构造函数和 `sendSafetyInfo` 恢复出的精确 `0x1400` offer，序号为 `0x0001`：

```json
{"authKeyTypes":"1","authAlgorithmTypes":"7","integrityTypes":"1","aesKeyTypes":"1","aesIvTypes":"3"}
```

设备在五秒观察窗口内依次发送了 `0x0022` 的 `mode=2`、`mediaInfo`、`state=3` 通知，并返回同序号的 `0x1401`（OPack `ack`、值类型 30）。其 JSON 是：

```json
{
  "aesIvType": "2",
  "aesKeyType": "1",
  "authAlgorithmType": "4",
  "authKeyType": "1",
  "integrityType": "1",
  "result": "0"
}
```

这证明该 `0x1400` 的外层 `$` 帧、OPack 内层、字段名与字段顺序均已被真机解析。早期记录曾把 `result="0"` 解释为拒绝；2026-07-19 的只读复验显示，两台 S12 在该结果后继续发送加密 `0x1402` challenge，因此项目现在把这个精确选择 `(authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2)` 视作 observed S12 SafetyInfo selection。这仍不是播放通道可用的证据：只有 `0x1402` 被解成完整 `cmd/authMsg` 后，才允许设计最小、可逆的 `0x1403` 认证回复测试。

紧接着设备发出原始 `0x1402`（seq `0x0000`）并关闭连接；探针没有回发 `0x1403`、媒体或其他数据。完整负载如下：

```text
000701E0021F4C4CB50810D7D40C897EE73090514B08615C41BDAD39751CEEB7CCCD0A6BAC5B6DD2AE5EAC7A82635D5FD69B654537560166E1385F1BD610314059C6A3AF30C1DAE517
```

其中 `01 E0 02` 与 `SafetyDataDeal` 静态确认的版本、加密/填充长度/完整性标志一致。后续对 `decryptData` 的反汇编已确认 `0x0007` 表示“头长减二”：该样本的完整头长为 9 字节，完整性值位于偏移 5–8，密文从偏移 9 开始（详见下文的 V1 头恢复）。该样本随后被离线复原：当会话端点为 local `192.168.10.9:9970`、peer `192.168.10.4:8899`，且 authKey 输入顺序为 `peerIPv4 + peerPort + localIPv4 + localPort` 时，AES key 使用 authKey 前半段；虽然原生 `genAesIv(type=2)` 静态分支取后半段，但 S12 inbound `0x1402` 只有使用 authKey 前半段作为 IV 时才解成完整 OPack `cmd` / JSON `authMsg`。探针在该阶段仍未回复 `0x1403`、媒体或其他数据。

#### 2026-07-19 SafetyAuth `0x1402` 只读解密复验

复验探针发送边界固定为：`0x0036(1)` 原生版本 `3.1.6030516\0`、对 server-first `0x0028` 的同序号 `0x0029`、以及 `0x1400(2)` SafetyInfo offer。之后只接收并尝试解密 `0x1402`，不会发送 `0x1403`、媒体或播放控制。

| 设备 | 本地 TCP 端点 | 设备版本帧 | 命中的 AES 候选 | 结果 |
| --- | --- | --- | --- | --- |
| `192.168.10.4` | `192.168.10.9:3864` | `0x0037` = `2.1.5091615\0` | `peer-first:observed-s12-inbound-iv-type1` | `0x1402` 解成 OPack `cmd`，JSON 字段 `authMsg`，长度 32 |
| `192.168.10.7` | `192.168.10.9:3869` | `0x0037` = `2.1.4052010\0` | `peer-first:observed-s12-inbound-iv-type1` | `0x1402` 解成 OPack `cmd`，JSON 字段 `authMsg`，长度 32 |

同一次探针还尝试了原生 `aesIvType=2` 后半段 IV，以及 diagnostic local-first 端点方向；这些候选没有产生完整 `cmd/authMsg`。因此当前阻塞点已从“无法解密 challenge”推进为“是否发送最小 `0x1403` HMAC-SHA256 认证回复”的可控测试设计问题。

#### 2026-07-19 最小 `0x1403` SafetyAuth 回复验证

在离线测试固定 `0x1403` 帧结构、HMAC-SHA256 输入、加密 SafetyData 外壳和失败候选后，对单台 `192.168.10.4` 执行一次受限认证回复探针。发送边界为：`0x0036(1)` 原生版本、对 server-first `0x0028` 的同序号 `0x0029`、`0x1400(2)` SafetyInfo；只有在唯一解出 `0x1402` 为 OPack `cmd/authMsg` 后，才发送一次加密 `0x1403`，之后只观察。

| 项 | 结果 |
| --- | --- |
| 本地 TCP 端点 | `192.168.10.9:14388` |
| 设备版本帧 | `0x0037` = `2.1.5091615\0` |
| `0x1401` selection | `result=0, authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2` |
| `0x1402` 解密候选 | `peer-first:observed-s12-inbound-iv-type1` |
| `0x1402` 结果 | OPack `cmd`，值类型 30，JSON 字段 `authMsg`，长度 32 |
| 发送的认证回复 | `0x1403`，seq `0x0000`，SafetyData 加密负载长度 121，HMAC 算法 SHA-256；未打印 authKey 或 HMAC 文本 |
| 设备后续行为 | 设备在累计 6 个 follow-up frame 后关闭 TCP 连接；未返回可解析的认证成功、`DealSafetyDone` 标志或下一状态帧 |

本次没有发送 RTSP、音频、播放或其他业务控制帧，也没有尝试第二种 IV/方向。结论是：`0x1403` 的最小回复已经按当前证据链发送过一次，但真机**尚未明确确认认证通过**。连接关闭可能代表认证失败、缺少源端自己的 `0x1402` 互验、出站 CBC 状态/IV 方向仍不匹配，或认证后等待业务命令超时；现有证据不足以进入媒体或播放阶段。

#### 2026-07-19 `0x1403` 后续静态复盘

对 APK 原生库的限址反汇编复核后，`onRecvCmd`（`0x1802bc`）给出了完整认证状态机：`0x1401` 分支在 `0x1808b8` 调用 `dealSafetyInfoAck`（`0x17c5f0`）；当返回值为 `0` 时跳到 `0x181240`，再于 `0x181244` 调用 `sendSafetyAuth()`。`dealSafetyInfoAck` 本身负责解析 `0x1401`、调用 `genAuthKey`/`genAesKey`/`genAesIv`，并在 `0x17cfcc` 构造 `SafetyDataDeal`。

`sendSafetyAuth()`（`0x17d1d0`）本身仍已静态确认：本端 `authMsg` 为空时，它使用 `GetNowUs -> to_string -> MD5` 生成本端挑战，并通过 `sendCmdData2(0x1402, valueType=30, ack=false)` 发出。`sendCmdPayload`（`0x17b858`）在 `CmdSource +0x3c0` 非空时会先调用 `SafetyDataDeal` vtable 的加密函数，再套 `$` 命令头。因此，本端 `0x1402` 如果由外层状态机触发，应当也是 SafetyData 加密后的 `0x1402`。

`SafetyDataDeal` 的构造函数为同一 key/IV 创建了独立的 encrypt/decrypt AES-CBC context；`encryptData` 使用对象 `+0x40` 的加密 context，`decryptData` 使用对象 `+0x100` 的解密 context。`AES_CBC_encrypt_buffer` / `AES_CBC_decrypt_buffer` 会推进各自方向的 CBC IV 状态，所以如果后续设计“先发本端 `0x1402`、再回对端 `0x1403`”的互验 probe，第二个出站 SafetyData 不能重新使用初始 IV。项目已加入 `MiPlaySafetyDataSessionCipher` 和对应单测来覆盖这个会话级 CBC 状态；当前一次性 codec 的旧向量保持兼容。

`0x1402` 分支在 `0x180e8c` 调用 `dealSafetyAuth`，该 handler 对对端 `authMsg` 计算 HMAC 并发 `0x1403`，但 `onRecvCmd` 随后直接返回观察循环，不会宣布认证完成。`0x1403` 分支在 `0x18094c` 调用 `dealSafetyAuthAck`；当返回值为 `0` 时跳到 `0x1811fc`，再于 `0x181200` 调用 `DealSafetyDone()`（`0x17be70`）。因此，native 完成条件是：本端先由 `sendSafetyAuth` 发 `0x1402`，随后收到并验证对端 `0x1403 authMsgAck`。这个静态触发链已经闭环；真机验证见下一节。在认证完成前不能发送媒体或播放控制。

#### 2026-07-19 最小互验 SafetyAuth 真机验证

在上述静态触发链闭环后，新增 `--miplay-native-safety-mutual-auth-probe`。它只发送认证帧：`0x0036`、对 server-first `0x0028` 的 `0x0029`、`0x1400`、本端一次加密 `0x1402`，以及最多一次对设备 `0x1402` 的加密 `0x1403`；随后只解密并校验设备 `0x1403 authMsgAck`，不发送媒体、RTSP、播放、openDevice 或其他业务控制。

第一次互验尝试已推进到设备返回 `0x1403`，但本地 parser 误把 `result:"0"` 当作失败而拒绝。离线复盘确认该 `0x1403` 在连续 inbound CBC state 下可解成 OPack `ack`，JSON 包含 `authMsgAck` 和 `result:"0"`。因此 `MiPlaySafetyAuthCodec.TryDecodeAcknowledgement` 已调整为接受 `result` 为 `"0"` 或 `"1"`，但本端生成的 ack 仍保持当前 APK 静态路径使用的 `"1"`。

修正后，对单台 `192.168.10.4` 执行一次互验 probe：

| 项目 | 结果 |
| --- | --- |
| TCP 端点 | local `192.168.10.9:13460`，peer `192.168.10.4:8899` |
| 前置帧 | `0x0036` seq `0x0001`、`0x0029` seq `0x013F`、`0x1400` seq `0x0002` |
| SafetyInfo | `0x1401 result=0`，选择 `(authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2)` |
| 本端 challenge | 发送加密 `0x1402` seq `0x0003`，SafetyData 长度 `73`，candidate `peer-first:observed-s12-inbound-iv-type1` |
| 设备 challenge | 收到 `0x1402` seq `0x0000`，同一 candidate 解成 OPack `cmd/authMsg`，JSON 长度 `53` |
| 本端 acknowledgement | 发送加密 `0x1403` seq `0x0000`，SafetyData 长度 `121` |
| 设备 acknowledgement | 收到 `0x1403` seq `0x0003`，解成 OPack `ack/authMsgAck`，HMAC 校验通过 |
| 结束条件 | 探针记录 mutual SafetyAuth completed 后立即停止；未发送媒体、RTSP、播放或业务控制 |

这说明当前实现已经在 `192.168.10.4` 上完成 SafetyAuth 认证互验，满足本地复原的 native `0x1403 -> DealSafetyDone` 前置条件。该结论仍只覆盖认证层：媒体协商、回连、流发送、播放控制和低延迟链路尚未验证。

#### 2026-07-19 认证后只读观察验证

继续限址静态复核 `DealSafetyDone()`（`0x17be70`）后，post-auth 边界被收窄为“认证完成标志 + listener 通知 + 定时器”：该函数会设置 `CmdSource +0x3a8 = 1`，记录认证耗时，在 listener 存在时通过 vtable offset `0x50` 发出事件值 `0x00030D41`，并在 `CmdSource +0xfc != 1` 时调用 `scheduleReaper()` 与 `scheduleKeepAlive()`。`CmdSource::onMessageReceived` 的跳表已静态复原：`what=6` 进入 reaper/liveness 分支，`what=7` 直接发送 heartbeat。构造函数默认值显示 `CmdSource +0xf0 = 5` 秒（keepalive interval）、`+0xf4 = 15` 秒（liveness window）、`+0xf8 = 1` 秒（reaper interval）、`+0xfc = 2`。

`sendHeartBeat()` / `what=7` 分支发送空 payload 的 `0x001a` 并递增 `CmdSource +0x2c0` 序号，`sendHeartBeatAck(int)` 发送空 payload 的 `0x001b`。`sendCmdPayload()` 在 `CmdSource +0x3c0` 的 `SafetyDataDeal` 存在时无条件调用 vtable offset `0x10` 加密函数，再套 `$` 命令头；因此静态上 `0x001a`/`0x001b` 也应当使用 SafetyData 加密，即使原始 payload 长度为 0。项目已添加离线单测覆盖“空 payload 在会话 CBC state 下加密为 25-byte SafetyData 并封装为 `0x001a` 帧”。

这些证据说明 native 认证后依赖 keepalive/reaper 定时器或 listener 上层事件继续维持命令会话；不过项目尚未向真机发送 `0x001a`/`0x001b`，也尚未确认设备在无上层业务帧时的关闭策略。因此本阶段仍只实现离线 heartbeat 原语和只读观察模式，不发送 heartbeat/control。

新增 `--miplay-native-safety-mutual-auth-observe-probe=<IPv4>` 后，对单台 `192.168.10.4` 做一次实机复验。它发送的认证帧与互验 probe 相同，认证完成后继续保持连接读 5 秒；在该窗口内不发送 heartbeat、RTSP、音频、播放、openDevice 或任何其他 post-auth 控制帧。

| 项目 | 结果 |
| --- | --- |
| TCP 端点 | local `192.168.10.9:14931`，peer `192.168.10.4:8899` |
| 前置帧 | `0x0036` seq `0x0001`、`0x0029` seq `0x0143`、`0x1400` seq `0x0002` |
| 设备前置状态帧 | 只读收到 `0x0037`、三个 `0x0022` 状态/媒体信息帧；探针未回复这些帧 |
| SafetyInfo | `0x1401 result=0`，选择 `(authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2)` |
| 本端 challenge | 发送加密 `0x1402` seq `0x0003`，SafetyData 长度 `73`，candidate `peer-first:observed-s12-inbound-iv-type1` |
| 设备 challenge | 收到 `0x1402` seq `0x0000`，同一 candidate 解成 OPack `cmd/authMsg`，JSON 长度 `53` |
| 本端 acknowledgement | 发送加密 `0x1403` seq `0x0000`，SafetyData 长度 `121` |
| 设备 acknowledgement | 收到 `0x1403` seq `0x0003`，解成 OPack `ack/authMsgAck`，HMAC 校验通过 |
| post-auth 只读观察 | 认证完成后未再收到新的 post-auth command；设备主动关闭 TCP。累计 follow-up frame 数为 `7`，其中包含认证前状态帧、`0x1401`、`0x1402`、`0x1403` |
| 发送边界 | 认证完成后没有发送任何数据；日志明确记录 `no post-auth data was sent` |

随后对第二台 S12 `192.168.10.7` 做同一 observe-only 复验，发送边界完全相同，仍未发送 heartbeat、RTSP、音频、播放、openDevice 或其他业务控制帧。

| 项目 | 结果 |
| --- | --- |
| TCP 端点 | local `192.168.10.9:12391`，peer `192.168.10.7:8899` |
| 设备版本帧 | `0x0037` = `2.1.4052010\0` |
| 前置帧 | `0x0036` seq `0x0001`、`0x0029` seq `0x00DE`、`0x1400` seq `0x0002` |
| 设备前置状态帧 | 只读收到三个 `0x0022` 状态/媒体信息帧；探针未回复这些帧 |
| SafetyInfo | `0x1401 result=0`，选择 `(authKey=1, authAlgorithm=4, integrity=1, aesKey=1, aesIv=2)` |
| 本端 challenge | 发送加密 `0x1402` seq `0x0003`，SafetyData 长度 `73`，candidate `peer-first:observed-s12-inbound-iv-type1` |
| 设备 challenge | 收到 `0x1402` seq `0x0000`，同一 candidate 解成 OPack `cmd/authMsg`，JSON 长度 `53` |
| 本端 acknowledgement | 发送加密 `0x1403` seq `0x0000`，SafetyData 长度 `121` |
| 设备 acknowledgement | 收到 `0x1403` seq `0x0003`，解成 OPack `ack/authMsgAck`，HMAC 校验通过 |
| post-auth 只读观察 | 认证完成后未再收到新的 post-auth command；设备主动关闭 TCP。累计 follow-up frame 数为 `7` |
| 发送边界 | 认证完成后没有发送任何数据；日志明确记录 `no post-auth data was sent` |

结论：SafetyAuth 互验现在已在两台 S12（`192.168.10.4` / `192.168.10.7`）上重复验证，且本地完成条件与 native `0x1403 -> DealSafetyDone` 静态路径一致。两台设备在认证完成后、源端保持静默时都会关闭 8899/TCP；这支持下一步优先静态闭环 keepalive/reaper 或第一个无媒体 post-auth 控制入口，而不是直接进入媒体、RTSP 或播放。

#### 2026-07-19 单次 post-auth heartbeat 真机验证

在静态确认 `what=7 -> sendHeartBeat() -> sendCmdPayload(0x001a, empty)` 且离线单测固定 SafetyData 空 payload 后，新增 `--miplay-native-safety-mutual-auth-heartbeat-probe=<IPv4>` 并对单台 `192.168.10.4` 执行一次受限实机测试。发送边界为：完整 SafetyAuth 互验成功后，只额外发送一次 SafetyData 加密空 payload 的 `0x001a` heartbeat；随后只读观察，不发送第二次 heartbeat、`0x001b` ack、RTSP、音频、播放、openDevice 或其他业务控制帧。

| 项目 | 结果 |
| --- | --- |
| TCP 端点 | local `192.168.10.9:1915`，peer `192.168.10.4:8899` |
| 前置认证 | `0x0036` / `0x0029` / `0x1400` / 本端加密 `0x1402` / 本端加密 `0x1403` 均按互验 probe 成功完成 |
| 设备 acknowledgement | 收到 `0x1403` seq `0x0003`，解成 OPack `ack/authMsgAck`，HMAC 校验通过 |
| post-auth heartbeat | 发送 `0x001a` seq `0x0004`，SafetyData 加密 payload 长度 `25`，candidate `peer-first:observed-s12-inbound-iv-type1` |
| 设备后续行为 | 未返回 `0x001b` 或其他 post-auth command；设备主动关闭 TCP。累计 follow-up frame 数仍为 `7` |
| 发送边界 | `0x001a` 后没有发送任何数据；日志明确记录 `no further data was sent` |

结论：单次 SafetyData 加密 `0x001a` heartbeat 的帧结构已被实现并实机发送，但它本身没有让 S12 维持 8899/TCP，也没有得到可验证的 `0x001b` ack。后续不应把 heartbeat 当作认证后最小可用控制面；更可能缺的是 native listener 的 `DealSafetyDone` 上层回调后动作、正确时序，或第一个真正的无媒体状态/会话控制入口。

#### 2026-07-19 post-auth device-info 上层静态闭环

继续从 Java 与 native 两侧复核 `DealSafetyDone()` 的 listener 路径后，post-auth 最小下一步被收窄为 device-info 查询，而不是 heartbeat 或 open/play/media：

- native `DealSafetyDone()` 通过 listener 上报 `0x00030D41`，Java `CmdSessionControl.onCmdSessionInfo(200001, extra)` 将它记录为 `CMD_SESSION_INFO_CONNECTED`，并调用 `CmdClientCallback.onSuccess()`；
- `MiplaySessionCallbackProxy.onSuccess()` 只把 message `38` 投递给 `MiPlayAudioService` 主 Handler；
- `MiPlayAudioService.cmdSessionSuccess(...)` 的实际顺序是：先 `cmdSessionControl.getDeviceInfo()`，再设置 `CmdSessionState=1`，随后才调用 `setLocalDeviceInfoSourceName(mac, 1)` 与 `setLocalDeviceInfo(mac)`；
- native `CmdSource::getDeviceInfo()`（`0x1779a4`）直接递增 `CmdSource +0x2c0` 序号并调用 `sendCmdPayload(command=0x001e, payload=null, len=0)`；`CmdSource::setLocalDeviceInfo()`（`0x1771e8`）则调用 `sendCmdPayload(command=0x0058, payload=<bytes>, len=<bytes>)`。

因此此阶段的最小可逆探针只实现 `0x001e getDeviceInfo`：它必须在完整 SafetyAuth 互验之后发送一帧 SafetyData 加密空 payload，然后只读观察并尝试按同一个入站 CBC state 解密响应；它不会发送 `0x0058 setLocalDeviceInfo`、`openDevice`、媒体信息、RTSP、音频或播放控制。`0x0058` 在下一个离线阶段恢复 payload 字段与来源语义，恢复完成前不进入真机发送范围。

#### 2026-07-19 单次 post-auth getDeviceInfo 真机验证

在上述静态闭环与 73/73 离线测试通过后，对单台 `192.168.10.4` 执行一次 `--miplay-native-safety-mutual-auth-device-info-probe=<IPv4>`。发送边界为：完整 SafetyAuth 互验成功后，只额外发送一次 SafetyData 加密空 payload 的 `0x001e getDeviceInfo`；随后只读观察，不发送 `0x0058 setLocalDeviceInfo`、第二个 `getDeviceInfo`、heartbeat、RTSP、音频、播放、openDevice 或其他业务控制帧。

| 项目 | 结果 |
| --- | --- |
| TCP 端点 | local `192.168.10.9:5281`，peer `192.168.10.4:8899` |
| 前置认证 | `0x0036` / `0x0029` / `0x1400` / 本端加密 `0x1402` / 本端加密 `0x1403` 均按互验 probe 成功完成 |
| 设备 acknowledgement | 收到 `0x1403` seq `0x0003`，HMAC 校验通过，满足本地 `DealSafetyDone` 前置条件 |
| post-auth getDeviceInfo | 发送 `0x001e` seq `0x0004`，SafetyData 加密 payload 长度 `25`，candidate `peer-first:observed-s12-inbound-iv-type1` |
| 设备后续行为 | 未返回 device-info ack/notify 或其他 post-auth command；设备主动关闭 TCP。累计 follow-up frame 数仍为 `7` |
| 发送边界 | `0x001e` 后没有发送任何数据；未发送 `0x0058`、heartbeat、openDevice、媒体或播放控制 |

结论：`0x001e getDeviceInfo` 的命令号、序号和 SafetyData 空 payload 形态已按 APK 静态证据实现并实机发送，但单独发送它仍不能让 S12 维持命令会话，也没有得到可解密响应。当前缺口更可能不只是“第一条 Java 上层调用”，而是 native 会话对象里某个尚未复原的连接状态、listener 环境、设备能力/账号上下文，或 `0x0058 setLocalDeviceInfo` 及其前置字段。下一步改为先离线恢复 `0x0058` payload；在发送顺序、双帧边界和可逆失败行为明确前，仍不进入真机发送范围。

#### 2026-07-19 `0x0058 setLocalDeviceInfo` payload 静态闭环

继续沿 `MiPlayAudioService.cmdSessionSuccess(...)` 的 Java 上层顺序恢复 `0x0058`。APK 18.0.0.3 的静态证据显示，认证完成后上层会调用两次 `CmdSessionControl.setLocalDeviceInfo(byte[])`，二者都进入 native `CmdSource::setLocalDeviceInfo()` 并通过 `sendCmdPayload(command=0x0058, payload=<bytes>, len=<bytes>)` 发送；但本阶段只恢复 payload，不发送真机帧。

| 上层入口 | Java payload builder | JSON 字段与顺序 | 已实现离线行为 |
| --- | --- | --- | --- |
| `setLocalDeviceInfoSourceName(mac, 1)` | `DeviceManager.sourceNameToJson(getLocalPhoneName(), context, 1)` | `sourceName`、`mSourceBtMac`、`canAlonePlayCtrl`、`canHeadsetCtrl` | `MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(...)` |
| `setLocalDeviceInfo(mac)` | `DeviceManager.setLocalDeviceInfo2(getLocalDeviceModel(), Build.VERSION.INCREMENTAL, appVersion)` | `model`、`romVersion`、`appVersion` | `MiPlayLocalDeviceInfoPayloadCodec.EncodeLocalDeviceInfo(...)` |

细节约束：`sourceNameToJson()` 在 `sourceName` 为空时返回 `null`；`mSourceBtMac` 在蓝牙 MAC 为空时写空字符串，否则先去掉冒号，再调用 `MD5Utils.md5EncryptTo32()`，其输出为大写 32 位 MD5；`canAlonePlayCtrl` 由 `DeviceManager.getCanAlonePlayCtrl()` 提供，默认字段值是字符串 `"0"`；`canHeadsetCtrl` 固定写字符串 `"1"`；`setLocalDeviceInfo2()` 的 `appVersion` 是 JSON number，不是字符串。`MiPlayLocalDeviceInfoPayloadCodec` 还用 relaxed UTF-8 JSON writer 保留中文 sourceName，而不是把它转义成 `\uXXXX`。

继续复核 Java 回调层后，`CmdSessionControl` 还暴露了 `CMD_SESSION_INFO_GET_DEVICEINFO_ACK = 210015`、`CMD_SESSION_INFO_SET_DEVICEINFO_ACK = 210028`、`CMD_SESSION_INFO_NOTIFY_DEVICEINFO = 211007`，以及 `onCmdSessionDeviceInfoAck(byte[])` / `onCmdSessionDeviceInfoExNotify(byte[])` / `onCmdSessionDeviceInfoNotify(byte[])` 三个 byte-array 回调。`setLocalDeviceInfo(byte[])` 本身只检查 `sessionType == 2` 后进入 native；Java 对 `SET_DEVICEINFO_ACK` 没有进一步业务处理。因此，后续即便测试 `0x0058`，也不应把“连接保持”或“静默 ack”误判为完整 device-info 成功；更可靠的观察目标仍是 `0x001e` 触发的 device-info bytes，且 `MiplaySessionCallbackManage.cmdSessionDevicesInfo(...)` 要求回包长度至少 40 bytes 才进入 `analysisDeviceInfo(...)`。

离线单测现在固定五类 payload/封装：中文 sourceName + 大写 MD5、缺失蓝牙 MAC 时的空 hash、`model/romVersion/appVersion` 三字段、非空 `0x0058 setLocalDeviceInfo` JSON payload 在 `0x001e` 之后继续沿同一 SafetyData CBC state 加密封装，以及 `0x001e -> 0x0058 -> 0x0058` 三帧连续 post-auth 序列；当前 78/78 通过。仍未向 S12 发送 `0x0058`。

#### 2026-07-19 post-auth 本地设备信息三帧 probe 实现（未实机发送）

基于 `MiPlayAudioService.cmdSessionSuccess(...)` 的 APK 顺序，`DLNACast.Probe` 新增显式门控参数 `--miplay-native-safety-mutual-auth-local-device-info-probe=<IPv4>`。它复用已验证的完整 SafetyAuth 互验路径，且只有在本端 `0x1402` 被设备 `0x1403` 验证、本端也验证设备 `0x1402 -> 0x1403` 后，才按同一 SafetyData sender CBC state 连续发送三帧：

| 顺序 | command | seq | plaintext |
| --- | --- | --- | --- |
| 1 | `0x001e getDeviceInfo` | `0x0004` | 空 payload |
| 2 | `0x0058 setLocalDeviceInfo` | `0x0005` | `sourceName`、`mSourceBtMac`、`canAlonePlayCtrl`、`canHeadsetCtrl` JSON |
| 3 | `0x0058 setLocalDeviceInfo` | `0x0006` | `model`、`romVersion`、`appVersion` JSON |

默认 Windows 等价取值仅用于 probe：`sourceName="DLNACast Windows"`、蓝牙 MAC 为空 hash、`canAlonePlayCtrl="0"`、`model="Windows"`、`romVersion=Environment.OSVersion.VersionString`、`appVersion=1`；可用 `--miplay-local-source-name=`、`--miplay-local-bluetooth-mac=`、`--miplay-local-can-alone-play-ctrl=`、`--miplay-local-model=`、`--miplay-local-rom-version=`、`--miplay-local-app-version=` 覆盖。该 probe 明确不发送 heartbeat、媒体、RTSP、音频、播放、openDevice 或其他业务控制；发送三帧后只读观察 5 秒，并只尝试解密响应。是否会返回长度至少 40 bytes 的 device-info bytes、静默 ack、或直接关闭连接，仍需单台 S12 实机验证。

### OPack 内层封装

现代通路不是把 JSON 直接放进旧式帧。`sendCmdData2`（`0x17b998`）先构造 OPack 内层；`OPackBuf::packString` 已确认只复制原始字符串字节，**不**额外写入长度。因此首字节是标签文本自身的单字节长度：

```text
tagLength (u8) | tag UTF-8 bytes | valueType (u8) | payloadLength (u32 big-endian) | payload
```

本次安全通路的标签固定为三个字节，因此 `cmd` 的开头是 `03 63 6d 64 1e`，`ack` 的开头是 `03 61 63 6b 1e`；`1e` 即值类型 30，之后为四字节大端 JSON 长度。再将这个内层放入 `$` 控制帧。`ParseDataMsg` 还支持其他值类型的长度形式，但安全命令已确认只使用类型 30。

项目中的 `MiPlaySafetyEnvelopeCodec` 和 `MiPlaySafetyCommandCodec` 已按此布局实现 `0x1400`–`0x1403` 的纯离线编码/解码，并强制 `0x1401`、`0x1403` 使用 `ack`、其余命令使用 `cmd`。它们不创建 TCP 连接。

### SafetyInfo 的字段、选择和验证

`sendSafetyInfo` 以 `0x1400`、值类型 30 发送 JSON，五个值均由无符号整数转成**十进制字符串**，字段顺序如下：

```json
{
  "authKeyTypes": "<supported type set>",
  "authAlgorithmTypes": "<supported type set>",
  "integrityTypes": "<supported type set>",
  "aesKeyTypes": "<supported type set>",
  "aesIvTypes": "<supported type set>"
}
```

`CmdSource` 构造函数的原生默认集合已直接从字段初始化恢复：`authKeyTypes=1`、`authAlgorithmTypes=7`、`integrityTypes=1`、`aesKeyTypes=1`、`aesIvTypes=3`。`sendSafetyInfo` 按上面顺序写入 JSON；实验代码以 `MiPlaySafetyInfoOffer.Native18_0_0_3` 表示这一特定样本的 offer，不把它泛化为其他固件的能力表。

对应的 `0x1401` 应答会先读取 `result`，再读取下列单数类型字段并以 `atoi` 转为整数。早期静态记录把成功条件写成字符串 `"1"`；两台 S12 的实测路径则在 `result="0"` 后继续发送加密 `0x1402`，因此实验代码对这个 observed S12 selection 接受 `"0"`，同时保留原始结果值供诊断：

```json
{
  "result": "0",
  "authKeyType": "<selected type>",
  "authAlgorithmType": "<selected type>",
  "integrityType": "<optional selected type>",
  "aesKeyType": "<optional selected type>",
  "aesIvType": "<optional selected type>"
}
```

样本的验证条件也已恢复：如果带有认证协商，`authKeyType` 与 `authAlgorithmType` 必须同时存在且非零；`integrityType` 可省略，但出现时必须非零；AES 类型可整体省略，若出现任一项则 `aesKeyType` 与 `aesIvType` 均必须非零。通过后，源端会：

1. 用 `authKeyType` 调用 `SafetyKeyDeal::genAuthKey`；
2. 保存 `authAlgorithmType`，作为后续 HMAC 算法号；
3. 用 `aesKeyType` / `aesIvType` 生成并保存 AES 材料；
4. 用 `integrityType`、AES 键与 IV 创建 `SafetyDataDeal`。

因此，这五个“类型”字段是会话状态机的输入而非描述性元数据；目前尚不知道各位的能力位语义，也没有非 S12 固件的接受样本。诊断解析会保留原始 `result`，并仅将当前实测过的 S12 `result="0"` / `(1,4,1,1,2)` 选择用于只读 `0x1402` 解密探针，不把它泛化为已完成认证或可播放会话。

### Lyra 受信任会话材料（静态确认与实测边界）

`MiPlayAudioService` 在设备既有 `lyraDeviceId` 又具备 Lyra 客户端时，才调用 Java 原生入口 `connectCmdSession2`，并将 `ProtocolSession` 的 JSON 作为最后一个参数传入。离线/缓存分支同样只会从已存在的 Lyra 记录取得该 JSON；它不会从普通 TCP 8899 或 mDNS TXT 记录生成密钥。Java 侧 JSON 的四个字符串字段为：

```json
{"wlan0ip":"<sender-ip>","authKey":"<secret>","streamKey":"<secret>","streamIV":"<secret>"}
```

原生 `CmdSource::setLyraInfo` 对四项均要求存在且为 JSON 字符串；随后把 `authKey`、`streamKey`、`streamIV` 保存到 `CmdSource` 的 `+0x360`、`+0x378`、`+0x390`。在实际 `onSessionConnect` 后，它们被复制到 `SafetyKeyDeal` 的 `+0x58`、`+0x70`、`+0x88`。此外该函数会把 `authKeyTypes` 的支持掩码置入位 `2`，并将 `aesKeyTypes` 和 `aesIvTypes` 均置入位 `4`；故从本样本默认值出发，Lyra 入口发出的能力集合会变为 `authKeyTypes=3`、`authAlgorithmTypes=7`、`integrityTypes=1`、`aesKeyTypes=5`、`aesIvTypes=7`。

这解释了 type 4 材料的来源：`genAuthKey(2)` 与 `genAesKey(..., 4)` 读取 `SafetyKeyDeal +0x58`（受信任的 `authKey`），`genAesIv(..., 4)` 读取 `+0x88`（受信任的 `streamIV`）；`streamKey` 位于 `+0x70`，属于后续流媒体材料，而不能凭空代入安全认证。项目中的 `MiPlayLyraSecretKeyCodec` 只提供这个 JSON 的离线、无日志编解码；它不发现、生成、传输或伪造任何密钥。

2026-07-19 在 `192.168.10.9` 上只读查询的结果为：两台 S12 均在 `_mi-connect._udp.local` 中显示 `lyra=False`，而 `_lyra-mdns._udp.local` 返回 0 台设备。因此 Lyra secret JSON 是该原生样本的受信任分支证据，但**不是**当前两台 S12 的可用补包方案；它的缺失不足以单独解释 `0x1401 result="0"`。当前更直接缺失的是原生 `CmdSource` 所获得的真实 `SessionInfo` 及其完整连接状态机。

### 非 Lyra TCP SessionInfo：端点采集与顺序（静态确认）

`TCPSession::initTCPSessionInfo`（`0x1a5320`）先检查 `SO_ERROR`，随后在已连接的 IPv4 socket 上分别调用 `getsockname` 和 `getpeername`。它将结果写入内嵌 `SessionInfo`：

| `SessionInfo` 偏移 | 来源 | 内容 |
| --- | --- | --- |
| `+0x00` | `getsockname` | 本机 IPv4 的点分十进制字符串 |
| `+0x18` | `getsockname` | 本机 TCP 端口（网络序转主机序） |
| `+0x20` | `getpeername` | 对端 IPv4 的点分十进制字符串 |
| `+0x38` | `getpeername` | 对端 TCP 端口（网络序转主机序） |

`CmdSource::onSessionConnect` 随后按 `SessionInfo +0x20`、`+0x38`、`+0x00`、`+0x18` 的顺序构造 `SafetyKeyDeal`。因此非 Lyra type‑1 认证键的精确输入顺序为：

```text
peerIPv4 + decimal(peerPort) + localIPv4 + decimal(localPort)
```

项目的 `MiPlayTcpSessionInfo` 已将此约束封装为离线模型，仅接受非零 IPv4 端点，并调用已有的 type‑1 数字替换/MD5 逻辑。它不创建 socket、不会将导出的键写入日志，也不会作为 `0x1400` 的一部分发送。即使拥有正确的 type‑1 键，它也只用于只读解密或后续最小认证回复构造；它本身不代表 MiPlay 控制通道已建立。

### 原生版本首帧 `0x0036`（静态确认与真机否定性验证）

在 `onSessionConnect` 完成端点型 `SafetyKeyDeal` 后，原生 `CmdSource` 会先递增本端序号并发送命令 `0x0036`、seq `0x0001`。静态代码的负载长度是 12，不是字符串长度 11：它发送 ASCII `3.1.6030516` **连同结尾 NUL**。因此同一原生最小序列中的随后 `0x1400` 使用 seq `0x0002`。

2026-07-19 对 `192.168.10.4` 的受限真机探针已按这个精确顺序发送 `0x0036(1)` → 已验证的 `0x0029` → `0x1400(2)`。设备回 `0x0037`、seq `0x0001`，负载为 ASCII `2.1.5091615\0`，证明版本帧已被解析并按序号确认。其后的 `0x1401` 仍为：

```json
{"aesIvType":"2","aesKeyType":"1","authAlgorithmType":"4","authKeyType":"1","integrityType":"1","result":"0"}
```

随后设备发送原始 `0x1402`（seq `0x0000`）并关闭连接；本次精确版本负载的原始 `0x1402` 为：

```text
000701E00200ECAE89F6CB0DD35E2CB4FD408221777435A6E936DFDC3852CD3AA9757CFBE03675611671BF743FA3D6E0D9DBB0091E0A740C140D84A436B97DE4AA88A3252D54B6F1CF
```

此前一次不含结尾 NUL 的 `0x0036` 也收到 `0x0037`，但静态长度证据要求采用上面的 NUL 版本；两次均得到相同的 `0x1401 result="0"` 并继续进入 `0x1402`。故现在可以排除“未发送版本首帧”“版本序号错误”和“漏发 NUL 终止符”作为先前无法推进 SafetyAuth 的原因。探针在这些记录中未回复 `0x1402`、未发送 `0x1403`、未尝试媒体或播放命令。

项目中的 `MiPlayNativeVersionCodec` 现以 `$` 帧编码该 NUL 结尾的 `0x0036`，并只接受可打印 ASCII（末尾可有一个 NUL）的 `0x0037` 应答；它是离线编解码器，不负责连接或发包。

### SafetyKeyDeal：认证键的已知派生部分

`SafetyKeyDeal::genAuthKey(unsigned int)`（`0x269564`）在类型 1 时的逻辑可由反汇编复原为：

```text
source = peerIPv4 + decimal(peerPort) + localIPv4 + decimal(localPort)
for every ASCII digit d in source:
    replace d with ('a' + (d - '0'))   // 0→a，…，9→j
authKey = MD5(source)
```

构造函数 `SafetyKeyDeal(string, ushort, string, ushort, string)` 将这四项保存到对象偏移 `+0x00`、`+0x18`、`+0x20`、`+0x38`。`CmdSource::onSessionConnect`（`0x1828d0`）以 `SessionInfo +0x20`、`+0x38`、`+0x00`、`+0x18`、空字符串为参数创建它，正好对应上节已经确认的对端后本机顺序。

`genAesKey` 与 `genAesIv` 对类型 1、2 的选择规则完全相同：类型 1 取 `authKey.substr(0, floor(length / 2))`，类型 2 取 `authKey.substr(floor(length / 2), floor(length / 2))`；因此奇数字符串的最后一个字符不会进入任一半段。类型 4 分别读取对象中预置的 `+0x58`（受信任 Lyra `authKey`）或 `+0x88`（受信任 Lyra `streamIV`）字段。实验代码的 `SelectDerivedAesMaterial` 故意只支持已确认的类型 1/2，遇到类型 4 会拒绝，而不会伪造材料。

`dealSafetyInfoAck` 在处理 `0x1401` 后调用这些函数并保存结果，说明会话键来自安全信息阶段，而非随意生成即可互通。

### SafetyDataDeal：已静态确认的加密侧边界

`SafetyDataDeal(bool, uint, string, string)`（`0x269d34`）保存加密开关、完整性类型、键与 IV，并以键和 IV 的**前 16 字节**建立两份 AES-CBC 状态；不足 16 字节时，内部缓冲区按零补齐。`encryptData`（`0x26a084`）在启用加密时使用零填充，填充长度为 `16 - (inputLength % 16)`，所以即使原文已是 16 的倍数也会附加一个完整的 16 字节填充块。

已确认的输出标记包括：版本字节为 `1`，标志字节包含加密位 `0x80`、填充长度字段位 `0x40`，以及有完整性时的 `0x20`；加密模式会写入填充长度。完整性类型 1 调用 `av_crc_miplay`，初始种子为 `-1`，并写出大端 CRC。

### SafetyDataDeal V1 头：静态恢复与 S12 原始帧匹配

`decryptData`（`0x26a3bc` 起）现已恢复出 V1 容器的可验证头顺序：先读取大端 `uint16(data[0..1]) + 2` 作为完整头长度，再读取 `data[2]` 的版本和 `data[3]` 的标志。对版本 `1`，游标从偏移 4 开始：

1. 若 `flags & 0x40`，偏移 4 为单字节填充长度；
2. 若 `flags & 0x20`，紧随其后的是 4 字节大端完整性值；
3. 负载从声明的完整头长度开始。完整性类型 1 以种子 `-1` 对这段**尚未解密的负载**调用 `av_crc_miplay`；
4. 仅当 `flags & 0x80` 时，原生代码才对该负载 AES-CBC 解密，并在解密后去除前述填充长度。

因此，精确版本探针中 S12 返回的开头 `00 07 01 E0 02 00 EC AE 89` 可以确定地解释为：完整头长 `0x0007 + 2 = 9`、V1、三个标志均置位、填充长度 `2`、大端完整性值 `0x00ECAE89`，密文起点为偏移 9。这是静态恢复与真机原始字节的直接匹配，不是对 `0x0007` 的猜测。

项目中的 `MiPlaySafetyDataHeaderCodec.TryDecodeVersion1` 只解析这套已确认的结构并保留元数据；它不实现 `av_crc_miplay`、不导出或推导会话材料、不解密负载，也不连接或发送网络数据。对应单元测试使用上面的 S12 原始 `0x1402` 字节，确认头长、三个标志、填充值、完整性值与负载偏移；当前测试总数为 71。

### SafetyAuth 应答与 HMAC 文本

在 `0x1402` 的处理路径中，源端对对端的 `authMsg` 计算 HMAC，并以 `0x1403`（OPack `"ack"`）返回：

```json
{"result":"1","authMsgAck":"<hmac>"}
```

`dealSafetyAuthAck` 则要求 `result == "1"`、`authMsgAck` 非空，并校验：

```text
authMsgAck == HMAC(selected algorithm, local authMsg, mAuthKey)
```

原生 `OAuth::hmac` 的参数顺序与标准 HMAC 一致：消息为 `authMsg`，键为 `mAuthKey`；超长键先散列。MD5、SHA-1 与 SHA-256 的 `getHash()` 均使用字符表 `0123456789abcdef` 输出小写十六进制文本（分别为 32、40、64 个字符）。这解决了离线实现中容易混淆的“原始字节、Base64 还是十六进制”的表示问题，但不授予 Windows 端任何信任身份。

## 媒体回连与封装（静态确认）

打开设备后，音箱会反向连接发送方的动态媒体端口。样本默认走 RTSP，另有可选的纯 RTP 路径。RTSP `SETUP` 的传输映射为：

- `RTP/AVP/UDP;unicast` 和旧式 `RTP/AVP;unicast`：模式 2、UDP；旧格式不带端口时默认 19000；
- `RTP/AVP/TCP`：模式 4、TCP datagram/interleaved；
- `RTP/AVP/MPT;unicast`：模式 5，创建 KCP 会话。

MPT/KCP 参数为 conv `0x1234`、MTU 1400、发送/接收窗口 256、10 ms 更新周期、fast resend 1、关闭拥塞窗口、最小 RTO 100 ms，`nodelay = 0`。

音频路径为 `AAC/ADTS → MPEG-TS → RTP → UDP/TCP/MPT(KCP)`：48 kHz、16-bit、双声道、256 kbps AAC-LC；每个访问单元有 7 字节 MPEG-2 ADTS 头；RTP 使用 MPEG-TS payload type 33。默认最大 RTP 包为 1472 字节，即 12 字节 RTP 头后最多 7 个 188 字节 TS 包。样本的主动播放缓冲为 5 GHz 下 0.8 秒、其他网络下 1.0 秒；这是该实现的策略，不能当作协议硬下限。

Lyra 会话使用的 `authKey`、`streamKey`、`streamIV` 均为每会话随机的 16 字节 ASCII 文本，并通过受信任控制通路同步。AES 在 MPEG-TS 之前对 AAC/ADTS 数据的完整 16 字节块执行 CBC；尾部不足 16 字节的数据不加密，访问单元携带加密前的 IV。缺少可信通道时，单独复刻 RTP/TS/KCP 不能完成可用投送。

## 工程实现与验证范围

当前实验代码已包含：

- `_mi-connect._udp.local` 发现及 `appsData` 多应用容器解析；
- 旧式 `$` 控制帧的编码/解码和长度校验；
- `OpenDevice` 载荷、会话随机材料和诊断辅助；
- 已确认旧式 `0x0028`/`0x0029` 挑战应答、`0x0036`/`0x0037` 版本帧、OPack 安全内层、`SafetyInfo` offer/ack JSON、`SafetyAuth` JSON/HMAC、Lyra 四字段 secret JSON、非 Lyra TCP `SessionInfo` 端点顺序，以及类型 1/2 `SafetyKeyDeal` 材料选择的纯离线实现；
- `DLNACast.Probe --miplay-safety-probe=<IPv4>`：显式真机实验入口，只允许一次经过命令号校验的 `0x0028`/`0x0029` 往返与有限观察；
- `DLNACast.Probe --miplay-safety-offer=<IPv4>`：在完成上述受限旧式挑战应答后，额外发送一次精确的原生默认 `0x1400` offer，并只记录后续帧；它绝不发送 `0x1403`、媒体或未知控制数据；
- `DLNACast.Probe --miplay-native-safety-probe=<IPv4>`：仅用于明确授权的测试设备；严格发送原生 `0x0036(1, 12-byte NUL-terminated version)`、已验证的 `0x0029` 和 `0x1400(2)`，然后只记录 `0x0037`/后续帧；它绝不发送 `0x1403`、媒体或未知控制数据；
- `DLNACast.Probe --miplay-native-safety-decrypt-probe=<IPv4>`：在同一受限发送边界内，仅额外尝试解密 `0x1402`，报告命中的端点方向/AES 候选；它仍绝不发送 `0x1403`、媒体或未知控制数据；
- `DLNACast.Probe --miplay-native-safety-auth-probe=<IPv4>`：在同一受限发送边界内，仅当唯一解出 `0x1402 cmd/authMsg` 后发送一次加密 `0x1403` HMAC acknowledgement，随后只观察；它绝不发送媒体、RTSP、播放或其他业务控制数据；
- `DLNACast.Probe --miplay-native-safety-mutual-auth-probe=<IPv4>`：在同一受限发送边界内，按 native `onRecvCmd` 静态顺序于 `0x1401` 后发送一次本端加密 `0x1402` challenge，只用 verified observed S12 candidate，随后最多回复一次设备 `0x1402` 的加密 `0x1403`，并只校验设备 `0x1403 authMsgAck`；它绝不发送媒体、RTSP、播放或其他业务控制数据；
- `DLNACast.Probe --miplay-native-safety-mutual-auth-observe-probe=<IPv4>`：发送范围与互验 probe 相同；只有在本端 `0x1402` 与设备 `0x1402` 均完成 `0x1403` HMAC 验证后，继续只读观察一个 5 秒窗口，不发送 post-auth heartbeat、getDeviceInfo、setLocalDeviceInfo、RTSP、音频、播放、openDevice 或其他控制帧；
- `DLNACast.Probe --miplay-native-safety-mutual-auth-heartbeat-probe=<IPv4>`：发送范围与互验 probe 相同；只有完整互验后，发送一次 SafetyData 加密空 payload 的 `0x001a` heartbeat（当前源端 seq `0x0004`），随后只读观察，不再发送第二次 heartbeat、heartbeat ack、getDeviceInfo、setLocalDeviceInfo、媒体、RTSP、音频、播放、openDevice 或其他控制帧；
- `DLNACast.Probe --miplay-native-safety-mutual-auth-device-info-probe=<IPv4>`：发送范围与互验 probe 相同；只有完整互验后，发送一次 SafetyData 加密空 payload 的 `0x001e getDeviceInfo`（当前源端 seq `0x0004`），随后只读观察并仅尝试解密响应，不发送 `0x0058 setLocalDeviceInfo`、heartbeat、媒体、RTSP、音频、播放、openDevice 或其他控制帧；
- `DLNACast.Probe --miplay-native-safety-mutual-auth-local-device-info-probe=<IPv4>`：发送范围与互验 probe 相同；只有完整互验后，按 APK 顺序发送一次 `0x001e getDeviceInfo`（seq `0x0004`）和两次 SafetyData 加密 JSON payload 的 `0x0058 setLocalDeviceInfo`（seq `0x0005`/`0x0006`），随后只读观察，不发送 heartbeat、媒体、RTSP、音频、播放、openDevice 或其他控制帧；
- 覆盖上述离线协议原语的 78 个单元测试（2026-07-19：78/78 通过）。

这些测试验证的是本地字节序、边界条件和解析行为，并不等同于音箱上的认证、播放或端到端延迟验证。

## 明确未解决的问题

1. `0x1401 result="0"` 在两台 S12 上会继续进入 `0x1402`，但 result 值本身的命名语义、各固件差异，以及其他可接受组合仍未知；
2. `authKeyTypes`、`authAlgorithmTypes`、`integrityTypes`、`aesKeyTypes`、`aesIvTypes` 的位/枚举语义，以及设备实际可接受的组合；
3. 完整 SafetyAuth 互验已在 `192.168.10.4` 上通过：本端 `0x1402` 得到设备 `0x1403 authMsgAck` HMAC 验证，本端也已回复设备 `0x1402`；只读观察显示源端认证后保持静默时设备会主动关闭 8899/TCP；
4. 该验证仍只覆盖认证层；单次 `0x001a` heartbeat 未得到 `0x001b` ack，单次 `0x001e getDeviceInfo` 未得到 device-info 响应，认证后的 `0x0058 setLocalDeviceInfo` payload 与三帧 probe 已离线编码锁定但尚未发送实测；keepalive/reaper 完整行为、状态查询/回连/媒体协商/播放控制、低延迟音频发送和非类型 1 完整性算法仍未实测；
5. 非 Xiaomi 系统如何建立 Continuity/Lyra 所需的受信任身份、设备确认和会话密钥同步；
6. 在不破坏现有播放会话的前提下，用单台测试音箱验证完整认证、回连、媒体协商与实际延迟。

## 后续工作准则

下一阶段可以在认证完成条件之上继续研究最小、可逆、无媒体的认证后状态边界，例如只观察 keepalive/reaper 相关帧或静态确认第一个 post-auth 控制入口；仍不得直接发送播放、RTSP、音频或 openDevice 等业务控制，直到对应帧结构和回滚边界被静态证据与单测闭环。

所有后续记录都应区分“实测”“静态确认”和“推断”，并在新证据推翻旧结论时保留本节的更正。
