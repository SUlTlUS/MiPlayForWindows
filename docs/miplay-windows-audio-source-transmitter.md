# Windows MiPlay audio-source transmitter

The product goal is Windows source -> S12 receiver: DLNACast should behave like
the phone audio transmitter. The temporary receiver at `192.168.10.9` remains
only a protocol instrument; it is not the product feature.

## Key route correction: basic source does not require modern SafetyAuth

A passive rooted-device syscall capture on 2026-08-07 proved that
`com.milink.service 12.4.8.13` opens normal TCP `8899` connections to two real
LX06/S12 receivers and completes the basic source bootstrap entirely in clear
legacy `$` frames. It sends no `0x1400..0x1403` and no SafetyData.

This is not an inference from the earlier fake receiver. Both real receivers
returned device info, local-device-info acknowledgements, mirror mode, volume,
state, media-info notifications, and long-running heartbeat acknowledgements.
The old/basic branch is therefore the shortest supported route to a Windows
source. The modern SafetyAuth/SafetyData branch remains a separate compatibility
target and is no longer a blocker for basic transmitter work.

The hash-pinned, redacted evidence and full timeline are in
`docs/miplay-real-legacy-source-fresh-session.md`.

## Implemented offline source controller

`MiPlayLegacyAudioSourceSession` reconstructs the real legacy-clear prefix. It
owns no socket and every transition remains `SafeForNetworkUse=false`:

```text
receiver -> 0x0028 seq=receiver-selected, 16 or 17 ASCII digits
source   -> write #1: 0x0036 seq=0 version "1.0.1123012\0"
                         + 0x0029 same receiver sequence, full-payload HMAC
source   -> write #2: empty 0x001e seq=1

receiver -> 0x0037 seq=0, parseable native control-session version
source   -> 0x0058 seq=2, exact sourceName-only JSON

receiver -> parseable 0x001f seq=1 and empty 0x0059 seq=2, either order
source   -> 0x0058 seq=3, {"isSameAccount":0}
source   -> empty 0x0034 seq=4

receiver -> empty 0x0059 seq=3
source   -> empty status queries beginning at seq=5
            receiver-B order: 0x000e, 0x0014, 0x001c
            receiver-A order: 0x000e, 0x001c, 0x0014

receiver -> 0x0035 seq=4 scalar mode=2
receiver -> 0x000f same-sequence scalar volume
receiver -> 0x001d same-sequence scalar state
receiver -> mode-2 0x0022 media-info notification
STOP before 0x0040/Open/AddMirror/RTSP/playback/media/audio
```

The controller accepts the two observed status-query orders explicitly because
their relative order differed between the two simultaneous real sessions. It
does not treat that order as a protocol gate. A five-byte scalar codec now pins
the observed payload shape as tag `0` plus unsigned big-endian 32-bit value.

The target receiver order reproduces these offline golden frames:

- `0x0036` source version frame SHA-256
  `558EBE495951AD7B8929C4E3AFE9D58926D8E963961374A12A3BB5EEBC1646B0`;
- `0x001e` sequence-1 frame SHA-256
  `203B2D81F6878C606F65693571D9EE10DDA64C08ADE9EDF29D649EB17E482B03`;
- `0x0058` sequence-2 sourceName frame SHA-256
  `1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113`;
- `0x0058` sequence-3 `isSameAccount=0` frame SHA-256
  `DB75703B2F77B6BA8A63D0611104DA6DE1266A144B00D985B905B28CC9A23FC6`;
- `0x0034` sequence-4 frame SHA-256
  `DDDAFA73414A3B71D7DF04B90FDC20BDDDAE735F852C1125E9BB576223032FD4`.

## Offline capture decoder

`MiPlayStraceNetworkCaptureDecoder` reconstructs `strace -xx` TCP calls,
including split `<unfinished ...>` / `<... resumed>` pairs keyed by thread id.
It groups streams by endpoint and direction, feeds their bytes through the
existing command decoder, and returns only lengths, hashes, directions, and
frame metadata. The returned model contains no raw payloads and defaults to no
payload prefix.

The Probe exposes an early-exit, offline-only entrypoint:

```powershell
dotnet run --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj --no-build -- `
  --miplay-scan-strace=.\artifacts\phone_live\fresh-source-captures\mipad4-miplay-source-20260807-131152.strace
```

For the pinned artifact it reports `80` TCP chunks, `92` command frames,
`0` issues, and `containsRawPayloads=False`; it opens no socket.

## Separate modern branch

`MiPlayAudioSourceControlSession` remains the offline controller for the modern
SafetyData path recovered from a later phone stack. It consumes decrypted
plaintext and emits plaintext steps only. That branch still lacks a verified
fresh first-command cipher transition and must not borrow IV/session state from
legacy, SafetyInfo, or inbound SafetyData.

The modern sequence and the legacy-clear sequence are not interchangeable:

- legacy clear: `0x0028/0x0029`, then plain `$` business/status frames;
- modern: SafetyInfo/SafetyAuth, directional SafetyData, then encrypted command
  payloads;
- media: Open/AddMirror/RTSP/RTP/audio is a later third boundary.

## Next bounded live validation

The Probe now has a default-off, fail-closed legacy-clear Windows-source
bootstrap entrypoint. Its separate dry-run opens no socket and prints the exact
eight-write/nine-frame ledger:

```powershell
dotnet run --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- `
  --miplay-legacy-audio-source-bootstrap-dry-run
```

