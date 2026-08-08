namespace DLNACast.Core.MiPlay;

public sealed record MiPlayMainApplicationLiveValidationSnapshot(
    string ValidationDate,
    string ReceiverSelectionLabel,
    string ReceiverAddress,
    string SourceAddress,
    int AdvertisedDurationMilliseconds,
    int SetMediaInfoPayloadLength,
    string SetMediaInfoPayloadSha256,
    int FirstMediaAccessUnitLength,
    int FirstMediaRtpFrameCount,
    int FirstMediaWireLength,
    int FirstAudioPcmPayloadLength,
    string FirstAudioPcmPayloadSha256,
    int FirstAudioPcmValue,
    int FirstAudioPcmBufferTime,
    int ReceiverPlayingState,
    double BoundedStreamingObservationSeconds,
    bool ReceiverReadinessReached,
    bool ApplicationStopInvoked,
    bool PauseResumeCloseOrAddMirrorSent,
    bool RetryFallbackOrAlternateTargetUsed,
    bool DebugProcessAndOwnedSocketsClosed,
    bool? UserConfirmedAudibleAtReceiver);

/// <summary>
/// Immutable evidence from the 2026-08-07 off-screen, single-target WinUI
/// validation. Creating this snapshot performs no network or UI operation.
/// </summary>
public static class MiPlayMainApplicationLiveValidationEvidence
{
    public static MiPlayMainApplicationLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            ValidationDate: "2026-08-07",
            ReceiverSelectionLabel: "小爱音箱-7503 · S12",
            ReceiverAddress: "192.168.10.3",
            SourceAddress: "192.168.10.9",
            AdvertisedDurationMilliseconds: 600_000,
            SetMediaInfoPayloadLength: 179,
            SetMediaInfoPayloadSha256: "83A6859C90535005160C904B8D23126ACB6C586A652429615D166E61A052BB0E",
            FirstMediaAccessUnitLength: 721,
            FirstMediaRtpFrameCount: 2,
            FirstMediaWireLength: 1_536,
            FirstAudioPcmPayloadLength: 49,
            FirstAudioPcmPayloadSha256: "4A1F05659BC922581465FE95C026C7A624863D42FBA2A545BC003C5DF28F33CE",
            FirstAudioPcmValue: 1,
            FirstAudioPcmBufferTime: 0,
            ReceiverPlayingState: 2,
            BoundedStreamingObservationSeconds: 12.475,
            ReceiverReadinessReached: true,
            ApplicationStopInvoked: true,
            PauseResumeCloseOrAddMirrorSent: false,
            RetryFallbackOrAlternateTargetUsed: false,
            DebugProcessAndOwnedSocketsClosed: true,
            UserConfirmedAudibleAtReceiver: null);

    public static bool ProvesMainApplicationTransportReady(
        MiPlayMainApplicationLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.ReceiverReadinessReached &&
               snapshot.FirstAudioPcmValue == 1 &&
               snapshot.ReceiverPlayingState == 2 &&
               snapshot.FirstMediaAccessUnitLength > 0 &&
               snapshot.FirstMediaRtpFrameCount is 1 or 2 &&
               snapshot.FirstMediaWireLength > 0 &&
               snapshot.BoundedStreamingObservationSeconds >= 12 &&
               snapshot.ApplicationStopInvoked &&
               !snapshot.PauseResumeCloseOrAddMirrorSent &&
               !snapshot.RetryFallbackOrAlternateTargetUsed &&
               snapshot.DebugProcessAndOwnedSocketsClosed;
    }
}
