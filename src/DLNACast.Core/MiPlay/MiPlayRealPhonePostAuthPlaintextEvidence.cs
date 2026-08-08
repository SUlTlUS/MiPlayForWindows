namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRealPhonePostAuthPlaintextSnapshot(
    string CapturePath,
    string PhoneEndpoint,
    string SpeakerEndpoint,
    string AuthKeyType1,
    string AesKeyMaterialType1,
    bool CaptureStartsAfterSafetyAuth,
    bool ContinuationCbcDecryptsAfterFirstFramePerDirection,
    string RecoveredOfficialSourceName,
    string RecoveredOfficialSourceBluetoothMacHash,
    string FirstSetLocalDeviceInfoKnownFirstBlock,
    string FirstSetLocalDeviceInfoKnownSuffix,
    string FirstSetLocalDeviceInfoJson,
    int FirstSetLocalDeviceInfoPlaintextLength,
    int FirstSetLocalDeviceInfoSafetyDataPayloadLength,
    int FirstSetLocalDeviceInfoPaddingLength,
    string SetLocalCanAlonePlayCtrlJson,
    string SetLocalAlonePlayCapacityJson,
    string GetMirrorModeAcknowledgementHex,
    string OfficialSetPlaySourceJson,
    bool SafeForNetworkReplay);

/// <summary>
/// Plaintext recovered offline from the already captured official
/// <c>com.milink.service:audio</c> phone-to-S12 post-auth command session.
/// It records only decrypted payload evidence and does not authorize replay.
/// </summary>
public static class MiPlayRealPhonePostAuthPlaintextEvidence
{
    public const string CapturePath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap";

    public const string PhoneEndpoint = "192.168.10.20:43720";
    public const string SpeakerEndpoint = "192.168.10.7:8899";
    public const string AuthKeyType1 = "a565e5251cce7d9995e34b18bb656c33";
    public const string AesKeyMaterialType1 = "a565e5251cce7d99";

    public const string CbcContinuationEvidence =
        "The pcap starts after SafetyAuth, so the first captured frame for each direction has an unknown first plaintext block. Once one frame in a direction is captured, its last ciphertext block is the CBC IV for that direction's next frame, allowing full plaintext recovery for later frames without sending any packet.";

    public const string RecoveredOfficialSourceName = "Xiaomi 13 Pro";
    public const string RecoveredOfficialSourceBluetoothMacHash = "8F649963F64A7F7AAE3A8CD4DE9AE346";
    public const string FirstSetLocalDeviceInfoKnownFirstBlock = "{\"sourceName\":\"X";
    public const string FirstSetLocalDeviceInfoKnownSuffix =
        "iaomi 13 Pro\",\"mSourceBtMac\":\"8F649963F64A7F7AAE3A8CD4DE9AE346\"}";
    public const string FirstSetLocalDeviceInfoJson =
        "{\"sourceName\":\"Xiaomi 13 Pro\",\"mSourceBtMac\":\"8F649963F64A7F7AAE3A8CD4DE9AE346\"}";
    public const int FirstSetLocalDeviceInfoPlaintextLength = 80;
    public const int FirstSetLocalDeviceInfoSafetyDataPayloadLength = 105;
    public const int FirstSetLocalDeviceInfoCiphertextLength = 96;
    public const int FirstSetLocalDeviceInfoPaddingLength = 16;
    public const int SafetyDataVersion1HeaderLength = 9;

    public const string SetLocalCanAlonePlayCtrlJson =
        "{\"canAlonePlayCtrl\":\"1\"}";

    public const string SetLocalAlonePlayCapacityJson =
        "{\"alonePlayCapacity\":\"1\"}";

    public const string GetMirrorModeAcknowledgementHex = "0000000002";

    public const string OfficialSetPlaySourceJson =
        "{\"ref_channel\":\"controlcenter\",\"ref_function\":\"single_room\",\"ref_content\":\"music_qq\"}";

    public const string OfficialSetPlaySourceRefChannel = "controlcenter";
    public const string OfficialSetPlaySourceRefFunction = "single_room";
    public const string OfficialSetPlaySourceRefContent = "music_qq";

    public static MiPlayRealPhonePostAuthPlaintextSnapshot CreateSnapshot() =>
        new(
            CapturePath,
            PhoneEndpoint,
            SpeakerEndpoint,
            AuthKeyType1,
            AesKeyMaterialType1,
            CaptureStartsAfterSafetyAuth: true,
            ContinuationCbcDecryptsAfterFirstFramePerDirection: true,
            RecoveredOfficialSourceName,
            RecoveredOfficialSourceBluetoothMacHash,
            FirstSetLocalDeviceInfoKnownFirstBlock,
            FirstSetLocalDeviceInfoKnownSuffix,
            FirstSetLocalDeviceInfoJson,
            FirstSetLocalDeviceInfoPlaintextLength,
            FirstSetLocalDeviceInfoSafetyDataPayloadLength,
            FirstSetLocalDeviceInfoPaddingLength,
            SetLocalCanAlonePlayCtrlJson,
            SetLocalAlonePlayCapacityJson,
            GetMirrorModeAcknowledgementHex,
            OfficialSetPlaySourceJson,
            SafeForNetworkReplay: false);

    public static MiPlayIdmStateDecision Evaluate(MiPlayRealPhonePostAuthPlaintextSnapshot snapshot)
    {
        if (snapshot.AuthKeyType1 != AuthKeyType1 ||
            snapshot.AesKeyMaterialType1 != AesKeyMaterialType1 ||
            !snapshot.ContinuationCbcDecryptsAfterFirstFramePerDirection)
        {
            return new MiPlayIdmStateDecision(false, "The offline continuation-decrypt preconditions do not match the rooted phone capture.");
        }

        if (snapshot.SafeForNetworkReplay)
        {
            return new MiPlayIdmStateDecision(false, "Recovered official plaintext must not be treated as replay permission.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The rooted phone pcap can be decrypted offline after the first captured frame per direction. It recovers official post-auth 0x0058 source-context JSON, a 0x0035 GetMirrorMode_Ack value of 2, and the official 0x0040 SetPlaySource JSON with controlcenter/single_room/music_qq. This is plaintext evidence only and is not permission to replay 0x0058, 0x0040, Open, RTSP, media, playback, or audio.");
    }
}
