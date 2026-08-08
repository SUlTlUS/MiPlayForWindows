# MiPlay Continuity NetBus service state

This note records one offline-only conclusion for the S12 MiPlay path: the
post-auth gap is not solved by sending another encrypted `0x001e` frame. Mi
Connect Service 5.1.251.10 also requires a Continuity/NetBus service state that
the current Windows Probe has not reproduced.

No S12 connection or network probe was performed for this note.

## Static evidence

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/service/NetBusService.java`
  - `INetBusService.RegisterService(IBinder, String serviceId, String invokePkg, ResultReceiver)` requires a Binder token, a caller package identity, a result receiver, and a caller-supplied `serviceId`.
  - It builds `AppInfo` from Binder `callingUid/callingPid`, checks `BIND_CONTINUITY_SERVICE_INTERNAL` for that `serviceId`, calls `NetBusManagerNative.nativeRegisterService(serviceId, appInfo, callback)`, and records a service token only on success.
  - `startAdvertisingV2(...)` again requires the `serviceId` and permission context before calling `NetBusManagerNative.nativeStartAdvertising(...)`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/service/RegisterServiceResultData.java`
  - The success object carries `mServiceId`, reinforcing that the runtime NetBus `serviceId` is explicit service state rather than mDNS app id state.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/netbus/staticmanager/staticadv/AdvertisingStaticConfigProcess.java`
  - Static advertising uses `advertisingConfigInfo.getServiceId()` and package-signature-derived `AppInfo` before `nativeRegisterService(...)` and `nativeStartAdvertising(...)`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/netbus/staticmanager/staticdisc/DiscStaticConfigProcess.java`
  - Static discovery uses `discConfigInfo.getServiceId()` for `nativeRegisterService(...)`, listener registration, and discovery start.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/api/IDMServiceProto.java` / `IPCParam.java`
  - `IPCParam.RegisterService` preserves fields `serviceProto=1`, `intentStr=2`, `intentType=3`, `discType=4`, `commType=5`, `serviceSecurityType=6`, `privateData=7`, `appParam=8`.
  - `IPCParam.UpdateServiceParam` preserves fields `discType=1`, `advMode=2`, `updateAppData=3`, `appData=4`, `updateStrategy=5`, `commType=6`, `updateType=7`, `advModeScreenOff=8`.
- `artifacts/apk-static/mi-connect-5.1.251.10-native/libidmservicemgr.so`
  - Dynamic JNI symbols include `Java_com_xiaomi_idm_IDMNative_nativeStartAdvertising`, `nativeStartAdvertisingIDM`, `nativeGetServiceInfo`, `nativeGetRemoteServiceInfo`, `nativeUpdateService`, `nativeRegisterIotService`, and `nativeUpdateCloudCtrlServiceConfigs`.
  - `nativeStartAdvertising` takes both a caller/client identity and a service proto/options payload, then returns a Java string through the JNI string-return path; `nativeGetServiceInfo` and `nativeGetRemoteServiceInfo` require both `clientIdOfTheServer` and runtime `serviceId`.

## Recovered gate model

The offline model added in
`src/DLNACast.Core/MiPlay/MiPlayContinuityNetBusServiceState.cs` keeps three
separate gates:

1. `RegisterService`: caller-supplied `serviceId`, Binder token, result receiver,
   Binder-derived `AppInfo`, internal service-id permission, and service-token
   registration.
2. `startAdvertising`: a previously registered service token, `AppInfo` binding,
   `StartAdvertisingOptionsV2`, and advertising data.
3. Static service config: service id from static XML/config, package signature
   material for `AppInfo`, and successful `nativeRegisterService(...)`.

The model deliberately returns `false` for deriving a NetBus service id from the
S12 mDNS discovery identity (`apps=[5]`) plus the IDM MiPlay audio URN
`urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0`.

## Testable hypothesis

Current Probe can prove SafetyAuth and SafetyData framing, but it still lacks at
least one official post-auth prerequisite: NetBus runtime `serviceId`,
Binder/package-derived `AppInfo`, service-id permission, service-token
registration, or static service config. Therefore the observed S12 behavior
after encrypted `0x001e` -- no `0x001f`, then close -- remains expected under
the static model and is not enough evidence to tune the 8899 frame shape.

Another restricted S12 reprobe is not worthwhile until a new offline artifact
identifies the actual service id or maps the official NetBus/Continuity listener
state back to the legacy 8899 command-session state.
