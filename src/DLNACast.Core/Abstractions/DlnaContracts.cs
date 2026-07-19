using DLNACast.Core.Audio;
using DLNACast.Core.Models;
using DLNACast.Core.Streaming;

namespace DLNACast.Core.Abstractions;

public interface IRendererDiscovery : IAsyncDisposable
{
    Task<IReadOnlyList<RendererDevice>> SearchAsync(CancellationToken cancellationToken);
}

public interface IRendererController
{
    Task<string> GetSinkProtocolInfoAsync(RendererDevice device, CancellationToken cancellationToken);
    Task SetTransportUriAsync(RendererDevice device, Uri streamUri, StreamProfile profile, CancellationToken cancellationToken);
    Task PlayAsync(RendererDevice device, CancellationToken cancellationToken);
    Task StopAsync(RendererDevice device, CancellationToken cancellationToken);
    Task<TransportStatus> GetTransportStatusAsync(RendererDevice device, CancellationToken cancellationToken);
    Task<int?> GetVolumeAsync(RendererDevice device, CancellationToken cancellationToken);
    Task SetVolumeAsync(RendererDevice device, int volume, CancellationToken cancellationToken);
}

public interface ILiveStreamServer : IAsyncDisposable
{
    Task<LiveStreamSession> StartSessionAsync(
        RendererDevice renderer,
        PcmFrameBuffer frames,
        StreamProfile profile,
        CancellationToken cancellationToken);
}
