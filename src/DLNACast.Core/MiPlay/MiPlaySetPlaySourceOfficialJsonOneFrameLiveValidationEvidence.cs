namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool OfficialMinimalJsonPayloadSent,
    bool SafetyDataWrappedSetPlaySourceSent,
    bool SetPlaySourceAcknowledgementObserved,
    bool DeviceClosedControlAfterSetPlaySource,
    bool RetryOrFallbackSent,
    bool CmdOpenSent,
    bool SetLocalDeviceInfo0058Sent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
    bool OfficialJsonSetPlaySourceAccepted,
    string Reason);

/// <summary>
/// Captures the bounded S12 live validation that sent exactly one official
/// Android minimal JSON Cmd_SetPlaySource payload after mutual SafetyAuth.
/// This is evidence, not a reusable probe policy.
/// </summary>
public static class MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 12_037;
    public const int DeviceControlPort = 8_899;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const string NativeSourceVersionSent = MiPlayProtocolConstants.NativeSourceVersion18_0_0_3;
    public const ushort NativeSourceVersionSequence = 0x0001;
    public const ushort SafetyInfoSequence = 0x0002;
    public const ushort LocalSafetyAuthSequence = 0x0003;
    public const ushort PeerSafetyAuthChallengeSequence = 0x0000;
    public const ushort SetPlaySourceSequence = 0x0004;
    public const int PlaintextPayloadLength = 61;
    public const int EncryptedSetPlaySourcePayloadLength = 73;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const int SocketNativeErrorAfterSetPlaySource = 10_053;
    public const string SelectedSafetyDataCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string OfficialMinimalPayloadText =
        "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}";
    public const string NextOfflineHypothesis =
        "official SetPlaySource JSON is not sufficient; continue below payload semantics into post-auth SafetyData direction/IV state, command envelope, or current 1.94 handler ownership";

    public static MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            OfficialMinimalJsonPayloadSent: true,
            SafetyDataWrappedSetPlaySourceSent: true,
            SetPlaySourceAcknowledgementObserved: false,
            DeviceClosedControlAfterSetPlaySource: true,
            RetryOrFallbackSent: false,
            CmdOpenSent: false,
            SetLocalDeviceInfo0058Sent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision EvaluateResult(
        MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
                false,
                "Mutual SafetyAuth did not complete, so official JSON Cmd_SetPlaySource behaviour was not tested.");
        }

        if (!snapshot.OfficialMinimalJsonPayloadSent || !snapshot.SafetyDataWrappedSetPlaySourceSent)
        {
            return new MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
                false,
                "The live validation did not send the intended SafetyData-wrapped official minimal JSON Cmd_SetPlaySource frame.");
        }

        if (snapshot.RetryOrFallbackSent ||
            snapshot.CmdOpenSent ||
            snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.AddMirrorSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
                false,
                "The one-frame live validation boundary was exceeded and cannot be used as official JSON SetPlaySource evidence.");
        }

        if (!snapshot.SetPlaySourceAcknowledgementObserved)
        {
            return new MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
                false,
                "The S12 accepted mutual SafetyAuth, then received exactly one SafetyData-wrapped official minimal JSON Cmd_SetPlaySource 0x0040 payload and closed without a 0x0041 acknowledgement. Because both empty and official JSON 0x0040 probes now fail the same way, the missing layer is unlikely to be ref_channel/ref_function/ref_content payload semantics; continue with post-auth SafetyData direction/IV state, command envelope, or current LX06 1.94 handler ownership.");
        }

        return new MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationDecision(
            true,
            "The receiver returned a decryptable 0x0041 Cmd_SetPlaySource acknowledgement for the official minimal JSON payload.");
    }
}
