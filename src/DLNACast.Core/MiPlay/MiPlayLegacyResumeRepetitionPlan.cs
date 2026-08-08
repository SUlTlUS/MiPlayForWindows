namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyResumeRepetition(
    int DueAfterInitialResumeMilliseconds,
    ushort Sequence,
    byte[] CommandFrame);

/// <summary>
/// Historical user-triggered Resume repetition from the contaminated playback
/// trace. It is retained only for offline evidence and is not part of the
/// automatic receiver-selection or Windows playback plan.
/// </summary>
public static class MiPlayLegacyResumeRepetitionPlan
{
    private static readonly int[] CapturedDueMilliseconds = [69, 98, 105];

    public static IReadOnlyList<MiPlayLegacyResumeRepetition> Create(
        ushort firstSequence = 18) =>
        CapturedDueMilliseconds.Select((due, index) =>
        {
            var sequence = checked((ushort)(firstSequence + index));
            return new MiPlayLegacyResumeRepetition(
                due,
                sequence,
                MiPlayCommandFrameCodec.Encode(
                    MiPlayProtocolConstants.ResumeCommand,
                    sequence,
                    []));
        }).ToArray();
}