The active form is not implied by the dry-run and requires both one exact IPv4
target and an explicit confirmation flag. It first prints the ledger, connects,
then sends nothing until the receiver supplies a valid 16- or 17-digit
`0x0028`. Every write is decoded again by
`MiPlayLegacyAudioSourceBootstrapProbeGuard`; the first write must contain only
the captured coalesced `0x0036` plus lowercase 40-hex-byte HMAC-SHA1 `0x0029`,
and the remaining seven writes must each contain exactly one expected frame.
The guard cannot authorize a tenth frame or ninth write.

The active syntax is intentionally recorded for a separately authorized run,
not executed as part of offline preparation:

```powershell
dotnet run --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- `
  --miplay-legacy-audio-source-bootstrap=<speaker-ipv4> `
  --miplay-confirm-legacy-audio-source-bootstrap
```

Use `--miplay-legacy-status-order=volume-state-media` only when reproducing the
alternate captured order; the default is `volume-media-state`.

The maximum permitted candidate would stop after the three empty status
queries and only observe their replies. It must send no `0x0040`, `0x0041`,
Open (`0x0000`), AddMirror (`0x002e`), RTSP, RTP, playback, media, or audio.
Any unexpected modern frame, wrong sequence, malformed acknowledgement, socket
close, or unrecognized command stops the run without fallback.

Passing that bounded bootstrap would prove that Windows can occupy the same
basic source role as the old phone service. It would still not prove audio:
the next missing evidence would be a real phone capture where playback is
actually started, exposing the first `0x0040`/Open/AddMirror/RTSP transition.

## 2026-08-07 playback capture closes the missing transition

The paragraph above records the earlier boundary and is now superseded by a
second rooted-phone capture in which the user selected the LX06 MiPlay route
and started playback:

- artifact:
  `artifacts/phone_live/fresh-source-captures/mipad4-miplay-full-switch-playback-20260807-141132.strace`;
- SHA-256:
  `499252CB2EFE79EE443526BD58C9AED13EEFAED366F3CE2FDE3D4885454FD8E3`;
- offline decode: `112` TCP chunks, `114` command frames, `0` issues, no raw
  command payload retention.

On the selected receiver the official source used this playback-time control
continuation after the already-proven sequence-0-through-7 bootstrap:

```text
seq  8  0x0058 sourceName refresh
seq  9  0x001e getDeviceInfo
        wait for 0x0059 seq 8 and parseable 0x001f seq 9
seq 10  0x0058 {"isSameAccount":0}; wait for 0x0059
seq 11  0x0034 getMirrorMode; require 0x0035 mode 2
seq 12  0x001a heartbeat; require 0x001b
seq 13  0x0040 {"ref_channel":"controlcenter","ref_function":"single_room",
                 "ref_content":"music_wangyiyun"}
seq 14  0x0000 wfd://source-ip:7274?mirrorMode=1\0
```

There was no `0x0041`, Open ACK, or `0x002e AddMirror`. `0x0040` was broadcast
to both known receivers; Open was sent only to the selected receiver. The
receiver then opened three reverse TCP connections to source port `7274`:
RTSP control, an unused second channel, and the audio channel. The source also
served a UDP clock responder on port `36524`.

`MiPlayLegacyPlaybackControlSession` now reproduces sequences 8 through 14 as
a pure state machine. Open stays gated until a TCP listener, UDP timer
responder, capacity for three reverse connections, and an AAC/MPEG-TS pipeline
are all ready. The state machine cannot emit AddMirror.

`MiPlayWfdSourceRtspSession` reproduces the captured 16-message initial RTSP
exchange and reaches `Ready` only after the receiver acknowledges CSeq 5
`TIME_OFFSET`. It accepts the initial two OPTIONS messages in either arrival
order but requires the captured AAC-only, no-video,
`RTP/AVP/TCP;interleaved` capability profile.

The audio wire path is now implemented offline:

- encoder ADTS chunks -> `MiPlayAdtsStreamParser`;
- normalize to MPEG-2 AAC-LC, 48 kHz, stereo;
- one access unit -> PES/MPEG-TS through `MiPlayMpegTsAudioMuxer`;
- one TS payload -> RTP payload type 33, marker set, SSRC `DEADBEEF`;
- RTP -> `$` plus 24-bit big-endian length through
  `MiPlayWfdInterleavedFrameCodec`.

The packetizer advances RTP time by `1920` ticks per 1024-sample AAC access
unit and reproduces the observed program-table refresh positions 0, 10, 15,
20, ... . A first table-bearing packet is 1332 wire bytes for the captured
682-byte AAC payload; a steady four-TS-packet frame is 768 bytes.

The remaining implementation boundary is runtime orchestration and a bounded
silence-only validation. No runtime sender is considered ready merely because
the codecs and state machines pass offline tests.

### 2026-08-07 target availability result

