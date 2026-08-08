# MiPlay Continuity channel-listener state boundary

This note is offline-only static evidence from Mi Connect Service 5.1.251.10.
No S12/device network operation was performed.

## Java registration path

- `artifacts/apk-static/mi-connect-5.1.251.10-jadx/sources/com/xiaomi/continuity/ContinuityConnectionManagerService.java`
  - `registerChannelListener(...)` wraps old `ServerChannelOptions` into
    `ServerChannelOptionsV2`, then calls `registerChannelListenerV2(...)`.
  - `registerChannelListenerV2(...)` requires non-null `ServiceName`,
    `ServerChannelOptionsV2`, and `IChannelInnerListener`.
  - It reads `Binder.getCallingUid()` / `Binder.getCallingPid()`, checks
    `BIND_CONTINUITY_SERVICE_INTERNAL`, generates `AppInfo` from the Binder
    caller, creates `ChannelListenerServerProxy`, inserts a weak reference into
    `mServiceListeners`, and calls
    `ContinuityConnectionManagerNative.nativeRegisterChannelListener(serviceName.toMergeString(), appInfo, serverChannelOptionsV2, proxy)`.
  - If native registration returns non-zero, the Java map insertion is reverted
    and the listener death recipient is unlinked. A synchronous native success
    therefore means only “registered”; it is not yet a channel-created callback.

## Listener callback path

- `ChannelListenerServerProxy` links the listener Binder death recipient and
  unregisters on listener death.
- `ChannelListenerProxy.onChannelCreated(deviceId, serviceNameString, ChannelInfoV2)`
  - logs the `ChannelInfoV2`;
  - records the channel id under the caller/service mapping;
  - if `channel.SDK_SUPPORT_USER_SECURITY_KEY` is present in
    `IChannelInnerListener.getFeatures()`, calls
    `IChannelInnerListener.onChannelCreatedV2(...)`;
  - otherwise converts to legacy `ChannelInfo` and calls
    `IChannelInnerListener.onChannelCreated(...)`;
  - calls `ChannelInfoV2.WipeTransKey()` after listener dispatch.
- `IChannelInnerListener` Binder transaction ids:
  - `getFeatures = 8`;
  - `onChannelCreated = 2`;
  - `onChannelCreatedV2 = 13`.

## ChannelInfoV2 shape

`ChannelInfoV2` local parcel version is `2`. Its relevant fields are:

- `channelId`
- `peerChannelId`
- `deviceId`
- `serviceName`
- `address`
- `port`
- `channelRole`
- `isSdkSocket`
- `localAddress`
- `transKey`
- `userSecretKey`
- `deviceType`
- `tunnelInfo`
- `mediumType`

`transKey` and `userSecretKey` are encoded only when their length is non-zero.
The parcel reader treats length `32` as a real key length; otherwise it falls
back to an empty byte array. `WipeTransKey()` overwrites the byte array after
callback dispatch.

## Native evidence

`llvm-nm -D --demangle artifacts/apk-static/mi-connect-5.1.251.10-native/libmicontinuity.so`
shows:

- `0x89D5B8` `Java_com_xiaomi_continuity_nativelib_ContinuityConnectionManagerNative_nativeRegisterChannelListener`
- `0x89D90C` `Java_com_xiaomi_continuity_nativelib_ContinuityConnectionManagerNative_nativeUnregisterChannelListener`

`llvm-strings -td artifacts/apk-static/mi-connect-5.1.251.10-native/libmicontinuity.so`
contains:

- `0xFFB16` `RegisterChannelListener`
- `0xFFB9D` `UnregisterChannelListener`
- `0x14CEBB` `JniChannelListener::OnChannelCreated`
- `0x14D5AA` `Java_com_xiaomi_continuity_nativelib_ContinuityConnectionManagerNative_nativeRegisterChannelListener`
- `0x14D60F` `JniServerChannelOptions::JavaToNative`
- `0x14D6CE` `JniChannelListener::AddServerChannelListener`
- `0x14D71F` `JniChannelListener::RevertServerChannelListener`
- `0x14D771` `JniChannelListener::DeleteStoreServerChannelListener`
- `0x14D7C8` `Java_com_xiaomi_continuity_nativelib_ContinuityConnectionManagerNative_nativeUnregisterChannelListener`
- `0x4D972F` `ChannelHandler::SetTransKey`

## Updated state model

`src/DLNACast.Core/MiPlay/MiPlayContinuityChannelListenerState.cs` separates
two gates:

1. listener registration gate:
   `ServiceName`, `ServerChannelOptionsV2`, `IChannelInnerListener`, internal
   permission, Binder-derived `AppInfo`, death-recipient link, weak-reference
   map insertion, and native registration success;
2. channel-created gate:
   registered listener still present, native `onChannelCreated` arrival,
   matching `ServiceName`, non-zero `channelId`, non-empty `deviceId`, non-empty
   `mediumType`, and if `SDK_SUPPORT_USER_SECURITY_KEY` is supported then
   `ChannelInfoV2` plus pre-dispatch `transKey`; finally the key must be wiped
   after dispatch.

The model intentionally refuses to treat Continuity `onChannelCreated` as the
legacy TCP 8899 `DealSafetyDone -> CmdClientCallback.onSuccess ->
getDeviceInfo` chain. It is useful evidence for the newer Lyra/Continuity
channel, but it does not authorize replaying more 8899 frames after SafetyAuth.

## Implication for the S12 probe

The current Windows Probe can prove mutual SafetyAuth, but it does not have the
official Android Binder caller identity, `ServiceName`, `ServerChannelOptionsV2`
/ `BusinessProfile`, listener proxy registration, native channel-created
callback, `ChannelInfoV2.deviceId`, medium type, or channel transKey. Repeating
the same post-auth `0x001e` read-only 8899 probe would not supply any of those
states. The next useful offline target is to map the MiPlay audio service name
and BusinessProfile options used by the actual Android source app, not to send
another network frame.
