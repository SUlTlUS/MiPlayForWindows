using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureDelayedSafetyDataSetPlaySourceRun()
    {
        Assert.Equal("192.168.10.4", MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(1_734, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("2.1.5091615", MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal((ushort)0x0004, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.SetPlaySourceSequence);
        Assert.Equal(0, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.PlaintextPayloadLength);
        Assert.Equal(25, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength);
        Assert.Equal(500, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.PostAuthSendDelayMilliseconds);
        Assert.Equal(7, MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.SelectedSafetyDataCandidate);
    }

    [Fact]
    public void DecisionRulesOutImmediatePostAuthTimingRace()
    {
        var decision = MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.EvaluateAckResult(
            MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.DispatcherAckVerified);
        Assert.Contains("500 ms", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without 0x0041", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not explained by an immediate", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesDelayedSafetyDataEvidence()
    {
        var unsafeSnapshot = MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            CmdOpenSent = true
        };

        var decision = MiPlaySetPlaySourceAckDelayedSafetyDataLiveValidationEvidence.EvaluateAckResult(unsafeSnapshot);

        Assert.False(decision.DispatcherAckVerified);
        Assert.Contains("boundary", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
