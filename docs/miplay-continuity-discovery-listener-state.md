# Continuity discovery listener state and DeviceInfo callback boundary

Date: 2026-07-20

Scope: offline static analysis only. No S12/device network operation was
performed.

## Why this path was checked

The previous endpoint-found pass showed that
`com.xiaomi.mi_connect_service.mi_play_endpoint_found` and static
`wakeUpEvent=endpoint_found` are BLE/NFC discovery notifications, not
post-SafetyAuth `getDeviceInfo` callbacks. The remaining question was whether
Mi Connect Service exposes another official path where `onSuccess` or a
listener obtains the device context that our 8899 Probe lacks.

## NetBus discovery listener evidence

- `NetBusService.registerDiscoveryListener/registerDiscoveryListenerV2` calls a
  shared `registerDiscoveryListenerInner(...)` path. It requires non-null
  Binder token, `serviceId`, `IDiscoveryListener`, and `ResultReceiver`, checks
  `com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL`, and then calls
  `NetBusManagerNative.nativeRegisterDiscoveryListener(...)`.
- `NetBusService.startDiscovery/startDiscoveryV2` similarly requires
  `StartDiscoveryOptions(V2)`, permission for the target `serviceId`, and calls
  `NetBusManagerNative.nativeStartDiscovery(...)`.
- `DiscoveryListenerImpl` links the supplied `IDiscoveryListener` Binder to
  death. On Binder death or remote exception, it unregisters the native
  discovery listener.
- On native `onDeviceFound`, `DiscoveryListenerImpl` forwards `DeviceInfoV2`
  through `onDeviceFoundV2` when the business listener supports feature
  `device.DEVICE_INFO_V2`; otherwise it converts to legacy `DeviceInfo` and
  calls `onDeviceFound`.
- On native `onDeviceInfoChanged`, legacy listeners suppress mask `0x200` when
  it is the only change and otherwise clear bit `0x200` before forwarding.
  V2-capable listeners receive the V2 callback and original mask.

This means a real official source can obtain a `deviceId` and full
`DeviceInfoV2` without calling `DeviceService.getDeviceInfo` at all: it can be
pushed by the native discovery listener after a registered service starts
discovery.

## Static discovery config evidence

- `DiscFilterParser` loads business-package metadata key `static_disc_filter`.
  The XML root is `disc_filters`, with `filter` entries.
- Each filter uses fields including `serviceId`, `mediumTypes`, `dataType`,
  `discSameAccount`, `discSameGroup`, `discSameP2PGroup`, `rangeGear`,
  `workWhenScreenOff`, `privacySecurity`, `extFlag`, `autoScanPeriod`, and
  `switch`.
- `serviceId` must be non-empty and at most 8 characters; the parser pads it to
  8 characters with leading zeroes, so `17803` becomes `00017803`.
- `mediumTypes` maps `MDNS`, `BLE`, `BLE_APPLE`/`bleApple`, `NFC`, and
  `WIFI_AWARE`/`wifiaware`; empty or unrecognized values default to MDNS.
- `DiscStaticConfigProcess.onPackageAdded` requires the business service to be
  enabled and to pass `BIND_CONTINUITY_SERVICE_INTERNAL`. On proxy enable it
  builds package-signature `AppInfo`, calls `nativeRegisterService`, registers
  a `DiscoveryListenerProxy`, stops stale discovery, and starts discovery.
- Static discovery callbacks are delivered to the business component as
  `com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_FOUND`,
  `NETBUS_DISC_DEVICE_CHANGED`, `NETBUS_DISC_DEVICE_LOST`, or
  `NETBUS_DISC_RECEIVE_DATA`, carrying service/device `DeviceInfo` extras.

## Native string evidence

From `libmicontinuity.so` strings:

- `StartDiscovery`: `0xFEFF5`
- `RegisterDiscoveryListener`: `0xFF14D`
- `UnregisterDiscoveryListener`: `0xFF1B8`
- `JniDiscoveryListener::OnDeviceFound`: `0x14759B`
- `JniDiscoveryListener::OnDeviceLost`: `0x1477DC`
- `JniDiscoveryListener::OnDeviceInfoChanged`: `0x147827`
- `JniDiscoveryListener::OnDevicePositionChanged`: `0x14787A`
- `JniDiscoveryListener::OnReceiveData`: `0x14791C`
- JNI `nativeStartDiscovery`: `0x1490D8`
- JNI `nativeStopDiscovery`: `0x1491E4`
- JNI `nativeRegisterDiscoveryListener`: `0x1492E4`
- JNI `nativeUnregisterDiscoveryListener`: `0x14933A`
- `DiscoveryDataNormal::NotifyOnDeviceFound`: `0x185F0F`
- `DiscoveryDataNormal::NotifyOnDeviceInfoChanged`: `0x18615A`
- symbolized `JniDiscoveryListener::OnDeviceFound`: `0x5B64B0`
- symbolized JNI `nativeRegisterDiscoveryListener`: `0x5B6619`

## Current conclusion

This path explains a missing source-side device-context mechanism: an official
business MiPlay client likely does not need to probe TCP 8899 for device info
immediately after SafetyAuth. It can register NetBus discovery for service id
`00017803`/related MiPlay service id, receive native `DeviceInfoV2`, and then
use that `deviceId`/service context for later connection or channel operations.

It still does not explain our legacy TCP `0x001e -> 0x001f` gap. The listener
callback path is Binder/native NetBus state, not the SafetyData command pair.
Therefore another S12 8899 read-only reprobe is still not justified by the
current evidence.

## Testable hypotheses

1. A real official source trace should show a business package with
   `static_disc_filter` or an explicit `registerDiscoveryListenerV2` call before
   it obtains `DeviceInfoV2` for an S12.
2. The service id should be normalized to an 8-character hex-ish string such as
   `00017803`, while the mDNS URN remains
   `urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0`.
3. If the receiving business listener supports `device.DEVICE_INFO_V2`, it
   should receive `onDeviceFoundV2`; otherwise it receives legacy `DeviceInfo`
   and loses V2-only change bit `0x200`.
4. The next offline target should be the MiPlay business/client APK or module
   that declares `static_disc_filter` or receives
   `NETBUS_DISC_DEVICE_FOUND`, then registers the connection/channel listener.
