# MiPlay CloudCtrl service config evidence

This note records a narrow offline-only finding from Mi Connect Service
5.1.251.10: CloudCtrl service configs preserve an app-id to IDM service-type
mapping, but they do not provide the Continuity or NetBus runtime identity that
the current S12 post-auth `getDeviceInfo` path is missing.

No S12 connection or network probe was performed for this note.

## Static evidence

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/j5/c.java`
  - Room projection model `j5.c` has `@ColumnInfo(name = "serviceType")` and
    `@ColumnInfo(name = "appid")`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/miconnect/security/db/AppConfigDataDao_Impl.java`
  - `getServiceConfigList()` runs `SELECT serviceType,appid FROM app_intent`.
  - `getServiceTypeByAppId(int)` runs
    `SELECT serviceType FROM app_intent WHERE appid = ?`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/miconnect/security/db/IDMAppConfigDataBase_Impl.java`
  - Room creates `app_intent(serviceType TEXT NOT NULL, appid INTEGER NOT NULL,
    action TEXT, extra TEXT, PRIMARY KEY(serviceType))`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/miconnect/security/db/converter/ConverterUtil.java`
  - `appIntentModelToAppIntent(...)` skips empty service types, stores `appId`,
    and may also store `action`/`extra` in the database row.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/i5/d.java`
  - On config updates, `ConfigMgr` builds `CloudCtrlProto.ServiceConfigs` by
    adding only `setServiceTypeId(row.appid)` and `setServiceType(row.serviceType)`;
    rows with empty serviceType are skipped.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/r0.java`
  - `IDMNative.getCloudCtrlServiceConfigs()` obtains the same list from
    `ConfigMgr` and returns the serialized `ServiceConfigs`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/mi_connect_service/proto/CloudCtrlProto.java`
  - `CloudCtrlProto.ServiceConfig` field `serviceTypeId_` is field number `1`;
    `serviceType_` is field number `2`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/idm/IDMNative.java`
  - `getCloudCtrlServiceConfigs()` calls back into `MiConnectService`.
  - `updateCloudCtrlServiceConfigs(byte[])` forwards the serialized configs to
    native `nativeUpdateCloudCtrlServiceConfigs(...)`.

## What this proves

For MiPlay audio, a CloudCtrl entry can plausibly express:

```text
serviceTypeId/appid = 5
serviceType = urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0
```

It does not include:

- Android package name or signature-derived `AppInfo`;
- Continuity `ServiceName.toMergeString()` value (`package:name` or `:name`);
- NetBus runtime `serviceId`;
- listener registration state;
- channel-created callback state;
- Java `onSuccess` state for the legacy 8899 command session.

The offline model in
`src/DLNACast.Core/MiPlay/MiPlayIdmCloudCtrlServiceConfig.cs` keeps this mapping
deliberately small. It can create the same two-field service config pair and
explicitly returns false for deriving Continuity package identity, Continuity
service name, or runtime NetBus service id from that pair.

## Updated hypothesis

CloudCtrl closes one ambiguity: app id `5` plus the IDM MiPlay audio URN is a
real official configuration shape, but it is still only a discovery/security
configuration shape. It is not enough to reconstruct the post-SafetyAuth
`getDeviceInfo` listener/onSuccess chain on Windows.

Another S12 read-only reprobe is still not justified. The next useful offline
target is to trace whether native `nativeUpdateCloudCtrlServiceConfigs(...)` or
`getCloudCtrlServiceConfigs(...)` enriches this two-field mapping internally, or
whether the runtime `serviceId` is created only by a separate
`nativeStartAdvertising` / `nativeRegisterIotService` path.
