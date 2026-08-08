# MiPlay Mi13P phone firmware source-side evidence

This note records offline-only evidence from the supplied Mi 13 Pro HyperOS
phone firmware:

- source directory: `D:\17系稳定版Pro_260602_Mi13P_OS3.0.313_92ed`
- decompressed workspace artifact: `artifacts/phone_firmware/mi13p_os3_0_313/super.img`
- logical partitions extracted for this pass: `product_a.img` and `system_ext_a.img`

No speaker, LAN, RTSP, media, playback, or audio operation was performed during
this pass. Whole-APK JADX decompilation is no longer used for this route because
it repeatedly hung for hours; the useful path is targeted EROFS extraction,
DEX/xref parsing, and native command-map analysis.

## Partition, EROFS extraction, and candidate files

A minimal read-only EROFS index localized the phone-side MiPlay candidates:

- `product_a:/priv-app/MirrorOS3/MirrorOS3.apk`
- `product_a:/priv-app/MirrorOS3/oat/arm64/MirrorOS3.vdex`
- `product_a:/priv-app/MirrorOS3/oat/arm64/MirrorOS3.odex`
- `product_a:/app/MiLinkOS3Cn/MiLinkOS3Cn.apk`
- `product_a:/app/MiLinkOS3Cn/oat/arm64/MiLinkOS3Cn.vdex`
- `product_a:/app/MiLinkOS3Cn/oat/arm64/MiLinkOS3Cn.odex`
- `system_ext_a:/app/MiuiAudioMonitor/lib/arm64/libCastSdk-jni.so`
  as a symlink target carrying `mediacastio` / `MiPlay_CastSDK` strings.

The selected `MirrorOS3` and `MiLinkOS3Cn` APK/VDEX/ODEX files were extracted
from EROFS layout-3/LZ4 compressed files with
`scripts/extract-erofs-files.py`. Both APKs passed `ZipFile.testzip()`.

Generated artifacts include:

- `artifacts/phone_firmware/mi13p_os3_0_313/erofs_extraction_summary.json`
- `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_extracted_string_hits.jsonl`
- `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_dex_cmdsession_xrefs.json`
- `artifacts/phone_firmware/mi13p_os3_0_313/mirroros3_command_name_map.json`

## Source-side legacy command stack

The phone firmware provides stronger source-side evidence than the previously
scanned Mi Connect Service APK native libraries.

Relevant product partition contexts include:

- `product_a+0x310ed0bf` and nearby offsets:
  `miplay_mylibrary`, `getDeviceInfo`, and
  `Java_com_xiaomi_miplay_mylibrary_mirror_CmdSessionControl_*`.
- `product_a+0x31123880` / `product_a+0x3174d31b`:
  `Cmd_SafetyAuth_Ack`.
- `product_a+0x31130659` / `product_a+0x3175b875`:
  `Cmd_Auth`.
- `product_a+0x310ee47d` / `product_a+0x31711df9`:
  `mirror::CmdControl`, `openDevice`, `CmdSource`,
  `getCmdNameFromCode`, `ParseDataMsg`, `AES_CBC_decrypt_buffer`,
  `SafetyKeyDeal::genAuthKey`, and `genAesIv`.
- `product_a+0x83f700c4`:
  `createCmdSession addr`, `port:%d`, `send openDevice %.*s`,
  `getVersion:%s`, `3.2.5121919`, `cmdType:%s`, `isAck`,
  `authUsedTime`, and `DealSafetyD`.

This proves that the phone system firmware contains a source-side MiPlay legacy
command-session implementation. It also connects the source stack to
`CmdSessionControl`, `CmdSource/CmdControl`, SafetyAuth handling, and an
`openDevice` sender path. This is still static evidence, not live authorization.

## Wire command IDs from phone firmware

`MirrorOS3/lib/arm64-v8a/libmirror-jni.so` provides a native command-name map.
The recovered values align with the LX06 receiver observations:

