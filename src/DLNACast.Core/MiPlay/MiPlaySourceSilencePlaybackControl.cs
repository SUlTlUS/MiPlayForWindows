namespace DLNACast.Core.MiPlay;

internal enum MiPlaySourcePlaybackTransition
{
    None,
    Pause,
    Resume,
}

internal sealed class MiPlaySourceSilencePlaybackControl(
    int pauseAfterSilentFrames = MiPlaySourceSilencePlaybackControl.DefaultPauseAfterSilentFrames)
{
    public const int DefaultPauseAfterSilentFrames = 1_000 / 20;

    private readonly int pauseAfterSilentFrames = pauseAfterSilentFrames > 0
        ? pauseAfterSilentFrames
        : throw new ArgumentOutOfRangeException(nameof(pauseAfterSilentFrames));
    private int consecutiveSilentFrames;
    private bool pauseRequested;

    public MiPlaySourcePlaybackTransition Observe(bool containsAudibleSignal)
    {
        if (containsAudibleSignal)
        {
            consecutiveSilentFrames = 0;
            if (!pauseRequested)
            {
                return MiPlaySourcePlaybackTransition.None;
            }

            pauseRequested = false;
            return MiPlaySourcePlaybackTransition.Resume;
        }

        if (pauseRequested)
        {
            return MiPlaySourcePlaybackTransition.None;
        }

        consecutiveSilentFrames++;
        if (consecutiveSilentFrames < pauseAfterSilentFrames)
        {
            return MiPlaySourcePlaybackTransition.None;
        }

        pauseRequested = true;
        return MiPlaySourcePlaybackTransition.Pause;
    }
}
