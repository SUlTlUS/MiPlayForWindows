namespace DLNACast.Core.MiPlay;

public enum MiPlayContinuityBusinessProfileLinkChoice
{
    Default = 0,
    P2pPrefer = 1,
    P2pOnly = 2,
    WlanPrefer = 3,
    WlanOnly = 4,
}

public sealed record MiPlayContinuityBusinessProfileSnapshot(
    string? Scenario,
    int LatencySuggest,
    int LatencyMax,
    int BandwidthSuggest,
    int BandwidthMin,
    MiPlayContinuityBusinessProfileLinkChoice LinkChoice,
    int LinkCapabilityFlags);

public sealed record MiPlayContinuityBusinessProfileAttachPrerequisites(
    bool ServerChannelOptionsProvided,
    MiPlayContinuityBusinessProfileSnapshot? Profile);

public sealed record MiPlayContinuityMiplayAudioChannelProfileEvidence(
    bool IdmMiPlayAudioServiceTypeObserved,
    bool ContinuityNativeMiPlayAudioLiteralObserved,
    MiPlayContinuityServiceName? ContinuityServiceName,
    bool ServiceNameObservedInContinuityJavaOrNative,
    string? BusinessProfileScenario,
    bool BusinessProfileObservedForServiceName,
    bool RegisterChannelListenerObservedForServiceName,
    bool GenericMiplayTransportSymbolsObserved);

/// <summary>
/// Offline-only evidence model for the gap between IDM MiPlay audio service
/// identity and Continuity channel-listener identity. It deliberately does
/// not construct or authorize any Probe network frame.
/// </summary>
public static class MiPlayContinuityMiplayAudioChannelProfile
{
    public const string IdmMiPlayAudioServiceType = MiPlayIdmServiceTypes.MiPlayAudioUrn;
    public const long IdmMiPlayAudioServiceTypeStringOffset = 0x1AD894;
    public const long IdmServiceTypeIdsTableStringOffset = 0x1ADA0D;

    public const int IContinuityConnectionManagerRegisterChannelListenerV2Transaction = 18;
    public const string ObservedNonMiPlayBusinessProfileScenario = "LinkResMgr";

    public const long NativeRegisterChannelListenerApiStringOffset =
        MiPlayContinuityChannelListenerState.NativeRegisterChannelListenerApiStringOffset;
    public const long NativeRegisterChannelListenerSymbolStringOffset =
        MiPlayContinuityChannelListenerState.NativeRegisterChannelListenerSymbolStringOffset;
    public const long NativeMiplayTransportGovernorStringOffset = 0x218C35;
    public const long NativeMiplayTransportSessionCreateKcpSessionStringOffset = 0x21F8C8;

    public const string OptionalBusinessProfileScenario = "CHANNEL_OPTIONAL_BIZ_PROF_SCENARIO";
    public const string OptionalBusinessProfileLatencyMax = "CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_MAX";
    public const string OptionalBusinessProfileLatencyNormal = "CHANNEL_OPTIONAL_BIZ_PROF_LATENCY_NORMAL";
    public const string OptionalBusinessProfileBandwidthMin = "CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_MIN";
    public const string OptionalBusinessProfileBandwidthNormal = "CHANNEL_OPTIONAL_BIZ_PROF_BANDWIDTH_NORMAL";
    public const string OptionalBusinessConnectionType = "CHANNEL_OPTIONAL_BIZ_CONNECTION_TYPE";
    public const string OptionalLinkCapabilityFlags = "CHANNEL_OPTIONAL_LINK_CAPABILITY_FLAGS";

    public static IReadOnlyList<string> ServerBusinessProfileAlwaysWrittenOptionalKeys { get; } =
    [
        OptionalBusinessProfileScenario,
        OptionalBusinessProfileLatencyMax,
        OptionalBusinessProfileLatencyNormal,
        OptionalBusinessProfileBandwidthMin,
        OptionalBusinessProfileBandwidthNormal,
        OptionalBusinessConnectionType,
    ];

