using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyStreamingHeartbeatPlanTests
{
    [Fact]
    public void AdvancesAnUnboundedSessionAtFiveSecondIntervals()
    {
        const long previousHeartbeatTimestamp = 1_000_000;
        const long frequency = 10_000_000;

        var firstDue = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
            previousHeartbeatTimestamp,
            MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
            frequency);
        var secondDue = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
            firstDue,
            MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
            frequency);

        Assert.Equal(51_000_000, firstDue);
        Assert.Equal(101_000_000, secondDue);
    }
}
