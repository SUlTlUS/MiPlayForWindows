# Real legacy-clear MiPlay source session

## Capture boundary

The user authorized one passive rooted-device capture. The Windows Probe sent
no MiPlay frame. The capture attached `strace -xx` to the already running
`com.milink.service 12.4.8.13`, observed only its TCP `8899` syscalls, and was
stopped before analysis. No service restart, Wi-Fi toggle, UI automation,
install, file push, speaker modification, or Windows-to-speaker probe occurred.

Artifact:

- path:
  `artifacts/phone_live/fresh-source-captures/mipad4-miplay-source-20260807-131152.strace`
- SHA-256:
  `509F8C4AC8DFBFE2AFA63B085B8E59BD8B0AC4EBC61A52311805451A85B80CC4`
- decoded by `MiPlayStraceNetworkCaptureDecoder`:
  `80` TCP chunks, `92` command frames, `0` issues;
- decoder privacy boundary: raw payloads are not returned and payload prefixes
  default to zero bytes.

Raw `0x001f` values contain permanent account, device, MAC, room, serial, and
MIoT identifiers. They are intentionally absent from this document and from the
new evidence model. Only sizes and hashes are retained where needed.

## Two independently working sessions

| Source -> receiver endpoint | `0x0028` sequence | challenge shape | Result |
| --- | ---: | --- | --- |
| `192.168.10.58:60912 -> 192.168.10.3:8899` | `0x00be` | 16 ASCII digits | basic bootstrap, status replies, heartbeat pairs |
| `192.168.10.58:52488 -> 192.168.10.4:8899` | `0x0370` | 17 ASCII digits | basic bootstrap, status replies, heartbeat pairs |

The 17-byte challenge is not NUL-terminated; all 17 bytes are digits and are
included in the legacy HMAC. This corrects the earlier assumption that every
receiver challenge was exactly 16 bytes.

Both sessions have the same source bootstrap command order and sequence model:

| Direction | Command | Sequence | Payload proof |
| --- | ---: | ---: | --- |
| receiver -> source | `0x0028` | receiver-selected | 16 or 17 digit challenge |
| source -> receiver | `0x0036` | `0` | `1.0.1123012\0`, 12 bytes |
| source -> receiver | `0x0029` | same as `0x0028` | 40 ASCII hex bytes |
| source -> receiver | `0x001e` | `1` | empty |
| receiver -> source | `0x0037` | `0` | native control-session version, not LX06 ROM version |
| source -> receiver | `0x0058` | `2` | exact 31-byte `{"sourceName":"MI PAD 4\\/Plus"}` |
| receiver -> source | `0x001f` | `1` | 415-byte parseable device-info map; raw values redacted |
| receiver -> source | `0x0059` | `2` | empty |
| source -> receiver | `0x0058` | `3` | exact 19-byte `{"isSameAccount":0}` |
| source -> receiver | `0x0034` | `4` | empty |
| receiver -> source | `0x0059` | `3` | empty |
| receiver -> source | `0x0035` | `4` | `00 00 00 00 02`, mirror mode 2 |

The receiver may interleave `0x0022` notifications. The source-name frame and
account frame are byte-identical across both sessions. No SafetyInfo,
SafetyAuth, or SafetyData frame appears.

## Status-query names from receiver disassembly

The post-bootstrap empty queries are no longer unknown. LX06 `1.88.51` `mpas`
static dispatch and live `1.94.13`/other-receiver behavior agree on the relevant
IDs:

| Request -> normal response | Static handler proof in `mpas` | Live result |
| --- | --- | --- |
| `0x000e -> 0x000f` `Cmd_GetVolume` | compare `#14` at `0x65c2c`; log VA loaded at `0x65c54`; response `#15` at `0x65cd4` | both receivers returned five-byte scalar volumes 25 and 24 |
| `0x0010 -> 0x0011` `Cmd_GetPosition` | compare `#16` at `0x679e4`; log at `0x67a0c`; response `#17` at `0x67ac0` | absent because playback was not started |
| `0x0014 -> 0x0015` `Cmd_GetMediaInfo` | compare `#20` at `0x65d6c`; normal response `#21` at `0x65e58` | in mode 2, no `0x0015`; a same-hash 158-byte `0x0022` followed each query |
| `0x001c -> 0x001d` `Cmd_GetState` | compare `#28` at `0x66534`; log at `0x6655c`; response `#29` at `0x665d0` | both receivers returned five-byte scalar state 0 |

The missing `0x0015` is explained by the static mirror-mode-2 branch: at
`0x6aa34`, `mpas` prepares speaker media info and loads response command decimal
`34` (`0x0022`) at `0x6aa10`. This matches the real capture rather than being a
timeout or parser omission.

