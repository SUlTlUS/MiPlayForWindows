namespace DLNACast.Core.MiPlay;

public sealed record MiPlayNoMediaRtspLiveValidationSnapshot(
    bool MutualSafetyAuthCompleted,
    bool CmdOpenSentAfterListenerStarted,
    bool SafetyDataWrappedCmdOpen,
    bool DeviceClosedControlAfterCmdOpen,
    bool RtspCallbackObserved,
    bool RtspResponseSent,
    bool MediaOrRtpSent,
    bool SetLocalDeviceInfo0058Sent,
    bool PlaybackCommandSent);

public sealed record MiPlayNoMediaRtspLiveValidationDecision(bool BridgeVerified, string Reason);

/// <summary>
/// Result of the bounded S12 live validation that sent exactly one no-media
/// Cmd_Open after mutual SafetyAuth. This is evidence, not a reusable probe policy.
/// </summary>
public static class MiPlayNoMediaRtspLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const string LocalControlAddress = "192.168.10.9";
    public const int LocalControlPort = 1718;
    public const int DeviceControlPort = 8899;
    public const string CurrentLx06FirmwareVersion = MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement = MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;
    public const int RtspListenPort = 7236;
    public const ushort CmdOpenSequence = 0x0004;
    public const string CmdOpenPayload = "wfd://192.168.10.9:7236?mirrorMode=1";
    public const int EncryptedCmdOpenPayloadLength = 57;
    public const int FollowUpFrameCountBeforeClose = 7;
    public const string SelectedSafetyDataCandidate = "peer-first:observed-s12-inbound-iv-type1";
    public const bool CmdOpenPayloadShapeStaticallyCompatibleWithMpas = true;
    public const string NextOfflineHypothesis = "pre-open source identity/device-info/add-mirror/session context";

    public static MiPlayNoMediaRtspLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            MutualSafetyAuthCompleted: true,
            CmdOpenSentAfterListenerStarted: true,
            SafetyDataWrappedCmdOpen: true,
            DeviceClosedControlAfterCmdOpen: true,
            RtspCallbackObserved: false,
            RtspResponseSent: false,
            MediaOrRtpSent: false,
            SetLocalDeviceInfo0058Sent: false,
            PlaybackCommandSent: false);

    public static MiPlayNoMediaRtspLiveValidationDecision EvaluateBridgeResult(
        MiPlayNoMediaRtspLiveValidationSnapshot snapshot)
    {
        if (!snapshot.MutualSafetyAuthCompleted)
        {
            return new MiPlayNoMediaRtspLiveValidationDecision(false, "Mutual SafetyAuth did not complete, so Cmd_Open bridge behaviour was not tested.");
        }

        if (!snapshot.CmdOpenSentAfterListenerStarted || !snapshot.SafetyDataWrappedCmdOpen)
        {
            return new MiPlayNoMediaRtspLiveValidationDecision(false, "The no-media RTSP listener and SafetyData-wrapped Cmd_Open preconditions were not both satisfied.");
        }

        if (snapshot.SetLocalDeviceInfo0058Sent || snapshot.RtspResponseSent || snapshot.MediaOrRtpSent || snapshot.PlaybackCommandSent)
        {
            return new MiPlayNoMediaRtspLiveValidationDecision(false, "The live validation boundary was exceeded and cannot be used as no-media bridge evidence.");
        }

        if (!snapshot.RtspCallbackObserved)
        {
            return new MiPlayNoMediaRtspLiveValidationDecision(
                false,
                "The S12 accepted mutual SafetyAuth and received one SafetyData-wrapped Cmd_Open whose payload shape is now statically compatible with the mpas parser, then closed the 8899 control connection without a callback to the prepared 192.168.10.9:7236 RTSP listener. This does not verify the Cmd_Open -> OpenMirrorClient bridge; because URL query ordering is ruled out, next evidence should focus on pre-open source identity/device-info/add-mirror/session context or receiver-side rejection before RTSP connection.");
        }

        return new MiPlayNoMediaRtspLiveValidationDecision(
            true,
            "The receiver opened the prepared no-media RTSP endpoint after Cmd_Open.");
    }
}