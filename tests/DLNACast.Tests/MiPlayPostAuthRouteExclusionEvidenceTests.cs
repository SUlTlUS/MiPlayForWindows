using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthRouteExclusionEvidenceTests
{
    [Fact]
    public void CurrentSnapshotKeepsControlSessionVersionSeparateFromFirmwareVersion()
    {
        var snapshot = MiPlayPostAuthRouteExclusionEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.UserConfirmedCurrentLx06FirmwareVersionKnown);
        Assert.True(snapshot.ControlSessionVersionFrameKeptSeparateFromFirmwareVersion);
        Assert.Equal("1.94.13", MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame);
        Assert.Contains("not LX06 ROM firmware", MiPlayPostAuthRouteExclusionEvidence.FirmwareVersionBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentNegativeMatrixDoesNotJustifyRepeatingBusinessFrameProbe()
    {
        var snapshot = MiPlayPostAuthRouteExclusionEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasSetPlaySourceAckBeforePayloadParseObserved);
        Assert.False(snapshot.ModernSafetyOwnerLocalizedIn18851ReceiverStack);
        Assert.True(snapshot.LegacyClearImmediateSetPlaySourceClosedWithoutAck);
        Assert.True(snapshot.LegacyClearAfterReadyNotifySetPlaySourceClosedWithoutAck);
        Assert.True(snapshot.SafetyDataImmediateSetPlaySourceClosedWithoutAck);
        Assert.True(snapshot.SafetyDataDelayedSetPlaySourceClosedWithoutAck);
        Assert.True(snapshot.SafetyDataNativeNoResetOfficialJsonClosedWithoutAck);
        Assert.True(snapshot.SafetyDataNativeNoResetOfficialJsonUsedSeparatedOutboundProfile);
        Assert.True(snapshot.StrictNoMediaNoPlaybackBoundaryHeld);
        Assert.False(snapshot.Current19413CommandSessionBridgeLocalized);

        var decision = MiPlayPostAuthRouteExclusionEvidence.EvaluateNextLiveBusinessProbe(snapshot);

        Assert.False(decision.CanJustifyNextLiveBusinessProbe);
        Assert.Contains("Five SetPlaySource routes", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("legacy clear immediate", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("state=3 notify", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("500 ms", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("native-no-reset SafetyData official JSON", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("missing ref_channel/ref_function/ref_content JSON", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("old promoted-inbound-IV outbound state alone", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("source/session context", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("ordering/state transition", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void MatrixRequiresNativeNoResetSeparatedOutboundEvidence()
    {
        var decision = MiPlayPostAuthRouteExclusionEvidence.EvaluateNextLiveBusinessProbe(
            MiPlayPostAuthRouteExclusionEvidence.CreateCurrentSnapshot() with
            {
                SafetyDataNativeNoResetOfficialJsonUsedSeparatedOutboundProfile = false,
            });

        Assert.False(decision.CanJustifyNextLiveBusinessProbe);
        Assert.Contains("outbound command cipher was separated", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateRequiresLocalizedBridgeAndReadOnlyAckBeforeMutationBoundary()
    {
        var current = MiPlayPostAuthRouteExclusionEvidence.CreateCurrentSnapshot();

        var missingReadOnlyBoundary = MiPlayPostAuthRouteExclusionEvidence.EvaluateNextLiveBusinessProbe(
            current with
            {
                Current19413CommandSessionBridgeLocalized = true,
                CandidateFrameTargetsLocalizedBridge = true,
                CandidateFrameIsReadOnlyAckBeforeMutation = false,
            });

        Assert.False(missingReadOnlyBoundary.CanJustifyNextLiveBusinessProbe);
        Assert.Contains("read-only", missingReadOnlyBoundary.Reason, StringComparison.OrdinalIgnoreCase);

        var ready = MiPlayPostAuthRouteExclusionEvidence.EvaluateNextLiveBusinessProbe(
            current with
            {
                Current19413CommandSessionBridgeLocalized = true,
                CandidateFrameTargetsLocalizedBridge = true,
                CandidateFrameIsReadOnlyAckBeforeMutation = true,
            });

        Assert.True(ready.CanJustifyNextLiveBusinessProbe);
        Assert.Contains("single bounded live verification", ready.Reason, StringComparison.Ordinal);
        Assert.Contains("excluding Cmd_Open", ready.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MisclassifyingControlSessionVersionBlocksFurtherLiveWork()
    {
        var decision = MiPlayPostAuthRouteExclusionEvidence.EvaluateNextLiveBusinessProbe(
            MiPlayPostAuthRouteExclusionEvidence.CreateCurrentSnapshot() with
            {
                ControlSessionVersionFrameKeptSeparateFromFirmwareVersion = false,
            });

        Assert.False(decision.CanJustifyNextLiveBusinessProbe);
        Assert.Contains("2.1.5091615", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("1.94.13", decision.Reason, StringComparison.Ordinal);
    }
}