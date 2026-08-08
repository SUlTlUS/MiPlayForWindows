# MiPlay post-auth SafetyData state boundary

This note records the first offline-proven difference after the bounded S12
`0x0040` negative validations. It does not authorize or describe any new
speaker/LAN network action.

## Current facts

- Mutual SafetyAuth is verified on S12/LX06.
- The SafetyData V1 container matches the native `SafetyDataDeal` structure:
  `00 07 01 e0 <padLen> <native-integrity-be> <aes-cbc-ciphertext>`.
  Integrity type 1 is CRC-derived, but the header stores the native integrity
  value in big-endian order; project tests keep the local CRC accumulator
  separate from this wire/header value.
- Source-side `CmdSource::sendCmdPayload` keeps the original outer `$` command
  id and sequence, and SafetyData-wraps only the command payload when a
  `SafetyDataDeal` is installed.
- The native `SafetyDataDeal` has separate CBC contexts for encrypt and decrypt.
- The current Probe can decrypt the S12 inbound SafetyAuth challenge only with
  the observed inbound IV workaround: `peer-first:observed-s12-inbound-iv-type1`.
- That workaround is explicitly inbound-only evidence. It does not prove the
  outbound IV for post-auth business commands.
- Empty `0x0040` and official JSON `0x0040`
  `{"ref_channel":"playpage","ref_function":"","ref_content":""}` both closed
  without `0x0041`.
- A separate legacy-clear read-only route succeeded: after legacy
  `0x0028 -> 0x0029` and decoded `state=3`, clear `0x001e` returned clear
  `0x001f` on LX06 `1.94.13`.

## First provable difference

The first implementation gap is now narrower than “payload JSON is wrong” or
“outer command framing is wrong”:

> Probe promotes an inbound-only S12 IV workaround to the outbound/post-auth
> command cipher state; this is verified for mutual SafetyAuth but unverified for
> business commands.

Native SafetyInfo selection says `aesKey=1, aesIv=2`. Static `genAesIv(type=2)`
selects the second half of `authKey`. Real S12 inbound `0x1402` challenge
decryption needs the first half as IV. Those can both be true if direction,
session phase, or `DealSafetyDone` state differs. The old Probe model used one
selected candidate cipher for local SafetyAuth, peer challenge ACK, and the
first post-auth command.

The core cipher now has a pure-offline constructor that can model different
initial encrypt/decrypt IVs. That only enables deterministic vector generation;
it does not make any candidate safe to send.

## Offline candidate matrix

Test-backed class: `MiPlayPostAuthSafetyDataStateBoundaryEvidence`.

| Candidate | AES key | Encrypt IV | Decrypt IV | Evidence | Network-safe |
| --- | --- | --- | --- | --- | --- |
| `native-selection-symmetric-type2` | authKey first half | authKey second half | authKey second half | native selection only | no |
| `observed-s12-inbound-symmetric-type1` | authKey first half | authKey first half | authKey first half | S12 inbound decrypt only | no |
| `asymmetric-native-outbound-observed-inbound` | authKey first half | authKey second half | authKey first half | combines native outbound hypothesis with observed inbound decrypt | no |
| `post-auth-fork-native-selection` | authKey first half | authKey second half | authKey second half | models a possible post-auth reset/fork | no |

## Deterministic synthetic vectors

The project now generates byte-level synthetic vectors with authKey
`0123456789abcdeffedcba9876543210`, command `0x0040`, sequence `0x0004`, and the
same official JSON plaintext used by the bounded live test. These vectors are
for offline comparison only; the sample authKey is synthetic and every vector is
marked `SafeForNetworkUse=false`.

| Candidate | Pre-advanced outbound SafetyAuth frames | Command frame SHA-256 | Result |
| --- | ---: | --- | --- |
| `native-selection-symmetric-type2` | 2 | `5c1d648c8cbd65c99b92bd96ef3e666aa648256b2d4ef82350513f6ae2eef21e` | native outbound IV after no-reset state |
| `observed-s12-inbound-symmetric-type1` | 2 | `bd9f769e4a1f866ec3c467e34f5a88edb28ff890053ac896584863ad5ca57d6e` | current observed-inbound workaround promoted to outbound |
| `asymmetric-native-outbound-observed-inbound` | 2 | `5c1d648c8cbd65c99b92bd96ef3e666aa648256b2d4ef82350513f6ae2eef21e` | same outbound bytes as native type-2; inbound IV cannot be distinguished by a send-only byte vector |
| `post-auth-fork-native-selection` | 0 | `ee39934bafff4d66a729b38f7d034c00938f92d6a5f4cd31cb3db970b33a5b26` | possible `DealSafetyDone` fork/reset baseline |

All four vectors decode to the same outer command shape: `$`, command `0x0040`,
sequence `0x0004`, payload length `73`; the SafetyData header is version `1`,
flags `0xe0`, padding length `3`, encrypted body length `64`. The two native
outbound candidates intentionally share the same bytes because the decrypt IV is
not observable from a send-only frame.