The first status-query order differed:

- receiver A: `0x000e`, `0x001c`, `0x0014`;
- receiver B: `0x000e`, `0x0014`, `0x001c`.

Therefore only the set, empty payload shape, and per-command sequence/response
matching are treated as protocol facts; relative order is not a readiness gate.

All observed scalar responses use five bytes: tag `0`, then unsigned 32-bit
big-endian value. The hashes reproduce values `0`, `2`, `24`, and `25` in unit
tests.

## What the capture does and does not prove

It proves:

- the current receiver family accepts a working old/basic clear source branch;
- modern SafetyAuth/SafetyData is not required for this branch;
- source version, legacy HMAC, device-info, sourceName, account, mirror-mode,
  status initialization, and heartbeat command shapes;
- two real receivers independently maintain the session.

It does not prove:

- `0x0040` SetPlaySource timing after this prefix;
- Open, AddMirror, RTSP negotiation, media ports, RTP, AAC, playback, or audio;
- that LX06 `1.94.13` internals are byte-for-byte identical to the available
  `1.88.51` receiver binary.

No `0x0040`, `0x0041`, `0x0000`, `0x002e`, `0x002f`, RTSP, playback, media, or
audio appeared because playback was not triggered during this passive capture.

Test-backed representations:

- `MiPlayStraceNetworkCaptureDecoder`;
- `MiPlayRealLegacySourceFreshSessionEvidence`;
- `MiPlayLegacyAudioSourceSession`;
- `MiPlayLegacySourceStatusQueryEvidence`;
- `MiPlayLegacyStatusScalarCodec`;
- `MiPlayLegacyAudioSourceBootstrapProbeGuard`.

The last type is the runtime boundary between the pure reconstructed state
machine and any separately authorized socket test. It accepts only the captured
eight-write/nine-frame sequence, validates frame structure and payload shape,
and stops permanently on a duplicate, reorder, unexpected command, business
command, or boundary overrun. Its dry-run ledger is available through
`--miplay-legacy-audio-source-bootstrap-dry-run` and performs no network
operation.

## Full switch-and-playback capture

A later capture on the same rooted source extends this document beyond the
no-media bootstrap:

- `mipad4-miplay-full-switch-playback-20260807-141132.strace`;
- SHA-256 `499252CB2EFE79EE443526BD58C9AED13EEFAED366F3CE2FDE3D4885454FD8E3`;
- selected LX06 `192.168.10.4`, source `192.168.10.58`;
- control endpoint `192.168.10.58:55776 -> 192.168.10.4:8899`.

The selected receiver used `0x0040` sequence `0x00bb` followed immediately by
Open sequence `0x00bc`. Exact payloads and hashes are pinned in
`MiPlayRealLegacyPlaybackSessionEvidence`. The Open payload contains a required
trailing NUL. The receiver opened reverse WFD/RTSP to port `7274`; the source
did not send AddMirror and did not wait for `0x0041` or `0x0001`.

The reverse RTSP exchange selects AAC only, no video, and TCP interleaved mode.
After receiver SETUP/PLAY and source TIME_OFFSET, audio is sent on the third
reverse TCP connection as `$ + 24-bit length + RTP`, not the usual RTSP
`$ + channel + 16-bit length` framing. RTP carries MPEG-TS payload type 33 with
PAT PID `0x0000`, PMT PID `0x0100`, PCR PID `0x1000`, and AAC PID `0x1100`.

Phone-firmware command-name evidence also resolves the small control commands
seen after RTSP:

| command | official name | ACK |
| ---: | --- | ---: |
| `0x0004` | `Pause` | `0x0005 Pause_Ack` |
| `0x0006` | `Resume` | `0x0007 Resume_Ack` |

These are not `StartMediaPlayer/PauseMediaPlayer/ResumeMediaPlayer`, whose
separate command family begins at `0x0042`. LX06 1.88.51 `mpas` specially
tolerates `0x0004/0x0006` when the service-name precheck does not match, which
is consistent with the capture containing no ACK for those frames.

Test-backed continuations are `MiPlayLegacyPlaybackControlSession`,
`MiPlayWfdSourceRtspSession`, `MiPlayAdtsStreamParser`, and
`MiPlayWfdAudioPacketizer`.

## Windows-origin live validation

The reconstructed path was subsequently validated against the selected LX06
at `192.168.10.4`, whose current ROM version is user-confirmed as `1.94.13`.
Windows at `192.168.10.9` completed the nine-frame bootstrap, seven-frame
playback continuation, three reverse TCP accepts, UDP timer exchange, full RTSP
Ready transition, and 48 silence-only AAC media writes. The 48 media wire
frames totaled 14,868 bytes and represented 1,024 ms at 48 kHz.

