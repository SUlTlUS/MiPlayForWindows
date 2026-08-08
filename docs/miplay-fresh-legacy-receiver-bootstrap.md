# MiPlay fresh legacy-clear receiver bootstrap

This note records the receiver plan derived from the authorized 2026-08-07
phone capture and the later separately authorized one-frame validation. It does
not authorize another network run.

This receiver is a protocol instrument, not the product direction. Its purpose
is to capture the official phone's source-side ordering so that DLNACast can
ultimately act as the MiPlay audio source toward a real speaker. The target
implementation remains Windows source -> S12 receiver.

## Source-side causality

The distinct receiver was `192.168.10.9:8899`; the phone source was
`192.168.10.58:50516`. The receiver sent only legacy
`0x0028(seq=0, "123456789")`.

The raw wire capture contains:

`0x0036(seq=0) -> 0x0029(seq=0) -> 0x001e(seq=1) -> 0x0058(seq=2) -> 0x001a(seq=3,4,5)`

The matching `com.milink.service 12.4.8.13` logcat identifies the test session
as `DID8899:743af401ceee` and connects it to `192.168.10.9:8899`. Its causal
order is:

1. native `CmdSource::onRecvCmd` receives `Cmd_Auth` (`0x0028`);
2. Java receives `cmd_sessionsuccess` and calls `onSuccess`;
3. `onSuccess` calls `getDeviceInfo` and `setLocalDeviceInfo` with
   `{"sourceName":"MI PAD 4\\/Plus"}`;
4. no `0x0037` was received from the test receiver before these calls.

Therefore `0x0037` is an optional receiver version report on this compatibility
path, not a prerequisite for the first device-info commands.

On the simultaneous real-S12 sessions, receipt of `0x001f` produces native
`onDeviceInfo`, then Java `onDeviceInfo`, same-account `0x0058`, device report,
and `getMirrorMode`. The test receiver never sent `0x001f`, so that callback is
the first missing gate.

## Receiver evidence for `0x001f`

Three independent sources align:

- LX06 1.88.51 `mpas` dispatches `0x001e` and sends `0x001f` through helper
  `0x368bc`, preserving the request sequence. The cached path is at `0x68350`;
  the uncached completion at `0x65320` reads the saved sequence and sends the
  same ACK.
- LX06 1.94.13 previously accepted a legacy-clear empty `0x001e(seq=2)` and
  returned `0x001f(seq=2)` with a 415-byte OPack-like device map.
- The recovered payload grammar is a 24-bit big-endian body length followed by
  repeated `keyLength, ASCII key, 0x0c, UTF-8 valueLength, UTF-8 value` fields.

`MiPlayLegacyDeviceInfoPayloadCodec.Encode` now implements the inverse of the
existing decoder with strict duplicate-name, printable-ASCII key, 16-bit value,
and 24-bit body-length checks.

`MiPlayFreshLegacyReceiverBootstrapPlanner` builds a deterministic 20-field
profile tied to the distinct mDNS identity:

- `deviceId` is the capture profile GUID;
- `bluetoothMac` is a stable locally administered unicast address derived from
  the same GUID;
- `deviceType=4`, `support=audio`, `channel=center`;
- `miName` is the distinct capture name;
- model/ROM identify the implementation as `DLNACast.LegacyReceiver/0.1.0`;
- account, house, room, MIoT, group, and serial identifiers are intentionally
  empty rather than copied from a real S12.

The planner encodes exactly one clear `0x001f` with the observed `0x001e`
sequence. It does not build `0x0037`, `0x0059`, `0x001b`, Open, AddMirror,
RTSP, media, playback, or audio frames. The plan remains
`SafeForNetworkUse=false` as a runtime safety property: a future network run
still needs separate explicit authorization. Acceptance of this exact profile
by the current phone is recorded below.

## Minimal future validation boundary

A future run requires fresh explicit authorization. Its complete outbound
accounting would be:

1. one existing legacy `0x0028` challenge;
2. after a valid `0x0029` and an observed empty clear `0x001e`, one
   same-sequence clear `0x001f` using the deterministic distinct-receiver map;
3. nothing else.

The receiver must ignore the concurrent `0x0058` and heartbeats and stop after
observing whether the phone logs `onDeviceInfo` or sends a new command. This is
still a read-only identity/bootstrap validation, not permission to open or play
media.

The Probe now prepares this boundary behind a mandatory confirmation gate:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-fresh-legacy-device-info-dry-run
```

For request sequence `1`, the offline dry-run deterministically reports a
377-byte device-info payload, 386-byte complete `0x001f` frame, 20 fields, and
frame SHA-256
`C344E8224C2ED699EE4F0EFDBE407821223C34C23D4027F8FAEA131517DD9FB3`.
It keeps `canSendNow=false` and opens no socket.

The separately authorized network command would be:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-fresh-legacy-device-info-receiver=192.168.10.9 `
  --miplay-confirm-fresh-legacy-device-info-receiver `
  --miplay-capture-seconds=120
```

