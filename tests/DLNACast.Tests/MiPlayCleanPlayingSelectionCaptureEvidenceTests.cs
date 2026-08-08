using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayCleanPlayingSelectionCaptureEvidenceTests
{
    [Fact]
    public void PinsTheCleanAutomaticSelectionBoundary()
    {
        var snapshot = MiPlayCleanPlayingSelectionCaptureEvidence.CreateCurrentSnapshot();
        var decision = MiPlayCleanPlayingSelectionCaptureEvidence.Evaluate(snapshot);

        Assert.Equal(0, snapshot.PauseCommandCount);
        Assert.Equal(0, snapshot.ResumeCommandCount);
        Assert.Equal((ushort)0x0099, snapshot.SetMediaInfoSequence);
        Assert.Equal((ushort)0x009a, snapshot.FirstPeriodicHeartbeatSequence);
        Assert.Equal(180, snapshot.SetMediaInfoPayloadLength);
        Assert.Equal(0, snapshot.Status);
        Assert.Equal(2, snapshot.DeviceState);
        Assert.Equal(2, snapshot.ReceiverPlayingState);
        Assert.InRange(snapshot.HeartbeatIntervalAcrossOpenMilliseconds, 4_999, 5_001);
        Assert.True(decision.ProvesAutomaticSelectionHasNoPauseOrResume);
        Assert.True(decision.ProvesPlayingDeviceStateTwo);
        Assert.True(decision.ProvesHeartbeatTimerContinuesAcrossOpen);
        Assert.True(decision.SupportsCorrectedWindowsStartupModel);
    }
}