One explicitly authorized attempt targeted `192.168.10.9:8899`. Windows
returned socket error `10061` (connection actively refused) before TCP setup
completed. The Probe therefore sent `0` writes and `0` MiPlay frames, made no
retry, performed no discovery, and did not try another address. This is only a
target/service availability result; it neither validates nor rejects the
reconstructed legacy bootstrap.

## 2026-08-07 Windows silence transmitter accepted by LX06 1.94.13

A later explicitly authorized, single-target run against `192.168.10.4`
completed the full legacy source path from Windows. The source address was
`192.168.10.9`; there was no retry, fallback, discovery, second target, phone
UI automation, AddMirror, Pause/Resume, or user audio.

The receiver supplied legacy challenge sequence `0x03bc`. It accepted all nine
bootstrap frames and the seven playback-continuation frames through sequence
14. The sequence-9 `0x001f` payload was 415 bytes. Sequence 13 was the captured
control-center `0x0040` JSON and sequence 14 was
`wfd://192.168.10.9:7274?mirrorMode=1\0`. Their complete frame SHA-256 values
are respectively:

- `5450DE56ADCD4946052E35F9897A5F2258FA5A943F61DFA008A2666F09275F93`;
- `5B89F6951449BC45CEE745D669050CF30FB290C1583E1EA42061578299A3B851`.

The receiver then opened three reverse TCP connections from source ports
`50256`, `50260`, and `50262` to the Windows listener on `7274`. The first
completed the reconstructed RTSP negotiation through `TIME_OFFSET` and Ready;
the third carried media. The UDP timer responder on Windows port `36524`
received the receiver's first packet from source port `34994`.

The bounded media ledger completed 48 generated silent AAC access units:

```text
9 table-bearing packets * 768 bytes =  6912 bytes
39 steady packets       * 204 bytes =  7956 bytes
                                  total 14868 bytes
48 * 1024 / 48000                  = 1024 ms
```

Program tables were emitted at access-unit indexes 0, 10, 15, 20, 25, 30, 35,
40, and 45. This explains why the result is smaller than a projection based on
captured 682-byte music access units: the generated silent AAC access unit is
only 13 bytes.

`MiPlayLegacySilencePlaybackLiveValidationEvidence` pins the endpoints,
accepted control ledger, control-frame hashes, reverse ports, RTSP/timer
outcome, and exactly reconstructs the 14,868-byte media ledger offline.

This is direct real-device proof that current LX06 firmware accepts Windows as
a legacy MiPlay/WFD audio source and accepts its media transport. It is not yet
proof of audible Windows audio because silence was deliberately used. The next
boundary is a deterministic PCM 44.1 kHz stereo -> AAC-LC 48 kHz streaming
encoder feeding the already-validated ADTS/MPEG-TS/RTP packetizer.

## 2026-08-07 non-silent and Windows system-loopback validations

The next two explicitly authorized single-target runs closed the encoder and
capture boundaries without expanding the command sequence.

First, a deterministic 440 Hz PCM source passed through the local FFmpeg AAC
encoder and the real receiver path. The receiver accepted 96 non-silent AAC
access units totaling 84,632 wire bytes over 2,048 ms. This proved that the
runtime encoder output, rather than only the pre-generated silent access unit,
fits the reverse media session.

The first system-loopback-only smoke exposed that 256 kbit/s FFmpeg VBR can
occasionally produce an AAC access unit too large for the then-assumed one-RTP,
seven-TS-packet boundary. A simple tone passed at 192 kbit/s, but a later real
music run proved that this was not sufficient: one table-refresh access unit
was 801 bytes where the table-bearing boundary is 720. The same five-second
PCM sample still overflowed once at 160 kbit/s (751 bytes), while 128 kbit/s
produced 236 access units with zero table or steady-state overflow. This led to
a temporary AAC-LC 48 kHz stereo / 128 kbit/s profile. The clean phone capture
and successful validation below supersede that limit: official senders split a
large AAC access unit across multiple RTP packets, so the current runtime uses
Media Foundation AAC at 256 kbit/s with same-timestamp fragmentation.

Finally, Windows injected a low-amplitude test tone into its current default
multimedia output, captured that exact output through WASAPI loopback, encoded
it through the then-current 192 kbit/s FFmpeg profile, and sent it over the already-proven
MiPlay/WFD session to `192.168.10.4`:

- default endpoint: `扬声器 (Realtek(R) Audio)`;
- receiver challenge sequence: `0x03ea`;
- initial capture buffer: 40 ms;
- reverse receiver TCP source ports: `50306`, `50310`, `50312`;
- receiver timer source port: `50639`;
- media: 240 AAC access units, 178,868 wire bytes, 5,120 ms;
- capture health: 3 old-frame overruns, 0 underruns;
- receiver kept RTSP alive and issued requests through CSeq 10;
- no AddMirror, Pause/Resume, discovery, retry, fallback, alternate target, or
  second session.

`MiPlayLegacySystemAudioLiveValidationEvidence` pins the redacted result. This
is protocol-level proof that the complete default Windows output -> WASAPI ->
FFmpeg -> ADTS/MPEG-TS/RTP path wrote bytes to the LX06 reverse socket. It did
not yet prove decoder acceptance or physical audibility; the later human
observation below explicitly showed no sound.

