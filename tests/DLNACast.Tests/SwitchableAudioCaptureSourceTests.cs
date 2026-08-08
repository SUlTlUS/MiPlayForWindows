using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Models;

namespace DLNACast.Tests;

public sealed class SwitchableAudioCaptureSourceTests
{
    [Fact]
    public async Task StartsReplacementBeforeDisposingPreviousCapture()
    {
        var calls = new List<string>();
        var original = new CaptureSelection.SystemMix("physical", "Physical output");
        var replacement = new CaptureSelection.SystemMix("virtual", "Virtual output");
        await using var buffer = new PcmFrameBuffer();
        await using var capture = new SwitchableAudioCaptureSource(new FakeCatalog(calls), original);

        await capture.StartAsync(buffer, CancellationToken.None);
        await capture.SwitchAsync(replacement);

        Assert.Equal(replacement, capture.Selection);
        Assert.True(capture.IsRunning);
        Assert.True(calls.IndexOf("start:virtual") < calls.IndexOf("stop:physical"));
        Assert.True(calls.IndexOf("stop:physical") < calls.IndexOf("dispose:physical"));
    }

    [Fact]
    public async Task KeepsPreviousCaptureWhenReplacementCannotStart()
    {
        var calls = new List<string>();
        var original = new CaptureSelection.SystemMix("physical", "Physical output");
        var failing = new CaptureSelection.SystemMix("failing", "Failing output");
        await using var buffer = new PcmFrameBuffer();
        await using var capture = new SwitchableAudioCaptureSource(
            new FakeCatalog(calls, "failing"),
            original);
        await capture.StartAsync(buffer, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => capture.SwitchAsync(failing));

        Assert.Equal(original, capture.Selection);
        Assert.True(capture.IsRunning);
        Assert.DoesNotContain("stop:physical", calls);
        Assert.Contains("dispose:failing", calls);
    }

    private sealed class FakeCatalog(List<string> calls, string? failingId = null) : IAudioSourceCatalog
    {
        public IReadOnlyList<AudioSourceItem> GetOutputDevices() => [];
        public IReadOnlyList<AudioSourceItem> GetCandidateProcesses() => [];

        public IAudioCaptureSource CreateCapture(CaptureSelection selection)
        {
            var id = ((CaptureSelection.SystemMix)selection).EndpointId;
            calls.Add($"create:{id}");
            return new FakeCapture(selection, id, calls, id == failingId);
        }
    }

    private sealed class FakeCapture(
        CaptureSelection selection,
        string id,
        List<string> calls,
        bool failStart) : IAudioCaptureSource
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
            calls.Add($"start:{id}");
            if (failStart) throw new IOException("capture start failed");
            IsRunning = true;
            destination.Write(new byte[PcmFrameBuffer.BytesPerFrame]);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            calls.Add($"stop:{id}");
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            calls.Add($"dispose:{id}");
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
