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
    TransportStall,
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
    MiPlayCastFailureKind FailureKind = MiPlayCastFailureKind.None,
    long MediaTimelineRebases = 0,
    double MaximumInterReceiverSendSkewMilliseconds = 0,
    int BufferedAccessUnits = 0,
    long AccessUnitBufferStarvations = 0,
    int? ReceiverReportedLatencyMilliseconds = null,
    long? ReceiverReportedRtpPacketCount = null,
    int? ReceiverReportedBitrate = null,
    double MinimumEncodedAccessUnitAgeMilliseconds = 0,
    double MaximumEncodedAccessUnitAgeMilliseconds = 0,
    bool? ReceiverPaused = null,
    bool? ContainsAudiblePcm = null);

public sealed record MiPlaySystemAudioRequest(
    RendererDevice Renderer,
    CaptureSelection Selection,
    string FfmpegPath,
    AudioChannelRoute ChannelRoute = AudioChannelRoute.Stereo,
    MiPlayPairSynchronization? PairSynchronization = null,
    MiPlaySharedAudioSession? SharedAudioSession = null,
    int PlaybackDelayMicroseconds = MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds)
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
        if (SharedAudioSession is not null && PairSynchronization is not null)
        {
            throw new ArgumentException(
                "Shared MiPlay capture already coordinates pair startup.",
                nameof(PairSynchronization));
        }
        if (PlaybackDelayMicroseconds is < MiPlayProtocolConstants.MinimumConfigurablePlaybackDelayMicroseconds or
            > MiPlayProtocolConstants.MaximumConfigurablePlaybackDelayMicroseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(PlaybackDelayMicroseconds));
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

/// <summary>
/// Optional active-session control surface for inserting the acoustic latency
/// probe without changing the capture source or reconnecting the receiver.
/// </summary>
public interface IMiPlayLatencyProbeController
{
    Task PrepareLatencyProbeAsync(
        CancellationToken cancellationToken = default);

    Task<long> StartLatencyProbeAsync(
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

/// <summary>
/// Active receiver control plane. Open is owned by session startup; these
/// operations serialize Pause, Resume/time-offset recovery, and Close onto the
/// same command connection without interrupting sibling receivers.
/// </summary>
public interface IMiPlayPlaybackController
{
    bool ReceiverPaused { get; }

    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task PauseReceiverAsync(CancellationToken cancellationToken = default) =>
        PauseAsync(cancellationToken);
    Task ResumeReceiverAsync(CancellationToken cancellationToken = default) =>
        ResumeAsync(cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
