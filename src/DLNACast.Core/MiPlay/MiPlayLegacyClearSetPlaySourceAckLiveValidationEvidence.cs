namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyClearSetPlaySourceAckLiveRun(
    string Mode,
    bool NativeVersionSent,
    bool LegacyChallengeAcknowledged,
    bool ReadyStateNotifyObservedBeforeSetPlaySource,
    bool ModernSafetyInfoSent,
    bool SafetyAuthSent,
    bool SafetyDataUsed,
    ushort SetPlaySourceSequence,
    bool EmptyPlaintextPayloadSent,
    bool SetPlaySourceAcknowledgementObserved,
    bool DeviceClosedControlAfterSetPlaySource,
    int FollowUpFrameCountBeforeClose,
    bool CmdOpenSent,
    bool SetLocalDeviceInfo0058Sent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlayLegacyClearSetPlaySourceAckLiveDecision(bool LegacyClearDispatcherVerified, string Reason);

/// <summary>
/// Captures the two bounded S12 live checks that tried the LX06 1.88.x legacy
/// clear-text 0x0040 dispatcher without modern SafetyInfo/SafetyAuth.
/// </summary>
public static class MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const ushort SetPlaySourceSequence = 0x0002;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const int ImmediateRunFollowUpFrameCountBeforeClose = 4;
    public const int AfterReadyNotifyRunFollowUpFrameCountBeforeClose = 4;
    public const string NextOfflineHypothesis = "current 1.94 receiver is not routing legacy clear business frames into the LX06 1.88.51 ServerApp dispatcher";

    public static MiPlayLegacyClearSetPlaySourceAckLiveRun CreateImmediateSnapshot() =>
        CreateSnapshot(
            Mode: "immediate-after-0x0029",
            ReadyStateNotifyObservedBeforeSetPlaySource: false,
            FollowUpFrameCountBeforeClose: ImmediateRunFollowUpFrameCountBeforeClose);

    public static MiPlayLegacyClearSetPlaySourceAckLiveRun CreateAfterReadyNotifySnapshot() =>
        CreateSnapshot(
            Mode: "after-state-3-notify",
            ReadyStateNotifyObservedBeforeSetPlaySource: true,
            FollowUpFrameCountBeforeClose: AfterReadyNotifyRunFollowUpFrameCountBeforeClose);

    public static MiPlayLegacyClearSetPlaySourceAckLiveDecision EvaluateLegacyClearResult(
        MiPlayLegacyClearSetPlaySourceAckLiveRun immediateRun,
        MiPlayLegacyClearSetPlaySourceAckLiveRun afterReadyNotifyRun)
    {
        if (!IsStrictBoundary(immediateRun) || !IsStrictBoundary(afterReadyNotifyRun))
        {
            return new MiPlayLegacyClearSetPlaySourceAckLiveDecision(false, "At least one legacy clear validation exceeded the no-media/no-modern-SafetyAuth boundary.");
        }

        if (!immediateRun.LegacyChallengeAcknowledged || !afterReadyNotifyRun.LegacyChallengeAcknowledged)
        {
            return new MiPlayLegacyClearSetPlaySourceAckLiveDecision(false, "Legacy 0x0028 -> 0x0029 was not completed in both runs, so clear business dispatch was not tested.");
        }

        if (!afterReadyNotifyRun.ReadyStateNotifyObservedBeforeSetPlaySource)
        {
            return new MiPlayLegacyClearSetPlaySourceAckLiveDecision(false, "The delayed legacy run did not wait for the receiver's state=3 ready notify before sending 0x0040.");
        }

        if (immediateRun.SetPlaySourceAcknowledgementObserved || afterReadyNotifyRun.SetPlaySourceAcknowledgementObserved)
        {
            return new MiPlayLegacyClearSetPlaySourceAckLiveDecision(true, "A clear 0x0041 Cmd_SetPlaySource acknowledgement was observed.");
        }

        return new MiPlayLegacyClearSetPlaySourceAckLiveDecision(
            false,
            "Both legacy clear ACK-only validations completed 0x0028 -> 0x0029 and sent one empty clear-text 0x0040 without 0x1400, 0x1402, 0x1403, SafetyData, JSON, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, or audio. The immediate and after-state=3-notify runs both closed without 0x0041, so current S12 1.94 behaviour is not reaching the LX06 1.88.51 clear ServerApp::doMpasCommand dispatcher.");
    }

    private static MiPlayLegacyClearSetPlaySourceAckLiveRun CreateSnapshot(
        string Mode,
        bool ReadyStateNotifyObservedBeforeSetPlaySource,
        int FollowUpFrameCountBeforeClose) =>
        new(
            Mode,
            NativeVersionSent: true,
            LegacyChallengeAcknowledged: true,
            ReadyStateNotifyObservedBeforeSetPlaySource,
            ModernSafetyInfoSent: false,
            SafetyAuthSent: false,
            SafetyDataUsed: false,
            SetPlaySourceSequence,
            EmptyPlaintextPayloadSent: true,
            SetPlaySourceAcknowledgementObserved: false,
            DeviceClosedControlAfterSetPlaySource: true,
            FollowUpFrameCountBeforeClose,
            CmdOpenSent: false,
            SetLocalDeviceInfo0058Sent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    private static bool IsStrictBoundary(MiPlayLegacyClearSetPlaySourceAckLiveRun run) =>
        run.NativeVersionSent &&
        run.EmptyPlaintextPayloadSent &&
        !run.ModernSafetyInfoSent &&
        !run.SafetyAuthSent &&
        !run.SafetyDataUsed &&
        !run.CmdOpenSent &&
        !run.SetLocalDeviceInfo0058Sent &&
        !run.AddMirrorSent &&
        !run.RtspListenerOrResponseUsed &&
        !run.MediaOrRtpSent &&
        !run.PlaybackOrAudioSent;
}
