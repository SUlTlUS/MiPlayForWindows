using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityStaticNetworkingServiceConfigTests
{
    [Fact]
    public void StaticNetworkingConstantsMatchJadxAndNativeEvidence()
    {
        Assert.Equal("static_networking_service_list", MiPlayContinuityStaticNetworkingServiceConfig.StaticNetworkingServiceListResourceKey);
        Assert.Equal("static_networking_service_switch", MiPlayContinuityStaticNetworkingServiceConfig.StaticNetworkingServiceSwitchKey);
        Assert.Equal("static_enable_switch", MiPlayContinuityStaticNetworkingServiceConfig.StaticEnableSwitchKey);
        Assert.Equal("networking_service_list", MiPlayContinuityStaticNetworkingServiceConfig.RootTag);
        Assert.Equal("service", MiPlayContinuityStaticNetworkingServiceConfig.ServiceTag);

        Assert.Equal("serviceName", MiPlayContinuityStaticNetworkingServiceConfig.AttributeServiceName);
        Assert.Equal("serviceData", MiPlayContinuityStaticNetworkingServiceConfig.AttributeServiceData);
        Assert.Equal("notifyConnect", MiPlayContinuityStaticNetworkingServiceConfig.AttributeNotifyConnect);
        Assert.Equal("needAddService", MiPlayContinuityStaticNetworkingServiceConfig.AttributeNeedAddService);
        Assert.Equal("trustLevel", MiPlayContinuityStaticNetworkingServiceConfig.AttributeTrustLevel);
        Assert.Equal("syncCloud", MiPlayContinuityStaticNetworkingServiceConfig.AttributeSyncCloud);
        Assert.Equal("trustedTypes", MiPlayContinuityStaticNetworkingServiceConfig.AttributeTrustedTypes);
        Assert.Equal("switch", MiPlayContinuityStaticNetworkingServiceConfig.AttributeSwitch);

        Assert.Equal("com.xiaomi.continuity.action.REQUEST_CONNECTION", MiPlayContinuityStaticNetworkingServiceConfig.ActionRequestConnection);
        Assert.Equal("com.xiaomi.continuity.EXTRA_SERVICE_NAME", MiPlayContinuityStaticNetworkingServiceConfig.ExtraServiceName);
        Assert.Equal("com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL", MiPlayContinuityStaticNetworkingServiceConfig.BindContinuityServiceInternalPermission);

        Assert.Equal(0x10001A, MiPlayContinuityStaticNetworkingServiceConfig.NativeRegisterServerConnectionInitiationListenerApiStringOffset);
        Assert.Equal(0x1000CA, MiPlayContinuityStaticNetworkingServiceConfig.NativeUnregisterServerConnectionInitiationListenerApiStringOffset);
        Assert.Equal(0x14DBD6, MiPlayContinuityStaticNetworkingServiceConfig.NativeRegisterServerConnectionInitiationListenerJniStringOffset);
        Assert.Equal(0x14DDC3, MiPlayContinuityStaticNetworkingServiceConfig.NativeUnregisterServerConnectionInitiationListenerJniStringOffset);
        Assert.Equal(0x14DEEE, MiPlayContinuityStaticNetworkingServiceConfig.NativeHasServerConnectionListenerJniStringOffset);
    }

    [Fact]
    public void TrustLevelAndTrustedTypeStringsMatchNetworkingServiceConfigInfo()
    {
        Assert.Equal(16, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel("sameAccount"));
        Assert.Equal(40, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel("sharedAccount"));
        Assert.Equal(48, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel("everyOne"));
        Assert.Equal(32, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel("trustGroup"));
        Assert.Equal(32, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel("unknown"));
        Assert.Equal(32, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustLevel(null));

        Assert.Equal(1, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustedTypes("sameAccount"));
        Assert.Equal(8, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustedTypes("sharedAccount"));
        Assert.Equal(0, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustedTypes("trustGroup"));
        Assert.Equal(0, MiPlayContinuityStaticNetworkingServiceConfig.ToTrustedTypes(null));
    }

    [Fact]
    public void ParserRequiresServiceMetadataResourcePackageResourcesParsedServicesAndEnabledPackage()
    {
        var accepted = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateParser(
            new MiPlayContinuityStaticNetworkingParserPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsResourceKey: true,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedServiceCount: 1,
                PackageEnabled: true));

        Assert.True(accepted.CanProceed);

        var missingMetadata = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateParser(
            new MiPlayContinuityStaticNetworkingParserPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsResourceKey: false,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedServiceCount: 1,
                PackageEnabled: true));
        var zeroResource = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateParser(
            new MiPlayContinuityStaticNetworkingParserPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsResourceKey: true,
                ResourceId: 0,
                AppResourcesAvailable: true,
                ParsedServiceCount: 1,
                PackageEnabled: true));
        var disabledPackage = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateParser(
            new MiPlayContinuityStaticNetworkingParserPrerequisites(
                ServiceInfoProvided: true,
                MetadataContainsResourceKey: true,
                ResourceId: 123,
                AppResourcesAvailable: true,
                ParsedServiceCount: 1,
                PackageEnabled: false));

        Assert.False(missingMetadata.CanProceed);
        Assert.False(zeroResource.CanProceed);
        Assert.False(disabledPackage.CanProceed);
    }

    [Fact]
    public void StaticNetworkingServiceNameIsPackageNamePlusConfiguredServiceName()
    {
        Assert.True(MiPlayContinuityStaticNetworkingServiceConfig.TryBuildServiceName(
            "com.xiaomi.music",
            "miplay-audio",
            out var serviceName));

        Assert.NotNull(serviceName);
        Assert.Equal("com.xiaomi.music:miplay-audio", serviceName.ToMergedString());

        Assert.False(MiPlayContinuityStaticNetworkingServiceConfig.TryBuildServiceName(
            "",
            "miplay-audio",
            out var missingPackage));
        Assert.Null(missingPackage);

        Assert.False(MiPlayContinuityStaticNetworkingServiceConfig.TryBuildServiceName(
            "com.xiaomi.music",
            "",
            out var missingServiceName));
        Assert.Null(missingServiceName);
    }

    [Fact]
    public void BusinessServicePublishRequiresNeedAddServiceProxyAndAppInfo()
    {
        var accepted = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateBusinessServicePublish(
            new MiPlayContinuityStaticNetworkingBusinessServicePrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NeedAddService: true,
                ServiceStaticConfigProxyEnabled: true,
                AppInfoGenerated: true));
        var notAdding = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateBusinessServicePublish(
            new MiPlayContinuityStaticNetworkingBusinessServicePrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NeedAddService: false,
                ServiceStaticConfigProxyEnabled: true,
                AppInfoGenerated: true));
        var missingAppInfo = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateBusinessServicePublish(
            new MiPlayContinuityStaticNetworkingBusinessServicePrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NeedAddService: true,
                ServiceStaticConfigProxyEnabled: true,
                AppInfoGenerated: false));

        Assert.True(accepted.CanProceed);
        Assert.False(notAdding.CanProceed);
        Assert.False(missingAppInfo.CanProceed);
    }

    [Fact]
    public void NotifyConnectRegistrationRequiresConfigFlagPermissionAndEnabledPackage()
    {
        var accepted = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectRegistration(
            new MiPlayContinuityNotifyConnectRegistrationPrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NotifyConnect: true,
                InternalBindPermissionGranted: true,
                TrustLevel: 32));
        var noNotifyConnect = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectRegistration(
            new MiPlayContinuityNotifyConnectRegistrationPrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NotifyConnect: false,
                InternalBindPermissionGranted: true,
                TrustLevel: 32));
        var noPermission = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectRegistration(
            new MiPlayContinuityNotifyConnectRegistrationPrerequisites(
                PackageName: "com.xiaomi.music",
                ServiceName: "miplay-audio",
                PackageEnabled: true,
                NotifyConnect: true,
                InternalBindPermissionGranted: false,
                TrustLevel: 32));

        Assert.True(accepted.CanProceed);
        Assert.False(noNotifyConnect.CanProceed);
        Assert.False(noPermission.CanProceed);
        Assert.False(MiPlayContinuityStaticNetworkingServiceConfig.NotifyConnectRegistrationIsRegisterChannelListenerV2());
    }

    [Fact]
    public void NotifyConnectDispatchRequiresNoNativeServerListenerMapEntryAndTrustMatch()
    {
        var accepted = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectDispatch(
            new MiPlayContinuityNotifyConnectDispatchPrerequisites(
                DeviceId: "remote-device",
                NativeCallbackMergedServiceName: "com.xiaomi.music:miplay-audio",
                NativeAlreadyHasServerConnectionListener: false,
                ServiceNameMappedToComponent: true,
                ConnectionTrustLevel: 16,
                ComponentTrustLevel: 32));
        var nativeListenerAlreadyExists = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectDispatch(
            new MiPlayContinuityNotifyConnectDispatchPrerequisites(
                DeviceId: "remote-device",
                NativeCallbackMergedServiceName: "com.xiaomi.music:miplay-audio",
                NativeAlreadyHasServerConnectionListener: true,
                ServiceNameMappedToComponent: true,
                ConnectionTrustLevel: 16,
                ComponentTrustLevel: 32));
        var missingMap = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectDispatch(
            new MiPlayContinuityNotifyConnectDispatchPrerequisites(
                DeviceId: "remote-device",
                NativeCallbackMergedServiceName: "com.xiaomi.music:miplay-audio",
                NativeAlreadyHasServerConnectionListener: false,
                ServiceNameMappedToComponent: false,
                ConnectionTrustLevel: 16,
                ComponentTrustLevel: 32));
        var trustMismatch = MiPlayContinuityStaticNetworkingServiceConfig.EvaluateNotifyConnectDispatch(
            new MiPlayContinuityNotifyConnectDispatchPrerequisites(
                DeviceId: "remote-device",
                NativeCallbackMergedServiceName: "com.xiaomi.music:miplay-audio",
                NativeAlreadyHasServerConnectionListener: false,
                ServiceNameMappedToComponent: true,
                ConnectionTrustLevel: 48,
                ComponentTrustLevel: 32));

        Assert.True(accepted.CanProceed);
        Assert.False(nativeListenerAlreadyExists.CanProceed);
        Assert.False(missingMap.CanProceed);
        Assert.False(trustMismatch.CanProceed);
    }

    [Fact]
    public void NotifyConnectIntentDoesNotExplainLegacyTcpGetDeviceInfoSuccess()
    {
        Assert.False(MiPlayContinuityStaticNetworkingServiceConfig.NotifyConnectCanExplainLegacyTcpGetDeviceInfoSuccess(
            actionRequestConnectionDelivered: true));
        Assert.False(MiPlayContinuityStaticNetworkingServiceConfig.NotifyConnectCanExplainLegacyTcpGetDeviceInfoSuccess(
            actionRequestConnectionDelivered: false));
    }
}
