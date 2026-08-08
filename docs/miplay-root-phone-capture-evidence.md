# MiPlay rooted phone capture evidence

## Root state

- Phone: Xiaomi 13 Pro / `nuwa`, HyperOS `OS3.0.313.0`.
- ADB endpoint used in this session: `192.168.10.20:37165`.
- Root manager package observed: `com.sukisu.ultra` (`SukiSU Ultra v4.1.3`).
- After enabling Shell access in SukiSU, both `su -c id` and
  `/system/bin/su -c id` return `uid=0(root)`.
- `/system/bin/tcpdump` is present and works on `wlan0`.
- `/proc/net/tcp` maps the observed `8899` sockets to UID `10168`, and Android
  package lookup maps UID `10168` to `com.milink.service`.
- The socket-owning process observed through `/proc/975/status` is
  `com.milink.service:audio`. Later `logcat` lines from the same process family
  show `MiPlay CmdControl` command-session logs such as
  `DID8899:CMD_2599` / `DID8899:CMD_1bc2`, but the 12:24 pcap itself remains the
  authoritative source for exact wire order.

## Passive fake-receiver bootstrap capture

Artifact:

- `artifacts/phone_live/2210132C_OS3.0.306.0/sender-captures/passive-sender-20260726-111422.stdout.log`

The test receiver advertised a distinct identity (`DLNACast 真机捕获器`) and sent
only one legacy pre-auth challenge:

- outbound `0x0028`, sequence `0x0000`, payload `123456789`;
- no `0x1401`, no `0x1402`, no business command, no RTSP, no media, no playback,
  and no audio frame.

The official phone sender voluntarily connected from `192.168.10.20:49432` to
`192.168.10.9:8899` and sent:

- `0x0036` sequence `0x0000`, payload `3.1.6030516\0`;
- valid legacy `0x0029` sequence `0x0000`;
- `0x1400` sequence `0x0001`, SafetyInfo offer:
  - `aesIvTypes = 3`
  - `aesKeyTypes = 3`
  - `authAlgorithmTypes = 7`
  - `authKeyTypes = 1`
  - `integrityTypes = 1`

This proves the current phone sender's pre-auth bootstrap without touching a real
speaker.

## Real S12 post-auth heartbeat capture

Artifact:

- `artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-scriptcheck-20260726-120328.pcap`
- `artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-triggered-20260726-121154.pcap`

The capture was produced by rooted phone `tcpdump` on `wlan0`, filtered to the
real S12 control port. No Probe packet was sent during this capture.

Observed flow:

- phone `192.168.10.20:44754` -> S12 `192.168.10.7:8899`
- S12 `192.168.10.7:8899` -> phone `192.168.10.20:44754`

Observed post-auth heartbeat frames:

| Direction | Command | Sequence | Payload length | Payload shape |
|---|---:|---:|---:|---|
| phone -> S12 | `0x001a` | `0x0032` | 25 | SafetyData v1 |
| S12 -> phone | `0x001b` | `0x0032` | 25 | SafetyData v1 |
| phone -> S12 | `0x001a` | `0x0033` | 25 | SafetyData v1 |
| S12 -> phone | `0x001b` | `0x0033` | 25 | SafetyData v1 |

Each payload has:

- SafetyData v1 header length `9`;
- flags `0xe0`;
- padding length `0x10`;
- CRC field present;
- encrypted payload length `16`.

This proves that in a real authenticated phone-to-S12 session, the outer
heartbeat command and sequence are clear, while the command payload is wrapped in
SafetyData v1.

The later triggered window captured only the already-authenticated heartbeat
stream on the same speaker:

- phone `192.168.10.20:43720` -> S12 `192.168.10.7:8899`
- repeated `0x001a -> 0x001b` pairs;
- sequences `0x0065` through `0x006c`;
- each payload is again a 25-byte SafetyData v1 container.

No new-session bootstrap (`0x0036`, `0x0028`, `0x1400`, `0x1402`) appeared in
that triggered window, so the phone likely kept an existing authenticated
control session instead of reconnecting.

