using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealLegacyPlaybackSessionEvidenceTests
{
    [Fact]
    public void RebuildsCapturedSetPlaySourceAndOpenFramesByteForByte()
    {
        var snapshot = MiPlayRealLegacyPlaybackSessionEvidence.CreateCurrentSnapshot();

        Assert.True(MiPlayRealLegacyPlaybackSessionEvidence.MatchesPinnedHashes(snapshot));
        Assert.Equal(101, snapshot.SetPlaySourceFrame.Length);
        Assert.Equal(47, snapshot.OpenFrame.Length);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            snapshot.SetPlaySourceFrame,
            out var setPlaySource,
            out var setPlaySourceConsumed));
        Assert.NotNull(setPlaySource);
        Assert.Equal(snapshot.SetPlaySourceFrame.Length, setPlaySourceConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, setPlaySource.Command);
        Assert.Equal((ushort)0x00bb, setPlaySource.Sequence);
        Assert.Equal(snapshot.SetPlaySourceJson, Encoding.UTF8.GetString(setPlaySource.Payload));

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            snapshot.OpenFrame,
            out var open,
            out var openConsumed));
        Assert.NotNull(open);
        Assert.Equal(snapshot.OpenFrame.Length, openConsumed);
        Assert.Equal(MiPlayProtocolConstants.OpenDeviceCommand, open.Command);
        Assert.Equal((ushort)0x00bc, open.Sequence);
        Assert.Equal(
            MiPlayRealLegacyPlaybackSessionEvidence.OpenPayloadText + "\0",
            Encoding.UTF8.GetString(open.Payload));
    }

    [Fact]
    public void PinsOfficialLegacyPlaybackAndReverseRtspOrder()
    {
        var snapshot = MiPlayRealLegacyPlaybackSessionEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.UsesLegacyClearControl);
        Assert.True(snapshot.SetPlaySourceWasBroadcastToBothReceivers);
        Assert.True(snapshot.OpenWasSentOnlyToSelectedReceiver);
        Assert.False(snapshot.SetPlaySourceAcknowledgementObserved);
        Assert.False(snapshot.OpenAcknowledgementObserved);
        Assert.False(snapshot.AddMirrorObserved);
        Assert.True(snapshot.ReceiverOpenedReverseRtsp);
        Assert.True(snapshot.UsesUdpTimerResponder);
        Assert.True(snapshot.UsesSeparateTcpAudioChannel);
        Assert.True(snapshot.UsesAacMpegTsRtp);
        Assert.False(snapshot.ContainsCapturedMediaBytes);
        Assert.False(snapshot.SafeForNetworkUse);

        Assert.Equal(16, snapshot.InitialRtspSteps.Count);
        Assert.Equal(
            [
                "OPTIONS", "RTSP/1.0", "OPTIONS", "RTSP/1.0", "GET_PARAMETER", "RTSP/1.0",
                "SET_PARAMETER", "RTSP/1.0", "SET_PARAMETER", "RTSP/1.0", "SETUP", "RTSP/1.0",
                "PLAY", "RTSP/1.0", "TIME_OFFSET", "RTSP/1.0",
            ],
            snapshot.InitialRtspSteps.Select(step => step.StartLine.Split(' ')[0]));

        Assert.Contains("AAC 00000001 00", MiPlayRealLegacyPlaybackSessionEvidence.ReceiverAudioCapabilities);
        Assert.Contains("wfd_video_formats: none", MiPlayRealLegacyPlaybackSessionEvidence.ReceiverAudioCapabilities);
        Assert.Contains("RTP/AVP/TCP;interleaved mode=play", MiPlayRealLegacyPlaybackSessionEvidence.SourceSelectedParameters);
    }
}
