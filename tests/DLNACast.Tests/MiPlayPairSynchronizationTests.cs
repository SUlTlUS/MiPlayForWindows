using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPairSynchronizationTests
{
    [Fact]
    public async Task ReleasesOpenAndMediaOnlyAfterBothSessionsArrive()
    {
        var synchronization = new MiPlayPairSynchronization();

        var firstOpen = synchronization.SynchronizeOpenAsync();
        Assert.False(firstOpen.IsCompleted);
        await synchronization.SynchronizeOpenAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await firstOpen.WaitAsync(TimeSpan.FromSeconds(1));

        var firstMedia = synchronization.SynchronizeMediaAsync();
        Assert.False(firstMedia.IsCompleted);
        await synchronization.SynchronizeMediaAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await firstMedia.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task BreakPropagatesToBothSynchronizationPhases()
    {
        var synchronization = new MiPlayPairSynchronization();
        var open = synchronization.SynchronizeOpenAsync();
        var failure = new IOException("paired receiver failed");

        synchronization.Break(failure);

        var openFailure = await Assert.ThrowsAsync<IOException>(() => open);
        var mediaFailure = await Assert.ThrowsAsync<IOException>(() =>
            synchronization.SynchronizeMediaAsync());
        Assert.Same(failure, openFailure);
        Assert.Same(failure, mediaFailure);
    }

    [Fact]
    public async Task RejectsAThirdParticipantInEitherPhase()
    {
        var synchronization = new MiPlayPairSynchronization();
        await Task.WhenAll(
            synchronization.SynchronizeOpenAsync(),
            synchronization.SynchronizeOpenAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            synchronization.SynchronizeOpenAsync());
    }
}