## Real phone post-auth command sequence capture

Artifact:

- `artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap`

The 60-second rooted `tcpdump` window captured 64 packets / 43 complete MiPlay
command frames on the already-authenticated control session:

- phone `192.168.10.20:43720` -> S12 `192.168.10.7:8899`
- no TCP SYN/FIN/RST and no pre-auth bootstrap appeared in this pcap;
- no Probe MiPlay frame was sent while collecting it.

Important correction: because this pcap has no TCP/SafetyAuth bootstrap and its
first phone sequence is already `0x013a`, "order 1" below means the first frame
visible in this capture window. It is not the first command after
`DealSafetyDone`. In particular, this pcap cannot justify replaying `0x0058` as
fresh-session sequence `0x0004`.

The important post-auth order is:

| Order | Direction | Command | Sequence | Payload length | Meaning |
|---:|---|---:|---:|---:|---|
| 1 | phone -> S12 | `0x0058` | `0x013a` | 105 | local-device-info/update |
| 2 | phone -> S12 | `0x001e` | `0x013b` | 25 | getDeviceInfo |
| 3 | S12 -> phone | `0x0059` | `0x013a` | 25 | ACK for first `0x0058` |
| 4 | phone -> S12 | `0x0058` | `0x013c` | 41 | local-device-info/update |
| 5 | phone -> S12 | `0x0058` | `0x013d` | 41 | local-device-info/update |
| 6 | S12 -> phone | `0x001f` | `0x013b` | 425 | large getDeviceInfo ACK |
| 7 | phone -> S12 | `0x0034` | `0x013e` | 25 | GetMirrorMode readiness/status command |
| 8 | S12 -> phone | `0x0059` | `0x013c` | 25 | ACK for second `0x0058` |
| 9-12 | phone -> S12 | `0x0058` | `0x013f`-`0x0142` | 41 each | additional local-device-info/update frames |
| 13,15-18 | S12 -> phone | `0x0059` | `0x013d`,`0x013f`-`0x0142` | 25 each | matching delayed `0x0058` ACKs |
| 14 | S12 -> phone | `0x0035` | `0x013e` | 25 | GetMirrorMode_Ack for `0x0034` |
| 19-20 | both | `0x001a`/`0x001b` | `0x0143` | 25 each | heartbeat pair |
| 21 | phone -> S12 | `0x0040` | `0x0144` | 105 | official SetPlaySource frame |
| 22-43 | both | `0x001a`/`0x001b` | `0x0145`-`0x014f` | 25 each | heartbeats continue |

Every captured post-auth command payload is still SafetyData v1 wrapped
(`00 07 01 e0 ...`): the capture proves wire command order, sequence ownership,
payload lengths, and continuation behavior, but not plaintext semantics.

Two conclusions replace the previous weaker ordering model:

1. `0x0058` is not merely a speculative command. The official phone sends it on
   the existing authenticated S12 session, and S12 answers with `0x0059`.
2. `0x0040` appears only after local-device-info, getDeviceInfo, `0x0034/0x0035`,
   and heartbeat context. No `0x0041` was seen in this window, yet the session
   remained alive and continued heartbeat ACKs. Therefore absence of `0x0041`
   is not by itself proof of immediate failure for an official sender.

### Offline continuation decrypt of the same post-auth capture

The same pcap starts after SafetyAuth, so the first captured SafetyData frame in
each direction lacks its initial CBC IV. That does not block later plaintext
recovery: after one frame in a direction is captured, its final ciphertext block
is the CBC IV for that direction's next frame.

Using the observed flow endpoints:

- phone/source: `192.168.10.20:43720`
- speaker/sink: `192.168.10.7:8899`
- type-1 authKey, peer-first order: `a565e5251cce7d9995e34b18bb656c33`
- AES key material type 1: `a565e5251cce7d99`

the continuation decrypt recovers these official plaintexts without sending or
replaying any packet:

