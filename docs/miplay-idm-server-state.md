# MiPlay IDM server state notes

Updated: 2026-07-20

Scope: offline-only static evidence from Mi Connect Service 5.1.251.10. No S12/device network operation was performed while producing this note.

## Current conclusion

The current APK keeps three identifiers separate:

- Mi Connect discovery app id `5`, observed in mDNS `apps=[5]` and AppMgr advertising paths.
- IDM/ServiceManager service type `urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0`, observed in `libidmservicemgr.so` rodata.
- Runtime IDM `clientId`/`serviceId`, used by server-process registration, request routing, and `updateService`.

The static evidence still does not show that app id `5` or service type `00017803` can be converted into a runtime `serviceId`, nor that either is enough to trigger the official post-auth `getDeviceInfo`/listener success path. Another S12 `0x001e` reprobe is therefore not justified yet.

## Evidence

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/i.java`
  - Native wrapper selection uses V2 only when `sdkVersionCode > 1005000`.
  - Server wrapper `i.b` calls V2 `IDMNative.registerIDMServer(...)` and V2 `IDMNative.updateService(...)`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/k.java`
  - `IDMPersistentServiceManager` constructs `new i.b(5000101, this)`, so this APK's persistent server manager is on the native V2 path.
  - Persistent requests are routed by `serviceId`; if no matching `PersistentService` exists, the request is dropped/logged.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/PersistentService.java`
  - A persistent service requires a non-empty intent string, an Android-resolvable activity/service, an initial client id, and a host server binding.
  - If the host server dies, incoming connect-service requests are queued and the host activity/service is restarted by package intent.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/MiConnectService.java`
  - `registerIDMServerProc` validates Binder caller PID/UID against `clientId`, requires a non-null callback, parses `IPCParam.RegisterIDMServer`, then registers native V2 server state.
  - AppMgr `updateService(int appId, ...)` updates an existing `LocalAppServer` only when a callback exists, the app server exists, it is already advertising, and the discovery type is supported.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/b0.java`
  - Binder transaction case 50 reads `clientId`, `serviceId`, and `UpdateServiceParam`; it looks up the server proc by `clientId` before calling native V2 `updateService(clientId, serviceId, param)`.
- Native `libidmservicemgr.so` symbols:
  - `Java_com_xiaomi_idm_IDMNative_nativeRegisterIotService` at `0x3A1C0`.
  - `Java_com_xiaomi_idm_IDMNative_nativeRegisterIDMServer` at `0x41108`.
  - `Java_com_xiaomi_idm_IDMNative_nativeUpdateCloudCtrlServiceConfigs` at `0x403B8`.
  - `idmjni::getCloudCtrlServiceConfigs(CloudCtrlProto::ServiceConfigs&)` at `0x40728`.
  - `Java_com_xiaomi_idm_IDMNative_nativeUpdateService` at `0x431F4`.

## Testable hypotheses now encoded

The repository encodes these conclusions in `MiPlayIdmServerState`:

1. IDM server registration is gated by a caller-bound `clientId`, callback, parsed `RegisterIDMServer`, and native V2 wrapper state.
2. Native IDM `updateService` is gated by an already registered server proc, runtime `clientId`, runtime `serviceId`, parsed `UpdateServiceParam`, and native V2 wrapper state.
3. AppMgr `updateService` is a separate app-id/advertising-data path: it needs an existing advertising `LocalAppServer` for app id `5`, and it rejects IP-discovery update shapes.
4. Discovery app id `5` plus IDM service type `00017803` does not derive a runtime `serviceId`.

## Next offline target

Continue static-only tracing in `libidmservicemgr.so` from `nativeRegisterIotService` and `nativeRegisterIDMServer` into the native V2 server object to find where runtime `serviceId` is created, persisted, or bound to `ServiceInfo`. If no static binding to `miplay-audio` appears there, inspect the CloudCtrl config protobuf path next.
