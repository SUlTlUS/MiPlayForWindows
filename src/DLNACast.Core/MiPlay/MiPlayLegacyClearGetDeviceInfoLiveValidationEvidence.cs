namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyClearGetDeviceInfoLiveValidationSnapshot(
    bool SentNativeVersionBootstrap,
    bool LegacyAuthChallengeObserved,
    bool LegacyAuthAcknowledgementSent,
    bool LegacyReadyStateNotifyObservedBeforeSend,
    bool SentExactlyOneEmptyClearGetDeviceInfo,
    bool LegacyGetDeviceInfoAcknowledgementObserved,
    bool NoModernSafetyInfoOrSafetyAuthSent,
    bool NoSafetyDataSent,
    bool NoSetPlaySourceSent,
    bool NoSetLocalDeviceInfoSent,
    bool NoCmdOpenSent,
    bool NoAddMirrorSent,
    bool NoRtspMediaPlaybackOrAudioSent,
    bool DeviceClosedAfterReadOnlyValidation);

/// <summary>
/// Live, no-media validation against a single LX06/S12 receiver. The probe only
/// exercised the legacy clear-text 8899 control path through Cmd_GetDeviceInfo
/// and records privacy-preserving payload evidence instead of permanent raw IDs.
/// </summary>
public static class MiPlayLegacyClearGetDeviceInfoLiveValidationEvidence
{
    public const string DeviceAddress = "192.168.10.4";
    public const int DevicePort = MiPlayProtocolConstants.DefaultControlPort;
    public const ushort NativeVersionCommand = MiPlayProtocolConstants.NativeSourceVersionCommand;
    public const ushort NativeVersionSequence = 0x0001;
    public const string NativeVersionPayload = MiPlayProtocolConstants.NativeSourceVersion18_0_0_3;
    public const ushort NativeVersionAcknowledgementCommand = MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand;
    public const string ControlSessionVersionAcknowledgement = "2.1.5091615";
    public const ushort LegacyAuthChallengeCommand = MiPlayProtocolConstants.LegacySafetyChallengeCommand;
    public const ushort LegacyAuthAcknowledgementCommand = MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand;
    public const ushort LegacyAuthSequence = 0x01bf;
    public const int LegacyAuthChallengePayloadLength = 16;
    public const int LegacyAuthAcknowledgementPayloadLength = 20;
    public const ushort ModeNotifySequence = 0x01c0;
    public const ushort MediaInfoNotifySequence = 0x01c1;
    public const ushort ReadyStateNotifySequence = 0x01c2;
    public const string ReadyStateNotifyLabel = "state";
    public const int ReadyStateNotifyIntegerValue = 3;
    public const ushort ClearGetDeviceInfoCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
    public const ushort ClearGetDeviceInfoSequence = 0x0002;
    public const int ClearGetDeviceInfoPlaintextPayloadLength = 0;
    public const ushort ClearGetDeviceInfoAcknowledgementCommand = MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand;
    public const ushort ClearGetDeviceInfoAcknowledgementSequence = 0x0002;
    public const int ClearGetDeviceInfoAcknowledgementPayloadLength = 415;
    public const string ClearGetDeviceInfoAcknowledgementPayloadSha256 =
        "BF693DD245AFA365D04BB246032A2A86BF9E28FC3765D3D9C36DB1F3F1E8155F";
    public const string DeviceInfoModel = "LX06";
    public const string DeviceInfoRomVersion = "1.94.13";
    public const string DeviceInfoSupport = "audio";
    public const string DeviceInfoDeviceType = "4";
    public const string DeviceInfoMiName = "\u5c0f\u7231\u97f3\u7bb1Pro";
    public const string RedactedSensitiveFields = "accountId, bluetoothMac, deviceId, house_Id, roomName, room_Id, sn, miotDid";
    public const int ObservedFollowUpFrameCountBeforeClose = 5;

    public static MiPlayLegacyClearGetDeviceInfoLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            SentNativeVersionBootstrap: true,
            LegacyAuthChallengeObserved: true,
            LegacyAuthAcknowledgementSent: true,
            LegacyReadyStateNotifyObservedBeforeSend: true,
            SentExactlyOneEmptyClearGetDeviceInfo: true,
            LegacyGetDeviceInfoAcknowledgementObserved: true,
            NoModernSafetyInfoOrSafetyAuthSent: true,
            NoSafetyDataSent: true,
            NoSetPlaySourceSent: true,
            NoSetLocalDeviceInfoSent: true,
            NoCmdOpenSent: true,
            NoAddMirrorSent: true,
            NoRtspMediaPlaybackOrAudioSent: true,
            DeviceClosedAfterReadOnlyValidation: true);

    public static MiPlayIdmStateDecision EvaluateLiveResult(
        MiPlayLegacyClearGetDeviceInfoLiveValidationSnapshot snapshot)
    {
        if (!snapshot.SentNativeVersionBootstrap ||
            !snapshot.LegacyAuthChallengeObserved ||
            !snapshot.LegacyAuthAcknowledgementSent)
        {
            return new MiPlayIdmStateDecision(false, "The legacy 8899 bootstrap/auth path was not fully exercised.");
        }

        if (!snapshot.LegacyReadyStateNotifyObservedBeforeSend)
        {
            return new MiPlayIdmStateDecision(false, "The read-only getDeviceInfo probe did not wait for decoded state=3 notify.");
        }

        if (!snapshot.SentExactlyOneEmptyClearGetDeviceInfo ||
            !snapshot.LegacyGetDeviceInfoAcknowledgementObserved)
        {
            return new MiPlayIdmStateDecision(false, "The empty clear 0x001e request was not proven by a matching 0x001f response.");
        }

        if (!snapshot.NoModernSafetyInfoOrSafetyAuthSent ||
            !snapshot.NoSafetyDataSent ||
            !snapshot.NoSetPlaySourceSent ||
            !snapshot.NoSetLocalDeviceInfoSent ||
            !snapshot.NoCmdOpenSent ||
            !snapshot.NoAddMirrorSent ||
            !snapshot.NoRtspMediaPlaybackOrAudioSent)
        {
            return new MiPlayIdmStateDecision(false, "The live validation boundary expanded beyond read-only legacy getDeviceInfo.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "Live LX06 1.94.13 accepted legacy 0x0029, emitted decoded state=3, then answered one empty clear-text 0x001e with matching 0x001f sequence 0x0002 and a 415-byte device-info payload containing model=LX06, romVersion=1.94.13, support=audio. This proves the old/basic 8899 read-only command route is usable and moves the next work to offline source-identity/session-context reconstruction; it still does not authorize Cmd_SetPlaySource, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.");
    }
}