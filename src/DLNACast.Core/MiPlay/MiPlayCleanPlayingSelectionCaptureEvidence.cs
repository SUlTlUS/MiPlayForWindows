namespace DLNACast.Core.MiPlay;

public sealed record MiPlayCleanPlayingSelectionCaptureSnapshot(
    string TraceRelativePath,
    string TraceSha256,
    ushort SetPlaySourceSequence,
    ushort OpenSequence,
    ushort SetMediaInfoSequence,
    ushort FirstPeriodicHeartbeatSequence,
    int SetMediaInfoPayloadLength,
    string SetMediaInfoPayloadSha256,
    int Status,
    int Volume,
    int DeviceState,
    int PauseCommandCount,
    int ResumeCommandCount,
    bool FirstAudioPcmObserved,
    int ReceiverPlayingState,
    double OpenToSetMediaInfoMilliseconds,
    double SetMediaInfoToTimeOffsetMilliseconds,
    double TimeOffsetToFirstMediaMilliseconds,
    double FirstAudioPcmToPlayingStateMilliseconds,
    double HeartbeatIntervalAcrossOpenMilliseconds);

public sealed record MiPlayCleanPlayingSelectionCaptureDecision(
    bool ProvesAutomaticSelectionHasNoPauseOrResume,
    bool ProvesPlayingDeviceStateTwo,
    bool ProvesHeartbeatTimerContinuesAcrossOpen,
    bool SupportsCorrectedWindowsStartupModel,
    string Reason);

/// <summary>
/// Redacted deterministic evidence from the clean rooted-phone capture where
/// music was already playing before receiver selection and no playback control
/// was touched for ten seconds afterward.
/// </summary>
public static class MiPlayCleanPlayingSelectionCaptureEvidence
{
    public static MiPlayCleanPlayingSelectionCaptureSnapshot CreateCurrentSnapshot() =>
        new(
            TraceRelativePath: "artifacts/phone_live/clean-selection-captures/mipad4-clean-playing-selection-20260807-172046.strace",
            TraceSha256: "71187E8D9B3DB1637D7A70648DA4975106247C81CD9534CF94B97EFB322A081E",
            SetPlaySourceSequence: 0x0097,
            OpenSequence: 0x0098,
            SetMediaInfoSequence: 0x0099,
            FirstPeriodicHeartbeatSequence: 0x009a,
            SetMediaInfoPayloadLength: 180,
            SetMediaInfoPayloadSha256: "76B303AEA73991DC26E0BAA3CF60E33569C724EEA1556938EDEBF3D764892197",
            Status: 0,
            Volume: 25,
            DeviceState: 2,
            PauseCommandCount: 0,
            ResumeCommandCount: 0,
            FirstAudioPcmObserved: true,
            ReceiverPlayingState: 2,
            OpenToSetMediaInfoMilliseconds: 652.424,
            SetMediaInfoToTimeOffsetMilliseconds: 309.603,
            TimeOffsetToFirstMediaMilliseconds: 3.501,
            FirstAudioPcmToPlayingStateMilliseconds: 3.952,
            HeartbeatIntervalAcrossOpenMilliseconds: 4_999.827);

    public static MiPlayCleanPlayingSelectionCaptureDecision Evaluate(
        MiPlayCleanPlayingSelectionCaptureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var noPlaybackControls =
            snapshot.PauseCommandCount == 0 && snapshot.ResumeCommandCount == 0;
        var playingDeviceState =
            snapshot.Status == 0 && snapshot.DeviceState == 2 &&
            snapshot.FirstAudioPcmObserved && snapshot.ReceiverPlayingState == 2;
        var heartbeatContinues =
            Math.Abs(snapshot.HeartbeatIntervalAcrossOpenMilliseconds - 5_000) < 1;
        var timingOrder =
            snapshot.OpenToSetMediaInfoMilliseconds > 0 &&
            snapshot.SetMediaInfoToTimeOffsetMilliseconds > 0 &&
            snapshot.TimeOffsetToFirstMediaMilliseconds > 0;

        return new(
            ProvesAutomaticSelectionHasNoPauseOrResume: noPlaybackControls,
            ProvesPlayingDeviceStateTwo: playingDeviceState,
            ProvesHeartbeatTimerContinuesAcrossOpen: heartbeatContinues,
            SupportsCorrectedWindowsStartupModel:
                noPlaybackControls && playingDeviceState && heartbeatContinues && timingOrder,
            Reason:
                "The clean already-playing phone trace sends SetMediaInfo status=0/deviceState=2 between PLAY acknowledgement and TIME_OFFSET, then the receiver reports first-audiopcm=1 and state=2 without Pause, Resume, or a startup heartbeat; the ordinary heartbeat remains anchored to the pre-Open five-second cadence.");
    }
}
