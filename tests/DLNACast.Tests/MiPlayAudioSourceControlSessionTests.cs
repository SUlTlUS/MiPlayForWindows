using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayAudioSourceControlSessionTests
{
    private const ushort FirstSequence = 4;

    [Fact]
    public void InitialBatchModelsPhoneSourcePrefixButRemainsOffline()
    {
        var session = MiPlayAudioSourceControlSession.CreateRecoveredCaptureComparisonSession(FirstSequence);

        var result = session.CreateInitialOfflineBatch();

        Assert.True(result.Accepted);
        Assert.False(result.Completed);
        Assert.Equal(MiPlayAudioSourceControlPhase.AwaitingDeviceInfoAcknowledgement, result.Phase);
        Assert.Equal(
            [
                MiPlayOfficialPostAuthSequenceStepKind.SendSourceName,
                MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo,
                MiPlayOfficialPostAuthSequenceStepKind.SendCanAlonePlayCtrl,
                MiPlayOfficialPostAuthSequenceStepKind.SendAlonePlayCapacity,
            ],
            result.OutboundPlaintextSteps.Select(step => step.Kind));
        Assert.Equal([4, 5, 6, 7], result.OutboundPlaintextSteps.Select(step => (int)step.Sequence));
        Assert.False(result.SafeForNetworkUse);
        Assert.False(result.AllowsOpenAddMirrorRtspOrMedia);
    }

    [Fact]
    public void ValidDeviceInfoAndMirrorModeProduce0034Then0040AndStopBeforeMedia()
    {
        var session = MiPlayAudioSourceControlSession.CreateRecoveredCaptureComparisonSession(FirstSequence);
        Assert.True(session.CreateInitialOfflineBatch().Accepted);

        var deviceInfoPayload = MiPlayFreshLegacyReceiverBootstrapPlanner
            .CreateOfflinePlan(FirstSequence + 1)
            .DeviceInfoPayload;
        var deviceInfo = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            FirstSequence + 1,
            deviceInfoPayload);

        Assert.True(deviceInfo.Accepted);
        Assert.False(deviceInfo.Completed);
        var getMirrorMode = Assert.Single(deviceInfo.OutboundPlaintextSteps);
        Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode, getMirrorMode.Kind);
        Assert.Equal(MiPlayProtocolConstants.GetMirrorModeCommand, getMirrorMode.Command);
        Assert.Equal(FirstSequence + 4, getMirrorMode.Sequence);
        Assert.Empty(getMirrorMode.PlaintextPayload);

        var mirrorMode = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            FirstSequence + 4,
            [0, 0, 0, 0, 2]);

        Assert.True(mirrorMode.Accepted);
        Assert.True(mirrorMode.Completed);
        Assert.Equal(MiPlayAudioSourceControlPhase.ControlPrefixComplete, mirrorMode.Phase);
        var setPlaySource = Assert.Single(mirrorMode.OutboundPlaintextSteps);
        Assert.Equal(MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource, setPlaySource.Kind);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, setPlaySource.Command);
        Assert.Equal(FirstSequence + 5, setPlaySource.Sequence);
        Assert.False(mirrorMode.SafeForNetworkUse);
        Assert.False(mirrorMode.AllowsOpenAddMirrorRtspOrMedia);

        var afterCompletion = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand,
            FirstSequence + 5,
            []);
        Assert.False(afterCompletion.Accepted);
        Assert.Empty(afterCompletion.OutboundPlaintextSteps);
    }

    [Fact]
    public void Interleaved0059AcknowledgementsDoNotAdvanceRequiredGates()
    {
        var session = MiPlayAudioSourceControlSession.CreateRecoveredCaptureComparisonSession(FirstSequence);
        session.CreateInitialOfflineBatch();

        var sourceNameAck = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            FirstSequence,
            []);

        Assert.True(sourceNameAck.Accepted);
        Assert.Equal(MiPlayAudioSourceControlPhase.AwaitingDeviceInfoAcknowledgement, sourceNameAck.Phase);
        Assert.Empty(sourceNameAck.OutboundPlaintextSteps);

        var duplicate = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            FirstSequence,
            []);
        Assert.False(duplicate.Accepted);
        Assert.Equal(MiPlayAudioSourceControlPhase.Stopped, duplicate.Phase);
        Assert.False(duplicate.AllowsOpenAddMirrorRtspOrMedia);
    }

    [Theory]
    [InlineData(0x001f, FirstSequence + 2)]
    [InlineData(0x0035, FirstSequence + 1)]
    public void WrongCommandOrSequenceStopsWithoutOutbound(
        ushort command,
        ushort sequence)
    {
        var session = MiPlayAudioSourceControlSession.CreateRecoveredCaptureComparisonSession(FirstSequence);
        session.CreateInitialOfflineBatch();

        var result = session.ProcessInboundPlaintext(command, sequence, [0, 1, 2, 3]);

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayAudioSourceControlPhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundPlaintextSteps);
        Assert.False(result.SafeForNetworkUse);
    }

    [Fact]
    public void WrongMirrorModeStopsBefore0040()
    {
        var session = MiPlayAudioSourceControlSession.CreateRecoveredCaptureComparisonSession(FirstSequence);
        session.CreateInitialOfflineBatch();
        var deviceInfoPayload = MiPlayFreshLegacyReceiverBootstrapPlanner
            .CreateOfflinePlan(FirstSequence + 1)
            .DeviceInfoPayload;
        Assert.True(session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            FirstSequence + 1,
            deviceInfoPayload).Accepted);

        var result = session.ProcessInboundPlaintext(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            FirstSequence + 4,
            [0, 0, 0, 0, 1]);

        Assert.False(result.Accepted);
        Assert.Empty(result.OutboundPlaintextSteps);
        Assert.Contains("mirrorMode=2", result.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidStepOrderIsRejected()
    {
        var steps = MiPlayOfficialPostAuthSequenceProbePlan.CreateSteps(FirstSequence).Reverse().ToArray();

        Assert.Throws<ArgumentException>(() => new MiPlayAudioSourceControlSession(steps));
    }
}
