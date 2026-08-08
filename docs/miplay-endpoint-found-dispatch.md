# MiPlay endpoint-found dispatch boundary

Date: 2026-07-20

Scope: offline static analysis only. No S12 network traffic was sent while
building this model.

## APK evidence

- No Java `DealSafetyDone`/`SafetyDone` string was found in the Mi Connect
  Service 5.1.251.10 jadx output during this pass. The visible Java
  `onSuccess` hits around this area are generic Continuity `AsyncResult` or
  Wi-Fi callbacks, not a SafetyAuth listener that can schedule legacy TCP 8899
  `getDeviceInfo`.
- `com.xiaomi.mi_connect_service.m.a.b(EndPoint, MiConnectAdvData)` is the
  discovery `onEndpointFound` path. After null/flag checks and local app-client
  callbacks, it stores non-Bonjour endpoints and calls
  `com.xiaomi.mi_connect_service.g0.a(endPoint, miConnectAdvData)` for
  background endpoint notification.
- `g0.a(EndPoint, MiConnectAdvData)` only dispatches background endpoint
  notifications for BLE or NFC discovery types. It rejects other discovery
  types before constructing the MiPlay screen-casting broadcast or static
  notify-app payload.
- `AppIdEnum.MI_PLAY` is `2`. This is distinct from the mDNS/IDM MiPlay audio
  application id `5` already modeled in `MiPlayMdnsCapabilities`.
- For the legacy MiPlay special case, `g0.a` constructs `g0.a`
  `ScreenCastingData`; its default action is
  `com.xiaomi.mi_connect_service.mi_play_endpoint_found`, command defaults to
  `1`, and `g0.d` sends the broadcast with
  `com.xiaomi.mi_connect_service.permission.RECEIVE_ENDPOINT`.
- `ConnectTrackerKt.DEVICE_MAC` resolves to extra key `mac`. The legacy MiPlay
  broadcast extras are `mac`, `disctype`, `rssi`, `name`, `idhash`, `cmd`,
  `wired_mac`, and `bt_mac`.
- The static notify-app branch uses `e6.n` to discover services handling
  `com.xiaomi.mi_connect_service.action.STATIC_CONFIG_ACTION` and requires
  service permission `com.xiaomi.permission.STATIC_BIND_SERVICE_BASED_ON_IDM`.
  `e6.o.a` then sends `wakeUpEvent=endpoint_found` and optional `notifyBean`.
  The `NotifyBean` fields are discovery data: mac, discType, adv, rssi, name,
  idhash, and verifyStatus.

## Native evidence

`libidmservicemgr.so` still exposes native endpoint/service callback strings,
but these are endpoint or service discovery callbacks, not post-SafetyAuth TCP
commands:

- `Java_com_xiaomi_idm_AppMgrNative_nativeOnEndpointFound`: `0x3B21`
- `Java_com_xiaomi_idm_AppMgrNative_nativeOnEndpointLost`: `0x3B58`
- `onServiceFound`: `0x1A76BE`
- `onServiceLost`: `0x1A76D3`
- `onServiceConnectStatus`: `0x1A76EC`
- MiPlay audio URN string: `0x1AD894`
- `onEndpointFound`: `0x1B127D`
- `onEndpointLost`: `0x1B1391`
- `IPCParam.OnServiceFound`: `0x1C2337`
- `IPCParam.OnServiceLost.serviceId`: `0x1C234F`
- `IPCParam.OnServiceConnectionStatus`: `0x1C23B6`

## Current conclusion

The `mi_play_endpoint_found` and `endpoint_found` paths are discovery
notification paths. They can wake or notify a MiPlay-capable client app, but
their payloads do not contain the post-auth session context needed to justify
legacy TCP 8899 `getDeviceInfo` success:

- no serviceId
- no connectionId
- no channelId
- no transKey
- no SafetyData session object/state

This makes them useful for explaining how an official client is told that a
candidate MiPlay endpoint exists, but not for explaining why our post-auth
`0x001e` probe received no `0x001f`.

## Testable hypotheses

1. A future official-client trace should show `mi_play_endpoint_found` or static
   `endpoint_found` before any app-owned connection/listener setup, not after
   SafetyAuth as a DealSafetyDone callback.
2. The missing state for S12 post-auth remains app-owned connection context:
   either Continuity `registerChannelListenerV2`/channel-created state or a
   separate private command-session listener that is not visible as
   `mi_play_endpoint_found`.
3. Another S12 read-only reprobe is not worthwhile from this evidence alone.
   The next useful offline artifact is the business MiPlay app or module that
   receives the endpoint-found notification and then registers the channel or
   command-session listener.