## 2026-08-07 no-audio result and media-clock correction

The longer explicitly authorized run to `192.168.10.4` kept the control,
reverse RTSP, UDP timer, and media TCP channels alive for about 20 seconds. It
wrote 938 AAC access units / 550,432 bytes and received same-sequence heartbeat
ACKs at 5, 10, 15, and 20 seconds. The user nevertheless reported that the
speaker produced no sound. Therefore those socket results prove byte delivery
only; they must not be described as audio decoder acceptance or usable MiPlay.

Offline reconstruction of the official rooted-phone media stream then found
the first concrete byte-level clock difference:

- the official CSeq 5 `TIME_OFFSET` value is `9,633,364,443` microseconds;
- its first RTP sequence, RTP timestamp, and PES PTS are all zero;
- the first MPEG-TS PCR is `866,913,276` at 90 kHz, which maps to
  `9,632,369,733.3` microseconds;
- the PCR is therefore about `994,709.7` microseconds before `TIME_OFFSET`,
  matching the protocol's one-second non-5-GHz playback-delay class within
  one AAC access-unit interval;
- the Windows runtime had incorrectly constructed `MiPlayWfdAudioPacketizer`
  with its default initial PCR of zero.

The official legacy media is not an encrypted-AAC boundary. The first 30
official PES payloads were reassembled offline into ADTS access units (first
sizes 689, 690, 690, 689, and 690 bytes). FFmpeg decoded all 30 as AAC-LC,
48 kHz, stereo, about 258 kbit/s. This rules out adding the unrelated Lyra
`streamKey` / `streamIV` access-unit cipher to this legacy-clear session.

`MiPlayWfdMediaClock` now derives the initial PCR from the exact monotonic
value placed in `TIME_OFFSET` minus 1,000,000 microseconds. The RTSP session
retains that value and returns it to the media runtime; RTP timestamps and PES
PTS remain zero-based as in the phone trace. A second wire difference was also
removed: the official source observes repeated receiver `VIDEO_LATENCY`
telemetry without replying, while the former Windows implementation sent an
extra RTSP 200 for each request.

The corrected-clock live run used `TIME_OFFSET=1,408,359,153,701 us`, derived
initial PCR `6,493,149,545` after the 33-bit wrap, and sent 938 system-loopback
AAC frames / 546,108 bytes over 20,010.7 ms. RTSP, timer, media, and four
same-sequence heartbeat acknowledgements remained healthy, but the user again
reported no sound. Therefore clock anchoring was a real wire correction but
was not the remaining audible-output gate.

## 2026-08-07 recovered post-Open playback-state gate

Re-reading the same official rooted-phone control stream exposed the first
direct state-machine difference after Open:

1. Open sequence `0x00bc`;
2. empty `Pause` (`0x0004`) sequence `0x00bd`;
3. `Cmd_SetMediaInfo` (`0x0012`) sequence `0x00be` with a 180-byte JSON
   payload;
4. receiver notification `first-audiopcm=1` after media begins;
5. empty heartbeat `0x001a` sequence `0x00bf`, followed by `0x001b`;
6. one `Resume` (`0x0006`) sequence `0x00c0`, immediately followed by receiver
   notification `state=2`.

The former Windows runtime sent none of Pause, SetMediaInfo, or Resume and
started periodic heartbeat sequence 15 directly. Its receiver notifications
were not decoded, so it could deliver and decode media while leaving the
receiver in state 0. LX06 1.88.51 `mpas` independently confirms a main dispatch
case for command `0x0012` and an immediate same-sequence `0x0013` ACK before
JSON caching. The real 1.94.13 stream does not justify requiring that optional
ACK as the Resume gate.

`MiPlaySetMediaInfoPayloadCodec` now reproduces the official field names/order
with truthful `Windows / System Audio` metadata. `MiPlayLegacyPostOpenPlaybackSession`
reserves sequences 15-18 for exactly one Pause, one SetMediaInfo, a startup
heartbeat gated by `first-audiopcm=1`, and exactly one Resume gated by the
heartbeat ACK. It requires receiver `state=2` before steady-state heartbeats
begin at sequence 19. An unsupported `0x0022` read-only notification is now
ignored and counted without a reply or state change; an unexpected business
command still stops the bounded run. AddMirror and repeated Resume remain
forbidden.

An offline five-second real-output encoder comparison also reproduced the
official size class with Media Foundation AAC at 256 kbit/s (235 access units,
average 690.4 bytes), but three table-refresh units exceeded the current
single-RTP table-bearing limit. The already used 128 kbit/s profile had no
overflow, and bitrate is deliberately unchanged for the state-machine test.
The Probe builds with zero warnings/errors and the full deterministic suite
passes 563/563 after the live-evidence snapshot was added. No network operation was performed while
recovering or implementing the post-Open state.

### Single-target live result

The subsequently authorized single session to `192.168.10.4` verified the new
gate on LX06 1.94.13. After Open, Windows sent only Pause sequence 15 and the
178-byte Windows SetMediaInfo sequence 16 before starting media. The receiver
reported `mode=1`, repeated `state=0`, then `first-audiopcm=1`. One second
later it acknowledged startup heartbeat sequence 17, echoed parsed
`mediaInfoEx` fields (`Windows`, `System Audio`, duration `20011`, device state
`3`), and reported `state=3`. Windows then sent exactly one Resume sequence 18;
the receiver immediately reported `state=2`.

