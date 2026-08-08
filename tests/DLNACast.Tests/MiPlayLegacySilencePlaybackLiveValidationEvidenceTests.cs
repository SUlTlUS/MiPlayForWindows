using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacySilencePlaybackLiveValidationEvidenceTests
{
    [Fact]
    public void PinsAcceptedControlAndReverseSessionWithoutOverclaimingAudibility()
    {
        var snapshot = MiPlayLegacySilencePlaybackLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.4", snapshot.ReceiverAddress);
        Assert.Equal("192.168.10.9", snapshot.SourceAddress);
        Assert.Equal("1.94.13", snapshot.ReceiverFirmwareVersion);
        Assert.Equal((ushort)0x03bc, snapshot.ReceiverChallengeSequence);
        Assert.Equal(9, snapshot.BootstrapFrameCount);
        Assert.Equal(7, snapshot.PlaybackContinuationFrameCount);
        Assert.Equal(415, snapshot.DeviceInfoPayloadLength);
        Assert.Equal([50256, 50260, 50262], snapshot.ReceiverReverseTcpSourcePorts);
        Assert.Equal(34994, snapshot.ReceiverTimerSourcePort);

        Assert.True(snapshot.BootstrapAccepted);
        Assert.True(snapshot.PlaybackContinuationAccepted);
        Assert.True(snapshot.ReverseRtspReachedReady);
        Assert.True(snapshot.TimerExchangeObserved);
        Assert.True(snapshot.MediaWriteCompleted);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.PauseOrResumeSent);
        Assert.False(snapshot.UserAudioSent);
        Assert.False(snapshot.RetryOrFallbackUsed);
        Assert.True(snapshot.ProvesWindowsSourceTransportAccepted);
        Assert.False(snapshot.ProvesAudibleUserAudio);
        Assert.True(MiPlayLegacySilencePlaybackLiveValidationEvidence
            .MatchesPinnedControlFrameHashes(snapshot));
    }

    [Fact]
    public void RebuildsTheExactBoundedSilenceMediaLedger()
    {
        var media = MiPlayLegacySilencePlaybackLiveValidationEvidence.ReconstructMediaSummary();

        Assert.Equal(48, media.AccessUnitCount);
        Assert.Equal(9, media.ProgramTablePacketCount);
        Assert.Equal(39, media.SteadyPacketCount);
        Assert.Equal(14_868, media.WireBytes);
        Assert.Equal(1_024, media.DurationMilliseconds);
        Assert.Equal([0, 10, 15, 20, 25, 30, 35, 40, 45], media.ProgramTableAccessUnitIndexes);
    }

    [Fact]
    public void ReconstructsTheTwoPlaybackControlFramesForTheWindowsEndpoint()
    {
        var snapshot = MiPlayLegacySilencePlaybackLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            snapshot.SetPlaySourceFrame,
            out var setPlaySource,
            out var setPlaySourceConsumed));
        Assert.NotNull(setPlaySource);
        Assert.Equal(snapshot.SetPlaySourceFrame.Length, setPlaySourceConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, setPlaySource.Command);
        Assert.Equal((ushort)13, setPlaySource.Sequence);
        Assert.Equal(
            MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceJson,
            Encoding.UTF8.GetString(setPlaySource.Payload));

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            snapshot.OpenFrame,
            out var open,
            out var openConsumed));
        Assert.NotNull(open);
        Assert.Equal(snapshot.OpenFrame.Length, openConsumed);
        Assert.Equal(MiPlayProtocolConstants.OpenDeviceCommand, open.Command);
        Assert.Equal((ushort)14, open.Sequence);
        Assert.Equal(
            "wfd://192.168.10.9:7274?mirrorMode=1\0",
            Encoding.UTF8.GetString(open.Payload));
    }
}
