using DLNACast.Core.Audio;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Mirrors the tablet capture boundary: after a real capture discontinuity, the
/// current PCM frame and the next two captured frames are replaced with silence.
/// No additional frames or presentation timestamps are inserted.
/// </summary>
internal sealed class MiPlayCaptureDiscontinuityRecovery
{
    public const int RecoverySilenceFrameCount = 3;

    private readonly PcmFrameFormat format;
    private readonly byte[] silenceFrame;
    private long discontinuityCount;
    private long replacedSilenceFrameCount;
    private int replacementFramesRemaining;

    public MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format != PcmFrameFormat.MiPlayAac)
        {
            throw new ArgumentException(
                "MiPlay discontinuity recovery requires 48 kHz stereo AAC input frames.",
                nameof(format));
        }

        this.format = format;
        silenceFrame = new byte[format.BytesPerFrame];
    }

    public long DiscontinuityCount => Interlocked.Read(ref discontinuityCount);

    public long ReplacedSilenceFrameCount => Interlocked.Read(ref replacedSilenceFrameCount);

    public PcmFrameRead Recover(PcmFrameRead frame)
    {
        ArgumentNullException.ThrowIfNull(frame.Bytes);
        if (frame.Bytes.Length != format.BytesPerFrame)
        {
            throw new ArgumentException(
                $"MiPlay PCM input must contain exactly {format.BytesPerFrame} bytes.",
                nameof(frame));
        }
        if (frame.IsBufferUnderrunSilence)
        {
            return frame;
        }

        if (frame.CaptureDiscontinuity)
        {
            Interlocked.Increment(ref discontinuityCount);
            replacementFramesRemaining = RecoverySilenceFrameCount;
        }

        if (replacementFramesRemaining <= 0)
        {
            return frame with { CaptureDiscontinuity = false };
        }

        replacementFramesRemaining--;
        Interlocked.Increment(ref replacedSilenceFrameCount);
        return frame with
        {
            Bytes = silenceFrame,
            CaptureDiscontinuity = false,
        };
    }
}
