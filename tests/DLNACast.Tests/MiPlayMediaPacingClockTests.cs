using DLNACast.Core.MiPlay;
using System.Diagnostics;

namespace DLNACast.Tests;

public sealed class MiPlayMediaPacingClockTests
{
    [Theory]
    [InlineData(1_000_000, -10_000_000)]
    [InlineData(21_333, -213_330)]
    [InlineData(1, -10)]
    public void ConvertsStopwatchTicksToNegativeRelativeHundredNanoseconds(
        long remainingTicks,
        long expectedDueTime)
    {
        if (Stopwatch.Frequency != 1_000_000)
        {
            var expected = -(long)Math.Ceiling(
                remainingTicks * 10_000_000d / Stopwatch.Frequency);
            Assert.Equal(
                Math.Min(-1, expected),
                MiPlayMediaPacingClock.ConvertStopwatchTicksToRelativeDueTime(remainingTicks));
            return;
        }

        Assert.Equal(
            expectedDueTime,
            MiPlayMediaPacingClock.ConvertStopwatchTicksToRelativeDueTime(remainingTicks));
    }

    [Fact]
    public void RejectsNonPositiveRemainingTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayMediaPacingClock.ConvertStopwatchTicksToRelativeDueTime(0));
    }

    [Fact]
    public async Task WindowsHighResolutionTimerWaitsForTheRequestedDeadline()
    {
        using var clock = MiPlayMediaPacingClock.Create();
        var startedAt = Stopwatch.GetTimestamp();
        var deadline = startedAt + Stopwatch.Frequency / 100;

        await clock.WaitUntilAsync(deadline, CancellationToken.None);

        var elapsedMilliseconds =
            (Stopwatch.GetTimestamp() - startedAt) * 1_000d / Stopwatch.Frequency;
        Assert.InRange(elapsedMilliseconds, 5, 1_000);
    }
}
