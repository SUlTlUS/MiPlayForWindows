namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthOutboundProfileDryRunLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool PeerSafetyAuthChallengeDecoded,
    bool PeerSafetyAuthAcknowledgementVerified,
    bool DryRunComparisonPrinted,
    bool PostAuthBusinessFrameSent,
    bool SetPlaySourceFrameSent,
    bool GetDeviceInfoFrameSent,
    bool SetLocalDeviceInfo0058Sent,
    bool CmdOpenSent,
    bool AddMirrorSent,
    bool RtspListenerOrResponseUsed,
    bool MediaOrRtpSent,
    bool PlaybackOrAudioSent);

public sealed record MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision(
    bool UsefulDryRunEvidence,
    bool AuthorizesPostAuthBusinessSend,
    string Reason);

/// <summary>
/// Captures the bounded S12 live run that completed mutual SafetyAuth and then
/// printed post-auth outbound SafetyData candidate frame hashes without sending
/// any post-auth business, media, RTSP, playback, or audio frame.
/// </summary>
public static class MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 12_679;
    public const int DeviceControlPort = MiPlayProtocolConstants.DefaultControlPort;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const string NativeSourceVersionSent = MiPlayProtocolConstants.NativeSourceVersion18_0_0_3;
    public const ushort NativeSourceVersionSequence = 0x0001;
    public const ushort SafetyInfoSequence = 0x0002;
    public const ushort LocalSafetyAuthSequence = 0x0003;
    public const ushort PeerSafetyAuthChallengeSequence = 0x0000;
    public const ushort DryRunPostAuthSequence = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SetPlaySourceSequence;
    public const ushort DryRunCommand = MiPlayProtocolConstants.SetPlaySourceCommand;
    public const int OfficialJsonPlaintextLength = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.PlaintextPayloadLength;
    public const int DryRunSafetyDataPayloadLength = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength;
    public const int DryRunCommandFrameLength = 82;
    public const string SelectedSafetyAuthCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const string NativeNoResetOutboundProfile = MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel;
    public const string OldProbeNegativeControlProfile = MiPlayPostAuthSafetyDataCipherProfile.ObservedInboundPromotedOutboundProfileLabel;
    public const string NativeNoResetCommandFrameSha256 = "29508b1064aaaa901e5de0d9e0b4467b4fcd42a9f334f4bca9f681fc3f0665bd";
    public const string OldProbeNegativeControlCommandFrameSha256 = "41d298788a1a63930b706eb82c55554e756161024032a4148fd75f058948bee7";
    public const string Boundary = "Mutual SafetyAuth plus dry-run only: the run printed post-auth Cmd_SetPlaySource candidate frame hashes but sent no post-auth business frame.";

    public static MiPlayPostAuthOutboundProfileDryRunLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            PeerSafetyAuthChallengeDecoded: true,
            PeerSafetyAuthAcknowledgementVerified: true,
            DryRunComparisonPrinted: true,
            PostAuthBusinessFrameSent: false,
            SetPlaySourceFrameSent: false,
            GetDeviceInfoFrameSent: false,
            SetLocalDeviceInfo0058Sent: false,
            CmdOpenSent: false,
            AddMirrorSent: false,
            RtspListenerOrResponseUsed: false,
            MediaOrRtpSent: false,
            PlaybackOrAudioSent: false);

    public static MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision EvaluateResult(
        MiPlayPostAuthOutboundProfileDryRunLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted ||
            !snapshot.PeerSafetyAuthChallengeDecoded ||
            !snapshot.PeerSafetyAuthAcknowledgementVerified)
        {
            return new MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision(
                UsefulDryRunEvidence: false,
                AuthorizesPostAuthBusinessSend: false,
                Reason: "Mutual SafetyAuth did not fully complete, so post-auth outbound profile bytes were not grounded in a verified live session.");
        }

        if (snapshot.PostAuthBusinessFrameSent ||
            snapshot.SetPlaySourceFrameSent ||
            snapshot.GetDeviceInfoFrameSent ||
            snapshot.SetLocalDeviceInfo0058Sent ||
            snapshot.CmdOpenSent ||
            snapshot.AddMirrorSent ||
            snapshot.RtspListenerOrResponseUsed ||
            snapshot.MediaOrRtpSent ||
            snapshot.PlaybackOrAudioSent)
        {
            return new MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision(
                UsefulDryRunEvidence: false,
                AuthorizesPostAuthBusinessSend: false,
                Reason: "The dry-run boundary was exceeded by a post-auth business, RTSP, media, playback, or audio action.");
        }

        if (!snapshot.DryRunComparisonPrinted)
        {
            return new MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision(
                UsefulDryRunEvidence: false,
                AuthorizesPostAuthBusinessSend: false,
                Reason: "The run completed SafetyAuth but did not print the post-auth outbound profile comparison.");
        }

        return new MiPlayPostAuthOutboundProfileDryRunLiveValidationDecision(
            UsefulDryRunEvidence: true,
            AuthorizesPostAuthBusinessSend: false,
            Reason: "The S12 run grounded the first post-auth send-only byte divergence in a verified mutual SafetyAuth session: native no-reset outbound and the old observed-inbound-promoted negative control produce different Cmd_SetPlaySource frame hashes. This is useful evidence but it does not prove receiver acceptance and does not authorize a post-auth business send.");
    }
}