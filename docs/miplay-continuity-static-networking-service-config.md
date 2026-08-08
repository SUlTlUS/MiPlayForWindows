# Static networking-service config and NotifyConnect boundary

Date: 2026-07-20

Scope: offline static analysis only. No S12 network traffic was sent while
building this model.

## APK evidence

- `NetworkingServiceParser` looks for Android `ServiceInfo.metaData` key
  `static_networking_service_list`. The referenced XML root is
  `networking_service_list`, with `service` entries using attributes such as
  `serviceName`, `serviceData`, `notifyConnect`, `needAddService`,
  `trustLevel`, `syncCloud`, `trustedTypes`, and `switch`.
- `ServiceSettingStaticConfigProcess.onPackageLoaded` loads that XML from the
  scanned business package's resources, not from Mi Connect Service's own
  resources. If parsing fails, the package is disabled, or the list is empty,
  existing static service info and notify-connect listener state are removed.
- When `needAddService=true`, the static path builds `BusinessServiceInfo` with
  the business package name, configured `serviceName`, and `serviceData`, then
  publishes it to `DevRepoNativeWrapper.addServiceInfo` once
  `ServiceStaticConfigProxy` is enabled and `PackageUtil` can build `AppInfo`.
- `NotifyConnectHelper.onPackageLoaded` builds a Continuity `ServiceName` as
  `<serviceInfo.packageName>:<networkingServiceConfigInfo.serviceName>`. If the
  package is enabled, `notifyConnect=true`, and
  `com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL` is granted, it calls
  `nativeRegisterServerConnectionInitiationListener` with
  `ServerChannelOptionsV2(trustLevel)`.
- `NotifyConnectHelper.onConnectionInitiated` first ignores callbacks when
  `nativeHasServerConnectionListener(serviceName)` is true. Otherwise it parses
  the native callback `serviceName`, checks the static component map, and sends
  `com.xiaomi.continuity.action.REQUEST_CONNECTION` with extra
  `com.xiaomi.continuity.EXTRA_SERVICE_NAME` only when the incoming
  `ConnectionInfo.trustLevel` is not higher than the configured component
  trust level. Higher-trust requests are rejected with `nativeConfirmConnection`.

## Static native offsets

- `RegisterServerConnectionInitiationListener`: `0x10001A`
- `UnregisterServerConnectionInitiationListener`: `0x1000CA`
- JNI `nativeRegisterServerConnectionInitiationListener`: `0x14DBD6`
- JNI `nativeUnregisterServerConnectionInitiationListener`: `0x14DDC3`
- JNI `nativeHasServerConnectionListener`: `0x14DEEE`

## Trust mapping

- `sameAccount` -> trust level `16`, trusted type `1`
- missing, unknown, or `trustGroup` -> trust level `32`, trusted type `0`
- `sharedAccount` -> trust level `40`, trusted type `8`
- `everyOne` -> trust level `48`, trusted type `0`

## Current conclusion

This path fills an important missing identity boundary: a real MiPlay business
package may provide the Continuity `ServiceName` through
`static_networking_service_list`. However, this is a server-connection
initiation listener and `ACTION_REQUEST_CONNECTION` dispatch path. It is not
`registerChannelListenerV2`, does not supply a MiPlay audio `BusinessProfile`,
and does not explain why the legacy TCP 8899 post-auth `0x001e` probe receives
no `0x001f`.

The next offline target should be the MiPlay client/business APK that owns the
static networking-service XML, not another S12 probe. Useful artifacts would be
an XML entry with `serviceName`, `notifyConnect`, `trustLevel`, and any adjacent
channel `BusinessProfile` or `registerChannelListenerV2` use.
