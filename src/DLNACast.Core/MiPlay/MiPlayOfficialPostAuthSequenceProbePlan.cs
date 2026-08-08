using System.Text;

namespace DLNACast.Core.MiPlay;

public enum MiPlayOfficialPostAuthSequenceStepKind
{
    SendSourceName = 1,
    SendGetDeviceInfo = 2,
    SendCanAlonePlayCtrl = 3,
    SendAlonePlayCapacity = 4,
    SendGetMirrorMode = 5,
    SendSetPlaySource = 6,
}

public sealed record MiPlayOfficialPostAuthSequenceStep(
    MiPlayOfficialPostAuthSequenceStepKind Kind,
    ushort Command,
    ushort Sequence,
    byte[] PlaintextPayload,
    ushort? ExpectedAcknowledgementCommand,
    ushort? ExpectedAcknowledgementSequence,
    bool AcknowledgementRequiredBeforeSetPlaySource,
    string Boundary);

public sealed record MiPlayOfficialPostAuthSequencePrerequisites(
    bool MutualSafetyAuthVerified,
    bool NativeNoResetOutboundProfileAvailable,
    bool OfficialPlaintextRecoveredFromRootPcap,
    bool FreshSessionCommandOrderCaptured,
    bool SafetyDataIntegrityEndianAlignedWithNative,
    bool LocalDeviceInfoPayloadsAvailable,
    bool GetDeviceInfoAcknowledgementParserAvailable,
    bool GetMirrorModePairLocalized,
    bool StopOnUnexpectedFrameOrClose,
    bool ForbidCmdOpen,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidMediaPlaybackOrAudio,
    bool FreshUserAuthorizationPresent,
    ushort FirstCommandSequence);

public sealed record MiPlayOfficialPostAuthSequenceDecision(
    bool CanPreparePlan,
    bool CanSendNow,
    bool SafeForNetworkUse,
    string Reason,
    IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> Steps);

/// <summary>
/// Offline plan for the smallest official-runtime-shaped post-auth command
/// sequence recovered from the rooted phone pcap. It prepares bytes and gates;
/// it does not send anything and defaults to not network-safe.
/// </summary>
public static class MiPlayOfficialPostAuthSequenceProbePlan
{
    public const string DefaultSourceName = MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceName;
    public const string DefaultBluetoothMacHash =
        MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash;
    public const string Boundary =
        "The recovered 0x0058 -> 0x001e -> 0x0034 -> 0x0040 order came from an already-authenticated mid-session capture (starting at sequence 0x013a), not the first command after DealSafetyDone; keep it offline until a fresh-session command order is captured; stop before Open/AddMirror/RTSP/media/playback/audio";

    public static IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> CreateSteps(
        ushort firstCommandSequence) =>
        CreateSteps(
            firstCommandSequence,
            DefaultSourceName,
            bluetoothMacHash: DefaultBluetoothMacHash);

