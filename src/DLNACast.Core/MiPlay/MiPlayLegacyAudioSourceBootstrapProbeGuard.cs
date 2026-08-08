using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyAudioSourceOutboundFrame(
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    string FrameSha256Hex);

public sealed record MiPlayLegacyAudioSourceWriteDecision(
    bool CanSend,
    IReadOnlyList<MiPlayLegacyAudioSourceOutboundFrame> Frames,
    int WritesAuthorized,
    int FramesAuthorized,
    bool BoundaryReached,
    string Reason);

/// <summary>
/// Runtime-only safety gate for the wire-reproduced legacy source bootstrap.
/// It authorizes exactly eight writes/nine frames in the captured order and
/// permanently refuses every playback, Open, AddMirror, RTSP, media, and audio command.
/// </summary>
public sealed class MiPlayLegacyAudioSourceBootstrapProbeGuard
{
    public const int MaximumWrites = 8;
    public const int MaximumFrames = 9;

    private readonly bool explicitlyAuthorized;
    private readonly MiPlayLegacyStatusQueryOrder statusQueryOrder;
    private int writesAuthorized;
    private int framesAuthorized;
    private bool stopped;

    public MiPlayLegacyAudioSourceBootstrapProbeGuard(
        IPAddress target,
        bool explicitlyAuthorized,
        MiPlayLegacyStatusQueryOrder statusQueryOrder = MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            IPAddress.Any.Equals(target) ||
            IPAddress.None.Equals(target) ||
            IPAddress.Broadcast.Equals(target) ||
            IPAddress.Loopback.Equals(target) ||
            target.IsIPv6Multicast)
        {
            throw new ArgumentException("The bootstrap target must be one explicit, non-loopback IPv4 address.", nameof(target));
        }

