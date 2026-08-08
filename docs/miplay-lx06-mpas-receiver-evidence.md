# MiPlay LX06 mpas/mpap receiver evidence

Date: 2026-07-21

Scope: offline static analysis only. No S12 network operation, device shell,
firmware flashing, playback request, RTSP request, media transfer, audio frame,
`0x0058`, or other protocol probe was performed while collecting this evidence.
Any later live validation must stay explicitly limited to the authorized probe
scope.

## Artifact boundary

Analyzed local artifact:

`artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted`

Source image identity supplied for this extraction:

- file: `mico_firmware_c0cb3a1a9_1.88.51.bin`
- SHA-256:
  `A245370CA924BFB38AB9DE00CBBAB0E7A9513FD11F2E6A5907FDC1B3A8DE63EC`
- hardware: `LX06`
- ROM: `1.88.51`
- build date: `2023-11-21`

Correction carried forward from the firmware handoff: LX06 current firmware is
`1.94.13`. The prior `2.1.x` version assumption is discarded for LX06. This
`1.88.51` image is still useful as positive static evidence for the old-version
basic receiver path. A matching `1.94.13` component would help prove exact
current modern SafetyAuth ownership, but it is not a prerequisite for pursuing
bounded legacy/basic functionality from `1.88.51`.

## Service startup

`rootfs-extracted/etc/init.d/miplay` is the first decisive difference from the
older `1.74.1` image:

- it starts `/usr/bin/mpas` with `procd_set_param command /usr/bin/mpas`;
- it uses `procd_set_param respawn 3600 5 0`;
- its stop path notifies `mediaplayer notify_mdplay_status '{"status":0}'` and
  removes `/tmp/multiroom.fifo`.

Binary identity:

- `usr/bin/mpas`, size `1,385,680`, SHA-256
  `9336BA754E864DEE015CDEE688BC45631570133C8E64EF46EBEDD6800D805C43`
- `usr/bin/mpap`, size `1,544,328`, SHA-256
  `BE0A07E405F28491871C2AA6397F8802FECBAC4279AEE61D01F05799C4C47481`

Conclusion: `1.88.51` contains a rootfs MiPlay receiver service. This supersedes
the earlier `1.74.1` negative string-only result for the receiver component.

## mDNS and port evidence

`mpas` contains `_miplay_audio._tcp.local.` at file offset `0x12d058`, virtual
address `0x13d058`.

Two independent ARM32 paths load port `8899` as immediate `0x22c3`:

1. `0x58d1c: movw r2, #0x22c3`; `0x58d20`/`0x58d28` set `r3` to
   `0x13d058`, the `_miplay_audio._tcp.local.` string; then `str r2, [sp]` and
   `bl 0x31264`. This is the mDNS service-registration path.
2. `0x74a44: movw r2, #0x22c3`, followed by `bl 0x40170`. This is a second
   service-initialization path using the same port immediate.

`0x22c3` is decimal `8899`, so this is static code evidence that `mpas`
registers and initializes `_miplay_audio._tcp.local.` on TCP port `8899`. This
is stronger than the earlier direct ASCII `"8899"` searches, whose hits were
digit-table false positives.

This aligns with the existing S12 observation that the receiver is server-first
on `8899/TCP`.

## Receiver dependency and missing SafetyData symbol boundary

`llvm-objdump -p usr/bin/mpas` shows the relevant dynamic dependencies include
`libidmsdk.so`, `libiotdcm_miplay.so`, and the transitive local DCM stack around
`libiotdcm.so`.

A read-only printable-string scan of this receiver-side set:

- `usr/bin/mpas`
- `usr/bin/mpap`
- `usr/lib/libiotdcm_miplay.so`
- `usr/lib/libidmsdk.so`
- `usr/lib/libiotdcm.so`

found the expected `CtrlClient`, `CtrlPipe`, `MiplayServiceCheck`, `Cmd_Auth`,
and `Cmd_GetDeviceInfo` names in `mpas`; `mpap` also contains the
`MiplayServiceCheck::DealPacket Cmd_Auth` auth-gate string. It did not find the
source-side safety-layer symbol family `SafetyData`, `SafetyAuth`, `SafetyInfo`,
`DealSafety`, `CmdSource`, or `SafetyKey` in those checked receiver binaries and
dependencies.

Conclusion: this does not prove that the receiver lacks a SafetyData layer; it
only proves that the current `1.88.51` receiver-side strings do not localize the
modern `0x1400..0x1403` SafetyAuth owner. That is a modern-compatibility gap,
not a blocker for reconstructing the older `mpas`/`mpap` basic receiver path
that is visibly present in this rootfs.

## Modern Safety command-handler boundary in `1.88.51`

A second offline pass checked aligned ARM immediates for the modern
SafetyInfo/SafetyAuth command family in the receiver-side set:

- `usr/bin/mpas`: no aligned `0x1400`, `0x1401`, `0x1402`, or `0x1403` command
  immediates were found in `.text`;
- `usr/bin/mpap`: no aligned `0x1400`, `0x1401`, `0x1402`, or `0x1403` command
  immediates were found in `.text`;
- `usr/lib/libiotdcm_miplay.so` and `usr/lib/libiotdcm.so` do contain aligned
  `0x1400` immediates, but the checked contexts at `libiotdcm_miplay.so:0x615d4`
  and the parallel DCM code use decimal `5120` as a `memset`/`snprintf` buffer
  length, not a MiPlay command id;
- `usr/lib/libidmsdk.so:0xa9a60` similarly compares against decimal `5120` in an
  IDM/logging/data path, not the MiPlay Safety command family;
- the checked receiver dependencies did not expose aligned `0x1401`, `0x1402`, or
  `0x1403` handlers tied to MiPlay Safety acknowledgement/challenge behavior.

Conclusion: the current live S12 behavior -- accepting `0x1400 -> 0x1401` and
mutual `0x1402/0x1403` -- cannot be fully restored from the visible `1.88.51`
`mpas`/`mpap` command code alone. This makes the modern SafetyAuth ownership gap
sharper than a `CtrlClient` state bit. It does not, however, invalidate the old
receiver path: `1.88.51` still contains enough positive evidence to drive a
bounded legacy/basic implementation track without waiting for an OTA delta.

## `1.88.51` basic-function route remains valid

For basic functionality, the actionable receiver evidence is already in the
`1.88.51` rootfs:

- `mpas` starts as an independent MiPlay receiver service and advertises
  `_miplay_audio._tcp.local.` on TCP `8899`;
- legacy auth is present as `0x0028 -> 0x0029`;
- `Cmd_GetDeviceInfo` maps `0x001e -> 0x001f` and preserves the request sequence;
- `Cmd_Open` maps to command `0x0000` in the `mpas` dispatcher;
- `mpap` contains `MiPlayQuick_AudioSink`, `OpenMirrorClient`, packet handling,
  AAC ELD/raw audio strings, queueing, delay-sync text, and the
  `/data/miplay/audio_dump` diagnostic path.

This should be treated as a separate implementation track: reconstruct and test
old-version/basic `mpas` behavior offline first, then decide what minimal live
validation is justified. The missing modern `0x1400..0x1403` owner only blocks
claiming exact current-firmware SafetyAuth compatibility; it does not require an
incremental package before implementing the legacy/basic route.
## Authentication boundary: `0x0028 -> 0x0029`

`mpas` contains `Cmd_Auth`, but it is not in the post-auth
`ServerApp::doMpasCommand` dispatcher.

The ARM32 path at `MiplayServiceCheck::DealPacket` reads the command field and
compares it to `0x0028`:

- `0x3ce3c: ldrh r3, [r1, #1]`
- `0x3ce40: cmp r3, #0x28`
- the following log path is `MiplayServiceCheck::DealPacket Cmd_Auth`

Conclusion: `0x0028` is `Cmd_Auth`, matching the already observed `0x0029`
reply. Keep SafetyAuth as a separate gate/state machine. Successful SafetyAuth
does not imply that later command IDs can be guessed from APK-side names or from
another dispatcher.
## `ServerApp::doMpasCommand` `Cmd_Auth_Ack`: `0x0029` acceptance boundary

