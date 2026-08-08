# MiPlay IDM runtime service-id map evidence

This note follows the native server-map strings that were intentionally left out
of the CloudCtrl conclusion. It is offline-only evidence and no S12/device
network operation was performed.

## Java V2 advertising path

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/IDMNative.java`
  - `startAdvertising(String clientId, IDMService service, int discType, int commType, int serviceSecurityType, AppParam appParam, ByteString privateData)` logs `service.getServiceId()` and calls native
    `nativeStartAdvertising(clientId, service.toByteArray(), discType, commType,
    serviceSecurityType, appParam.toByteArray(), privateData.toByteArray())`.
  - The native call returns a Java `String`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/i.java`
  - Wrapper V2 dispatch calls the above `IDMNative.startAdvertising(...)`.
  - Wrapper V1 instead calls `startAdvertisingIDM(iDMService.getServiceId(), ...)`, so V1 and V2 must stay separate.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/o.java`
  - `onAdvertisingResult(byte[])` parses `IDMServiceProto.IDMAdvertisingResult` and passes it to the server proc callback.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/n.java`
  - Failure helper builds `IDMAdvertisingResult(status, serviceId)`, confirming the callback shape even before native success handling.

## Generated protobuf shape

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/api/proto/IDMServiceProto.java`
  - `IDMService` fields:
    - `serviceId = 1`;
    - `type = 2`;
    - `name = 3`;
    - `endpoint = 4`;
    - `originalServiceId = 5`;
    - `superType = 6`;
    - `appData = 7`.
  - `IDMAdvertisingResult` fields:
    - `status = 1`;
    - `serviceId = 2`.

These fields show two different service-id moments: the `IDMService` passed into
advertising, and the service id reported by advertising result.

## Native server-map evidence

`llvm-strings -td artifacts/apk-static/mi-connect-5.1.251.10-native/libidmservicemgr.so`
contains the following relevant strings and offsets:

- `0x1AEA8B` `getRealServiceId failed`
- `0x1AF071` `getUnitServiceIdListByServerId: add serviceUuid %s`
- `0x1AF0D4` `getIdmServerProcByServiceId`
- `0x1AF0F0` `addServerMapServie serverId:%s serviceUuid:%s serviceId:%s`
- `0x1AF12B` `addServerMapServie`
- `0x1AF13E` `updateServerMapServie oldServiceId:%s newServiceId:%s`
- `0x1AF20E` `getRealServiceId serverId:%s serviceUuid:%s`
- `0x1AF23B` `getServiceId`
- `0x1AF248` `getServiceUuidByServiceId: serviceId %s`
- `0x1AF270` `getServiceUuidByServiceId`

This names a native map keyed by at least `serverId` and `serviceUuid`, with a
runtime `serviceId` value. It is separate from the CloudCtrl two-field
`serviceTypeId/serviceType` map.

## Updated state model

`src/DLNACast.Core/MiPlay/MiPlayIdmRuntimeServiceIdMap.cs` models the native
map as requiring:

1. a registered server process for the server id;
2. non-empty `serverId`;
3. non-empty `serviceUuid`;
4. non-empty runtime `serviceId`;
5. an accepted advertising result carrying that service id.

Lookup of the real service id similarly requires `serverId`, `serviceUuid`, and
an existing native map entry. The model explicitly refuses to derive this state
from S12 mDNS `apps=[5]` plus the MiPlay audio IDM URN, or from CloudCtrl
`ServiceConfig(serviceTypeId=5, serviceType=<MiPlay audio URN>)`.

## Implication for `getDeviceInfo`

The Windows Probe still lacks the official runtime server-map state used by IDM
advertising/service lookup. This is another reason not to reinterpret the S12
post-auth `0x001e` silence as a payload-format problem. A useful next offline
step is to trace `nativeStartAdvertising` more deeply to determine whether the
returned Java string is the runtime `serviceId`, a service UUID, or a generated
replacement for `IDMService.originalServiceId`.
