namespace DLNACast.Core.MiPlay;

/// <summary>
/// Converts the monotonic first-sample time attached to each captured PCM read
/// into the relative 90 kHz PTS used by MiPlay. A recovery frame can establish
/// a new capture anchor while the receiver-facing timeline advances by exactly
/// one AAC access unit.
/// </summary>
internal sealed class MiPlayCapturePresentationTimeline
{
    public const ulong NominalAccessUnitMicroseconds = 21_333;
    public const ulong DiscontinuityThresholdMicroseconds = 42_667;

    private ulong anchorPresentationTimeMicroseconds;
    private uint anchorTimestamp90Khz;
    private ulong previousPresentationTimeMicroseconds;
    private uint previousTimestamp90Khz;
    private bool initialized;

    public uint Map(
        ulong presentationTimeMicroseconds,
        bool reanchorPresentationTimeline = false)
    {
        if (!initialized)
        {
            initialized = true;
            anchorPresentationTimeMicroseconds = presentationTimeMicroseconds;
            previousPresentationTimeMicroseconds = presentationTimeMicroseconds;
            anchorTimestamp90Khz = 0;
            previousTimestamp90Khz = 0;
            return 0;
        }

        if (reanchorPresentationTimeline)
        {
            return Reanchor(presentationTimeMicroseconds);
        }

        if (presentationTimeMicroseconds > previousPresentationTimeMicroseconds &&
            presentationTimeMicroseconds - previousPresentationTimeMicroseconds >=
                DiscontinuityThresholdMicroseconds)
        {
            // Frames dropped by an internal bounded buffer are not capture
            // discontinuities and must not manufacture three more silence
            // frames. Keep receiver time continuous and anchor to the newest
            // surviving capture frame.
            return Reanchor(presentationTimeMicroseconds);
        }

        if (presentationTimeMicroseconds <= previousPresentationTimeMicroseconds)
        {
            previousTimestamp90Khz = unchecked(
                previousTimestamp90Khz + MiPlayWfdAudioPacketizer.TimestampStep90Khz);
            return previousTimestamp90Khz;
        }

        previousPresentationTimeMicroseconds = presentationTimeMicroseconds;

        var relativeMicroseconds = presentationTimeMicroseconds >= anchorPresentationTimeMicroseconds
            ? presentationTimeMicroseconds - anchorPresentationTimeMicroseconds
            : 0;
        var timestamp90Khz = unchecked((uint)(
            ((UInt128)relativeMicroseconds * MiPlayWfdMediaClock.ProgramClockRate) /
            MiPlayWfdMediaClock.MicrosecondsPerSecond) +
            anchorTimestamp90Khz);
        if (timestamp90Khz <= previousTimestamp90Khz)
        {
            timestamp90Khz = unchecked(
                previousTimestamp90Khz + MiPlayWfdAudioPacketizer.TimestampStep90Khz);
        }
        previousTimestamp90Khz = timestamp90Khz;
        return timestamp90Khz;
    }

    private uint Reanchor(ulong presentationTimeMicroseconds)
    {
        previousTimestamp90Khz = unchecked(
            previousTimestamp90Khz + MiPlayWfdAudioPacketizer.TimestampStep90Khz);
        anchorPresentationTimeMicroseconds = presentationTimeMicroseconds;
        anchorTimestamp90Khz = previousTimestamp90Khz;
        previousPresentationTimeMicroseconds = presentationTimeMicroseconds;
        return previousTimestamp90Khz;
    }
}
