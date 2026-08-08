# MiPlay Mi13P source field evidence for `0x0040 SetPlaySource`

This note is offline-only. It summarizes targeted DEX evidence from
`MiLinkOS3Cn/classes3.dex` after abandoning whole-APK JADX for this route.

Generated trace artifact:

- `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_ref_identity_trace.json`

No speaker, LAN, RTSP, media, playback, or audio operation was performed.

## `ref_channel`

`MiDevice` contains the field:

- `Lcom/xiaomi/miplay/mylibrary/MiDevice;->ref_channel:Ljava/lang/String;`

Accessors are present:

- `MiDevice.getRef_channel()` at code offset `0x274098`
- `MiDevice.setRef_channel(String)` at code offset `0x274de4`

`StatsUtils.setPlaySource(DeviceManager, Map)` reads `MiDevice.getRef_channel()`
and uses it as the first argument to `ontrackDataToJson(...)` before calling
`CmdSessionControl.setPlaySource(byte[])`.

`MiDevice.setRef_channel(String)` is referenced from
`MiplayMultiDisplayManage.getMultiPort(...)`; the normal `setPlaySource` path
reads the field and does not synthesize `ref_channel` locally.

`StatsUtils.getRefChannel(I)` returns the observed enum-like values:

| Code | Value |
|---:|---|
| 0 | `controlcenter` |
| 1 | `nearfield` |
| 2 | `xiaoai_phone` |
| 3 | `farfield` |
| 4 | `lockscreen` |
| 5 | `notification` |
| 6 | `playpage` |
| 7 | `world` |
| 8 | `relay_card` |
| 9 | `nfc` |

Unknown/default falls through to `controlcenter` in the recovered method shape.

## `ref_content`

`StatsUtils.setRefContent(String packageName)` maps selected media packages to
`ref_content` values:

| Package | `ref_content` |
|---|---|
| `com.miui.player` | `music_miui` |
| `com.netease.cloudmusic` | `music_wangyiyun` |
| `com.tencent.qqmusic` | `music_qq` |
| `com.kugou.android` | `music_kugou` |
| `cn.kuwo.player` | `music_kuwo` |
| `com.ximalaya.ting.android` | `fm_himalaya` |
| `fm.qingting.qtradio` | `fm_qingting` |
| `com.yibasan.lizhifm` | `fm_lizhi` |
| `com.luojilab.player` | `fm_dedao` |

`MiPlayAudioService.onTopActiveSessionChange(ActiveSessionRecord)` calls:

1. `ActiveSessionRecord.getPackageName()`;
2. `StatsUtils.setRefContent(packageName)`;
3. `StatsUtils.setPlaySource(DeviceManager, cmdSessionControlMap)`;
4. `StatsUtils.setRecordPackageName(packageName)`.

This makes active-session package changes an official source for updated
`ref_content` and a trigger for sending `0x0040 SetPlaySource`.

## `ref_function`

`StatsUtils.activeMiplayStats(...)` and `StatsUtils.connectStats(...)` reference
`ref_function` and the observed values:

- `single_room`
- `multi_room`
- `stereo`

The exact branch conditions are still better treated as static-analysis targets;
for payload construction, these are the recovered official value set.

## Legacy command session versus Lyra/Continuity identity

`MiPlayAudioService.startCommandChannel(MiDevice, int)` creates the legacy
command session path:

- constructs `CmdSessionControl(MiDevice)`;
- installs `MiplaySessionCallbackProxy`;
- calls `CmdSessionControl.connectCmdSession(...)` overloads;
- stores the session in `cmdSessionControlMap`.

`MiPlayAudioService.cmdSessionSuccess(MiDevice, CmdSessionControl)` calls
`CmdSessionControl.getDeviceInfo()` after command-session success.

### Optional `SecretKeyCommand`

`startCommandChannel(...)` may pass an optional `secretKeyCommand` string into
`CmdSessionControl.connectCmdSession(...)`.

Targeted DEX tracing localizes this to `ProtocolSession` / `SecretKeyCommand`:

- `ProtocolSession.parseSecretKeyCommand(String)` reads JSON keys:
  `wlan0ip`, `authKey`, `streamKey`, `streamIV`;
- `ProtocolSession.toJson(SecretKeyCommand)` writes the same JSON keys;
- `ProtocolSession.generatorMirrorKey(Context)` derives those fields from
  local Wi-Fi IP and generated mirror keys.

This is useful for separating contexts: the optional string is Lyra/mirror key
material, not the missing AppInfo/ServiceName/signature/package identity bridge
for legacy 8899 `SetPlaySource`.

Native confirmation from `MiLinkOS3Cn/lib/arm64-v8a/libmirror-jni.so` strengthens
that boundary:

- `CmdControl::setPlaySource` (`0x18b698-0x18b6b4`) and
  `CmdSource::setPlaySource` (`0x18b724-0x18b740`) load command `0x40`, increment
  the source sequence at `CmdSource + 0x2c0`, and call
  `sendCmdPayload(cmd=0x40, seq, payload, len)`;
- `connectCmdSession2` (`0x17f410-0x17f5b8`) converts the optional Java
  `secretKeyCommand` string and calls the vtable slot matching `setLyraInfo`
  before `connectCmdSession(addr, port, sessionType)`;
- `CmdSource::setLyraInfo` (`0x18da68`) parses JSON keys `wlan0ip`, `authKey`,
  `streamKey`, `streamIV`; stores `authKey` / `streamKey` / `streamIV` into
  CmdSource string fields at `+0x360` / `+0x378` / `+0x390`; and sets Lyra flags;
- that native path contains no `AppInfo`, `ServiceName`, package signature,
  package identity, or `ref_*` bridge evidence.

So the currently recovered native bridge explains only optional Lyra key
material entering the legacy command session. It does not explain Android source
identity entering legacy 8899.

In the current targeted xref set, `startCommandChannel` does not reference
`ServiceName`, `AppInfo`, package signature, or Continuity channel APIs.
Those appear in a separate Lyra/Continuity path:

- `CommonUtil.getServiceName()` builds a `ServiceName`;
- `LyraChannelManager` stores that `ServiceName`;
- `ContinuityChannelManager.createChannel/registerChannel` consume it.

This is important negative evidence: Java-level xrefs currently show the legacy
8899 `CmdSessionControl` and Lyra/Continuity `ServiceName` paths as separate.
The source-identity bridge is still missing.

## Current boundary

The official `0x0040` payload fields are now grounded enough for offline payload
examples. This does not authorize live sending.

Still forbidden without a separate exact plan and fresh authorization:

- live non-empty `0x0040`;
- `0x0058`;
- `Cmd_Open/openDevice 0x0000`;
- `Cmd_AddMirror 0x002e`;
- RTSP;
- media, playback, or audio frames.

## Test-backed representation

- `src/DLNACast.Core/MiPlay/MiPlayPhoneFirmwareSourceFieldEvidence.cs`
- `tests/DLNACast.Tests/MiPlayPhoneFirmwareSourceFieldEvidenceTests.cs`