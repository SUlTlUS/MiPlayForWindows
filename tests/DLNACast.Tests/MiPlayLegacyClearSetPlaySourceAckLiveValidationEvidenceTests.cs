using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidenceTests
{
    [Fact]
    public void CapturesImmediateAndAfterReadyNotifyLegacyClearRuns()
    {
        var immediate = MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateImmediateSnapshot();
        var afterReady = MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateAfterReadyNotifySnapshot();

        Assert.Equal("192.168.10.4", MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.DeviceAddress);
        Assert.Equal("2.1.5091615", MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal((ushort)0x0002, MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.SetPlaySourceSequence);
        Assert.Equal("immediate-after-0x0029", immediate.Mode);
        Assert.Equal("after-state-3-notify", afterReady.Mode);
        Assert.False(immediate.ReadyStateNotifyObservedBeforeSetPlaySource);
        Assert.True(afterReady.ReadyStateNotifyObservedBeforeSetPlaySource);
        Assert.Equal(4, immediate.FollowUpFrameCountBeforeClose);
        Assert.Equal(4, afterReady.FollowUpFrameCountBeforeClose);
    }

    [Fact]
    public void DecisionRulesOutLegacyClearTimingOnlyHypothesis()
    {
        var decision = MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.EvaluateLegacyClearResult(
            MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateImmediateSnapshot(),
            MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateAfterReadyNotifySnapshot());

        Assert.False(decision.LegacyClearDispatcherVerified);
        Assert.Contains("state=3", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("without 0x0041", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not reaching", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ServerApp::doMpasCommand", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesLegacyClearEvidence()
    {
        var unsafeImmediate = MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateImmediateSnapshot() with
        {
            ModernSafetyInfoSent = true
        };

        var decision = MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.EvaluateLegacyClearResult(
            unsafeImmediate,
            MiPlayLegacyClearSetPlaySourceAckLiveValidationEvidence.CreateAfterReadyNotifySnapshot());

        Assert.False(decision.LegacyClearDispatcherVerified);
        Assert.Contains("boundary", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
