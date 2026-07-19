namespace DLNACast.Core.Audio;

public sealed class PcmFrameAssembler
{
    private readonly PcmFrameBuffer _destination;
    private readonly byte[] _pending = new byte[PcmFrameBuffer.BytesPerFrame];
    private int _pendingCount;

    public PcmFrameAssembler(PcmFrameBuffer destination) => _destination = destination;

    public void Push(ReadOnlySpan<byte> pcm)
    {
        while (!pcm.IsEmpty)
        {
            var copyLength = Math.Min(_pending.Length - _pendingCount, pcm.Length);
            pcm[..copyLength].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += copyLength;
            pcm = pcm[copyLength..];

            if (_pendingCount == _pending.Length)
            {
                _destination.Write(_pending);
                _pendingCount = 0;
            }
        }
    }
}

