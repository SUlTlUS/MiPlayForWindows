using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedStartupAccessUnitBufferTests
{
    [Fact]
    public void LateJoinCountMatchesTheTabletOneSecondHistoryWindow()
    {
        Assert.Equal(47, MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount);
        Assert.True(46 * 1_024L * 1_000_000 / 48_000 < 1_000_000);
        Assert.True(47 * 1_024L * 1_000_000 / 48_000 >= 1_000_000);
        Assert.Equal(14, MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount);
    }

    [Fact]
    public async Task InitialSharedGroupKeepsTheValidatedFourteenAccessUnitRunway()
    {
        var source = new Queue<MiPlaySharedIndexedAccessUnit>(
            Enumerable.Range(0, MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount)
                .Select(index => Frame(index)));

        var buffer = await MiPlaySharedStartupAccessUnitBuffer.FillAsync(
            _ => ValueTask.FromResult(source.Dequeue()),
            MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount,
            CancellationToken.None);

        Assert.Equal(14, buffer.Count);
        Assert.Equal(0, buffer.FirstAccessUnitIndex);
    }

    [Fact]
    public async Task RetainsGlobalIndexesWhileDrainingTheLateJoinRunway()
    {
        var source = new Queue<MiPlaySharedIndexedAccessUnit>(
            Enumerable.Range(41, MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount)
            .Select(index => Frame(index)));
        var buffer = await MiPlaySharedStartupAccessUnitBuffer.FillAsync(
            _ => ValueTask.FromResult(source.Dequeue()),
            MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount,
            CancellationToken.None);

        Assert.Equal(41, buffer.FirstAccessUnitIndex);
        Assert.Equal(MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount, buffer.Count);
        var drained = new List<long>();
        while (buffer.TryRead(out var accessUnit))
        {
            drained.Add(accessUnit.AccessUnitIndex);
            Assert.False(accessUnit.ParticipatesInSendBarrier);
        }
        Assert.Equal(
            Enumerable.Range(41, MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount)
                .Select(index => (long)index),
            drained);
    }

    [Fact]
    public async Task RejectsAGappedRunwayBeforeAnyMediaCanBeSent()
    {
        var source = new Queue<MiPlaySharedIndexedAccessUnit>(
            Enumerable.Range(41, MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount)
            .Select(index => Frame(index == 48 ? 49 : index)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MiPlaySharedStartupAccessUnitBuffer.FillAsync(
                _ => ValueTask.FromResult(source.Dequeue()),
                MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount,
                CancellationToken.None));
    }

    private static MiPlaySharedIndexedAccessUnit Frame(long index) =>
        new(
            index,
            new MiPlayEncodedAccessUnit([(byte)index], checked((ulong)index * 21_333)),
            ParticipatesInSendBarrier: false);
}
