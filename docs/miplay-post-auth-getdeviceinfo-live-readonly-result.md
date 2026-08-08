# MiPlay post-auth getDeviceInfo live-readonly result

This records one bounded S12 validation against `192.168.10.4:8899`.

## Sent boundary

- Native version bootstrap sent: `0x0036`, sequence `0x0001`, payload `3.1.6030516`
- Legacy server-first challenge acknowledged: `0x0028 -> 0x0029`
- SafetyInfo offer sent: `0x1400`, sequence `0x0002`
- Mutual SafetyAuth completed:
  - local `0x1402`, sequence `0x0003`
  - peer `0x1402`, sequence `0x0000`
  - local `0x1403`, sequence `0x0000`
  - peer `0x1403`, sequence `0x0003`
- Exactly one post-auth command sent:
  - outer command: `0x001e` (`Cmd_GetDeviceInfo`)
  - sequence: `0x0004`
  - plaintext payload length: `0`
  - encrypted SafetyData payload length: `25`
  - selected SafetyAuth candidate: `peer-first:observed-s12-inbound-iv-type1`
  - outbound profile: `native-no-reset-outbound-type2`

No `0x0040`, `0x0058`, `Cmd_Open`, `Cmd_AddMirror`, RTSP, media, RTP, playback, audio, retry, or fallback frame was sent.

## Observed result

The device closed the TCP connection after the post-auth `0x001e` frame.

No same-sequence `0x001f` acknowledgement was observed.

## Interpretation boundary

This is a negative result only for the tested candidate:

> native-no-reset outbound SafetyData plus empty post-auth `0x001e`.

It does not prove that `Cmd_GetDeviceInfo` cannot work in the official post-auth command session. It narrows the remaining gap to one of:

- post-auth command-session cipher phase or IV state still differs from the official sender;
- command-session/listener readiness differs from the official `cmdSessionSuccess -> getDeviceInfo` timing;
- receiver-side session context differs from the official source session.

The legacy clear-text `0x001e -> 0x001f` success must not be promoted to post-auth SafetyData success.

## Next safe work

Continue offline. Do not repeat this network probe without new evidence.

The next useful target is a byte-level official sender vector for the first post-auth command after `DealSafetyDone`, or static evidence that selects a different command-session cipher commit/reset point.