The server-side dispatcher also contains a separate `0x0029` branch for
`Cmd_Auth_Ack`, which explains the previously observed same-sequence
`0x0029 -> 0x0022` transition.

Static ARM32 evidence:

- `ServerApp::doMpasCommand` compares the incoming command with `0x0029` at
  `0x65ea8` and enters the branch at `0x65eb0`;
- the branch reads the stored auth challenge/response material from
  `CtrlClient + 0xf8` and `CtrlClient + 0xfc` at `0x65ec4` and `0x65ec8`;
- the verification path uses HMAC-SHA1-shaped constants: ipad immediate
  `0x36363636`, opad transform `0x6a6a6a6a`, and digest length `20`;
- the log string at `0x13ea20` is
  `Cmd_Auth_Ack(%d), authResult[%s]`, with explicit `true`/`false` strings at
  `0x138134` and `0x1375c4`;
- the false branch logs `Cmd_Auth_Ack close Handle` at `0x13ea50`, reaches
  `0x6a5c4`, loads vtable slot `+0x14` at `0x6a600`, and calls it at
  `0x6a604`;
- the true branch writes byte `1` to `CtrlClient + 0x160` at `0x663f4`;
- after the true branch, `0x663f8` calls predicate `0x5bb5c`; when that path is
  active it builds a `syncPhoneState` payload using string `0x13d540`, reads
  mode byte `ServerApp + 0x38b`, increments sequence halfword
  `ServerApp + 0x212`, and sends notify command `0x0022` via helper `0x59ae4`
  at call site `0x664e4`.

This matches the bounded live observation already recorded in
`docs/miplay-research.md`: after our same-sequence `0x0029` auth ACK, the S12
sent `0x0022` payload `04 6d 6f 64 65 03 02`, i.e. `syncPhoneState mode=2`.

Conclusion: the live `0x0022` notify is stronger evidence than "the socket did
not immediately close". It statically implies the receiver accepted the legacy
`Cmd_Auth_Ack` and set the `CtrlClient + 0x160` auth flag. The missing `0x001f`
after post-auth `0x001e` should therefore move downstream of legacy `0x0029`
acceptance: SafetyData/session routing, which embedded parser receives the clear
frame, or the exact `CtrlClient` business-session transition remain better
suspects than the legacy auth ACK itself.

## Auth-success callback boundary

The same `MiplayServiceCheck::DealPacket` path provides a tighter boundary for
what "auth success" means inside `mpas`.

The constructor at `0x3d268` calls `CtrlProtocol` construction at `0x3d284`,
installs the `MiplayServiceCheck` vtable `0x1390ec`, and clears the auth-related
state slots including auth/socket object `+0x78` at `0x3d3b4` and completion
callback manager `+0x8c` at `0x3d3c0`.

After matching command `0x0028`, the path calls an auth/socket object stored at
`MiplayServiceCheck + 0x78`. It then checks for a completion callback manager at
`MiplayServiceCheck + 0x8c` around `0x3ce90` and, when present, invokes the
callback through `MiplayServiceCheck + 0x90` around `0x3cee8`.

Conclusion: the auth gate can report success through a listener/completion
callback, but that callback is external state installed after construction, and
this path still does not directly enter `ServerApp::doMpasCommand`. A post-auth
command such as `0x001e` therefore still requires a later session/listener
installation step before the clear-frame parser and `CtrlClient` business
dispatcher become reachable.

## `MiplayServiceCheck::connectEv`: socket setup, not success listener setup

The next auth-side function is `MiplayServiceCheck::connectEv` at `0x3d4e0`.
It narrows the pre-auth and post-auth listener boundary further:

- `0x3d5d0` allocates a `0xb4`-byte socket-like object and `0x3d5e0` calls its
  constructor path;
- `0x3d624` stores the socket object at `MiplayServiceCheck + 0x78`, and
  `0x3d62c` stores the corresponding control block at `+0x7c`;
- `0x3d6c8` registers the socket data callback using helper pair
  `0x3cefc`/`0x3cc40`;
- `0x3d710` registers the socket state callback using helper pair
  `0x3ccb0`/`0x3cc78`;
- `0x3d730` reloads the socket, `0x3d734` passes the address string at `+0x44`,
  `0x3d738` passes the port at `+0x5c`, `0x3d740` saves the active socket at
  `+0x28`, and `0x3d744` calls the socket vtable slot `+0x5c`.

The state callback at `0x3ccb0` distinguishes connect states:

- CONNECT failed logs through string `0x13920c` at `0x3cd14`, then invokes the
  `+0x8c/+0x90` result callback with `false` at `0x3cd44` when installed;
- ERROR logs through string `0x13923c` at `0x3cd74`, closes/cancels the socket
  through vtable slot `+0x14`, then invokes the same result callback with
  `false` at `0x3cdb8` when installed;
- CLOSED logs through string `0x139264` at `0x3cde4`;
- CONNECT ok logs through string `0x1391e4` at `0x3ce14`, then calls the socket
  vtable slot `+0x34` at `0x3ce30`.

Conclusion: `connectEv` sets up the auth socket and its callbacks, but it does
not install the `MiplayServiceCheck + 0x8c/+0x90` result listener. It also does
not signal success merely because TCP connected: failure/error can call the
listener with `false`, while the `true` completion remains the later `Cmd_Auth`
path. The still-missing installer is external to both the constructor and
`connectEv`, which makes that owner/caller the next best offline target.
## `Cmd_GetDeviceInfo`: `0x001e -> 0x001f`, with async preparation

The stripped binary still exposes a C++ type/lambda string proving the post-auth
dispatcher shape:

`ServerApp::doMpasCommand(shared_ptr<CtrlClient>, _tagCtrlCmdHeader&, void*, uint)`

The dispatcher entry is at `0x65730`. It reads:

- the command field as a halfword at `_tagCtrlCmdHeader + 1`;
- the sequence/request field as a halfword at `_tagCtrlCmdHeader + 3`.

For command `0x001e`:

- dispatch reaches `0x6825c: cmp r3, #0x1e`;
- the handler logs `Cmd_GetDeviceInfo` using string virtual address `0x13e714`;
- after cache/device-info preparation, `0x68350: mov r1, #31` selects response
  command `0x001f` and calls helper `0x368bc`;
- the response path preserves the request sequence from header offset `3`.

One crucial branch prevents a false negative interpretation:

- `0x68290: ldrb r6, [r4, #0x2c0]` reads a cached-info/session flag;
- when that flag is zero, the handler follows `0x69ad8`, an asynchronous device
  information preparation path;
- the async setup copies the current `ServerApp`, `CtrlClient`, and original
  `_tagCtrlCmdHeader` context into a callback object;
- callback `0x65320` writes generated device-info fields back to the `ServerApp`,
  sets `r0 + 0x2c0` to `1` at `0x65398`, reads the saved request sequence from
  callback offset `0x83` at `0x6541c`, and sends response command `0x001f` via
  helper `0x368bc` at `0x65430`;
- when the cached flag is already non-zero, the direct path constructs the
  payload and sends `0x001f` at `0x68350`.

Conclusion: the `0x001e` handler exists, and both cached and uncached paths have
static `0x001f` send evidence. A live probe that sends `0x001e` and immediately
gets a TCP close should no longer be explained by missing handler evidence or a
short observation window; it is more likely failing before or at handler entry,
such as SafetyData post-auth receive state, command-session acceptance, saved
client context, or another session semantic expected by `mpas`.

## `ServerApp::doMpasCommand` pre-switch boundary

The dispatcher has a service/name precheck before the command switch, but this
branch does not explain missing `0x001f` for `0x001e`:

- around `0x6575c`, `doMpasCommand` checks the `CtrlClient`-side shared object
  and calls its vtable `+0x30` at `0x65774` to retrieve a service/name string;
- when the returned string length matches `ServerApp + 0x374`, the path compares
  bytes using `memcmp` at `0x65b44` against `ServerApp + 0x370`;
