# MiPlay LX06 idmruntime bridge boundary

Scope: offline-only string evidence from
`artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted/usr/bin/idmruntime` and
the already unpacked LX06 `1.88.51` rootfs. No device/network operation was
performed while producing this note.

## Startup and adjacency

- `etc/init.d/idmruntime` starts `/usr/bin/idmruntime` as a separate procd
  service with respawn.
- `usr/bin/mpas` imports `libidmsdk.so` and `libiotdcm_miplay.so`, so mpas is
  adjacent to the IDM runtime stack even though its visible legacy TCP `8899`
  dispatcher is still a separate path.

## idmruntime string evidence

The following offsets were recovered with a read-only ASCII string scan:

| Offset | String evidence | Meaning |
| --- | --- | --- |
| `0x235875` | `_mi-connect._udp.` | generic Mi Connect mDNS service |
| `0x205C0A` | `urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0` | MiPlay audio IDM service type, not a TCP command bridge |
| `0x20BBA0` | `appsData=` | TXT advertisement data builder |
| `0x236529` | `serviceName+serviceType:%s` | service-name/service-type advertisement state |
| `0x2366D9` | `registerOneService success.` | Bonjour service registration path |
| `0x2354A5` | `UpdateAdvertising` | advertisement update path |
| `0x233485` / `0x2334AD` | `AddAppClient` / `AddAppServer` | app-side registry |
| `0x1FEDA3` | `connectService enter, id:%s, sid:%s...` | service connection path carries client/service ids |
| `0x214805` / `0x2345AA` | `APP_ATTRIBUTE_ID_APP_AUTH` reads/handler | endpoint app-auth attribute path |
| `0x234727` / `0x234864` | `setAttributeNotification` | attribute notification registration, including level |
| `0x218507` | `MC_MI_SEC_COMM...MC_MI_SEC_TRANS` | secure transport mode names |
| `0x1FA9A5` etc. | ASCII `...8788899091...` digit-table context | `8899` text hits are not port evidence |

## Recovered xref evidence

The ARM32 PIC references were recovered with `llvm-objdump -d --demangle` and a
single-pass literal-load resolver. They refine the string scan by showing where
these strings are used:

| Evidence | Xref(s) | Local behavior |
| --- | --- | --- |
| `APP_ATTRIBUTE_ID_APP_AUTH` pair/read log | `ldr 0x10A35C -> add 0x10A364 -> syslog 0x10A370` | pair endpoint app-auth read/log path |
| `APP_ATTRIBUTE_ID_APP_AUTH` handler log | `ldr 0x19D64C -> add 0x19D654 -> syslog 0x19D660`; mode compare at `0x19D664`; callback calls at `0x19D6B4` / `0x19D6D4` with immediate `13` | generic app-auth attribute handling and callbacks |
| `setAttributeNotification` handler logs | with-level log `ldr 0x1999BC -> add 0x1999CC`; app/attr log `ldr 0x1999F4 -> add 0x1999FC` | attribute-notification validation/logging path |
| `_mi-connect._udp.` | `ldr 0x1A50BC -> add 0x1A50C4`; `ldr 0x1A547C -> add 0x1A5484` | mDNS service object creation/registration |
| `appsData=` | `ldr 0xD6F70 -> add 0xD6F78`; `ldr 0x1AA664 -> add 0x1AA670` | TXT/app data output and list insertion |
| `serviceName+serviceType:%s` | `ldr 0x1A9418 -> add 0x1A9420 -> syslog 0x1A9434` | service-name/service-type advertisement logging |
| `AddAppServer` | `ldr 0x19748C -> add 0x197494 -> syslog 0x1974A8` | app/server registry keyed by service id and callback state |
| `MC_MI_SEC_*` cluster | `ldr 0x3E76C -> add 0x3E77C`; table init starts at `0x3E780` | static secure-transport mode table |

This xref pass does not turn any of the strings into legacy TCP 8899 command
ownership. The references stay inside IDM advertisement, endpoint attribute,
notification, app registry, and static enum/table code.
## Boundary

This proves a real device-side IDM identity/auth/advertisement layer beside
mpas: `_mi-connect._udp.`, `appsData`, service name/type, app client/server
registry, `APP_ATTRIBUTE_ID_APP_AUTH`, attribute notification, and secure modes
all exist in `idmruntime`. The only lowercase `miplay` hit in this binary is the IDM service-type URN above; no `mpas`, `mpap`, `Cmd_`, `CtrlClient`, `ServerApp`, `SafetyAuth`, or `SafetyData` strings were found. The ASCII `8899` hits sit inside continuous digit-table text and are not port/bridge evidence.

It does **not** yet prove that this layer hands accepted endpoints to the mpas
TCP `8899` `CtrlClient` / `ServerApp::doMpasCommand` dispatcher, and it does
not localize the current modern `0x1400..0x1403` SafetyAuth owner. Therefore it
does not justify another 8899 business-frame probe.

## Next offline target

The next useful static search is narrower than before: follow
`idmruntime` / `libidmsdk.so` / `libiotdcm_miplay.so` from
`APP_ATTRIBUTE_ID_APP_AUTH`, `connectService`, and attribute notification into
any callback or IPC path that names mpas, `_miplay_audio._tcp.local.`, TCP
`8899`, `CtrlClient`, `ServerApp`, or modern `0x1400..0x1403`.

The test-backed representation is `MiPlayLx06IdmRuntimeBridgeBoundary`.
