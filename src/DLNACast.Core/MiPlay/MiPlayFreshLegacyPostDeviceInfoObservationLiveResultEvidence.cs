namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyPostDeviceInfoObservationLiveResultSnapshot(
    bool MdnsAdvertisedForBoundedWindow,
    bool TcpSenderConnected,
    int OutboundLegacyChallengeCount,
    int OutboundGetDeviceInfoAcknowledgementCount,
    bool ObservationWindowEndedNormally,
    bool NoRawSenderPayloadLogged,
    bool FollowupAdbDeviceConfirmed,
    bool FollowupDeviceAsleep,
    bool FollowupDisplayOff,
    bool FollowupKeyguardShowing,
    bool FollowupMiPlayAudioServiceRunning,
    bool FollowupRootShellVerified);

public sealed record MiPlayFreshLegacyPostDeviceInfoObservationLiveResultDecision(
    bool ProducesProtocolResult,
    bool RequiresFreshAuthorizationForRetry,
    bool FollowupStateConsistentWithNoSenderTrigger,
    string Reason);

/// <summary>
/// Result of the explicitly authorized 2026-08-07 observation attempt. The
/// phone did not connect during the bounded window, so no TCP command frame was
/// sent and the run is neither a positive nor a negative protocol result.
/// </summary>
public static class MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence
{
    public const string ReceiverAddress = "192.168.10.9";
    public const int ObservationSeconds = 120;
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-post-device-info-observation-20260807-115514.stdout.log";

    public static MiPlayFreshLegacyPostDeviceInfoObservationLiveResultSnapshot CreateCurrentSnapshot() =>
        new(
            MdnsAdvertisedForBoundedWindow: true,
            TcpSenderConnected: false,
            OutboundLegacyChallengeCount: 0,
            OutboundGetDeviceInfoAcknowledgementCount: 0,
            ObservationWindowEndedNormally: true,
            NoRawSenderPayloadLogged: true,
            FollowupAdbDeviceConfirmed: true,
            FollowupDeviceAsleep: true,
            FollowupDisplayOff: true,
            FollowupKeyguardShowing: true,
            FollowupMiPlayAudioServiceRunning: true,
            FollowupRootShellVerified: true);

    public static MiPlayFreshLegacyPostDeviceInfoObservationLiveResultDecision Evaluate(
        MiPlayFreshLegacyPostDeviceInfoObservationLiveResultSnapshot snapshot)
    {
        var cleanNoConnection =
            snapshot.MdnsAdvertisedForBoundedWindow &&
            !snapshot.TcpSenderConnected &&
            snapshot.OutboundLegacyChallengeCount == 0 &&
            snapshot.OutboundGetDeviceInfoAcknowledgementCount == 0 &&
            snapshot.ObservationWindowEndedNormally &&
            snapshot.NoRawSenderPayloadLogged;
        var followupConsistentWithNoTrigger =
            snapshot.FollowupAdbDeviceConfirmed &&
            snapshot.FollowupDeviceAsleep &&
            snapshot.FollowupDisplayOff &&
            snapshot.FollowupKeyguardShowing &&
            snapshot.FollowupMiPlayAudioServiceRunning &&
            snapshot.FollowupRootShellVerified;

        return new MiPlayFreshLegacyPostDeviceInfoObservationLiveResultDecision(
            ProducesProtocolResult: false,
            RequiresFreshAuthorizationForRetry: true,
            FollowupStateConsistentWithNoSenderTrigger: followupConsistentWithNoTrigger,
            cleanNoConnection
                ? "The phone did not establish TCP 8899 during the authorized 120-second mDNS window. No 0x0028, 0x001f, or follow-up command frame was sent, so this run is not a protocol acceptance or rejection result. A later read-only ADB check found the verified tablet asleep with its display off and keyguard showing while MiPlayAudioService remained active; that is consistent with no sender trigger but does not prove the state during the earlier window."
                : "The no-connection transcript or zero-outbound accounting is incomplete; do not interpret this run as protocol evidence.");
    }
}
