namespace DLNACast.Core.MiPlay;

public enum MiPlayContinuityStaticNetworkingTrustLevel
{
    SameAccount = 16,
    TrustGroupOrDefault = 32,
    SharedAccount = 40,
    EveryOne = 48,
}

public enum MiPlayContinuityStaticNetworkingTrustedTypes
{
    None = 0,
    SameAccount = 1,
    SharedAccount = 8,
}

public sealed record MiPlayContinuityStaticNetworkingParserPrerequisites(
    bool ServiceInfoProvided,
    bool MetadataContainsResourceKey,
    int ResourceId,
    bool AppResourcesAvailable,
    int ParsedServiceCount,
    bool PackageEnabled);

public sealed record MiPlayContinuityStaticNetworkingBusinessServicePrerequisites(
    string? PackageName,
    string? ServiceName,
    bool PackageEnabled,
    bool NeedAddService,
    bool ServiceStaticConfigProxyEnabled,
    bool AppInfoGenerated);

public sealed record MiPlayContinuityNotifyConnectRegistrationPrerequisites(
    string? PackageName,
    string? ServiceName,
    bool PackageEnabled,
    bool NotifyConnect,
    bool InternalBindPermissionGranted,
    int TrustLevel);

public sealed record MiPlayContinuityNotifyConnectDispatchPrerequisites(
    string? DeviceId,
    string? NativeCallbackMergedServiceName,
    bool NativeAlreadyHasServerConnectionListener,
    bool ServiceNameMappedToComponent,
    int ConnectionTrustLevel,
    int ComponentTrustLevel);

/// <summary>
/// Offline model for the static networking-service path in Mi Connect Service
/// 5.1.251.10. The path loads another package's
/// static_networking_service_list XML and can register a server connection
/// initiation listener. It is distinct from registerChannelListenerV2 and from
/// legacy TCP 8899 SafetyData getDeviceInfo.
/// </summary>
public static class MiPlayContinuityStaticNetworkingServiceConfig
{
    public const string StaticNetworkingServiceListResourceKey = "static_networking_service_list";
    public const string StaticNetworkingServiceSwitchKey = "static_networking_service_switch";
    public const string StaticEnableSwitchKey = "static_enable_switch";
    public const string RootTag = "networking_service_list";
    public const string ServiceTag = "service";

    public const string AttributeServiceName = "serviceName";
    public const string AttributeServiceData = "serviceData";
    public const string AttributeNotifyConnect = "notifyConnect";
    public const string AttributeNeedAddService = "needAddService";
    public const string AttributeTrustLevel = "trustLevel";
    public const string AttributeSyncCloud = "syncCloud";
    public const string AttributeTrustedTypes = "trustedTypes";
    public const string AttributeSwitch = "switch";

    public const string ActionRequestConnection = "com.xiaomi.continuity.action.REQUEST_CONNECTION";
    public const string ExtraServiceName = "com.xiaomi.continuity.EXTRA_SERVICE_NAME";
    public const string BindContinuityServiceInternalPermission =
        "com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL";

    public const long NativeRegisterServerConnectionInitiationListenerApiStringOffset = 0x10001A;
    public const long NativeUnregisterServerConnectionInitiationListenerApiStringOffset = 0x1000CA;
    public const long NativeRegisterServerConnectionInitiationListenerJniStringOffset = 0x14DBD6;
    public const long NativeUnregisterServerConnectionInitiationListenerJniStringOffset = 0x14DDC3;
    public const long NativeHasServerConnectionListenerJniStringOffset = 0x14DEEE;

    public static int ToTrustLevel(string? value) => value switch
    {
        "sameAccount" => (int)MiPlayContinuityStaticNetworkingTrustLevel.SameAccount,
        "sharedAccount" => (int)MiPlayContinuityStaticNetworkingTrustLevel.SharedAccount,
        "everyOne" => (int)MiPlayContinuityStaticNetworkingTrustLevel.EveryOne,
        _ => (int)MiPlayContinuityStaticNetworkingTrustLevel.TrustGroupOrDefault,
    };

    public static int ToTrustedTypes(string? value) => value switch
    {
        "sameAccount" => (int)MiPlayContinuityStaticNetworkingTrustedTypes.SameAccount,
        "sharedAccount" => (int)MiPlayContinuityStaticNetworkingTrustedTypes.SharedAccount,
        _ => (int)MiPlayContinuityStaticNetworkingTrustedTypes.None,
    };

    public static bool TryBuildServiceName(
        string? packageName,
        string? serviceName,
        out MiPlayContinuityServiceName? continuityServiceName)
    {
        continuityServiceName = null;
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        continuityServiceName = new MiPlayContinuityServiceName(packageName, serviceName);
        return true;
    }

