namespace DLNACast.Core.MiPlay;

internal sealed record MiPlayLegacyRuntimeControlCommand(
    ushort Command,
    ushort AcknowledgementCommand,
    ushort Sequence,
    byte[] Payload,
    byte[] CommandFrame,
    bool WaitForAcknowledgement);

/// <summary>
/// Allocates one shared command sequence for all controls sent after
/// SetMediaInfo. Runtime volume changes therefore advance subsequent heartbeat
/// sequences instead of colliding with the captured no-interleaving schedule.
/// </summary>
internal sealed class MiPlayLegacyRuntimeControlSequence(
    ushort initialSequence = MiPlayLegacyPostOpenPlaybackSession.FirstPeriodicHeartbeatSequence)
{
    public const int CapturedResumeRepeatDelayMilliseconds = 29;

    private ushort nextSequence = initialSequence;

    public MiPlayLegacyRuntimeControlCommand PrepareHeartbeat() =>
        Prepare(
            MiPlayProtocolConstants.HeartbeatCommand,
            MiPlayProtocolConstants.HeartbeatAcknowledgementCommand,
            []);

    public MiPlayLegacyRuntimeControlCommand PreparePause() =>
        Prepare(
            MiPlayProtocolConstants.PauseCommand,
            MiPlayProtocolConstants.PauseAcknowledgementCommand,
            [],
            waitForAcknowledgement: false);

    public MiPlayLegacyRuntimeControlCommand PrepareResume() =>
        Prepare(
            MiPlayProtocolConstants.ResumeCommand,
            MiPlayProtocolConstants.ResumeAcknowledgementCommand,
            [],
            waitForAcknowledgement: false);

    public (MiPlayLegacyRuntimeControlCommand First, MiPlayLegacyRuntimeControlCommand Second)
        PrepareResumePair() =>
        (PrepareResume(), PrepareResume());

    public MiPlayLegacyRuntimeControlCommand PrepareSetVolume(int volume)
    {
        if (volume is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        return Prepare(
            MiPlayProtocolConstants.SetVolumeCommand,
            MiPlayProtocolConstants.SetVolumeAcknowledgementCommand,
            MiPlaySetVolumePayloadCodec.Encode((uint)volume));
    }

    public static bool IsExpectedAcknowledgement(
        MiPlayLegacyRuntimeControlCommand command,
        MiPlayCommandFrame acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(acknowledgement);
        return acknowledgement.Command == command.AcknowledgementCommand &&
            acknowledgement.Sequence == command.Sequence &&
            acknowledgement.Payload.AsSpan().SequenceEqual(command.Payload);
    }

    private MiPlayLegacyRuntimeControlCommand Prepare(
        ushort command,
        ushort acknowledgementCommand,
        byte[] payload,
        bool waitForAcknowledgement = true)
    {
        var sequence = nextSequence;
        nextSequence = unchecked((ushort)(nextSequence + 1));
        return new(
            command,
            acknowledgementCommand,
            sequence,
            payload,
            MiPlayCommandFrameCodec.Encode(command, sequence, payload),
            waitForAcknowledgement);
    }
}
