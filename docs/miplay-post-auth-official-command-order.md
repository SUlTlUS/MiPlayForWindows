# MiPlay post-auth official command order evidence

This note records a narrow offline conclusion from the already extracted Mi13P OS3 phone-firmware artifacts. It does not authorize any S12/LAN probe by itself.

## Evidence scope

- Phone firmware: `D:/17系稳定版Pro_260602_Mi13P_OS3.0.313_92ed`
- Structured artifacts:
  - `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_dex_cmdsession_xrefs.json`
  - `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_all_dex_ref_identity_trace.json`
  - `artifacts/phone_firmware/mi13p_os3_0_313/mirroros3_command_name_map.json`

## Command-session wrappers

`CmdSessionControl` in `MiLinkOS3Cn/classes3.dex` exposes Java wrappers for the native command session:

- `connectCmdSession(String,String,String,int,int)` at DEX code offset `0x294780`
- `createCmdSession(String,int,int)` at DEX code offset `0x294900`
- `getDeviceInfo()` at DEX code offset `0x295014`
- `openDevice(String,int)` at DEX code offset `0x295460`
- `setPlaySource(byte[])` at DEX code offset `0x295c84`

The traced wrapper methods share `cmdHandler`, `mLockControl`, and `sessionType` field access before calling their long-handle native forms. This means a raw TCP frame is not equivalent to an official Java command unless the native `cmdHandler/sessionType` context has been reproduced or proven irrelevant.

Native string clusters in the same phone-firmware scope include `CmdSource`, `createCmdSession addr`, `port:%d`, `send] cmd`, `getCmdData`, `sendPayload`, and `Cmd_SafetyAuth_Ack`. This localizes a legacy command-session sender in the phone build without proving every handler branch.

## Recovered order boundary

The current structured DEX xrefs separate three paths:

1. `MiPlayAudioService.startCommandChannel(...)` calls `CmdSessionControl.connectCmdSession(...)`.
2. `MiPlayAudioService.cmdSessionSuccess(MiDevice, CmdSessionControl)` calls `CmdSessionControl.getDeviceInfo()`. TV and refresh paths also call `getDeviceInfo()`.
3. `StatsUtils.setPlaySource(DeviceManager, Map)` builds the source JSON and calls `CmdSessionControl.setPlaySource(byte[])`; its traced callers include `MiPlayAudioService.onTopActiveSessionChange(...)` and `MiplayMultiDisplayManage.onPlay(...)`.

So the official source-side shape is not “mutual SafetyAuth then immediately send `0x0040`”. A better current model is:

`startCommandChannel -> connectCmdSession -> cmdSessionSuccess -> getDeviceInfo`, then later active-session / playback state may trigger `StatsUtils.setPlaySource -> setPlaySource`.

## Command ID alignment

`mirroros3_command_name_map.json` aligns the command names used in the phone sender stack:

- `0x001e` = `GetDeviceInfo`
- `0x001f` = `GetDeviceInfo_Ack`
- `0x0034` = `GetMirrorMode`
- `0x0035` = `GetMirrorMode_Ack`
- `0x0040` = `SetPlaySource`
- `0x0041` = `SetPlaySource_Ack`
- `0x002e` = `AddMirror`
- `0x002f` = `AddMirror_Ack`
- `0x0000` = `Open`

This matches the receiver-side LX06 1.88.51 `mpas` command names, while still keeping 1.88.51 static evidence separate from current LX06 1.94.13 runtime claims.

## Root tcpdump update: observed official runtime order

The later rooted phone capture
`artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap`
captured an existing authenticated phone-to-S12 `8899` session. It does not
contain TCP bootstrap frames, and it does not provide SafetyData plaintext, but
it does provide the real outer wire order used by the official phone sender:

`0x0058(seq 0x013a) -> 0x001e(seq 0x013b) -> 0x0059(seq 0x013a) -> 0x0058(seq 0x013c/0x013d) -> 0x001f(seq 0x013b) -> 0x0034/0x0035 GetMirrorMode/GetMirrorMode_Ack(seq 0x013e) -> more 0x0058/0x0059 -> heartbeat -> 0x0040(seq 0x0144) -> heartbeat`

