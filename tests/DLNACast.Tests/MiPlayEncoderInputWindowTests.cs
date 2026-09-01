using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayEncoderInputWindowTests
{
    [Fact]
    public async Task StartsImmediatelyButBoundsAllUnconsumedEncoderInput()
    {
        using var window = new MiPlayEncoderInputWindow();
        Assert.Equal(12, MiPlayEncoderInputWindow.DefaultCapacity);
        // Startup can still collect the tablet's fourteen-AU runway because
        // each consumed encoder output releases one input slot while FillAsync
        // is running; the whole runway need not remain in the input window.
        Assert.True(MiPlayEncoderInputWindow.DefaultCapacity <
            MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount);

        for (var i = 0; i < MiPlayEncoderInputWindow.DefaultCapacity; i++)
        {
            var acquire = window.AcquireAsync(CancellationToken.None);
            Assert.True(acquire.IsCompletedSuccessfully);
            await acquire;
        }

        Assert.Equal(MiPlayEncoderInputWindow.DefaultCapacity, window.PendingFrameCount);
        var blockedInput = window.AcquireAsync(CancellationToken.None);
        Assert.False(blockedInput.IsCompleted);

        window.ReleaseConsumedFrame();
        await blockedInput.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(MiPlayEncoderInputWindow.DefaultCapacity, window.PendingFrameCount);
    }

    [Fact]
    public async Task ConsumingOneAccessUnitOnlyReleasesOneInputSlot()
    {
        using var window = new MiPlayEncoderInputWindow(capacity: 1);
        await window.AcquireAsync(CancellationToken.None);
        var first = window.AcquireAsync(CancellationToken.None);
        Assert.False(first.IsCompleted);

        window.ReleaseConsumedFrame();
        await first.WaitAsync(TimeSpan.FromSeconds(1));
        var second = window.AcquireAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);

        window.ReleaseConsumedFrame();
        await second.WaitAsync(TimeSpan.FromSeconds(1));
        window.ReleaseConsumedFrame();
        Assert.Equal(0, window.PendingFrameCount);
    }

    [Fact]
    public async Task CancelingABlockedInputDoesNotConsumeAnotherSlot()
    {
        using var window = new MiPlayEncoderInputWindow(capacity: 1);
        await window.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var blocked = window.AcquireAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        window.ReleaseConsumedFrame();
        Assert.Equal(0, window.PendingFrameCount);
        await window.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RejectsCompletionWithoutCorrespondingInput()
    {
        using var window = new MiPlayEncoderInputWindow();
        Assert.Throws<SemaphoreFullException>(window.ReleaseConsumedFrame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidCapacity(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiPlayEncoderInputWindow(capacity));
}
