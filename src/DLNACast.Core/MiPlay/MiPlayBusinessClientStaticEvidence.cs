namespace DLNACast.Core.MiPlay;

public enum MiPlayBusinessClientArtifactRole
{
    Unknown = 0,
    ContinuityPlatformService = 1,
    BusinessClient = 2,
}

public sealed record MiPlayBusinessClientStaticArtifactEvidence(
    MiPlayBusinessClientArtifactRole ArtifactRole,
    string? PackageNameOrLibraryNamespace,
    bool DecodedAndroidManifestAvailable,
    bool DecodedBusinessResourcesAvailable,
    bool PlatformContinuityManagersObserved,
    bool StaticDiscoveryFrameworkObserved,
    bool StaticNetworkingFrameworkObserved,
    bool BusinessStaticDiscFilterMetadataObserved,
    bool BusinessStaticDiscFilterXmlObserved,
    bool BusinessStaticNetworkingServiceListMetadataObserved,
    bool BusinessStaticNetworkingServiceListXmlObserved,
    bool BusinessNetbusDiscoveryIntentReceiverObserved,
    bool BusinessExplicitDiscoveryListenerRegistrationObserved,
    string? BusinessMiPlayServiceId,
    bool BusinessDeviceInfoV2ContextObserved,
    bool BusinessConnectionOrChannelListenerObserved,
    bool LegacyTcp8899CommandBridgeObserved);

public sealed record MiPlayBusinessClientPostAuthContextPrerequisites(
    bool MutualSafetyAuthVerified,
    bool SourcePackageIdentityAvailable,
    bool SourceAppInfoAvailable,
    bool DeviceInfoV2OrDeviceIdAvailable,
    bool DiscoveryOrStaticListenerRegisteredBeforeGetDeviceInfo,
    bool ConnectionOrChannelListenerRegistered,
    MiPlayPostAuthConnectionMode ConnectionMode,
    bool LegacyTcp8899CommandBridgeObserved,
    bool ReadOnlyProbeBoundary);

/// <summary>
/// Offline-only checklist for deciding whether a decompiled artifact proves the
/// missing MiPlay business-client side of the Continuity/IDM flow. This is a
/// static evidence gate: it does not derive keys, open sockets, or construct
/// any Probe frame.
/// </summary>
public static class MiPlayBusinessClientStaticEvidence
{
    public const string CurrentMiConnectServiceLibraryNamespace = "com.xiaomi.continuity.service";
    public const string CurrentMiConnectServiceVersionName = "5.1.251.10.fullCnRelease.0616209";
    public const string CurrentMiConnectServiceFlavor = "fullCn";

    public const string MiPlayStaticDiscoveryServiceId =
        "00017803";
    public const string MiPlayShortDiscoveryServiceId =
        "17803";
    public const string MiPlayAudioUrn = MiPlayIdmServiceTypes.MiPlayAudioUrn;
    public const string StaticDiscFilterResourceKey =
        MiPlayContinuityDiscoveryListenerState.StaticDiscFilterResourceKey;
    public const string StaticNetworkingServiceListResourceKey =
        MiPlayContinuityStaticNetworkingServiceConfig.StaticNetworkingServiceListResourceKey;
    public const string BindContinuityServiceInternalPermission =
        MiPlayContinuityStaticNetworkingServiceConfig.BindContinuityServiceInternalPermission;
    public const string NetbusDiscDeviceFoundAction =
        MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscDeviceFound;
    public const string NetbusDiscReceiveDataAction =
        MiPlayContinuityDiscoveryListenerState.ActionNetbusDiscReceiveData;
    public const string DeviceInfoV2Feature =
        MiPlayContinuityDiscoveryListenerState.DeviceInfoV2Feature;

    public static MiPlayBusinessClientStaticArtifactEvidence CreateCurrentMiConnectServiceApkSnapshot() =>
        new(
            ArtifactRole: MiPlayBusinessClientArtifactRole.ContinuityPlatformService,
            PackageNameOrLibraryNamespace: CurrentMiConnectServiceLibraryNamespace,
            DecodedAndroidManifestAvailable: false,
            DecodedBusinessResourcesAvailable: false,
            PlatformContinuityManagersObserved: true,
            StaticDiscoveryFrameworkObserved: true,
            StaticNetworkingFrameworkObserved: true,
            BusinessStaticDiscFilterMetadataObserved: false,
            BusinessStaticDiscFilterXmlObserved: false,
            BusinessStaticNetworkingServiceListMetadataObserved: false,
            BusinessStaticNetworkingServiceListXmlObserved: false,
            BusinessNetbusDiscoveryIntentReceiverObserved: false,
            BusinessExplicitDiscoveryListenerRegistrationObserved: false,
            BusinessMiPlayServiceId: null,
            BusinessDeviceInfoV2ContextObserved: false,
            BusinessConnectionOrChannelListenerObserved: false,
            LegacyTcp8899CommandBridgeObserved: false);