The session completed 938 system-loopback AAC writes / 549,492 bytes over
20,010.7 ms, with valid RTSP/timer traffic and steady-state heartbeat ACKs from
sequence 19. Capture recorded six old-frame overruns and zero underruns. No
AddMirror, repeated Resume, retry, fallback, discovery, alternate target, or
second session was used. This is direct receiver-side proof of AAC-to-PCM
acceptance and entry into the playing state. Human audibility remains a
separate observation and was not recorded by the Probe.

## 2026-08-07 correction from clean already-playing selection capture

The user clarified that the Pause in the earlier rooted-phone trace was a
manual UI action. A new passive `strace -f -tt -T -xx -s 65535 -e
trace=network` capture was therefore taken while music was already playing;
after selecting the bedroom XiaoAI Speaker Pro (whose receiver connection came
from `192.168.10.3`), no Pause, Resume, seek, track, or volume control
was touched for ten seconds.

Artifact:

- `artifacts/phone_live/clean-selection-captures/mipad4-clean-playing-selection-20260807-172046.strace`;
- SHA-256
  `71187E8D9B3DB1637D7A70648DA4975106247C81CD9534CF94B97EFB322A081E`;
- decoder result: 97 TCP chunks, 98 MiPlay command frames, one harmless
  end-of-capture unfinished syscall.

The clean automatic sequence is materially different from the contaminated
trace:

1. `0x0040` sequence `0x0097` at `17:21:40.340167`;
2. Open sequence `0x0098` at `40.408527`;
3. PLAY acknowledgement at `40.925689`;
4. SetMediaInfo sequence `0x0099` at `41.060951`, payload length 180,
   `status=0`, `volume=25`, and `mDeviceState=2`;
5. TIME_OFFSET at `41.370554`, first media write at `41.374055`;
6. receiver `first-audiopcm=1` at `42.251715`;
7. receiver `mediaInfoEx` followed by automatic `state=2` at `42.255667`;
8. ordinary heartbeat sequence `0x009a` at `44.285512`.

There is no Pause `0x0004`, no Resume `0x0006`, and no special startup
heartbeat anywhere in this selection. The heartbeat immediately before Open
was sequence `0x0096` at `39.285685`; the next heartbeat is exactly
`4,999.827 ms` later, proving that the normal five-second timer simply
continues across Open.

The previous Pause/heartbeat/Resume startup theory is therefore withdrawn.
`MiPlaySetMediaInfoPayloadCodec` now emits `mDeviceState=2`,
`MiPlayLegacyPostOpenPlaybackSession` waits for receiver
`first-audiopcm=1 + state=2` without sending playback controls, and periodic
heartbeat sequence 16 is anchored to the actual pre-Open heartbeat timestamp.
Build is clean, targeted tests pass 13/13, and the complete deterministic suite
passes 568/568 after adding the clean-capture evidence test. This is an offline
implementation result until a separate bounded Windows-to-LX06 run confirms
the physical light bar and audible output.

## 2026-08-07 audible Windows system-audio validation

Fresh same-day phone logs corrected a stale DHCP assumption that had obscured
the physical tests:

- `192.168.10.3` -> `小爱音箱-7503` -> `次卧的小爱音箱 Pro`;
- `192.168.10.4` -> `小爱音箱-6333` -> `客厅的小爱音箱 Art`.

The earlier Windows runs sent to `.4` while the user was observing the Pro, so
those human “no sound” observations are not valid negative audibility evidence
for either the Pro at `.3` or the actual `.4` receiver. Device ID/friendly name,
not a DHCP address, must be used to bind future UI selections.

The clean rooted-phone capture also corrected the last media assumption. The
official source fragments oversized access units instead of limiting each AAC
access unit to one RTP packet. For example, RTP sequences 13/14 carry one
753-byte ADTS access unit and sequences 82/83 carry one 946-byte access unit;
each pair shares one RTP timestamp and sets the marker only on its final
fragment. Media remains on the third reverse TCP connection. Official RTP
timestamps use microsecond quantization
`floor(index * 21,333 us * 90,000 / 1,000,000)`, and program tables occur at
access-unit indexes 0, 13, 18, 23, ... . The initial burst pacing was likewise
recovered from the clean capture before settling to the 1024/48000 cadence.

The corrected Probe therefore used Media Foundation `aac_mf`, AAC-LC 48 kHz
stereo at 256 kbit/s, same-timestamp RTP fragmentation, the captured table
cadence, and the captured startup pacing. In one explicitly authorized bounded
session from `192.168.10.9` to the Pro at `192.168.10.3` (LX06 1.94.13):

- SafetyAuth challenge sequence: `0x0296`;
- receiver reverse TCP source ports: `39122`, `39126`, `39128`; audio used the
  third connection;
- receiver timer source port: `33822`;
- `TIME_OFFSET=1,415,153,637,704 us`, initial PCR `7,104,653,105`;
- 938 AAC access units became 964 RTP frames (26 extra fragments), totaling
  848,640 wire bytes over 20,010.7 ms;
