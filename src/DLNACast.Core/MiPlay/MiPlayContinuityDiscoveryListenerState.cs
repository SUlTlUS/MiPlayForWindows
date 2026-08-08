namespace DLNACast.Core.MiPlay;

public enum MiPlayContinuityDiscoveryDeviceCallbackShape
{
    LegacyDeviceInfo = 1,
    DeviceInfoV2 = 2,
}

public enum MiPlayContinuityDiscoveryChangeCallbackAction
{
    Suppress = 0,
    LegacyDeviceInfo = 1,
    DeviceInfoV2 = 2,
}

public sealed record MiPlayContinuityRegisterDiscoveryListenerPrerequisites(
    string? ServiceId,
    bool BinderTokenProvided,
    bool DiscoveryListenerProvided,
    bool ResultReceiverProvided,
    bool InternalBindPermissionGranted,
    bool NativeRegisterReturnedSuccess);

public sealed record MiPlayContinuityStartDiscoveryPrerequisites(
    string? ServiceId,
    bool DiscoveryListenerRegistered,
    bool StartDiscoveryOptionsProvided,
    MiPlayContinuityMediumType MediumType,
    bool DiscoveryDataTypeValid,
    bool InternalBindPermissionGranted,
    bool NativeStartReturnedSuccess);

public sealed record MiPlayContinuityDiscoveryCallbackPrerequisites(
    bool DiscoveryListenerRegistered,
    bool ListenerBinderAlive,
    bool NativeCallbackArrived,
    string? CallbackServiceId,
    string? DeviceId,
    bool DeviceInfoV2Provided,
    bool ListenerSupportsDeviceInfoV2);

public sealed record MiPlayContinuityStaticDiscConfigPrerequisites(
    bool ServiceInfoProvided,
    bool MetadataContainsStaticDiscFilter,
    int ResourceId,
    bool AppResourcesAvailable,
    int ParsedFilterCount,
    bool PackageEnabled,
    bool InternalBindPermissionGranted);

public sealed record MiPlayContinuityStaticDiscStartPrerequisites(
    string? ConfigServiceId,
    bool StaticConfigProxyEnabled,
    bool PackageSignatureAvailable,
    bool NativeRegisterServiceReturnedSuccess,
    bool DiscoveryListenerProxyCreated,
    bool StartDiscoveryOptionsCanBeBuilt);

public sealed record MiPlayContinuityDiscoveryChangeCallbackDecision(
    bool CanDeliver,
    MiPlayContinuityDiscoveryChangeCallbackAction Action,
    int DeliveredChangeMask,
    string Reason);

/// <summary>
/// Offline model for Continuity NetBus discovery listener registration and
/// callbacks in Mi Connect Service 5.1.251.10. It models native listener
/// delivery of DeviceInfoV2/DeviceInfo and static discovery XML startup, not
/// the legacy TCP 8899 SafetyData getDeviceInfo command.
/// </summary>
public static class MiPlayContinuityDiscoveryListenerState
{
    public const string DeviceInfoV2Feature = "device.DEVICE_INFO_V2";

    public const string StaticDiscFilterResourceKey = "static_disc_filter";
    public const string StaticDiscSwitchV3Key = "static_disc_switch_v3";
    public const string StaticDiscSwitchV2Key = "static_disc_switch_v2";
    public const string StaticDiscSwitchKey = "static_disc_switch";
    public const string StaticEnableSwitchKey = "static_enable_switch";
    public const string StaticDiscRootTag = "disc_filters";
    public const string StaticDiscFilterTag = "filter";

    public const string AttributeServiceId = "serviceId";
    public const string AttributeMediumTypes = "mediumTypes";
    public const string AttributeDataType = "dataType";
    public const string AttributeDiscSameAccount = "discSameAccount";
    public const string AttributeDiscSameGroup = "discSameGroup";
    public const string AttributeDiscSameP2PGroup = "discSameP2PGroup";
    public const string AttributeRangeGear = "rangeGear";
    public const string AttributeWorkWhenScreenOff = "workWhenScreenOff";
    public const string AttributePrivacySecurity = "privacySecurity";
    public const string AttributeExtFlag = "extFlag";
    public const string AttributeAutoScanPeriod = "autoScanPeriod";
    public const string AttributeSwitch = "switch";

