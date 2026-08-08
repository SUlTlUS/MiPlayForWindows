using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityDiscoveryListenerStateTests
{
    [Fact]
    public void StaticDiscConfigConstantsMatchJadxEvidence()
    {
        Assert.Equal("static_disc_filter", MiPlayContinuityDiscoveryListenerState.StaticDiscFilterResourceKey);
        Assert.Equal("static_disc_switch_v3", MiPlayContinuityDiscoveryListenerState.StaticDiscSwitchV3Key);
        Assert.Equal("static_disc_switch_v2", MiPlayContinuityDiscoveryListenerState.StaticDiscSwitchV2Key);
        Assert.Equal("static_disc_switch", MiPlayContinuityDiscoveryListenerState.StaticDiscSwitchKey);
        Assert.Equal("static_enable_switch", MiPlayContinuityDiscoveryListenerState.StaticEnableSwitchKey);
        Assert.Equal("disc_filters", MiPlayContinuityDiscoveryListenerState.StaticDiscRootTag);
        Assert.Equal("filter", MiPlayContinuityDiscoveryListenerState.StaticDiscFilterTag);

        Assert.Equal("serviceId", MiPlayContinuityDiscoveryListenerState.AttributeServiceId);
        Assert.Equal("mediumTypes", MiPlayContinuityDiscoveryListenerState.AttributeMediumTypes);
        Assert.Equal("dataType", MiPlayContinuityDiscoveryListenerState.AttributeDataType);
        Assert.Equal("discSameAccount", MiPlayContinuityDiscoveryListenerState.AttributeDiscSameAccount);
        Assert.Equal("discSameGroup", MiPlayContinuityDiscoveryListenerState.AttributeDiscSameGroup);
        Assert.Equal("discSameP2PGroup", MiPlayContinuityDiscoveryListenerState.AttributeDiscSameP2PGroup);
        Assert.Equal("rangeGear", MiPlayContinuityDiscoveryListenerState.AttributeRangeGear);
        Assert.Equal("workWhenScreenOff", MiPlayContinuityDiscoveryListenerState.AttributeWorkWhenScreenOff);
        Assert.Equal("privacySecurity", MiPlayContinuityDiscoveryListenerState.AttributePrivacySecurity);
        Assert.Equal("extFlag", MiPlayContinuityDiscoveryListenerState.AttributeExtFlag);
        Assert.Equal("autoScanPeriod", MiPlayContinuityDiscoveryListenerState.AttributeAutoScanPeriod);
        Assert.Equal("switch", MiPlayContinuityDiscoveryListenerState.AttributeSwitch);
    }

    [Fact]
    public void StaticDiscNotifyIntentConstantsMatchStaticConfigEvidence()
    {
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_FOUND",
            MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscDeviceFound);
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_CHANGED",
            MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscDeviceChanged);
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_LOST",
            MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscDeviceLost);
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_RECEIVE_DATA",
            MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscReceiveData);

        Assert.Equal(
            "com.xiaomi.continuity.NETBUS_DISC_SERVICE_ID",
            MiPlayContinuityDiscoveryListenerState.ExtraNetbusDiscServiceId);
        Assert.Equal(
            "com.xiaomi.continuity.NETBUS_DISC_DEVICE_ID",
            MiPlayContinuityDiscoveryListenerState.ExtraNetbusDiscDeviceId);
        Assert.Equal(
            "com.xiaomi.continuity.NETBUS_DISC_DEVICE_INFO",
            MiPlayContinuityDiscoveryListenerState.ExtraNetbusDiscDeviceInfo);
        Assert.Equal(
            "com.xiaomi.continuity.NETBUS_DISC_CHANGE_MASK",
            MiPlayContinuityDiscoveryListenerState.ExtraNetbusDiscChangeMask);
        Assert.Equal(
            "com.xiaomi.continuity.NETBUS_DISC_DISCOVERY_DATA",
            MiPlayContinuityDiscoveryListenerState.ExtraNetbusDiscDiscoveryData);
    }

    [Fact]
    public void BinderTransactionsAndNativeOffsetsMatchStaticEvidence()
    {
        Assert.Equal(3, MiPlayContinuityDiscoveryListenerState.NetBusRegisterServiceTransaction);
        Assert.Equal(7, MiPlayContinuityDiscoveryListenerState.NetBusStartDiscoveryTransaction);
        Assert.Equal(10, MiPlayContinuityDiscoveryListenerState.NetBusRegisterDiscoveryListenerTransaction);
        Assert.Equal(25, MiPlayContinuityDiscoveryListenerState.NetBusStartDiscoveryV2Transaction);
        Assert.Equal(28, MiPlayContinuityDiscoveryListenerState.NetBusRegisterDiscoveryListenerV2Transaction);

        Assert.Equal(1, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceFoundTransaction);
        Assert.Equal(2, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceLostTransaction);
        Assert.Equal(3, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceInfoChangedTransaction);
        Assert.Equal(4, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnReceiveDataTransaction);
        Assert.Equal(5, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerHasFeatureTransaction);
        Assert.Equal(6, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceFoundV2Transaction);
        Assert.Equal(7, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceLostV2Transaction);
        Assert.Equal(8, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDeviceInfoChangedV2Transaction);
        Assert.Equal(9, MiPlayContinuityDiscoveryListenerState.DiscoveryListenerOnDevicePositionChangedTransaction);

        Assert.Equal(0xFEFF5, MiPlayContinuityDiscoveryListenerState.NativeStartDiscoveryApiStringOffset);
        Assert.Equal(0xFF14D, MiPlayContinuityDiscoveryListenerState.NativeRegisterDiscoveryListenerApiStringOffset);
        Assert.Equal(0xFF1B8, MiPlayContinuityDiscoveryListenerState.NativeUnregisterDiscoveryListenerApiStringOffset);
        Assert.Equal(0x14759B, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnDeviceFoundOffset);
        Assert.Equal(0x1477DC, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnDeviceLostOffset);
        Assert.Equal(0x147827, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnDeviceInfoChangedOffset);
        Assert.Equal(0x14787A, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnDevicePositionChangedOffset);
        Assert.Equal(0x14791C, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnReceiveDataOffset);
        Assert.Equal(0x1490D8, MiPlayContinuityDiscoveryListenerState.NativeStartDiscoveryJniStringOffset);
        Assert.Equal(0x1491E4, MiPlayContinuityDiscoveryListenerState.NativeStopDiscoveryJniStringOffset);
        Assert.Equal(0x1492E4, MiPlayContinuityDiscoveryListenerState.NativeRegisterDiscoveryListenerJniStringOffset);
        Assert.Equal(0x14933A, MiPlayContinuityDiscoveryListenerState.NativeUnregisterDiscoveryListenerJniStringOffset);
        Assert.Equal(0x185F0F, MiPlayContinuityDiscoveryListenerState.NativeNotifyOnDeviceFoundSymbolOffset);
        Assert.Equal(0x18615A, MiPlayContinuityDiscoveryListenerState.NativeNotifyOnDeviceInfoChangedSymbolOffset);
        Assert.Equal(0x5B64B0, MiPlayContinuityDiscoveryListenerState.NativeJniDiscoveryListenerOnDeviceFoundSymbolOffset);
        Assert.Equal(0x5B6619, MiPlayContinuityDiscoveryListenerState.NativeNetBusRegisterDiscoveryListenerSymbolOffset);
    }

    [Fact]
    public void StaticDiscServiceIdAndMediumParsingMatchesDiscFilterParser()
    {
        Assert.Equal("00000005", MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId("5"));
        Assert.Equal("00017803", MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId("17803"));
        Assert.Equal("00017803", MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId("00017803"));
        Assert.Null(MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId(""));
        Assert.Null(MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId("123456789"));

        Assert.Equal(
            MiPlayContinuityMediumType.Mdns,
            MiPlayContinuityDiscoveryListenerState.ParseStaticDiscMediumTypes(null));
        Assert.Equal(
            MiPlayContinuityMediumType.Mdns,
            MiPlayContinuityDiscoveryListenerState.ParseStaticDiscMediumTypes("unknown"));
        Assert.Equal(
            MiPlayContinuityMediumType.Mdns | MiPlayContinuityMediumType.Ble | MiPlayContinuityMediumType.Nfc,
            MiPlayContinuityDiscoveryListenerState.ParseStaticDiscMediumTypes("MDNS|BLE|NFC"));
        Assert.Equal(
            MiPlayContinuityMediumType.BleApple | MiPlayContinuityMediumType.WifiAware,
            MiPlayContinuityDiscoveryListenerState.ParseStaticDiscMediumTypes("bleApple|wifiaware"));
    }

    [Fact]
    public void RegisterDiscoveryListenerRequiresBinderListenerReceiverPermissionAndNativeSuccess()
    {
        var accepted = MiPlayContinuityDiscoveryListenerState.EvaluateRegisterDiscoveryListener(
            new MiPlayContinuityRegisterDiscoveryListenerPrerequisites(
                ServiceId: "00017803",
                BinderTokenProvided: true,
                DiscoveryListenerProvided: true,
                ResultReceiverProvided: true,
                InternalBindPermissionGranted: true,
                NativeRegisterReturnedSuccess: true));

        Assert.True(accepted.CanProceed);

        var missingListener = MiPlayContinuityDiscoveryListenerState.EvaluateRegisterDiscoveryListener(
            new MiPlayContinuityRegisterDiscoveryListenerPrerequisites(
                ServiceId: "00017803",
                BinderTokenProvided: true,
                DiscoveryListenerProvided: false,
                ResultReceiverProvided: true,
                InternalBindPermissionGranted: true,
                NativeRegisterReturnedSuccess: true));
        var missingPermission = MiPlayContinuityDiscoveryListenerState.EvaluateRegisterDiscoveryListener(
            new MiPlayContinuityRegisterDiscoveryListenerPrerequisites(
                ServiceId: "00017803",
                BinderTokenProvided: true,
                DiscoveryListenerProvided: true,
                ResultReceiverProvided: true,
                InternalBindPermissionGranted: false,
                NativeRegisterReturnedSuccess: true));
        var nativeFailure = MiPlayContinuityDiscoveryListenerState.EvaluateRegisterDiscoveryListener(
            new MiPlayContinuityRegisterDiscoveryListenerPrerequisites(
                ServiceId: "00017803",
                BinderTokenProvided: true,
                DiscoveryListenerProvided: true,
                ResultReceiverProvided: true,
                InternalBindPermissionGranted: true,
                NativeRegisterReturnedSuccess: false));

        Assert.False(missingListener.CanProceed);
        Assert.False(missingPermission.CanProceed);
        Assert.False(nativeFailure.CanProceed);
    }

    [Fact]
    public void StartDiscoveryRequiresRegisteredListenerOptionsMediumDataTypePermissionAndNativeSuccess()
    {
        var accepted = MiPlayContinuityDiscoveryListenerState.EvaluateStartDiscovery(
            new MiPlayContinuityStartDiscoveryPrerequisites(
                ServiceId: "00017803",
                DiscoveryListenerRegistered: true,
                StartDiscoveryOptionsProvided: true,
                MediumType: MiPlayContinuityMediumType.Mdns,
                DiscoveryDataTypeValid: true,
                InternalBindPermissionGranted: true,
                NativeStartReturnedSuccess: true));

        Assert.True(accepted.CanProceed);

        var missingListener = MiPlayContinuityDiscoveryListenerState.EvaluateStartDiscovery(
            new MiPlayContinuityStartDiscoveryPrerequisites(
                ServiceId: "00017803",
                DiscoveryListenerRegistered: false,
                StartDiscoveryOptionsProvided: true,
                MediumType: MiPlayContinuityMediumType.Mdns,
                DiscoveryDataTypeValid: true,
                InternalBindPermissionGranted: true,
                NativeStartReturnedSuccess: true));
        var missingMedium = MiPlayContinuityDiscoveryListenerState.EvaluateStartDiscovery(
            new MiPlayContinuityStartDiscoveryPrerequisites(
                ServiceId: "00017803",
                DiscoveryListenerRegistered: true,
                StartDiscoveryOptionsProvided: true,
                MediumType: MiPlayContinuityMediumType.None,
                DiscoveryDataTypeValid: true,
                InternalBindPermissionGranted: true,
                NativeStartReturnedSuccess: true));
        var invalidDataType = MiPlayContinuityDiscoveryListenerState.EvaluateStartDiscovery(
            new MiPlayContinuityStartDiscoveryPrerequisites(
                ServiceId: "00017803",
                DiscoveryListenerRegistered: true,
                StartDiscoveryOptionsProvided: true,
                MediumType: MiPlayContinuityMediumType.Mdns,
                DiscoveryDataTypeValid: false,
                InternalBindPermissionGranted: true,
                NativeStartReturnedSuccess: true));

        Assert.False(missingListener.CanProceed);
        Assert.False(missingMedium.CanProceed);
        Assert.False(invalidDataType.CanProceed);
    }

    [Fact]
    public void DeviceFoundCallbackRequiresRegisteredAliveListenerNativeCallbackServiceIdDeviceIdAndDeviceInfo()
    {
        var v2 = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceFoundCallback(
            new MiPlayContinuityDiscoveryCallbackPrerequisites(
                DiscoveryListenerRegistered: true,
                ListenerBinderAlive: true,
                NativeCallbackArrived: true,
                CallbackServiceId: "00017803",
                DeviceId: "netbus-device-id",
                DeviceInfoV2Provided: true,
                ListenerSupportsDeviceInfoV2: true));
        var legacy = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceFoundCallback(
            new MiPlayContinuityDiscoveryCallbackPrerequisites(
                DiscoveryListenerRegistered: true,
                ListenerBinderAlive: true,
                NativeCallbackArrived: true,
                CallbackServiceId: "00017803",
                DeviceId: "netbus-device-id",
                DeviceInfoV2Provided: true,
                ListenerSupportsDeviceInfoV2: false));

        Assert.True(v2.CanProceed);
        Assert.Contains(nameof(MiPlayContinuityDiscoveryDeviceCallbackShape.DeviceInfoV2), v2.Reason, StringComparison.Ordinal);
        Assert.True(legacy.CanProceed);
        Assert.Contains(nameof(MiPlayContinuityDiscoveryDeviceCallbackShape.LegacyDeviceInfo), legacy.Reason, StringComparison.Ordinal);

        var missingDeviceId = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceFoundCallback(
            new MiPlayContinuityDiscoveryCallbackPrerequisites(
                DiscoveryListenerRegistered: true,
                ListenerBinderAlive: true,
                NativeCallbackArrived: true,
                CallbackServiceId: "00017803",
                DeviceId: "",
                DeviceInfoV2Provided: true,
                ListenerSupportsDeviceInfoV2: true));
        var binderDead = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceFoundCallback(
            new MiPlayContinuityDiscoveryCallbackPrerequisites(
                DiscoveryListenerRegistered: true,
                ListenerBinderAlive: false,
                NativeCallbackArrived: true,
                CallbackServiceId: "00017803",
                DeviceId: "netbus-device-id",
                DeviceInfoV2Provided: true,
                ListenerSupportsDeviceInfoV2: true));

        Assert.False(missingDeviceId.CanProceed);
        Assert.False(binderDead.CanProceed);
    }

    [Fact]
    public void DeviceInfoChangedCallbackUsesV2WhenSupportedAndClearsOrSuppressesV2OnlyMaskForLegacy()
    {
        var prerequisites = new MiPlayContinuityDiscoveryCallbackPrerequisites(
            DiscoveryListenerRegistered: true,
            ListenerBinderAlive: true,
            NativeCallbackArrived: true,
            CallbackServiceId: "00017803",
            DeviceId: "netbus-device-id",
            DeviceInfoV2Provided: true,
            ListenerSupportsDeviceInfoV2: true);

        var v2 = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceInfoChangedCallback(
            prerequisites,
            nativeChangeMask: 0x201);
        var legacy = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceInfoChangedCallback(
            prerequisites with { ListenerSupportsDeviceInfoV2 = false },
            nativeChangeMask: 0x201);
        var suppressed = MiPlayContinuityDiscoveryListenerState.EvaluateDeviceInfoChangedCallback(
            prerequisites with { ListenerSupportsDeviceInfoV2 = false },
            nativeChangeMask: MiPlayContinuityDiscoveryListenerState.DeviceInfoV2OnlyChangeMask);

        Assert.True(v2.CanDeliver);
        Assert.Equal(MiPlayContinuityDiscoveryChangeCallbackAction.DeviceInfoV2, v2.Action);
        Assert.Equal(0x201, v2.DeliveredChangeMask);

        Assert.True(legacy.CanDeliver);
        Assert.Equal(MiPlayContinuityDiscoveryChangeCallbackAction.LegacyDeviceInfo, legacy.Action);
        Assert.Equal(0x001, legacy.DeliveredChangeMask);

        Assert.False(suppressed.CanDeliver);
        Assert.Equal(MiPlayContinuityDiscoveryChangeCallbackAction.Suppress, suppressed.Action);
    }

    [Fact]
    public void StaticDiscConfigLoadAndProxyStartRequireBusinessPackageConfigPermissionSignatureAndRegisterService()
    {
        var loaded = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscConfigLoad(
            new MiPlayContinuityStaticDiscConfigPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsStaticDiscFilter: true,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedFilterCount: 1,
                PackageEnabled: true,
                InternalBindPermissionGranted: true));

        Assert.True(loaded.CanProceed);

        var missingMetadata = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscConfigLoad(
            new MiPlayContinuityStaticDiscConfigPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsStaticDiscFilter: false,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedFilterCount: 1,
                PackageEnabled: true,
                InternalBindPermissionGranted: true));
        var noPermission = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscConfigLoad(
            new MiPlayContinuityStaticDiscConfigPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsStaticDiscFilter: true,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedFilterCount: 1,
                PackageEnabled: true,
                InternalBindPermissionGranted: false));

        Assert.False(missingMetadata.CanProceed);
        Assert.False(noPermission.CanProceed);

        var start = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscStart(
            new MiPlayContinuityStaticDiscStartPrerequisites(
                ConfigServiceId: "17803",
                StaticConfigProxyEnabled: true,
                PackageSignatureAvailable: true,
                NativeRegisterServiceReturnedSuccess: true,
                DiscoveryListenerProxyCreated: true,
                StartDiscoveryOptionsCanBeBuilt: true));
        var invalidService = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscStart(
            new MiPlayContinuityStaticDiscStartPrerequisites(
                ConfigServiceId: "123456789",
                StaticConfigProxyEnabled: true,
                PackageSignatureAvailable: true,
                NativeRegisterServiceReturnedSuccess: true,
                DiscoveryListenerProxyCreated: true,
                StartDiscoveryOptionsCanBeBuilt: true));
        var registerFailed = MiPlayContinuityDiscoveryListenerState.EvaluateStaticDiscStart(
            new MiPlayContinuityStaticDiscStartPrerequisites(
                ConfigServiceId: "17803",
                StaticConfigProxyEnabled: true,
                PackageSignatureAvailable: true,
                NativeRegisterServiceReturnedSuccess: false,
                DiscoveryListenerProxyCreated: true,
                StartDiscoveryOptionsCanBeBuilt: true));

        Assert.True(start.CanProceed);
        Assert.False(invalidService.CanProceed);
        Assert.False(registerFailed.CanProceed);
    }

    [Fact]
    public void DiscoveryListenerDeviceInfoDoesNotExplainLegacyTcp8899GetDeviceInfo()
    {
        Assert.False(MiPlayContinuityDiscoveryListenerState.DiscoveryListenerCanExplainLegacyTcp8899GetDeviceInfo(
            nativeDeviceFoundDelivered: true,
            connectionMode: MiPlayPostAuthConnectionMode.LegacyTcp8899));
        Assert.False(MiPlayContinuityDiscoveryListenerState.DiscoveryListenerCanExplainLegacyTcp8899GetDeviceInfo(
            nativeDeviceFoundDelivered: true,
            connectionMode: MiPlayPostAuthConnectionMode.LyraContinuityChannel));
    }
}