- if the service/name matches, or no such object is present, execution enters
  the main command switch at `0x65810`;
- if the service/name mismatches, the special branch at `0x657bc` only
  short-circuits the early-command family where `(cmd & ~2) == 0x0004`, i.e.
  `0x0004/0x0006`; other commands still enter the main switch at `0x65810`.

Conclusion: `Cmd_GetDeviceInfo` (`0x001e`) is not blocked by this pre-switch
service/name check. If `0x001e` reaches `doMpasCommand`, the handler at
`0x6825c` should run. Missing `0x001f` therefore remains a pre-dispatch/session
routing problem, not a service/name mismatch gate in this branch.

## Receiver `CtrlProtocol` clear-frame parser boundary

Before `CtrlClient::DealPacket`, `mpas` has a lower-level `CtrlProtocol` parser
that operates on the legacy `$` command-frame shape rather than directly on raw
SafetyData ciphertext.

Static ARM32 evidence:

- parser entry `0x36a68` waits until the accumulated frame is at least `9`
  bytes, matching the legacy `$` command header length;
- `0x36c50` copies the header into a local parser object;
- the parser reads and endian-normalizes command, sequence, and payload length
  from wire offsets `1`, `3`, and `5` respectively. In the local buffer object
  these appear as reads around `0x36c60`, `0x36c64`, and `0x36c68` because the
  stored frame bytes begin at object offset `+4`;
- after the full frame is available, the parser loads the vtable `+0x8`
  `DealPacket` slot. At `0x36cac` it compares that target with the base
  adapter `0x33b90`; only the base-adapter path uses callback manager/invoker
  fields `+0x34/+0x38` near `0x36ce8`. A subclass or secondary vtable target
  instead branches through `0x36d08` and calls the virtual target at `0x36d10`.

Conclusion: the `ServerApp::doMpasCommand` evidence is downstream of a clear
legacy command-frame parser. The observed immediate TCP close after our
post-auth `0x001e` is therefore unlikely to be solved by changing the outer
`$` command id or header shape alone. The next missing receiver-side proof is
where the authenticated SafetyData/session layer decrypts or accepts post-auth
payloads and hands a clear frame into the context-bound `CtrlClient` parser.

## `CtrlProtocol` virtual dispatch and session-object boundary

`CtrlProtocol` is embedded inside higher-level session objects rather than being
a free global parser:

- the `CtrlClient` vtable contains a thunk at `0x32464` that adjusts `this` by
  `+0xb4` and jumps to parser entry `0x36a68`;
- the `CtrlPipe` vtable contains a second thunk at `0x33838` that adjusts `this`
  by `+0x98` and jumps to the same parser entry.

The base callback path is not present by default, but the important session
objects do not require that generic base path:

- constructor `0x33cb8` installs the `CtrlProtocol` vtable at `0x13835c` and
  clears callback state, including the manager slot at `+0x34` around
  `0x33cd4`;
- parser dispatch checks callback manager `+0x34` around `0x36cb4`, then loads
  the invoker at `+0x38` around `0x36ce0` and calls it near `0x36ce8`;
- `MiplayServiceCheck` constructor `0x3d268` installs vtable `0x1390ec`; its
  `+0x8` slot points directly to `MiplayServiceCheck::DealPacket` at `0x3ce3c`,
  and socket data callback `0x3cefc` calls the parser at `0x3cf48`;
- `CtrlClient` constructor `0x331ec` constructs the embedded parser at
  `CtrlClient + 0xb4` via call `0x33294`, then stores secondary vtable
  `0x137ff4` at `0x332c0`; that secondary `+0x8` target is thunk `0x33754`,
  which subtracts `0xb4` and jumps to `CtrlClient::DealPacket` at `0x3344c`;
- `CtrlPipe` similarly constructs its embedded parser at `CtrlPipe + 0x98` via
  call `0x33a44`, stores secondary vtable `0x13827c` at `0x33a54`, and uses
  thunk `0x33908` to reach `CtrlPipe::DealPacket` at `0x33888`. That dispatcher
  first checks owner/context field `CtrlPipe + 0xd4`.

Conclusion: a valid post-auth SafetyData frame still is not enough unless it is
bound to the correct session object's embedded parser. The old “find the generic
`CtrlProtocol + 0x34/+0x38` callback installer” hypothesis is too broad: auth,
client, and pipe parser paths use subclass or secondary vtables that dispatch
directly to their `DealPacket` targets. The sharper missing proof is whether
post-auth SafetyData plaintext is delivered into the `ServerApp::addClient` /
`CtrlClient + 0xb4` parser with `CtrlClient + 0xf4` and `+0x161` preserved.

## `SlaveDevice::DealPacket`: auth response is not getDeviceInfo reachability

A separate `SlaveDevice` dispatcher in `mpas` is an important negative control for
interpreting the already validated `0x0028 -> 0x0029` behavior.

Static ARM32 evidence:

- vtable/name evidence at `0x13ff70` / `SlaveDevice`, with `DealPacket` string at
  `0x13ff98`;
- `SlaveDevice` constructor entry `0x7dff4` is reached from a separate allocation
  path at `0x5f700`, after allocating a `0x160`-byte object. This is not the
  `ServerApp::addClient` allocation of a 368-byte `CtrlClient` at `0x5de64`;
- `SlaveDevice::DealPacket` entry `0x7e694` reads command field
  `_tagCtrlCmdHeader + 1`, subtracts base `0x001a`, and dispatches through a
  30-entry jump table covering `0x001a..0x0037`;
- command `0x0028` reaches branch `0x7e79c`, logs `SlaveDevice recv Cmd_Auth`
  via string `0x1405bc`, computes the legacy HMAC-SHA1 response, then sends
  command `0x0029` through helper `0x367bc` at call site `0x7ea98`;
- command `0x001e` and command `0x001f` both map to the default return target
  `0x7ed64` in this receive jump table;
- the nearby strings `getDeviceInfo` (`0x13ff04`) and
  `SlaveDevice getDeviceInfo` (`0x140368`) belong to a method on `SlaveDevice`,
  but the receive-side `DealPacket` table does not route incoming `0x001e` to a
  `0x001f` response.

Conclusion: accepting `Cmd_Auth` / replying `0x0029` is not sufficient evidence
that later post-auth frames are entering `ServerApp::doMpasCommand`. If a live
session's post-auth bytes are routed into this `SlaveDevice` receive dispatcher,
then no `0x001f` should be expected for an incoming `0x001e`. This gives a
specific, testable offline hypothesis for the S12 close-after-`0x001e` result:
the missing state may be the role/session-object transition from auth responder
or slave device state into the `ServerApp::addClient`/`CtrlClient` business
path, not the numeric `0x001e` command itself.

This evidence still does not justify another identical `0x001e` live reprobe. A
future bounded probe would need new proof that auth completion routes decrypted
SafetyData into the context-bound `CtrlClient + 0xb4` parser rather than this
`SlaveDevice` defaulting receive table.

## Post-auth `CtrlClient` entry boundary

The command-session entry below the raw socket parser is not a bare direct jump
from TCP bytes to `ServerApp::doMpasCommand`.

Static ARM32 evidence now ties the path together as follows:

- `CtrlClient::DealPacket` entry `0x3344c` first reads the context pointer at
  `CtrlClient + 0xf4`; if it is null, it returns before logging or dispatching
  the command.
- The same entry checks byte `CtrlClient + 0x161` at `0x33458`; the constructor
  path around `0x331ec` initializes that flag to `1` at `0x332f8`, so a normal
  constructed client should pass this gate. The located false-write call goes
  through setter `0x329b4` at call site `0x59a24` while removing/clearing a
  client from the server list, so this is cleanup rather than the normal auth
  success gate.
- When both gates pass, `0x33584` calls bridge `0x6dae0`, which calls
  `ServerApp::doMpasCommand` at `0x6db58`.