    public static MiPlayIdmStateDecision EvaluateServerBusinessProfileAttach(
        MiPlayContinuityBusinessProfileAttachPrerequisites prerequisites)
    {
        if (!prerequisites.ServerChannelOptionsProvided)
        {
            return new MiPlayIdmStateDecision(false, "ServerChannelOptionsV2 is missing for registerChannelListener.");
        }

        if (prerequisites.Profile is null)
        {
            return new MiPlayIdmStateDecision(false, "BusinessProfile is missing for registerChannelListener.");
        }

        if (string.IsNullOrEmpty(prerequisites.Profile.Scenario))
        {
            return new MiPlayIdmStateDecision(false, "BusinessProfile.attachTo(ServerChannelOptionsV2) returns without writing optional values when scenario is empty.");
        }

        if (!Enum.IsDefined(prerequisites.Profile.LinkChoice))
        {
            return new MiPlayIdmStateDecision(false, "BusinessProfile LinkChoice does not match the APK enum values.");
        }

        return new MiPlayIdmStateDecision(true, "BusinessProfile can attach optional values to ServerChannelOptionsV2.");
    }

    public static bool ServerBusinessProfileWritesLinkCapabilityFlags(int linkCapabilityFlags) =>
        linkCapabilityFlags > 0;

    public static MiPlayIdmStateDecision EvaluateMiplayAudioChannelProfileEvidence(
        MiPlayContinuityMiplayAudioChannelProfileEvidence evidence)
    {
        if (!evidence.IdmMiPlayAudioServiceTypeObserved)
        {
            return new MiPlayIdmStateDecision(false, "The IDM MiPlay audio service type was not observed.");
        }

        if (!evidence.ContinuityNativeMiPlayAudioLiteralObserved)
        {
            return new MiPlayIdmStateDecision(false, "The MiPlay audio IDM literal was not observed in the Continuity native channel layer.");
        }

        if (evidence.ContinuityServiceName is null)
        {
            return new MiPlayIdmStateDecision(false, "No Continuity ServiceName has been proven for MiPlay audio.");
        }

        if (!evidence.ServiceNameObservedInContinuityJavaOrNative)
        {
            return new MiPlayIdmStateDecision(false, "The candidate ServiceName was not observed in the Continuity Java/native channel path.");
        }

        if (string.IsNullOrEmpty(evidence.BusinessProfileScenario))
        {
            return new MiPlayIdmStateDecision(false, "No non-empty BusinessProfile scenario has been proven for MiPlay audio.");
        }

        if (!evidence.BusinessProfileObservedForServiceName)
        {
            return new MiPlayIdmStateDecision(false, "The BusinessProfile was not tied to the candidate MiPlay audio ServiceName.");
        }

        if (!evidence.RegisterChannelListenerObservedForServiceName)
        {
            return new MiPlayIdmStateDecision(false, "registerChannelListenerV2 was not observed with the candidate MiPlay audio ServiceName.");
        }

        return new MiPlayIdmStateDecision(true, "The static evidence is sufficient to model a MiPlay audio Continuity channel profile.");
    }

    public static bool CanUseIdmServiceTypeAsContinuityServiceName(
        MiPlayIdmServiceType serviceType,
        MiPlayContinuityServiceName? serviceName) =>
        serviceName is not null &&
        serviceType.ServiceName == MiPlayIdmServiceTypes.MiPlayAudioServiceName &&
        serviceType.TypeId == MiPlayIdmServiceTypes.MiPlayAudioTypeId &&
        false;

    public static bool GenericMiplayTransportSymbolsSatisfyChannelProfile(
        bool genericMiplayTransportSymbolsObserved,
        bool serviceNameMappingObserved,
        bool businessProfileMappingObserved) =>
        genericMiplayTransportSymbolsObserved &&
        serviceNameMappingObserved &&
        businessProfileMappingObserved;
}