    public static IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> CreateSteps(
        ushort firstCommandSequence,
        string sourceName,
        string? bluetoothMac = null,
        string? bluetoothMacHash = null,
        string canAlonePlayCtrl = "1",
        string alonePlayCapacity = "1")
    {
        if (firstCommandSequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstCommandSequence), "The first post-auth command sequence must be initialized.");
        }

        return
        [
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendSourceName,
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                AddSequence(firstCommandSequence, 0),
                bluetoothMacHash is null
                    ? MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
                        sourceName,
                        bluetoothMac,
                        includeControlFields: false)
                    : MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceNameWithBluetoothMacHash(
                        sourceName,
                        bluetoothMacHash,
                        includeControlFields: false),
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                AddSequence(firstCommandSequence, 0),
                AcknowledgementRequiredBeforeSetPlaySource: false,
                Boundary: "The already-authenticated official pcap window starts with sourceName/mSourceBtMac local context at sequence 0x013a. Its plaintext and 105-byte SafetyData length are recovered, but it is not proven to be the fresh DealSafetyDone successor."),
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo,
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                AddSequence(firstCommandSequence, 1),
                [],
                MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                AddSequence(firstCommandSequence, 1),
                AcknowledgementRequiredBeforeSetPlaySource: true,
                Boundary: "Official pcap sends empty 0x001e and receives same-sequence 0x001f before SetPlaySource."),
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendCanAlonePlayCtrl,
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                AddSequence(firstCommandSequence, 2),
                MiPlayLocalDeviceInfoPayloadCodec.EncodeCanAlonePlayCtrl(canAlonePlayCtrl),
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                AddSequence(firstCommandSequence, 2),
                AcknowledgementRequiredBeforeSetPlaySource: false,
                Boundary: "Official pcap repeats a single-field canAlonePlayCtrl=1 local-device-info update."),
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendAlonePlayCapacity,
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                AddSequence(firstCommandSequence, 3),
                MiPlayLocalDeviceInfoPayloadCodec.EncodeAlonePlayCapacity(alonePlayCapacity),
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                AddSequence(firstCommandSequence, 3),
                AcknowledgementRequiredBeforeSetPlaySource: false,
                Boundary: "Official pcap repeats a single-field alonePlayCapacity=1 local-device-info update."),
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode,
                MiPlayProtocolConstants.GetMirrorModeCommand,
                AddSequence(firstCommandSequence, 4),
                [],
                MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
                AddSequence(firstCommandSequence, 4),
                AcknowledgementRequiredBeforeSetPlaySource: true,
                Boundary: "Official pcap sends empty 0x0034 and receives same-sequence 0x0035 valueType=0/mirrorMode=2 before SetPlaySource."),
            new(
                MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource,
                MiPlayProtocolConstants.SetPlaySourceCommand,
                AddSequence(firstCommandSequence, 5),
                MiPlaySetPlaySourcePayloadCodec.EncodeRecoveredOfficialRuntimePayload(),
                MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand,
                AddSequence(firstCommandSequence, 5),
                AcknowledgementRequiredBeforeSetPlaySource: false,
                Boundary: "Official pcap sends recovered runtime 0x0040 JSON after source context, device info, and mirror-mode readiness; no immediate 0x0041 was captured, so absence of 0x0041 alone must not trigger retries or later media/open commands."),
        ];
    }

    public static IReadOnlyList<byte[]> CreateSafetyDataCommandFrames(
        IEnumerable<MiPlayOfficialPostAuthSequenceStep> steps,
        MiPlaySafetyDataSessionCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(cipher);

        var frames = new List<byte[]>();
        foreach (var step in steps)
        {
            frames.Add(MiPlayCommandFrameCodec.Encode(
                step.Command,
                step.Sequence,
                cipher.EncryptVersion1(step.PlaintextPayload)));
        }

        return frames;
    }

    public static MiPlayOfficialPostAuthSequenceDecision Evaluate(
        MiPlayOfficialPostAuthSequencePrerequisites prerequisites,
        string? sourceName = null,
        string? bluetoothMac = null,
        string? bluetoothMacHash = null)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return Reject("Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.NativeNoResetOutboundProfileAvailable)
        {
            return Reject("The native no-reset outbound SafetyData profile is not available.");
        }

        if (!prerequisites.OfficialPlaintextRecoveredFromRootPcap)
        {
            return Reject("The official post-auth plaintext sequence has not been recovered from the rooted phone pcap.");
        }

        var steps = sourceName is null && bluetoothMac is null && bluetoothMacHash is null
            ? CreateSteps(prerequisites.FirstCommandSequence)
            : CreateSteps(
                prerequisites.FirstCommandSequence,
                sourceName ?? DefaultSourceName,
                bluetoothMac,
                bluetoothMacHash);

        if (!prerequisites.FreshSessionCommandOrderCaptured)
        {
            return new MiPlayOfficialPostAuthSequenceDecision(
                CanPreparePlan: true,
                CanSendNow: false,
                SafeForNetworkUse: false,
                Reason: "Prepared for offline comparison only: the rooted pcap starts mid-session at sequence 0x013a and does not prove that 0x0058 is the first command after fresh DealSafetyDone. Capture the first official phone frame on a new mutually authenticated session before enabling this order.",
                Steps: steps);
        }

        if (!prerequisites.SafetyDataIntegrityEndianAlignedWithNative)
        {
            return Reject("SafetyData integrity byte order is not aligned with the current native implementation.");
        }

        if (!prerequisites.LocalDeviceInfoPayloadsAvailable)
        {
            return Reject("The official 0x0058 local-device-info payload shapes are not available.");
        }

        if (!prerequisites.GetDeviceInfoAcknowledgementParserAvailable)
        {
            return Reject("The 0x001f device-info acknowledgement parser is not available.");
        }

        if (!prerequisites.GetMirrorModePairLocalized)
        {
            return Reject("The 0x0034/0x0035 GetMirrorMode pair is not localized.");
        }

        if (!prerequisites.StopOnUnexpectedFrameOrClose)
        {
            return Reject("The plan must stop on any unexpected frame or close.");
        }

        if (!prerequisites.ForbidCmdOpen ||
            !prerequisites.ForbidAddMirror ||
            !prerequisites.ForbidRtsp ||
            !prerequisites.ForbidMediaPlaybackOrAudio)
        {
            return Reject("Open, AddMirror, RTSP, media, playback, and audio must remain forbidden.");
        }

        if (!prerequisites.FreshUserAuthorizationPresent)
        {
            return new MiPlayOfficialPostAuthSequenceDecision(
                CanPreparePlan: true,
                CanSendNow: false,
                SafeForNetworkUse: false,
                Reason: "Prepared offline only: a fresh explicit authorization is still required before sending this S12 command sequence.",
                Steps: steps);
        }

        if (!steps[0].PlaintextPayload.SequenceEqual(MiPlayLocalDeviceInfoPayloadCodec.EncodeRecoveredOfficialSourceIdentity()))
        {
            return new MiPlayOfficialPostAuthSequenceDecision(
                CanPreparePlan: true,
                CanSendNow: false,
                SafeForNetworkUse: false,
                Reason: "Prepared offline only: the first 0x0058 source identity does not match the recovered official phone plaintext/length, so it must not be sent as the official sequence.",
                Steps: steps);
        }

        return new MiPlayOfficialPostAuthSequenceDecision(
            CanPreparePlan: true,
            CanSendNow: true,
            SafeForNetworkUse: true,
            Reason: "Ready only under the fresh authorization boundary: send the prepared official-runtime-shaped sequence, require 0x001f and 0x0035 before 0x0040, then stop without Open/AddMirror/RTSP/media/playback/audio.",
            Steps: steps);

        static MiPlayOfficialPostAuthSequenceDecision Reject(string reason) =>
            new(false, false, false, reason, []);
    }

    public static string DecodePlaintextUtf8(MiPlayOfficialPostAuthSequenceStep step) =>
        Encoding.UTF8.GetString(step.PlaintextPayload);

    private static ushort AddSequence(ushort sequence, ushort offset)
    {
        var value = sequence + offset;
        if (value > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "The official post-auth sequence would overflow the 16-bit command sequence.");
        }

        return (ushort)value;
    }
}
