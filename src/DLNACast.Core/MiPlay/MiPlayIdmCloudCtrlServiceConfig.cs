namespace DLNACast.Core.MiPlay;

public sealed record MiPlayIdmCloudCtrlServiceConfig(
    int ServiceTypeId,
    string ServiceType);

public sealed record MiPlayIdmCloudCtrlAppIntentRow(
    int ApplicationId,
    string? ServiceType,
    string? IntentAction,
    string? IntentExtra);

/// <summary>
/// Offline model for the Mi Connect Service CloudCtrl ServiceConfigs bridge.
/// The APK maps Room `app_intent(serviceType, appid)` rows into protobuf
/// `ServiceConfig(serviceTypeId, serviceType)` entries for IDMNative. It does
/// not carry Continuity package identity, ServiceName, runtime serviceId, or a
/// listener/onSuccess state.
/// </summary>
public static class MiPlayIdmCloudCtrlServiceConfigs
{
    public const string RoomAppIntentTableName = "app_intent";
    public const string RoomAppIntentServiceTypeColumn = "serviceType";
    public const string RoomAppIntentApplicationIdColumn = "appid";

    public const int CloudCtrlServiceConfigServiceTypeIdFieldNumber = 1;
    public const int CloudCtrlServiceConfigServiceTypeFieldNumber = 2;

    public const string GetServiceConfigListSql = "SELECT serviceType,appid FROM app_intent";
    public const string GetServiceTypeByAppIdSql = "SELECT serviceType FROM app_intent WHERE appid = ?";

    public static bool TryCreateServiceConfig(
        MiPlayIdmCloudCtrlAppIntentRow row,
        out MiPlayIdmCloudCtrlServiceConfig? serviceConfig)
    {
        serviceConfig = null;

        if (string.IsNullOrWhiteSpace(row.ServiceType))
        {
            return false;
        }

        serviceConfig = new MiPlayIdmCloudCtrlServiceConfig(
            row.ApplicationId,
            row.ServiceType);
        return true;
    }

    public static bool ContainsOnlyCloudCtrlMappingFields(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        serviceConfig.ServiceTypeId >= 0 &&
        serviceConfig.ServiceType.Length > 0;

    public static bool CanProvideContinuityPackageIdentity(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;

    public static bool CanProvideContinuityServiceName(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;

    public static bool CanProvideRuntimeServiceId(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;
}
