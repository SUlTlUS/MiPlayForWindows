# MiPlay post-auth first-command candidate matrix

This matrix is the current offline boundary after the single read-only
post-auth `0x001e` validation.

No new network operation is authorized by this document.

## Current rows

| Candidate | Route | Command | SafetyData | Live result | Next-frame authority |
| --- | --- | --- | --- | --- | --- |
| `legacy-clear-getDeviceInfo` | clear-text 8899 after decoded `state=3` notify | `0x001e -> 0x001f` | no | accepted on LX06 `1.94.13` | no |
| `post-auth-native-no-reset-getDeviceInfo` | mutual SafetyAuth then native-no-reset command-session candidate | `0x001e -> 0x001f` | yes | closed without `0x001f` | no |
| `post-auth-native-no-reset-setPlaySource` | mutual SafetyAuth then native-no-reset command-session candidate | `0x0040 -> 0x0041` | yes | closed without `0x0041` | no |
| `post-auth-observed-inbound-promoted-setPlaySource` | old Probe negative-control path | `0x0040 -> 0x0041` | yes | closed without `0x0041` | no |
| `post-auth-fork-reset-getDeviceInfo` | hypothetical `DealSafetyDone` cipher fork/reset | `0x001e -> 0x001f` | yes | not live-tested; offline-only | no |

## Interpretation

The successful clear-text `0x001e` route proves that the receiver can answer a
read-only device-info request in the legacy 8899 path. It does not prove that
the post-auth SafetyData command session is ready, correctly encrypted, or using
the same state.

The new negative result means the following candidate is now ruled out for the
tested S12:

> mutual SafetyAuth + native-no-reset outbound SafetyData + empty `0x001e`.

Together with the earlier `0x0040` negative results, this blocks repeating
native-no-reset first-command probes and blocks any `0x0040`, `0x0058`,
`Cmd_Open`, `Cmd_AddMirror`, RTSP, media, playback, or audio follow-up.

## Next useful offline target

The next useful evidence is one of:

- a byte-level official sender vector for the first post-auth command after
  `DealSafetyDone`;
- static proof that `DealSafetyDone` forks/resets/reinstalls the SafetyData
  command cipher;
- static proof of a receiver-side command-session/listener/context transition
  that the current Probe does not reproduce.

Without one of those, another S12 network action would be a guess rather than a
validation.