- capture health: one old-frame overrun, zero underruns; PCM was 80.077% nonzero,
  peak 0.906921, RMS 0.109914 / -19.18 dBFS;
- no AddMirror, Pause, Resume, retry, fallback, alternate target, or second
  session was sent.

The receiver light bar activated and the user explicitly confirmed audible
sound. `MiPlayLegacyProAudibleSystemAudioLiveValidationEvidence` pins the
complete ledger offline. This is the first end-to-end proof that the bounded
Windows default-output -> WASAPI loopback -> AAC -> MPEG-TS/RTP -> LX06 path is
actually usable and audible. It does not yet prove main-application integration,
device-selection UX, indefinite streaming, or reconnect resilience.

After all media bytes had completed, the receiver sent an otherwise unsupported
read-only `0x0022` notification payload. The strict parser previously converted
that telemetry into a failure exit. It now records and ignores unsupported
notifications without sending any reply or changing playback state; unexpected
business commands remain fail-closed.

## Main-application integration boundary

The audible Probe path is now available to the WinUI application as a transport
parallel to the existing DLNA `CastCoordinator`; the DLNA implementation and
fallback behavior were not changed. The app continues to remember a renderer by
stable SSDP UDN and uses the address returned by the current discovery result,
rather than persisting a DHCP address as device identity.

The current MiPlay UI uses the same Windows capture-source boundary as DLNA:

- the user must explicitly select `MiPlay` and start a renderer;
- both `CaptureSelection.SystemMix` and `CaptureSelection.Process` are allowed;
- the selected renderer contributes exactly one current, non-loopback IPv4
  target; there is no discovery fallback, retry, or alternate target;
- `ffmpeg.exe` must expose `aac_mf` and is resolved from `DLNACAST_FFMPEG`, the
  application directory, or `PATH`;
- the validated bootstrap, `0x0040`, Open, SetMediaInfo, RTSP, timer, AAC/WFD,
  and ordinary heartbeat sequence is retained;
- Pause, Resume, AddMirror, DLNA fallback, and target switching remain absent;
- a session is capped at ten minutes and can be stopped earlier by cancellation,
  which closes owned endpoints without inventing an unverified Close command;
- startup socket phases have bounded timeouts, so the disabled Start button
  cannot remain stuck indefinitely when a receiver fails to call back.

The UI makes unsupported controls visibly unavailable while MiPlay is selected:
process capture and the DLNA volume slider are disabled, and transport switching
is disabled for the life of a session. Diagnostics expose capture buffer,
access-unit/RTP counts, wire bytes, overruns, and underruns.

Offline verification currently covers the lifecycle coordinator, request guards,
FFmpeg resolution order, all protocol state machines, WinUI XAML compilation,
and both application and Probe builds. No main-application network operation was
performed during this integration. A future explicitly authorized manual check
should select the Pro by friendly name/UDN (not by memorized IP), select System
Audio, press MiPlay Start once, confirm light-bar and audible output, then press
Stop once and confirm the status returns to Idle without a retry or second target.

### First main-application validation result

One main-application session was then run after explicitly selecting
`小爱音箱-7503 · S12` and MiPlay system audio. A local-only preflight first
measured 99.992% nonzero PCM, peak 1.0, RMS 0.186252 / -14.60 dBFS, so the Start
button was not invoked on silence. The app sent to only that selected renderer;
there was no retry, DLNA fallback, or alternate target.

The receiver reverse path and media socket stayed active. Two UI snapshots
showed 576 access units / 589 RTP packets / 0 underruns, then 1,276 access units
/ 1,318 RTP packets / 0 underruns. The app did not observe the required
`first-audiopcm=1 + state=2` readiness transition, so human audibility was not
claimed for this application-owned run.

This run exposed two application-lifecycle defects rather than a new wire-format
result:

1. the five-second readiness reader completed as a canceled Task, while the
   media loop checked only faulted background Tasks and therefore kept sending;
2. the UI awaited readiness inside its `AsyncRelayCommand`, leaving Stop disabled
   during that startup state.

UI Automation correctly refused to invoke the disabled Stop button. The single
app process started for the validation was then terminated, closing its owned
TCP/UDP sockets and FFmpeg child; no Stop, Close, Pause, Resume, AddMirror, retry,
or second-session protocol command was sent. Offline fixes now convert the
readiness cancellation into a timeout failure, treat any prematurely canceled
background channel as fatal, and let the UI Start command return after ownership
is established so Stop becomes available during Connecting/AwaitingReceiver.
No second main-application network run was performed as part of that fix round.

### Second main-application validation result

After the lifecycle fixes, one controlled application session was run against
the explicitly selected `小爱音箱-7503 · S12`. A new local-only preflight measured
99.627% nonzero PCM, peak 1.0, and RMS -14.92 dBFS. The application Start command
was invoked once at 21:11:49.587. Within 1.2 seconds the UI had returned from the
command handler, displayed `Receiver connected; completing the validated MiPlay
bootstrap...`, and exposed an enabled Stop button. This confirms the UI no longer
locks out cancellation while a MiPlay session is starting.