- If `doMpasCommand` returns false, `0x6db70` enters fallback logic. One path
  logs `waitCmd %d seq=%d` using string VA `0x13eb04`, allocates a 44-byte
  waiting-command object at `0x6dc38`, links it at `ServerApp + 0xc8`, and
  increments `ServerApp + 0xd0`. The hard cap is 16 entries before the
  `throw waiting request %d` log at `0x13eb28`.
- The normal accept/add-client path now explains how the `+0xf4` context is
  populated: `ServerApp::addClient` at `0x5de08` allocates a 368-byte
  `CtrlClient` at `0x5de64`, calls the constructor at `0x5de74`, then calls the
  tiny setter at `0x329bc` through call site `0x5df34`, storing the `ServerApp`
  context into `CtrlClient + 0xf4`. The nearby log string is `add client %d`
  (`0x13d5c4`) under `ServerApp` (`0x13db5c`).
- The same `addClient` path is also an auth/bootstrap path, not an immediate
  business-command path. It calls `CtrlClient` vtable `+0x58` at `0x5df28`
  (target `0x499a4`), binds the `ServerApp` context, then calls
  `CtrlClient::startAuthCountdown` via vtable `+0x34` at `0x5df44` (target
  `0x49728`; string `startAuthCountdown` at `0x138034`, lambda string at
  `0x138090`). It then prepares an auth payload and calls `CtrlProtocol` send
  helper `0x367bc` at `0x5e020` with command `0x0028` and sequence `0`.

Conclusion: an unsupported or temporarily unprocessable command does not by
itself prove an immediate TCP close path in `mpas`; it can be queued as
`waitCmd`. The `CtrlClient + 0xf4` server-context binding and the `0x0028`
auth/bootstrap send are now proven for the normal `ServerApp::addClient` accept
path. That still does not prove that the post-auth receive-side parser receives
live SafetyData plaintext, so the more specific remaining pre-handler gap is
whether auth completion routes the live SafetyData session into that
addClient-bound `CtrlClient + 0xb4` parser with `CtrlClient + 0xf4` and `+0x161`
preserved.
## Source-side post-auth framing boundary from APK static evidence

The legacy source-side APK sample (`Xiaomi Interconnectivity Services 18.0.0.3`,
`libaudiomirror-jni.so`) supports the current Probe's outer-command shape:

- `CmdSource::sendCmdPayload` at `0x17b858` checks `CmdSource + 0x3c0` for an
  installed `SafetyDataDeal`;
- when present, it calls the `SafetyDataDeal` encrypt function through vtable
  offset `+0x10`, then wraps the encrypted payload with the original outer `$`
  command header;
- `SafetyDataDeal` keeps separate AES-CBC contexts at `+0x40` for encrypt and
  `+0x100` for decrypt, so only the corresponding direction's IV advances;
- `CmdSource::getDeviceInfo` at `0x1779a4` increments `CmdSource + 0x2c0` and
  calls `sendCmdPayload(command=0x001e, payload=null, len=0)`;
- `CmdSource::setLocalDeviceInfo` at `0x1771e8` uses
  `sendCmdPayload(command=0x0058, payload=<bytes>, len=<bytes>)`, but this is a
  later source-side sequence step, not permission to send it without a verified
  same-sequence `0x001f`.

The same source-side receive jump table keeps the observation gates explicit:

- `0x001f` reaches the device-info ACK branch at `0x180aa4` and listener vtable
  offset `+0x28`;
- `0x0059` reaches the set-local-device-info ACK branch at `0x180bc4` and event
  `0x0003346c`;
- `0x0022` reaches the notify branch at `0x180c44`.

Conclusion: the current post-auth `0x001e` probe shape -- outer `$` command
`0x001e` with SafetyData-wrapped empty plaintext payload -- is statically
consistent with the source-side native implementation. Since LX06 `mpas` also
proves a receiver-side `0x001e -> 0x001f` handler, repeating the same live
`0x001e` packet without new SafetyData/session-context evidence is low value.
The more useful offline target is now the authenticated receive layer that
bridges SafetyData into `CtrlProtocol` and preserves the `CtrlClient` context.

## `Cmd_Open` and `0x0058`

The same dispatcher maps the primary `Cmd_Open` log path to command `0x0000`.
There is also an alternate open-ish branch at `0x0036` that jumps into later
open handling, but it does not prove the old `0x0058` candidate.

A narrower open-payload pass now proves that `Cmd_Open` is not a bare empty open
command. The `0x0000` branch logs `Cmdtype::Cmd_Open` at `0x667c4`; when payload
bytes are present it branches to `0x69c28`, searches for marker `?mirrorMode=`
(string VA `0x13e604`, find call around `0x69c7c`), parses the following decimal
mode, and routes mode `1` to `0x6bf54` or mode `2` to `0x6c028` through state
helper `0x702a0`. The same branch later searches for `wfd://` (string VA
`0x13e66c`, find call around `0x69dcc`) and, when the source changes, can emit a
`0x0022` `seize` notification through the send path at `0x69fb8`.

A follow-up disassembly pass rules out the most obvious payload-order mistake in
the no-media live run. The handler creates the mirror-mode value substring at
`0x69ca0` and calls `substr` at `0x69cb4`, then parses it with `strtol` at
`0x69cd0`. After the mirror-mode branch, it creates `substr(0,
markerIndex)` at `0x69da0`/`0x69da8` and assigns that stripped value back into
the working URL at `0x69db8`; only then does it search `wfd://` at `0x69dcc` and
split host/port with `strrchr(':')` at `0x69e20` / host copy at `0x69e38`.
Therefore `wfd://192.168.10.9:7236?mirrorMode=1` is statically compatible with
the located parser: `mirrorMode=1` is parsed from the suffix, while the URL used
for WFD matching becomes `wfd://192.168.10.9:7236`.

Another pass found pre-open command paths adjacent to, but distinct from, direct
network `Cmd_Open` receipt. `mpas` loads `sender-info-prepared` at `0x70d88`,
logs `sender-info-prepared index:%d port:%d valid:%d` at `0x70e38`, logs
`on sender-info-prepared pSlave send Cmdtype::Cmd_Open %s` at `0x71158`, sends
command `0x0000` through helper `0x367bc` at `0x711bc`, and later logs
`on sender-info-prepared local send Cmdtype::Cmd_Open %s` at `0x71328`. A
separate `Cmd_AddMirror_Ack` path compares the response sequence around
`0x721e8` and logs `on Cmd_AddMirror_Ack master send Cmdtype::Cmd_Open` at
`0x72210`, then clears/rearms state at `0x72230`/`0x72234`/`0x72238`.

The pre-open command pass now resolves the first layer of that sequence:

- external `ServerApp::doMpasCommand` dispatch compares `0x0040` at `0x65840`
  and enters the `Cmd_SetPlaySource` handler at `0x66ad8`; it sends an immediate
  acknowledgement `0x0041` through helper `0x368bc` at `0x66b50`/`0x66b58`;
- the `0x0041` acknowledgement is sent before payload presence is checked around
  `0x66b70` and before `json_tokener_parse_ex` at `0x66c70`; therefore an empty
  plaintext `0x0040` is valid as an ACK-only dispatcher/session probe and should
  not mutate source identity;
- non-empty `Cmd_SetPlaySource` semantics parse JSON keys `ref_channel`
  (`0x13cb8c`), `ref_function` (`0x13cbc0`), and `ref_content` (`0x13ccd8`), then
  assign them around `0x67710`/`0x67730`/`0x67740`; that source-identity mutation
  remains a later, separate gate;
- an internal/pipe-side helper logs `setPlaySource Cmd_SetPlaySource[%s][%u]`
  at `0x74510`, but sends command `0x005a` at `0x74540`/`0x74544`; this must not
  be conflated with the external `0x0040` command;
- local AddMirror helpers send `Cmd_AddMirror` as `0x002e` at `0x6e96c`/`0x6e970`
  and again at `0x6f1c8`/`0x6f1cc`; before sending they set pending flag `+0x332`
  and store the request sequence at `+0x32e` (`0x6e948`/`0x6e94c`);