| Captured order | Command | Sequence | Plaintext evidence |
|---:|---:|---:|---|
| 1 | `0x0058` | `0x013a` | full JSON reconstructed: missing first block is `{"sourceName":"X`; known suffix completes `{"sourceName":"Xiaomi 13 Pro","mSourceBtMac":"<32-char uppercase MD5>"}` |
| 2 | `0x001e` | `0x013b` | empty payload |
| 4,9,11 | `0x0058` | `0x013c`, `0x013f`, `0x0141` | `{"canAlonePlayCtrl":"1"}` |
| 5,10,12 | `0x0058` | `0x013d`, `0x0140`, `0x0142` | `{"alonePlayCapacity":"1"}` |
| 6 | `0x001f` | `0x013b` | OPack-like device-info map parsed by `MiPlayLegacyDeviceInfoPayloadCodec`; sensitive fields remain redacted in summaries |
| 7 | `0x0034` | `0x013e` | empty payload |
| 14 | `0x0035` | `0x013e` | `00 00 00 00 02` (`valueType=0`, big-endian mirror mode `2`) |
| 21 | `0x0040` | `0x0144` | `{"ref_channel":"controlcenter","ref_function":"single_room","ref_content":"music_qq"}` |

This replaces the earlier static-only SetPlaySource payload guess. The previous
bounded negative test used `{"ref_channel":"playpage","ref_function":"","ref_content":""}`;
the official phone runtime for this capture used
`controlcenter/single_room/music_qq` after local-device-info, getDeviceInfo, and
GetMirrorMode readiness context.

The project now has a deterministic offline continuation-decrypt helper and
tests for these fixture payloads. The first `0x0058` source identity plaintext is
80 bytes; because SafetyData v1 always adds a full zero block for aligned
plaintext, it predicts the captured 105-byte SafetyData container
(`9-byte header + 96-byte ciphertext`). It also has an offline
official-sequence plan that prepares, but does not send, the minimal recovered
order:

`0x0058 sourceName/mSourceBtMac -> 0x001e -> 0x0058 canAlonePlayCtrl -> 0x0058 alonePlayCapacity -> 0x0034 -> 0x0040`

The plan requires same-sequence `0x001f` before sending `0x0034`, then
same-sequence `0x0035` with mirrorMode `2` before sending `0x0040`; it treats
`0x0059` as local-device-info ACK context and forbids Open, AddMirror, RTSP,
media, playback, and audio.

The Probe runner now exposes this only behind a double opt-in:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-native-safety-mutual-auth-official-post-auth-sequence-probe=<S12 IPv4> `
  --miplay-confirm-official-post-auth-sequence
```

Without the confirm flag it will refuse the post-auth sequence after mutual
SafetyAuth.

The prepared sequence can also be inspected without opening any socket:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- `
  --miplay-official-post-auth-sequence-dry-run
```

This dry-run must report the recovered first `0x0058` as an 80-byte plaintext /
105-byte SafetyData container, distinct from the rejected 73-byte default
Windows identity. It is diagnostic only and never marks the sequence
network-safe.

Follow-up live validation result: the explicitly authorized run is recorded in
`docs/miplay-official-post-auth-sequence-live-validation.md`. Mutual SafetyAuth
completed, but the S12 closed immediately after the first default Windows
identity `0x0058 sourceName/mSourceBtMac` frame. No `0x001e`, `0x0034`, `0x0040`,
Open, AddMirror, RTSP, media, playback, audio, retry, or fallback was sent. That
result localizes the next gap to exact source identity / first 0x0058 context
before retrying later commands. The official-sequence plan has since been
changed to prepare the recovered official phone source identity by default; this
is still only a bounded candidate and not replay permission.