    public const string ActionNetbusDiscDeviceFound =
        "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_FOUND";
    public const string ActionNetbusDiscDeviceChanged =
        "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_CHANGED";
    public const string ActionNetbusDiscDeviceLost =
        "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_LOST";
    public const string ActionNetbusDiscReceiveData =
        "com.xiaomi.continuity.action.NETBUS_DISC_RECEIVE_DATA";
    public const string ExtraNetbusDiscServiceId =
        "com.xiaomi.continuity.NETBUS_DISC_SERVICE_ID";
    public const string ExtraNetbusDiscDeviceId =
        "com.xiaomi.continuity.NETBUS_DISC_DEVICE_ID";
    public const string ExtraNetbusDiscDeviceInfo =
        "com.xiaomi.continuity.NETBUS_DISC_DEVICE_INFO";
    public const string ExtraNetbusDiscChangeMask =
        "com.xiaomi.continuity.NETBUS_DISC_CHANGE_MASK";
    public const string ExtraNetbusDiscDiscoveryData =
        "com.xiaomi.continuity.NETBUS_DISC_DISCOVERY_DATA";

    public const int NetBusRegisterServiceTransaction = 3;
    public const int NetBusStartDiscoveryTransaction = 7;
    public const int NetBusRegisterDiscoveryListenerTransaction = 10;
    public const int NetBusStartDiscoveryV2Transaction = 25;
    public const int NetBusRegisterDiscoveryListenerV2Transaction = 28;

    public const int DiscoveryListenerOnDeviceFoundTransaction = 1;
    public const int DiscoveryListenerOnDeviceLostTransaction = 2;
    public const int DiscoveryListenerOnDeviceInfoChangedTransaction = 3;
    public const int DiscoveryListenerOnReceiveDataTransaction = 4;
    public const int DiscoveryListenerHasFeatureTransaction = 5;
    public const int DiscoveryListenerOnDeviceFoundV2Transaction = 6;
    public const int DiscoveryListenerOnDeviceLostV2Transaction = 7;
    public const int DiscoveryListenerOnDeviceInfoChangedV2Transaction = 8;
    public const int DiscoveryListenerOnDevicePositionChangedTransaction = 9;

    public const int DeviceInfoV2OnlyChangeMask = 0x200;
    public const int PlatformTypeAndroid = 1;
    public const int StaticDiscoveryDefaultMediumType = (int)MiPlayContinuityMediumType.Mdns;

    public const long NativeStartDiscoveryApiStringOffset = 0xFEFF5;
    public const long NativeRegisterDiscoveryListenerApiStringOffset = 0xFF14D;
    public const long NativeUnregisterDiscoveryListenerApiStringOffset = 0xFF1B8;
    public const long NativeJniDiscoveryListenerOnDeviceFoundOffset = 0x14759B;
    public const long NativeJniDiscoveryListenerOnDeviceLostOffset = 0x1477DC;
    public const long NativeJniDiscoveryListenerOnDeviceInfoChangedOffset = 0x147827;
    public const long NativeJniDiscoveryListenerOnDevicePositionChangedOffset = 0x14787A;
    public const long NativeJniDiscoveryListenerOnReceiveDataOffset = 0x14791C;
    public const long NativeStartDiscoveryJniStringOffset = 0x1490D8;
    public const long NativeStopDiscoveryJniStringOffset = 0x1491E4;
    public const long NativeRegisterDiscoveryListenerJniStringOffset = 0x1492E4;
    public const long NativeUnregisterDiscoveryListenerJniStringOffset = 0x14933A;
    public const long NativeNotifyOnDeviceFoundSymbolOffset = 0x185F0F;
    public const long NativeNotifyOnDeviceInfoChangedSymbolOffset = 0x18615A;
    public const long NativeJniDiscoveryListenerOnDeviceFoundSymbolOffset = 0x5B64B0;
    public const long NativeNetBusRegisterDiscoveryListenerSymbolOffset = 0x5B6619;

    public static MiPlayIdmStateDecision EvaluateRegisterDiscoveryListener(
        MiPlayContinuityRegisterDiscoveryListenerPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The discovery serviceId argument is missing.");
        }

        if (!prerequisites.BinderTokenProvided)
        {
            return new MiPlayIdmStateDecision(false, "The caller Binder token is missing.");
        }

        if (!prerequisites.DiscoveryListenerProvided)
        {
            return new MiPlayIdmStateDecision(false, "The IDiscoveryListener Binder callback is missing.");
        }

        if (!prerequisites.ResultReceiverProvided)
        {
            return new MiPlayIdmStateDecision(false, "The discovery listener ResultReceiver is missing.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The serviceId-specific internal Continuity permission check failed.");
        }

        if (!prerequisites.NativeRegisterReturnedSuccess)
        {
            return new MiPlayIdmStateDecision(false, "nativeRegisterDiscoveryListener did not return success.");
        }

