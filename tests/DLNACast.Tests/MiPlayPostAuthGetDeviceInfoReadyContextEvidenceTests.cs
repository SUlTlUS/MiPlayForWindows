using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthGetDeviceInfoReadyContextEvidenceTests
{
    [Fact]
    public void SnapshotCapturesOfficialReadOnlyGetDeviceInfoAsNextSemanticGate()
    {
        var snapshot = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.OfficialPhoneOrderGetsDeviceInfoBeforeSetPlaySource);
        Assert.True(snapshot.CmdSourceGetDeviceInfoUsesEmpty001ePayload);
        Assert.True(snapshot.CmdSourceSendCmdPayloadSafetyDataWrapsOriginalCommand);
        Assert.True(snapshot.Source001fAckListenerLocalized);
        Assert.True(snapshot.Receiver18851GetDeviceInfoHandlerPreservesSequence);
        Assert.True(snapshot.ReceiverDeviceInfoPayloadCodecAvailable);
        Assert.True(snapshot.Current19413LegacyClear001fObserved);
        Assert.False(snapshot.Current19413PostAuthSafetyData001fObserved);
        Assert.False(snapshot.CurrentProbeReproducesListenerOnSuccessReadyContext);
        Assert.True(snapshot.CandidateIsReadOnlyGetDeviceInfoOnly);
        Assert.True(snapshot.CandidateForbids0058OpenAddMirrorRtspMediaPlaybackAudio);
        Assert.True(snapshot.NoNetworkOperationPerformed);

        Assert.Contains("cmdSessionSuccess", MiPlayPostAuthGetDeviceInfoReadyContextEvidence.OfficialOrder, StringComparison.Ordinal);
        Assert.Contains("0x001e", MiPlayPostAuthGetDeviceInfoReadyContextEvidence.SourceFrameShape, StringComparison.Ordinal);
        Assert.Contains("0x001f", MiPlayPostAuthGetDeviceInfoReadyContextEvidence.SourceAckObservation, StringComparison.Ordinal);
        Assert.Contains("receiver context only", MiPlayPostAuthGetDeviceInfoReadyContextEvidence.ReceiverSemantics, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflinePlanIsReadOnlySameSequenceAndNotSafeForNetworkUse()
    {
        var plan = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateOfflineReadOnlyPlan();

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, plan.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, plan.ExpectedAcknowledgement);
        Assert.Equal((ushort)0x0004, plan.FirstCandidateSequence);
        Assert.Equal(0, plan.PlaintextPayloadLength);
        Assert.Equal(MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength, plan.MinimumAcknowledgementPayloadLength);
        Assert.True(plan.RequiresSafetyDataWrapper);
        Assert.True(plan.RequiresSameSequenceAcknowledgement);
        Assert.False(plan.SafeForNetworkUse);
        Assert.Contains("no 0x0040", plan.Boundary, StringComparison.Ordinal);
        Assert.Contains("0x0058", plan.Boundary, StringComparison.Ordinal);
        Assert.Contains("no 0x0040, 0x0058, Open, AddMirror, RTSP, media, playback, audio", plan.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentDecisionAllowsOnlyOfflinePlanUntilListenerReadyContextIsRecovered()
    {
        var decision = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.Evaluate(
            MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanSendLiveReadOnlyProbe);
        Assert.True(decision.CanWriteOfflineReadOnlyPlan);
        Assert.False(decision.CanAdvanceToLocalDeviceInfoGate);
        Assert.NotNull(decision.Plan);
        Assert.False(decision.Plan.SafeForNetworkUse);
        Assert.Contains("read-only getDeviceInfo ready-context plan", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not another 0x0040", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafeForNetworkUse=false", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("fresh explicit authorization", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("same-sequence 0x001f", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSourceAckPathBlocksEvenOfflineReadOnlyPlan()
    {
        var snapshot = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot() with
        {
            Source001fAckListenerLocalized = false,
        };

        var decision = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.Evaluate(snapshot);

        Assert.False(decision.CanSendLiveReadOnlyProbe);
        Assert.False(decision.CanWriteOfflineReadOnlyPlan);
        Assert.False(decision.CanAdvanceToLocalDeviceInfoGate);
        Assert.Contains("0x001f observation path is incomplete", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MutatingExpansionInvalidatesReadOnlyGate()
    {
        var snapshot = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot() with
        {
            CandidateForbids0058OpenAddMirrorRtspMediaPlaybackAudio = false,
        };

        var decision = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.Evaluate(snapshot);

        Assert.False(decision.CanSendLiveReadOnlyProbe);
        Assert.False(decision.CanWriteOfflineReadOnlyPlan);
        Assert.False(decision.CanAdvanceToLocalDeviceInfoGate);
        Assert.Contains("expands beyond a single read-only getDeviceInfo", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedPostAuth001fCanOnlyAdvanceToSeparateLocalDeviceInfoGate()
    {
        var snapshot = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot() with
        {
            CurrentProbeReproducesListenerOnSuccessReadyContext = true,
            Current19413PostAuthSafetyData001fObserved = true,
        };

        var decision = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.Evaluate(snapshot);

        Assert.False(decision.CanSendLiveReadOnlyProbe);
        Assert.False(decision.CanWriteOfflineReadOnlyPlan);
        Assert.True(decision.CanAdvanceToLocalDeviceInfoGate);
        Assert.Contains("separate local-device-info gate", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize 0x0058", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkOperationInvalidatesStaticReadyContextBoundary()
    {
        var snapshot = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.CreateCurrentSnapshot() with
        {
            NoNetworkOperationPerformed = false,
        };

        var decision = MiPlayPostAuthGetDeviceInfoReadyContextEvidence.Evaluate(snapshot);

        Assert.False(decision.CanSendLiveReadOnlyProbe);
        Assert.False(decision.CanWriteOfflineReadOnlyPlan);
        Assert.False(decision.CanAdvanceToLocalDeviceInfoGate);
        Assert.Contains("offline-only", decision.Reason, StringComparison.Ordinal);
    }
}
