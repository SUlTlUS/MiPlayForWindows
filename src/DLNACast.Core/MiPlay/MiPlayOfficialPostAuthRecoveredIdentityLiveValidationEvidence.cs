namespace DLNACast.Core.MiPlay;

public sealed record MiPlayOfficialPostAuthRecoveredIdentityLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool NativeNoResetOutboundProfileUsed,
    bool RecoveredOfficialSourceIdentitySent,
    bool FirstFrameMatchedRecoveredPhonePcapLength,
    bool LocalDeviceInfoAcknowledgement0059Observed,
    bool GetDeviceInfo001eSent,
    bool CanAlonePlayCtrl0058Sent,
    bool AlonePlayCapacity0058Sent,
    bool GetMirrorMode0034Sent,
    bool SetPlaySource0040Sent,
    bool SocketAbortedAfterRecoveredIdentity0058,
    bool RetryOrFallbackSent,
    bool CmdOpenSent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
    bool RecoveredIdentityAccepted,
    bool AuthorizesNextFrame,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Captures the bounded S12 live validation that sent the fully reconstructed
/// official phone first 0x0058 source identity frame after mutual SafetyAuth.
/// The receiver still closed before any post-auth acknowledgement.
/// </summary>
public static class MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 1_776;
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
    public const ushort LegacySafetyAuthChallengeSequence = 0x0338;
    public const ushort ModeNotifySequence = 0x0339;
    public const ushort MediaInfoNotifySequence = 0x033A;
    public const ushort StateNotifySequence = 0x033B;
    public const int StateNotifyIntegerValue = 0;
    public const ushort FirstPostAuthCommandSequence = 0x0004;
    public const ushort FirstPostAuthCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
    public const ushort ExpectedFirstPostAuthAcknowledgement =
        MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand;
    public const string FirstPostAuthStepKind = "SendSourceName";
    public const int FirstPostAuthPlaintextPayloadLength =
        MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoPlaintextLength;
    public const int FirstPostAuthEncryptedPayloadLength =
        MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoSafetyDataPayloadLength;
    public const int PreviousDefaultWindowsEncryptedPayloadLength =
        MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthEncryptedPayloadLength;
    public const int PostAuthFramesObservedAfterFirst0058 = 0;
    public const int FollowUpFrameCountBeforeAbort = 7;
    public const int SocketNativeErrorAfterFirst0058 = 10_053;
    public const string SelectedSafetyAuthCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string OutboundProfile = MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel;
    public const string NextOfflineHypothesis =
        "the source identity bytes are valid for the captured mid-session update, but sequence 0x013a did not prove they belong immediately after DealSafetyDone; capture the official phone's first fresh post-auth frame with an authentication-only test receiver before another S12 send";

    public static MiPlayOfficialPostAuthRecoveredIdentityLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            NativeNoResetOutboundProfileUsed: true,
            RecoveredOfficialSourceIdentitySent: true,
            FirstFrameMatchedRecoveredPhonePcapLength: true,
            LocalDeviceInfoAcknowledgement0059Observed: false,
            GetDeviceInfo001eSent: false,
            CanAlonePlayCtrl0058Sent: false,
            AlonePlayCapacity0058Sent: false,
            GetMirrorMode0034Sent: false,
            SetPlaySource0040Sent: false,
            SocketAbortedAfterRecoveredIdentity0058: true,
            RetryOrFallbackSent: false,
            CmdOpenSent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision EvaluateResult(
        MiPlayOfficialPostAuthRecoveredIdentityLiveValidationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
                RecoveredIdentityAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "Mutual SafetyAuth did not complete, so the recovered-identity first 0x0058 behaviour was not tested.",
                NextOfflineTarget: "re-establish the authenticated boundary before considering post-auth evidence");
        }

        if (!snapshot.NativeNoResetOutboundProfileUsed ||
            !snapshot.RecoveredOfficialSourceIdentitySent ||
            !snapshot.FirstFrameMatchedRecoveredPhonePcapLength)
        {
            return new MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
                RecoveredIdentityAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The live validation did not send the intended native-no-reset recovered official first 0x0058 source identity frame.",
                NextOfflineTarget: "fix the local dry-run shape before any S12 retry");
        }

        if (snapshot.RetryOrFallbackSent ||
            snapshot.GetDeviceInfo001eSent ||
            snapshot.CanAlonePlayCtrl0058Sent ||
            snapshot.AlonePlayCapacity0058Sent ||
            snapshot.GetMirrorMode0034Sent ||
            snapshot.SetPlaySource0040Sent ||
            snapshot.CmdOpenSent ||
            snapshot.AddMirrorSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
                RecoveredIdentityAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The recovered-identity live validation boundary was exceeded and cannot be used as isolated 0x0058 evidence.",
                NextOfflineTarget: "discard this run as bounded first-frame evidence");
        }

        if (!snapshot.LocalDeviceInfoAcknowledgement0059Observed ||
            snapshot.SocketAbortedAfterRecoveredIdentity0058)
        {
            return new MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
                RecoveredIdentityAccepted: false,
                AuthorizesNextFrame: false,
                Reason: "The S12 completed mutual SafetyAuth, then received the recovered official 80-byte / 105-byte 0x0058 payload and closed without a 0x0059 acknowledgement. No 0x001e, 0x0034, or 0x0040 was sent. The source bytes and length match a real phone frame, but that phone frame was captured mid-session at sequence 0x013a, so this negative run proves only that replaying the mid-session update as the fresh DealSafetyDone successor is invalid. It does not rule out the same 0x0058 payload at its proper later lifecycle point and authorizes no retry, Open, AddMirror, RTSP, media, playback, or audio.",
                NextOfflineTarget: "capture the official phone's first command after a fresh type-2 mutual SafetyAuth session with the bounded authentication-only test receiver, preserving CBC continuation and sending no business acknowledgement");
        }

        return new MiPlayOfficialPostAuthRecoveredIdentityLiveValidationDecision(
            RecoveredIdentityAccepted: true,
            AuthorizesNextFrame: false,
            Reason: "The receiver acknowledged the recovered official first 0x0058 source identity. This would validate only the first local-device-info gate, not later 0x001e/0x0034/0x0040 or media paths.",
            NextOfflineTarget: "inspect the 0x0059 payload and only then design the next bounded gate");
    }
}