Recovered-identity live validation update: a later authorized run sent the
recovered official first `0x0058` source identity (`80` bytes plaintext, `105`
bytes SafetyData) to `192.168.10.4`. The S12 still closed without `0x0059`; no
`0x001e`, `0x0034`, `0x0040`, Open, AddMirror, RTSP, media, playback, audio,
retry, or fallback was sent. This rules out source identity / first-frame length
as the primary remaining gap and moves the next offline target back to
post-auth SafetyData command-session state: cipher phase, IV/session fork, or
missing native listener/session context after `DealSafetyDone`.

## Current `com.milink.service:audio` native sender evidence

The owner of the live 8899 sockets is the updated system package
`com.milink.service`:

- versionName `17.2.4.1.2606161948`, versionCode `170020401`;
- pulled APK:
  `artifacts/phone_live/2210132C_OS3.0.313.0/packages/com.milink.service_17.2.4.1.2606161948/base.apk`;
- APK SHA-256:
  `ABE48100CD90EF872ABD40C8B5CAFA34F3561E8A7871865BF60CA93D2DFB1C4E`;
- native library:
  `.../extracted/lib/arm64-v8a/libaudiomirror-jni.so`;
- native library SHA-256:
  `DADB024547BAE1B210BD99E4C4AC00AD9595A8924E438EBD1730AAD7DF200DDF`.

Focused ARM64 static evidence from this current library:

- `CmdSource::sendCmdPayload` at `0x17b858` checks `CmdSource+0x3c0`; when the
  SafetyData wrapper exists, it calls the wrapper virtual transform before
  `getCmdData` builds the clear outer `$` command/sequence/length header.
- `CmdSource::dealSafetyInfoAck` at `0x17c5f0` derives auth/AES material, calls
  `SafetyDataDeal(true, integrityType, aesKey, aesIv)` at `0x17cfcc`, then
  stores the wrapper at `CmdSource+0x3c0`.
- `SafetyDataDeal` constructs separate AES-CBC contexts at `this+0x40` and
  `this+0x100`, used by `encryptData` (`0x26a084`) and `decryptData`
  (`0x26a350`). This confirms distinct SafetyData session state after
  SafetyInfo, not a direct reuse of the SafetyAuth codec as a single stream.
- SafetyData integrity is a native big-endian header value, not a little-endian
  CRC field. In `encryptData`, the integrityType=1 branch calls
  `av_crc_miplay(init=-1)` at `0x26a220..0x26a23c`, then the recovered write
  sequence at `0x26a270..0x26a298` stores `crc>>24`, `crc>>16`, `crc>>8`, then
  `crc`; in `decryptData`, `0x26a468..0x26a474` loads the four header bytes and
  applies `rev` before the compare at `0x26a548`. The observed S12 sample stores
  `00 EC AE 89`, while the local CRC-32/MPEG-2 accumulator returns
  `89 AE EC 00` for the same ciphertext, so project code now byte-reverses the
  local accumulator before validating or writing the SafetyData header.
- `DealSafetyDone` at `0x17be70` only marks auth done and schedules keepalive /
  reaper work; it does not itself emit `0x0058`, `0x001e`, or `0x0040`.
- `Java_com_xiaomi_miplay_mylibrary_mirror_CmdSessionControl_getMirrorMode`
  (`0x16e270`) reaches `CmdSource::getMirrorMode` (`0x177648`), which sends
  command `0x0034` with null plaintext payload and an incremented command
  sequence.
- `CmdSource::onRecvCmd` (`0x1802bc`) uses a low-command jump table at
  `0x10e67a`. Its `0x0035` entry targets `0x180e08`, where value-type `0`
  ACK payloads are parsed as a big-endian uint32 mirror-mode value before the
  callback dispatch. String evidence in the same library names the pair
  `GetMirrorMode` / `GetMirrorMode_Ack`.

So the previously unknown `0x0034/0x0035` pair in the real S12 capture is now
identified as `GetMirrorMode` / `GetMirrorMode_Ack`.

## Real phone session mapping

Artifact:

- `artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-map-20260726-132653.pcap`

This short passive pcap captured only heartbeat traffic for the same S12 `.10.7`
flow:

