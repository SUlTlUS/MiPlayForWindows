using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyPostDeviceInfoRaceLiveResultSnapshot(
    bool MdnsAdvertisedForBoundedWindow,
    bool TcpSenderConnected,
    bool ReceiverAppearedThroughAutomaticLanDiscovery,
    bool ReceiverWasNotSelectedByUser,
    int OutboundLegacyChallengeCount,
    int OutboundGetDeviceInfoAcknowledgementCount,
    bool VerifiedLegacyAuthenticationAcknowledgement,
    bool ObservedEmptyGetDeviceInfo,
    bool ObservedExactInitialSetLocalDeviceInfoAfterDeviceInfoAcknowledgement,
    bool ObserverStoppedWithoutReply,
    bool NoOtherOutboundFrames,
    bool PhoneLogObservedOnDeviceInfo,
    bool PhoneLogObservedSetLocalDeviceInfoSameAccount,
    bool PhoneLogObservedGetMirrorMode,
    bool AdvancedSetLocalDeviceInfoNotWireObserved,
    bool GetMirrorModeNotWireObserved,
    bool UserObservedReceiverAppearThenDisappear);

public sealed record MiPlayFreshLegacyPostDeviceInfoRaceLiveResultDecision(
    bool ProvesDeviceInfoAccepted,
    bool ProvesInitialSetLocalDeviceInfoCanRaceAfterDeviceInfoAcknowledgement,
    bool ProvesGetMirrorModeReachedPhoneSide,
    bool ProvesGetMirrorModeOnWire,
    bool AppearanceConsistentWithDeliberateDisconnect,
    bool RequiresFreshAuthorizationForRetry,
    string Reason);

/// <summary>
/// Privacy-preserving evidence from the second explicitly authorized
/// 2026-08-07 observation. Automatic LAN discovery created the source session;
/// the strict observer stopped on a byte-exact but previously unmodelled
/// initial 0x0058 race and sent no reply.
/// </summary>
public static class MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence
{
    public const string ReceiverAddress = "192.168.10.9";
    public const int ReceiverPort = MiPlayProtocolConstants.DefaultControlPort;
    public const string SourceAddress = "192.168.10.58";
    public const int SourcePort = 50_730;
    public const int ObservationSeconds = 120;
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-post-device-info-observation-20260807-125156.stdout.log";

    public static MiPlayFreshLegacyPostDeviceInfoRaceLiveResultSnapshot CreateCurrentSnapshot() =>
        new(
            MdnsAdvertisedForBoundedWindow: true,
            TcpSenderConnected: true,
            ReceiverAppearedThroughAutomaticLanDiscovery: true,
            ReceiverWasNotSelectedByUser: true,
            OutboundLegacyChallengeCount: 1,
            OutboundGetDeviceInfoAcknowledgementCount: 1,
            VerifiedLegacyAuthenticationAcknowledgement: true,
            ObservedEmptyGetDeviceInfo: true,
            ObservedExactInitialSetLocalDeviceInfoAfterDeviceInfoAcknowledgement: true,
            ObserverStoppedWithoutReply: true,
            NoOtherOutboundFrames: true,
            PhoneLogObservedOnDeviceInfo: true,
            PhoneLogObservedSetLocalDeviceInfoSameAccount: true,
            PhoneLogObservedGetMirrorMode: true,
            AdvancedSetLocalDeviceInfoNotWireObserved: true,
            GetMirrorModeNotWireObserved: true,
            UserObservedReceiverAppearThenDisappear: true);

    public static MiPlayFreshLegacyPostDeviceInfoRaceLiveResultDecision Evaluate(
        MiPlayFreshLegacyPostDeviceInfoRaceLiveResultSnapshot snapshot)
    {
        var reconstructedInitialFrame =
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructInitialSetLocalDeviceInfoFrame();
        var exactInitialFramePinned =
            reconstructedInitialFrame.Length ==
                MiPlayProtocolConstants.CommandHeaderLength +
                MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoPayloadLength &&
            string.Equals(
                Convert.ToHexString(SHA256.HashData(reconstructedInitialFrame)),
                MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoFrameSha256,
                StringComparison.Ordinal);

        var exactOutboundBoundary =
            snapshot.OutboundLegacyChallengeCount == 1 &&
            snapshot.OutboundGetDeviceInfoAcknowledgementCount == 1 &&
            snapshot.NoOtherOutboundFrames &&
            snapshot.ObserverStoppedWithoutReply;
        var authenticatedDeviceInfoExchange =
            snapshot.MdnsAdvertisedForBoundedWindow &&
            snapshot.TcpSenderConnected &&
            snapshot.VerifiedLegacyAuthenticationAcknowledgement &&
            snapshot.ObservedEmptyGetDeviceInfo &&
            exactOutboundBoundary;
        var phoneProgression =
            snapshot.PhoneLogObservedOnDeviceInfo &&
            snapshot.PhoneLogObservedSetLocalDeviceInfoSameAccount &&
            snapshot.PhoneLogObservedGetMirrorMode;
        var provesDeviceInfoAccepted = authenticatedDeviceInfoExchange && phoneProgression;
        var provesRace =
            authenticatedDeviceInfoExchange &&
            exactInitialFramePinned &&
            snapshot.ObservedExactInitialSetLocalDeviceInfoAfterDeviceInfoAcknowledgement;
        var automaticDiscoveryAppearance =
            snapshot.ReceiverAppearedThroughAutomaticLanDiscovery &&
            snapshot.ReceiverWasNotSelectedByUser &&
            snapshot.UserObservedReceiverAppearThenDisappear;
        var appearanceConsistentWithDisconnect =
            automaticDiscoveryAppearance && snapshot.ObserverStoppedWithoutReply;

        return new MiPlayFreshLegacyPostDeviceInfoRaceLiveResultDecision(
            ProvesDeviceInfoAccepted: provesDeviceInfoAccepted,
            ProvesInitialSetLocalDeviceInfoCanRaceAfterDeviceInfoAcknowledgement: provesRace,
            ProvesGetMirrorModeReachedPhoneSide: provesDeviceInfoAccepted,
            ProvesGetMirrorModeOnWire: false,
            AppearanceConsistentWithDeliberateDisconnect: appearanceConsistentWithDisconnect,
            RequiresFreshAuthorizationForRetry: true,
            provesDeviceInfoAccepted && provesRace
                ? "Automatic LAN discovery established the fresh legacy session without a user selection. The receiver sent exactly one 0x0028 and one same-sequence 0x001f, and the phone logs reached onDeviceInfo, setLocalDeviceInfoSameAccount, and getMirrorMode. The next wire frame was the byte-exact initial 0x0058 sequence 0x0002, proving that this already-queued sourceName frame can race after 0x001f. The strict observer deliberately disconnected without a reply, which explains the brief device-list appearance. Advanced 0x0058 sequence 0x0003 and 0x0034 were not observed on the wire, so another run would require fresh authorization."
                : "The authenticated exchange, exact two-frame outbound boundary, byte-exact initial 0x0058 race, or phone-side callback evidence is incomplete; do not interpret this snapshot as device-info acceptance.");
    }
}
