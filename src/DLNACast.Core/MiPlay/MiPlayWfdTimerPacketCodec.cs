using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

// Retains the original Windows field names/order for compatibility callers.
// Native tablet replies put send at +16 and receive at +24 instead; use
// CreateTabletResponse for that wire layout, not these legacy field names.
public sealed record MiPlayWfdTimerPacket(
    ulong RemoteTimestamp0,
    ulong RemoteTimestamp1,
    ulong SourceReceiveTimestamp,
    ulong SourceSendTimestamp,
    uint Sequence,
    uint Reserved);

/// <summary>
/// Codec for the 40-byte little-endian UDP clock-sync packets advertised by
/// the source's wfd_timer_server_port OPTIONS header. Both response layouts
/// preserve the receiver's first two timestamps, sequence and reserved bytes.
/// Encode/Decode/CreateResponse retain the original Windows layout; the
/// tablet response has the two source timestamp slots in the opposite order.
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

    internal static byte[] CreateTabletResponse(
        ReadOnlySpan<byte> request,
        ulong sourceReceiveTimestamp,
        ulong sourceSendTimestamp)
    {
        if (request.IsEmpty)
        {
            throw new FormatException("MiPlay WFD timer requests must contain at least one byte.");
        }

        // ARM64 TimerService::Sync: recvfrom uses sp+0x20 as packet base;
        // its zeroed receive buffer is capped at 40 bytes, but any positive
        // recvfrom result is answered with the full 40-byte buffer. Mirror
        // that truncation/zero-fill behavior instead of requiring a canonical
        // request length; some receiver variants probe the timer endpoint with
        // a shorter datagram before beginning their ordinary 40-byte rounds.
        // the first clock read is stored at sp+0x38 (receive, packet +24),
        // the second at sp+0x30 (send, packet +16). TimerAlgorithm::unitRound
        // subtracts [+16] and adds [+24] to the client's elapsed time.
        var response = new byte[PacketLength];
        request[..Math.Min(request.Length, PacketLength)].CopyTo(response);
        BinaryPrimitives.WriteUInt64LittleEndian(response.AsSpan(16, 8), sourceSendTimestamp);
        BinaryPrimitives.WriteUInt64LittleEndian(response.AsSpan(24, 8), sourceReceiveTimestamp);
        return response;
    }
}
