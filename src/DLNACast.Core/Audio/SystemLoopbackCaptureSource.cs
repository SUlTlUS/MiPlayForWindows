using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DLNACast.Core.Audio;

public sealed class SystemLoopbackCaptureSource : IAudioCaptureSource
{
    private readonly CaptureSelection.SystemMix _selection;
    private WasapiLoopbackCapture? _capture;
    private CancellationTokenRegistration _cancellationRegistration;
    private int _stopping;
    private readonly CaptureHealthTracker _health = new();

    public SystemLoopbackCaptureSource(CaptureSelection.SystemMix selection) => _selection = selection;

    public CaptureSelection Selection => _selection;
    public bool IsRunning => _capture?.CaptureState == CaptureState.Capturing;
    public CaptureHealth Health => _health.Snapshot();
    public event EventHandler<Exception>? CaptureFailed;

    public Task StartAsync(PcmFrameBuffer destination, CancellationToken cancellationToken)
    {
        if (_capture is not null)
        {
            throw new InvalidOperationException("捕获已经启动。");
        }

        using var enumerator = new MMDeviceEnumerator();
        _health.Reset();
        var device = enumerator.GetDevice(_selection.EndpointId);
        var assembler = new PcmFrameAssembler(destination);
        var capture = new WasapiLoopbackCapture(device)
        {
            WaveFormat = new WaveFormat(PcmFrameBuffer.SampleRate, PcmFrameBuffer.BitsPerSample, PcmFrameBuffer.Channels)
        };
        capture.DataAvailable += (_, args) =>
        {
            var pcm = args.Buffer.AsSpan(0, args.BytesRecorded);
            _health.Record(pcm);
            assembler.Push(pcm);
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null && Volatile.Read(ref _stopping) == 0)
            {
                CaptureFailed?.Invoke(this, args.Exception);
            }
        };

        _capture = capture;
        _cancellationRegistration = cancellationToken.Register(() => _ = StopAsync());
        capture.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            _cancellationRegistration.Dispose();
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
        }
        finally
        {
            Interlocked.Exchange(ref _stopping, 0);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
