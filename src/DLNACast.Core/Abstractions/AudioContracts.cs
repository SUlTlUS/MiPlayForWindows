using DLNACast.Core.Audio;
using DLNACast.Core.Models;

namespace DLNACast.Core.Abstractions;

public interface IAudioCaptureSource : IAsyncDisposable
{
    CaptureSelection Selection { get; }
    bool IsRunning { get; }
    CaptureHealth Health { get; }
    event EventHandler<Exception>? CaptureFailed;
    Task StartAsync(PcmFrameBuffer destination, CancellationToken cancellationToken);
    Task StopAsync();
}

public interface IAudioSourceCatalog
{
    IReadOnlyList<AudioSourceItem> GetOutputDevices();
    IReadOnlyList<AudioSourceItem> GetCandidateProcesses();
    IAudioCaptureSource CreateCapture(CaptureSelection selection);
}

public interface ILocalOutputManager
{
    ValueTask<ILocalOutputLease> RouteForCastAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken);
}

public interface ISwitchableLocalOutputManager : ILocalOutputManager
{
    ValueTask<CaptureSelection> SwitchActiveRouteAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken);
}

public interface ILocalOutputLease : IAsyncDisposable
{
    CaptureSelection CaptureSelection { get; }
}
