namespace DLNACast.Core.MiPlay;

public sealed record MiPlayContinuityNetBusRegisterServicePrerequisites(
    string? ServiceId,
    bool BinderTokenProvided,
    bool ResultReceiverProvided,
    bool AppInfoGeneratedFromBinderCaller,
    bool InternalBindPermissionGranted,
    bool ServiceTokenCanBeRegistered);

public sealed record MiPlayContinuityNetBusStartAdvertisingPrerequisites(
    string? ServiceId,
    bool ServiceTokenRegistered,
    bool AppInfoBoundToServiceId,
    bool StartAdvertisingOptionsProvided,
    bool AdvertisingDataProvided);

public sealed record MiPlayContinuityNetBusStaticServicePrerequisites(
    string? ConfigServiceId,
    bool StaticConfigAvailable,
    bool PackageSignatureAvailable,
    bool RegisterServiceCompleted);

/// <summary>
/// Offline model for the Continuity NetBus service gates observed in Mi Connect
/// Service 5.1.251.10. These rules only preserve static JADX/native evidence;
/// they are not used by Probe and do not send or authorize device traffic.
/// </summary>
public static class MiPlayContinuityNetBusServiceState
{
    public const string BindContinuityServiceInternalPermission =
        "com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL";

    public const string RegisterServiceResultDataServiceIdField = "mServiceId";
    public const string NativeRegisterServiceSymbol = "NetBusManagerNative.nativeRegisterService";
    public const string NativeStartAdvertisingSymbol = "NetBusManagerNative.nativeStartAdvertising";
    public const string AdvertisingStaticConfigProcessSymbol =
        "AdvertisingStaticConfigProcess.startProxyAdvertising";
    public const string DiscoveryStaticConfigProcessSymbol =
        "DiscStaticConfigProcess.startDiscoveryProxy";

    public const int IpcRegisterServiceServiceProtoFieldNumber = 1;
    public const int IpcRegisterServiceIntentStringFieldNumber = 2;
    public const int IpcRegisterServiceIntentTypeFieldNumber = 3;
    public const int IpcRegisterServiceDiscoveryTypeFieldNumber = 4;
    public const int IpcRegisterServiceCommunicationTypeFieldNumber = 5;
    public const int IpcRegisterServiceSecurityTypeFieldNumber = 6;
    public const int IpcRegisterServicePrivateDataFieldNumber = 7;
    public const int IpcRegisterServiceAppParamFieldNumber = 8;

    public const int IpcUpdateServiceDiscoveryTypeFieldNumber = 1;
    public const int IpcUpdateServiceAdvertisingModeFieldNumber = 2;
    public const int IpcUpdateServiceUpdateAppDataFieldNumber = 3;
    public const int IpcUpdateServiceAppDataFieldNumber = 4;
    public const int IpcUpdateServiceUpdateStrategyFieldNumber = 5;
    public const int IpcUpdateServiceCommunicationTypeFieldNumber = 6;
    public const int IpcUpdateServiceUpdateTypeFieldNumber = 7;
    public const int IpcUpdateServiceAdvertisingModeScreenOffFieldNumber = 8;

    public static MiPlayIdmStateDecision EvaluateRegisterService(
        MiPlayContinuityNetBusRegisterServicePrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The NetBus serviceId argument is missing.");
        }

        if (!prerequisites.BinderTokenProvided)
        {
            return new MiPlayIdmStateDecision(false, "The caller Binder token is missing.");
        }

        if (!prerequisites.ResultReceiverProvided)
        {
            return new MiPlayIdmStateDecision(false, "The RegisterService ResultReceiver is missing.");
        }

        if (!prerequisites.AppInfoGeneratedFromBinderCaller)
        {
            return new MiPlayIdmStateDecision(false, "PackageUtil did not generate AppInfo from the Binder caller.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The serviceId-specific internal Continuity permission check failed.");
        }

        if (!prerequisites.ServiceTokenCanBeRegistered)
        {
            return new MiPlayIdmStateDecision(false, "The service token cannot be recorded for the caller.");
        }

        return new MiPlayIdmStateDecision(true, "The NetBus RegisterService Binder gates match the official path.");
    }

    public static MiPlayIdmStateDecision EvaluateStartAdvertising(
        MiPlayContinuityNetBusStartAdvertisingPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The NetBus advertising serviceId is missing.");
        }

        if (!prerequisites.ServiceTokenRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No registered service token exists for the serviceId.");
        }

        if (!prerequisites.AppInfoBoundToServiceId)
        {
            return new MiPlayIdmStateDecision(false, "No AppInfo binding exists for the serviceId.");
        }

        if (!prerequisites.StartAdvertisingOptionsProvided)
        {
            return new MiPlayIdmStateDecision(false, "StartAdvertisingOptionsV2 is missing.");
        }

        if (!prerequisites.AdvertisingDataProvided)
        {
            return new MiPlayIdmStateDecision(false, "The NetBus advertising data payload is missing.");
        }

        return new MiPlayIdmStateDecision(true, "The NetBus startAdvertising gates match a registered service.");
    }

    public static MiPlayIdmStateDecision EvaluateStaticServiceConfig(
        MiPlayContinuityNetBusStaticServicePrerequisites prerequisites)
    {
        if (!prerequisites.StaticConfigAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The static NetBus service config is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ConfigServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The static NetBus config does not provide a serviceId.");
        }

        if (!prerequisites.PackageSignatureAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The static NetBus path cannot build package-signature AppInfo.");
        }

        if (!prerequisites.RegisterServiceCompleted)
        {
            return new MiPlayIdmStateDecision(false, "The static NetBus path has not completed nativeRegisterService.");
        }

        return new MiPlayIdmStateDecision(true, "The static NetBus service config can proceed to its native operation.");
    }

    public static bool CanDeriveServiceIdFromDiscoveryIdentity(
        int applicationId,
        MiPlayIdmServiceType serviceType) =>
        applicationId == MiPlayMdnsCapabilities.MiPlayAudioApplicationId &&
        serviceType.ServiceName == MiPlayIdmServiceTypes.MiPlayAudioServiceName &&
        false;
}