- the external `ServerApp::doMpasCommand` dispatcher does not accept incoming
  `0x002e`: it compares command `44` at `0x65e9c`, then command `50` at
  `0x666b8` and `52` at `0x666c0`; command `46` falls through to the unhandled
  false return at `0x667e8`, explaining the negative AddMirror-only run as a
  direction/role error rather than a payload-shape error;
- the matching `Cmd_AddMirror_Ack` dispatch is `0x002f`: `0x70a5c` compares
  command `47` and `0x70a68` branches to the ack handler at `0x71970`;
- AddMirror payload construction exposes identity fragments `from:` (`0x13ec74`,
  append at `0x6ef30`) and `&islocal:` (`0x13ebc8`, append at `0x6ef68`), with a
  literal `0` string at VA `0x141620` / file offset `0x131620`.

A follow-up local-helper pass closes the missing local AddMirror identity value:

- local `addLocalMediaMirror` starts at `0x6e620` and calls the local IP getter
  `0x526e0` at `0x6e630`;
- it appends literal `:7236` (VA `0x13ebb8`) at `0x6e6c4`, stores `0x1c44`
  into `ServerApp + 0x34c` at `0x6e6ec`, and assigns the endpoint string to
  `ServerApp + 0x334/+0x338` at `0x6e6f0`;
- it builds the AddMirror payload by appending the local endpoint at `0x6e704`,
  `&from:` (VA `0x13ebc0`) at `0x6e728`, the same local IP without `:7236` at
  `0x6e740`, `&islocal:` at `0x6e7a0`, and literal `1` (VA `0x1421ec`) at
  `0x6e814`;
- it logs `addLocalMediaMirror Cmd_AddMirror[%s][%zu] %d` (VA `0x13ebd4`, load
  `0x6e910`) and sends command `0x002e` at `0x6e96c`/`0x6e970`.

Therefore the local AddMirror payload shape is now recovered as:

```text
<local-ip>:7236&from:<local-ip>&islocal:1
```

The helper at `0x567f4` independently confirms how local media endpoints are
formed for slave-device state: it calls local IP getter `0x526e0`, compares
against `local ip error` (VA `0x13c774`), uses/defaults port `0x1c44` (`7236`)
at `0x56850`, stores the selected port at `0x569b4`, increments the next seed at
`0x569c0`, and formats `localIP:port`. `SlaveDevice +0x90/+0x94` is logged as
`SlaveDevice m_strMediaUrl[%s]` (VA `0x1403b4`, load/call `0x7c160`/`0x7c168`)
after a `0x567f4` call at `0x7c0cc`; the same path later logs
`SlaveDevice addMirror` (VA `0x1403e4`, load `0x7c1b4`) and calls the AddMirror
builder at `0x7c2e8`.

Finally, local `startLocalMediaClient` at `0x6ea90` reads the prepared
`ServerApp +0x334/+0x338` endpoint, prepends `wfd://`, appends `?mirrorMode=`,
logs `startLocalMediaClient Cmd_Open[%s][%zu]` (VA `0x13ec18`, load around
`0x6ec20`), and sends command `0x0000` at `0x6ec60`/`0x6ec64`. This ordering is
important: AddMirror prepares identity/session state before a local Cmd_Open,
but external `0x002e` is not the right receiver entry.

Conclusion: the current no-callback live `Cmd_Open` result is no longer well
explained by URL query ordering. The direct AddMirror-only validation is now
also explained by static dispatch: external `0x002e` is unhandled. The next
reversible live question is narrower and safer: can an authenticated external
SafetyData command reach `ServerApp::doMpasCommand` at all? An empty-plaintext
`0x0040 Cmd_SetPlaySource` should produce `0x0041` before any JSON parsing or
source-identity mutation, while JSON source identity, `Cmd_Open`, `0x0058`,
AddMirror, RTSP, media, playback, and audio remain gated.

For command `0x0058` decimal `88`, the observed `ServerApp::doMpasCommand`
dispatch path falls through the handled comparisons and returns false. It is not
handled as `Cmd_Open` in this `1.88.51` `mpas` dispatcher.

Conclusion: `0x0058` must remain gated. It may belong to another framing layer,
another binary, another firmware generation, or a previous misinterpretation,
but this `mpas` evidence does not justify sending it.

## mpap audio receiver boundary

`mpap` independently confirms the downstream open bridge. Its `Cmd_Open`
payload path logs `Cmd_Open:%s m_Layout:%d` (string VA `0x15e6ac`, load around
`0x21398`), searches the same `?mirrorMode=` marker (string VA `0x15e6d4`, find
around `0x213c8`), then searches `wfd://` (string VA `0x15e734`, find around
`0x2155c`). If the payload already contains a WFD URL, it calls
`OpenMirrorClient` (`0x1f900`) directly around `0x21570`. If not, the fallback
branch at `0x21cac` synthesizes a WFD URL by appending `wfd://`, the original
payload, and the default suffix `:7236` (string VA `0x15e754`, append around
`0x21d2c`), then calls `OpenMirrorClient` around `0x21d8c`.

This means the old/basic open payload target is no longer just an `mpas`
dispatcher guess: the paired audio receiver process expects the same structured
mirror payload and can normalize it into the WFD/RTSP bridge. This still does
not authorize live `Cmd_Open`, RTSP, playback, media, or audio-frame probes.

`OpenMirrorClient` then parses the source endpoint. It searches the last `:` in
its WFD URL at `0x1fb50` (separator string VA `0x15e5f8`), defaults a missing
port to `0x1c44` / decimal `7236` at `0x1fb58`, and parses explicit ports with
`strtol` at `0x1fc64`. The downstream WFD client constructor at `0x6a588` repeats
that endpoint split more concretely: it skips the `wfd://` prefix at `0x6a668`,
uses `strrchr(':')` at `0x6a678`, copies the host into object offset `+0x04`,
stores the parsed port at `+0x24`, and also writes the same default `7236` at
`0x6a6e4`/`0x6a6ec` when no port is present.

The later WFD/RTSP state also exposes explicit `sourceHost` and `sourcePort`
keys (`0x167998` and `0x1679a4`). These keys are emitted around `0x83548` /
`0x83560` and `0x83998` / `0x839b0`, and parsed around `0x8aca0` / `0x8acc0`.
RTSP setup builds `rtsp://%s/wfd1.0/streamid=0` (string VA `0x1686fc`, snprintf
around `0x88058`), while WFD presentation metadata contains
`wfd_presentation_URL: rtsp://%s/wfd1.0/streamid=0 none` (string VA `0x16a544`,
build path around `0x927e8`).

Conclusion: a future open test cannot just send `Cmd_Open`; the source side must
first provide a reachable WFD/RTSP endpoint shape, with host and port semantics
matching the payload. The static default is port `7236`, but live open, RTSP,
playback, media, and audio-frame probes remain gated.

`mpap` contains the receiver-side audio path strings:

- `MiPlayQuick_AudioSink`
- `OpenMirrorClient`
- `DealPacket`
- `audio/mp4a-latm`
- `/data/miplay/audio_dump`

Conclusion: `mpap` is the paired low-latency audio receiver process, but this is
only static evidence. It does not authorize media, RTSP, playback, or audio
frame probes.

## Test-backed hypotheses

The test-backed representation is in `MiPlayLx06MpasReceiverEvidence`:

1. `mpas`/`mpap` are present and `etc/init.d/miplay` starts `mpas`.
2. mDNS registration and a second init path statically prove port `8899`.
3. The checked receiver dependency set links `libidmsdk.so` and
   `libiotdcm_miplay.so` but does not expose SafetyData/SafetyAuth/DealSafety
   strings, so this firmware does not localize the modern `0x1400..0x1403`
   owner.
4. Aligned opcode scanning found no `0x1400..0x1403` command-handler immediates
   in `mpas`/`mpap`; checked dependency `0x1400` hits are decimal `5120`
   buffer/log constants, and no `0x1401..0x1403` MiPlay Safety handler is
   localized. This is a modern-compatibility gap, not a blocker for the old
   receiver path.
