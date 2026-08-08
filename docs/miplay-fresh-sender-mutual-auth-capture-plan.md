# MiPlay fresh sender mutual-auth capture plan

This plan fixes a lifecycle ambiguity in the existing rooted-phone evidence. It
is implemented and tested offline, but it has not been run on the LAN in this
change.

## Corrected evidence boundary

The rooted pcap
`artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap`
contains no TCP bootstrap, `0x1400`, `0x1402`, or `0x1403`. Its first visible
phone command is `0x0058` at sequence `0x013a`, so it is a mid-session update,
not proof that `0x0058` immediately follows `DealSafetyDone`.

Consequently, the negative S12 run that replayed this `0x0058` as sequence
`0x0004` proves only that the mid-session update cannot be transplanted into the
fresh-session position. It does not invalidate the recovered JSON at its proper
later lifecycle point.

`MiPlayOfficialPostAuthSequenceProbePlan` now requires
`FreshSessionCommandOrderCaptured=true` before it can ever become network-safe.
The current Probe passes `false`, so the old six-step replay path is blocked even
when its old confirmation flag is present.

## Authentication-only capture design

The existing distinct fake receiver identity is reused; no real S12 identity is
copied. A separate CLI mode is prepared:

```powershell
dotnet run --project .\tools\DLNACast.Probe -- --miplay-mutual-auth-sender-capture=192.168.10.9 --miplay-confirm-mutual-auth-sender-capture --miplay-capture-seconds=180
```

Running that command is a LAN operation and still requires fresh explicit user
authorization. This offline update did not run it.

The permitted outbound set is closed and test-backed:

1. one legacy `0x0028` challenge;
2. one same-sequence `0x1401` selecting `(authKey=1,
   authAlgorithm=4, integrity=1, aesKey=1, aesIv=2)`;
3. one receiver `0x1402` challenge;
4. at most one `0x1403` response to the phone's challenge.

After both HMAC directions verify, the receiver decrypts exactly one
phone-originated post-auth frame with the continued type-2 inbound CBC state. It
sends no acknowledgement or follow-up command. `0x001e`, `0x0058`, `0x0040`,
Open, AddMirror, heartbeat, RTSP, media, playback, and audio are all forbidden as
outbound frames in this mode.

The log records only command, sequence, lengths, JSON field names, and SHA-256
fingerprints. It does not print the endpoint-derived auth key, AES material,
HMAC, ciphertext, raw identity, or plaintext.

## Offline proof

`MiPlayPassiveSenderMutualAuthCaptureSession` is a pure state machine with no
network I/O. Its tests simulate both the official phone source and bounded
receiver:

- receiver-side peer-source endpoint ordering reproduces the auth-key derivation
  already proven by the rooted phone pcap;
- the captured phone `0x1400` offer accepts selection `(1,4,1,1,2)`;
- crossed `0x1402` challenges and both `0x1403` HMAC acknowledgements verify;
- each direction keeps an independent CBC state;
- the phone's first post-auth frame decrypts only by continuing the same inbound
  state after the phone's `0x1402` and `0x1403`;
- a second post-auth frame is refused, and business commands are never permitted
  outbound.

This capture is the shortest path to the missing golden vector: the actual first
official command after fresh `DealSafetyDone`, with its command id, sequence,
plaintext length/hash, and exact type-2 CBC phase.

## 2026-08-07 fresh legacy-clear result

The explicitly authorized phone capture reached a different, equally important
branch before any modern SafetyInfo exchange. The test receiver advertised the
same distinct non-Lyra identity and sent exactly one legacy `0x0028` challenge.
The phone connected from `192.168.10.58:50516` to
`192.168.10.9:8899` and volunteered:

1. `0x0036`, sequence `0`, payload `1.0.1123012\0`;
2. `0x0029`, sequence `0`, valid acknowledgement of `123456789`;
3. clear empty `0x001e`, sequence `1`;
4. clear `0x0058`, sequence `2`, exact 31-byte JSON
   `{"sourceName":"MI PAD 4\\/Plus"}`;
5. clear empty `0x001a` heartbeats, sequences `3`, `4`, and `5`.

No `0x1400`, `0x1401`, `0x1402`, `0x1403`, or SafetyData appeared. The
receiver sent no `0x0037`, `0x001f`, `0x0059`, `0x001b`, business ACK, RTSP,
media, playback, or audio frame. The phone then closed the socket.

The 31-byte length is now explained exactly: the source JSON escapes `/` as
`\/`; it is not a trailing NUL or newline. Read-only ADB inspection tied this
sender to `com.milink.service` version `12.4.8.13`, whose native log reports SDK
version `1.0.1123012`. This result disproves the assumption that every fresh
8899 session must finish mutual SafetyAuth before getDeviceInfo. It does not
authorize any receiver reply. `0x0037`, `0x001f`, `0x0059`, and `0x001b`
remain offline-only candidates until their exact fresh-clear payload and timing
are proven.

Golden evidence is modeled by
`MiPlayFreshLegacySenderCaptureEvidence`; the raw Base64 frames remain in
`artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.stdout.log`.

The matching phone log now narrows the first receiver dependency further:
`Cmd_Auth` immediately triggers `cmd_sessionsuccess/onSuccess`, which calls
`getDeviceInfo` and sourceName `setLocalDeviceInfo` without waiting for
`0x0037`. A real-S12 `0x001f` later triggers `onDeviceInfo`; the passive test
receiver never reached that callback. The offline inverse device-info codec and
one-frame plan are documented in
`docs/miplay-fresh-legacy-receiver-bootstrap.md`. They remain network-disabled.
