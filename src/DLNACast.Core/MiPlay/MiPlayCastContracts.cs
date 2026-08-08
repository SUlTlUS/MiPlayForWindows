using DLNACast.Core.Models;
using DLNACast.Core.Audio;
using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

public enum MiPlayCastState
{
    Idle,
    Connecting,
    Bootstrapping,
    AwaitingReceiver,
    Streaming,
    Stopping,
    Error,
}

public enum MiPlayCastFailureKind
{
    None,
    ReceiverBusy,
}

public sealed record MiPlayCastDiagnostics(
    MiPlayCastState State,
    string Message,
    int BufferedMilliseconds = 0,
    long Overruns = 0,
    long Underruns = 0,
    long AccessUnits = 0,
    long RtpFrames = 0,
    long WireBytes = 0,
    string? LastError = null,
    string? ProtocolEvidence = null,
    double MinimumMediaSendGapMilliseconds = 0,
    double MaximumMediaSendGapMilliseconds = 0,
    long LateMediaSends = 0,
    long CatchUpMediaSends = 0,
    MiPlayCastFailureKind FailureKind = MiPlayCastFailureKind.None);

public sealed record MiPlaySystemAudioRequest(
    RendererDevice Renderer,
    CaptureSelection Selection,
    string FfmpegPath,
    AudioChannelRoute ChannelRoute = AudioChannelRoute.Stereo,
    MiPlayPairSynchronization? PairSynchronization = null)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(FfmpegPath);
        if (!Enum.IsDefined(ChannelRoute))
        {
            throw new ArgumentOutOfRangeException(nameof(ChannelRoute));
        }
        if (Renderer.Address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.Any.Equals(Renderer.Address) ||
            IPAddress.None.Equals(Renderer.Address) ||
            IPAddress.Broadcast.Equals(Renderer.Address) ||
            IPAddress.Loopback.Equals(Renderer.Address))
        {
            throw new ArgumentException(
                "MiPlay requires one explicit, non-loopback IPv4 receiver address.",
                nameof(Renderer));
        }
    }
}

public interface IMiPlaySystemAudioSessionRunner
{
    Task RunAsync(
        MiPlaySystemAudioRequest request,
        Action receiverReady,
        Action<MiPlayCastDiagnostics> report,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional active-session control surface for replacing the capture endpoint
/// without closing the MiPlay control, RTSP, or media connections.
/// </summary>
public interface IMiPlayAudioCaptureController
{
    Task SetCaptureSelectionAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken = default);
}

public sealed class MiPlayReceiverVolumeChangedEventArgs(int volume) : EventArgs
{
    public int Volume { get; } = volume is >= 0 and <= 100
        ? volume
        : throw new ArgumentOutOfRangeException(nameof(volume));
}

/// <summary>
/// Optional active-session control surface implemented by MiPlay runners that
/// can serialize receiver-volume commands onto their owned control connection.
/// </summary>
public interface IMiPlayReceiverVolumeController
{
    int? ReceiverVolume { get; }
    event EventHandler<MiPlayReceiverVolumeChangedEventArgs>? ReceiverVolumeChanged;

    Task SetReceiverVolumeAsync(int volume, CancellationToken cancellationToken = default);
}