5. `1.88.51` is sufficient evidence for a bounded legacy/basic route: TCP `8899`,
   `0x0028/0x0029`, `0x001e/0x001f`, `Cmd_Open 0x0000`, and the paired `mpap`
   audio receiver bridge are all statically present.
6. `Cmd_Auth` is command `0x0028` in `MiplayServiceCheck::DealPacket`, matching
   the observed `0x0029` reply.
7. `ServerApp::doMpasCommand` also handles `Cmd_Auth_Ack` command `0x0029`;
   the true branch writes `CtrlClient + 0x160 = 1` and can emit `0x0022`
   `syncPhoneState`, so the observed `0x0022 mode=2` proves legacy auth ACK
   acceptance.
8. Auth success triggers a `MiplayServiceCheck + 0x8c/+0x90` completion callback,
   but does not directly enter `ServerApp::doMpasCommand`.
9. `MiplayServiceCheck::connectEv` installs the auth socket and socket
   data/state callbacks, but does not install the `+0x8c/+0x90` result listener;
   TCP connect success only starts the socket path, while `true` completion waits
   for `Cmd_Auth`.
10. `Cmd_GetDeviceInfo` maps request `0x001e` to response `0x001f` with the same
    sequence field.
11. The async preparation path at `0x69ad8` completes through callback `0x65320`,
    sets `r0 + 0x2c0`, and can send `0x001f`; missing `0x001f` is therefore not
    evidence of a missing handler or merely too short an observe window.
12. `CtrlClient::DealPacket` requires context `+0xf4` and enabled flag `+0x161`;
    `+0x161` defaults true at construction, and the located false write at
    `0x59a24` is client-removal cleanup. False `doMpasCommand` returns can queue
    `waitCmd`, so an immediate close remains a pre-handler/session-state symptom.
13. The `doMpasCommand` service/name precheck does not block `0x001e`; a mismatch
    only short-circuits the `0x0004/0x0006` early-command family, while
    `0x001e` still enters the main switch at `0x65810` and handler `0x6825c`.
14. `ServerApp::addClient` constructs `CtrlClient`, binds `CtrlClient +0xf4`,
    starts `CtrlClient::startAuthCountdown`, and sends `Cmd_Auth 0x0028` through
    `CtrlProtocol` helper `0x367bc`, so the normal accept path is now an
    auth/bootstrap path rather than an immediate business-command path.
15. `CtrlProtocol` parses the clear legacy `$` header before dispatching through
    vtable `+0x8`; only the base adapter uses the generic `+0x34/+0x38`
    callback path, while subclass/secondary paths virtual-call their
    `DealPacket` targets.
16. `MiplayServiceCheck`, `CtrlClient`, and `CtrlPipe` each use direct
    subclass/secondary parser dispatch. `CtrlClient + 0xb4` reaches thunk
    `0x33754`, then `CtrlClient::DealPacket`, so the key missing receiver-side
    proof is SafetyData/session routing into that context-bound parser.
17. Source-side `sendCmdPayload` wraps the original outer command after
    SafetyData encryption, so `0x001e` with encrypted empty payload is a supported
    shape; this does not authorize `0x0058`.
18. `Cmd_Open` maps to `0x0000` in this dispatcher and requires a structured
    payload containing `?mirrorMode=` plus a `wfd://` source; source changes can
    emit a `0x0022` `seize` notification. `0x0058` is not handled and remains
    forbidden for probes.
19. `mpas` parses the `?mirrorMode=` suffix first, then strips that query before
    searching for `wfd://`. The previous no-media payload
    `wfd://192.168.10.9:7236?mirrorMode=1` therefore matches the located parser;
    its negative live result should not be attributed to URL query ordering.
20. `mpas` also has `sender-info-prepared` and `Cmd_AddMirror_Ack` paths that can
    send or re-arm `Cmd_Open 0x0000`; this makes source identity, device-info,
    AddMirror, or sender-info session state the next testable pre-open
    hypothesis.
21. `Cmd_SetPlaySource` is split across external `0x0040`/ack `0x0041` and an
    internal pipe helper `0x005a`; do not conflate the two in Probe code.
22. `Cmd_AddMirror` is `0x002e` and matching `Cmd_AddMirror_Ack` is `0x002f`;
    the local helper stores the request sequence at `+0x32e`, sets pending
    `+0x332`, then only a matching ack can re-arm the master `Cmd_Open` path.
23. AddMirror payload construction includes `from:` and `&islocal:` identity
    fragments and the local value is closed as `<local-ip>:7236&from:<local-ip>&islocal:1`,
    but external `0x002e` is not accepted by `ServerApp::doMpasCommand`; do not
    retry AddMirror-only without a new role/direction mechanism.
24. `mpap` independently bridges `Cmd_Open` payloads into `OpenMirrorClient`:
    direct `wfd://` payloads call it directly, while non-WFD payloads can be
    normalized to `wfd://<payload>:7236`. This strengthens the old/basic payload
    target but still does not authorize a live open or media probe.
25. `OpenMirrorClient` and the downstream WFD client constructor parse the WFD
    URL into source host and port. Missing port defaults to `7236`, host is stored
    at WFD-client offset `+0x04`, and port at `+0x24`.
26. WFD/RTSP state uses explicit `sourceHost`/`sourcePort` keys and builds
    `rtsp://%s/wfd1.0/streamid=0`, so any future reversible open validation needs
    a real source-side WFD/RTSP endpoint prepared before sending `Cmd_Open`.
27. `mpap` proves an audio receiver path, but no media/playback action is in
    scope.

## Bounded live validation note

After this static evidence was written, the user explicitly authorized one real
device validation against `192.168.10.4`. The Probe performed only the already
bounded sequence: native version `0x0036`, legacy `0x0028 -> 0x0029`, SafetyInfo
`0x1400`, mutual SafetyAuth `0x1402/0x1403`, then a single empty-plaintext
SafetyData `0x001e getDeviceInfo` with sequence `0x0004` and a 20-second
post-auth observation window.

Result: mutual SafetyAuth succeeded, but the S12 closed TCP immediately after
`0x001e`; no `0x001f` was observed. No `0x0058`, `Cmd_Open`, RTSP, playback,
media, audio, heartbeat, or other business-control frame was sent.

Interpretation: the failure is no longer explained by a missing `0x001e` handler
or by the previous 5-second observation window. The static async callback at
`0x65320` also sends `0x001f` when the uncached path succeeds, so the next
hypothesis should stay offline and focus on pre-handler acceptance: the exact
post-auth SafetyData receive state, command-session state, preserved CtrlClient
context, and other session semantics expected by `mpas` after mutual SafetyAuth.

## AddMirror-only acknowledgement validation result (negative; superseded)

The Probe retains the historical AddMirror-only mode, but it is no longer the next real-device check:

```powershell
dotnet run --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-safety-mutual-auth-add-mirror-probe=192.168.10.4 --miplay-post-auth-observe-seconds=15
```

After legacy `0x0028 -> 0x0029`, native version `0x0036`, SafetyInfo `0x1400`,
and mutual `0x1402/0x1403` SafetyAuth are all verified, this mode sends exactly
one SafetyData-wrapped `Cmd_AddMirror 0x002e` with sequence `0x0004` on the
native-bootstrap path. The plaintext payload is derived from the connected TCP
local IPv4 address:

```text
<control-local-ip>:7236&from:<control-local-ip>&islocal:1
```

The validation target is only whether the receiver returns a decryptable
`Cmd_AddMirror_Ack 0x002f`. The mode does not start an RTSP listener and sends
no response or follow-up command.

Explicitly forbidden in this mode: `Cmd_Open 0x0000`, `0x0058`, `0x0040`, RTSP
listener/response, SETUP/PLAY progression, RTP, media, playback, audio frames,
retry, fallback control frames, and any second AddMirror attempt.

### 2026-07-21 S12 AddMirror-only validation result

A single run was executed against `192.168.10.4` with the AddMirror-only mode.
Observed local control endpoint: `192.168.10.9:10527`.

Verified/sent sequence:

