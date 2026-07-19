namespace DLNACast.Core.Models;

public sealed record CaptureHealth(
    DateTimeOffset StartedAt,
    DateTimeOffset? LastPacketAt,
    DateTimeOffset? LastAudiblePacketAt,
    long PacketCount)
{
    public bool IsContinuouslySilent(TimeSpan duration, DateTimeOffset? now = null)
    {
        var reference = LastAudiblePacketAt ?? StartedAt;
        return (now ?? DateTimeOffset.UtcNow) - reference >= duration;
    }
}