- `192.168.10.20:43720 -> 192.168.10.7:8899`
- `0x001a/0x001b` heartbeat pairs with sequences `1082`, `1083`, and `1084`.

Read-only Android process inspection and package lookup show:

- `/proc/net/tcp` owner UID: `10168`;
- package for UID `10168`: `com.milink.service`;
- socket-owning process: PID `975`, `com.milink.service:audio`.

The immediately following logcat heartbeat chain from the same process shows:

- `DID8899:CMD_1bc2` sends/receives heartbeat sequence `1086`;
- `DID8899:CMD_2599` simultaneously sends/receives heartbeat sequence `1103`.

Therefore the captured `.10.7` flow aligns with `DID8899:CMD_1bc2`. A second
S12 command-session chain (`DID8899:CMD_2599`) is present in the same process and
matches the observed fact that the phone has two established `8899` sockets, but
this note does not rely on assigning that second chain to a specific speaker.

## Offline pcap decoder

`MiPlayTcpdumpPcapDecoder` now parses classic tcpdump pcap files with
Ethernet/IPv4/TCP packets and extracts non-empty TCP payloads into MiPlay command
frame summaries. It is intentionally offline-only and does not perform TCP
reassembly, SafetyData decryption, packet replay, or network operations.

## Capture script

Local helper:

- `scripts/run-phone-root-miplay-capture.ps1`

It pushes a temporary shell script to `/data/local/tmp`, runs rooted `tcpdump` on
`wlan0`, interrupts it after the requested duration, pulls the pcap back to the
workspace, then removes the temporary remote script. The script only captures; it
does not send MiPlay, RTSP, media, playback, or audio data.

## Fresh legacy-clear compatibility capture

A later explicitly authorized capture used a distinct non-Lyra test receiver,
not an S12 identity. Artifact:

- `artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.stdout.log`
- phone logcat:
  `artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.milink-logcat.txt`

Read-only ADB inspection verified root as `uid=0(root)` with
`u:r:magisk:s0`, and identified this older source stack as
`com.milink.service` version `12.4.8.13`. The device had no tcpdump binary, so
no executable was pushed to it. The receiver-owned TCP stream already provided
the exact frames.

The phone connected from `192.168.10.58:50516` to
`192.168.10.9:8899`. After the receiver sent only legacy
`0x0028(seq 0, "123456789")`, the phone sent the following clear sequence:

| Order | Command | Sequence | Payload | SHA-256 |
|---:|---:|---:|---|---|
| 1 | `0x0036` | `0` | `1.0.1123012\0` (12 bytes) | `558EBE495951AD7B8929C4E3AFE9D58926D8E963961374A12A3BB5EEBC1646B0` |
| 2 | `0x0029` | `0` | valid legacy acknowledgement (40 bytes) | `AF8BF73F0315FD5BE81E05980E8AEFC266CCD56521E451DD8BAC45BC03F5B517` |
| 3 | `0x001e` | `1` | empty | `203B2D81F6878C606F65693571D9EE10DDA64C08ADE9EDF29D649EB17E482B03` |
| 4 | `0x0058` | `2` | `{"sourceName":"MI PAD 4\\/Plus"}` (31 bytes) | `1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113` |
| 5 | `0x001a` | `3` | empty | `2FACCB98E2B34F7E7EB1086874B8592125DF0561BF928D960DC9FDA8B066594E` |
| 6 | `0x001a` | `4` | empty | `79722E27F8439222D60815BC2B8ABC97E87570AE8A663CBD9C7C5A7A45035BD6` |
| 7 | `0x001a` | `5` | empty | `413FA7738258FD71FA337D49746B4D36FF410332CDF7DF8DC1EA52C936EC171D` |

No SafetyInfo, SafetyAuth, or SafetyData frame appeared. The receiver sent no
business response and the phone closed after the unanswered heartbeats. The
capture therefore proves a fresh legacy-clear compatibility branch and the
exact source-name JSON, but it does not prove or authorize receiver `0x0037`,
`0x001f`, `0x0059`, or `0x001b` replies.
