using System.Net;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Casting;
using DLNACast.Core.Dlna;
using DLNACast.Core.Models;
using DLNACast.Core.Streaming;

namespace DLNACast.Tests;

public sealed class CastCoordinatorTests
{
    [Fact]
    public async Task FallsBackToMp3WhenRendererRejectsWaveAndKeepsStreaming()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var controller = new FakeRendererController(rejectWave: true);
        var streams = new FakeStreamServer();
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(), controller, streams, new FakeLocalOutputManager());
        var transitions = new List<CastSessionState>();
        coordinator.DiagnosticsChanged += (_, diagnostics) => transitions.Add(diagnostics.State);

        await coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            new CaptureSelection.SystemMix("fake", "Fake system mix"),
            allowMp3Fallback: true,
            timeout.Token);

        Assert.Equal(CastSessionState.Streaming, coordinator.Diagnostics.State);
        Assert.Equal(StreamProfile.Mp3Cbr320, coordinator.Diagnostics.Profile);
        Assert.Equal([StreamProfile.PcmWave, StreamProfile.Mp3Cbr320], controller.TransportUris);
        Assert.Contains(CastSessionState.Recovering, transitions);
    }

    [Fact]
    public async Task RejectingSecondStartDoesNotTearDownActiveSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var controller = new FakeRendererController(rejectWave: false);
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(), controller, new FakeStreamServer(), new FakeLocalOutputManager());
        var renderer = CreateRenderer("http-get:*:*:*");
        var selection = new CaptureSelection.SystemMix("fake", "Fake system mix");
        await coordinator.StartAsync(renderer, selection, allowMp3Fallback: true, timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(renderer, selection, allowMp3Fallback: true, timeout.Token));

        Assert.True(coordinator.IsCasting);
        Assert.Equal(CastSessionState.Streaming, coordinator.Diagnostics.State);
        Assert.Equal(0, controller.StopCalls);
    }

    [Fact]
    public async Task MutesAfterCaptureStartsAndRestoresAfterCaptureStops()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var calls = new List<string>();
        var localOutputs = new FakeLocalOutputManager(calls);
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(calls),
            new FakeRendererController(rejectWave: false),
            new FakeStreamServer(),
            localOutputs);
        var selection = new CaptureSelection.SystemMix("fake", "Fake system mix");

        await coordinator.StartAsync(CreateRenderer("http-get:*:*:*"), selection, true, timeout.Token);

        Assert.Equal(["capture-start", "mute"], calls.Take(2));
        Assert.Equal(selection, localOutputs.MutedSelection);
        Assert.False(localOutputs.Restored);

        await coordinator.StopAsync();

        Assert.True(localOutputs.Restored);
        Assert.True(calls.IndexOf("capture-dispose") < calls.IndexOf("restore"));
    }

    [Fact]
    public async Task RestoresLocalOutputWhenCastStartupFails()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var localOutputs = new FakeLocalOutputManager();
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(),
            new FakeRendererController(rejectWave: true),
            new FakeStreamServer(),
            localOutputs);

        await Assert.ThrowsAsync<UpnpException>(() => coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            new CaptureSelection.SystemMix("fake", "Fake system mix"),
            allowMp3Fallback: false,
            timeout.Token));

        Assert.True(localOutputs.Restored);
    }

    private static RendererDevice CreateRenderer(string sinkProtocolInfo)
    {
        var endpoint = new Uri("http://127.0.0.1:1400/control");
        var service = new UpnpServiceEndpoint("urn:schemas-upnp-org:service:AVTransport:1", endpoint, endpoint, endpoint);
        return new RendererDevice("uuid:fake", "Fake Renderer", "Tests", "Renderer", IPAddress.Loopback,
            endpoint, service, service, service, sinkProtocolInfo);
    }

    private sealed class FakeAudioCatalog(List<string>? calls = null) : IAudioSourceCatalog
    {
        public IReadOnlyList<AudioSourceItem> GetOutputDevices() => [];
        public IReadOnlyList<AudioSourceItem> GetCandidateProcesses() => [];
        public IAudioCaptureSource CreateCapture(CaptureSelection selection) => new FakeCapture(selection, calls);
    }

    private sealed class FakeCapture(CaptureSelection selection, List<string>? calls) : IAudioCaptureSource
    {
        public CaptureSelection Selection { get; } = selection;
        public bool IsRunning { get; private set; }
        public CaptureHealth Health { get; } = new(DateTimeOffset.UtcNow, null, null, 0);
        public event EventHandler<Exception>? CaptureFailed
        {
            add { }
            remove { }
        }

        public Task StartAsync(PcmFrameBuffer destination, CancellationToken cancellationToken)
        {
            calls?.Add("capture-start");
            IsRunning = true;
            destination.Write(new byte[PcmFrameBuffer.BytesPerFrame]);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            calls?.Add("capture-dispose");
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLocalOutputManager(List<string>? calls = null) : ILocalOutputManager
    {
        public CaptureSelection? MutedSelection { get; private set; }
        public bool Restored { get; private set; }

        public ValueTask<IAsyncDisposable> MuteForCastAsync(
            CaptureSelection selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls?.Add("mute");
            MutedSelection = selection;
            return ValueTask.FromResult<IAsyncDisposable>(new RestoreLease(this, calls));
        }

        private sealed class RestoreLease(FakeLocalOutputManager owner, List<string>? calls) : IAsyncDisposable
        {
            private int _restored;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _restored, 1) == 0)
                {
                    calls?.Add("restore");
                    owner.Restored = true;
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeStreamServer : ILiveStreamServer
    {
        public Task<LiveStreamSession> StartSessionAsync(
            RendererDevice renderer,
            PcmFrameBuffer frames,
            StreamProfile profile,
            CancellationToken cancellationToken)
        {
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var session = new LiveStreamSession(
                new Uri($"http://127.0.0.1:49555/stream/{profile}.bin"),
                profile,
                lifetime,
                () =>
                {
                    lifetime.Cancel();
                    lifetime.Dispose();
                    return ValueTask.CompletedTask;
                });
            session.MarkClientConnected();
            return Task.FromResult(session);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRendererController(bool rejectWave) : IRendererController
    {
        public List<StreamProfile> TransportUris { get; } = [];
        public int StopCalls { get; private set; }

        public Task<string> GetSinkProtocolInfoAsync(RendererDevice device, CancellationToken cancellationToken) =>
            Task.FromResult(device.SinkProtocolInfo);

        public Task SetTransportUriAsync(RendererDevice device, Uri streamUri, StreamProfile profile, CancellationToken cancellationToken)
        {
            TransportUris.Add(profile);
            if (rejectWave && profile == StreamProfile.PcmWave)
            {
                throw new UpnpException(714, "Unsupported MIME type");
            }
            return Task.CompletedTask;
        }

        public Task PlayAsync(RendererDevice device, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(RendererDevice device, CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<TransportStatus> GetTransportStatusAsync(RendererDevice device, CancellationToken cancellationToken) =>
            Task.FromResult(new TransportStatus("PLAYING", "OK"));

        public Task<int?> GetVolumeAsync(RendererDevice device, CancellationToken cancellationToken) => Task.FromResult<int?>(30);
        public Task SetVolumeAsync(RendererDevice device, int volume, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
