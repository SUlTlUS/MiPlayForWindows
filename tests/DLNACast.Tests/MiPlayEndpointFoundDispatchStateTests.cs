using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayEndpointFoundDispatchStateTests
{
    [Fact]
    public void ConstantsMatchJadxDiscoveryBroadcastAndStaticWakeupEvidence()
    {
        Assert.Equal(2, MiPlayEndpointFoundDispatchState.LegacyMiPlayAppId);
        Assert.Equal(5, MiPlayEndpointFoundDispatchState.MdnsMiPlayAudioApplicationId);
        Assert.False(MiPlayEndpointFoundDispatchState.LegacyMiPlayAppIdIsMdnsMiPlayAudioApplicationId());

        Assert.Equal(
            "com.xiaomi.mi_connect_service.mi_play_endpoint_found",
            MiPlayEndpointFoundDispatchState.LegacyMiPlayEndpointFoundAction);
        Assert.Equal(
            "com.xiaomi.mi_connect_service.permission.RECEIVE_ENDPOINT",
            MiPlayEndpointFoundDispatchState.ReceiveEndpointPermission);
        Assert.Equal(
            "com.xiaomi.mi_connect_service.action.STATIC_CONFIG_ACTION",
            MiPlayEndpointFoundDispatchState.StaticConfigAction);
        Assert.Equal(
            "com.xiaomi.permission.STATIC_BIND_SERVICE_BASED_ON_IDM",
            MiPlayEndpointFoundDispatchState.StaticBindServiceBasedOnIdmPermission);

        Assert.Equal("wakeUpEvent", MiPlayEndpointFoundDispatchState.WakeUpEventExtra);
        Assert.Equal("notifyBean", MiPlayEndpointFoundDispatchState.NotifyBeanExtra);
        Assert.Equal("endpoint_found", MiPlayEndpointFoundDispatchState.EndpointFoundWakeUpValue);
        Assert.Equal("endpoint_connected", MiPlayEndpointFoundDispatchState.EndpointConnectedWakeUpValue);
    }

    [Fact]
    public void ExtrasMatchScreenCastingDataNotifyBeanAndStaticConfigEvidence()
    {
        Assert.Equal("mac", MiPlayEndpointFoundDispatchState.ExtraMac);
        Assert.Equal("disctype", MiPlayEndpointFoundDispatchState.ExtraDiscoveryType);
        Assert.Equal("rssi", MiPlayEndpointFoundDispatchState.ExtraRssi);
        Assert.Equal("name", MiPlayEndpointFoundDispatchState.ExtraName);
        Assert.Equal("idhash", MiPlayEndpointFoundDispatchState.ExtraIdHash);
        Assert.Equal("cmd", MiPlayEndpointFoundDispatchState.ExtraCommand);
        Assert.Equal("wired_mac", MiPlayEndpointFoundDispatchState.ExtraWiredMac);
        Assert.Equal("bt_mac", MiPlayEndpointFoundDispatchState.ExtraBluetoothMac);
        Assert.Equal("adv", MiPlayEndpointFoundDispatchState.ExtraAdv);
        Assert.Equal("verifystatus", MiPlayEndpointFoundDispatchState.ExtraVerifyStatus);
        Assert.Equal(1, MiPlayEndpointFoundDispatchState.ScreenCastingDefaultCommand);
    }

    [Fact]
    public void NativeEndpointAndServiceOffsetsMatchStaticStringEvidence()
    {
        Assert.Equal(0x3B21, MiPlayEndpointFoundDispatchState.NativeAppMgrOnEndpointFoundJniStringOffset);
        Assert.Equal(0x3B58, MiPlayEndpointFoundDispatchState.NativeAppMgrOnEndpointLostJniStringOffset);
        Assert.Equal(0x1A76BE, MiPlayEndpointFoundDispatchState.NativeOnServiceFoundStringOffset);
        Assert.Equal(0x1A76D3, MiPlayEndpointFoundDispatchState.NativeOnServiceLostStringOffset);
        Assert.Equal(0x1A76EC, MiPlayEndpointFoundDispatchState.NativeOnServiceConnectStatusStringOffset);
        Assert.Equal(0x1AD894, MiPlayEndpointFoundDispatchState.NativeMiPlayAudioUrnStringOffset);
        Assert.Equal(0x1B127D, MiPlayEndpointFoundDispatchState.NativeOnEndpointFoundStringOffset);
        Assert.Equal(0x1B1391, MiPlayEndpointFoundDispatchState.NativeOnEndpointLostStringOffset);
        Assert.Equal(0x1C2337, MiPlayEndpointFoundDispatchState.NativeIpcParamOnServiceFoundStringOffset);
        Assert.Equal(0x1C234F, MiPlayEndpointFoundDispatchState.NativeIpcParamOnServiceLostServiceIdStringOffset);
        Assert.Equal(0x1C23B6, MiPlayEndpointFoundDispatchState.NativeIpcParamOnServiceConnectionStatusStringOffset);
    }

    [Fact]
    public void LegacyMiPlayBroadcastRequiresEndpointAdvBleOrNfcAppIdAndIntentConfig()
    {
        var accepted = MiPlayEndpointFoundDispatchState.EvaluateLegacyMiPlayBroadcast(
            new MiPlayEndpointFoundBroadcastPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Nfc,
                AdvAppsContainLegacyMiPlayAppId: true,
                LegacyMiPlayIntentConfigPresent: true));

        Assert.True(accepted.CanProceed);
        Assert.True(MiPlayEndpointFoundDispatchState.IsBleOrNfc(MiConnectDiscoveryType.Ble));
        Assert.True(MiPlayEndpointFoundDispatchState.IsBleOrNfc(MiConnectDiscoveryType.Nfc));
        Assert.False(MiPlayEndpointFoundDispatchState.IsBleOrNfc(MiConnectDiscoveryType.IpBonjour));

        var missingAppId = MiPlayEndpointFoundDispatchState.EvaluateLegacyMiPlayBroadcast(
            new MiPlayEndpointFoundBroadcastPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Nfc,
                AdvAppsContainLegacyMiPlayAppId: false,
                LegacyMiPlayIntentConfigPresent: true));
        var ipBonjour = MiPlayEndpointFoundDispatchState.EvaluateLegacyMiPlayBroadcast(
            new MiPlayEndpointFoundBroadcastPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.IpBonjour,
                AdvAppsContainLegacyMiPlayAppId: true,
                LegacyMiPlayIntentConfigPresent: true));
        var missingIntentConfig = MiPlayEndpointFoundDispatchState.EvaluateLegacyMiPlayBroadcast(
            new MiPlayEndpointFoundBroadcastPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Nfc,
                AdvAppsContainLegacyMiPlayAppId: true,
                LegacyMiPlayIntentConfigPresent: false));

        Assert.False(missingAppId.CanProceed);
        Assert.False(ipBonjour.CanProceed);
        Assert.False(missingIntentConfig.CanProceed);
    }

    [Fact]
    public void StaticConfigEndpointFoundWakeupRequiresPermissionResidentScanDeviceTypeAndVerificationWhenNeeded()
    {
        var accepted = MiPlayEndpointFoundDispatchState.EvaluateStaticConfigEndpointFoundWakeup(
            new MiPlayEndpointFoundStaticWakeupPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Ble,
                StaticConfigPresentForAppId: true,
                StaticConfigServicePermissionGranted: true,
                ResidentBackgroundScanEnabled: true,
                EndpointDeviceTypeAllowed: true,
                SameAccountRequired: true,
                EndpointIdentityVerified: true));

        Assert.True(accepted.CanProceed);

        var missingPermission = MiPlayEndpointFoundDispatchState.EvaluateStaticConfigEndpointFoundWakeup(
            new MiPlayEndpointFoundStaticWakeupPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Ble,
                StaticConfigPresentForAppId: true,
                StaticConfigServicePermissionGranted: false,
                ResidentBackgroundScanEnabled: true,
                EndpointDeviceTypeAllowed: true,
                SameAccountRequired: false,
                EndpointIdentityVerified: false));
        var sameAccountNotVerified = MiPlayEndpointFoundDispatchState.EvaluateStaticConfigEndpointFoundWakeup(
            new MiPlayEndpointFoundStaticWakeupPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.Ble,
                StaticConfigPresentForAppId: true,
                StaticConfigServicePermissionGranted: true,
                ResidentBackgroundScanEnabled: true,
                EndpointDeviceTypeAllowed: true,
                SameAccountRequired: true,
                EndpointIdentityVerified: false));
        var ipBonjour = MiPlayEndpointFoundDispatchState.EvaluateStaticConfigEndpointFoundWakeup(
            new MiPlayEndpointFoundStaticWakeupPrerequisites(
                EndpointProvided: true,
                AdvDataProvided: true,
                AdvAppsProvided: true,
                DiscoveryType: MiConnectDiscoveryType.IpBonjour,
                StaticConfigPresentForAppId: true,
                StaticConfigServicePermissionGranted: true,
                ResidentBackgroundScanEnabled: true,
                EndpointDeviceTypeAllowed: true,
                SameAccountRequired: false,
                EndpointIdentityVerified: false));

        Assert.False(missingPermission.CanProceed);
        Assert.False(sameAccountNotVerified.CanProceed);
        Assert.False(ipBonjour.CanProceed);
    }

    [Fact]
    public void EndpointFoundPayloadDoesNotContainPostAuthSessionContext()
    {
        var screenCastingPayload = new MiPlayEndpointFoundPayloadShape(
            IncludesMac: true,
            IncludesDiscoveryType: true,
            IncludesRssi: true,
            IncludesName: true,
            IncludesIdHash: true,
            IncludesCommand: true,
            IncludesWiredMac: true,
            IncludesBluetoothMac: true,
            IncludesAdvData: false,
            IncludesVerifyStatus: false,
            IncludesWakeUpEvent: false,
            IncludesNotifyBean: false,
            IncludesServiceId: false,
            IncludesConnectionId: false,
            IncludesChannelId: false,
            IncludesTransKey: false,
            IncludesSafetyDataSession: false);
        var staticWakeupPayload = new MiPlayEndpointFoundPayloadShape(
            IncludesMac: true,
            IncludesDiscoveryType: true,
            IncludesRssi: true,
            IncludesName: true,
            IncludesIdHash: true,
            IncludesCommand: false,
            IncludesWiredMac: false,
            IncludesBluetoothMac: false,
            IncludesAdvData: true,
            IncludesVerifyStatus: true,
            IncludesWakeUpEvent: true,
            IncludesNotifyBean: true,
            IncludesServiceId: false,
            IncludesConnectionId: false,
            IncludesChannelId: false,
            IncludesTransKey: false,
            IncludesSafetyDataSession: false);

        Assert.False(MiPlayEndpointFoundDispatchState.EndpointFoundPayloadContainsPostAuthSessionContext(screenCastingPayload));
        Assert.False(MiPlayEndpointFoundDispatchState.EndpointFoundPayloadContainsPostAuthSessionContext(staticWakeupPayload));
        Assert.False(MiPlayEndpointFoundDispatchState.EndpointFoundCanExplainLegacyTcpGetDeviceInfoOnSuccess(
            endpointFoundDelivered: true,
            payloadShape: screenCastingPayload));
        Assert.False(MiPlayEndpointFoundDispatchState.EndpointFoundCanExplainLegacyTcpGetDeviceInfoOnSuccess(
            endpointFoundDelivered: true,
            payloadShape: staticWakeupPayload));
    }
}
