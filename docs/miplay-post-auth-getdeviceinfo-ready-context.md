# MiPlay post-auth getDeviceInfo ready-context evidence

This note captures the next offline gate after the native-no-reset `0x0040` negative result. It does not authorize a network action.

## Why this gate matters

The Mi13P OS3 phone-firmware xrefs now separate two source-side phases:

1. command-session setup and success callback:
   `startCommandChannel -> CmdSessionControl.connectCmdSession -> cmdSessionSuccess -> CmdSessionControl.getDeviceInfo`;
2. later source/playback-statistics events:
   `onTopActiveSessionChange` or `MiplayMultiDisplayManage.onPlay -> StatsUtils.setPlaySource -> CmdSessionControl.setPlaySource(byte[])`.

So the next semantic command after command-session success is `getDeviceInfo`, not an immediate `SetPlaySource`.

## Static command shape

Existing source-side native evidence from `libaudiomirror-jni.so` says:

- `CmdSource::getDeviceInfo` sends command `0x001e` with empty plaintext;
- `CmdSource::sendCmdPayload` checks the SafetyDataDeal pointer and, when present, SafetyData-wraps the original outer command;
- `CmdSource::onRecvCmd` routes `0x001f` to the device-info ACK listener at vtable `+0x28`.

Existing receiver-side evidence says:

- LX06 1.88.51 `mpas` maps `0x001e` to `0x001f`;
- it preserves the incoming sequence;
- it does not inspect request payload bytes;
- the `0x001f` payload is receiver context only and must not be reused as source identity.

Existing runtime evidence says:

- LX06 1.94.13 returned a parsed legacy-clear `0x001f`;
- no post-auth SafetyData-wrapped `0x001e -> 0x001f` success is currently observed.

## Offline read-only plan boundary

The only plan that is worth preparing next is:

- send exactly one SafetyData-wrapped `0x001e`;
- plaintext payload length: `0`;
- first candidate sequence: `0x0004`;
- observe only for same-sequence `0x001f`;
- require decrypted payload length `>= 40`;
- decode with `MiPlayLegacyDeviceInfoPayloadCodec`;
- send no `0x0040`, `0x0058`, `Cmd_Open`, `Cmd_AddMirror`, RTSP, media, playback, audio, retry, or fallback.

This plan remains `SafeForNetworkUse=false` until the listener/onSuccess ready context is recovered or explicitly reviewed and the user grants a fresh live-readonly authorization.

## Current conclusion

The post-auth `0x001e/0x001f` gate is the correct next target, but not yet sendable. The immediate work remains offline:

- localize or emulate the `cmdSessionSuccess`/listener/onSuccess ready context in Probe state;
- confirm the SafetyData cipher profile for an empty `0x001e` frame without conflating it with previous `0x0040` failures;
- only after that, pre-review a one-frame read-only live validation plan.
