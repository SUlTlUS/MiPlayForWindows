namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceAckLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
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
    bool PlaybackCommandSent);

public sealed record MiPlaySetPlaySourceAckLiveValidationDecision(bool DispatcherAckVerified, string Reason);

/// <summary>
/// Result of the bounded S12 live validation that sent exactly one empty
/// Cmd_SetPlaySource frame after mutual SafetyAuth. This is evidence, not a
/// reusable probe policy.
/// </summary>
public static class MiPlaySetPlaySourceAckLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 4_828;
    public const int DeviceControlPort = 8_899;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const ushort SetPlaySourceSequence = 0x0004;
    public const int PlaintextPayloadLength = 0;
    public const int EncryptedSetPlaySourcePayloadLength = 25;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const string SelectedSafetyDataCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string NextOfflineHypothesis = "post-auth SafetyData/session routing is below ServerApp::doMpasCommand or the command envelope is still mismatched";

    public static MiPlaySetPlaySourceAckLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
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
            PlaybackCommandSent: false);

    public static MiPlaySetPlaySourceAckLiveValidationDecision EvaluateAckResult(
        MiPlaySetPlaySourceAckLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlaySetPlaySourceAckLiveValidationDecision(false, "Mutual SafetyAuth did not complete, so Cmd_SetPlaySource behaviour was not tested.");
        }

        if (!snapshot.SafetyDataWrappedSetPlaySourceSent || !snapshot.EmptyPlaintextPayloadSent)
        {
            return new MiPlaySetPlaySourceAckLiveValidationDecision(false, "The live validation did not send the intended empty-plaintext SafetyData-wrapped Cmd_SetPlaySource frame.");
        }

        if (snapshot.JsonSourceIdentitySent || snapshot.CmdOpenSent || snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.AddMirrorSent || snapshot.RtspListenerOrResponseUsed || snapshot.MediaOrRtpSent ||
            snapshot.PlaybackCommandSent)
        {
            return new MiPlaySetPlaySourceAckLiveValidationDecision(false, "The live validation boundary was exceeded and cannot be used as ACK-only evidence.");
        }

        if (!snapshot.SetPlaySourceAcknowledgementObserved)
        {
            return new MiPlaySetPlaySourceAckLiveValidationDecision(
                false,
                "The S12 accepted mutual SafetyAuth and received one SafetyData-wrapped empty Cmd_SetPlaySource 0x0040, then closed the 8899 control connection without a 0x0041 acknowledgement. Because LX06 1.88.51 mpas sends 0x0041 before payload-length or JSON parsing, this negative result points below ServerApp::doMpasCommand: post-auth SafetyData/session routing, command envelope, or handler ownership is still mismatched rather than missing source-identity JSON.");
        }

        return new MiPlaySetPlaySourceAckLiveValidationDecision(true, "The receiver returned a decryptable 0x0041 Cmd_SetPlaySource acknowledgement.");
    }
}