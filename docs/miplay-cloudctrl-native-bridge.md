# MiPlay CloudCtrl native bridge evidence

This note follows the previous CloudCtrl app-intent mapping one layer deeper
into `libidmservicemgr.so`. It is offline-only evidence and does not involve any
S12 traffic.

## Native entry points

`llvm-nm -D --demangle libidmservicemgr.so` exposes two relevant symbols:

- `Java_com_xiaomi_idm_IDMNative_nativeUpdateCloudCtrlServiceConfigs` at
  `0x403B8`.
- `idmjni::getCloudCtrlServiceConfigs(CloudCtrlProto::ServiceConfigs&)` at
  `0x40728`.

The exported Java wrapper in
`artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/IDMNative.java`
matches this shape:

- `getCloudCtrlServiceConfigs()` calls back into `MiConnectService` and returns
  a serialized byte array.
- `updateCloudCtrlServiceConfigs(byte[])` forwards a serialized byte array to
  native `nativeUpdateCloudCtrlServiceConfigs(...)`.

## Native byte-array/protobuf boundary

`nativeUpdateCloudCtrlServiceConfigs` first validates the Java byte array and
copies its bytes, then parses it into `CloudCtrlProto::ServiceConfigs`. The
function contains null/parse failure paths but no Java package, Continuity
`ServiceName`, listener, or runtime `serviceId` argument.

`getCloudCtrlServiceConfigs(CloudCtrlProto::ServiceConfigs&)` performs the
inverse direction: it calls the Java callback, receives a byte array, parses it
into `CloudCtrlProto::ServiceConfigs`, and releases the byte array.

The same ELF string table contains:

- `CloudCtrlProto.ServiceConfigs`;
- `CloudCtrlProto.ServiceConfig.serviceType`;
- `serviceTypeId: `;
- `, serviceType: `;
- `updateCloudCtrlServiceConfigs`;
- `getCloudCtrlServiceConfigs`.

Generated protobuf evidence in
`artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/proto/CloudCtrlProto.java`
keeps this structure narrow:

- `ServiceConfig.serviceTypeId` field number `1`;
- `ServiceConfig.serviceType` field number `2`;
- repeated `ServiceConfigs.serviceConfig` field number `1`.

## Boundary conclusion

CloudCtrl native sync confirms that the two-field mapping is a native-consumed
configuration, not just a Java-side artifact. It still does not provide:

- Android package/signature `AppInfo`;
- Continuity `ServiceName.toMergeString()`;
- NetBus runtime `serviceId`;
- server listener registration;
- channel-created callback state;
- legacy 8899 Java `onSuccess`.

Strings such as `getRealServiceId`, `addServerMapServie`, and
`getServiceUuidByServiceId` do exist in the same native library, but their
addresses sit outside this CloudCtrl JNI byte-array boundary. They should be
traced as the next independent server-map/service-registration path rather than
assumed to enrich CloudCtrl configs.

The repository model
`src/DLNACast.Core/MiPlay/MiPlayIdmCloudCtrlNativeBridge.cs` therefore treats
native CloudCtrl as a serialized `ServiceConfigs` bridge only and explicitly
rejects deriving a runtime service id from it. Another S12 `0x001e` reprobe is
still not justified by this evidence.
