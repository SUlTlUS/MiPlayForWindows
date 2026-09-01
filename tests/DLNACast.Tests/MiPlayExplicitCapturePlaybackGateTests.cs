using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayExplicitPlaybackGateTests
{
    [Fact]
    public async Task ExplicitPauseBlocksEncoderInputUntilResume()
    {
        var gate = new MiPlayExplicitPlaybackGate();
        await gate.WaitUntilResumedAsync(CancellationToken.None);

        gate.Pause();
        var wait = gate.WaitUntilResumedAsync(CancellationToken.None);

        Assert.True(gate.IsPaused);
        Assert.False(wait.IsCompleted);
        gate.Resume();
        await wait;
        Assert.False(gate.IsPaused);
    }

    [Fact]
    public async Task WaitCanBeCancelledWithoutResumingTheGate()
    {
        var gate = new MiPlayExplicitPlaybackGate();
        gate.Pause();
        using var cancellation = new CancellationTokenSource();
        var wait = gate.WaitUntilResumedAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.True(gate.IsPaused);
        gate.Resume();
        await gate.WaitUntilResumedAsync(CancellationToken.None);
    }
}
