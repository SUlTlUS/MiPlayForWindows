using System.Net;
using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;
using DLNACast.Core.Models;

namespace DLNACast.Tests;

public sealed class MiPlayCastContractsTests
{
    [Fact]
    public void DiagnosticsExposeTheReceiverPlaybackState()
    {
        var playing = new MiPlayCastDiagnostics(
            MiPlayCastState.Streaming,
            "streaming",
            ReceiverPaused: false);
        var paused = playing with { ReceiverPaused = true };

        Assert.False(playing.ReceiverPaused);
        Assert.True(paused.ReceiverPaused);
    }

    [Fact]
    public void AcceptsSystemMixAndProcessCaptureWithoutADurationLimit()
    {
        var systemRequest = CreateRequest(new CaptureSelection.SystemMix("default", "Default output"));
        var processRequest = CreateRequest(new CaptureSelection.Process(42, "Player", true));

        systemRequest.Validate();
        processRequest.Validate();

        Assert.Throws<ArgumentException>(() => (CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output")) with
        {
            Renderer = CreateRenderer(IPAddress.Loopback),
        }).Validate());
    }

    [Fact]
    public void CarriesAValidatedPerSpeakerChannelRoute()
    {
        var request = CreateRequest(new CaptureSelection.SystemMix("default", "Default output")) with
        {
            ChannelRoute = AudioChannelRoute.RightAsMono,
        };

        request.Validate();
        Assert.Equal(AudioChannelRoute.RightAsMono, request.ChannelRoute);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (request with { ChannelRoute = (AudioChannelRoute)99 }).Validate());
    }

    [Fact]
    public void CarriesAValidatedPlaybackDelay()
    {
        var request = CreateRequest(new CaptureSelection.SystemMix("default", "Default output"));

        Assert.Equal(MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds, request.PlaybackDelayMicroseconds);
        (request with { PlaybackDelayMicroseconds = 650_000 }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (request with
            {
                PlaybackDelayMicroseconds = MiPlayProtocolConstants.MinimumConfigurablePlaybackDelayMicroseconds - 1,
            }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (request with
            {
                PlaybackDelayMicroseconds = MiPlayProtocolConstants.MaximumConfigurablePlaybackDelayMicroseconds + 1,
            }).Validate());
    }

    [Fact]
    public void CarriesWindowedMediaPacingDiagnostics()
    {
        var diagnostics = new MiPlayCastDiagnostics(
            MiPlayCastState.Streaming,
            "Streaming",
            MinimumMediaSendGapMilliseconds: 20.75,
            MaximumMediaSendGapMilliseconds: 47.5,
            LateMediaSends: 3,
            CatchUpMediaSends: 1,
            MaximumInterReceiverSendSkewMilliseconds: 2.25,
            ReceiverReportedLatencyMilliseconds: 469,
            ReceiverReportedRtpPacketCount: 98,
            ReceiverReportedBitrate: -1,
            MinimumEncodedAccessUnitAgeMilliseconds: 12.5,
            MaximumEncodedAccessUnitAgeMilliseconds: 34.75);

        Assert.Equal(20.75, diagnostics.MinimumMediaSendGapMilliseconds);
        Assert.Equal(47.5, diagnostics.MaximumMediaSendGapMilliseconds);
        Assert.Equal(3, diagnostics.LateMediaSends);
        Assert.Equal(1, diagnostics.CatchUpMediaSends);
        Assert.Equal(2.25, diagnostics.MaximumInterReceiverSendSkewMilliseconds);
        Assert.Equal(469, diagnostics.ReceiverReportedLatencyMilliseconds);
        Assert.Equal(98, diagnostics.ReceiverReportedRtpPacketCount);
        Assert.Equal(-1, diagnostics.ReceiverReportedBitrate);
        Assert.Equal(12.5, diagnostics.MinimumEncodedAccessUnitAgeMilliseconds);
        Assert.Equal(34.75, diagnostics.MaximumEncodedAccessUnitAgeMilliseconds);
    }

    internal static MiPlaySystemAudioRequest CreateRequest(CaptureSelection selection)
    {
        return new MiPlaySystemAudioRequest(
            CreateRenderer(IPAddress.Parse("192.168.10.3")),
            selection,
            "ffmpeg.exe");
    }

    private static RendererDevice CreateRenderer(IPAddress address)
    {
        var description = new Uri($"http://{address}/device.xml");
        var service = new UpnpServiceEndpoint("service", description, description, description);
        return new RendererDevice(
            "uuid:759c0613-5052-4a81-a189-ca76d3432438",
            "Bedroom XiaoAI Speaker Pro",
            "Xiaomi",
            "LX06",
            address,
            description,
            service,
            service,
            service,
            "");
    }
}