    public static MiPlayIdmStateDecision EvaluateParser(
        MiPlayContinuityStaticNetworkingParserPrerequisites prerequisites)
    {
        if (!prerequisites.ServiceInfoProvided)
        {
            return new MiPlayIdmStateDecision(false, "No Android ServiceInfo is available for static networking parsing.");
        }

        if (!prerequisites.MetadataContainsResourceKey)
        {
            return new MiPlayIdmStateDecision(false, "ServiceInfo metadata does not contain static_networking_service_list.");
        }

        if (prerequisites.ResourceId == 0)
        {
            return new MiPlayIdmStateDecision(false, "The static networking-service resource id is zero.");
        }

        if (!prerequisites.AppResourcesAvailable)
        {
            return new MiPlayIdmStateDecision(false, "PackageUtil could not load the business package resources.");
        }

        if (prerequisites.ParsedServiceCount <= 0)
        {
            return new MiPlayIdmStateDecision(false, "The parsed static networking-service list is empty.");
        }

        if (!prerequisites.PackageEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The package is disabled; static service and notify-connect state are removed.");
        }

        return new MiPlayIdmStateDecision(true, "The static networking-service XML can be processed for this package.");
    }

    public static MiPlayIdmStateDecision EvaluateBusinessServicePublish(
        MiPlayContinuityStaticNetworkingBusinessServicePrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.PackageName) ||
            string.IsNullOrWhiteSpace(prerequisites.ServiceName))
        {
            return new MiPlayIdmStateDecision(false, "Package name or networking serviceName is missing.");
        }

        if (!prerequisites.PackageEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The package is disabled; business service info is removed.");
        }

        if (!prerequisites.NeedAddService)
        {
            return new MiPlayIdmStateDecision(false, "The static networking-service config disables DevRepo addServiceInfo.");
        }

        if (!prerequisites.ServiceStaticConfigProxyEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The ServiceStaticConfigProxy is not enabled.");
        }

        if (!prerequisites.AppInfoGenerated)
        {
            return new MiPlayIdmStateDecision(false, "PackageUtil did not generate AppInfo for DevRepo addServiceInfo.");
        }

        return new MiPlayIdmStateDecision(true, "The static path can publish BusinessServiceInfo to the device repository.");
    }

    public static MiPlayIdmStateDecision EvaluateNotifyConnectRegistration(
        MiPlayContinuityNotifyConnectRegistrationPrerequisites prerequisites)
    {
        if (!TryBuildServiceName(prerequisites.PackageName, prerequisites.ServiceName, out _))
        {
            return new MiPlayIdmStateDecision(false, "The notify-connect ServiceName cannot be built from packageName and serviceName.");
        }

        if (!prerequisites.PackageEnabled)
        {
            return new MiPlayIdmStateDecision(false, "The package is disabled; notify-connect listener is unregistered.");
        }

        if (!prerequisites.NotifyConnect)
        {
            return new MiPlayIdmStateDecision(false, "notifyConnect is false in the static networking-service config.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The business service lacks BIND_CONTINUITY_SERVICE_INTERNAL.");
        }

        return new MiPlayIdmStateDecision(true, "The static path can register nativeRegisterServerConnectionInitiationListener.");
    }

    public static MiPlayIdmStateDecision EvaluateNotifyConnectDispatch(
        MiPlayContinuityNotifyConnectDispatchPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "The native connection initiation callback did not provide a device id.");
        }

        if (prerequisites.NativeAlreadyHasServerConnectionListener)
        {
            return new MiPlayIdmStateDecision(false, "NotifyConnectHelper ignores the callback when a native server connection listener already exists.");
        }

        if (!MiPlayContinuityServiceName.TryParseApkMergedString(
            prerequisites.NativeCallbackMergedServiceName,
            out var serviceName) ||
            serviceName is null)
        {
            return new MiPlayIdmStateDecision(false, "The native callback serviceName cannot be parsed.");
        }

        if (!prerequisites.ServiceNameMappedToComponent)
        {
            return new MiPlayIdmStateDecision(false, "The parsed ServiceName is not mapped to a static business component.");
        }

        if (prerequisites.ConnectionTrustLevel > prerequisites.ComponentTrustLevel)
        {
            return new MiPlayIdmStateDecision(false, "The incoming connection trustLevel is higher than the static component trustLevel.");
        }

        return new MiPlayIdmStateDecision(true, "NotifyConnectHelper can dispatch ACTION_REQUEST_CONNECTION to the static business service.");
    }

    public static bool NotifyConnectRegistrationIsRegisterChannelListenerV2() => false;

    public static bool NotifyConnectCanExplainLegacyTcpGetDeviceInfoSuccess(
        bool actionRequestConnectionDelivered) =>
        actionRequestConnectionDelivered && false;
}