1. native source version `0x0036`, sequence `0x0001`, payload `3.1.6030516`;
2. server-first legacy `0x0028` accepted with `0x0029`;
3. SafetyInfo `0x1400`, sequence `0x0002`; device replied `0x1401 result=0`,
   selected `authAlgorithm=4`, `aesIv=2`, control-session version frame `0x0037 = 2.1.5091615`;
4. local `0x1402`, peer `0x1402`, local `0x1403`, and peer `0x1403` all
   verified with candidate `peer-first:observed-s12-inbound-iv-type1`;
5. one SafetyData-wrapped `Cmd_AddMirror 0x002e`, sequence `0x0004`, encrypted
   payload length `57`, plaintext payload
   `192.168.10.9:7236&from:192.168.10.9&islocal:1`.

Result: the S12 closed the `8899` control connection after the `Cmd_AddMirror`;
no `0x002f Cmd_AddMirror_Ack` was observed.

No `Cmd_Open`, `0x0058`, `0x0040`, RTSP listener/response, SETUP/PLAY
progression, RTP, media, playback, audio frame, retry, fallback, or second
AddMirror frame was sent.

Conclusion: this negative run confirms the local AddMirror payload can be built
and sent under the current SafetyData session, but it does not verify that an
external Windows source may directly send `0x002e` to the S12. The next offline
question is direction/role gating: whether `0x002e` is only emitted by the
receiver-side master/local helper toward a slave/pipe path, whether an external
source needs a prior sender-info role state, or whether the current post-auth
parser closes on unexpected pre-open commands.

## Prepared no-media `Cmd_Open` RTSP callback validation

The Probe now has a bounded live-validation mode for the old/basic route:

```powershell
dotnet run --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-safety-mutual-auth-open-rtsp-stub-probe=192.168.10.4 --miplay-post-auth-observe-seconds=20
```

Before sending `Cmd_Open`, this mode binds a local no-media RTSP/WFD listener on
the actual IPv4 address selected by the TCP control connection and port `7236`.
If the listener cannot start, the Probe refuses to send `Cmd_Open`.

The only post-auth business frame sent by this mode is one SafetyData-wrapped
`Cmd_Open 0x0000` with payload shape:

```text
wfd://<control-local-ip>:7236?mirrorMode=1
```

The validation target is narrow: observe whether the S12 connects back to the
source endpoint and sends one parseable RTSP/1.0 request. The listener records
remote endpoint, method, target, headers, consumed bytes, and body length, then
stops. It sends no RTSP response.

Explicitly forbidden in this mode: `0x0058`, repeated open, `getDeviceInfo`,
heartbeat, RTSP replies, SETUP/PLAY progression, RTP, media, playback, audio
frames, and any retry or fallback control frame.

### 2026-07-21 S12 no-media `Cmd_Open` validation result

A single run was executed against `192.168.10.4` with the bounded mode above.
Observed local control endpoint: `192.168.10.9:1718`; RTSP listener:
`192.168.10.9:7236`.

Verified/sent sequence:

1. native source version `0x0036`, sequence `0x0001`, payload `3.1.6030516`;
2. server-first legacy `0x0028` accepted with `0x0029`;
3. SafetyInfo `0x1400`, sequence `0x0002`; device replied `0x1401 result=0`,
   selected `authAlgorithm=4`, `aesIv=2`, control-session version frame `0x0037 = 2.1.5091615`;
4. local `0x1402`, peer `0x1402`, local `0x1403`, and peer `0x1403` all
   verified with candidate `peer-first:observed-s12-inbound-iv-type1`;
5. one SafetyData-wrapped `Cmd_Open 0x0000`, sequence `0x0004`, encrypted
   payload length `57`, plaintext payload
   `wfd://192.168.10.9:7236?mirrorMode=1`.

Result: the S12 closed the `8899` control connection after the `Cmd_Open`; the
prepared no-media listener observed no callback on `192.168.10.9:7236` before
control observation ended.

No `0x0058`, `getDeviceInfo`, heartbeat, RTSP response, SETUP/PLAY progression,
RTP, media, playback, audio frame, retry, or fallback control frame was sent.

Conclusion: this negative run does not verify the `Cmd_Open -> OpenMirrorClient`
bridge on the current S12 path. A follow-up offline parser trace shows the exact
payload is statically compatible with `mpas`, because `?mirrorMode=1` is parsed
then stripped before the `wfd://` search. Since mutual SafetyAuth and one
compatible encrypted `Cmd_Open` were both sent, the next work should focus on
what pre-open session state the current receiver requires before accepting
`Cmd_Open`: likely source identity/device-info context, AddMirror/sender-info
prepared state, command-session state, or a receiver-side rejection path before
it attempts the RTSP connection. A local firewall/drop of inbound `7236` cannot
be fully excluded from this single run, but the immediate control close means
repeating the identical probe is low value without new evidence.

## Empty `Cmd_SetPlaySource` ACK-only validation result (negative)

The ACK-only validation was deliberately narrower than a source-identity,
`Cmd_Open`, `0x001e`, or AddMirror retry:

```powershell
dotnet run --no-build --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-safety-mutual-auth-set-play-source-ack-probe=192.168.10.4 --miplay-post-auth-observe-seconds=10
```

After legacy `0x0028 -> 0x0029`, native version `0x0036`, SafetyInfo `0x1400`,
and mutual `0x1402/0x1403` SafetyAuth were all verified, the Probe sent exactly
one SafetyData-wrapped `Cmd_SetPlaySource 0x0040`, sequence `0x0004`, with an
empty plaintext payload. The selected SafetyData candidate was
`peer-first:observed-s12-inbound-iv-type1`; the encrypted payload length was
`25` bytes. Static `mpas` evidence shows the receiver should send `0x0041` at
`0x66b50`/`0x66b58` before the payload gate `0x66b70` or JSON parse `0x66c70`,
so this run only tested whether the post-auth frame reached that dispatcher.

Observed result: the S12 at `192.168.10.4:8899` closed the control connection
after seven follow-up frames; no decryptable `0x0041` was observed.

Explicitly not sent in this mode: JSON source identity, `Cmd_Open 0x0000`,
`0x0058`, AddMirror `0x002e`, RTSP listener/response, SETUP/PLAY progression,
RTP, media, playback, audio frames, retry, fallback control frames, or any
second post-auth command.

Conclusion: because `0x0041` is emitted before payload-length and JSON parsing,
this negative result does not support chasing a non-empty source-identity JSON
next. The current gap is lower: post-auth SafetyData/session routing, command
envelope shape, IV/state transition, or handler ownership before
`ServerApp::doMpasCommand`. Do not repeat empty `0x0040`, `0x001e`, direct
`Cmd_Open`, or AddMirror-only probes without new static evidence.
## Legacy clear `Cmd_SetPlaySource` ACK-only validation results (negative)

Because LX06 `1.88.51` `mpas` does not localize modern `0x1400..0x1403`
SafetyAuth command constants, two bounded legacy/basic checks were run without
modern SafetyInfo, SafetyAuth, or SafetyData. Both used only the legacy auth
bootstrap plus one empty clear-text `Cmd_SetPlaySource 0x0040`:

1. Immediate legacy clear run against `192.168.10.4`: sent `0x0036`, received
   `0x0028` seq `0x01AA`, replied `0x0029`, then sent clear-text `0x0040` seq
   `0x0002` with payload length `0`. The device returned `0x0037` and the usual
   `0x0022` `mode`, `mediaInfoEx`, and `state=3` notifications, then closed;
   no clear `0x0041` was observed.
2. After-ready-notify run against `192.168.10.4`: sent `0x0036`, received
   `0x0028` seq `0x01AE`, replied `0x0029`, waited until decoded notify
   `label=state`, `integerValue=3`, then sent the same clear-text `0x0040` seq
   `0x0002` with payload length `0`. The device closed immediately after the
   post-notify `0x0040`; no clear `0x0041` was observed.

Neither run sent `0x1400`, `0x1402`, `0x1403`, SafetyData, JSON source identity,
`Cmd_Open`, `0x0058`, AddMirror, RTSP listener/response, SETUP/PLAY progression,
RTP, media, playback, audio frames, retry, fallback frames, or any second
business command.

