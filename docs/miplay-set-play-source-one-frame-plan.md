# MiPlay official `0x0040` one-frame validation plan

This note is an offline preparation artifact. It does not perform a speaker or
LAN operation and does not authorize one by itself.

## Current boundary

The previous live validations are negative but useful:

- an empty SafetyData-wrapped `0x0040 Cmd_SetPlaySource` after mutual SafetyAuth
  closed without a `0x0041` acknowledgement;
- clear legacy empty `0x0040` routes also closed without a `0x0041`.

So repeating the empty-payload route is not justified without new evidence. The
next distinct hypothesis is the official source-side JSON payload recovered from
the Mi13P phone firmware:

```json
{"ref_channel":"playpage","ref_function":"","ref_content":""}
```

The payload is built by `MiPlaySetPlaySourcePayloadCodec`, matching the Android
`StatsUtils.ontrackDataToJson` `putOpt` order and UTF-8 encoding. Native
`libmirror-jni.so` evidence says `setPlaySource` sends command `0x0040`, while
`connectCmdSession2` only explains optional Lyra key material
(`wlan0ip/authKey/streamKey/streamIV`), not AppInfo, ServiceName, signature, or
package identity bridging.

## One-frame plan

Only after fresh explicit user authorization for a single S12 network action,
the prepared plan is:

1. complete mutual SafetyAuth exactly as already validated;
2. send exactly one SafetyData-wrapped command frame:
   - command: `0x0040 Cmd_SetPlaySource`;
   - sequence: the next command-session sequence, currently modeled as `4`;
   - plaintext payload: `{"ref_channel":"playpage","ref_function":"","ref_content":""}`;
3. observe only for `0x0041`;
4. stop on close or any unexpected frame.

The plan forbids retry, fallback, `0x0058`, `Cmd_Open/openDevice 0x0000`,
`Cmd_AddMirror 0x002e`, RTSP listener/response traffic, media, playback, and
audio.

## Probe entrypoint

The live-capable Probe path is intentionally separate from the old empty-payload
ACK probe:

```powershell
dotnet run --project tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-safety-mutual-auth-official-json-set-play-source-one-frame-probe=192.168.10.4 --miplay-confirm-official-json-set-play-source-one-frame --miplay-post-auth-observe-seconds=5
```

Do not run it without first announcing the exact network action and receiving
fresh explicit user authorization in the parent task. The address option enters
the mutual SafetyAuth flow; the confirmation flag is an additional software gate
for the single official JSON `0x0040` frame. The old empty-payload route remains
`--miplay-native-safety-mutual-auth-set-play-source-ack-probe=...` and should not
be repeated unless new evidence specifically justifies it.

## Implementation update: native no-reset outbound profile

After the 2026-07-24 dry-run result, the live-capable official JSON one-frame
Probe path no longer encrypts `0x0040` with the old
`observed-inbound-promoted-outbound-type1` negative-control state. It now
reconstructs a separate `native-no-reset-outbound-type2` command cipher from the
real local SafetyAuth plaintexts already produced in the mutual-auth run: local
`0x1402`, then local `0x1403`.

The verified inbound SafetyAuth candidate remains available for response
decryption/observation. This separates two states that the old Probe conflated:

1. inbound SafetyAuth/session decrypt candidate:
   `peer-first:observed-s12-inbound-iv-type1`;
2. first post-auth outbound business command profile:
   `native-no-reset-outbound-type2`.

This is still only a prepared live-capable path. Running it would send a real
post-auth `0x0040` business frame and therefore still requires a fresh explicit
authorization statement immediately before execution.

## Code-backed gate

The decision model is `MiPlaySetPlaySourceOneFrameProbePlan`. The offline frame builder is `MiPlaySetPlaySourceOneFrameProbe`; it can construct the clear command frame and SafetyData-wrapped command frame for tests, but it does not bypass the decision gate.

- `CanPreparePlan=true`, `CanSendNow=false`: evidence is sufficient to describe
  the one-frame plan, but there is no fresh explicit user authorization.
- `CanPreparePlan=true`, `CanSendNow=true`: all static, cryptographic, prior
  negative, no-media, and explicit-authorization gates are present.
- `CanPreparePlan=false`: the plan has drifted outside the minimal official
  payload or one-frame/no-media boundary.

This keeps static structure, cryptographic/session readiness, and live semantic
validation separate. A successful `0x0041` would prove only that official
`0x0040` was accepted; it would not authorize `0x0058`, Open, AddMirror, RTSP,
media, playback, or audio.
## Live result: S12 `192.168.10.4`

