using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyPostOpenPlaybackLiveValidationEvidenceTests
{
    [Fact]
    public void PinsTheSingleResumeStateTwoBreakthrough()
    {
        var snapshot = MiPlayLegacyPostOpenPlaybackLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.4", snapshot.ReceiverAddress);
        Assert.Equal((ushort)0x0490, snapshot.ReceiverChallengeSequence);
        Assert.Equal([50504, 50508, 50510], snapshot.ReceiverReverseTcpSourcePorts);
        Assert.Equal(51697, snapshot.ReceiverTimerSourcePort);
        Assert.Equal(1_409_295_729_837UL, snapshot.TimeOffsetMicroseconds);
        Assert.Equal(6_577_441_397UL, snapshot.InitialProgramClockReference90Khz);
        Assert.Equal(938, snapshot.MediaAccessUnitCount);
        Assert.Equal(549_492, snapshot.MediaWireBytes);
        Assert.Equal(178, snapshot.SetMediaInfoPayloadLength);
        Assert.True(snapshot.FirstAudioPcmObserved);
        Assert.True(snapshot.StartupHeartbeatAcknowledged);
        Assert.True(snapshot.MediaInfoEchoObserved);
        Assert.True(snapshot.ReceiverStateThreeObserved);
        Assert.True(snapshot.ReceiverStateTwoObservedAfterResume);
        Assert.Equal(1, snapshot.ResumeFrameCount);
        Assert.True(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesReceiverPlaybackStateReached(snapshot));
    }

    [Fact]
    public void KeepsProtocolStateProofSeparateFromHumanAudibility()
    {
        var snapshot = MiPlayLegacyPostOpenPlaybackLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Null(snapshot.UserConfirmedAudibleAtReceiver);
        Assert.False(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesAudiblePlayback(snapshot));
        Assert.True(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesAudiblePlayback(snapshot with { UserConfirmedAudibleAtReceiver = true }));
    }

    [Fact]
    public void RejectsExpandedOrIncompleteControlLedgers()
    {
        var snapshot = MiPlayLegacyPostOpenPlaybackLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.False(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesReceiverPlaybackStateReached(snapshot with { AddMirrorSent = true }));
        Assert.False(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesReceiverPlaybackStateReached(snapshot with { ResumeFrameCount = 2 }));
        Assert.False(MiPlayLegacyPostOpenPlaybackLiveValidationEvidence
            .ProvesReceiverPlaybackStateReached(snapshot with { ReceiverStateTwoObservedAfterResume = false }));
    }
}