This is a mid-session command window, not a fresh post-auth bootstrap order. The
absence of TCP/SafetyAuth frames and the starting sequence `0x013a` mean the
capture cannot identify which command immediately follows `DealSafetyDone`.

Observed payload sizes are also meaningful:

- first `0x0058`: 105-byte SafetyData payload;
- follow-up `0x0058`: 41-byte SafetyData payloads;
- `0x001e`: 25-byte SafetyData payload;
- `0x001f`: 425-byte SafetyData payload;
- `0x0034`/`0x0035` GetMirrorMode pair: 25-byte SafetyData payloads;
- `0x0040`: 105-byte SafetyData payload.

No `0x0041` was captured after the official `0x0040`, but the socket did not
close and `0x001a/0x001b` heartbeat pairs continued through sequence `0x014f`.
So the previous "wait only for immediate 0x0041" assumption is too narrow for
official sender behavior.

## Bounded S12 validation update

The explicitly authorized bounded run is recorded in
`docs/miplay-official-post-auth-sequence-live-validation.md`.

It completed mutual SafetyAuth, then sent only the first planned post-auth
`0x0058` frame:

```json
{"sourceName":"DLNACast Windows","mSourceBtMac":""}
```

The S12 closed immediately after that frame with socket error `10053`. No
`0x001e`, `0x0034`, `0x0040`, Open, AddMirror, RTSP, media, playback, audio,
retry, or fallback was sent. Therefore this live result does not reject the
later recovered official order. It rejects only the current default Windows
source identity / empty `mSourceBtMac` first-frame substitute.

Follow-up offline work reconstructs the exact official first 0x0058 JSON: the
pcap-hidden first block is `{"sourceName":"X`, and the known suffix completes
`{"sourceName":"Xiaomi 13 Pro","mSourceBtMac":"<32-char uppercase MD5>"}`.
The plaintext is 80 bytes, and SafetyData v1 padding predicts the captured
105-byte container. The official-sequence Probe plan now prepares that recovered
identity by default, but it is still a candidate requiring fresh bounded
authorization, not proof of acceptance.

Recovered-identity live validation update: with fresh authorization, the Probe
sent that recovered 80-byte / 105-byte first 0x0058 frame to `192.168.10.4`.
The receiver still closed without `0x0059`, and the Probe sent no `0x001e`,
`0x0034`, `0x0040`, Open, AddMirror, RTSP, media, playback, audio, retry, or
fallback. Therefore the observed official order remains valid as passive
mid-session phone evidence, but it is no longer treated as a sendable
fresh-session order. The missing proof is the phone's first command and exact CBC
continuation immediately after a newly completed `DealSafetyDone`.

## Root tcpdump plaintext recovery

The 12:24 rooted pcap begins mid-session, but CBC chaining still permits
offline plaintext recovery after the first captured frame in each direction: the
previous same-direction frame's final ciphertext block becomes the next frame's
first-block IV.

For the `192.168.10.20:43720 -> 192.168.10.7:8899` flow, peer-first type-1
authKey derivation gives `a565e5251cce7d9995e34b18bb656c33`, and AES key type 1
is the first half `a565e5251cce7d99`. With that key, the captured official
runtime plaintext is:

- `0x001e GetDeviceInfo`: empty payload;
- repeated `0x0058 SetLocalDeviceInfo`: `{"canAlonePlayCtrl":"1"}` and
  `{"alonePlayCapacity":"1"}` after the initial source-name frame;
- `0x001f GetDeviceInfo_Ack`: parsed OPack-like device-info map;
- `0x0034 GetMirrorMode`: empty payload;
- `0x0035 GetMirrorMode_Ack`: `00 00 00 00 02`;
- `0x0040 SetPlaySource`:
  `{"ref_channel":"controlcenter","ref_function":"single_room","ref_content":"music_qq"}`.

This is the first byte-level evidence that the earlier `playpage`/empty
SetPlaySource JSON was not the official runtime payload for the captured path.
It also proves that `0x0040` is not the first meaningful post-auth command: the
phone has already supplied local source context, received device info, and
queried mirror mode before sending it.

