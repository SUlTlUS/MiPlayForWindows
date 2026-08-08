using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyDeviceInfoLiveValidationSnapshot(
    bool PhoneConnectedToDistinctReceiver,
    bool SentExactlyOneLegacyChallenge,
    bool ObservedNativeSourceVersion,
    bool ObservedEmptyGetDeviceInfo,
    bool ObservedInitialSetLocalDeviceInfo,
    bool VerifiedLegacyAuthenticationAcknowledgement,
    bool SentExactlyOneSameSequenceDeviceInfoAcknowledgement,
    bool ObservedAdvancedSetLocalDeviceInfoAfterAcknowledgement,
    bool NoOtherOutboundFrames,
    bool StoppedImmediatelyAfterPositiveObservation);

/// <summary>
/// Evidence from the explicitly authorized 2026-08-07 fresh legacy receiver
/// validation. It records only command metadata and hashes; sender payload bytes
/// and permanent device identifiers were not logged.
/// </summary>
public static class MiPlayFreshLegacyDeviceInfoLiveValidationEvidence
{
    public const string ReceiverAddress = "192.168.10.9";
    public const int ReceiverPort = MiPlayProtocolConstants.DefaultControlPort;
    public const string SourceAddress = "192.168.10.58";
    public const int SourcePort = 50_538;
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-device-info-20260807-0211.stdout.log";
    public const string PhoneFirmwareDexArtifact =
        "artifacts/phone_firmware/mi13p_os3_0_313/apk_extract/MiLinkOS3Cn/classes3.dex";
    public const int SetLocalDeviceInfoSameAccountMethodAddress = 0x2b76c0;
    public const int IsSameAccountToJsonMethodAddress = 0x26ee20;

    public const ushort LegacyChallengeCommand = MiPlayProtocolConstants.LegacySafetyChallengeCommand;
    public const ushort LegacyChallengeSequence = 0x0000;
    public const int LegacyChallengePayloadLength = 9;

    public const ushort NativeSourceVersionCommand = MiPlayProtocolConstants.NativeSourceVersionCommand;
    public const ushort NativeSourceVersionSequence = 0x0000;
    public const int NativeSourceVersionPayloadLength = 12;
    public const string NativeSourceVersionFrameSha256 =
        "558EBE495951AD7B8929C4E3AFE9D58926D8E963961374A12A3BB5EEBC1646B0";

    public const ushort GetDeviceInfoCommand = MiPlayProtocolConstants.GetDeviceInfoCommand;
    public const ushort GetDeviceInfoSequence = 0x0001;
    public const int GetDeviceInfoPayloadLength = 0;
    public const string GetDeviceInfoFrameSha256 =
        "203B2D81F6878C606F65693571D9EE10DDA64C08ADE9EDF29D649EB17E482B03";

    public const ushort InitialSetLocalDeviceInfoCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
    public const ushort InitialSetLocalDeviceInfoSequence = 0x0002;
    public const int InitialSetLocalDeviceInfoPayloadLength = 31;
    public const string InitialSetLocalDeviceInfoFrameSha256 =
        "1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113";

    public const ushort LegacyAcknowledgementCommand = MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand;
    public const ushort LegacyAcknowledgementSequence = 0x0000;
    public const int LegacyAcknowledgementPayloadLength = 40;
    public const string LegacyAcknowledgementFrameSha256 =
        "AF8BF73F0315FD5BE81E05980E8AEFC266CCD56521E451DD8BAC45BC03F5B517";

    public const ushort DeviceInfoAcknowledgementCommand = MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand;
    public const ushort DeviceInfoAcknowledgementSequence = GetDeviceInfoSequence;
    public const int DeviceInfoAcknowledgementPayloadLength = 377;
    public const int DeviceInfoAcknowledgementFrameLength = 386;
    public const string DeviceInfoAcknowledgementFrameSha256 =
        "C344E8224C2ED699EE4F0EFDBE407821223C34C23D4027F8FAEA131517DD9FB3";

    public const ushort AdvancedSetLocalDeviceInfoCommand = MiPlayProtocolConstants.SetLocalDeviceInfoCommand;
    public const ushort AdvancedSetLocalDeviceInfoSequence = 0x0003;
    public const int AdvancedSetLocalDeviceInfoPayloadLength = 19;
    public const int AdvancedSetLocalDeviceInfoIsSameAccount = 0;
    public const string AdvancedSetLocalDeviceInfoJson = "{\"isSameAccount\":0}";
    public const string AdvancedSetLocalDeviceInfoFrameSha256 =
        "DB75703B2F77B6BA8A63D0611104DA6DE1266A144B00D985B905B28CC9A23FC6";

