# MiPlay IDM startAdvertising identity boundary

This note is offline-only static evidence from Mi Connect Service 5.1.251.10.
No S12/device network operation was performed.

## Java path

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/MiConnectService.java`
  - `O0(String clientId, byte[] bArr)` handles IDM server-proc
    `registerService`.
  - The V2 branch validates the caller and logs the full
    `IDMService{serviceId, type, name, endpoint, originalServiceId, superType,
    appData}` plus `discType`, `commType`, `serviceSecurityType`, `AppParam`,
    and `privateData`.
  - It calls `mVarB.f8430d.a(serviceProto, discType, commType,
    serviceSecurityType, appParam, privateData)` and returns the resulting
    string after the Kotlin non-null check named `startAdvertising(...)`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/i.java`
  - V2 calls `IDMNative.startAdvertising(clientId, IDMService, discType,
    commType, serviceSecurityType, AppParam, privateData)`.
  - V1 is a separate path: it passes `IDMService.serviceId` to
    `startAdvertisingIDM(...)` and logs a returned `newUuid`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/IDMNative.java`
  - V2 logs `IDMService.serviceId` but calls
    `nativeStartAdvertising(clientId, service.toByteArray(), discType,
    commType, serviceSecurityType, appParam.toByteArray(),
    privateData.toByteArray())`.
  - The native method synchronously returns a Java `String`.

## Advertising result callback path

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/o.java`
  - `onAdvertisingResult(byte[])` parses
    `IDMServiceProto.IDMAdvertisingResult` and calls the server-proc callback.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/m.java`
  - The server proc wraps the parsed result into
    `IPCParam.OnAdvertisingResult` and calls
    `IIDMServiceProcCallback.onAdvertisingResult(...)`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/n.java`
  - Permission failure also builds an `IDMAdvertisingResult(status, serviceId)`,
    so the callback shape is used for both failure and native-result reporting.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/compat/proto/IPCParam.java`
  - `OnAdvertisingResult.idmAdvertisingResult` is field `1`.

## Native evidence

`llvm-objdump -d --demangle --start-address=0x3C118 --stop-address=0x3C3A0
artifacts/apk-static/mi-connect-5.1.251.10-native/libidmservicemgr.so`
shows `Java_com_xiaomi_idm_IDMNative_nativeStartAdvertising`:

- JNI entry address: `0x3C118`.
- It converts/parses the Java `clientId`, `IDMService`, `AppParam`, and
  `privateData` into native stack objects.
- It calls a native worker at `0x92650`.
- It copies the worker result and builds Java result objects.
- It calls a post-result helper at `0x43BD8` before returning a Java string.

`llvm-strings -td artifacts/apk-static/mi-connect-5.1.251.10-native/libidmservicemgr.so`
contains:

- `0x1A1FDF` `Java_com_xiaomi_idm_IDMNative_nativeStartAdvertising`
- `0x1A2014` `serviceId is null.`
- `0x1A7CBB` `startAdvertising`
- `0x1AC74F` `IDMServiceProto.IDMAdvertisingResult.serviceId`
- `0x1B7A32` `HandleStartAdvertising`
- `0x1B7C5B` `server_id:%s, unique_service_id:%s, call service manager startAdvertisingIdm failed`
- `0x1B7CAF` `server_id:%s, unique_service_id:%s, call service manager startAdvertisingIdm success`

The native strings explicitly mention a `unique_service_id` in the
start-advertising handler and a separate protobuf
`IDMAdvertisingResult.serviceId`. This is not enough to assert that the
synchronous Java string is already the runtime service id; the safe boundary is
to require a matching async advertising result before treating it as verified.

## Updated state model

`src/DLNACast.Core/MiPlay/MiPlayIdmStartAdvertisingIdentity.cs` models:

1. V2 `startAdvertising` return prerequisites:
   `clientId`, seed `IDMService.serviceId`, serialized `IDMService`,
   serialized `AppParam`, serialized `privateData`, and a non-empty native
   returned string.
2. Async advertising-result prerequisites:
   native callback arrival, successful `IDMAdvertisingResult` parse, IPC wrapper
   creation, registered server-proc callback, success `status`, and non-empty
   `serviceId`.
3. Verification rule:
   the returned string is considered a verified runtime service id only if it
   exactly matches the async `IDMAdvertisingResult.serviceId`.

## Implication for `getDeviceInfo`

The Probe has authenticated SafetyAuth, but it does not yet reproduce the
official pre-`getDeviceInfo` IDM listener/advertising state. The missing state
is not just an mDNS `apps=[5]` identity and not just CloudCtrl
`serviceTypeId=5/serviceType=<MiPlay audio URN>`. A future read-only S12
recheck is only worth doing after the Probe can model or replay the official
server-proc listener registration and can observe or supply the verified runtime
service id boundary described here.