Conclusion: the lack of `0x0041` is not explained by sending the clear legacy
business frame before the receiver's `state=3` notify. On the current S12
receiver, the old LX06 `1.88.51` clear dispatcher is still not reachable from
this Windows-source sequence.

## Delayed SafetyData `Cmd_SetPlaySource` ACK-only validation result (negative)

The encrypted ACK-only check was repeated once with an explicit post-auth delay
to rule out an immediate `DealSafetyDone` timing race:

```powershell
dotnet run --no-build --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-safety-mutual-auth-set-play-source-ack-probe=192.168.10.4 --miplay-post-auth-send-delay-ms=500 --miplay-post-auth-observe-seconds=10
```

This run completed legacy `0x0028 -> 0x0029`, `0x1400 -> 0x1401`, local
`0x1402 -> peer 0x1403`, and peer `0x1402 -> local 0x1403` with candidate
`peer-first:observed-s12-inbound-iv-type1`. After receiving the peer `0x1403`,
the Probe waited `500 ms` without sending data, then sent exactly one
SafetyData-wrapped empty `Cmd_SetPlaySource 0x0040`, sequence `0x0004`, encrypted
payload length `25`. The device closed after seven follow-up frames; no
`0x0041` was observed.

No JSON source identity, `Cmd_Open`, `0x0058`, AddMirror, RTSP listener/response,
SETUP/PLAY progression, RTP, media, playback, audio frames, retry, fallback
frames, or second post-auth command was sent.

Updated conclusion: three mutually separated ACK-only paths now fail in the same
pre-dispatch way: legacy clear immediate, legacy clear after `state=3`, and
modern SafetyData after mutual SafetyAuth plus a `500 ms` delay. Since LX06
`1.88.51` sends `0x0041` before `0x0040` payload-length or JSON parsing, the next
useful work should not be another `0x0040`, `0x001e`, `Cmd_Open`, or AddMirror
probe. The missing evidence is the current `1.94.13` receiver-side
command-session bridge: which component owns modern `0x1400..0x1403`, how it
hands accepted sessions to the legacy `ServerApp`/`CtrlClient` dispatcher, and
what source identity/role state gates external business commands before
`ServerApp::doMpasCommand`.
## Post-auth route-exclusion matrix after ACK-only live checks

The current LX06 firmware boundary is now explicit: the user-confirmed ROM
version is `1.94.13`. The historical `0x0037` values such as `2.1.5091615` and
`2.1.4052010` are preserved only as 8899 control-session version-frame payloads;
they are not LX06 firmware versions.

The ACK-only evidence now excludes four simple explanations for the lack of
post-auth business responses:

| Route | What was sent | Result | Excluded explanation |
| --- | --- | --- | --- |
| Legacy clear immediate | `0x0036`, accepted `0x0028 -> 0x0029`, then empty clear `0x0040` | closed, no `0x0041` | not merely missing modern SafetyData |
| Legacy clear after ready notify | waited for decoded `0x0022 state=3`, then empty clear `0x0040` | closed, no `0x0041` | not merely sent before ready notify |
| SafetyData immediate | mutual `0x1402/0x1403`, then empty encrypted `0x0040` | closed, no `0x0041` | not merely clear-vs-encrypted framing |
| SafetyData delayed | mutual `0x1402/0x1403`, wait `500 ms`, then empty encrypted `0x0040` | closed, no `0x0041` | not merely an immediate post-auth timing race |

Because `mpas` `1.88.51` sends `0x0041` before payload-length or JSON parsing,
these results also rule out non-empty source-identity JSON as the next first
thing to try. The useful remaining target is below that handler: localize the
current `1.94.13` command-session bridge or handler owner that accepts modern
`0x1400..0x1403`, then proves how a successful session is handed to the legacy
`CtrlClient` / `ServerApp::doMpasCommand` path. Until that bridge is proven, no
repeat SafetyData `0x001e`, repeat `0x0040`, `Cmd_Open`, `0x0058`, AddMirror, RTSP, media,
playback, or audio probe is justified. A separate legacy-clear, read-only `0x001e` validation is tracked below because it targets a different non-SafetyData route.

The test-backed representation is `MiPlayPostAuthRouteExclusionEvidence`.

## Legacy clear `Cmd_GetDeviceInfo` live validation (positive)

One bounded live validation was run against a single S12/LX06 at `192.168.10.4`
after the legacy/static evidence separated the old/basic 8899 route from the
modern SafetyData route:

```powershell
dotnet run --no-build --project .\tools\DLNACast.Probe\DLNACast.Probe.csproj -- --miplay-native-legacy-clear-get-device-info-after-ready-notify-probe=192.168.10.4 --miplay-post-auth-observe-seconds=20
```

The exact send boundary was:

- sent native version bootstrap `0x0036`, sequence `0x0001`, payload `3.1.6030516`;
- received server-first legacy `0x0028`, sequence `0x01bf`, payload length `16`;
- sent legacy `0x0029`, same sequence `0x01bf`, HMAC payload length `20`;
- observed `0x0037` version ACK payload `2.1.5091615` as a control-session version frame, not a ROM version;
- observed decoded `0x0022` notifies: `mode=2`, `mediaInfoEx` with status/device state `3`, then `state=3`;
- only after `state=3`, sent exactly one clear-text `$` frame `0x001e Cmd_GetDeviceInfo`, sequence `0x0002`, empty payload;
- observed matching clear `0x001f`, sequence `0x0002`, payload length `415`;
- sent no `0x1400`, `0x1402`, `0x1403`, SafetyData, `0x0040`, `0x0058`, `Cmd_Open`, AddMirror, RTSP listener/response, RTP, media, playback, audio, retry, fallback, or second control frame.

The `0x001f` payload contains sensitive device/account identifiers, so the
project evidence stores its length and SHA-256 instead of permanently writing
raw private IDs into the Markdown:

- payload length: `415`
- payload SHA-256: `BF693DD245AFA365D04BB246032A2A86BF9E28FC3765D3D9C36DB1F3F1E8155F`
- non-sensitive parsed fields observed: `model=LX06`, `romVersion=1.94.13`, `support=audio`, `deviceType=4`, `miName=小爱音箱Pro`
- sensitive fields observed but redacted from committed docs/tests: `accountId`, `bluetoothMac`, `deviceId`, `house_Id`, `roomName`, `room_Id`, `sn`, `miotDid`

Conclusion: the old/basic 8899 read-only command route is usable on the current
LX06 `1.94.13` receiver. The earlier negative `0x001e` result applies to the
modern SafetyData-wrapped post-auth path, not to this legacy clear route. The
next useful step is to use the now-proven `0x001f` shape for offline
source-identity/session-context reconstruction. This result still does not
authorize `Cmd_SetPlaySource`, `0x0058`, `Cmd_Open`, AddMirror, RTSP, media,
playback, or audio.

The test-backed representation is
`MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence`.

## Small playback-state command family

The earlier `ServerApp::doMpasCommand` observation around `0x657bc` is now
paired with the phone-side MirrorOS3 command-name table. The source library
maps:

- stub `0x0b67b4`, string VA `0x2ab37f`: `0x0004 Pause`;
- stub `0x0b67c0`: `0x0005 Pause_Ack`;
- stub `0x0b67cc`, string VA `0x2ab397`: `0x0006 Resume`;
- stub `0x0b67d8`: `0x0007 Resume_Ack`.

At `mpas` `0x657bc`, `ldrh [header+1]`, `bic command,#2`, `cmp #4` recognizes
exactly `0x0004/0x0006`. When the preceding service/name comparison does not
match, those two commands return success before the main switch. They otherwise
fall through the small-command tree rather than the media-player handlers.

This distinguishes them from `0x0042 StartMediaPlayer`, `0x0044
PauseMediaPlayer`, and `0x0046 ResumeMediaPlayer`. In the full rooted-phone
playback capture the small Pause/Resume commands have empty payloads, no ACKs,
and do not interrupt the continuing RTP audio stream.
