using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityMiplayAudioChannelProfileTests
{
    [Fact]
    public void NativeAndJadxEvidenceConstantsMatchStaticEvidence()
    {
        Assert.Equal(MiPlayIdmServiceTypes.MiPlayAudioUrn, MiPlayContinuityMiplayAudioChannelProfile.IdmMiPlayAudioServiceType);
        Assert.Equal(0x1AD894, MiPlayContinuityMiplayAudioChannelProfile.IdmMiPlayAudioServiceTypeStringOffset);
        Assert.Equal(0x1ADA0D, MiPlayContinuityMiplayAudioChannelProfile.IdmServiceTypeIdsTableStringOffset);

        Assert.Equal(18, MiPlayContinuityMiplayAudioChannelProfile.IContinuityConnectionManagerRegisterChannelListenerV2Transaction);
        Assert.Equal(0xFFB16, MiPlayContinuityMiplayAudioChannelProfile.NativeRegisterChannelListenerApiStringOffset);
        Assert.Equal(0x14D5AA, MiPlayContinuityMiplayAudioChannelProfile.NativeRegisterChannelListenerSymbolStringOffset);
        Assert.Equal(0x218C35, MiPlayContinuityMiplayAudioChannelProfile.NativeMiplayTransportGovernorStringOffset);
        Assert.Equal(0x21F8C8, MiPlayContinuityMiplayAudioChannelProfile.NativeMiplayTransportSessionCreateKcpSessionStringOffset);

        Assert.Equal("CHANNEL_OPTIONAL_BIZ_PROF_SCENARIO", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessProfileScenario);
        Assert.Equal("CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_MAX", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessProfileLatencyMax);
        Assert.Equal("CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_NORMAL", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessProfileLatencyNormal);
        Assert.Equal("CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_MIN", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessProfileBandwidthMin);
        Assert.Equal("CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_NORMAL", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessProfileBandwidthNormal);
        Assert.Equal("CHANNEL_OPTIONAL_BIZ_CONNECTION_TYPE", MiPlayContinuityMiplayAudioChannelProfile.OptionalBusinessConnectionType);
        Assert.Equal("CHANNEL_OPTIONAL_LINK_CAPABILITY_FLAGS", MiPlayContinuityMiplayAudioChannelProfile.OptionalLinkCapabilityFlags);
        Assert.Equal("LinkResMgr", MiPlayContinuityMiplayAudioChannelProfile.ObservedNonMiPlayBusinessProfileScenario);
    }

    [Fact]
    public void BusinessProfileLinkChoicesMatchApkEnumValues()
    {
        Assert.Equal(0, (int)MiPlayContinuityBusinessProfileLinkChoice.Default);
        Assert.Equal(1, (int)MiPlayContinuityBusinessProfileLinkChoice.P2pPrefer);
        Assert.Equal(2, (int)MiPlayContinuityBusinessProfileLinkChoice.P2pOnly);
        Assert.Equal(3, (int)MiPlayContinuityBusinessProfileLinkChoice.WlanPrefer);
        Assert.Equal(4, (int)MiPlayContinuityBusinessProfileLinkChoice.WlanOnly);
    }

    [Fact]
    public void BusinessProfileServerAttachRequiresNonEmptyScenarioAndServerOptions()
    {
        var accepted = MiPlayContinuityMiplayAudioChannelProfile.EvaluateServerBusinessProfileAttach(
            new MiPlayContinuityBusinessProfileAttachPrerequisites(
                ServerChannelOptionsProvided: true,
                Profile: new MiPlayContinuityBusinessProfileSnapshot(
                    Scenario: "miplay-audio",
                    LatencySuggest: 20,
                    LatencyMax: 80,
                    BandwidthSuggest: 1_000,
                    BandwidthMin: 256,
                    LinkChoice: MiPlayContinuityBusinessProfileLinkChoice.P2pPrefer,
                    LinkCapabilityFlags: 0)));

        Assert.True(accepted.CanProceed);

        var missingOptions = MiPlayContinuityMiplayAudioChannelProfile.EvaluateServerBusinessProfileAttach(
            new MiPlayContinuityBusinessProfileAttachPrerequisites(
                ServerChannelOptionsProvided: false,
                Profile: new MiPlayContinuityBusinessProfileSnapshot(
                    Scenario: "miplay-audio",
                    LatencySuggest: 0,
                    LatencyMax: 0,
                    BandwidthSuggest: 0,
                    BandwidthMin: 0,
                    LinkChoice: MiPlayContinuityBusinessProfileLinkChoice.Default,
                    LinkCapabilityFlags: 0)));
        var emptyScenario = MiPlayContinuityMiplayAudioChannelProfile.EvaluateServerBusinessProfileAttach(
            new MiPlayContinuityBusinessProfileAttachPrerequisites(
                ServerChannelOptionsProvided: true,
                Profile: new MiPlayContinuityBusinessProfileSnapshot(
                    Scenario: "",
                    LatencySuggest: 0,
                    LatencyMax: 0,
                    BandwidthSuggest: 0,
                    BandwidthMin: 0,
                    LinkChoice: MiPlayContinuityBusinessProfileLinkChoice.Default,
                    LinkCapabilityFlags: 0)));

        Assert.False(missingOptions.CanProceed);
        Assert.False(emptyScenario.CanProceed);
    }

    [Fact]
    public void BusinessProfileAttachModelsOptionalKeysAndLinkCapabilityPositiveGate()
    {
        Assert.Equal(
            [
                "CHANNEL_OPTIONAL_BIZ_PROF_SCENARIO",
                "CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_MAX",
                "CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_NORMAL",
                "CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_MIN",
                "CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_NORMAL",
                "CHANNEL_OPTIONAL_BIZ_CONNECTION_TYPE",
            ],
            MiPlayContinuityMiplayAudioChannelProfile.ServerBusinessProfileAlwaysWrittenOptionalKeys);

        Assert.False(MiPlayContinuityMiplayAudioChannelProfile.ServerBusinessProfileWritesLinkCapabilityFlags(0));
        Assert.False(MiPlayContinuityMiplayAudioChannelProfile.ServerBusinessProfileWritesLinkCapabilityFlags(-1));
        Assert.True(MiPlayContinuityMiplayAudioChannelProfile.ServerBusinessProfileWritesLinkCapabilityFlags(1));
    }

    [Fact]
    public void IdmMiplayAudioUrnParseShapeIsNotContinuityServiceNameProof()
    {
        Assert.True(MiPlayIdmServiceType.TryParse(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var idmServiceType));
        Assert.NotNull(idmServiceType);

        Assert.True(MiPlayContinuityServiceName.TryParseApkMergedString(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var parsedServiceName));
        Assert.NotNull(parsedServiceName);
        Assert.Equal("urn:aiot-spec-v3", parsedServiceName.ToMergedString());

        Assert.False(MiPlayContinuityMiplayAudioChannelProfile.CanUseIdmServiceTypeAsContinuityServiceName(
            idmServiceType,
            parsedServiceName));
    }

    [Fact]
    public void CurrentMiConnectApkEvidenceDoesNotCompleteMiplayAudioChannelIdentity()
    {
        var decision = MiPlayContinuityMiplayAudioChannelProfile.EvaluateMiplayAudioChannelProfileEvidence(
            new MiPlayContinuityMiplayAudioChannelProfileEvidence(
                IdmMiPlayAudioServiceTypeObserved: true,
                ContinuityNativeMiPlayAudioLiteralObserved: false,
                ContinuityServiceName: null,
                ServiceNameObservedInContinuityJavaOrNative: false,
                BusinessProfileScenario: null,
                BusinessProfileObservedForServiceName: false,
                RegisterChannelListenerObservedForServiceName: false,
                GenericMiplayTransportSymbolsObserved: true));

        Assert.False(decision.CanProceed);
        Assert.Contains("Continuity native", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericMiplayTransportSymbolsDoNotSatisfyServiceNameOrBusinessProfileMapping()
    {
        Assert.False(MiPlayContinuityMiplayAudioChannelProfile.GenericMiplayTransportSymbolsSatisfyChannelProfile(
            genericMiplayTransportSymbolsObserved: true,
            serviceNameMappingObserved: false,
            businessProfileMappingObserved: false));
        Assert.False(MiPlayContinuityMiplayAudioChannelProfile.GenericMiplayTransportSymbolsSatisfyChannelProfile(
            genericMiplayTransportSymbolsObserved: true,
            serviceNameMappingObserved: true,
            businessProfileMappingObserved: false));
        Assert.True(MiPlayContinuityMiplayAudioChannelProfile.GenericMiplayTransportSymbolsSatisfyChannelProfile(
            genericMiplayTransportSymbolsObserved: true,
            serviceNameMappingObserved: true,
            businessProfileMappingObserved: true));
    }

    [Fact]
    public void CompleteProfileCanProceedOnlyWhenEveryBoundaryIsObserved()
    {
        var serviceName = new MiPlayContinuityServiceName("com.xiaomi.miplay", "audio");
        var accepted = MiPlayContinuityMiplayAudioChannelProfile.EvaluateMiplayAudioChannelProfileEvidence(
            new MiPlayContinuityMiplayAudioChannelProfileEvidence(
                IdmMiPlayAudioServiceTypeObserved: true,
                ContinuityNativeMiPlayAudioLiteralObserved: true,
                ContinuityServiceName: serviceName,
                ServiceNameObservedInContinuityJavaOrNative: true,
                BusinessProfileScenario: "miplay-audio",
                BusinessProfileObservedForServiceName: true,
                RegisterChannelListenerObservedForServiceName: true,
                GenericMiplayTransportSymbolsObserved: true));
        var missingBusinessProfile = MiPlayContinuityMiplayAudioChannelProfile.EvaluateMiplayAudioChannelProfileEvidence(
            new MiPlayContinuityMiplayAudioChannelProfileEvidence(
                IdmMiPlayAudioServiceTypeObserved: true,
                ContinuityNativeMiPlayAudioLiteralObserved: true,
                ContinuityServiceName: serviceName,
                ServiceNameObservedInContinuityJavaOrNative: true,
                BusinessProfileScenario: "miplay-audio",
                BusinessProfileObservedForServiceName: false,
                RegisterChannelListenerObservedForServiceName: true,
                GenericMiplayTransportSymbolsObserved: true));

        Assert.True(accepted.CanProceed);
        Assert.False(missingBusinessProfile.CanProceed);
    }
}
