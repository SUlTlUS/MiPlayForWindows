# MiPlay post-auth getDeviceInfo live-readonly validation plan

This is the first candidate that is narrow enough for a new real-device validation, but it still requires an explicit pre-send review before running.

## Candidate

- Target: one S12, preferably `192.168.10.4`
- Control port: `8899/TCP`
- Prerequisite exchange: existing verified native bootstrap + mutual SafetyAuth path only
- Post-auth frame: exactly one SafetyData-wrapped `Cmd_GetDeviceInfo`
- Outer command: `0x001e`
- Sequence: `0x0004` when native bootstrap is used
- Plaintext payload: empty, length `0`
- SafetyData outbound profile: native no-reset outbound profile reconstructed from local `0x1402` and `0x1403` plaintexts
- Expected response: same-sequence `0x001f`
- Success gate: decryptable SafetyData payload length `>= 40`, decodable by `MiPlayLegacyDeviceInfoPayloadCodec`

## Hard stop boundary

The validation sends no `0x0040`, `0x0058`, `Cmd_Open`, `Cmd_AddMirror`, RTSP, media, RTP, playback, audio, retry, or fallback frame.

Any close, timeout, wrong command, wrong sequence, SafetyData decode failure, or too-short/undecodable `0x001f` is a bounded result and must not trigger a fallback command.

## Probe command prepared

The prepared Probe entrypoint is:

```powershell
dotnet run --project tools\DLNACast.Probe\DLNACast.Probe.csproj --no-build -- --miplay-native-safety-mutual-auth-readonly-get-device-info-probe=192.168.10.4 --miplay-confirm-readonly-get-device-info-one-frame --miplay-post-auth-observe-seconds=5
```

Do not run this command until immediately before the run, after restating the exact sent frame and receiving explicit user approval for that network action.
