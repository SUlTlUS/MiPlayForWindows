namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyPostOpenPlaybackLiveValidationSnapshot(
    string ValidationDate,
    string ReceiverAddress,
    string SourceAddress,
    string ReceiverFirmwareVersion,
    ushort ReceiverChallengeSequence,
    IReadOnlyList<int> ReceiverReverseTcpSourcePorts,
    int ReceiverTimerSourcePort,
    ulong TimeOffsetMicroseconds,
    ulong InitialProgramClockReference90Khz,
    int MediaAccessUnitCount,
    long MediaWireBytes,
    double MediaDurationMilliseconds,
    long CaptureOverruns,
    long CaptureUnderruns,
    int SetMediaInfoPayloadLength,
    string PauseFrameSha256,
    string SetMediaInfoFrameSha256,
    string StartupHeartbeatFrameSha256,
    string ResumeFrameSha256,
    bool FirstAudioPcmObserved,
    bool StartupHeartbeatAcknowledged,
    bool MediaInfoEchoObserved,
    bool ReceiverStateThreeObserved,
    bool ReceiverStateTwoObservedAfterResume,
    bool AddMirrorSent,
    int ResumeFrameCount,
    bool RetryOrFallbackUsed,
    bool MediaWriteCompleted,
    bool? UserConfirmedAudibleAtReceiver);

/// <summary>
/// Redacted facts from the explicitly authorized 2026-08-07 single-target
/// post-Open playback-state validation. Creating the snapshot is pure and
/// performs no capture or network operation.
/// </summary>
public static class MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
{
    public static MiPlayLegacyPostOpenPlaybackLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            ValidationDate: "2026-08-07",
            ReceiverAddress: "192.168.10.4",
            SourceAddress: "192.168.10.9",
            ReceiverFirmwareVersion: "1.94.13",
            ReceiverChallengeSequence: 0x0490,
            ReceiverReverseTcpSourcePorts: [50504, 50508, 50510],
            ReceiverTimerSourcePort: 51697,
            TimeOffsetMicroseconds: 1_409_295_729_837,
            InitialProgramClockReference90Khz: 6_577_441_397,
            MediaAccessUnitCount: 938,
            MediaWireBytes: 549_492,
            MediaDurationMilliseconds: 20_010.7,
            CaptureOverruns: 6,
            CaptureUnderruns: 0,
            SetMediaInfoPayloadLength: 178,
            PauseFrameSha256: "6208F000B6CEFFEEA87604B09EC5C8603CD5372741089B6FD0A7E40A7FD16425",
            SetMediaInfoFrameSha256: "EC132EABB0B983C20A03FB83955168B4F5B756E8536B43FE80545DD4AD827A48",
            StartupHeartbeatFrameSha256: "F30BA4FA56440A972C805C0B78D1948DC07739CF9134CE3A3FFEC2A7CF03A426",
            ResumeFrameSha256: "5E18A811D2D1C9415B0F2E1B059044C7333A6C030B9B9836B3FBAE20F9B981DF",
            FirstAudioPcmObserved: true,
            StartupHeartbeatAcknowledged: true,
            MediaInfoEchoObserved: true,
            ReceiverStateThreeObserved: true,
            ReceiverStateTwoObservedAfterResume: true,
            AddMirrorSent: false,
            ResumeFrameCount: 1,
            RetryOrFallbackUsed: false,
            MediaWriteCompleted: true,
            UserConfirmedAudibleAtReceiver: null);

    public static bool ProvesReceiverPlaybackStateReached(
        MiPlayLegacyPostOpenPlaybackLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.FirstAudioPcmObserved &&
               snapshot.StartupHeartbeatAcknowledged &&
               snapshot.MediaInfoEchoObserved &&
               snapshot.ReceiverStateThreeObserved &&
               snapshot.ReceiverStateTwoObservedAfterResume &&
               snapshot.MediaWriteCompleted &&
               !snapshot.AddMirrorSent &&
               snapshot.ResumeFrameCount == 1 &&
               !snapshot.RetryOrFallbackUsed;
    }

    public static bool ProvesAudiblePlayback(
        MiPlayLegacyPostOpenPlaybackLiveValidationSnapshot snapshot) =>
        ProvesReceiverPlaybackStateReached(snapshot) &&
        snapshot.UserConfirmedAudibleAtReceiver == true;
}