No AddMirror, Pause/Resume, user audio, retry, fallback, or second target was
used. Therefore this run proves receiver acceptance of the Windows source
session and transport, while deliberately leaving audible PCM capture and AAC
encoding as the next boundary. The complete redacted snapshot and byte-level
reconstruction are in `MiPlayLegacySilencePlaybackLiveValidationEvidence`.

The same session was then exercised with real Windows default-output loopback
rather than a pre-generated access unit. WASAPI captured the current default
multimedia endpoint, FFmpeg converted 44.1 kHz stereo signed-16 PCM to AAC-LC
48 kHz stereo at 192 kbit/s, and the receiver accepted all 240 access units
(178,868 wire bytes, 5,120 ms). The redacted snapshot is
`MiPlayLegacySystemAudioLiveValidationEvidence`. This proves the full software
socket-write path only. A later 20-second, 938-access-unit run remained silent
according to the user even though RTSP, timer, media writes, and four 5-second
heartbeats remained healthy.

Offline comparison with this document's official phone trace localized a
previously omitted media-clock rule. CSeq 5 carries
`TimeOffset:9633364443`, while the first table-bearing RTP packet uses PCR
`866913276` at 90 kHz: the PCR maps about 994.71 ms before the RTSP monotonic
clock even though RTP timestamp and PES PTS start at zero. The Windows runtime
had emitted initial PCR zero. `MiPlayWfdMediaClock` now anchors PCR to
`TIME_OFFSET - 1,000,000 us`, and `MiPlayWfdSourceRtspSession` exposes the
captured time offset to the packetizer. The same trace also proves that the
source does not reply to receiver `VIDEO_LATENCY` telemetry, so the former
extra 200 responses were removed.

The corrected-clock run was stable but remained silent, so the missing PCR
anchor was not the final gate. The official trace continues after Open with
Pause `0x0004`, SetMediaInfo `0x0012`, receiver `first-audiopcm=1`, heartbeat
`0x001a/0x001b`, then Resume `0x0006`; the first Resume is immediately followed
by receiver `state=2`. Windows had skipped this entire startup-state sequence
and started periodic heartbeats at sequence 15.

The deterministic post-Open model now uses sequences 15-18 for exactly those
four outbound frames. SetMediaInfo carries generic Windows system-audio
metadata rather than replaying the captured phone's Vantage/Follow song. The
startup heartbeat is gated on decoded `first-audiopcm=1`, Resume is gated on
its ACK, and steady-state traffic is not entered until the receiver reports
`state=2`. `0x0013` is accepted if present but is not required because the
1.94.13 capture did not expose it. AddMirror and repeated Resume remain outside
the bounded validation.

The resulting Probe builds with zero warnings/errors and the full suite passes
563/563. At that checkpoint the post-Open sequence was still an offline repair
candidate; the following live result is the separate real-device proof.

The next explicitly authorized live session closed that protocol boundary on
LX06 1.94.13. The receiver progressed through `state=0`,
`first-audiopcm=1`, echoed the Windows `mediaInfoEx`, reported `state=3`,
acknowledged startup heartbeat sequence 17, and reported `state=2` immediately
after the single Resume sequence 18. It then accepted the bounded 938-frame,
20.01-second Windows system-loopback stream and steady-state heartbeat
sequence 19 onward. `MiPlayLegacyPostOpenPlaybackLiveValidationEvidence` pins
the redacted ports, hashes, clock, media totals, and exact one-Resume ledger.
This proves receiver playback-state entry, while physical audibility still
requires the user's separate observation.

## Correction: Pause and Resume were user actions

The earlier post-Open interpretation above is retained as experiment history,
but it is not the automatic selection protocol. The user confirmed that Pause
was pressed manually. A clean follow-up capture with music already playing and
no playback UI action contains no `0x0004` and no `0x0006` at all.

The clean selected-receiver flow is Open `0x0098`, SetMediaInfo `0x0099` with
`status=0` and `mDeviceState=2`, TIME_OFFSET, media,
`first-audiopcm=1`, then receiver `state=2` automatically. Heartbeat `0x009a`
occurs `4,999.827 ms` after the pre-Open heartbeat `0x0096`, so it is the
existing periodic timer rather than a first-PCM-triggered startup gate. The
artifact and exact hashes/timestamps are pinned by
`MiPlayCleanPlayingSelectionCaptureEvidence` and documented in
`miplay-windows-audio-source-transmitter.md`.