Without the confirmation option, the program throws before creating mDNS or TCP
sockets. With confirmation, `MiPlayFreshLegacyReceiverBootstrapSession` still
requires a cryptographically valid `0x0029`, one empty clear `0x001e`, exactly
one previously sent `0x0028`, zero prior `0x001f`, and no other outbound frame.
Only then can `MiPlayFreshLegacyReceiverProbePolicy` release one same-sequence
`0x001f`. The mode observes subsequent metadata and never responds again.

## 2026-08-07 authorized live result

The command above was run once with fresh explicit authorization. The complete
outbound count was exactly two frames:

1. `0x0028(seq=0, payloadLength=9, "123456789")`;
2. after cryptographically verifying `0x0029` and observing empty
   `0x001e(seq=1)`, exactly one
   `0x001f(seq=1, payloadLength=377, frameLength=386)` with SHA-256
   `C344E8224C2ED699EE4F0EFDBE407821223C34C23D4027F8FAEA131517DD9FB3`.

The official phone source first sent `0x0058(seq=2, payloadLength=31)`. After
the receiver's `0x001f`, it sent a new
`0x0058(seq=3, payloadLength=19)` with frame SHA-256
`DB75703B2F77B6BA8A63D0611104DA6DE1266A144B00D985B905B28CC9A23FC6`.
The receiver then stopped deliberately without sending `0x0059` or any other
reply.

The 19-byte payload is now recovered byte-for-byte from two independent offline
constraints:

- `MiLinkOS3Cn/classes3.dex` method
  `MiplaySessionCtrProxy.setLocalDeviceInfoSameAccount` at `0x2b76c0` calls
  `DeviceManager.isSameAccountToJson(int)` before passing the returned bytes to
  `CmdSessionControl.setLocalDeviceInfo`;
- `DeviceManager.isSameAccountToJson` at `0x26ee20` creates a `JSONObject`,
  calls `putOpt("isSameAccount", Integer.valueOf(value))`, serializes it, and
  returns UTF-8 bytes.

For value `0`, the official builder yields exactly:

```json
{"isSameAccount":0}
```

Encoding those 19 bytes as clear legacy `0x0058(seq=3)` reproduces the observed
complete-frame SHA-256 exactly. Values `1`, `2`, string `"0"`, or an extra JSON
field do not reproduce it. The live sender therefore reported integer
`isSameAccount=0`; this conclusion no longer depends only on payload length or
the earlier log label.

This sequence is wire-level positive evidence that the source accepted the
deterministic 20-field device-info response and advanced through its
`onDeviceInfo`/`setLocalDeviceInfo` path. It closes the first fresh legacy
receiver identity/bootstrap gate. It does not prove Open, mirror negotiation,
RTSP, playback, media, or audio readiness, and it does not authorize any of
those commands.

The privacy-preserving transcript is stored at
`artifacts/phone_live/fresh-legacy-captures/fresh-legacy-device-info-20260807-0211.stdout.log`.
No raw sender payload bytes or permanent phone identifiers were logged. Any
future network run requires new explicit authorization.

## Official post-`0x001f` progression

Targeted DEX and current rooted-native disassembly now recover the exact source
order after the phone accepts `0x001f`:

1. `CmdSessionControl.onCmdSessionDeviceInfoAck` (`classes3.dex` `0x2962dc`)
   dispatches to `onDeviceInfo`;
2. `MiplaySessionCallbackManage.cmdSessionDevicesInfo` (`0x2b1eec`)
   requires `cmdSessionState == 1`, parses the device-info payload, calls
   `verifySameAccount` at `0x2b1fd4`, then immediately calls `handleDevice` at
   `0x2b1fda`;
3. for the captured value-zero branch, `verifySameAccount` calls
   `setLocalDeviceInfoSameAccount(mac, 0)` at `0x2b2d8a`, producing the
   observed `0x0058(seq=3)` payload `{"isSameAccount":0}`;
4. `handleDevice` calls `setDevice` at `0x2b251e` and `getMirrorMode` at
   `0x2b2528`.

In the current rooted phone library
`com.milink.service 17.2.4.1.2606161948/libaudiomirror-jni.so` (SHA-256
`DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF`),
`CmdSource::setLocalDeviceInfo` and `CmdSource::getMirrorMode` consume and
increment the same sequence counter at `CmdSource+0x2c0`. The former loads
command `0x0058` at `0x17723c`; the latter loads command `0x0034` at
`0x177690` with null payload. `CmdSource::sendPayload` posts its message
asynchronously at `0x17b708`.