    public static MiPlayIdmStateDecision EvaluateBusinessClientStaticEvidence(
        MiPlayBusinessClientStaticArtifactEvidence evidence)
    {
        if (evidence.ArtifactRole != MiPlayBusinessClientArtifactRole.BusinessClient)
        {
            return new MiPlayIdmStateDecision(false, "The artifact is not identified as a MiPlay business-client package.");
        }

        if (string.IsNullOrWhiteSpace(evidence.PackageNameOrLibraryNamespace))
        {
            return new MiPlayIdmStateDecision(false, "The source package identity is missing.");
        }

        if (!evidence.DecodedAndroidManifestAvailable)
        {
            return new MiPlayIdmStateDecision(false, "A decoded AndroidManifest is required to prove services, receivers, and permissions.");
        }

        if (!evidence.DecodedBusinessResourcesAvailable)
        {
            return new MiPlayIdmStateDecision(false, "Decoded business resources are required to prove static discovery or networking XML.");
        }

        if (!HasStaticBusinessConfig(evidence) &&
            !evidence.BusinessExplicitDiscoveryListenerRegistrationObserved)
        {
            return new MiPlayIdmStateDecision(
                false,
                "No business static discovery/networking config or explicit discovery listener registration was observed.");
        }

        if (NormalizeMiPlayServiceId(evidence.BusinessMiPlayServiceId) != MiPlayStaticDiscoveryServiceId)
        {
            return new MiPlayIdmStateDecision(false, "The business artifact is not tied to MiPlay service id 00017803.");
        }

        if (!evidence.BusinessNetbusDiscoveryIntentReceiverObserved &&
            !evidence.BusinessExplicitDiscoveryListenerRegistrationObserved)
        {
            return new MiPlayIdmStateDecision(false, "No NETBUS_DISC receiver or explicit discovery listener path was observed.");
        }

        if (!evidence.BusinessDeviceInfoV2ContextObserved)
        {
            return new MiPlayIdmStateDecision(false, "No business-side DeviceInfoV2/deviceId context was observed.");
        }

        if (!evidence.BusinessConnectionOrChannelListenerObserved)
        {
            return new MiPlayIdmStateDecision(false, "No business-side connection or channel listener registration was observed.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The static artifact proves the MiPlay business identity, discovery context, and listener chain.");
    }

    public static MiPlayIdmStateDecision EvaluatePostAuthContext(
        MiPlayBusinessClientPostAuthContextPrerequisites prerequisites)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlayIdmStateDecision(false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SourcePackageIdentityAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The source package identity is missing.");
        }

        if (!prerequisites.SourceAppInfoAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The source Continuity AppInfo context is missing.");
        }

        if (!prerequisites.DeviceInfoV2OrDeviceIdAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The target DeviceInfoV2/deviceId context is missing.");
        }

        if (!prerequisites.DiscoveryOrStaticListenerRegisteredBeforeGetDeviceInfo)
        {
            return new MiPlayIdmStateDecision(false, "The discovery/static listener timing before getDeviceInfo is not proven.");
        }

        if (!prerequisites.ConnectionOrChannelListenerRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No connection or channel listener is registered for the post-auth context.");
        }

        if (prerequisites.ConnectionMode == MiPlayPostAuthConnectionMode.Unknown)
        {
            return new MiPlayIdmStateDecision(false, "The post-auth connection mode is unknown.");
        }

        if (!prerequisites.ReadOnlyProbeBoundary)
        {
            return new MiPlayIdmStateDecision(false, "The candidate verification is not constrained to a read-only boundary.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The post-auth source identity, device context, listener timing, and read-only gates are complete.");
    }

    public static bool CanJustifyLegacyTcp8899GetDeviceInfoReprobe(
        MiPlayBusinessClientStaticArtifactEvidence staticEvidence,
        MiPlayBusinessClientPostAuthContextPrerequisites postAuthContext) =>
        EvaluateBusinessClientStaticEvidence(staticEvidence).CanProceed &&
        EvaluatePostAuthContext(postAuthContext).CanProceed &&
        postAuthContext.ConnectionMode == MiPlayPostAuthConnectionMode.LegacyTcp8899 &&
        staticEvidence.LegacyTcp8899CommandBridgeObserved &&
        postAuthContext.LegacyTcp8899CommandBridgeObserved;

    public static string? NormalizeMiPlayServiceId(string? serviceId) =>
        MiPlayContinuityDiscoveryListenerState.NormalizeStaticDiscServiceId(serviceId);

    public static bool CurrentMiConnectServiceApkCanIdentifyBusinessClient() =>
        EvaluateBusinessClientStaticEvidence(CreateCurrentMiConnectServiceApkSnapshot()).CanProceed;

    private static bool HasStaticBusinessConfig(MiPlayBusinessClientStaticArtifactEvidence evidence) =>
        evidence.BusinessStaticDiscFilterMetadataObserved &&
        evidence.BusinessStaticDiscFilterXmlObserved ||
        evidence.BusinessStaticNetworkingServiceListMetadataObserved &&
        evidence.BusinessStaticNetworkingServiceListXmlObserved;
}
