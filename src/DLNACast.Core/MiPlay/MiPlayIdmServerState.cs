namespace DLNACast.Core.MiPlay;

public enum MiPlayIdmNativeWrapperVersion
{
    V1 = 1,
    V2 = 2,
}

public sealed record MiPlayIdmStateDecision(bool CanProceed, string Reason);

public sealed record MiPlayIdmServerProcRegistrationPrerequisites(
    string? ClientId,
    int SdkVersionCode,
    bool CallerMatchesClientId,
    bool CallbackProvided,
    bool RegisterIdmServerParamParsed);

public sealed record MiPlayIdmNativeServiceUpdatePrerequisites(
    string? ClientId,
    string? ServiceId,
    int SdkVersionCode,
    bool ServerProcRegisteredForClientId,
    bool UpdateServiceParamParsed);

public sealed record MiPlayIdmAppMgrServiceUpdatePrerequisites(
    int ApplicationId,
    bool CallbackRegistered,
    bool LocalAppServerExists,
    bool AlreadyAdvertising,
    bool DiscoveryTypeSupported,
    bool DiscoveryTypeIncludesIp);

/// <summary>
/// Offline model for the IDM server/update gates observed in Mi Connect
/// Service 5.1.251.10. This does not register an IDM service and is not used
/// by Probe; it only preserves static JNI/JADX evidence as testable state.
/// </summary>
public static class MiPlayIdmServerState
{
    public const int NativeV2SdkVersionThreshold = 1_005_000;
    public const int ObservedPersistentServiceManagerSdkVersion = 5_000_101;

    public const long NativeRegisterIotServiceJniAddress = 0x3A1C0;
    public const long NativeRegisterIdmServerJniAddress = 0x41108;
    public const long NativeUpdateCloudCtrlServiceConfigsJniAddress = 0x403B8;
    public const long NativeUpdateServiceJniAddress = 0x431F4;
    public const long GetCloudCtrlServiceConfigsNativeAddress = 0x40728;

    public static MiPlayIdmNativeWrapperVersion SelectNativeWrapperVersion(int sdkVersionCode) =>
        sdkVersionCode > NativeV2SdkVersionThreshold
            ? MiPlayIdmNativeWrapperVersion.V2
            : MiPlayIdmNativeWrapperVersion.V1;

    public static MiPlayIdmStateDecision EvaluateServerProcRegistration(
        MiPlayIdmServerProcRegistrationPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ClientId))
        {
            return new MiPlayIdmStateDecision(false, "The IDM server clientId is missing.");
        }

        if (!prerequisites.CallerMatchesClientId)
        {
            return new MiPlayIdmStateDecision(false, "The Binder caller is not matched to the IDM server clientId.");
        }

        if (!prerequisites.CallbackProvided)
        {
            return new MiPlayIdmStateDecision(false, "The IDM service process callback is missing.");
        }

        if (!prerequisites.RegisterIdmServerParamParsed)
        {
            return new MiPlayIdmStateDecision(false, "The RegisterIDMServer protobuf parameter was not parsed.");
        }

        if (SelectNativeWrapperVersion(prerequisites.SdkVersionCode) != MiPlayIdmNativeWrapperVersion.V2)
        {
            return new MiPlayIdmStateDecision(false, "The APK marks native V1 server registration as unreachable for this path.");
        }

        return new MiPlayIdmStateDecision(true, "The IDM server process registration gates match the native V2 path.");
    }

    public static MiPlayIdmStateDecision EvaluateNativeServiceUpdate(
        MiPlayIdmNativeServiceUpdatePrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ClientId))
        {
            return new MiPlayIdmStateDecision(false, "The IDM update clientId is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The IDM update serviceId is missing.");
        }

        if (!prerequisites.ServerProcRegisteredForClientId)
        {
            return new MiPlayIdmStateDecision(false, "No IDM server process is registered for the clientId.");
        }

        if (!prerequisites.UpdateServiceParamParsed)
        {
            return new MiPlayIdmStateDecision(false, "The UpdateServiceParam protobuf parameter was not parsed.");
        }

        if (SelectNativeWrapperVersion(prerequisites.SdkVersionCode) != MiPlayIdmNativeWrapperVersion.V2)
        {
            return new MiPlayIdmStateDecision(false, "The APK marks native V1 service update as unreachable for this path.");
        }

        return new MiPlayIdmStateDecision(true, "The native IDM updateService gates match the registered V2 server path.");
    }

    public static MiPlayIdmStateDecision EvaluateAppMgrServiceUpdate(
        MiPlayIdmAppMgrServiceUpdatePrerequisites prerequisites)
    {
        if (prerequisites.ApplicationId != MiPlayMdnsCapabilities.MiPlayAudioApplicationId)
        {
            return new MiPlayIdmStateDecision(false, "The AppMgr update is not for the MiPlay audio application id.");
        }

        if (!prerequisites.CallbackRegistered)
        {
            return new MiPlayIdmStateDecision(false, "The AppMgr callback for the application id is missing.");
        }

        if (!prerequisites.LocalAppServerExists)
        {
            return new MiPlayIdmStateDecision(false, "The LocalAppServer for the application id is missing.");
        }

        if (!prerequisites.AlreadyAdvertising)
        {
            return new MiPlayIdmStateDecision(false, "The LocalAppServer is not advertising yet.");
        }

        if (!prerequisites.DiscoveryTypeSupported)
        {
            return new MiPlayIdmStateDecision(false, "The requested AppMgr discovery type is not supported.");
        }

        if (prerequisites.DiscoveryTypeIncludesIp)
        {
            return new MiPlayIdmStateDecision(false, "The Java AppMgr update path rejects IP discovery updates.");
        }

        return new MiPlayIdmStateDecision(true, "The AppMgr update gates match an existing advertising LocalAppServer.");
    }

    public static bool CanDeriveRuntimeServiceIdFromDiscoveryIdentity(
        int applicationId,
        MiPlayIdmServiceType serviceType) =>
        applicationId == MiPlayMdnsCapabilities.MiPlayAudioApplicationId &&
        serviceType.ServiceName == MiPlayIdmServiceTypes.MiPlayAudioServiceName &&
        false;
}