Current `com.milink.service:audio` native evidence identifies the previously
unknown `0x0034/0x0035` pair. In pulled
`lib/arm64-v8a/libaudiomirror-jni.so` SHA-256
`DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF`,
`CmdSource::getMirrorMode` at `0x177648` sends command `0x0034` with null
payload. `CmdSource::onRecvCmd` uses a low-command jump table at `0x10e67a`;
the `0x0035` entry targets `0x180e08`, which parses a value-type-0 big-endian
uint32 mirror-mode ACK. String evidence in the same library names the pair
`GetMirrorMode` / `GetMirrorMode_Ack`.

The same current native library also localizes the post-auth SafetyData
insertion point:

- `CmdSource::dealSafetyInfoAck` at `0x17c5f0` derives key material and creates
  `SafetyDataDeal(true, integrityType, aesKey, aesIv)`, stored at
  `CmdSource+0x3c0`;
- `CmdSource::sendCmdPayload` at `0x17b858` invokes this wrapper before building
  the outer `$ cmd seq len` frame;
- `SafetyDataDeal` initializes separate AES-CBC contexts for encrypt and
  decrypt, so SafetyAuth and post-auth SafetyData must remain distinct state
  machines.
- `SafetyDataDeal::encryptData` calls `av_crc_miplay(init=-1)` for
  integrityType=1 (`0x26a220..0x26a23c`) and writes the resulting integrity value
  high-to-low (`0x26a270..0x26a298`). `decryptData` reverses the little-endian
  loaded field (`0x26a468..0x26a474`) before comparing at `0x26a548`. The project
  now treats SafetyData v1 integrity as a native big-endian value; the existing
  local CRC-32/MPEG-2 accumulator is byte-reversed before it is compared with or
  written to the header.

A follow-up heartbeat-only mapping pcap
`artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-map-20260726-132653.pcap`
aligns the `.10.7` socket sequence with logcat command session
`DID8899:CMD_1bc2`, owned by `com.milink.service:audio` (UID `10168`, PID
`975`). This keeps the `.10.7` command sequence separate from the second
simultaneous `DID8899:CMD_2599` chain.

## Current implication

The latest bounded LX06 1.94.13 run accepted mutual SafetyAuth, then closed after exactly one native-no-reset official JSON `0x0040` without returning `0x0041`.

With the phone-firmware order and root tcpdump evidence above, that negative
result is no longer just a cipher/IV clue. It strongly suggests the Probe skipped
the official ready/context stage: the real phone sends `0x0058`, then
`0x001e -> 0x001f`, then `0x0034 -> 0x0035` GetMirrorMode, and only later sends
`0x0040`.

The next useful work is therefore offline and read-only in design:

- run the prepared official-sequence validation only after fresh user
  authorization; the Core plan and Probe runner now prepare
  `0x0058 sourceName/mSourceBtMac -> 0x001e -> 0x0058 canAlonePlayCtrl -> 0x0058 alonePlayCapacity -> 0x0034 -> 0x0040`
  and requires same-sequence `0x001f` plus `0x0035` before `0x0040`;
- determine from native/Java state whether `0x001f` and `0x0035` update
  listener/session/device/mirror-mode context before `0x0040`;
- keep generated `0x0040`, `0x0058`, `Cmd_Open`, `Cmd_AddMirror`, RTSP, media, playback, and audio forbidden until the official post-auth SafetyData state can be reproduced byte-for-byte or a new live plan is explicitly authorized.

## Route-specific correction from the fresh legacy sender

The 2026-08-07 `com.milink.service 12.4.8.13` capture adds a second official
source path. Against the distinct non-Lyra receiver, the phone never offered
SafetyInfo and sent clear `0x001e`, clear 31-byte
`0x0058 {"sourceName":"MI PAD 4\\/Plus"}`, and clear heartbeats after the
receiver's only outbound frame, legacy `0x0028`.

Consequently, the modern sequence above is valid for the captured OS3/S12
SafetyData session, but it is not universal across sender versions and receiver
capabilities. A receiver implementation must select the branch from observed
wire behavior rather than assuming `DealSafetyDone` always precedes the first
device-info command. This correction is evidence-only: all possible receiver
replies on the fresh legacy-clear branch remain `SafeForNetworkUse=false`.
