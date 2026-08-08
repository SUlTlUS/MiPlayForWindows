# MiPlay audio Continuity channel profile boundary

Date: 2026-07-20

Scope: offline static analysis only. No S12 network traffic was sent while
building this model.

## Evidence

- `libidmservicemgr.so` contains the IDM MiPlay audio service type
  `urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0` at string
  offset `0x1AD894`, near `SMGR_ServiceTypeIds` at `0x1ADA0D`.
- `libmicontinuity.so` contains the Continuity channel listener API strings
  (`RegisterChannelListener`, JNI `nativeRegisterChannelListener`) and generic
  `lyra::netbus::mpt::MiplayTransport*` symbols, but the searched native string
  set has zero hits for `00017803` and `miplay-audio`.
- JADX `com.xiaomi.continuity.ServiceName` is a Continuity channel identity with
  `packageName` and `name`; `toMergeString()` emits either `:<name>` or
  `<packageName>:<name>`. Its parser accepts colon-separated strings by using
  only the first two split fields, so parsing an IDM URN shape is not proof that
  the URN is a valid channel `ServiceName`.
- JADX `BusinessProfile.attachTo(ServerChannelOptionsV2)` writes channel optional
  values only when `scenario` is non-empty; otherwise it returns without adding
  anything to the server channel options.
- The only direct Java `new BusinessProfile(...)` use found in Mi Connect Service
  5.1.251.10 is `new BusinessProfile("LinkResMgr")` in `LyraQosUtils`, which is
  a link-resource-manager/QoS path, not a proven MiPlay audio channel profile.
- `IContinuityConnectionManager.TRANSACTION_registerChannelListenerV2 = 18`.
  `ContinuityConnectionManagerService.registerChannelListenerV2(...)` requires a
  non-null `ServiceName`, non-null `ServerChannelOptionsV2`, non-null inner
  listener, internal bind permission, caller `AppInfo`, listener map insertion,
  and native registration success.

## Testable hypotheses captured in code

- `MiPlayContinuityMiplayAudioChannelProfile.CanUseIdmServiceTypeAsContinuityServiceName`
  is always false for the observed MiPlay audio IDM service type until a separate
  Continuity channel `ServiceName` mapping is proven.
- `EvaluateServerBusinessProfileAttach` requires both `ServerChannelOptionsV2`
  and a non-empty `BusinessProfile.scenario`, matching the APK's server-side
  `attachTo` guard.
- Generic `MiplayTransport*` native symbols are not enough to identify the
  MiPlay audio channel profile unless there is also a ServiceName mapping and a
  BusinessProfile mapping tied to that ServiceName.
- Current Mi Connect Service evidence is therefore sufficient for an IDM service
  type assertion, but insufficient for constructing or validating the post-auth
  Continuity channel listener profile needed to explain why legacy TCP `0x001e`
  still receives no `0x001f`.

## Current conclusion

Another S12 probe is not justified from this evidence alone. The next offline
target should be the actual client/app side that calls
`registerChannelListenerV2` or provides the MiPlay audio channel
`ServiceName`/`BusinessProfile`; Mi Connect Service 5.1.251.10 appears to expose
the infrastructure but not the complete MiPlay audio profile binding.
