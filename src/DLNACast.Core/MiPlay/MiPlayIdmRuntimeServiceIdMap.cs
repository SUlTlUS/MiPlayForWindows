namespace DLNACast.Core.MiPlay;

public sealed record MiPlayIdmRuntimeServiceIdMapPrerequisites(
    string? ServerId,
    string? ServiceUuid,
    string? RuntimeServiceId,
    bool ServerProcRegistered,
    bool AdvertisingResultAccepted);

public sealed record MiPlayIdmRuntimeServiceLookupPrerequisites(
    string? ServerId,
    string? ServiceUuid,
    bool ServerMapContainsPair);

/// <summary>
/// Offline model for the native IDM ServiceManager runtime service-id map.
/// Static strings in libidmservicemgr.so show a serverId + serviceUuid ->
/// serviceId relationship. This model keeps that state distinct from mDNS app
/// ids, CloudCtrl serviceType configs, and Continuity ServiceName values.
/// </summary>
public static class MiPlayIdmRuntimeServiceIdMap
{
    public const long NativeGetRealServiceIdFailedStringOffset = 0x1AEA8B;
    public const long NativeGetUnitServiceIdListByServerIdStringOffset = 0x1AF071;
    public const long NativeGetIdmServerProcByServiceIdStringOffset = 0x1AF0D4;
    public const long NativeAddServerMapServiceFormatStringOffset = 0x1AF0F0;
    public const long NativeAddServerMapServiceNameStringOffset = 0x1AF12B;
    public const long NativeUpdateServerMapServiceFormatStringOffset = 0x1AF13E;
    public const long NativeGetRealServiceIdFormatStringOffset = 0x1AF20E;
    public const long NativeGetServiceIdStringOffset = 0x1AF23B;
    public const long NativeGetServiceUuidByServiceIdFormatStringOffset = 0x1AF248;
    public const long NativeGetServiceUuidByServiceIdNameStringOffset = 0x1AF270;

    public const int IdmServiceServiceIdFieldNumber = 1;
    public const int IdmServiceTypeFieldNumber = 2;
    public const int IdmServiceNameFieldNumber = 3;
    public const int IdmServiceEndpointFieldNumber = 4;
    public const int IdmServiceOriginalServiceIdFieldNumber = 5;
    public const int IdmServiceSuperTypeFieldNumber = 6;
    public const int IdmServiceAppDataFieldNumber = 7;

    public const int IdmAdvertisingResultStatusFieldNumber = 1;
    public const int IdmAdvertisingResultServiceIdFieldNumber = 2;

    public static MiPlayIdmStateDecision EvaluateServerMapInsert(
        MiPlayIdmRuntimeServiceIdMapPrerequisites prerequisites)
    {
        if (!prerequisites.ServerProcRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No IDM server process is registered for the server id.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ServerId))
        {
            return new MiPlayIdmStateDecision(false, "The native serverId is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ServiceUuid))
        {
            return new MiPlayIdmStateDecision(false, "The native serviceUuid is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.RuntimeServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The runtime serviceId is missing.");
        }

        if (!prerequisites.AdvertisingResultAccepted)
        {
            return new MiPlayIdmStateDecision(false, "The advertising result carrying serviceId has not been accepted.");
        }

        return new MiPlayIdmStateDecision(true, "The native server map can bind serverId + serviceUuid to runtime serviceId.");
    }

    public static MiPlayIdmStateDecision EvaluateRealServiceIdLookup(
        MiPlayIdmRuntimeServiceLookupPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ServerId))
        {
            return new MiPlayIdmStateDecision(false, "The lookup serverId is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ServiceUuid))
        {
            return new MiPlayIdmStateDecision(false, "The lookup serviceUuid is missing.");
        }

        if (!prerequisites.ServerMapContainsPair)
        {
            return new MiPlayIdmStateDecision(false, "The server map does not contain the serverId + serviceUuid pair.");
        }

        return new MiPlayIdmStateDecision(true, "The server map can resolve the real runtime serviceId.");
    }

    public static bool CanDeriveFromCloudCtrlConfig(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        MiPlayIdmCloudCtrlServiceConfigs.ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;

    public static bool CanDeriveFromDiscoveryIdentity(
        int applicationId,
        MiPlayIdmServiceType serviceType) =>
        applicationId == MiPlayMdnsCapabilities.MiPlayAudioApplicationId &&
        serviceType.ServiceName == MiPlayIdmServiceTypes.MiPlayAudioServiceName &&
        false;
}
