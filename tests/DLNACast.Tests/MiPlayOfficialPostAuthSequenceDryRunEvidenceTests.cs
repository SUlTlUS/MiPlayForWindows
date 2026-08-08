using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayOfficialPostAuthSequenceDryRunEvidenceTests
{
    [Fact]
    public void DryRunComputesRecoveredOfficialSequenceSafetyDataLengths()
    {
        var snapshot = MiPlayOfficialPostAuthSequenceDryRunEvidence.CreateCurrentSnapshot();

        Assert.Equal((ushort)0x0004, snapshot.FirstCommandSequence);
        Assert.True(snapshot.UsesRecoveredOfficialSourceIdentity);
        Assert.Equal(80, snapshot.RecoveredOfficialFirstPlaintextLength);
        Assert.Equal(105, snapshot.RecoveredOfficialFirstSafetyDataPayloadLength);
        Assert.Equal(73, snapshot.PreviousDefaultWindowsFirstSafetyDataPayloadLength);
        Assert.True(snapshot.FirstFrameMatchesRecoveredPhonePcapLength);
        Assert.True(snapshot.PreviousDefaultWindowsFirstFrameWasRejectedLive);
        Assert.False(snapshot.SafeForNetworkUse);

        Assert.Collection(
            snapshot.Steps,
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendSourceName, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0004, step.Sequence);
                Assert.Equal(80, step.PlaintextPayloadLength);
                Assert.Equal(105, step.SafetyDataPayloadLength);
                Assert.False(step.AcknowledgementGate);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0005, step.Sequence);
                Assert.Equal(0, step.PlaintextPayloadLength);
                Assert.Equal(25, step.SafetyDataPayloadLength);
                Assert.True(step.AcknowledgementGate);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendCanAlonePlayCtrl, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0006, step.Sequence);
                Assert.Equal(24, step.PlaintextPayloadLength);
                Assert.Equal(41, step.SafetyDataPayloadLength);
                Assert.False(step.AcknowledgementGate);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendAlonePlayCapacity, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, step.Command);
                Assert.Equal((ushort)0x0007, step.Sequence);
                Assert.Equal(25, step.PlaintextPayloadLength);
                Assert.Equal(41, step.SafetyDataPayloadLength);
                Assert.False(step.AcknowledgementGate);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.GetMirrorModeCommand, step.Command);
                Assert.Equal((ushort)0x0008, step.Sequence);
                Assert.Equal(0, step.PlaintextPayloadLength);
                Assert.Equal(25, step.SafetyDataPayloadLength);
                Assert.True(step.AcknowledgementGate);
            },
            step =>
            {
                Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource, step.Kind);
                Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, step.Command);
                Assert.Equal((ushort)0x0009, step.Sequence);
                Assert.Equal(85, step.PlaintextPayloadLength);
                Assert.Equal(105, step.SafetyDataPayloadLength);
                Assert.False(step.AcknowledgementGate);
            });
    }

    [Fact]
    public void DryRunDecisionNarrowsCandidateButDoesNotAuthorizeNetworkSend()
    {
        var decision = MiPlayOfficialPostAuthSequenceDryRunEvidence.Evaluate(
            MiPlayOfficialPostAuthSequenceDryRunEvidence.CreateCurrentSnapshot());

        Assert.True(decision.PreparedRecoveredOfficialFirstFrame);
        Assert.False(decision.AuthorizesNetworkSend);
        Assert.Contains("80-byte plaintext / 105-byte SafetyData", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("73-byte default Windows", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("also a live negative now", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize an S12 send", decision.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(15, 25)]
    [InlineData(16, 41)]
    [InlineData(51, 73)]
    [InlineData(76, 89)]
    [InlineData(80, 105)]
    [InlineData(85, 105)]
    public void SafetyDataLengthFormulaAddsNativeZeroPadding(int plaintextLength, int expectedSafetyDataLength)
    {
        Assert.Equal(
            expectedSafetyDataLength,
            MiPlayOfficialPostAuthSequenceDryRunEvidence.ComputeSafetyDataVersion1PayloadLength(plaintextLength));
    }
}