The queue order is now also statically closed. Both methods call the same
`CmdSource::sendCmdPayload` on the same `CmdSource` instance. `sendPayload`
gets that instance's handler at `0x17b618`, constructs message code `3`, and
calls `AMessage::post(0)`. In the matching current rooted
`libmirror-jni.so` (SHA-256
`35778B2DA7D95D02FFD37AE1AD645D4B23D7E0A1718604F7B40D4EB9E04810DE`),
`AMessage::post` reaches `ALooper::post` at `0x262600`. Its zero-delay path
starts at `0x25ee80`, locks the queue at `0x25ee88`, appends at the tail at
`0x25eedc`, and unlocks at `0x25eee8`. Message code `3` maps through
`CmdSource::onMessageReceived` to `onSendCmd` at `0x17e9d8`; `onSendCmd`
retrieves the queued buffer and invokes the same command-session write at
`0x1801ac`. Thus the recovered path preserves `0x0058` before `0x0034` in
the native FIFO, rather than merely proving two unordered asynchronous posts.

Consequently the official source does not wait for a receiver ACK between the
two Java calls. Given the captured `0x0058(seq=3)`, the next command queued by
this branch is deterministically:

```text
command:      0x0034 GetMirrorMode
sequence:     0x0004
payload:      <empty>
frame bytes:  24 00 34 00 04 00 00 00 00
frame length: 9
frame SHA-256: DDDAFA73414A3B71D7DF04B90FDC20BDDDAE735F852C1125E9BB576223032FD4
```

The current native receive jump table maps `0x0059` to branch `0x180bc4`,
which dispatches event `210028` (`CMD_SESSION_INFO_SET_DEVICEINFO_ACK`). The
DEX packed-switch case for `210028` reaches the `return-void` at `0x297072`
without a Java callback or state transition. This proves `0x0059` is not a
Java-side prerequisite for queuing `0x0034`; it does not assert that the native
receive path has no internal side effects.

The prior live receiver stopped immediately after observing `0x0058`, so it
closed before it could read the already-queued `0x0034`. Absence of `0x0034`
from that transcript is therefore an observation-window artifact, not evidence
that `0x0059` was required. `MiPlayFreshLegacyPostDeviceInfoProgressionEvidence`
pins the exact DEX/native identities, offsets, sequence transition, predicted
frame hash, and negative cases. It remains entirely offline and reports
`CanUseNetwork=false`.

The next useful live check, if separately authorized, is observation-only:
repeat the already proven `0x001f` bootstrap and keep the socket open briefly
after `0x0058`, without sending `0x0059`, to observe whether the queued
`0x0034(seq=4)` arrives. It must not answer `0x0034` with `0x0035` and must not
send Open, AddMirror, RTSP, playback, media, or audio frames.

