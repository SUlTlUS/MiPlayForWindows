# MiPlay source identity context boundary

This note separates three things that are easy to accidentally merge:

1. target receiver context proven by `0x001e -> 0x001f`;
2. official sender-side identity and `0x0040 SetPlaySource` payload bytes;
3. authorization to send live business/open/media frames.

Only the first two are partly localized. The third remains forbidden without a
separate exact plan and fresh authorization.

## Proven target context

The current target context comes from legacy clear `0x001e GetDeviceInfo` and a
parsed `0x001f GetDeviceInfo_Ack` payload:

- model: `LX06`
- ROM version observed on the actual target path: `1.94.13`
- support: `audio`

This proves the target receiver identity. It does not create the source-side
Android identity required by later business commands.

## Android source identity

Static evidence localizes the official Android identity shape:

- `AppInfo(appId, signature, platformType=1, flags)`;
- package signing certificate SHA-256 fingerprint;
- `platformType=1`;
- `ServiceName.toMergeString()` as `package:name` or `:name`.

Targeted all-DEX trace artifact:

- `artifacts/phone_firmware/mi13p_os3_0_313/phone_source_all_dex_ref_identity_trace.json`

`PackageUtil.generateAppInfo(...)` constructs Continuity `AppInfo` as:

- `appId = package name`;
- `signature = PackageUtil.getSignature(package)`;
- `platformType = 1`;
- `flags = getPackageFlags(package)`.

`generateCustomAppInfo(...)` also uses `platformType = 1`, but with empty
signature and `flags = 0`.

`PackageUtil.getSignature(...)` reads the package signing certificate through
`PackageInfo.signingInfo.getApkContentsSigners()[0].toByteArray()`, wraps it as
X509 certificate data, hashes `Certificate.getEncoded()` with `SHA-256`, and
formats uppercase colon-separated hex.

`ServiceName.toMergeString()` builds `packageName:name` when a package name is
present, otherwise `:name`.

The important negative evidence is now stronger than the earlier MiLink-only
trace: across all extracted MiLinkOS3Cn + MirrorOS3 DEX files, the targeted
caller intersection is empty:

| Referrer set | Count |
|---|---:|
| `CmdSessionControl` / legacy command refs | 206 |
| Continuity `AppInfo` refs | 29 |
| `ServiceName` / Continuity channel refs | 334 |
| `CmdSessionControl ∩ AppInfo` | 0 |
| `CmdSessionControl ∩ ServiceName` | 0 |
| `CmdSessionControl ∩ signature` | 0 |

This identity material is passed to Continuity/NetBus/IDM channel paths. It is
still not proven to be bridged into the legacy 8899 `CmdSessionControl` path.
The checked native `connectCmdSession2` path explains only the optional
`SecretKeyCommand` / `setLyraInfo` bridge for Lyra key material, not
`AppInfo` / `ServiceName` / package signature. The missing bridge is therefore
either a different native/session field, a runtime-injected boundary, or not
actually required before `0x0040 SetPlaySource`.

## Official `0x0040 SetPlaySource` payload bytes

The Mi13P phone firmware now localizes the official sender-side builder:

- `StatsUtils.setPlaySource(DeviceManager, Map)`;
- `StatsUtils.ontrackDataToJson(ref_channel, ref_function, ref_content)`;
- `JSONObject.putOpt` order: `ref_channel`, `ref_function`, `ref_content`;
- UTF-8 bytes passed to `CmdSessionControl.setPlaySource(byte[])`;
- native command map confirms `0x0040 = SetPlaySource` and
  `0x0041 = SetPlaySource_Ack`.

The project models this with `MiPlaySetPlaySourcePayloadCodec`.

## Source field update

The Mi13P DEX trace now recovers the `ref_channel` enum values (`controlcenter`, `nearfield`, `xiaoai_phone`, `farfield`, `lockscreen`, `notification`, `playpage`, `world`, `relay_card`, `nfc`), the `ref_content` package map for common music/FM apps, and the `ref_function` values (`single_room`, `multi_room`, `stereo`). `onTopActiveSessionChange` updates `ref_content` and then calls `setPlaySource`, while `startCommandChannel` stays on the legacy `CmdSessionControl` path without Java-level `ServiceName`/`AppInfo` xrefs.

## Remaining blocker

The native-no-reset official JSON `0x0040` run on 2026-07-24 changes the blocker:
minimal payload bytes and the old promoted-IV outbound state are no longer the
primary suspects. The missing proof is now narrower:

- whether `AppInfo` / `ServiceName` / package signature / current source context
  ever enter the legacy 8899 command session outside the checked
  `connectCmdSession2 -> setLyraInfo` path;
- what command ordering or session state must exist before external `0x0040` is
  accepted with a `0x0041` acknowledgement;
- what state transition a successful non-empty `0x0040` is expected to cause
  before any `Cmd_Open`, AddMirror, RTSP, media, playback, or audio path;
- which current LX06 `1.94.13` handler owner accepts `0x1400..0x1403` but closes
  every tested `0x0040` route before `0x0041`.

Therefore the next useful offline target is not another `0x0040` probe and not
full JADX. It is a targeted native/session-state trace around `CmdSource` fields,
`sendCmdPayload`, command ordering after `DealSafetyDone`, and the post-auth
`0x0040 -> 0x0041` state effect.

## Live boundary

Offline construction of `0x0040` JSON bytes is supported and a single
native-no-reset live validation has already been performed. Repeating it is not
authorized by this evidence alone.

Still forbidden without new offline evidence and fresh explicit authorization:

- live non-empty `0x0040` repeat;
- `0x0058`;
- `Cmd_Open/openDevice 0x0000`;
- `Cmd_AddMirror 0x002e`;
- RTSP;
- media, playback, or audio frames.