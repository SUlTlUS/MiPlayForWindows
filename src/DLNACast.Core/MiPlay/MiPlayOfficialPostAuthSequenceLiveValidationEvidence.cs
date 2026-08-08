namespace DLNACast.Core.MiPlay;

public sealed record MiPlayOfficialPostAuthSequenceLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool NativeNoResetOutboundProfileUsed,
    bool OfficialSequencePlanPrepared,
    bool SourceNameLocalDeviceInfo0058Sent,
    bool SourceNamePayloadUsedDefaultWindowsIdentity,
    bool SourceNamePayloadMatchedRecoveredPhoneIdentity,
    bool LocalDeviceInfoAcknowledgement0059ObservedAfterSourceName,
    bool GetDeviceInfo001eSent,
    bool CanAlonePlayCtrl0058Sent,
    bool AlonePlayCapacity0058Sent,
    bool GetMirrorMode0034Sent,
    bool SetPlaySource0040Sent,
    bool SocketAbortedAfterFirst0058,
    bool RetryOrFallbackSent,
    bool CmdOpenSent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlayOfficialPostAuthSequenceLiveValidationDecision(
    bool OfficialSequenceAccepted,
    bool AuthorizesNextFrame,
    string Reason);

/// <summary>
/// Captures the bounded S12 live validation that attempted the recovered
/// official post-auth command order, but stopped after the first
/// SafetyData-wrapped 0x0058 local-device-info frame closed the control
/// connection. This is evidence, not a reusable probe policy.
/// </summary>
public static class MiPlayOfficialPostAuthSequenceLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 4_434;
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
    public const ushort PeerSafetyAuthProofSequence = 0x0003;
    public const ushort ModeNotifySequence = 0x0335;
    public const ushort MediaInfoNotifySequence = 0x0336;
    public const ushort StateNotifySequence = 0x0337;
    public const int StateNotifyIntegerValue = 0;
    public const ushort FirstPostAuthCommandSequence = 0x0004;
    public const ushort FirstPostAuthCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
    public const ushort ExpectedFirstPostAuthAcknowledgement =
        MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand;
    public const string FirstPostAuthStepKind = "SendSourceName";
    public const string FirstPostAuthPlaintextPayload =
        "{\"sourceName\":\"DLNACast Windows\",\"mSourceBtMac\":\"\"}";
    public const int FirstPostAuthPlaintextPayloadLength = 51;
    public const int FirstPostAuthEncryptedPayloadLength = 73;
    public const int OfficialPhoneFirst0058SafetyDataPayloadLength = 105;
    public const int PostAuthFramesObservedAfterFirst0058 = 0;
    public const int FollowUpFrameCountBeforeAbort = 7;
    public const int SocketNativeErrorAfterFirst0058 = 10_053;
    public const string SelectedSafetyAuthCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string OutboundProfile = MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel;
    public const string RecoveredOfficialPhoneSourceName = "Xiaomi 13 Pro";
    public const string NextOfflineHypothesis =
        "the first 0x0058 source identity/context is the earliest failing gate; the official sourceName/mSourceBtMac JSON is now recovered offline as an 80-byte plaintext / 105-byte SafetyData candidate, but it still needs fresh bounded authorization before any retry";

    public static MiPlayOfficialPostAuthSequenceLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            NativeNoResetOutboundProfileUsed: true,
            OfficialSequencePlanPrepared: true,
            SourceNameLocalDeviceInfo0058Sent: true,
            SourceNamePayloadUsedDefaultWindowsIdentity: true,
            SourceNamePayloadMatchedRecoveredPhoneIdentity: false,
            LocalDeviceInfoAcknowledgement0059ObservedAfterSourceName: false,
            GetDeviceInfo001eSent: false,
            CanAlonePlayCtrl0058Sent: false,
            AlonePlayCapacity0058Sent: false,
            GetMirrorMode0034Sent: false,
            SetPlaySource0040Sent: false,
            SocketAbortedAfterFirst0058: true,
            RetryOrFallbackSent: false,
            CmdOpenSent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlayOfficialPostAuthSequenceLiveValidationDecision EvaluateResult(
        MiPlayOfficialPostAuthSequenceLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
                OfficialSequenceAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "Mutual SafetyAuth did not complete, so the recovered official post-auth sequence was not tested.");
        }

        if (!snapshot.NativeNoResetOutboundProfileUsed ||
            !snapshot.OfficialSequencePlanPrepared ||
            !snapshot.SourceNameLocalDeviceInfo0058Sent)
        {
            return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
                OfficialSequenceAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The live validation did not reach the intended native-no-reset first 0x0058 local-device-info frame.");
        }

        if (snapshot.RetryOrFallbackSent ||
            snapshot.CmdOpenSent ||
            snapshot.AddMirrorSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
                OfficialSequenceAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The official post-auth sequence validation boundary was exceeded and cannot be used as isolated evidence.");
        }

        if (!snapshot.SourceNamePayloadMatchedRecoveredPhoneIdentity)
        {
            return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
                OfficialSequenceAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The S12 completed mutual SafetyAuth, then closed after the first SafetyData-wrapped 0x0058 sourceName/mSourceBtMac frame. No 0x001e, 0x0034, or 0x0040 was sent. This rules out treating the default Windows sourceName with an empty mSourceBtMac as equivalent to the recovered official phone identity; it does not reject the later recovered official command order, and it authorizes no retry, Open, AddMirror, RTSP, media, playback, or audio.");
        }

        if (!snapshot.LocalDeviceInfoAcknowledgement0059ObservedAfterSourceName ||
            snapshot.SocketAbortedAfterFirst0058)
        {
            return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
                OfficialSequenceAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The first recovered-order 0x0058 source context was sent but did not receive a decryptable 0x0059 acknowledgement, so the probe must not advance to 0x001e, 0x0034, or 0x0040.");
        }

        return new MiPlayOfficialPostAuthSequenceLiveValidationDecision(
            OfficialSequenceAccepted: true,
            AuthorizesNextFrame: false,
            Reason: "The receiver accepted the bounded recovered official post-auth sequence without requiring Open, AddMirror, RTSP, media, playback, or audio.");
    }
}
