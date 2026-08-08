# MiPlay Continuity DeviceService query boundary

This note is offline-only static evidence from Mi Connect Service 5.1.251.10.
No S12/device network operation was performed.

## Java DeviceService path

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/service/DeviceService.java`
  - `getDeviceInfo(deviceId, receiver)` requires non-null `deviceId` and
    `ResultReceiver`, calls
    `DeviceManagerNative.nativeGetDeviceInfo(Binder.getCallingUid(), deviceId)`,
    converts `DeviceInfoV2` to `DeviceInfo`, and sends the result through
    `ResultReceiver`.
  - `getDeviceInfoV2(deviceId, receiver)` uses the same native call but returns
    `DeviceInfoV2` without conversion.
  - `getServiceList(deviceId, receiver)` calls
    `DeviceManagerNative.nativeGetServiceList(Binder.getCallingUid(), deviceId)`
    and returns a string-array list through `ResultReceiver`.
  - Successful `DeviceService` responses use Bundle key `result`; failures use
    Bundle key `message`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/service/NetBusService.java`
  - Its generic callback helper uses Bundle key `data`, which is a different
    shape from `DeviceService.handleResult`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/DeviceManagerNative.java`
  - `nativeGetDeviceInfo(int uid, String deviceId)` returns
    `Result<DeviceInfoV2>`.
  - `nativeGetServiceList(int uid, String deviceId)` returns
    `Result<String[]>`.
- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/netbus/Result.java`
  - success is `errorCode == 0`;
  - failure carries `errorCode` and `message`.

## Native evidence

`llvm-nm -D --demangle artifacts/apk-static/mi-connect-5.1.251.10-native/libmicontinuity.so`
shows:

- `0x882550` `Java_com_xiaomi_continuity_netbus_DeviceManagerNative_nativeGetDeviceInfo`
- `0x8834A4` `Java_com_xiaomi_continuity_netbus_DeviceManagerNative_nativeGetServiceList`

`llvm-objdump` shows the `nativeGetDeviceInfo` JNI body:

1. rejects missing JNI env/string arguments with error `0x2712`;
2. converts Java `deviceId` to a native string;
3. calls `GetDeviceInfo(uid, deviceId, DeviceInfo*)`;
4. on non-zero result, creates `JniResult(errorCode, GetErrMsg(errorCode))`;
5. on success, calls `JniDeviceInfoV2::NativeToJava(...)` and then
   `JniResult::Create(JNIEnv, jobject)`.

The `nativeGetServiceList` body follows the same uid/deviceId boundary, calls
`GetServiceList`, converts native service ids to Java strings, releases the
native service-list buffer, and returns a `JniResult` object.

`llvm-strings -td artifacts/apk-static/mi-connect-5.1.251.10-native/libmicontinuity.so`
contains:

- `0xFE8B6` `GetDeviceInfo`
- `0xFEA01` `GetServiceList`
- `0x145750` `JniDeviceInfoV2::NativeToJava`
- `0x1462B2` `Java_com_xiaomi_continuity_netbus_DeviceManagerNative_nativeGetDeviceInfo`
- `0x146592` `Java_com_xiaomi_continuity_netbus_DeviceManagerNative_nativeGetServiceList`

## DeviceInfo shape

`DeviceInfo` carries at least these base fields:

1. `deviceType`
2. `deviceName`
3. `deviceId`
4. `uidHash`
5. `groupId`
6. `noGroupId`
7. `discMediumTypes`
8. `connMediumTypes`
9. `isCutOff`
10. `capability`
11. `capabilityV2`
12. `capabilityV3`

`DeviceInfoV2` local parcel version is `4` and adds:

1. `screenState`
2. `wifiSwitch`
3. `wifiConnectStatus`
4. `bleSwitch`
5. `lyraSwitch`

## Updated hypothesis

The APK 5.1.251.10 `DeviceService.getDeviceInfo/getServiceList` path is a
Binder/native ResultReceiver query. It is not the legacy TCP 8899
`0x001e -> 0x001f` SafetyData command pair. Therefore a future S12 probe should
not send more 8899 frames merely because the Continuity API has a
`getDeviceInfo` method name. To make a 8899 read-only recheck worthwhile, the
offline evidence still needs a bridge from the authenticated TCP command session
to the same source/device/listener context that official Android obtains from
Binder UID, native `deviceId`, DeviceInfoV2, and service-list state.