The session then failed before media startup with the generic framework error
`The operation has timed out.` Diagnostics remained at 0 AAC access units and 0
RTP packets. The application automatically returned to an enabled Start button,
and no MiPlay 8899/7274 connection remained. There was no controlled retry,
alternate target, DLNA fallback, Pause, Resume, AddMirror, or media write in this
run. The debug application process was closed after verifying teardown.

The generic error could originate from several distinct startup waits, so it is
not evidence that a specific receiver callback failed. The runner now gives the
control connect, legacy bootstrap, playback continuation, reverse RTSP,
auxiliary, reverse audio, PLAY/TIME_OFFSET, and post-Open readiness waits unique
timeout messages. It also reports the reverse-callback phase in the UI. These
changes are diagnostic only: they do not add commands, retries, or media behavior.

### Stage-labelled main-application validation result

After the stage-specific timeout build passed its offline tests, one new session
was started against the same selected Pro at 21:17:22.110. Its local-only
preflight measured 99.750% nonzero PCM, peak 0.992157, and RMS -15.53 dBFS. The
receiver accepted enough of the clear legacy prefix to enter
`AwaitingAccountAndMirrorAcknowledgements`, but did not advance that phase within
ten seconds. The run ended with 0 AAC access units and 0 RTP packets, automatically
returned to Start, and left no MiPlay control or reverse-media socket. It was not
retried.

This narrows the failure ahead of `0x0040`, Open, RTSP, SetMediaInfo, and media.
At that phase the sender has already sent source `0x0058` sequence 3 with
`isSameAccount=0` and empty GetMirrorMode `0x0034` sequence 4. Completion requires
the matching account `0x0059`, mirror-mode `0x0035`, and—after account
acknowledgement triggers them—the volume `0x000f`, state `0x001d`, and media-info
response/notification. The audible Probe and application runner instantiate the
same `MiPlayLegacyAudioSourceSession`, but this run exposed a runner configuration
difference that the shared state machine had hidden. The runner now records all
eight acknowledgement/query flags, allowing a single session to identify the
exact missing receiver response without a packet or payload guess.

The next flag-labelled observation completed every gate except media info:
`deviceInfo=1, sourceName=1, account=1, mirror=1, queries=1, volume=1, state=1,
mediaInfo=0`. The hash-pinned rooted-phone strace for this same Pro endpoint
shows the official source sending `GetVolume` sequence 5, `GetState` sequence 6,
then `GetMediaInfo` sequence 7. The application runner had instead hard-coded
the other captured receiver's `VolumeMediaInfoState` order. The runner's
validated Pro profile now uses `VolumeStateMediaInfo`. This changes only the
order of the two empty read-only status queries; it adds no command, fallback,
retry, Open, RTSP, or media behavior.

### Controlled no-click replay and media-info gate correction

The next application run removed UI interference completely: the debug window
was placed off-screen and UI Automation verified the exact selected renderer
`小爱音箱-7503 · S12`, the MiPlay transport, and System Mix before invoking Start
exactly once at `2026-08-07T21:45:50.7338711+08:00`. No retry or second target
was used. The final persisted application log was:

```text
2026-08-07T21:45:50.8317514+08:00 [INFO] 开始投送到 小爱音箱-7503，音源 扬声器 (Realtek(R) Audio)
2026-08-07T21:46:01.1124569+08:00 [ERROR] MiPlay 会话失败：The receiver did not advance the legacy bootstrap within ten seconds. phase=AwaitingAccountAndMirrorAcknowledgements; deviceInfo=1, sourceName=1, account=1, mirror=1, queries=1, volume=1, state=1, mediaInfo=0.
```

The run produced zero AAC access units and zero RTP frames and closed before
`0x0040`, Open, RTSP, or media. It proves the repeated `mediaInfo=0` result was
not caused by manual clicks or SSDP status overwrite.

That result invalidated the old readiness model. `0x0014` has a normal
same-sequence `0x0015` response only outside mirror mode 2. In mode 2 the
receiver emits an asynchronous `0x0022`: its sequence is receiver-owned and its
payload contains mutable playback state. A 158-byte body with SHA-256
`871BA314...15014` is therefore a useful capture fingerprint, not a protocol
acknowledgement identity.

Targeted `dexdump` of the official phone source gives the independent static
boundary. In `MiLinkOS3Cn/classes3.dex`, `MiplaySessionCallbackManage.handleDevice`
at code item `0x2b227c` calls `setDevice` at `0x2b251e`, then `getMirrorMode` at
`0x2b2528`, and marks device info present at `0x2b252e`. Its
`mirrorModeNotify` code item at `0x2b2538` updates mirror mode and returns; it
does not wait for GetMediaInfo, volume, or state. The public GetMediaInfo and
GetVolume operations appear as independent handler branches at `0x2767ce` and
`0x276838`. This matches the wire fact that the phone sends the three queries
without waiting between them and that the media notification is not correlated
by request sequence.

The application bootstrap now completes after the account and mirror-mode
acknowledgements plus the same-sequence volume and state scalar responses.
GetMediaInfo is still sent in the captured Pro order and a matching media-info
notification is still recorded when it arrives, but absence of that
asynchronous observation no longer blocks the already verified playback
continuation. No playback-frame shape, target, retry policy, or media path was
changed by this correction.

