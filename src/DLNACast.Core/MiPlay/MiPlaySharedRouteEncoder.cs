using DLNACast.Core.Audio;
using System.Threading.Channels;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Owns the single AAC encoder for one channel route in a shared MiPlay group.
/// Its lifetime belongs to the group, not to any receiver, matching the native
/// audioSender/multiAudioSender ownership model.
/// </summary>
internal sealed class MiPlaySharedRouteEncoder : IAsyncDisposable
{
    private readonly MiPlayFfmpegAacEncoder encoder;
    private readonly MiPlayOrderedAccessUnitBuffer accessUnits = new();
    private readonly MiPlayEncoderInputWindow inputWindow = new();
    private readonly Channel<MiPlayEncoderInputTiming> inputTimings =
        Channel.CreateUnbounded<MiPlayEncoderInputTiming>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource lifetime;
    private readonly Task writerTask;
    private readonly Task readerTask;
    private int disposed;

    public MiPlaySharedRouteEncoder(
        AudioChannelRoute route,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        PcmBuffer = new PcmFrameBuffer(
            route,
            PcmFrameFormat.MiPlayAac,
            maximumMilliseconds: PcmFrameBuffer.MaximumBufferMilliseconds,
            preserveFrameSequence: true);
        encoder = MiPlayFfmpegAacEncoder.Start(
            ffmpegPath,
            MiPlayProtocolConstants.AacBitRate,
            "aac_mf");
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        writerTask = Task.Run(() => RunWriterAsync(lifetime.Token), lifetime.Token);
        readerTask = Task.Run(() => RunReaderAsync(lifetime.Token), lifetime.Token);
    }

    internal PcmFrameBuffer PcmBuffer { get; }
    internal int BufferedAccessUnits => accessUnits.BufferedCount;
    internal long StarvationCount => accessUnits.StarvationCount;
    internal int PendingInputFrames => inputWindow.PendingFrameCount;

    internal async ValueTask<MiPlayEncodedAccessUnit> ReadAsync(
        CancellationToken cancellationToken)
    {
        var accessUnit = await accessUnits.ReadAsync(cancellationToken).ConfigureAwait(false);
        inputWindow.ReleaseConsumedFrame();
        return accessUnit;
    }

    private async Task RunWriterAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // MediaCodec offers a finite set of input buffers. Bound PCM
                // accepted by aac_mf the same way instead of letting its pipe
                // and internal queues hide seconds of startup audio. Acquire
                // before reading PCM; no stale frame is held across this wait.
                // Encoding still starts immediately, independently of RTSP.
                await inputWindow.AcquireAsync(cancellationToken).ConfigureAwait(false);
                // Shared capture already supplies paced PCM and recovery zero
                // frames. A route waits for that exact sequence; independent
                // timed reads can insert extra silence and shift one channel.
                var pcm = await PcmBuffer.ReadFramePreservingSequenceAsync(cancellationToken)
                    .ConfigureAwait(false);
                await encoder.WritePcmAsync(pcm.Bytes, cancellationToken).ConfigureAwait(false);
                await inputTimings.Writer.WriteAsync(
                        new MiPlayEncoderInputTiming(
                            pcm.PresentationTimeMicroseconds,
                            pcm.ReanchorPresentationTimeline),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            accessUnits.Complete(exception);
            lifetime.Cancel();
            throw;
        }
        finally
        {
            inputTimings.Writer.TryComplete();
        }
    }

    private async Task RunReaderAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytes = await encoder.ReadAccessUnitAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (bytes is null)
                {
                    accessUnits.Complete();
                    return;
                }
                var timing = await inputTimings.Reader.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                await accessUnits.WriteAsync(
                        new MiPlayEncodedAccessUnit(
                            bytes,
                            timing.PresentationTimeMicroseconds,
                            timing.ReanchorPresentationTimeline),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            accessUnits.Complete(exception);
            lifetime.Cancel();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        PcmBuffer.Complete();
        accessUnits.Complete(new OperationCanceledException(
            "The shared MiPlay route encoder was disposed."));
        try
        {
            await Task.WhenAll(writerTask, readerTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await encoder.DisposeAsync().ConfigureAwait(false);
            await PcmBuffer.DisposeAsync().ConfigureAwait(false);
            inputWindow.Dispose();
            lifetime.Dispose();
        }
    }
}
