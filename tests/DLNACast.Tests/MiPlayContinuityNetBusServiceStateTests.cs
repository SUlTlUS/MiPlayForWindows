using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityNetBusServiceStateTests
{
    [Fact]
    public void RegisterServiceRequiresServiceIdCallerAppInfoPermissionAndToken()
    {
        var accepted = MiPlayContinuityNetBusServiceState.EvaluateRegisterService(
            new MiPlayContinuityNetBusRegisterServicePrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                BinderTokenProvided: true,
                ResultReceiverProvided: true,
                AppInfoGeneratedFromBinderCaller: true,
                InternalBindPermissionGranted: true,
                ServiceTokenCanBeRegistered: true));

        Assert.True(accepted.CanProceed);

        var missingServiceId = MiPlayContinuityNetBusServiceState.EvaluateRegisterService(
            new MiPlayContinuityNetBusRegisterServicePrerequisites(
                ServiceId: "",
                BinderTokenProvided: true,
                ResultReceiverProvided: true,
                AppInfoGeneratedFromBinderCaller: true,
                InternalBindPermissionGranted: true,
                ServiceTokenCanBeRegistered: true));
        var missingAppInfo = MiPlayContinuityNetBusServiceState.EvaluateRegisterService(
            new MiPlayContinuityNetBusRegisterServicePrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                BinderTokenProvided: true,
                ResultReceiverProvided: true,
                AppInfoGeneratedFromBinderCaller: false,
                InternalBindPermissionGranted: true,
                ServiceTokenCanBeRegistered: true));
        var missingPermission = MiPlayContinuityNetBusServiceState.EvaluateRegisterService(
            new MiPlayContinuityNetBusRegisterServicePrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                BinderTokenProvided: true,
                ResultReceiverProvided: true,
                AppInfoGeneratedFromBinderCaller: true,
                InternalBindPermissionGranted: false,
                ServiceTokenCanBeRegistered: true));

        Assert.False(missingServiceId.CanProceed);
        Assert.False(missingAppInfo.CanProceed);
        Assert.False(missingPermission.CanProceed);
    }

    [Fact]
    public void StartAdvertisingRequiresRegisteredServiceTokenAndAdvertisingPayload()
    {
        var accepted = MiPlayContinuityNetBusServiceState.EvaluateStartAdvertising(
            new MiPlayContinuityNetBusStartAdvertisingPrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                ServiceTokenRegistered: true,
                AppInfoBoundToServiceId: true,
                StartAdvertisingOptionsProvided: true,
                AdvertisingDataProvided: true));

        Assert.True(accepted.CanProceed);

        var missingToken = MiPlayContinuityNetBusServiceState.EvaluateStartAdvertising(
            new MiPlayContinuityNetBusStartAdvertisingPrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                ServiceTokenRegistered: false,
                AppInfoBoundToServiceId: true,
                StartAdvertisingOptionsProvided: true,
                AdvertisingDataProvided: true));
        var missingData = MiPlayContinuityNetBusServiceState.EvaluateStartAdvertising(
            new MiPlayContinuityNetBusStartAdvertisingPrerequisites(
                ServiceId: "com.xiaomi.miplay.audio",
                ServiceTokenRegistered: true,
                AppInfoBoundToServiceId: true,
                StartAdvertisingOptionsProvided: true,
                AdvertisingDataProvided: false));

        Assert.False(missingToken.CanProceed);
        Assert.False(missingData.CanProceed);
    }

    [Fact]
    public void StaticServiceConfigUsesConfigSuppliedServiceIdAndPackageSignature()
    {
        var accepted = MiPlayContinuityNetBusServiceState.EvaluateStaticServiceConfig(
            new MiPlayContinuityNetBusStaticServicePrerequisites(
                ConfigServiceId: "com.xiaomi.miplay.audio",
                StaticConfigAvailable: true,
                PackageSignatureAvailable: true,
                RegisterServiceCompleted: true));

        Assert.True(accepted.CanProceed);

        var missingConfigServiceId = MiPlayContinuityNetBusServiceState.EvaluateStaticServiceConfig(
            new MiPlayContinuityNetBusStaticServicePrerequisites(
                ConfigServiceId: "",
                StaticConfigAvailable: true,
                PackageSignatureAvailable: true,
                RegisterServiceCompleted: true));
        var missingPackageSignature = MiPlayContinuityNetBusServiceState.EvaluateStaticServiceConfig(
            new MiPlayContinuityNetBusStaticServicePrerequisites(
                ConfigServiceId: "com.xiaomi.miplay.audio",
                StaticConfigAvailable: true,
                PackageSignatureAvailable: false,
                RegisterServiceCompleted: true));

        Assert.False(missingConfigServiceId.CanProceed);
        Assert.False(missingPackageSignature.CanProceed);
    }

    [Fact]
    public void IpcRegisterAndUpdateFieldNumbersMatchApkEvidence()
    {
        Assert.Equal(1, MiPlayContinuityNetBusServiceState.IpcRegisterServiceServiceProtoFieldNumber);
        Assert.Equal(2, MiPlayContinuityNetBusServiceState.IpcRegisterServiceIntentStringFieldNumber);
        Assert.Equal(3, MiPlayContinuityNetBusServiceState.IpcRegisterServiceIntentTypeFieldNumber);
        Assert.Equal(4, MiPlayContinuityNetBusServiceState.IpcRegisterServiceDiscoveryTypeFieldNumber);
        Assert.Equal(5, MiPlayContinuityNetBusServiceState.IpcRegisterServiceCommunicationTypeFieldNumber);
        Assert.Equal(6, MiPlayContinuityNetBusServiceState.IpcRegisterServiceSecurityTypeFieldNumber);
        Assert.Equal(7, MiPlayContinuityNetBusServiceState.IpcRegisterServicePrivateDataFieldNumber);
        Assert.Equal(8, MiPlayContinuityNetBusServiceState.IpcRegisterServiceAppParamFieldNumber);

        Assert.Equal(1, MiPlayContinuityNetBusServiceState.IpcUpdateServiceDiscoveryTypeFieldNumber);
        Assert.Equal(2, MiPlayContinuityNetBusServiceState.IpcUpdateServiceAdvertisingModeFieldNumber);
        Assert.Equal(3, MiPlayContinuityNetBusServiceState.IpcUpdateServiceUpdateAppDataFieldNumber);
        Assert.Equal(4, MiPlayContinuityNetBusServiceState.IpcUpdateServiceAppDataFieldNumber);
        Assert.Equal(5, MiPlayContinuityNetBusServiceState.IpcUpdateServiceUpdateStrategyFieldNumber);
        Assert.Equal(6, MiPlayContinuityNetBusServiceState.IpcUpdateServiceCommunicationTypeFieldNumber);
        Assert.Equal(7, MiPlayContinuityNetBusServiceState.IpcUpdateServiceUpdateTypeFieldNumber);
        Assert.Equal(8, MiPlayContinuityNetBusServiceState.IpcUpdateServiceAdvertisingModeScreenOffFieldNumber);
    }

    [Fact]
    public void DiscoveryAppIdAndIdmUrnDoNotDeriveNetBusServiceId()
    {
        Assert.True(MiPlayIdmServiceType.TryParse(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var serviceType));
        Assert.NotNull(serviceType);

        Assert.False(MiPlayContinuityNetBusServiceState.CanDeriveServiceIdFromDiscoveryIdentity(
            MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            serviceType));
    }
}
