using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public enum MiPlayLegacyStatusQueryOrder
{
    VolumeMediaInfoState,
    VolumeStateMediaInfo,
}

public enum MiPlayLegacyAudioSourcePhase
{
    Created,
    AwaitingNativeVersionAcknowledgement,
    AwaitingInitialDeviceInfoAcknowledgements,
    AwaitingAccountAndMirrorAcknowledgements,
    BasicBootstrapComplete,
    Stopped,
}

public sealed record MiPlayLegacyAudioSourceWrite(IReadOnlyList<byte[]> Frames)
{
    public byte[] ToArray()
    {
        var length = Frames.Sum(frame => frame.Length);
        var payload = new byte[length];
        var offset = 0;
        foreach (var frame in Frames)
        {
            frame.CopyTo(payload, offset);
            offset += frame.Length;
        }

        return payload;
    }
}

public sealed record MiPlayLegacyAudioSourceTransition(
    bool Accepted,
    MiPlayLegacyAudioSourcePhase Phase,
    ushort ObservedCommand,
    ushort ObservedSequence,
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> OutboundWrites,
    bool Completed,
    bool SafeForNetworkUse,
    string Boundary);

internal sealed record MiPlayLegacyAudioSourceProgress(
    MiPlayLegacyAudioSourcePhase Phase,
    bool DeviceInfoAcknowledged,
    bool SourceNameAcknowledged,
    bool AccountAcknowledged,
    bool MirrorModeAcknowledged,
    bool StatusQueriesPrepared,
    bool VolumeAcknowledged,
    bool StateAcknowledged,
    bool MediaInfoObserved);

/// <summary>
/// Pure, offline reconstruction of the basic legacy-clear source bootstrap
/// captured from com.milink.service 12.4.8.13 against two real LX06 receivers.
/// It ends after the source-name/account/device-info/mirror-mode acknowledgements
/// and never prepares Open, AddMirror, RTSP, playback, media, or audio traffic.
/// </summary>
public sealed class MiPlayLegacyAudioSourceSession
{
    public const ushort NativeVersionSequence = 0;
    public const ushort GetDeviceInfoSequence = 1;
    public const ushort SourceNameSequence = 2;
    public const ushort IsSameAccountSequence = 3;
    public const ushort GetMirrorModeSequence = 4;
    public const int MinimumObservedLegacyChallengeLength = 12;
    public const int MaximumObservedLegacyChallengeLength = 17;
    public const byte LiveObservedMirrorMode = 1;
    public const byte CapturedMirrorMode = 2;

    private readonly string sourceName;
    private bool deviceInfoAcknowledged;
    private bool sourceNameAcknowledged;
    private bool accountAcknowledged;
    private bool mirrorModeAcknowledged;
    private bool statusQueriesPrepared;
    private bool volumeAcknowledged;
    private uint? currentVolume;
    private bool stateAcknowledged;
    private bool mediaInfoObserved;
    private readonly MiPlayLegacyStatusQueryOrder statusQueryOrder;
    private MiPlayLegacyAudioSourcePhase phase = MiPlayLegacyAudioSourcePhase.Created;

    public MiPlayLegacyAudioSourceSession(
        string sourceName,
        MiPlayLegacyStatusQueryOrder statusQueryOrder = MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            throw new ArgumentException("Source name must not be null or empty.", nameof(sourceName));
        }