    public static byte[] ReconstructAdvancedSetLocalDeviceInfoFrame() =>
        MiPlayCommandFrameCodec.Encode(
            AdvancedSetLocalDeviceInfoCommand,
            AdvancedSetLocalDeviceInfoSequence,
            MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(
                AdvancedSetLocalDeviceInfoIsSameAccount));

    public static byte[] ReconstructInitialSetLocalDeviceInfoFrame() =>
        MiPlayCommandFrameCodec.Encode(
            InitialSetLocalDeviceInfoCommand,
            InitialSetLocalDeviceInfoSequence,
            Encoding.UTF8.GetBytes(MiPlayFreshLegacySenderCaptureEvidence.SetLocalDeviceInfoJson));

    public static MiPlayFreshLegacyDeviceInfoLiveValidationSnapshot CreateCurrentSnapshot() =>
        new(
            PhoneConnectedToDistinctReceiver: true,
            SentExactlyOneLegacyChallenge: true,
            ObservedNativeSourceVersion: true,
            ObservedEmptyGetDeviceInfo: true,
            ObservedInitialSetLocalDeviceInfo: true,
            VerifiedLegacyAuthenticationAcknowledgement: true,
            SentExactlyOneSameSequenceDeviceInfoAcknowledgement: true,
            ObservedAdvancedSetLocalDeviceInfoAfterAcknowledgement: true,
            NoOtherOutboundFrames: true,
            StoppedImmediatelyAfterPositiveObservation: true);

    public static MiPlayIdmStateDecision EvaluateLiveResult(
        MiPlayFreshLegacyDeviceInfoLiveValidationSnapshot snapshot)
    {
        var reconstructedAdvancedFrame = ReconstructAdvancedSetLocalDeviceInfoFrame();
        if (reconstructedAdvancedFrame.Length != MiPlayProtocolConstants.CommandHeaderLength + AdvancedSetLocalDeviceInfoPayloadLength ||
            !string.Equals(
                Convert.ToHexString(SHA256.HashData(reconstructedAdvancedFrame)),
                AdvancedSetLocalDeviceInfoFrameSha256,
                StringComparison.Ordinal))
        {
            return new MiPlayIdmStateDecision(
                false,
                "The statically recovered isSameAccount payload no longer reproduces the observed advanced 0x0058 frame hash.");
        }

        if (!snapshot.PhoneConnectedToDistinctReceiver ||
            !snapshot.SentExactlyOneLegacyChallenge ||
            !snapshot.ObservedNativeSourceVersion ||
            !snapshot.ObservedEmptyGetDeviceInfo ||
            !snapshot.ObservedInitialSetLocalDeviceInfo ||
            !snapshot.VerifiedLegacyAuthenticationAcknowledgement)
        {
            return new MiPlayIdmStateDecision(
                false,
                "The fresh legacy source bootstrap and authentication observations are incomplete.");
        }

        if (!snapshot.SentExactlyOneSameSequenceDeviceInfoAcknowledgement ||
            !snapshot.ObservedAdvancedSetLocalDeviceInfoAfterAcknowledgement)
        {
            return new MiPlayIdmStateDecision(
                false,
                "The same-sequence 0x001f was not followed by the source's advanced 0x0058 sequence.");
        }

        if (!snapshot.NoOtherOutboundFrames ||
            !snapshot.StoppedImmediatelyAfterPositiveObservation)
        {
            return new MiPlayIdmStateDecision(
                false,
                "The live run expanded beyond the authorized one-frame receiver validation boundary.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The official com.milink.service 12.4.8.13 source accepted the deterministic 20-field, same-sequence clear 0x001f from the distinct receiver: after its initial 0x0058 sequence 0x0002, it emitted 0x0058 sequence 0x0003 with the byte-exact payload {\"isSameAccount\":0}. This is wire-level positive evidence for source onDeviceInfo progression and makes the fresh legacy device-info bootstrap usable. It does not prove mirror/open/media readiness and does not authorize 0x0059, Open, AddMirror, RTSP, playback, media, or audio frames.");
    }
}
