using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedTimeOffsetStateTests
{
    [Fact]
    public void LateSenderReceivesTheExistingGroupValueInsteadOfAHistoryDerivedCandidate()
    {
        var state = new MiPlaySharedTimeOffsetState();
        const ulong groupTimeOffset = 58_878_859_721;

        Assert.Equal(groupTimeOffset, state.Initialize(groupTimeOffset));
        Assert.Equal(groupTimeOffset, state.Initialize(groupTimeOffset));
        Assert.Equal(groupTimeOffset, state.GetCurrent());
        Assert.NotEqual(
            MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(
                serverNowMicroseconds: 60_000_000,
                firstAudioPresentationTimeMicroseconds: 9_000_000,
                playbackDelayMicroseconds: 1_000_000),
            state.GetCurrent());
    }

    [Fact]
    public void ResumeOrTransportRecoveryReplacesTheValueCopiedByTheNextSender()
    {
        var state = new MiPlaySharedTimeOffsetState();
        state.Initialize(58_878_859_721);
        var key = new MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.GroupResume,
            7);

        var firstSenderValue = state.PrepareRuntimeUpdate(key, 58_879_209_188);
        var laterSenderValue = state.PrepareRuntimeUpdate(key, 58_879_209_999);

        Assert.Equal(58_879_209_188UL, firstSenderValue);
        Assert.Equal(firstSenderValue, laterSenderValue);
        Assert.Equal(58_878_859_721UL, state.GetCurrent());

        state.CommitRuntimeUpdate(key, laterSenderValue);

        Assert.Equal(58_879_209_188UL, state.GetCurrent());
    }

    [Fact]
    public void LateCompletionCannotRollTheGroupBackToAnOlderRuntimeUpdate()
    {
        var state = new MiPlaySharedTimeOffsetState();
        state.Initialize(10);
        var older = new MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.TransportRecovery,
            40);
        var newer = new MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.TransportRecovery,
            80);

        var olderValue = state.PrepareRuntimeUpdate(older, 20);
        var newerValue = state.PrepareRuntimeUpdate(newer, 30);
        state.CommitRuntimeUpdate(newer, newerValue);
        state.CommitRuntimeUpdate(older, olderValue);

        Assert.Equal(30UL, state.GetCurrent());
    }

    [Fact]
    public void RejectsUseBeforeInitializationAndDisagreementAtInitialStartup()
    {
        var state = new MiPlaySharedTimeOffsetState();
        Assert.Throws<InvalidOperationException>(() => state.GetCurrent());
        var key = new MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.Resume,
            1);
        Assert.Throws<InvalidOperationException>(() => state.PrepareRuntimeUpdate(key, 1));
        Assert.Throws<InvalidOperationException>(() => state.CommitRuntimeUpdate(key, 1));
        state.Initialize(10);
        Assert.Throws<InvalidOperationException>(() => state.Initialize(11));
        Assert.Throws<InvalidOperationException>(() => state.CommitRuntimeUpdate(key, 1));
    }
}
