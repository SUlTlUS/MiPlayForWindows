namespace DLNACast.Core.Audio;

public sealed class PcmFrameAssembler(PcmFrameBuffer destination)
{
    private readonly PcmFrameBuffer _destination = destination;
    private readonly byte[] _pending = new byte[destination.FrameByteCount];
    private int _pendingCount;
    private ulong _pendingPresentationTimeMicroseconds;
    private ulong _previousFramePresentationTimeMicroseconds;
    private bool _hasPreviousFrame;

    public void Push(ReadOnlySpan<byte> pcm) =>
        Push(pcm, PcmPresentationTimestamp.GetElapsedRealtimeNanoseconds());

    public void Push(
        ReadOnlySpan<byte> pcm,
        ulong elapsedRealtimeNanoseconds)
    {
        if (pcm.IsEmpty) return;
        var readPresentationTimeMicroseconds =
            PcmPresentationTimestamp.CalculateMicroseconds(
                elapsedRealtimeNanoseconds,
                pcm.Length,
                _destination.Format);
        var consumedBytes = 0;
        while (!pcm.IsEmpty)
        {
            if (_pendingCount == 0)
            {
                _pendingPresentationTimeMicroseconds = PcmPresentationTimestamp.AddBytes(
                    readPresentationTimeMicroseconds,
                    consumedBytes,
                    _destination.Format);
            }
            var copyLength = Math.Min(_pending.Length - _pendingCount, pcm.Length);
            pcm[..copyLength].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += copyLength;
            consumedBytes += copyLength;
            pcm = pcm[copyLength..];

            if (_pendingCount == _pending.Length)
            {
                var captureDiscontinuity = _hasPreviousFrame &&
                    ExceedsTabletRecoveryWindow(
                        _pendingPresentationTimeMicroseconds -
                        Math.Min(
                            _pendingPresentationTimeMicroseconds,
                            _previousFramePresentationTimeMicroseconds));
                _destination.Write(
                    _pending,
                    _pendingPresentationTimeMicroseconds,
                    captureDiscontinuity: captureDiscontinuity);
                _previousFramePresentationTimeMicroseconds =
                    _pendingPresentationTimeMicroseconds;
                _hasPreviousFrame = true;
                _pendingCount = 0;
            }
        }
    }

    private bool ExceedsTabletRecoveryWindow(ulong gapMicroseconds) =>
        _destination.Format == PcmFrameFormat.MiPlayAac &&
        // Decompiled MicRecorder uses bytes / (sampleRate * channels) as its
        // recovery duration, stores now + duration, then requires another two
        // durations before recovery. For one 4096-byte stereo frame that is a
        // strict gap greater than about 128 ms. Preserve that conservative
        // trigger instead of treating ordinary capture batching as a dropout.
        (UInt128)gapMicroseconds *
            (uint)_destination.Format.SampleRate *
            (uint)_destination.Format.Channels >
        (UInt128)(3 * _destination.Format.BytesPerFrame) * 1_000_000UL;
}