The observation path also has an offline dry-run:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-fresh-legacy-post-device-info-observation-dry-run
```

The dry-run opens no socket and reports the exact predicted 9-byte frame and
SHA-256 above. A future explicitly authorized live command would additionally
require `--miplay-observe-post-device-info-get-mirror-mode` on the existing
confirmed receiver mode. Without that extra flag, the previous stop-on-`0x0058`
behavior is preserved. With it, the post-response observation state accepts
either the byte-exact advanced `0x0058(seq=3,isSameAccount=0)` directly, or one
byte-exact already queued initial `0x0058(seq=2)` source-name race before that
advanced frame, followed by the byte-exact empty `0x0034(seq=4)`. The initial
frame may occur at most once. The observer never constructs a response and
reports `AllowsFollowUpSend=false`; any duplicate or mismatch stops the run
immediately.

### 2026-08-07 authorized observation attempt

The inbound-only extension was run once with explicit authorization against
the test receiver at `192.168.10.9`. The receiver advertised for the bounded
120-second window, but the phone did not establish TCP `8899`. The code sends
the legacy challenge only after `AcceptTcpClientAsync` succeeds, so the exact
TCP outbound accounting was zero `0x0028`, zero `0x001f`, and zero follow-up
frames. The window ended normally.

This is neither a positive nor a negative protocol result: the source session
was never created. It must not be used to revise the recovered
`0x0058 -> 0x0034` order. The sanitized transcript is stored at
`artifacts/phone_live/fresh-legacy-captures/fresh-legacy-post-device-info-observation-20260807-115514.stdout.log`.
Any retry requires fresh explicit authorization and a newly triggered phone
sender connection; the Probe will not retry automatically.

A later read-only ADB check at the user-supplied Mi Pad 4 endpoint confirmed
the expected `MI PAD 4/Plus` running `com.milink.service 12.4.8.13`. The main
process and foreground `MiPlayAudioService` were active, and an authorized
read-only root check returned `uid=0` with `u:r:magisk:s0`. At that later time,
however, Android reported `mWakefulness=Asleep`, display state `OFF`, and the
keyguard showing. No recent `getDeviceInfo`, `getMirrorMode`, or connection to
the test receiver appeared in the filtered logs. This is consistent with the
sender UI never being triggered, but it is post-window state and therefore not
proof of the exact tablet state throughout the earlier 120 seconds.

### 2026-08-07 authorized automatic-discovery observation

A second, separately authorized 120-second run was started after the tablet was
available. The user did not select the test receiver: MiPlay automatically
searched the LAN and briefly displayed it. The phone then established TCP from
`192.168.10.58:50730` to `192.168.10.9:8899`.

The complete outbound accounting remained exactly two frames:

1. one `0x0028(seq=0,payloadLength=9,"123456789")`;
2. after the byte-exact valid `0x0029(seq=0)` and empty `0x001e(seq=1)`, one
   `0x001f(seq=1,payloadLength=377)` with complete-frame SHA-256
   `C344E8224C2ED699EE4F0EFDBE407821223C34C23D4027F8FAEA131517DD9FB3`.

The next inbound frame was the known initial
`0x0058(seq=2,payloadLength=31)` with SHA-256
`1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113`.
It contains the already recovered source-name JSON. The first observer revision
expected only advanced `seq=3`, so it deliberately stopped and closed the socket
without sending `0x0059`, `0x0035`, or any other frame. This safe disconnect is
consistent with the receiver appearing briefly and then disappearing from the
automatically populated device list; it is not a phone-side rejection result.

The ordering proves a legitimate race: the initial source-name `0x0058(seq=2)`
can arrive after the receiver has already sent `0x001f`, even though the earlier
capture read it before `0x001f`. A filtered read-only phone log after the run
also reached `onDeviceInfo`, `setLocalDeviceInfoSameAccount`, and
`getMirrorMode` for this session. That is positive phone-side evidence that the
`0x001f` was accepted and the source progressed to GetMirrorMode. Advanced
`0x0058(seq=3)` and `0x0034(seq=4)` were not observed on this socket because the
observer had already disconnected, so their wire arrival remains unproven.

The sanitized transcript is stored at
`artifacts/phone_live/fresh-legacy-captures/fresh-legacy-post-device-info-observation-20260807-125156.stdout.log`.
The revised offline state machine permits that byte-exact `seq=2` race once and
continues read-only. No retry is authorized by this result; another network run
would require fresh explicit authorization.

## Offline `0x0059` boundary after `isSameAccount`

The optional receiver-side ACK can now be narrowed, but is not authorized for
network use and is no longer the next evidence priority. In the rooted
current-S12 capture, the phone's `0x0058` requests
receive same-sequence `0x0059` responses. Once the first unknown inbound CBC
block has been skipped, captured `0x0059` frames at sequences `0x013c`,
`0x013d`, and `0x013f` through `0x0142` decrypt to empty command plaintext.
The source native `0x0059` route reports the set-device-info ACK event and does
not require an ACK payload.

Those facts produce one deterministic legacy-clear candidate for the fresh
request:

```text
request:   0x0058 seq=0x0003 payload={"isSameAccount":0}
candidate: 0x0059 seq=0x0003 payload=<empty>
frame length: 9
frame SHA-256: 7E597F917619DF09D1F86173EAF953BB0DE9F06575DB919A67217C645FD242B8
```

There is an important firmware-version boundary. In LX06 1.88.51 `mpas`, the
dispatcher reads the command at `0x6580c`; command `0x0058` follows the range
branches at `0x658e8` and `0x65910`, misses the `0x0062` compare at `0x66f60`
and the `0x0400` compare at `0x677b0`, then reaches the default false return at
`0x667e8`. That image therefore cannot statically prove the handler used by the
current 1.94.13 receiver. The 1.94.13 evidence is passive wire/decryption
evidence, while the 1.88.51 evidence is an explicit absence.

`MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner` preserves that
distinction and keeps `CanSendNow=false`,
`ExactFreshClearAcknowledgementObserved=false`, and `SafeForNetworkUse=false`.
No Probe mode sends this candidate. Static source ordering shows the phone has
already queued GetMirrorMode without waiting for this ACK, so the next evidence
priority is passive observation rather than transmitting `0x0059`. Any future
single-frame check would still require new explicit authorization and a
separately reviewed stop condition; nothing here permits heartbeat ACK,
GetMirrorMode ACK, Open, AddMirror, RTSP, playback, media, or audio.