On 2026-07-23, the prepared one-frame path was run once against S12
`192.168.10.4`.

Observed sequence:

1. connected from `192.168.10.9:12037` to `192.168.10.4:8899`;
2. sent native source version `0x0036`, sequence `0x0001`, payload
   `3.1.6030516`;
3. received legacy `0x0028`, sent `0x0029`;
4. sent SafetyInfo `0x1400`, sequence `0x0002`;
5. received `0x0037` control-session version `2.1.5091615`;
6. observed notify frames `mode=2`, `mediaInfoEx`, and `state=3`, with no notify
   reply;
7. received SafetyInfo ack `0x1401 result=0`;
8. sent local SafetyAuth `0x1402`, sequence `0x0003`;
9. decoded peer SafetyAuth `0x1402`, sequence `0x0000`;
10. sent peer challenge acknowledgement `0x1403`;
11. verified peer acknowledgement `0x1403`, sequence `0x0003`;
12. sent exactly one SafetyData-wrapped official JSON `0x0040`, sequence
    `0x0004`, plaintext length `61`, encrypted payload length `73`;
13. observed no `0x0041`; the TCP connection aborted while reading with native
    socket error `10053`.

No retry, fallback, `0x0058`, Cmd_Open/openDevice, AddMirror, RTSP, media, RTP,
playback, or audio was sent.

Conclusion: official `ref_channel/ref_function/ref_content` JSON is not
sufficient to reach the LX06 command handler. Since both the previous empty
payload and this official JSON payload fail without `0x0041`, the next missing
layer is more likely post-auth SafetyData direction/IV state, command envelope,
or current LX06 `1.94.13` handler ownership than the JSON payload semantics
itself. Do not repeat this same one-frame probe without new evidence.
## Live result: native no-reset outbound `0x0040`

On 2026-07-24, after explicit user authorization, the prepared native no-reset
one-frame path was run once against S12 `192.168.10.4`.

Observed sequence:

1. connected from `192.168.10.9:7576` to `192.168.10.4:8899`;
2. sent native source version `0x0036`, sequence `0x0001`, payload
   `3.1.6030516`;
3. received legacy `0x0028`, sequence `0x021e`, sent `0x0029`;
4. sent SafetyInfo `0x1400`, sequence `0x0002`;
5. received `0x0037` control-session version `2.1.5091615`;
6. observed notify frames `mode=2`, `mediaInfoEx`, and `state=3`, with no notify
   reply;
7. received SafetyInfo ack `0x1401 result=0`, `authKey=1`, `authAlgorithm=4`,
   `integrity=1`, `aesKey=1`, `aesIv=2`;
8. sent local SafetyAuth `0x1402`, sequence `0x0003`;
9. decoded peer SafetyAuth `0x1402`, sequence `0x0000`;
10. sent peer challenge acknowledgement `0x1403`;
11. verified peer acknowledgement `0x1403`, sequence `0x0003`;
12. sent exactly one SafetyData-wrapped official JSON `0x0040`, sequence
    `0x0004`, plaintext length `61`, encrypted payload length `73`, using
    `safetyAuthCandidate=peer-first:observed-s12-inbound-iv-type1` for verified
    auth/session observation and `outboundProfile=native-no-reset-outbound-type2`
    for the command encryption;
13. observed no `0x0041`; the device closed the control connection after
    `7` follow-up frames.

No retry, fallback, `0x001e`, `0x0058`, Cmd_Open/openDevice, AddMirror, RTSP,
media, RTP, playback, or audio was sent.

Conclusion: changing the first post-auth outbound cipher from the old
promoted-inbound-IV negative control to `native-no-reset-outbound-type2` is not
sufficient to make official minimal JSON `0x0040` accepted. This rules out the
old promoted-IV state as the only failure, but it does not prove receiver-side
acceptance or authorize the next business frame. The next missing layer is now
more likely command ordering, source/session context, envelope ownership, or
current LX06 `1.94.13` handler state. Do not repeat `0x0040` without new
offline evidence.

Test-backed class:
`MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence`.

## Follow-up offline boundary

The official JSON one-frame result no longer points at missing
`ref_channel/ref_function/ref_content` fields as the primary cause. The later
native-no-reset run also rules out the old promoted-inbound-IV outbound state as
the only blocker. The current productive target is now above raw SafetyData
bytes: official command ordering, source/session context, envelope or handler
ownership, and the post-`DealSafetyDone` state transition around `0x0040 ->
0x0041`.

Do not repeat this `0x0040` run or send any SafetyData business command from this
state.
