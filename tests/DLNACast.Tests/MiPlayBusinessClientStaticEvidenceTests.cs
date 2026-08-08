using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayBusinessClientStaticEvidenceTests
{
    [Fact]
    public void ConstantsCaptureCurrentMiConnectServiceAndMiPlayBusinessSearchKeys()
    {
        Assert.Equal("com.xiaomi.continuity.service", MiPlayBusinessClientStaticEvidence.CurrentMiConnectServiceLibraryNamespace);
        Assert.Equal("5.1.251.10.fullCnRelease.0616209", MiPlayBusinessClientStaticEvidence.CurrentMiConnectServiceVersionName);
        Assert.Equal("fullCn", MiPlayBusinessClientStaticEvidence.CurrentMiConnectServiceFlavor);

        Assert.Equal("00017803", MiPlayBusinessClientStaticEvidence.MiPlayStaticDiscoveryServiceId);
        Assert.Equal("17803", MiPlayBusinessClientStaticEvidence.MiPlayShortDiscoveryServiceId);
        Assert.Equal(MiPlayIdmServiceTypes.MiPlayAudioUrn, MiPlayBusinessClientStaticEvidence.MiPlayAudioUrn);
        Assert.Equal("static_disc_filter", MiPlayBusinessClientStaticEvidence.StaticDiscFilterResourceKey);
        Assert.Equal("static_networking_service_list", MiPlayBusinessClientStaticEvidence.StaticNetworkingServiceListResourceKey);
        Assert.Equal(
            "com.xiaomi.permission.BIND_CONTINUITY_SERVICE_INTERNAL",
            MiPlayBusinessClientStaticEvidence.BindContinuityServiceInternalPermission);
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_DEVICE_FOUND",
            MiPlayBusinessClientStaticEvidence.NetbusDiscDeviceFoundAction);
        Assert.Equal(
            "com.xiaomi.continuity.action.NETBUS_DISC_RECEIVE_DATA",
            MiPlayBusinessClientStaticEvidence.NetbusDiscReceiveDataAction);
        Assert.Equal("device.DEVICE_INFO_V2", MiPlayBusinessClientStaticEvidence.DeviceInfoV2Feature);
    }

    [Fact]
    public void CurrentMiConnectServiceApkSnapshotIsPlatformOnlyAndCannotIdentifyBusinessClient()
    {
        var snapshot = MiPlayBusinessClientStaticEvidence.CreateCurrentMiConnectServiceApkSnapshot();

        Assert.Equal(MiPlayBusinessClientArtifactRole.ContinuityPlatformService, snapshot.ArtifactRole);
        Assert.Equal("com.xiaomi.continuity.service", snapshot.PackageNameOrLibraryNamespace);
        Assert.True(snapshot.PlatformContinuityManagersObserved);
        Assert.True(snapshot.StaticDiscoveryFrameworkObserved);
        Assert.True(snapshot.StaticNetworkingFrameworkObserved);
        Assert.False(snapshot.DecodedAndroidManifestAvailable);
        Assert.False(snapshot.DecodedBusinessResourcesAvailable);
        Assert.False(snapshot.BusinessStaticDiscFilterXmlObserved);
        Assert.False(snapshot.BusinessNetbusDiscoveryIntentReceiverObserved);
        Assert.False(snapshot.BusinessExplicitDiscoveryListenerRegistrationObserved);
        Assert.False(snapshot.BusinessDeviceInfoV2ContextObserved);
        Assert.False(snapshot.BusinessConnectionOrChannelListenerObserved);
        Assert.False(snapshot.LegacyTcp8899CommandBridgeObserved);

        Assert.False(MiPlayBusinessClientStaticEvidence.CurrentMiConnectServiceApkCanIdentifyBusinessClient());

        var decision = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(snapshot);
        Assert.False(decision.CanProceed);
        Assert.Contains("not identified", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessClientStaticEvidenceRequiresManifestResourcesAndBusinessConfig()
    {
        var complete = CreateCompleteBusinessEvidence();

        Assert.True(MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(complete).CanProceed);

        var missingManifest = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with { DecodedAndroidManifestAvailable = false });
        var missingResources = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with { DecodedBusinessResourcesAvailable = false });
        var missingConfig = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with
            {
                BusinessStaticDiscFilterMetadataObserved = false,
                BusinessStaticDiscFilterXmlObserved = false,
                BusinessStaticNetworkingServiceListMetadataObserved = false,
                BusinessStaticNetworkingServiceListXmlObserved = false,
                BusinessExplicitDiscoveryListenerRegistrationObserved = false,
            });

        Assert.False(missingManifest.CanProceed);
        Assert.Contains("AndroidManifest", missingManifest.Reason, StringComparison.Ordinal);
        Assert.False(missingResources.CanProceed);
        Assert.Contains("resources", missingResources.Reason, StringComparison.Ordinal);
        Assert.False(missingConfig.CanProceed);
        Assert.Contains("static discovery/networking", missingConfig.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessClientStaticEvidenceRequiresMiPlayServiceIdAndDiscoveryDeliveryPath()
    {
        var complete = CreateCompleteBusinessEvidence();

        Assert.Equal("00017803", MiPlayBusinessClientStaticEvidence.NormalizeMiPlayServiceId("17803"));
        Assert.Equal("00017803", MiPlayBusinessClientStaticEvidence.NormalizeMiPlayServiceId("00017803"));
        Assert.Null(MiPlayBusinessClientStaticEvidence.NormalizeMiPlayServiceId("123456789"));

        var wrongServiceId = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with { BusinessMiPlayServiceId = "00017802" });
        var missingReceiverAndListener = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with
            {
                BusinessNetbusDiscoveryIntentReceiverObserved = false,
                BusinessExplicitDiscoveryListenerRegistrationObserved = false,
            });

        Assert.False(wrongServiceId.CanProceed);
        Assert.Contains("00017803", wrongServiceId.Reason, StringComparison.Ordinal);
        Assert.False(missingReceiverAndListener.CanProceed);
        Assert.Contains("NETBUS_DISC", missingReceiverAndListener.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessClientStaticEvidenceRequiresDeviceContextAndConnectionOrChannelListener()
    {
        var complete = CreateCompleteBusinessEvidence();

        var missingDeviceContext = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with { BusinessDeviceInfoV2ContextObserved = false });
        var missingConnectionOrChannel = MiPlayBusinessClientStaticEvidence.EvaluateBusinessClientStaticEvidence(
            complete with { BusinessConnectionOrChannelListenerObserved = false });

        Assert.False(missingDeviceContext.CanProceed);
        Assert.Contains("DeviceInfoV2", missingDeviceContext.Reason, StringComparison.Ordinal);
        Assert.False(missingConnectionOrChannel.CanProceed);
        Assert.Contains("connection or channel", missingConnectionOrChannel.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PostAuthContextRequiresSourceIdentityDeviceContextListenerTimingModeAndReadOnlyBoundary()
    {
        var complete = CreateCompletePostAuthContext();

        Assert.True(MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(complete).CanProceed);

        var missingSourceIdentity = MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(
            complete with { SourcePackageIdentityAvailable = false });
        var missingDeviceContext = MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(
            complete with { DeviceInfoV2OrDeviceIdAvailable = false });
        var missingTiming = MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(
            complete with { DiscoveryOrStaticListenerRegisteredBeforeGetDeviceInfo = false });
        var unknownMode = MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(
            complete with { ConnectionMode = MiPlayPostAuthConnectionMode.Unknown });
        var notReadOnly = MiPlayBusinessClientStaticEvidence.EvaluatePostAuthContext(
            complete with { ReadOnlyProbeBoundary = false });

        Assert.False(missingSourceIdentity.CanProceed);
        Assert.False(missingDeviceContext.CanProceed);
        Assert.False(missingTiming.CanProceed);
        Assert.False(unknownMode.CanProceed);
        Assert.False(notReadOnly.CanProceed);
    }

    [Fact]
    public void LegacyTcp8899ReprobeRequiresExplicitBusinessBridgeEvenWhenOtherContextIsComplete()
    {
        var staticEvidence = CreateCompleteBusinessEvidence();
        var postAuthContext = CreateCompletePostAuthContext();

        Assert.False(MiPlayBusinessClientStaticEvidence.CanJustifyLegacyTcp8899GetDeviceInfoReprobe(
            staticEvidence with { LegacyTcp8899CommandBridgeObserved = false },
            postAuthContext));
        Assert.False(MiPlayBusinessClientStaticEvidence.CanJustifyLegacyTcp8899GetDeviceInfoReprobe(
            staticEvidence,
            postAuthContext with { LegacyTcp8899CommandBridgeObserved = false }));
        Assert.False(MiPlayBusinessClientStaticEvidence.CanJustifyLegacyTcp8899GetDeviceInfoReprobe(
            staticEvidence,
            postAuthContext with { ConnectionMode = MiPlayPostAuthConnectionMode.LyraContinuityChannel }));
        Assert.True(MiPlayBusinessClientStaticEvidence.CanJustifyLegacyTcp8899GetDeviceInfoReprobe(
            staticEvidence,
            postAuthContext));
    }

    private static MiPlayBusinessClientStaticArtifactEvidence CreateCompleteBusinessEvidence() =>
        new(
            ArtifactRole: MiPlayBusinessClientArtifactRole.BusinessClient,
            PackageNameOrLibraryNamespace: "com.xiaomi.miplay.client",
            DecodedAndroidManifestAvailable: true,
            DecodedBusinessResourcesAvailable: true,
            PlatformContinuityManagersObserved: false,
            StaticDiscoveryFrameworkObserved: false,
            StaticNetworkingFrameworkObserved: false,
            BusinessStaticDiscFilterMetadataObserved: true,
            BusinessStaticDiscFilterXmlObserved: true,
            BusinessStaticNetworkingServiceListMetadataObserved: false,
            BusinessStaticNetworkingServiceListXmlObserved: false,
            BusinessNetbusDiscoveryIntentReceiverObserved: true,
            BusinessExplicitDiscoveryListenerRegistrationObserved: false,
            BusinessMiPlayServiceId: "17803",
            BusinessDeviceInfoV2ContextObserved: true,
            BusinessConnectionOrChannelListenerObserved: true,
            LegacyTcp8899CommandBridgeObserved: true);

    private static MiPlayBusinessClientPostAuthContextPrerequisites CreateCompletePostAuthContext() =>
        new(
            MutualSafetyAuthVerified: true,
            SourcePackageIdentityAvailable: true,
            SourceAppInfoAvailable: true,
            DeviceInfoV2OrDeviceIdAvailable: true,
            DiscoveryOrStaticListenerRegisteredBeforeGetDeviceInfo: true,
            ConnectionOrChannelListenerRegistered: true,
            ConnectionMode: MiPlayPostAuthConnectionMode.LegacyTcp8899,
            LegacyTcp8899CommandBridgeObserved: true,
            ReadOnlyProbeBoundary: true);
}
