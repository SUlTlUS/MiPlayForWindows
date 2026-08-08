using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayWfdTimerPacket(
    ulong RemoteTimestamp0,
    ulong RemoteTimestamp1,
    ulong SourceReceiveTimestamp,
    ulong SourceSendTimestamp,
    uint Sequence,
    uint Reserved);

/// <summary>
/// Codec for the 40-byte little-endian UDP clock-sync packets advertised by
/// the source's wfd_timer_server_port OPTIONS header. The rooted phone copies
/// the receiver's first two timestamps and sequence, then fills the source
/// receive/send timestamps with its monotonic-microsecond clock.
/// </summary>
public static class MiPlayWfdTimerPacketCodec
{
    public const int PacketLength = 40;

    public static MiPlayWfdTimerPacket Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != PacketLength)
        {
            throw new FormatException($"MiPlay WFD timer packets must be exactly {PacketLength} bytes.");
        }

        return new MiPlayWfdTimerPacket(
            BinaryPrimitives.ReadUInt64LittleEndian(packet[..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(24, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(32, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(36, 4)));
    }

    public static byte[] Encode(MiPlayWfdTimerPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var bytes = new byte[PacketLength];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0, 8), packet.RemoteTimestamp0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), packet.RemoteTimestamp1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16, 8), packet.SourceReceiveTimestamp);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24, 8), packet.SourceSendTimestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), packet.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36, 4), packet.Reserved);
        return bytes;
    }

    public static byte[] CreateResponse(
        ReadOnlySpan<byte> request,
        ulong sourceReceiveTimestamp,
        ulong sourceSendTimestamp)
    {
        var decoded = Decode(request);
        return Encode(decoded with
        {
            SourceReceiveTimestamp = sourceReceiveTimestamp,
            SourceSendTimestamp = sourceSendTimestamp,
        });
    }
}
