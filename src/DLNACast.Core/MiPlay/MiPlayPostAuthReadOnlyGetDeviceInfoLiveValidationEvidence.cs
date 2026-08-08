namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool NativeNoResetOutboundProfileUsed,
    bool SafetyDataWrappedGetDeviceInfoSent,
    bool EmptyPlaintextPayloadSent,
    bool GetDeviceInfoAcknowledgementObserved,
    bool DeviceClosedControlAfterGetDeviceInfo,
    bool RetryOrFallbackSent,
    bool SetPlaySource0040Sent,
    bool SetLocalDeviceInfo0058Sent,
    bool CmdOpenSent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
    bool PostAuthGetDeviceInfoAccepted,
    bool AuthorizesNextFrame,
    string Reason);

/// <summary>
/// Captures the bounded S12 live validation that sent exactly one
/// SafetyData-wrapped Cmd_GetDeviceInfo frame after mutual SafetyAuth using the
/// native no-reset post-auth outbound profile.
/// </summary>
public static class MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 1_542;
    public const int DeviceControlPort = MiPlayProtocolConstants.DefaultControlPort;
    public const string CurrentLx06FirmwareVersion =
        MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement =
        MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const string NativeSourceVersionSent = MiPlayProtocolConstants.NativeSourceVersion18_0_0_3;
    public const ushort NativeSourceVersionSequence = 0x0001;
    public const ushort SafetyInfoSequence = 0x0002;
    public const ushort LocalSafetyAuthSequence = 0x0003;
    public const ushort PeerSafetyAuthChallengeSequence = 0x0000;
    public const ushort ModeNotifySequence = 0x0223;
    public const ushort MediaInfoNotifySequence = 0x0224;
    public const ushort ReadyStateNotifySequence = 0x0225;
    public const string ReadyStateNotifyLabel = "state";
    public const int ReadyStateNotifyIntegerValue = 3;
    public const ushort GetDeviceInfoCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
    public const ushort GetDeviceInfoSequence = 0x0004;
    public const int GetDeviceInfoPlaintextPayloadLength = 0;
    public const int EncryptedGetDeviceInfoPayloadLength = 25;
    public const ushort ExpectedGetDeviceInfoAcknowledgementCommand =
        MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand;
    public const int MinimumExpectedAcknowledgementPayloadLength =
        MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const string SelectedSafetyAuthCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string OutboundProfile = MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel;
    public const string NextOfflineHypothesis =
        "native-no-reset outbound SafetyData plus empty 0x001e is not sufficient on the tested S12; the next gap is still ambiguous between post-auth command-session cipher phase, command-session/listener readiness, and receiver-side session context";

    public static MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            NativeNoResetOutboundProfileUsed: true,
            SafetyDataWrappedGetDeviceInfoSent: true,
            EmptyPlaintextPayloadSent: true,
            GetDeviceInfoAcknowledgementObserved: false,
            DeviceClosedControlAfterGetDeviceInfo: true,
            RetryOrFallbackSent: false,
            SetPlaySource0040Sent: false,
            SetLocalDeviceInfo0058Sent: false,
            CmdOpenSent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision EvaluateResult(
        MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
                PostAuthGetDeviceInfoAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "Mutual SafetyAuth did not complete, so post-auth Cmd_GetDeviceInfo behaviour was not tested.");
        }

        if (!snapshot.NativeNoResetOutboundProfileUsed ||
            !snapshot.SafetyDataWrappedGetDeviceInfoSent ||
            !snapshot.EmptyPlaintextPayloadSent)
        {
            return new MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
                PostAuthGetDeviceInfoAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The live validation did not send the intended native-no-reset SafetyData-wrapped empty Cmd_GetDeviceInfo frame.");
        }

        if (snapshot.RetryOrFallbackSent ||
            snapshot.SetPlaySource0040Sent ||
            snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.CmdOpenSent ||
            snapshot.AddMirrorSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
                PostAuthGetDeviceInfoAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The read-only post-auth getDeviceInfo boundary was exceeded and cannot be used as isolated 0x001e evidence.");
        }

        if (!snapshot.GetDeviceInfoAcknowledgementObserved)
        {
            return new MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
                PostAuthGetDeviceInfoAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The S12 accepted mutual SafetyAuth, then received exactly one native-no-reset SafetyData-wrapped empty Cmd_GetDeviceInfo 0x001e frame with sequence 0x0004 and closed without a same-sequence 0x001f acknowledgement. This rules out treating legacy clear 0x001e success as automatic proof of post-auth SafetyData 0x001e success. It does not distinguish cipher phase mismatch from command-session/listener/context readiness, and it authorizes no 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio frame.");
        }

        return new MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationDecision(
            PostAuthGetDeviceInfoAccepted: true,
            AuthorizesNextFrame: false,
            Reason: "The receiver returned a decryptable same-sequence 0x001f Cmd_GetDeviceInfo acknowledgement. This validates only the read-only post-auth getDeviceInfo gate and does not authorize 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.");
    }
}
