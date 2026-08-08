using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthFirstCommandCandidateMatrixEvidenceTests
{
    [Fact]
    public void MatrixKeepsLegacyClearSuccessSeparateFromPostAuthSafetyDataFailures()
    {
        var matrix = MiPlayPostAuthFirstCommandCandidateMatrixEvidence.CreateCurrentMatrix();
        var legacyClear = matrix.Single(candidate =>
            candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.LegacyClearGetDeviceInfoLabel);
        var postAuthGetDeviceInfo = matrix.Single(candidate =>
            candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.NativeNoResetGetDeviceInfoLabel);

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, legacyClear.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, legacyClear.AcknowledgementCommand);
        Assert.False(legacyClear.UsesSafetyData);
        Assert.True(legacyClear.LiveTestedOnS12);
        Assert.True(legacyClear.AcknowledgementObserved);
        Assert.False(legacyClear.AuthorizesNextFrame);

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, postAuthGetDeviceInfo.Command);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, postAuthGetDeviceInfo.AcknowledgementCommand);
        Assert.True(postAuthGetDeviceInfo.UsesSafetyData);
        Assert.True(postAuthGetDeviceInfo.LiveTestedOnS12);
        Assert.False(postAuthGetDeviceInfo.AcknowledgementObserved);
        Assert.True(postAuthGetDeviceInfo.DeviceClosedAfterFrame);
        Assert.Equal("native-no-reset-outbound-type2", postAuthGetDeviceInfo.CipherProfile);
    }

    [Fact]
    public void MatrixCapturesBothNativeNoResetFirstCommandNegativeResults()
    {
        var matrix = MiPlayPostAuthFirstCommandCandidateMatrixEvidence.CreateCurrentMatrix();
        var postAuthGetDeviceInfo = matrix.Single(candidate =>
            candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.NativeNoResetGetDeviceInfoLabel);
        var postAuthSetPlaySource = matrix.Single(candidate =>
            candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.NativeNoResetSetPlaySourceLabel);
        var postAuthDefaultIdentity0058 = matrix.Single(candidate =>
            candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.NativeNoResetDefaultIdentitySetLocalDeviceInfoLabel);

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, postAuthGetDeviceInfo.Command);
        Assert.Contains("no same-sequence 0x001f", postAuthGetDeviceInfo.Evidence, StringComparison.Ordinal);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, postAuthSetPlaySource.Command);
        Assert.Contains("no 0x0041", postAuthSetPlaySource.Evidence, StringComparison.Ordinal);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, postAuthDefaultIdentity0058.Command);
        Assert.Contains("default Windows source identity", postAuthDefaultIdentity0058.Evidence, StringComparison.Ordinal);
        Assert.Contains("no 0x0059", postAuthDefaultIdentity0058.Evidence, StringComparison.Ordinal);
        Assert.False(postAuthGetDeviceInfo.SafeForNetworkUse);
        Assert.False(postAuthSetPlaySource.SafeForNetworkUse);
        Assert.False(postAuthDefaultIdentity0058.SafeForNetworkUse);
        Assert.False(postAuthGetDeviceInfo.AuthorizesNextFrame);
        Assert.False(postAuthSetPlaySource.AuthorizesNextFrame);
        Assert.False(postAuthDefaultIdentity0058.AuthorizesNextFrame);
    }

    [Fact]
    public void MatrixCapturesRecoveredOfficialFirst0058AsLiveNegative()
    {
        var candidate = MiPlayPostAuthFirstCommandCandidateMatrixEvidence
            .CreateCurrentMatrix()
            .Single(candidate => candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.NativeNoResetRecoveredIdentitySetLocalDeviceInfoLabel);

        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, candidate.Command);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, candidate.AcknowledgementCommand);
        Assert.True(candidate.UsesSafetyData);
        Assert.True(candidate.LiveTestedOnS12);
        Assert.False(candidate.AcknowledgementObserved);
        Assert.True(candidate.DeviceClosedAfterFrame);
        Assert.False(candidate.SafeForNetworkUse);
        Assert.False(candidate.AuthorizesNextFrame);
        Assert.Contains("plaintext 80 bytes", candidate.Evidence, StringComparison.Ordinal);
        Assert.Contains("SafetyData 105 bytes", candidate.Evidence, StringComparison.Ordinal);
        Assert.Contains("no 0x0059", candidate.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void MatrixRetainsObservedInboundPromotionOnlyAsNegativeControl()
    {
        var candidate = MiPlayPostAuthFirstCommandCandidateMatrixEvidence
            .CreateCurrentMatrix()
            .Single(candidate => candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.ObservedInboundPromotedSetPlaySourceLabel);

        Assert.True(candidate.UsesSafetyData);
        Assert.True(candidate.LiveTestedOnS12);
        Assert.False(candidate.AcknowledgementObserved);
        Assert.True(candidate.DeviceClosedAfterFrame);
        Assert.Equal("observed-inbound-promoted-outbound-type1", candidate.CipherProfile);
        Assert.Contains("negative controls", candidate.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void ForkResetCandidateRemainsOfflineOnly()
    {
        var candidate = MiPlayPostAuthFirstCommandCandidateMatrixEvidence
            .CreateCurrentMatrix()
            .Single(candidate => candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.ForkResetGetDeviceInfoLabel);

        Assert.True(candidate.UsesSafetyData);
        Assert.False(candidate.LiveTestedOnS12);
        Assert.False(candidate.SafeForNetworkUse);
        Assert.False(candidate.AuthorizesNextFrame);
        Assert.Equal("post-auth-fork-native-selection", candidate.CipherProfile);
        Assert.Contains("offline-only", candidate.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentDecisionBlocksAnotherLiveProbeUntilOfficialVectorOrStateTransitionEvidence()
    {
        var decision = MiPlayPostAuthFirstCommandCandidateMatrixEvidence.EvaluateCurrent();

        Assert.False(decision.CanRepeatNativeNoResetReadOnlyGetDeviceInfo);
        Assert.False(decision.CanSendAnotherPostAuthCandidateNow);
        Assert.Contains("one accepted legacy clear-text 0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("rejected native-no-reset SafetyData", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("recovered official phone identity", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("outbound SafetyData cipher phase", decision.NextOfflineTarget, StringComparison.Ordinal);
        Assert.Contains("DealSafetyDone", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkingAnyCandidateNetworkSafeInvalidatesTheMatrix()
    {
        var matrix = MiPlayPostAuthFirstCommandCandidateMatrixEvidence.CreateCurrentMatrix()
            .Select(candidate => candidate.Label == MiPlayPostAuthFirstCommandCandidateMatrixEvidence.ForkResetGetDeviceInfoLabel
                ? candidate with { SafeForNetworkUse = true }
                : candidate)
            .ToArray();

        var decision = MiPlayPostAuthFirstCommandCandidateMatrixEvidence.Evaluate(matrix);

        Assert.False(decision.CanRepeatNativeNoResetReadOnlyGetDeviceInfo);
        Assert.False(decision.CanSendAnotherPostAuthCandidateNow);
        Assert.Contains("incorrectly marked network-safe", decision.Reason, StringComparison.Ordinal);
    }
}