### Successful main-application transport validation

The next controlled run reached every application-owned transport gate but
initially exposed a narrower decoder bug. `SetMediaInfo` was sent at
`22:25:52.718`; Media Foundation produced and Windows wrote the first AAC/RTP
batch at `22:25:56.921`. The receiver then sent a stable 49-byte `0x0022`
notification with SHA-256
`4A1F05659BC922581465FE95C026C7A624863D42FBA2A545BC003C5DF28F33CE`,
followed by `mediaInfoEx` and `state=2`. Its exact OPack-like body is two
consecutive scalar fields: `first-audiopcm=1` and
`first-audiopcm-buffer-time=0`. The former decoder stopped after the first
17 bytes and rejected the valid trailing uint32 field. This made a confirmed
receiver PCM transition look like a timeout.

`MiPlayNotifyPayloadCodec` now accepts trailing scalar fields and uint32 type
`0x06`. The readiness timeout is also anchored to the first completed media
write rather than `SetMediaInfo`, so the approximately 4.2-second `aac_mf`
startup latency no longer consumes the five-second receiver window. Both
changes are local receive/timing fixes and add no outbound command.

The final off-screen WinUI run selected exactly `小爱音箱-7503 · S12` and
started at `2026-08-07T22:28:22.8074493+08:00`. Deterministic evidence:

- ten-minute `SetMediaInfo`: payload length 179, SHA-256
  `83A6859C90535005160C904B8D23126ACB6C586A652429615D166E61A052BB0E`;
- first media batch: 721-byte AAC access unit, two RTP frames, 1,536 wire
  bytes;
- receiver compound `first-audiopcm=1`, buffer time 0, then `mediaInfoEx` and
  `state=2`; unsupported-notification count remained zero;
- the application entered Streaming, continued for approximately 12.5
  seconds, then its enabled Stop action canceled the owned session;
- no Close, Pause, Resume, AddMirror, retry, fallback, alternate target, or
  second application session was sent by the successful run;
- the debug process and all owned sockets closed; only normal TCP TIME_WAIT
  entries remained.

This proves the main application, not only the Probe, can complete the legacy
MiPlay bootstrap, WFD reverse connections, AAC/RTP delivery, receiver PCM
confirmation, and playing-state transition against the bedroom LX06 at
`192.168.10.3`. Human audibility was not explicitly observed during this
off-screen run, so it remains separate from the confirmed transport result.
`MiPlayMainApplicationLiveValidationEvidence` pins the result offline.

## Active-session receiver volume backend

The backend now exposes receiver volume without changing the WinUI surface:

- `MiPlaySystemAudioTransmitter.ReceiverVolume` reports the last validated
  receiver value;
- `ReceiverVolumeChanged` publishes the initial `GetVolume` result and each
  acknowledged runtime change;
- `SetReceiverVolumeAsync(0..100)` is accepted only while the transmitter is
  in `Streaming` state;
- `MiPlayLegacySystemAudioSessionRunner` queues volume requests onto its owned
  TCP 8899 connection. It does not create a second control connection;
- runtime volume and heartbeat writes share one sequence allocator and one
  reader. Inserting a volume command therefore advances the following
  heartbeat sequence instead of reusing a captured fixed sequence.

The wire shape is backed by two independent static paths:

1. In the OS3 phone `MirrorOS3/libmirror-jni.so`, the `CmdSource` volume path
   at `0xA8AB4` applies `rev` to the requested integer, stores four bytes,
   loads command `0x000C` at `0xA8AC4`, loads length `4` at `0xA8AC8`, and
   sends through the normal command-payload function. The adjacent command
   name table maps `0x000C/0x000D` to `SetVolume/SetVolume_Ack`.
2. LX06 1.88.51 `usr/bin/mpas` logs `Cmd_SetVolume`, compares the inbound
   payload length with `4` at `0x686BC`, and sends command `13` at `0x687F0`
   using the inbound sequence loaded at `0x687DC`.

Accordingly, `SetVolume` uses a raw four-byte unsigned big-endian payload
(`24 -> 00 00 00 18`). It must not reuse the tagged five-byte scalar used by
`GetVolume_Ack 0x000F` (`00 || uint32be`). The implementation requires the
`0x000D` response to match command, sequence, and the four-byte value before
updating public state.

This section's Windows implementation remains protocol/static and offline-test
evidence. The receiver-side disassembly is from LX06 1.88.51; it is not by
itself a claim that the current 1.94.13 receiver has accepted the new Windows
runtime volume path.

A fresh official-phone logcat capture on 2026-08-07 independently confirmed
the runtime semantics against `小爱音箱Pro`. Two user slider gestures produced
many UI intermediate values, while the service submitted the coalesced values
`38`, `44`, `45`, `56`, and `58` through
`CmdSessionControl_setVolume`. Each submitted value produced the same-value
`onVolumeNotify` and cache update; the two gesture endpoints were `44` and
`58`. Initial cached values were `21` for the Pro and `24` for the Art. This
proves absolute 0..100 value and acknowledgement semantics on the official
path, but is not a Windows wire validation. The frontend should throttle slider
input similarly; the backend guarantees serialization and exact ACK matching.
