namespace DLNACast.Core.MiPlay;

public enum MiConnectDiscoveryType
{
    None = -1,
    Bluetooth = 1,
    IpBonjour = 2,
    Nfc = 4,
    IpP2p = 16,
    IpSoftAp = 32,
    Ble = 64,
    BluetoothClassic = 128,
}

public sealed record MiPlayEndpointFoundBroadcastPrerequisites(
    bool EndpointProvided,
    bool AdvDataProvided,
    bool AdvAppsProvided,
    MiConnectDiscoveryType DiscoveryType,
    bool AdvAppsContainLegacyMiPlayAppId,
    bool LegacyMiPlayIntentConfigPresent);

public sealed record MiPlayEndpointFoundStaticWakeupPrerequisites(
    bool EndpointProvided,
    bool AdvDataProvided,
    bool AdvAppsProvided,
    MiConnectDiscoveryType DiscoveryType,
    bool StaticConfigPresentForAppId,
    bool StaticConfigServicePermissionGranted,
    bool ResidentBackgroundScanEnabled,
    bool EndpointDeviceTypeAllowed,
    bool SameAccountRequired,
    bool EndpointIdentityVerified);

public sealed record MiPlayEndpointFoundPayloadShape(
    bool IncludesMac,
    bool IncludesDiscoveryType,
    bool IncludesRssi,
    bool IncludesName,
    bool IncludesIdHash,
    bool IncludesCommand,
    bool IncludesWiredMac,
    bool IncludesBluetoothMac,
    bool IncludesAdvData,
    bool IncludesVerifyStatus,
    bool IncludesWakeUpEvent,
    bool IncludesNotifyBean,
    bool IncludesServiceId,
    bool IncludesConnectionId,
    bool IncludesChannelId,
    bool IncludesTransKey,
    bool IncludesSafetyDataSession);

/// <summary>
/// Offline model for Mi Connect Service 5.1.251.10 endpoint-found dispatch.
/// This path is discovery notification state only. It does not represent
/// SafetyAuth DealSafetyDone, Continuity channel creation, or legacy TCP 8899
/// post-auth getDeviceInfo success.
/// </summary>
public static class MiPlayEndpointFoundDispatchState
{
    public const int LegacyMiPlayAppId = 2;
    public const int MdnsMiPlayAudioApplicationId = MiPlayMdnsCapabilities.MiPlayAudioApplicationId;

    public const string LegacyMiPlayEndpointFoundAction =
        "com.xiaomi.mi_connect_service.mi_play_endpoint_found";
    public const string ReceiveEndpointPermission =
        "com.xiaomi.mi_connect_service.permission.RECEIVE_ENDPOINT";

    public const string StaticConfigAction =
        "com.xiaomi.mi_connect_service.action.STATIC_CONFIG_ACTION";
    public const string StaticBindServiceBasedOnIdmPermission =
        "com.xiaomi.permission.STATIC_BIND_SERVICE_BASED_ON_IDM";
    public const string WakeUpEventExtra = "wakeUpEvent";
    public const string NotifyBeanExtra = "notifyBean";
    public const string EndpointFoundWakeUpValue = "endpoint_found";
    public const string EndpointConnectedWakeUpValue = "endpoint_connected";

    public const string ExtraMac = "mac";
    public const string ExtraDiscoveryType = "disctype";
    public const string ExtraRssi = "rssi";
    public const string ExtraName = "name";
    public const string ExtraIdHash = "idhash";
    public const string ExtraCommand = "cmd";
    public const string ExtraWiredMac = "wired_mac";
    public const string ExtraBluetoothMac = "bt_mac";
    public const string ExtraAdv = "adv";
    public const string ExtraVerifyStatus = "verifystatus";

    public const int ScreenCastingDefaultCommand = 1;
    public const long NativeAppMgrOnEndpointFoundJniStringOffset = 0x3B21;
    public const long NativeAppMgrOnEndpointLostJniStringOffset = 0x3B58;
    public const long NativeOnServiceFoundStringOffset = 0x1A76BE;
    public const long NativeOnServiceLostStringOffset = 0x1A76D3;
    public const long NativeOnServiceConnectStatusStringOffset = 0x1A76EC;
    public const long NativeMiPlayAudioUrnStringOffset = 0x1AD894;
    public const long NativeOnEndpointFoundStringOffset = 0x1B127D;
    public const long NativeOnEndpointLostStringOffset = 0x1B1391;
    public const long NativeIpcParamOnServiceFoundStringOffset = 0x1C2337;
    public const long NativeIpcParamOnServiceLostServiceIdStringOffset = 0x1C234F;
    public const long NativeIpcParamOnServiceConnectionStatusStringOffset = 0x1C23B6;

