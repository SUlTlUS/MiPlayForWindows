# Java onSuccess / getDeviceInfo boundary

Date: 2026-07-20

Scope: offline static analysis only. No S12/device network operation was
performed.

## What was checked

The goal was to avoid conflating three similarly named things:

1. generic Java `AsyncResult.onSuccess(...)` callbacks;
2. NetBus `DeviceService.getDeviceInfo/getDeviceInfoV2` Binder APIs;
3. the legacy TCP 8899 SafetyData command pair `0x001e -> 0x001f`.

The current Mi Connect Service JADX source was searched for:

- `DealSafetyDone`, `SafetyDone`, `SafetyAuth`,
- `getDeviceInfo`,
- `onSuccess`,
- connection/channel listener APIs.

## Positive evidence

### Generic onSuccess exists

`sources/com/xiaomi/continuity/netbus/AsyncResult.java` defines a generic
`OnSuccessListener<T>` and dispatches it when `success(T)` marks the result as
completed and successful. This is a framework callback shape, not a MiPlay
SafetyAuth-specific callback by itself.

### NetBus DeviceService getDeviceInfo exists

`sources/com/xiaomi/continuity/netbus/service/DeviceService.java` implements:

- `getDeviceInfo(String, ResultReceiver)`;
- `getDeviceInfoV2(String, ResultReceiver)`;
- both call `DeviceManagerNative.nativeGetDeviceInfo(Binder.getCallingUid(),
  deviceId)` and return through a `ResultReceiver` bundle using key `result`.

`sources/com/xiaomi/continuity/netbus/IDeviceService.java` assigns Binder
transactions:

- `TRANSACTION_getDeviceInfo = 1`;
- `TRANSACTION_getDeviceInfoV2 = 10`.

### Discovery callbacks can already provide DeviceInfo

`sources/com/xiaomi/continuity/netbus/service/NetBusService.java` forwards
native `DeviceInfoV2` to the registered business listener through
`onDeviceFoundV2` when supported, or converts it to legacy `DeviceInfo` via
`deviceInfoV2.getDeviceInfo()`.

This reinforces the earlier conclusion: official source code can often obtain
device context from discovery callbacks before any explicit DeviceService query.

## Negative evidence

No Java source hit in the current Mi Connect Service artifact proves:

- `DealSafetyDone` or `SafetyDone`;
- a SafetyAuth success callback that schedules `DeviceService.getDeviceInfo`;
- an `onSuccess` handler whose body calls
  `IDeviceService.getDeviceInfo/getDeviceInfoV2`;
- a bridge from the Binder `DeviceService` query into legacy TCP 8899 command
  `0x001e`.

Several `getDeviceInfo` hits are unrelated wearable, notification, or model
accessors. They should not be used as MiPlay post-auth evidence.

## Captured testable hypotheses

`MiPlayJavaOnSuccessGetDeviceInfoBoundary` now encodes:

1. `AsyncResult.onSuccess` is only a generic dispatch boundary unless a concrete
   call site ties it to SafetyAuth and `DeviceService.getDeviceInfo`.
2. `DeviceService.getDeviceInfo/getDeviceInfoV2` is a Binder/ResultReceiver API
   with transaction ids `1` and `10`; that success does not imply TCP 8899
   `0x001e`.
3. A complete Java trace must prove all of:
   - `DealSafetyDone`/SafetyDone symbol or callback,
   - SafetyAuth success callback,
   - Java `onSuccess` callback,
   - `onSuccess -> DeviceService.getDeviceInfo/getDeviceInfoV2`,
   - source caller package identity,
   - discovery-derived `DeviceInfoV2` or `deviceId`,
   - ResultReceiver parsing,
   - explicit legacy TCP 8899 command bridge.

## Current conclusion

The current APK still does not justify another S12 reprobe. The next offline
artifact should be a MiPlay business/client APK or module where the concrete
`onSuccess` implementation is available; the platform service alone only
provides Binder and NetBus framework boundaries.
