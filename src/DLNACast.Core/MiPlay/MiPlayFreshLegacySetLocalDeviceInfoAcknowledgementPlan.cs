using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementSnapshot(
    bool FreshSourcePayloadReconstructedExactly,
    bool CurrentS12CapturedSameSequenceAcknowledgements,
    bool CurrentS12ContinuationDecryptProvesEmptyAcknowledgementPlaintext,
    bool SourceNativeAcknowledgementRouteAcceptsEmptyPayload,
    bool Lx0618851ContainsSetLocalDeviceInfoHandler);

public sealed record MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlan(
    ushort RequestSequence,
    byte[] RequestPayload,
    byte[] AcknowledgementFrame,
    string AcknowledgementFrameSha256,
    bool ExactFreshClearAcknowledgementObserved,
    bool SafeForNetworkUse);

public sealed record MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementDecision(
    bool CanBuildDeterministicCandidate,
    bool CanSendNow,
    string Reason,
    string RemainingBoundary,
    MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlan Plan);

/// <summary>
/// Offline-only candidate for acknowledging the post-0x001f isSameAccount
/// update. Current S12 SafetyData captures prove same-sequence 0x0059 with
/// empty command plaintext, while the older LX06 1.88.51 mpas dispatcher does
/// not contain a 0x0058 handler. Official source ordering now proves this ACK
/// is not required before the phone enqueues GetMirrorMode. The candidate
/// therefore remains network-gated and is not the next evidence priority.
/// </summary>
public static class MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner
{
    public const string CurrentS12PcapArtifact =
        "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap";
    public const string Lx0618851MpasArtifact =
        "artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted/usr/bin/mpas";

    public const int MpasCommandReadAddress = 0x6580c;
    public const int MpasHighRangeEntryAddress = 0x658e8;
    public const int Mpas0058RangeBranchAddress = 0x65910;
    public const int Mpas0062CompareAddress = 0x66f60;
    public const int Mpas0400CompareAddress = 0x677b0;
    public const int MpasDefaultReturnAddress = 0x667e8;

    public static MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementSnapshot CreateCurrentSnapshot() =>
        new(
            FreshSourcePayloadReconstructedExactly: true,
            CurrentS12CapturedSameSequenceAcknowledgements: true,
            CurrentS12ContinuationDecryptProvesEmptyAcknowledgementPlaintext: true,
            SourceNativeAcknowledgementRouteAcceptsEmptyPayload: true,
            Lx0618851ContainsSetLocalDeviceInfoHandler: false);

    public static MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlan CreateOfflinePlan(
        ushort requestSequence = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoSequence)
    {
        var requestPayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoIsSameAccount);
        var acknowledgementFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            requestSequence,
            []);

        return new MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlan(
            requestSequence,
            requestPayload,
            acknowledgementFrame,
            Convert.ToHexString(SHA256.HashData(acknowledgementFrame)),
            ExactFreshClearAcknowledgementObserved: false,
            SafeForNetworkUse: false);
    }

    public static MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementDecision Evaluate(
        MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementSnapshot snapshot)
    {
        var plan = CreateOfflinePlan();
        var canBuild =
            snapshot.FreshSourcePayloadReconstructedExactly &&
            snapshot.CurrentS12CapturedSameSequenceAcknowledgements &&
            snapshot.CurrentS12ContinuationDecryptProvesEmptyAcknowledgementPlaintext &&
            snapshot.SourceNativeAcknowledgementRouteAcceptsEmptyPayload &&
            !snapshot.Lx0618851ContainsSetLocalDeviceInfoHandler &&
            MiPlayCommandFrameCodec.TryDecode(
                plan.AcknowledgementFrame,
                out var acknowledgement,
                out var bytesConsumed) &&
            acknowledgement is not null &&
            bytesConsumed == plan.AcknowledgementFrame.Length &&
            acknowledgement.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand &&
            acknowledgement.Sequence == plan.RequestSequence &&
            acknowledgement.Payload.Length == 0;

        return new MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementDecision(
            canBuild,
            CanSendNow: false,
            canBuild
                ? "The fresh source's byte-exact {\"isSameAccount\":0} update is command 0x0058 sequence 0x0003. Current LX06 1.94.13 passive SafetyData captures contain matching 0x0059 acknowledgements whose later direction-chain frames decrypt to empty command plaintext, and the source 0x0059 route does not require a response payload. This supports one deterministic clear 0x0059 sequence 0x0003 empty candidate, but official source ordering proves the phone enqueues GetMirrorMode without waiting for it."
                : "The fresh request payload, current-S12 0x0059 sequence/plaintext evidence, source ACK route, or strict candidate encoding is incomplete.",
            "No fresh legacy-clear 0x0059 has yet been observed, but sending it is not the next evidence priority: the prior receiver stopped before it could observe the already-queued 0x0034. LX06 1.88.51 cannot close the receiver-ACK gap because its mpas dispatcher routes 0x0058 through the default false-return branch rather than a handler. Keep the candidate SafeForNetworkUse=false and do not infer permission for heartbeat ACK, GetMirrorMode ACK, Open, AddMirror, RTSP, playback, media, or audio.",
            plan);
    }
}
