namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationSnapshot(
    bool MutualSafetyAuthVerified,
    bool PostAuthDelayObservedBeforeSetPlaySource,
    bool SafetyDataWrappedSetPlaySourceSent,
    bool EmptyPlaintextPayloadSent,
    bool SetPlaySourceAcknowledgementObserved,
    bool DeviceClosedControlAfterSetPlaySource,
    bool JsonSourceIdentitySent,
    bool CmdOpenSent,
    bool SetLocalDeviceInfo0058Sent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(bool DispatcherAckVerified, string Reason);

/// <summary>
/// Captures the bounded S12 live check that repeated the encrypted empty 0x0040
/// ACK-only probe after a 500 ms post-mutual-SafetyAuth delay.
/// </summary>
public static class MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 1_734;
    public const int DeviceControlPort = 8_899;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const ushort SetPlaySourceSequence = 0x0004;
    public const int PlaintextPayloadLength = 0;
    public const int EncryptedSetPlaySourcePayloadLength = 25;
    public const int PostAuthSendDelayMilliseconds = 500;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const string SelectedSafetyDataCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string NextOfflineHypothesis = "post-auth command is not reaching the LX06 1.88.51 ServerApp dispatcher even after SafetyAuth timing delay";

    public static MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthVerified: true,
            PostAuthDelayObservedBeforeSetPlaySource: true,
            SafetyDataWrappedSetPlaySourceSent: true,
            EmptyPlaintextPayloadSent: true,
            SetPlaySourceAcknowledgementObserved: false,
            DeviceClosedControlAfterSetPlaySource: true,
            JsonSourceIdentitySent: false,
            CmdOpenSent: false,
            SetLocalDeviceInfo0058Sent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision EvaluateAckResult(
        MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthVerified)
        {
            return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(false, "Mutual SafetyAuth did not complete, so delayed Cmd_SetPlaySource behaviour was not tested.");
        }

        if (!snapshot.PostAuthDelayObservedBeforeSetPlaySource)
        {
            return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(false, "The delayed validation did not wait after local/peer 0x1403 verification before sending 0x0040.");
        }

        if (!snapshot.SafetyDataWrappedSetPlaySourceSent || !snapshot.EmptyPlaintextPayloadSent)
        {
            return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(false, "The live validation did not send the intended empty-plaintext SafetyData-wrapped Cmd_SetPlaySource frame.");
        }

        if (snapshot.JsonSourceIdentitySent || snapshot.CmdOpenSent || snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.AddMirrorSent || snapshot.RtspListenerOrResponseUsed || snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(false, "The delayed live validation boundary was exceeded and cannot be used as ACK-only evidence.");
        }

        if (!snapshot.SetPlaySourceAcknowledgementObserved)
        {
            return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(
                false,
                "The S12 accepted mutual SafetyAuth, the Probe waited 500 ms after local/peer 0x1403 verification, and then sent one SafetyData-wrapped empty Cmd_SetPlaySource 0x0040. The device closed without 0x0041, so the prior no-ACK result is not explained by an immediate post-auth timing race; the missing layer is command-session routing/envelope/handler ownership relative to the current 1.94 receiver stack.");
        }

        return new MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationDecision(true, "The receiver returned a decryptable 0x0041 Cmd_SetPlaySource acknowledgement after the post-auth delay.");
    }
}