Test-backed method: `CreateDeterministicCandidateVectors()`.

## DealSafetyDone continuity result

The old source-side APK trace now constrains the vector choice further:

- `dealSafetyInfoAck` installs `SafetyDataDeal` before local `sendSafetyAuth`;
- local `0x1402` and local `0x1403` are sent through `sendCmdPayload`, so they
  advance the same outbound CBC state;
- `DealSafetyDone()` is reached only after successful `0x1403` acknowledgement;
- the bounded static trace shows `DealSafetyDone()` setting the done flag,
  notifying the listener, and scheduling timers;
- no `DealSafetyDone()` clear/reinstall of the `SafetyDataDeal` pointer is
  observed;
- post-auth heartbeat also goes through `sendCmdPayload`, which implies continued
  use of the installed SafetyDataDeal in the old source path.

Therefore, for the old source-side APK evidence, the best send-only offline
candidate is the no-reset native outbound vector:

- preferred label: `native-selection-symmetric-type2`;
- equivalent send-only bytes: `asymmetric-native-outbound-observed-inbound`;
- unsupported fork/reset baseline: `post-auth-fork-native-selection`.

The equivalence matters: a captured outbound `0x0040` byte stream can prove the
native outbound IV state, but it cannot by itself prove whether the inbound IV
was native type-2 or the observed S12 type-1 workaround.

Test-backed method: `EvaluateDealSafetyDoneCipherContinuity()`.

## Implementation boundary: outbound cipher profile

The code now has a Core-only, offline `MiPlayPostAuthSafetyDataCipherProfile`
that separates two concepts the old Probe had conflated:

1. SafetyAuth verification candidate: the state that decodes S12 inbound
   `0x1402/0x1403` using the observed inbound IV workaround;
2. post-auth outbound command profile: the state used to encrypt the first
   source-side business command after local `0x1402` and local `0x1403` have
   advanced the outbound CBC chain.

`CreateNativeNoResetOutboundProfile()` uses:

- AES key: authKey first half;
- outbound encrypt IV: native aesIv type 2, authKey second half;
- required outbound pre-advance frames: local `0x1402`, then local `0x1403`;
- expected send-only vector: `native-selection-symmetric-type2`;
- `SafeForNetworkUse=false`.

`CreateObservedInboundPromotedOutboundProfile()` is retained only as the old
Probe negative-control profile. It reproduces the previously sent promoted-IV
bytes and remains `SafeForNetworkUse=false`.

Important boundary: this is outbound-only. It does not prove post-auth inbound
response decrypt state, and it does not enable a live Probe path. Any later live
probe must be a separate, explicitly authorized change after the profile is wired
with the real local SafetyAuth plaintexts and a response-decrypt policy.

Test-backed class: `MiPlayPostAuthSafetyDataCipherProfileTests`.

## Probe dry-run diagnostic

The Probe now has an explicitly gated diagnostic option:
`--miplay-post-auth-outbound-profile-dry-run`. It is only accepted with
`--miplay-native-safety-mutual-auth-probe=<ip>` or
`--miplay-native-safety-mutual-auth-observe-probe=<ip>`, and refuses to run
alongside heartbeat, getDeviceInfo, `0x0040`, `0x0058`, AddMirror, Cmd_Open,
RTSP, media, playback, or audio probe options.

When a future authorized mutual-auth run reaches the verified post-auth boundary,
the diagnostic uses the real local SafetyAuth plaintexts already generated in
that run: local `0x1402`, then local `0x1403`. It compares two send-only
SafetyData command-frame hypotheses for the official JSON `Cmd_SetPlaySource`
payload:

1. `native-no-reset-outbound-type2`;
2. `observed-inbound-promoted-outbound-type1`, the old Probe negative control.

The diagnostic prints only profile labels, command id, sequence, frame lengths,
payload lengths, and full command-frame SHA-256 values. It does not print
`authKey`, plaintext, IVs, or ciphertext bytes, and it does not send the dry-run
post-auth frame. Every generated result remains `SafeForNetworkUse=false`.

Important boundary: invoking this option with a real target would still perform
the selected mutual SafetyAuth or observe-only network handshake. This project
change only adds the dry-run comparison; this offline update did not execute it
against a speaker.

Test-backed classes: `MiPlayPostAuthSafetyDataOutboundDryRun` and
`MiPlayPostAuthSafetyDataOutboundDryRunTests`.

## Live dry-run result: S12 `192.168.10.4`

On 2026-07-24, the bounded dry-run diagnostic was executed once against a
single S12 at `192.168.10.4:8899`. This run performed only the existing native
bootstrap, SafetyInfo, and mutual SafetyAuth path, then stopped before any
post-auth business command. It did not send `0x0040`, `0x001e`, `0x0058`,
Cmd_Open, AddMirror, RTSP, media, playback, or audio.

