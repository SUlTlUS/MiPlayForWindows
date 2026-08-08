using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacySystemAudioLiveValidationEvidenceTests
{
    [Fact]
    public void PinsTheSuccessfulBoundedSystemLoopbackTransport()
    {
        var snapshot = MiPlayLegacySystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.4", snapshot.ReceiverAddress);
        Assert.Equal("192.168.10.9", snapshot.SourceAddress);
        Assert.Equal("1.94.13", snapshot.ReceiverFirmwareVersion);
        Assert.Equal((ushort)0x03ea, snapshot.ReceiverChallengeSequence);
        Assert.Equal("扬声器 (Realtek(R) Audio)", snapshot.WindowsOutputEndpoint);
        Assert.Equal(192_000, snapshot.AacBitRate);
        Assert.Equal(240, snapshot.MediaAccessUnitCount);
        Assert.Equal(178_868, snapshot.MediaWireBytes);
        Assert.Equal(5_120, snapshot.MediaDurationMilliseconds);
        Assert.Equal([50306, 50310, 50312], snapshot.ReceiverReverseTcpSourcePorts);
        Assert.Equal(50639, snapshot.ReceiverTimerSourcePort);
        Assert.Equal(0, snapshot.CaptureUnderruns);
        Assert.Equal(3, snapshot.CaptureOverruns);
        Assert.True(snapshot.LocalTestToneInjected);
        Assert.True(MiPlayLegacySystemAudioLiveValidationEvidence
            .IsSuccessfulBoundedTransport(snapshot));
    }

    [Fact]
    public void SeparatesTransportProofFromHumanAudibilityConfirmation()
    {
        var snapshot = MiPlayLegacySystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.ProvesSystemAudioTransportAccepted);
        Assert.False(snapshot.UserConfirmedAudibleAtReceiver);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.PauseOrResumeSent);
        Assert.False(snapshot.RetryOrFallbackUsed);
    }

    [Fact]
    public void RejectsAnIncompleteOrExpandedLedger()
    {
        var snapshot = MiPlayLegacySystemAudioLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.False(MiPlayLegacySystemAudioLiveValidationEvidence
            .IsSuccessfulBoundedTransport(snapshot with { MediaWriteCompleted = false }));
        Assert.False(MiPlayLegacySystemAudioLiveValidationEvidence
            .IsSuccessfulBoundedTransport(snapshot with { AddMirrorSent = true }));
        Assert.False(MiPlayLegacySystemAudioLiveValidationEvidence
            .IsSuccessfulBoundedTransport(snapshot with { CaptureUnderruns = 1 }));
    }
}
