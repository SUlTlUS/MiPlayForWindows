namespace DLNACast.Core.MiPlay;

public enum MiPlayLegacyPostOpenPlaybackPhase
{
    Created,
    AwaitingReceiverPlaybackReadiness,
    Playing,
    Stopped,
}

public sealed record MiPlayLegacyPostOpenPlaybackTransition(
    bool Accepted,
    MiPlayLegacyPostOpenPlaybackPhase Phase,
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> OutboundWrites,
    MiPlayNotifyPayload? Notify,
    string Boundary);

/// <summary>
/// Pure post-Open control state recovered from the rooted-phone capture. It
/// emits the playing SetMediaInfo recovered from a clean receiver-selection
/// capture, then waits for the receiver's first-audiopcm=1 and state=2
/// notifications. Pause, Resume, and a special startup heartbeat are
/// deliberately excluded: they were artifacts of user playback actions in an
/// earlier capture, not part of automatic receiver selection.
/// </summary>
public sealed class MiPlayLegacyPostOpenPlaybackSession
{
    public const ushort SetMediaInfoSequence = 15;
    public const ushort FirstPeriodicHeartbeatSequence = 16;

    private readonly byte[] mediaInfoPayload;
    private MiPlayLegacyPostOpenPlaybackPhase phase = MiPlayLegacyPostOpenPlaybackPhase.Created;

    public MiPlayLegacyPostOpenPlaybackSession(MiPlaySetMediaInfoPayload mediaInfo)
    {
        mediaInfoPayload = MiPlaySetMediaInfoPayloadCodec.Encode(mediaInfo);
    }

    public MiPlayLegacyPostOpenPlaybackPhase Phase => phase;
    public int? ReceiverState { get; private set; }
    public bool FirstAudioPcmObserved { get; private set; }
    public int UnsupportedNotificationCount { get; private set; }

    public MiPlayLegacyPostOpenPlaybackTransition Start()
    {
        if (phase != MiPlayLegacyPostOpenPlaybackPhase.Created)
        {
            return Reject("The post-Open playback session can be started only once.");
        }

        phase = MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness;
        return Accept(
            [Write(MiPlayProtocolConstants.SetMediaInfoCommand, SetMediaInfoSequence, mediaInfoPayload)],
            null,
            "Prepared SetMediaInfo without the user-triggered Pause; awaiting first-audiopcm=1.");
    }

    public MiPlayLegacyPostOpenPlaybackTransition ProcessInbound(ReadOnlySpan<byte> frameBytes)
    {
        if (phase is MiPlayLegacyPostOpenPlaybackPhase.Created or MiPlayLegacyPostOpenPlaybackPhase.Stopped)
        {
            return Reject("The post-Open playback session is not accepting inbound frames.");
        }
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) ||
            frame is null || consumed != frameBytes.Length)
        {
            return Stop("Inbound bytes are not exactly one complete MiPlay command frame.");
        }

        if (frame.Command == MiPlayProtocolConstants.NotifyCommand)
        {
            if (!MiPlayNotifyPayloadCodec.TryDecode(frame.Payload, out var notify, out var notifyConsumed) ||
                notify is null || notifyConsumed != frame.Payload.Length)
            {
                UnsupportedNotificationCount++;
                return Accept(
                    [],
                    null,
                    "Ignored an unsupported receiver notification payload without changing playback state.");
            }

            if (notify.Label == "first-audiopcm" && notify.IntegerValue == 1)
            {
                FirstAudioPcmObserved = true;
            }
            if (notify.Label == "state" && notify.IntegerValue is { } state)
            {
                ReceiverState = state;
            }
            if (phase == MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness &&
                FirstAudioPcmObserved && ReceiverState == 2)
            {
                phase = MiPlayLegacyPostOpenPlaybackPhase.Playing;
            }
            return Accept([], notify, $"Observed receiver notification {notify.Label}.");
        }

        if (IsOptionalEmptyAcknowledgement(frame))
        {
            return Accept([], null, "Observed an optional same-sequence control acknowledgement.");
        }

        return Stop($"Unexpected post-Open command 0x{frame.Command:X4} sequence {frame.Sequence}.");
    }

    private static bool IsOptionalEmptyAcknowledgement(MiPlayCommandFrame frame) =>
        frame.Payload.Length == 0 &&
        frame.Command == MiPlayProtocolConstants.SetMediaInfoAcknowledgementCommand &&
        frame.Sequence == SetMediaInfoSequence;

    private static MiPlayLegacyAudioSourceWrite Write(ushort command, ushort sequence, byte[] payload) =>
        new([MiPlayCommandFrameCodec.Encode(command, sequence, payload)]);

    private MiPlayLegacyPostOpenPlaybackTransition Accept(
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        MiPlayNotifyPayload? notify,
        string boundary) =>
        new(true, phase, writes, notify, boundary);

    private MiPlayLegacyPostOpenPlaybackTransition Reject(string boundary) =>
        new(false, phase, [], null, boundary);

    private MiPlayLegacyPostOpenPlaybackTransition Stop(string boundary)
    {
        phase = MiPlayLegacyPostOpenPlaybackPhase.Stopped;
        return Reject(boundary);
    }
}
