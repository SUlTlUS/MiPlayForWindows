using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySourceSilencePlaybackControlTests
{
    [Fact]
    public void PausesOnceAfterSustainedSilenceAndResumesOnceOnAudio()
    {
        var control = new MiPlaySourceSilencePlaybackControl(pauseAfterSilentFrames: 3);

        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.Pause, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.Resume, control.Observe(true));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(true));
    }

    [Fact]
    public void BriefSilenceDoesNotPauseAndAudioRestartsTheDebounce()
    {
        var control = new MiPlaySourceSilencePlaybackControl(pauseAfterSilentFrames: 3);

        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(true));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.None, control.Observe(false));
        Assert.Equal(MiPlaySourcePlaybackTransition.Pause, control.Observe(false));
    }
}