        Target = target;
        this.explicitlyAuthorized = explicitlyAuthorized;
        this.statusQueryOrder = statusQueryOrder;
    }

    public IPAddress Target { get; }

    public bool BoundaryReached => framesAuthorized == MaximumFrames;

    public MiPlayLegacyAudioSourceWriteDecision AuthorizeNextWrite(MiPlayLegacyAudioSourceWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (!explicitlyAuthorized)
        {
            return Refuse("Fresh explicit authorization was not supplied; no socket write is permitted.");
        }

        if (stopped || BoundaryReached)
        {
            stopped = true;
            return Refuse("The fixed nine-frame no-media boundary has already been reached or the guard has stopped.");
        }

        if (write.Frames.Count is < 1 or > 2 ||
            writesAuthorized >= MaximumWrites ||
            framesAuthorized + write.Frames.Count > MaximumFrames)
        {
            stopped = true;
            return Refuse("The write count or frame count differs from the captured eight-write/nine-frame transcript.");
        }

        var decoded = new List<MiPlayLegacyAudioSourceOutboundFrame>(write.Frames.Count);
        for (var index = 0; index < write.Frames.Count; index++)
        {
            var frameBytes = write.Frames[index];
            if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed) ||
                frame is null ||
                bytesConsumed != frameBytes.Length)
            {
                stopped = true;
                return Refuse("An outbound item is not exactly one complete MiPlay command frame.");
            }

            var slot = framesAuthorized + index;
            if (!MatchesCapturedSlot(slot, frame))
            {
                stopped = true;
                return Refuse(
                    $"Outbound slot {slot} does not match the captured legacy source command, sequence, or payload shape.");
            }

            decoded.Add(new MiPlayLegacyAudioSourceOutboundFrame(
                frame.Command,
                frame.Sequence,
                frame.Payload.Length,
                Convert.ToHexString(SHA256.HashData(frameBytes))));
        }

        if ((framesAuthorized == 0 && write.Frames.Count != 2) ||
            (framesAuthorized != 0 && write.Frames.Count != 1))
        {
            stopped = true;
            return Refuse("Only the first captured write may coalesce two frames (0x0036 plus 0x0029).");
        }

        writesAuthorized++;
        framesAuthorized += decoded.Count;
        return new MiPlayLegacyAudioSourceWriteDecision(
            true,
            decoded,
            writesAuthorized,
            framesAuthorized,
            BoundaryReached,
            BoundaryReached
                ? "Authorized the final read-only status query and reached the hard no-media boundary."
                : "Authorized the next exact captured legacy-clear bootstrap write.");
    }

    public static IReadOnlyList<string> CreateDryRunLedger(
        MiPlayLegacyStatusQueryOrder statusQueryOrder = MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState)
    {
        var secondStatus = statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState
            ? "0x0014 seq=6 empty GetMediaInfo"
            : "0x001c seq=6 empty GetState";
        var thirdStatus = statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState
            ? "0x001c seq=7 empty GetState"
            : "0x0014 seq=7 empty GetMediaInfo";

        return
        [
            "write 1: 0x0036 seq=0 sourceVersion=1.0.1123012\\0 + 0x0029 seq=receiver-challenge lowercase HMAC-SHA1(full challenge)",
            "write 2: 0x001e seq=1 empty GetDeviceInfo",
            "write 3: 0x0058 seq=2 sourceName-only JSON",
            "write 4: 0x0058 seq=3 {\"isSameAccount\":0}",
            "write 5: 0x0034 seq=4 empty GetMirrorMode",
            "write 6: 0x000e seq=5 empty GetVolume",
            $"write 7: {secondStatus}",
            $"write 8: {thirdStatus}",
        ];
    }

    private bool MatchesCapturedSlot(int slot, MiPlayCommandFrame frame) => slot switch
    {
        0 => frame.Command == MiPlayProtocolConstants.NativeSourceVersionCommand &&
             frame.Sequence == MiPlayLegacyAudioSourceSession.NativeVersionSequence &&
             frame.Payload.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(
                 MiPlayProtocolConstants.NativeSourceVersion12_4_8_13 + "\0")),
        1 => frame.Command == MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand &&
             frame.Payload.Length == 40 &&
             frame.Payload.All(IsLowerHexDigit),
        2 => MatchesEmpty(frame, MiPlayProtocolConstants.GetDeviceInfoCommand, 1),
        3 => frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
             frame.Sequence == 2 &&
             frame.Payload.AsSpan().SequenceEqual(
                 MiPlayLocalDeviceInfoPayloadCodec.EncodeLegacySourceNameOnly("MI PAD 4/Plus")),
        4 => frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
             frame.Sequence == 3 &&
             frame.Payload.AsSpan().SequenceEqual(MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0)),
        5 => MatchesEmpty(frame, MiPlayProtocolConstants.GetMirrorModeCommand, 4),
        6 => MatchesEmpty(frame, MiPlayProtocolConstants.GetVolumeCommand, 5),
        7 when statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState =>
            MatchesEmpty(frame, MiPlayProtocolConstants.GetMediaInfoCommand, 6),
        7 => MatchesEmpty(frame, MiPlayProtocolConstants.GetStateCommand, 6),
        8 when statusQueryOrder == MiPlayLegacyStatusQueryOrder.VolumeMediaInfoState =>
            MatchesEmpty(frame, MiPlayProtocolConstants.GetStateCommand, 7),
        8 => MatchesEmpty(frame, MiPlayProtocolConstants.GetMediaInfoCommand, 7),
        _ => false,
    };

    private static bool MatchesEmpty(MiPlayCommandFrame frame, ushort command, ushort sequence) =>
        frame.Command == command && frame.Sequence == sequence && frame.Payload.Length == 0;

    private static bool IsLowerHexDigit(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f';

    private MiPlayLegacyAudioSourceWriteDecision Refuse(string reason) =>
        new(false, [], writesAuthorized, framesAuthorized, BoundaryReached, reason);
}
