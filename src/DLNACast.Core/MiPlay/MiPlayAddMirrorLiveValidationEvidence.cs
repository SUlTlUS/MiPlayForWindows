namespace DLNACast.Core.MiPlay;

public sealed record MiPlayAddMirrorLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool SafetyDataWrappedAddMirrorSent,
    bool AddMirrorPayloadMatchedRecoveredLocalShape,
    bool AddMirrorAcknowledgementObserved,
    bool DeviceClosedControlAfterAddMirror,
    bool CmdOpenSent,
    bool SetLocalDeviceInfo0058Sent,
    bool SetPlaySource0040Sent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackCommandSent);

public sealed record MiPlayAddMirrorLiveValidationDecision(bool AddMirrorAccepted, string Reason);

/// <summary>
/// Result of the bounded S12 live validation that sent exactly one AddMirror
/// frame after mutual SafetyAuth. This is evidence, not a reusable probe policy.
/// </summary>
public static class MiPlayAddMirrorLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 10_527;
    public const int DeviceControlPort = 8_899;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const ushort AddMirrorSequence = 0x0004;
    public const string AddMirrorPayload = "192.168.10.9:7236&from:192.168.10.9&islocal:1";
    public const int EncryptedAddMirrorPayloadLength = 57;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const string SelectedSafetyDataCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const bool RecoveredLocalAddMirrorPayloadShapeSent = true;
    public const string NextOfflineHypothesis = "external-source 0x002e direction/role gate or missing sender-info session state";

    public static MiPlayAddMirrorLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            SafetyDataWrappedAddMirrorSent: true,
            AddMirrorPayloadMatchedRecoveredLocalShape: true,
            AddMirrorAcknowledgementObserved: false,
            DeviceClosedControlAfterAddMirror: true,
            CmdOpenSent: false,
            SetLocalDeviceInfo0058Sent: false,
            SetPlaySource0040Sent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackCommandSent: false);

    public static MiPlayAddMirrorLiveValidationDecision EvaluateAddMirrorResult(
        MiPlayAddMirrorLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlayAddMirrorLiveValidationDecision(false, "Mutual SafetyAuth did not complete, so Cmd_AddMirror behaviour was not tested.");
        }

        if (!snapshot.SafetyDataWrappedAddMirrorSent || !snapshot.AddMirrorPayloadMatchedRecoveredLocalShape)
        {
            return new MiPlayAddMirrorLiveValidationDecision(false, "The SafetyData-wrapped AddMirror frame did not match the recovered LX06 local payload shape.");
        }

        if (snapshot.CmdOpenSent || snapshot.SetLocalDeviceInfo0058Sent || snapshot.SetPlaySource0040Sent ||
            snapshot.RtspListenerOrResponseUsed || snapshot.MediaOrRtpSent || snapshot.PlaybackCommandSent)
        {
            return new MiPlayAddMirrorLiveValidationDecision(false, "The live validation boundary was exceeded and cannot be used as AddMirror-only evidence.");
        }

        if (!snapshot.AddMirrorAcknowledgementObserved)
        {
            return new MiPlayAddMirrorLiveValidationDecision(
                false,
                "The S12 accepted mutual SafetyAuth and received one SafetyData-wrapped Cmd_AddMirror payload matching the recovered LX06 local shape, then closed the 8899 control connection without a 0x002f acknowledgement. This does not verify external-source AddMirror acceptance; next evidence should focus on 0x002e receive direction, master/slave role gates, and sender-info session state before trying another live control frame.");
        }

        return new MiPlayAddMirrorLiveValidationDecision(true, "The receiver returned a decryptable 0x002f Cmd_AddMirror acknowledgement.");
    }
}