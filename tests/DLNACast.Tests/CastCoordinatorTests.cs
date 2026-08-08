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
            muteLocalOutput: true,
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
        await coordinator.StartAsync(renderer, selection, allowMp3Fallback: true, muteLocalOutput: true, timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(renderer, selection, allowMp3Fallback: true, muteLocalOutput: true, timeout.Token));

        Assert.True(coordinator.IsCasting);
        Assert.Equal(CastSessionState.Streaming, coordinator.Diagnostics.State);
        Assert.Equal(0, controller.StopCalls);
    }

    [Fact]
    public async Task RoutesBeforeCaptureStartsAndRestoresAfterCaptureStops()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var calls = new List<string>();
        var routedSelection = new CaptureSelection.SystemMix("virtual", "Virtual speaker");
        var localOutputs = new FakeLocalOutputManager(calls, routedSelection);
        var audioCatalog = new FakeAudioCatalog(calls);
        await using var coordinator = new CastCoordinator(
            audioCatalog,
            new FakeRendererController(rejectWave: false),
            new FakeStreamServer(),
            localOutputs);
        var selection = new CaptureSelection.SystemMix("fake", "Fake system mix");

        await coordinator.StartAsync(CreateRenderer("http-get:*:*:*"), selection, true, true, timeout.Token);

        Assert.Equal(["route", "capture-start"], calls.Take(2));
        Assert.Equal(selection, localOutputs.OriginalSelection);
        Assert.Equal(routedSelection, audioCatalog.CreatedSelection);
        Assert.False(localOutputs.Restored);

        await coordinator.StopAsync();

        Assert.True(localOutputs.Restored);
        Assert.True(calls.IndexOf("capture-dispose") < calls.IndexOf("restore"));
    }

    [Fact]
    public async Task KeepsLocalOutputPlayingWhenMuteIsDisabled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var localOutputs = new FakeLocalOutputManager();
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(),
            new FakeRendererController(rejectWave: false),
            new FakeStreamServer(),
            localOutputs);

        await coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            new CaptureSelection.SystemMix("fake", "Fake system mix"),
            allowMp3Fallback: true,
            muteLocalOutput: false,
            timeout.Token);

        Assert.Null(localOutputs.OriginalSelection);
        Assert.False(localOutputs.Restored);
    }

    [Fact]
    public async Task SwitchesLocalPlaybackWithoutStoppingTheDlnaSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var calls = new List<string>();
        var original = new CaptureSelection.SystemMix("physical", "Physical output");
        var routed = new CaptureSelection.SystemMix("virtual", "Virtual output");
        var controller = new FakeRendererController(rejectWave: false);
        var audioCatalog = new FakeAudioCatalog(calls);
        var localOutputs = new FakeLocalOutputManager(calls, routed);
        await using var coordinator = new CastCoordinator(
            audioCatalog,
            controller,
            new FakeStreamServer(),
            localOutputs);
        await coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            original,
            allowMp3Fallback: true,
            muteLocalOutput: false,
            timeout.Token);

        await coordinator.SetMuteLocalOutputAsync(true, timeout.Token);

        Assert.True(coordinator.IsCasting);
        Assert.Equal(0, controller.StopCalls);
        Assert.Equal(routed, audioCatalog.CreatedSelection);
        Assert.False(localOutputs.Restored);

        await coordinator.SetMuteLocalOutputAsync(false, timeout.Token);

        Assert.True(coordinator.IsCasting);
        Assert.Equal(0, controller.StopCalls);
        Assert.Equal(original, audioCatalog.CreatedSelection);
        Assert.True(localOutputs.Restored);
    }

    [Fact]
    public async Task ProcessCaptureChangesRoutingWithoutReplacingTheCapture()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var calls = new List<string>();
        var selection = new CaptureSelection.Process(42, "Player", true);
        var controller = new FakeRendererController(rejectWave: false);
        await using var coordinator = new CastCoordinator(
            new FakeAudioCatalog(calls),
            controller,
            new FakeStreamServer(),
            new FakeLocalOutputManager(calls, selection));
        await coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            selection,
            allowMp3Fallback: true,
            muteLocalOutput: false,
            timeout.Token);

        await coordinator.SetMuteLocalOutputAsync(true, timeout.Token);
        await coordinator.SetMuteLocalOutputAsync(false, timeout.Token);

        Assert.Equal(1, calls.Count(call => call == "capture-start"));
        Assert.Equal(0, controller.StopCalls);
        Assert.True(coordinator.IsCasting);
    }

    [Fact]
    public async Task SwitchesCaptureSourceWithoutStoppingTheDlnaSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var calls = new List<string>();
        var original = new CaptureSelection.SystemMix("physical-a", "Physical A");
        var replacement = new CaptureSelection.SystemMix("physical-b", "Physical B");
        var controller = new FakeRendererController(rejectWave: false);
        var audioCatalog = new FakeAudioCatalog(calls);
        await using var coordinator = new CastCoordinator(
            audioCatalog,
            controller,
            new FakeStreamServer(),
            new FakeLocalOutputManager(calls));
        await coordinator.StartAsync(
            CreateRenderer("http-get:*:*:*"),
            original,
            allowMp3Fallback: true,
            muteLocalOutput: false,
            timeout.Token);

        await coordinator.SetCaptureSelectionAsync(replacement, replacement, timeout.Token);

        Assert.Equal(replacement, audioCatalog.CreatedSelection);
        Assert.Equal(0, controller.StopCalls);
        Assert.True(coordinator.IsCasting);
        Assert.Equal(CastSessionState.Streaming, coordinator.Diagnostics.State);
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
            muteLocalOutput: true,
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
        public CaptureSelection? CreatedSelection { get; private set; }
        public IReadOnlyList<AudioSourceItem> GetOutputDevices() => [];
        public IReadOnlyList<AudioSourceItem> GetCandidateProcesses() => [];
        public IAudioCaptureSource CreateCapture(CaptureSelection selection)
        {
            CreatedSelection = selection;
            return new FakeCapture(selection, calls);
        }
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

    private sealed class FakeLocalOutputManager(
        List<string>? calls = null,
        CaptureSelection? captureSelection = null) : ILocalOutputManager
    {
        public CaptureSelection? OriginalSelection { get; private set; }
        public bool Restored { get; private set; }

        public ValueTask<ILocalOutputLease> RouteForCastAsync(
            CaptureSelection selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls?.Add("route");
            OriginalSelection = selection;
            return ValueTask.FromResult<ILocalOutputLease>(
                new RestoreLease(this, calls, captureSelection ?? selection));
        }

        private sealed class RestoreLease(
            FakeLocalOutputManager owner,
            List<string>? calls,
            CaptureSelection captureSelection) : ILocalOutputLease
        {
            private int _restored;
            public CaptureSelection CaptureSelection { get; } = captureSelection;

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