        this.sourceName = sourceName;
        this.statusQueryOrder = statusQueryOrder;
    }

    public MiPlayLegacyAudioSourcePhase Phase => phase;
    public uint? CurrentVolume => currentVolume;

    internal MiPlayLegacyAudioSourceProgress Progress => new(
        phase,
        deviceInfoAcknowledged,
        sourceNameAcknowledged,
        accountAcknowledged,
        mirrorModeAcknowledged,
        statusQueriesPrepared,
        volumeAcknowledged,
        stateAcknowledged,
        mediaInfoObserved);

    public static MiPlayLegacyAudioSourceSession CreateCapturedMiPadComparisonSession() =>
        new("MI PAD 4/Plus", MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState);

    public MiPlayLegacyAudioSourceTransition ProcessInboundFrame(ReadOnlySpan<byte> frameBytes)
    {
        if (phase is MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete or MiPlayLegacyAudioSourcePhase.Stopped)
        {
            return Reject(0, 0, "The legacy source bootstrap has already completed or stopped at its no-media boundary.");
        }

        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed) ||
            frame is null ||
            bytesConsumed != frameBytes.Length)
        {
            return Stop(0, 0, "Inbound bytes are not exactly one complete MiPlay command frame.");
        }

        if (frame.Command is MiPlayProtocolConstants.SafetyInfoCommand or
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand)
        {
            return Stop(frame.Command, frame.Sequence, "A modern SafetyInfo/SafetyAuth frame appeared on the captured legacy-clear branch.");
        }

        if (frame.Command == MiPlayProtocolConstants.NotifyCommand &&
            phase != MiPlayLegacyAudioSourcePhase.Created)
        {
            if (phase == MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements &&
                statusQueriesPrepared &&
                frame.Payload.Length == 158 &&
                string.Equals(
                    Convert.ToHexString(SHA256.HashData(frame.Payload)),
                    MiPlayLegacySourceStatusQueryEvidence.MediaInfoNotificationPayloadSha256Hex,
                    StringComparison.Ordinal))
            {
                mediaInfoObserved = true;
                return CompleteStatusInitializationIfReady(
                    frame,
                    [],
                    "Accepted the captured mode-2 158-byte 0x0022 media-info notification.");
            }

            return Accept(frame, [], completed: false, "Observed an interleaved receiver notification; no source frame is prepared.");
        }

        return phase switch
        {
            MiPlayLegacyAudioSourcePhase.Created => ProcessChallenge(frame),
            MiPlayLegacyAudioSourcePhase.AwaitingNativeVersionAcknowledgement => ProcessNativeVersionAcknowledgement(frameBytes, frame),
            MiPlayLegacyAudioSourcePhase.AwaitingInitialDeviceInfoAcknowledgements => ProcessInitialDeviceInfoAcknowledgement(frame),
            MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements => ProcessAccountAndMirrorAcknowledgement(frame),
            _ => Stop(frame.Command, frame.Sequence, "Unsupported legacy source phase."),
        };
    }

    private MiPlayLegacyAudioSourceTransition ProcessChallenge(MiPlayCommandFrame frame)
    {
        if (frame.Command != MiPlayProtocolConstants.LegacySafetyChallengeCommand ||
            frame.Payload.Length is < MinimumObservedLegacyChallengeLength or > MaximumObservedLegacyChallengeLength ||
            frame.Payload.Any(value => value is < (byte)'0' or > (byte)'9'))
        {
            return Stop(
                frame.Command,
                frame.Sequence,
                $"The fresh source session must begin with one 12- to 17-digit clear 0x0028 challenge, " +
                $"matching captured and live-observed receivers. Observed command 0x{frame.Command:X4}, " +
                $"sequence {frame.Sequence}, payload length {frame.Payload.Length}.");
        }

        var version = MiPlayNativeVersionCodec.EncodeSourceVersion(
            NativeVersionSequence,
            MiPlayProtocolConstants.NativeSourceVersion12_4_8_13);
        var acknowledgement = MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(
            MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(frame.Sequence, frame.Payload));
        var getDeviceInfo = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            GetDeviceInfoSequence,
            []);

        phase = MiPlayLegacyAudioSourcePhase.AwaitingNativeVersionAcknowledgement;
        return Accept(
            frame,
            [
                new MiPlayLegacyAudioSourceWrite([version, acknowledgement]),
                new MiPlayLegacyAudioSourceWrite([getDeviceInfo]),
            ],
            completed: false,
            "Reconstructed the captured two-write prefix: coalesced 0x0036+0x0029, then empty 0x001e. Awaiting same-sequence 0x0037 before source identity.");
    }

    private MiPlayLegacyAudioSourceTransition ProcessNativeVersionAcknowledgement(
        ReadOnlySpan<byte> frameBytes,
        MiPlayCommandFrame frame)
    {
        if (!MiPlayNativeVersionCodec.TryDecodeAcknowledgement(frameBytes, out var sequence, out _) ||
            sequence != NativeVersionSequence)
        {
            return Stop(
                frame.Command,
                frame.Sequence,
                "Expected one parseable native-version 0x0037 with sequence 0 before source identity.");
        }

        var sourceIdentity = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            SourceNameSequence,
            MiPlayLocalDeviceInfoPayloadCodec.EncodeLegacySourceNameOnly(sourceName));
        phase = MiPlayLegacyAudioSourcePhase.AwaitingInitialDeviceInfoAcknowledgements;
        return Accept(
            frame,
            [new MiPlayLegacyAudioSourceWrite([sourceIdentity])],
            completed: false,
            "Accepted 0x0037 sequence 0 and reproduced the legacy sourceName-only 0x0058 sequence 2 shape.");
    }

    private MiPlayLegacyAudioSourceTransition ProcessInitialDeviceInfoAcknowledgement(MiPlayCommandFrame frame)
    {
        if (frame.Command == MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
        {
            if (deviceInfoAcknowledged ||
                frame.Sequence != GetDeviceInfoSequence ||
                !MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(frame.Payload, out _, out var bytesConsumed) ||
                bytesConsumed != frame.Payload.Length)
            {
                return Stop(frame.Command, frame.Sequence, "0x001f must be one parseable, same-sequence device-info payload and may occur only once.");
            }

            deviceInfoAcknowledged = true;
        }
        else if (frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
        {
            if (sourceNameAcknowledged || frame.Sequence != SourceNameSequence || frame.Payload.Length != 0)
            {
                return Stop(frame.Command, frame.Sequence, "The sourceName 0x0059 must be empty, sequence 2, and occur only once.");
            }

            sourceNameAcknowledged = true;
        }
        else
        {
            return Stop(frame.Command, frame.Sequence, "Expected the 0x001f sequence 1 and empty 0x0059 sequence 2 pair, allowing only 0x0022 interleaving.");
        }

        if (!deviceInfoAcknowledged || !sourceNameAcknowledged)
        {
            return Accept(frame, [], completed: false, "Accepted one half of the initial device-info acknowledgement pair; waiting for the other half.");
        }

        var isSameAccount = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            IsSameAccountSequence,
            MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0));
        var getMirrorMode = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeCommand,
            GetMirrorModeSequence,
            []);
        phase = MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements;
        return Accept(
            frame,
            [
                new MiPlayLegacyAudioSourceWrite([isSameAccount]),
                new MiPlayLegacyAudioSourceWrite([getMirrorMode]),
            ],
            completed: false,
            "Both initial acknowledgements are verified; reproduced 0x0058 sequence 3 isSameAccount=0 followed by empty 0x0034 sequence 4.");
    }

    private MiPlayLegacyAudioSourceTransition ProcessAccountAndMirrorAcknowledgement(MiPlayCommandFrame frame)
    {
        if (frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
        {
            if (accountAcknowledged || frame.Sequence != IsSameAccountSequence || frame.Payload.Length != 0)
            {
                return Stop(frame.Command, frame.Sequence, "The account 0x0059 must be empty, sequence 3, and occur only once.");
            }

            accountAcknowledged = true;

            if (!statusQueriesPrepared)
            {
                statusQueriesPrepared = true;
                var statusWrites = CreateStatusQueryWrites();
                return CompleteStatusInitializationIfReady(
                    frame,
                    statusWrites,
                    "Accepted empty 0x0059 sequence 3 and reproduced the target receiver's empty status-query prefix starting at sequence 5.");
            }
        }
        else if (frame.Command == MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand)
        {
            var mirrorModeDecoded = MiPlayLegacyStatusScalarCodec.TryDecode(
                frame.Payload,
                out var mirrorMode);
            if (mirrorModeAcknowledged ||
                frame.Sequence != GetMirrorModeSequence ||
                !mirrorModeDecoded ||
                !IsObservedMirrorMode(mirrorMode))
            {
                return Stop(
                    frame.Command,
                    frame.Sequence,
                    $"The mirror-mode 0x0035 must be sequence 4 with an observed five-byte mode-1 or mode-2 payload " +
                    $"and occur only once. Observed payload length {frame.Payload.Length}, decoded mode {mirrorMode}.");
            }

            mirrorModeAcknowledged = true;
        }
        else if (frame.Command == MiPlayProtocolConstants.GetVolumeAcknowledgementCommand)
        {
            if (!statusQueriesPrepared ||
                volumeAcknowledged ||
                frame.Sequence != 5 ||
                !MiPlayLegacyStatusScalarCodec.TryDecode(frame.Payload, out var volume) ||
                volume > 100)
            {
                return Stop(frame.Command, frame.Sequence, "The volume 0x000f must be a same-sequence five-byte scalar in range 0..100 and occur only once.");
            }

            volumeAcknowledged = true;
            currentVolume = volume;
        }
        else if (frame.Command == MiPlayProtocolConstants.GetStateAcknowledgementCommand)
        {
            if (!statusQueriesPrepared ||
                stateAcknowledged ||
                frame.Sequence != GetStateSequence ||
                !MiPlayLegacyStatusScalarCodec.TryDecode(frame.Payload, out _))
            {
                return Stop(frame.Command, frame.Sequence, "The state 0x001d must be a same-sequence five-byte scalar and occur only once.");
            }

            stateAcknowledged = true;
        }
        else if (frame.Command == MiPlayProtocolConstants.GetMediaInfoAcknowledgementCommand)
        {
            if (!statusQueriesPrepared || mediaInfoObserved || frame.Sequence != GetMediaInfoSequence)
            {
                return Stop(frame.Command, frame.Sequence, "The optional normal 0x0015 media-info response must preserve the query sequence and occur only once.");
            }

            mediaInfoObserved = true;
        }
        else
        {
            return Stop(frame.Command, frame.Sequence, "Expected account, mirror-mode, volume, state, or media-info status responses, allowing 0x0022 interleaving.");
        }

        return CompleteStatusInitializationIfReady(
            frame,
            [],
            "Accepted one status-initialization response; waiting for the remaining wire-proven acknowledgements.");
    }

    public static bool IsObservedMirrorMode(uint mirrorMode) =>
        mirrorMode is LiveObservedMirrorMode or CapturedMirrorMode;

    private ushort GetMediaInfoSequence =>
        statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState ? (ushort)6 : (ushort)7;

    private ushort GetStateSequence =>
        statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState ? (ushort)7 : (ushort)6;

    private IReadOnlyList<MiPlayLegacyAudioSourceWrite> CreateStatusQueryWrites()
    {
        var volume = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetVolumeCommand,
            5,
            []);
        var mediaInfo = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMediaInfoCommand,
            GetMediaInfoSequence,
            []);
        var state = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetStateCommand,
            GetStateSequence,
            []);

        return statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState
            ?
            [
                new MiPlayLegacyAudioSourceWrite([volume]),
                new MiPlayLegacyAudioSourceWrite([mediaInfo]),
                new MiPlayLegacyAudioSourceWrite([state]),
            ]
            :
            [
                new MiPlayLegacyAudioSourceWrite([volume]),
                new MiPlayLegacyAudioSourceWrite([state]),
                new MiPlayLegacyAudioSourceWrite([mediaInfo]),
            ];
    }

    private MiPlayLegacyAudioSourceTransition CompleteStatusInitializationIfReady(
        MiPlayCommandFrame frame,
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        string waitingBoundary)
    {
        // Cmd_GetMediaInfo is not an acknowledgement gate in mirror mode 2.
        // The receiver may answer it as an asynchronous 0x0022 notification
        // whose sequence is receiver-owned and whose body reflects mutable
        // playback state.  The official source issues the query but advances
        // independently; require only the same-sequence scalar replies that
        // prove the command session is responsive.
        if (!accountAcknowledged ||
            !mirrorModeAcknowledged ||
            !statusQueriesPrepared ||
            !volumeAcknowledged ||
            !stateAcknowledged)
        {
            return Accept(frame, writes, completed: false, waitingBoundary);
        }

        phase = MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete;
        return Accept(
            frame,
            writes,
            completed: true,
            "Wire-proven legacy source identity and scalar status initialization is complete; media-info notification remains optional. Hard stop before 0x0040, Open, AddMirror, RTSP, playback, media, or audio.");
    }

    private MiPlayLegacyAudioSourceTransition Accept(
        MiPlayCommandFrame frame,
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        bool completed,
        string boundary) =>
        new(
            Accepted: true,
            phase,
            frame.Command,
            frame.Sequence,
            writes,
            completed,
            SafeForNetworkUse: false,
            boundary);

    private MiPlayLegacyAudioSourceTransition Reject(
        ushort command,
        ushort sequence,
        string boundary) =>
        new(
            Accepted: false,
            phase,
            command,
            sequence,
            [],
            Completed: phase == MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete,
            SafeForNetworkUse: false,
            boundary);

    private MiPlayLegacyAudioSourceTransition Stop(
        ushort command,
        ushort sequence,
        string boundary)
    {
        phase = MiPlayLegacyAudioSourcePhase.Stopped;
        return Reject(command, sequence, boundary);
    }
}