    public static MiPlayIdmStateDecision EvaluateLegacyMiPlayBroadcast(
        MiPlayEndpointFoundBroadcastPrerequisites prerequisites)
    {
        if (!prerequisites.EndpointProvided)
        {
            return new MiPlayIdmStateDecision(false, "DiscoveryManager did not provide an EndPoint.");
        }

        if (!prerequisites.AdvDataProvided)
        {
            return new MiPlayIdmStateDecision(false, "DiscoveryManager did not provide MiConnectAdvData.");
        }

        if (!prerequisites.AdvAppsProvided)
        {
            return new MiPlayIdmStateDecision(false, "MiConnectAdvData.apps is missing.");
        }

        if (!IsBleOrNfc(prerequisites.DiscoveryType))
        {
            return new MiPlayIdmStateDecision(false, "IntentDispatcher only dispatches endpoint-found broadcasts for BLE or NFC.");
        }

        if (!prerequisites.AdvAppsContainLegacyMiPlayAppId)
        {
            return new MiPlayIdmStateDecision(false, "The advertisement apps list does not contain AppIdEnum.MI_PLAY.");
        }

        if (!prerequisites.LegacyMiPlayIntentConfigPresent)
        {
            return new MiPlayIdmStateDecision(false, "The legacy MiPlay intent config is missing.");
        }

        return new MiPlayIdmStateDecision(true, "The legacy MiPlay endpoint-found broadcast can be dispatched.");
    }

    public static MiPlayIdmStateDecision EvaluateStaticConfigEndpointFoundWakeup(
        MiPlayEndpointFoundStaticWakeupPrerequisites prerequisites)
    {
        if (!prerequisites.EndpointProvided)
        {
            return new MiPlayIdmStateDecision(false, "DiscoveryManager did not provide an EndPoint.");
        }

        if (!prerequisites.AdvDataProvided)
        {
            return new MiPlayIdmStateDecision(false, "DiscoveryManager did not provide MiConnectAdvData.");
        }

        if (!prerequisites.AdvAppsProvided)
        {
            return new MiPlayIdmStateDecision(false, "MiConnectAdvData.apps is missing.");
        }

        if (!IsBleOrNfc(prerequisites.DiscoveryType))
        {
            return new MiPlayIdmStateDecision(false, "MiConnectNotifyManager endpoint-found wakeup is reached only from the BLE/NFC dispatch branch.");
        }

        if (!prerequisites.StaticConfigPresentForAppId)
        {
            return new MiPlayIdmStateDecision(false, "No static app config is loaded for the advertised app id.");
        }

        if (!prerequisites.StaticConfigServicePermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The static notify service lacks STATIC_BIND_SERVICE_BASED_ON_IDM.");
        }

        if (!prerequisites.ResidentBackgroundScanEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The static app config does not enable resident background scan.");
        }

        if (!prerequisites.EndpointDeviceTypeAllowed)
        {
            return new MiPlayIdmStateDecision(false, "The endpoint device type is not in the configured report list.");
        }

        if (prerequisites.SameAccountRequired && !prerequisites.EndpointIdentityVerified)
        {
            return new MiPlayIdmStateDecision(false, "The static endpoint-found report requires verified same-account identity.");
        }

        return new MiPlayIdmStateDecision(true, "The static config path can wake the app with endpoint_found and NotifyBean.");
    }

    public static bool IsBleOrNfc(MiConnectDiscoveryType discoveryType) =>
        discoveryType is MiConnectDiscoveryType.Ble or MiConnectDiscoveryType.Nfc;

    public static bool EndpointFoundPayloadContainsPostAuthSessionContext(
        MiPlayEndpointFoundPayloadShape payloadShape) =>
        payloadShape.IncludesServiceId &&
        payloadShape.IncludesConnectionId &&
        payloadShape.IncludesChannelId &&
        payloadShape.IncludesTransKey &&
        payloadShape.IncludesSafetyDataSession;

    public static bool EndpointFoundCanExplainLegacyTcpGetDeviceInfoOnSuccess(
        bool endpointFoundDelivered,
        MiPlayEndpointFoundPayloadShape payloadShape) =>
        endpointFoundDelivered &&
        EndpointFoundPayloadContainsPostAuthSessionContext(payloadShape) &&
        false;

    public static bool LegacyMiPlayAppIdIsMdnsMiPlayAudioApplicationId() =>
        LegacyMiPlayAppId == MdnsMiPlayAudioApplicationId;
}
