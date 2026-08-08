using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayIdmServerStateTests
{
    [Fact]
    public void IdmServerProcRegistrationRequiresCallerBoundClientIdAndV2Wrapper()
    {
        Assert.Equal(
            MiPlayIdmNativeWrapperVersion.V2,
            MiPlayIdmServerState.SelectNativeWrapperVersion(
                MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion));
        Assert.Equal(
            MiPlayIdmNativeWrapperVersion.V1,
            MiPlayIdmServerState.SelectNativeWrapperVersion(
                MiPlayIdmServerState.NativeV2SdkVersionThreshold));

        var accepted = MiPlayIdmServerState.EvaluateServerProcRegistration(
            new MiPlayIdmServerProcRegistrationPrerequisites(
                ClientId: "R4PX0HQT",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                CallerMatchesClientId: true,
                CallbackProvided: true,
                RegisterIdmServerParamParsed: true));

        Assert.True(accepted.CanProceed);

        var wrongCaller = MiPlayIdmServerState.EvaluateServerProcRegistration(
            new MiPlayIdmServerProcRegistrationPrerequisites(
                ClientId: "R4PX0HQT",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                CallerMatchesClientId: false,
                CallbackProvided: true,
                RegisterIdmServerParamParsed: true));
        var missingCallback = MiPlayIdmServerState.EvaluateServerProcRegistration(
            new MiPlayIdmServerProcRegistrationPrerequisites(
                ClientId: "R4PX0HQT",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                CallerMatchesClientId: true,
                CallbackProvided: false,
                RegisterIdmServerParamParsed: true));
        var nativeV1 = MiPlayIdmServerState.EvaluateServerProcRegistration(
            new MiPlayIdmServerProcRegistrationPrerequisites(
                ClientId: "R4PX0HQT",
                SdkVersionCode: MiPlayIdmServerState.NativeV2SdkVersionThreshold,
                CallerMatchesClientId: true,
                CallbackProvided: true,
                RegisterIdmServerParamParsed: true));

        Assert.False(wrongCaller.CanProceed);
        Assert.False(missingCallback.CanProceed);
        Assert.False(nativeV1.CanProceed);
    }

    [Fact]
    public void IdmNativeUpdateServiceRequiresRegisteredServerProcAndRuntimeServiceId()
    {
        var accepted = MiPlayIdmServerState.EvaluateNativeServiceUpdate(
            new MiPlayIdmNativeServiceUpdatePrerequisites(
                ClientId: "R4PX0HQT",
                ServiceId: "runtime-service-id",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                ServerProcRegisteredForClientId: true,
                UpdateServiceParamParsed: true));

        Assert.True(accepted.CanProceed);

        var missingServerProc = MiPlayIdmServerState.EvaluateNativeServiceUpdate(
            new MiPlayIdmNativeServiceUpdatePrerequisites(
                ClientId: "R4PX0HQT",
                ServiceId: "runtime-service-id",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                ServerProcRegisteredForClientId: false,
                UpdateServiceParamParsed: true));
        var missingServiceId = MiPlayIdmServerState.EvaluateNativeServiceUpdate(
            new MiPlayIdmNativeServiceUpdatePrerequisites(
                ClientId: "R4PX0HQT",
                ServiceId: "",
                SdkVersionCode: MiPlayIdmServerState.ObservedPersistentServiceManagerSdkVersion,
                ServerProcRegisteredForClientId: true,
                UpdateServiceParamParsed: true));

        Assert.False(missingServerProc.CanProceed);
        Assert.False(missingServiceId.CanProceed);
    }

    [Fact]
    public void AppMgrUpdateServiceIsSeparateFromIdmRuntimeServiceUpdate()
    {
        var accepted = MiPlayIdmServerState.EvaluateAppMgrServiceUpdate(
            new MiPlayIdmAppMgrServiceUpdatePrerequisites(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                CallbackRegistered: true,
                LocalAppServerExists: true,
                AlreadyAdvertising: true,
                DiscoveryTypeSupported: true,
                DiscoveryTypeIncludesIp: false));

        Assert.True(accepted.CanProceed);

        var notAdvertising = MiPlayIdmServerState.EvaluateAppMgrServiceUpdate(
            new MiPlayIdmAppMgrServiceUpdatePrerequisites(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                CallbackRegistered: true,
                LocalAppServerExists: true,
                AlreadyAdvertising: false,
                DiscoveryTypeSupported: true,
                DiscoveryTypeIncludesIp: false));
        var ipUpdate = MiPlayIdmServerState.EvaluateAppMgrServiceUpdate(
            new MiPlayIdmAppMgrServiceUpdatePrerequisites(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                CallbackRegistered: true,
                LocalAppServerExists: true,
                AlreadyAdvertising: true,
                DiscoveryTypeSupported: true,
                DiscoveryTypeIncludesIp: true));

        Assert.False(notAdvertising.CanProceed);
        Assert.False(ipUpdate.CanProceed);
    }

    [Fact]
    public void DiscoveryAppIdAndIdmServiceTypeDoNotDeriveRuntimeServiceId()
    {
        Assert.True(MiPlayIdmServiceType.TryParse(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var serviceType));
        Assert.NotNull(serviceType);

        Assert.Equal(5, MiPlayMdnsCapabilities.MiPlayAudioApplicationId);
        Assert.Equal(17_803, serviceType.TypeId);
        Assert.False(MiPlayIdmServerState.CanDeriveRuntimeServiceIdFromDiscoveryIdentity(
            MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            serviceType));
    }
}