        return new MiPlayIdmStateDecision(true, "The NetBus discovery listener is registered at the native success boundary.");
    }

    public static MiPlayIdmStateDecision EvaluateStartDiscovery(
        MiPlayContinuityStartDiscoveryPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The startDiscovery serviceId argument is missing.");
        }

        if (!prerequisites.DiscoveryListenerRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No discovery listener is registered for the serviceId.");
        }

        if (!prerequisites.StartDiscoveryOptionsProvided)
        {
            return new MiPlayIdmStateDecision(false, "StartDiscoveryOptionsV2 is missing.");
        }

        if (prerequisites.MediumType == MiPlayContinuityMediumType.None)
        {
            return new MiPlayIdmStateDecision(false, "The discovery medium type mask is empty.");
        }

        if (!prerequisites.DiscoveryDataTypeValid)
        {
            return new MiPlayIdmStateDecision(false, "The discovery data type is NONE or invalid.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The serviceId-specific internal Continuity permission check failed.");
        }

        if (!prerequisites.NativeStartReturnedSuccess)
        {
            return new MiPlayIdmStateDecision(false, "nativeStartDiscovery did not return success.");
        }

        return new MiPlayIdmStateDecision(true, "The NetBus discovery listener can receive native DeviceInfo callbacks.");
    }

    public static MiPlayIdmStateDecision EvaluateDeviceFoundCallback(
        MiPlayContinuityDiscoveryCallbackPrerequisites prerequisites)
    {
        var common = EvaluateCommonCallbackPrerequisites(prerequisites);
        if (!common.CanProceed)
        {
            return common;
        }

        var shape = prerequisites.ListenerSupportsDeviceInfoV2
            ? MiPlayContinuityDiscoveryDeviceCallbackShape.DeviceInfoV2
            : MiPlayContinuityDiscoveryDeviceCallbackShape.LegacyDeviceInfo;
        return new MiPlayIdmStateDecision(true, $"DiscoveryListener can deliver {shape} for native onDeviceFound.");
    }

    public static MiPlayContinuityDiscoveryChangeCallbackDecision EvaluateDeviceInfoChangedCallback(
        MiPlayContinuityDiscoveryCallbackPrerequisites prerequisites,
        int nativeChangeMask)
    {
        var common = EvaluateCommonCallbackPrerequisites(prerequisites);
        if (!common.CanProceed)
        {
            return new MiPlayContinuityDiscoveryChangeCallbackDecision(
                false,
                MiPlayContinuityDiscoveryChangeCallbackAction.Suppress,
                0,
                common.Reason);
        }

        if (prerequisites.ListenerSupportsDeviceInfoV2)
        {
            return new MiPlayContinuityDiscoveryChangeCallbackDecision(
                true,
                MiPlayContinuityDiscoveryChangeCallbackAction.DeviceInfoV2,
                nativeChangeMask,
                "DiscoveryListener can deliver DeviceInfoV2 with the native change mask.");
        }

        if (nativeChangeMask == DeviceInfoV2OnlyChangeMask)
        {
            return new MiPlayContinuityDiscoveryChangeCallbackDecision(
                false,
                MiPlayContinuityDiscoveryChangeCallbackAction.Suppress,
                0,
                "Legacy DeviceInfo listeners suppress DeviceInfoV2-only change mask 0x200.");
        }

        return new MiPlayContinuityDiscoveryChangeCallbackDecision(
            true,
            MiPlayContinuityDiscoveryChangeCallbackAction.LegacyDeviceInfo,
            nativeChangeMask & ~DeviceInfoV2OnlyChangeMask,
            "DiscoveryListener can deliver legacy DeviceInfo with DeviceInfoV2-only change bits cleared.");
    }

    public static MiPlayIdmStateDecision EvaluateStaticDiscConfigLoad(
        MiPlayContinuityStaticDiscConfigPrerequisites prerequisites)
    {
        if (!prerequisites.ServiceInfoProvided)
        {
            return new MiPlayIdmStateDecision(false, "No Android ServiceInfo is available for static discovery parsing.");
        }

        if (!prerequisites.MetadataContainsStaticDiscFilter)
        {
            return new MiPlayIdmStateDecision(false, "ServiceInfo metadata does not contain static_disc_filter.");
        }

        if (prerequisites.ResourceId == 0)
        {
            return new MiPlayIdmStateDecision(false, "The static discovery filter resource id is zero.");
        }

        if (!prerequisites.AppResourcesAvailable)
        {
            return new MiPlayIdmStateDecision(false, "PackageUtil could not load the business package resources.");
        }

        if (prerequisites.ParsedFilterCount <= 0)
        {
            return new MiPlayIdmStateDecision(false, "The parsed static discovery filter list is empty.");
        }

        if (!prerequisites.PackageEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The package is disabled; static discovery state is removed.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The static discovery service lacks BIND_CONTINUITY_SERVICE_INTERNAL.");
        }

        return new MiPlayIdmStateDecision(true, "The static discovery XML can create discovery listener proxy state.");
    }

    public static MiPlayIdmStateDecision EvaluateStaticDiscStart(
        MiPlayContinuityStaticDiscStartPrerequisites prerequisites)
    {
        if (NormalizeStaticDiscServiceId(prerequisites.ConfigServiceId) is null)
        {
            return new MiPlayIdmStateDecision(false, "The static discovery config serviceId is invalid.");
        }

        if (!prerequisites.StaticConfigProxyEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The ServiceStaticConfigProxy has not enabled this discovery config.");
        }

        if (!prerequisites.PackageSignatureAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The static discovery path cannot build package-signature AppInfo.");
        }

        if (!prerequisites.NativeRegisterServiceReturnedSuccess)
        {
            return new MiPlayIdmStateDecision(false, "static discovery nativeRegisterService did not return success.");
        }

        if (!prerequisites.DiscoveryListenerProxyCreated)
        {
            return new MiPlayIdmStateDecision(false, "The static DiscoveryListenerProxy is missing.");
        }

        if (!prerequisites.StartDiscoveryOptionsCanBeBuilt)
        {
            return new MiPlayIdmStateDecision(false, "StartDiscoveryOptionsV2 cannot be built from the static discovery config.");
        }

        return new MiPlayIdmStateDecision(true, "Static discovery can register the listener, stop stale discovery, and start discovery.");
    }

    public static string? NormalizeStaticDiscServiceId(string? serviceId)
    {
        if (string.IsNullOrEmpty(serviceId) || serviceId.Length > 8)
        {
            return null;
        }

        return serviceId.PadLeft(8, '0');
    }

    public static MiPlayContinuityMediumType ParseStaticDiscMediumTypes(string? mediumTypes)
    {
        if (string.IsNullOrWhiteSpace(mediumTypes))
        {
            return MiPlayContinuityMediumType.Mdns;
        }

        var result = MiPlayContinuityMediumType.None;
        foreach (var item in mediumTypes.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(item, "MDNS", StringComparison.OrdinalIgnoreCase))
            {
                result |= MiPlayContinuityMediumType.Mdns;
            }
            else if (string.Equals(item, "BLE", StringComparison.OrdinalIgnoreCase))
            {
                result |= MiPlayContinuityMediumType.Ble;
            }
            else if (string.Equals(item, "BLE_APPLE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item, "bleApple", StringComparison.Ordinal))
            {
                result |= MiPlayContinuityMediumType.BleApple;
            }
            else if (string.Equals(item, "NFC", StringComparison.OrdinalIgnoreCase))
            {
                result |= MiPlayContinuityMediumType.Nfc;
            }
            else if (string.Equals(item, "WIFI_AWARE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item, "wifiaware", StringComparison.Ordinal))
            {
                result |= MiPlayContinuityMediumType.WifiAware;
            }
        }

        return result == MiPlayContinuityMediumType.None
            ? MiPlayContinuityMediumType.Mdns
            : result;
    }

    public static bool DiscoveryListenerCanExplainLegacyTcp8899GetDeviceInfo(
        bool nativeDeviceFoundDelivered,
        MiPlayPostAuthConnectionMode connectionMode) =>
        nativeDeviceFoundDelivered &&
        connectionMode == MiPlayPostAuthConnectionMode.LegacyTcp8899 &&
        false;

    private static MiPlayIdmStateDecision EvaluateCommonCallbackPrerequisites(
        MiPlayContinuityDiscoveryCallbackPrerequisites prerequisites)
    {
        if (!prerequisites.DiscoveryListenerRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No discovery listener is registered for this callback.");
        }

        if (!prerequisites.ListenerBinderAlive)
        {
            return new MiPlayIdmStateDecision(false, "The discovery listener Binder is dead and should be unregistered.");
        }

        if (!prerequisites.NativeCallbackArrived)
        {
            return new MiPlayIdmStateDecision(false, "No native discovery callback has arrived.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.CallbackServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The native discovery callback serviceId is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "The native discovery callback deviceId is missing.");
        }

        if (!prerequisites.DeviceInfoV2Provided)
        {
            return new MiPlayIdmStateDecision(false, "The native discovery callback did not provide DeviceInfoV2.");
        }

        return new MiPlayIdmStateDecision(true, "The native discovery callback can be forwarded to the business listener.");
    }
}
