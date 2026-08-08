# MiPlay official post-auth sequence live validation

This note records one explicitly authorized, bounded S12 live validation. It
does not authorize any replay, retry, Open, AddMirror, RTSP, media, playback,
or audio frame.

## Run scope

- Target: `192.168.10.4:8899`
- Local endpoint observed by the Probe: `192.168.10.9:4434`
- Current LX06 firmware boundary: user-confirmed `1.94.13`
- Control-session version acknowledgement observed on the wire: `2.1.5091615`
- Native source version sent: `3.1.6030516`
- Probe command:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-native-safety-mutual-auth-official-post-auth-sequence-probe=192.168.10.4 `
  --miplay-confirm-official-post-auth-sequence `
  --miplay-post-auth-observe-seconds=20
```

The Probe boundary was the recovered official order:

`0x0058 sourceName/mSourceBtMac -> 0x001e -> 0x0058 canAlonePlayCtrl -> 0x0058 alonePlayCapacity -> 0x0034 -> 0x0040`

The runner was configured to require same-sequence `0x001f` before sending
`0x0034`, then same-sequence `0x0035` with `mirrorMode=2` before sending
`0x0040`. It was also configured to send no Open, AddMirror, RTSP, media,
playback, or audio.

## Observed result

Mutual SafetyAuth still succeeded:

- 0x0036 source version sent at sequence `0x0001`;
- 0x0037 receiver version acknowledged as `2.1.5091615`;
- 0x1401 result `0` accepted;
- local 0x1402 / peer 0x1402 / local 0x1403 / peer 0x1403 completed with
  candidate `peer-first:observed-s12-inbound-iv-type1`.

Before post-auth data, the receiver sent the usual notify frames:

- 0x0022 `mode=2`, sequence `0x0335`;
- 0x0022 `mediaInfoEx`, sequence `0x0336`;
- 0x0022 `state=0`, sequence `0x0337`.

The only post-auth SafetyData command sent was the first planned 0x0058:

- step: `SendSourceName`;
- command: `0x0058`;
- sequence: `0x0004`;
- outbound profile: `native-no-reset-outbound-type2`;
- plaintext payload length: `51`;
- encrypted payload length: `73`;
- plaintext payload:

```json
{"sourceName":"DLNACast Windows","mSourceBtMac":""}
```

Immediately after that first 0x0058, the connection aborted with socket native
error `10053`. No post-auth response frame was observed after the 0x0058, and
the Probe sent no `0x001e`, `0x0034`, `0x0040`, Open, AddMirror, RTSP, media,
playback, audio, retry, or fallback.

## Interpretation

This is a useful negative result, but its boundary is early and narrow:

- It does not test the later recovered official order beyond the first 0x0058.
- It does not reject `0x001e`, `0x0034`, or `0x0040`, because none of those
  frames were sent.
- It does reject treating the Probe default identity
  `sourceName=DLNACast Windows` plus empty `mSourceBtMac` as equivalent to the
  recovered official phone identity.
- The rooted phone pcap had a first 0x0058 SafetyData payload length of `105`,
  while this Probe first 0x0058 was `73`. The recovered source name from the
  official sender is `Xiaomi 13 Pro`, and the `mSourceBtMac` field is non-empty
  in the official shape.

So the earliest unresolved gate is now the source identity / first 0x0058
local-device-info context, not SetPlaySource JSON semantics and not media
transport.

## Follow-up offline closure

The first captured frame starts after the previous CBC IV, so the first 16
plaintext bytes are not directly decrypted from the pcap. But the known suffix
starts with `iaomi 13 Pro`, and the missing first 16 bytes are exactly:

```text
{"sourceName":"X
```

So the full official first 0x0058 JSON is now reconstructed offline:

```json
{"sourceName":"Xiaomi 13 Pro","mSourceBtMac":"<32-char uppercase MD5>"}
```

That JSON is 80 bytes. SafetyData v1 adds a full 16-byte zero block for aligned
plaintext, so the expected SafetyData container length is `9 + 96 = 105`, which
matches the official rooted pcap's first 0x0058. The Probe's official-sequence
plan can still prepare this recovered identity for offline comparison. It is now
hard-blocked from network use because the rooted pcap starts mid-session at
sequence `0x013a` and does not prove that this `0x0058` follows fresh
`DealSafetyDone`.

Important boundary: length/identity matching was a necessary correction, not
proof that the S12 will accept the frame. The recovered-identity live update
below confirms that a 105-byte first 0x0058 can still fail if the surrounding
post-auth SafetyData/session context is wrong.

Before any future live retry, run the no-network dry-run:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-official-post-auth-sequence-dry-run
```

Expected first-frame summary:

- `usesRecoveredIdentity=True`;
- first plaintext length `80`;
- first SafetyData payload length `105`;
- previous default-Windows SafetyData payload length `73`;
- `safeForNetworkUse=False`.

## Recovered-identity live validation update

A later explicitly authorized run sent the recovered official first 0x0058
identity instead of the default Windows identity:

- target: `192.168.10.4:8899`;
- local endpoint: `192.168.10.9:1776`;
- mutual SafetyAuth completed again;
- first command sent by this Probe after auth: `0x0058`, sequence `0x0004`;
- plaintext length: `80`;
- SafetyData payload length: `105`;
- plaintext shape:

```json
{"sourceName":"Xiaomi 13 Pro","mSourceBtMac":"<32-char uppercase MD5>"}
```

The S12 still closed immediately after that first 0x0058 with socket native
error `10053`. No `0x0059` acknowledgement was observed, and no `0x001e`,
`0x0034`, `0x0040`, Open, AddMirror, RTSP, media, playback, audio, retry, or
fallback was sent.

This means the primary remaining gap is no longer just `sourceName` /
`mSourceBtMac` identity or first-frame SafetyData length. The next useful work
is offline comparison of the Probe's post-auth outbound SafetyData state against
the official native command-session state: cipher phase, IV/session fork,
possible native `SafetyDataDeal` reset/reinstall after `DealSafetyDone`, and
missing listener/session context.
