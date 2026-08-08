# MiPlay Cmd_SetPlaySource payload semantics boundary

This note is offline-only. It uses the extracted LX06 ROM `1.88.51` receiver
binaries, previous Mi Connect APK static identity evidence, and the supplied
Mi13P phone firmware source-side evidence, and the 2026-07-24 bounded LX06
`1.94.13` native-no-reset official JSON `0x0040` negative result. It does not authorize another speaker/LAN probe.

## Receiver-side `Cmd_SetPlaySource 0x0040`

In `artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted/usr/bin/mpas`,
`ServerApp::doMpasCommand` compares external command `0x0040` at `0x65840` and
enters the handler at `0x66ad8`.

The handler sends acknowledgement `0x0041` before source payload semantics are
interpreted:

- ACK command move: `0x66b50`
- ACK send call: `0x66b58`
- payload presence gate: `0x66b70`
- JSON parse call: `0x66c70`

Therefore empty `0x0040` was a dispatcher reachability probe only. Non-empty
`0x0040` is not read-only: the receiver parses JSON and assigns source-reference
state.

The localized receiver JSON keys are:

- `ref_channel` at VA `0x13cb8c`, compare `0x66d14`, assignment around `0x67710`
- `ref_function` at VA `0x13cbc0`, compare `0x66ccc`, assignment around `0x67730`
- `ref_content` at VA `0x13ccd8`, compare `0x66ce8`, assignment around `0x67740`

A separate internal/pipe helper logs `setPlaySource Cmd_SetPlaySource[%s][%u]`
at `0x74510`, but it sends command `0x005a` at `0x74544`. Do not conflate this
internal command with external `0x0040`.

## Android source identity

Mi Connect static evidence localizes official Android source identity, but not
the bridge into legacy 8899:

- `PackageUtil.generateAppInfo(Context, uid, pid, invokePkg)` resolves the
  calling package, selects `appId`, computes an X.509 certificate SHA-256
  fingerprint, sets `platformType=1`, and carries package flags.
- `ServiceName.toMergeString()` serializes either `package:name` or `:name`.
- native channel registration/creation receives both `ServiceName` and
  `AppInfo`.

This identity material is not equivalent to the Windows probe's local JSON
fields, and it is not yet proven to enter the legacy 8899 command session.

## Official source-side `0x0040` builder from Mi13P firmware

The Mi13P phone firmware resolves the old missing `0x0040` builder. The useful
route was targeted DEX parsing, not full JADX.

Recovered chain:

1. `StatsUtils.setPlaySource(DeviceManager, Map)` iterates
   `DeviceManager.getMiDeviceList()`.
2. For each `MiDevice`, it looks up `cmdSessionControlMap.get(miDevice.getMac())`.
3. It reads `miDevice.getRef_channel()`.
4. It reads `ref_function` and `ref_content` from the `StatsUtils` maps,
   defaulting missing values to `""`.
5. It calls `StatsUtils.ontrackDataToJson(ref_channel, ref_function, ref_content)`.
6. `ontrackDataToJson` builds `JSONObject.putOpt` fields in order:
   `ref_channel`, `ref_function`, `ref_content`.
7. It encodes the JSON with `StandardCharsets.UTF_8`.
8. It passes those bytes to `CmdSessionControl.setPlaySource(byte[])`.
9. Native `libmirror-jni.so` forwards that byte array as command `0x0040`.

The offline codec is `MiPlaySetPlaySourcePayloadCodec`. It models the official
bytes only; it does not send them.

## `0x0040`, AddMirror, Open ordering boundary

The receiver-local pre-open evidence remains split by role/direction:

- external `0x0040 Cmd_SetPlaySource` is handled and can mutate
  `ref_channel/ref_function/ref_content` state after its immediate `0x0041` ACK;
- receiver-local AddMirror helpers emit `0x002e Cmd_AddMirror` with payload shape
  `<local-ip>:7236&from:<local-ip>&islocal:1` and store pending sequence state;
- external incoming `0x002e` is not handled by `ServerApp::doMpasCommand`, so a
  direct AddMirror probe is a role/direction error;
- `0x002f Cmd_AddMirror_Ack` can re-arm a master `Cmd_Open 0x0000` path;
- `sender-info-prepared` and local-media paths can send `Cmd_Open 0x0000`.

Static receiver order and source-side `0x0040` bytes are localized. The official
external-source order is still incomplete because the AppInfo/ServiceName bridge
and the pre/post `0x0040` state transition are not yet proven.

Until that is found, do not repeat non-empty `0x0040`: the native-no-reset official minimal JSON path has already closed without `0x0041` on LX06 `1.94.13`. `0x0058`, `Cmd_Open`, AddMirror, RTSP, media, playback, and audio remain forbidden.

## Test-backed representation

The model is captured in:

- `src/DLNACast.Core/MiPlay/MiPlaySetPlaySourcePayloadSemanticsEvidence.cs`
- `src/DLNACast.Core/MiPlay/MiPlaySetPlaySourcePayloadCodec.cs`
- `tests/DLNACast.Tests/MiPlaySetPlaySourcePayloadSemanticsEvidenceTests.cs`
- `tests/DLNACast.Tests/MiPlaySetPlaySourcePayloadCodecTests.cs`