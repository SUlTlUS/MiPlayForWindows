using DLNACast.Core.Models;

namespace DLNACast.Core.Audio;

internal sealed class CaptureHealthTracker
{
    private long _startedTicks = DateTimeOffset.UtcNow.UtcTicks;
    private long _lastPacketTicks;
    private long _lastAudibleTicks;
    private long _packetCount;

    public void Reset()
    {
        Volatile.Write(ref _startedTicks, DateTimeOffset.UtcNow.UtcTicks);
        Volatile.Write(ref _lastPacketTicks, 0);
        Volatile.Write(ref _lastAudibleTicks, 0);
        Interlocked.Exchange(ref _packetCount, 0);
    }

    public void Record(ReadOnlySpan<byte> pcm)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        Volatile.Write(ref _lastPacketTicks, now);
        Interlocked.Increment(ref _packetCount);
        foreach (var sampleByte in pcm)
        {
            if (sampleByte == 0) continue;
            Volatile.Write(ref _lastAudibleTicks, now);
            break;
        }
    }

    public CaptureHealth Snapshot()
    {
        var started = Volatile.Read(ref _startedTicks);
        var lastPacket = Volatile.Read(ref _lastPacketTicks);
        var lastAudible = Volatile.Read(ref _lastAudibleTicks);
        return new CaptureHealth(
            new DateTimeOffset(started, TimeSpan.Zero),
            lastPacket == 0 ? null : new DateTimeOffset(lastPacket, TimeSpan.Zero),
            lastAudible == 0 ? null : new DateTimeOffset(lastAudible, TimeSpan.Zero),
            Interlocked.Read(ref _packetCount));
    }
}