- `0x0000` = `Open`
- `0x0001` = `Open_Ack`
- `0x001e` = `GetDeviceInfo`
- `0x001f` = `GetDeviceInfo_Ack`
- `0x0028` = `Auth`
- `0x0029` = `Auth_Ack`
- `0x002e` = `AddMirror`
- `0x002f` = `AddMirror_Ack`
- `0x0040` = `SetPlaySource`
- `0x0041` = `SetPlaySource_Ack`
- `0x0058` = `SetDeviceInfo`
- `0x0059` = `SetDeviceInfo_Ack`
- `0x1400` = `SafetyInfo`
- `0x1401` = `SafetyInfo_Ack`
- `0x1402` = `SafetyAuth`
- `0x1403` = `SafetyAuth_Ack`

Media/open-mirror commands also exist in the map, but they remain outside the
current safe boundary.

## Official sender-side `0x0040 SetPlaySource` payload builder

The targeted DEX xref pass localizes the official builder in
`MiLinkOS3Cn/classes3.dex` without whole-APK JADX:

- `StatsUtils.setPlaySource(DeviceManager, Map)` calls
  `CmdSessionControl.setPlaySource(byte[])`.
- It iterates `DeviceManager.getMiDeviceList()`.
- For each `MiDevice`, it looks up `cmdSessionControlMap.get(miDevice.getMac())`.
- It reads:
  - `miDevice.getRef_channel()`;
  - `StatsUtils.getRef_functionMap().get("ref_function")`, defaulting to `""`;
  - `StatsUtils.getRef_contentMap().get("ref_content")`, defaulting to `""`.
- It calls `StatsUtils.ontrackDataToJson(ref_channel, ref_function, ref_content)`.
- `ontrackDataToJson` builds a `JSONObject` with `putOpt` in this order:
  `ref_channel`, `ref_function`, `ref_content`.
- The JSON string is encoded with `StandardCharsets.UTF_8` and passed to
  `CmdSessionControl.setPlaySource(byte[])`.

Therefore the official source-side non-empty `0x0040` payload shape is now
localized as UTF-8 JSON, for example:

```json
{"ref_channel":"playpage","ref_function":"single_room","ref_content":"music_wangyiyun"}
```

This is implemented as an offline byte codec in
`src/DLNACast.Core/MiPlay/MiPlaySetPlaySourcePayloadCodec.cs`.

## What this still does not prove

The key missing proof is no longer the `0x0040` JSON shape. The missing proof is
how Android `AppInfo(appId, signature, platformType=1, flags)` and
`ServiceName.toMergeString()` enter this legacy 8899 command session, and what
state transition a non-empty `0x0040` is expected to trigger on the current LX06
`1.94.13` receiver before any Open/AddMirror/media path.

Still forbidden without a separate plan and fresh authorization:

- live non-empty `0x0040`;
- `0x0058`;
- `Cmd_Open/openDevice 0x0000`;
- `Cmd_AddMirror 0x002e`;
- RTSP;
- media, playback, or audio frames.

## Source field follow-up

The follow-up note `docs/miplay-mi13p-source-fields.md` records the recovered `ref_channel` enum values, `ref_content` package map, `ref_function` values, and the negative Java xref boundary between legacy `CmdSessionControl` and Lyra/Continuity `ServiceName` paths.

## Test-backed representation

The evidence model and codec are captured in:

- `src/DLNACast.Core/MiPlay/MiPlayPhoneFirmwareSourceEvidence.cs`
- `src/DLNACast.Core/MiPlay/MiPlaySetPlaySourcePayloadCodec.cs`
- `tests/DLNACast.Tests/MiPlayPhoneFirmwareSourceEvidenceTests.cs`
- `tests/DLNACast.Tests/MiPlaySetPlaySourcePayloadCodecTests.cs`
- `src/DLNACast.Core/MiPlay/MiPlayPhoneFirmwareSourceFieldEvidence.cs`
- `tests/DLNACast.Tests/MiPlayPhoneFirmwareSourceFieldEvidenceTests.cs`