Observed public/session fields:

- local control endpoint: `192.168.10.9:12679`;
- peer control endpoint: `192.168.10.4:8899`;
- native source version sent: `3.1.6030516`, command `0x0036`, sequence
  `0x0001`;
- receiver control-session version frame: `2.1.5091615`, command `0x0037`,
  sequence `0x0001`; this is not treated as the LX06 firmware version, which
  remains the user-confirmed `1.94.13`;
- SafetyInfo ack: `0x1401 result=0`, `authKey=1`, `authAlgorithm=4`,
  `integrity=1`, `aesKey=1`, `aesIv=2`;
- selected SafetyAuth decrypt candidate:
  `peer-first:observed-s12-inbound-iv-type1`;
- mutual SafetyAuth completed: local `0x1402` sequence `0x0003`, peer `0x1402`
  sequence `0x0000`, peer `0x1403` sequence `0x0003`;
- dry-run command modeled: official JSON `Cmd_SetPlaySource`, command
  `0x0040`, sequence `0x0004`, plaintext length `61`, SafetyData payload
  length `73`, full command-frame length `82`.

Real-session dry-run SHA-256 values:

| Profile | Command-frame SHA-256 | Boundary |
| --- | --- | --- |
| `native-no-reset-outbound-type2` | `29508b1064aaaa901e5de0d9e0b4467b4fcd42a9f334f4bca9f681fc3f0665bd` | best old-source no-reset outbound hypothesis |
| `observed-inbound-promoted-outbound-type1` | `41d298788a1a63930b706eb82c55554e756161024032a4148fd75f058948bee7` | old Probe negative control |

Conclusion: the first post-auth send-only byte difference is now grounded in a
verified mutual SafetyAuth session. The old promoted-inbound-IV Probe path and
the native no-reset outbound profile do not produce the same first `0x0040`
command frame. This is useful evidence, but it still does not prove receiver
acceptance and does not authorize sending a post-auth business frame.

Test-backed class:
`MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence`.

## Implementation follow-up: official JSON send path

The live-capable official JSON one-frame Probe branch has been rewired for the
next authorized validation: it now constructs a separate
`native-no-reset-outbound-type2` SafetyData command cipher from the real local
SafetyAuth plaintexts, local `0x1402` then local `0x1403`, instead of reusing
the old selected inbound decrypt candidate for outbound post-auth encryption.

This preserves the verified `peer-first:observed-s12-inbound-iv-type1` candidate
for inbound SafetyAuth/session observation while changing only the first
post-auth outbound command profile. The branch is still gated by the existing
explicit confirmation flag and was not executed in this update.

## Live native no-reset `0x0040` result

On 2026-07-24, after explicit user authorization, the live-capable official JSON
branch was executed once against `192.168.10.4`. The branch used the separated
state model prepared above:

- verified inbound/session auth candidate:
  `peer-first:observed-s12-inbound-iv-type1`;
- first post-auth outbound command profile:
  `native-no-reset-outbound-type2`;
- command: official JSON `Cmd_SetPlaySource`, `0x0040`;
- sequence: `0x0004`;
- plaintext length: `61`;
- SafetyData payload length: `73`.

The receiver did not return `0x0041`; it closed the control connection after
the one frame. The probe sent no retry, fallback, `0x001e`, `0x0058`, Cmd_Open,
AddMirror, RTSP, media, playback, or audio.

Interpretation boundary: this result proves that the old promoted-inbound-IV
outbound state was not the only blocker, because the native no-reset outbound
profile also failed in isolation. It does not prove that all SafetyData state is
correct, and it does not justify moving to AddMirror/Open/media. The next
productive work should be offline: command ordering, source/session context,
outer envelope ownership, or current LX06 `1.94.13` handler state.

Test-backed class:
`MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence`.

## Next offline work

The deterministic IV/vector layer has now done its job: the old promoted-inbound
outbound state and the native no-reset outbound profile are distinguishable, and
the native no-reset official JSON `0x0040` path was still rejected by LX06
`1.94.13`. The next offline target is therefore above raw SafetyData bytes:

1. official command ordering after mutual SafetyAuth, especially whether a source
   context or listener state transition must precede `0x0040`;
2. source/session context ownership around the legacy `CmdSessionControl` path,
   including any AppInfo/ServiceName/runtime-injected bridge not visible in the
   checked DEX/native traces;
3. current `1.94.13` handler owner that accepts `0x1400..0x1403` but closes
   before `0x0041` for every tested `0x0040` route;
4. a future candidate must again be read-only before mutation before any live
   probe can be designed.

Do not repeat `0x0040`, do not send `0x001e` over SafetyData, and do not send
`0x0058`, `Cmd_Open/openDevice`, AddMirror, RTSP, media, playback, or audio
without new offline evidence and fresh explicit authorization.
