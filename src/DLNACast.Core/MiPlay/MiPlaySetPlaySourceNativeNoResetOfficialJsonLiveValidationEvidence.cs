namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool OfficialMinimalJsonPayloadSent,
    bool NativeNoResetOutboundProfileUsed,
    bool SafetyDataWrappedSetPlaySourceSent,
    bool SetPlaySourceAcknowledgementObserved,
    bool DeviceClosedControlAfterSetPlaySource,
    bool RetryOrFallbackSent,
    bool CmdOpenSent,
    bool SetLocalDeviceInfo0058Sent,
    bool AddMirrorSent,
    bool GetDeviceInfoSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
    bool NativeNoResetOfficialJsonAccepted,
    bool AuthorizesNextBusinessFrame,
    string Reason);

/// <summary>
/// Captures the bounded S12 live validation that sent exactly one official
/// Android minimal JSON Cmd_SetPlaySource payload after mutual SafetyAuth using
/// the native no-reset post-auth outbound SafetyData profile.
/// </summary>
public static class MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 7_576;
    public const int DeviceControlPort = MiPlayProtocolConstants.DefaultControlPort;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const string NativeSourceVersionSent = MiPlayProtocolConstants.NativeSourceVersion18_0_0_3;
    public const ushort NativeSourceVersionSequence = 0x0001;
    public const ushort SafetyInfoSequence = 0x0002;
    public const ushort LocalSafetyAuthSequence = 0x0003;
    public const ushort PeerSafetyAuthChallengeSequence = 0x0000;
    public const ushort SetPlaySourceSequence = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SetPlaySourceSequence;
    public const ushort SetPlaySourceCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
    public const int PlaintextPayloadLength = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.PlaintextPayloadLength;
    public const int EncryptedSetPlaySourcePayloadLength = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength;
    public const int FollowUpFrameCountBeforeClose = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.FollowUpFrameCountBeforeClose;
    public const string SelectedSafetyAuthCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string OutboundProfile = MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel;
    public const string OfficialMinimalPayloadText = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.OfficialMinimalPayloadText;
    public const string NextOfflineHypothesis = "native no-reset outbound SafetyData plus official minimal JSON is not sufficient; continue with command ordering, source/session context, envelope ownership, or current 1.94 handler state";

    public static MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            OfficialMinimalJsonPayloadSent: true,
            NativeNoResetOutboundProfileUsed: true,
            SafetyDataWrappedSetPlaySourceSent: true,
            SetPlaySourceAcknowledgementObserved: false,
            DeviceClosedControlAfterSetPlaySource: true,
            RetryOrFallbackSent: false,
            CmdOpenSent: false,
            SetLocalDeviceInfo0058Sent: false,
            AddMirrorSent: false,
            GetDeviceInfoSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision EvaluateResult(
        MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
                NativeNoResetOfficialJsonAccepted: false,
                AuthorizesNextBusinessFrame: false,
                Reason: "Mutual SafetyAuth did not complete, so native no-reset Cmd_SetPlaySource behaviour was not tested.");
        }

        if (!snapshot.OfficialMinimalJsonPayloadSent ||
            !snapshot.NativeNoResetOutboundProfileUsed ||
            !snapshot.SafetyDataWrappedSetPlaySourceSent)
        {
            return new MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
                NativeNoResetOfficialJsonAccepted: false,
                AuthorizesNextBusinessFrame: false,
                Reason: "The live validation did not send the intended SafetyData-wrapped official minimal JSON Cmd_SetPlaySource frame with the native no-reset outbound profile.");
        }

        if (snapshot.RetryOrFallbackSent ||
            snapshot.CmdOpenSent ||
            snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.AddMirrorSent ||
            snapshot.GetDeviceInfoSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
                NativeNoResetOfficialJsonAccepted: false,
                AuthorizesNextBusinessFrame: false,
                Reason: "The one-frame native no-reset validation boundary was exceeded and cannot be used as isolated Cmd_SetPlaySource evidence.");
        }

        if (!snapshot.SetPlaySourceAcknowledgementObserved)
        {
            return new MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
                NativeNoResetOfficialJsonAccepted: false,
                AuthorizesNextBusinessFrame: false,
                Reason: "The S12 accepted mutual SafetyAuth, then received exactly one SafetyData-wrapped official minimal JSON Cmd_SetPlaySource 0x0040 frame encrypted with native-no-reset-outbound-type2 and closed without a 0x0041 acknowledgement. This rules out the old promoted-inbound-IV state as the only failure, but native no-reset plus minimal JSON is still insufficient; continue offline with command ordering, source/session context, envelope ownership, or current LX06 1.94 handler state before sending another business frame.");
        }

        return new MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationDecision(
            NativeNoResetOfficialJsonAccepted: true,
            AuthorizesNextBusinessFrame: false,
            Reason: "The receiver returned a decryptable 0x0041 Cmd_SetPlaySource acknowledgement for the native no-reset official minimal JSON payload. This validates only 0x0040 acceptance and does not authorize 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.");
    }
}