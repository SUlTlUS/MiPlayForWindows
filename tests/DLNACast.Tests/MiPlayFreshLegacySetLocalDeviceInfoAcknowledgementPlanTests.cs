using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanTests
{
    [Fact]
    public void BuildsSameSequenceEmptyClearAcknowledgementOffline()
    {
        var plan = MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.CreateOfflinePlan();

        Assert.Equal((ushort)0x0003, plan.RequestSequence);
        Assert.Equal("{\"isSameAccount\":0}", Encoding.UTF8.GetString(plan.RequestPayload));
        Assert.Equal(9, plan.AcknowledgementFrame.Length);
        Assert.Equal(
            "7E597F917619DF09D1F86173EAF953BB0DE9F06575DB919A67217C645FD242B8",
            plan.AcknowledgementFrameSha256);
        Assert.False(plan.ExactFreshClearAcknowledgementObserved);
        Assert.False(plan.SafeForNetworkUse);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(plan.AcknowledgementFrame, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(plan.AcknowledgementFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, frame.Command);
        Assert.Equal(plan.RequestSequence, frame.Sequence);
        Assert.Empty(frame.Payload);
    }

    [Fact]
    public void CurrentEvidenceBuildsCandidateButDoesNotAuthorizeSend()
    {
        var snapshot = MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.CreateCurrentSnapshot();
        var decision = MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.Evaluate(snapshot);

        Assert.True(decision.CanBuildDeterministicCandidate);
        Assert.False(decision.CanSendNow);
        Assert.False(snapshot.Lx0618851ContainsSetLocalDeviceInfoHandler);
        Assert.Contains("empty command plaintext", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("enqueues GetMirrorMode without waiting", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("No fresh legacy-clear 0x0059", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("not the next evidence priority", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("already-queued 0x0034", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("default false-return branch", decision.RemainingBoundary, StringComparison.Ordinal);
        Assert.Contains("SafeForNetworkUse=false", decision.RemainingBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCurrentS12PlaintextOrOldFirmwareDistinctionInvalidatesCandidate()
    {
        var snapshot = MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.CreateCurrentSnapshot();

        Assert.False(MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.Evaluate(
            snapshot with { CurrentS12ContinuationDecryptProvesEmptyAcknowledgementPlaintext = false }).CanBuildDeterministicCandidate);
        Assert.False(MiPlayFreshLegacySetLocalDeviceInfoAcknowledgementPlanner.Evaluate(
            snapshot with { Lx0618851ContainsSetLocalDeviceInfoHandler = true }).CanBuildDeterministicCandidate);
    }
}
