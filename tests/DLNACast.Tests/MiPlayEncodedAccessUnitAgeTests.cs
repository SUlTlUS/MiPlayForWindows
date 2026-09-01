using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayEncodedAccessUnitAgeTests
{
    [Fact]
    public void CalculatesAgeFromTheCapturePresentationTimestamp()
    {
        Assert.Equal(21.333, MiPlayEncodedAccessUnitAge.CalculateMilliseconds(2_021_333, 2_000_000), 3);
    }

    [Theory]
    [InlineData(2_000_000UL, 2_000_000UL)]
    [InlineData(1_999_999UL, 2_000_000UL)]
    public void ClampsAClockThatHasNotPassedThePresentationTimestamp(
        ulong currentMonotonicMicroseconds,
        ulong presentationTimeMicroseconds)
    {
        Assert.Equal(
            0,
            MiPlayEncodedAccessUnitAge.CalculateMilliseconds(
                currentMonotonicMicroseconds,
                presentationTimeMicroseconds));
    }
}
