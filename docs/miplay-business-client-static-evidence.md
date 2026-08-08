# MiPlay business-client static evidence gate

Date: 2026-07-20

Scope: offline static analysis only. No S12/device network operation was
performed.

## Why this gate exists

SafetyAuth is now verified, but the post-auth `0x001e -> 0x001f`
`getDeviceInfo` read-only reprobe did not receive a qualifying response. The
static APK evidence keeps pointing to a missing source-side context rather than
to a missing SafetyAuth primitive:

- source package/service identity,
- Continuity `AppInfo`/permission context,
- discovery-delivered `DeviceInfoV2` or `deviceId`,
- connection/channel listener state,
- and a proven bridge, if any, from that state into legacy TCP 8899 commands.

`MiPlayBusinessClientStaticEvidence` captures those requirements as pure
offline tests so that the next network decision is not based on a loose API name
match.

## Current Mi Connect Service APK evidence

The current local artifact is:

`artifacts/apk-static/mi-connect-5.1.251.10-jadx`

Positive platform-side evidence:

- `sources/com/xiaomi/continuity/service/BuildConfig.java`
  - `LIBRARY_PACKAGE_NAME = "com.xiaomi.continuity.service"`
  - `FLAVOR = "fullCn"`
  - `VERSION_NAME = "5.1.251.10.fullCnRelease.0616209"`
- `sources/com/xiaomi/continuity/staticmanager/staticdisc/DiscFilterParser.java`
  - `RESOURCE_KEY = "static_disc_filter"`
  - `ROOT_TAG = "disc_filters"`
  - attributes include `serviceId` and `mediumTypes`
- `sources/com/xiaomi/continuity/staticmanager/staticdisc/DiscStaticConfigProcess.java`
  - static discovery dispatches `ACTION_NETBUS_DISC_DEVICE_FOUND`,
    `ACTION_NETBUS_DISC_DEVICE_CHANGED`, `ACTION_NETBUS_DISC_DEVICE_LOST`, and
    `ACTION_NETBUS_DISC_RECEIVE_DATA`
  - it checks `BIND_CONTINUITY_SERVICE_INTERNAL`, calls
    `NetBusManagerNative.nativeRegisterService`, and starts discovery through
    `NetBusManagerNative.nativeStartDiscovery`
- `sources/com/xiaomi/continuity/ContinuityConnectionManagerService.java`
  - exposes `createChannelV2(...)`,
    `createChannelbyAddressV2(...)`, and `registerChannelListenerV2(...)`

Negative business-client evidence from the current decoded artifact:

- no decoded `AndroidManifest.xml` file was present,
- `resources/` contained no XML files,
- resource searches found no business XML hit for `static_disc_filter`,
  `00017803`, or `miplay-audio`.

So this APK currently proves the Continuity/NetBus platform framework and
parsers, but it does not identify the MiPlay business client that registers or
receives the MiPlay audio service context.

## Captured testable hypotheses

The new offline model treats the current Mi Connect Service artifact as
`ContinuityPlatformService`, not as `BusinessClient`.

A future artifact can pass the business-client static gate only if it proves all
of these:

1. decoded manifest and decoded business resources are available;
2. a source package identity is known;
3. a `static_disc_filter` or `static_networking_service_list` declaration is
   present, or an explicit `registerDiscoveryListener(V2)` call is observed;
4. the MiPlay service id normalizes to `00017803`;
5. there is a NETBUS discovery receiver path or explicit discovery listener;
6. the business side receives/holds `DeviceInfoV2` or `deviceId`;
7. connection or channel listener registration is tied to the same business
   context.

Even after that passes, a legacy TCP 8899 `getDeviceInfo` reprobe remains
unjustified unless a separate bridge is proven from the official business
context into that exact command-session path.

## Current conclusion

Another S12 reprobe is not worth doing yet. The next useful offline target is a
business/client APK or module that declares the MiPlay static discovery or
networking config, receives `NETBUS_DISC_*`, or calls
`registerDiscoveryListenerV2`, `requestConnection`, `createChannelV2`, or
`registerChannelListenerV2` with the MiPlay audio identity.
