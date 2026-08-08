using System.Buffers.Binary;
using System.Net;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPcapTcpPayload(
    int PacketIndex,
    uint TimestampSeconds,
    uint TimestampSubseconds,
    string SourceAddress,
    ushort SourcePort,
    string DestinationAddress,
    ushort DestinationPort,
    byte[] Payload);

public sealed record MiPlayPcapCommandFrame(
    int PacketIndex,
    string SourceEndpoint,
    string DestinationEndpoint,
    MiPlayCapturedCommandFrameSummary Frame);

public sealed record MiPlayPcapDecodeIssue(int PacketIndex, string Reason);

public sealed record MiPlayPcapDecodeResult(
    IReadOnlyList<MiPlayPcapTcpPayload> TcpPayloads,
    IReadOnlyList<MiPlayPcapCommandFrame> CommandFrames,
    IReadOnlyList<MiPlayPcapDecodeIssue> Issues);

/// <summary>
/// Offline parser for classic pcap files produced by rooted-phone tcpdump.
/// It intentionally supports only Ethernet + IPv4 + TCP captures and never
/// performs TCP reassembly, decryption, replay, or network operations.
/// </summary>
public static class MiPlayTcpdumpPcapDecoder
{
    private const uint MagicLittleEndianMicrosecond = 0xA1B2C3D4;
    private const uint MagicBigEndianMicrosecond = 0xD4C3B2A1;
    private const uint MagicLittleEndianNanosecond = 0xA1B23C4D;
    private const uint MagicBigEndianNanosecond = 0x4D3CB2A1;

    private const int GlobalHeaderLength = 24;
    private const int PacketHeaderLength = 16;
    private const int EthernetHeaderLength = 14;
    private const ushort EthernetTypeIpv4 = 0x0800;
    private const byte Ipv4Version = 4;
    private const byte IpProtocolTcp = 6;

    public static MiPlayPcapDecodeResult Decode(ReadOnlySpan<byte> pcap)
    {
        var payloads = new List<MiPlayPcapTcpPayload>();
        var frames = new List<MiPlayPcapCommandFrame>();
        var issues = new List<MiPlayPcapDecodeIssue>();

        if (pcap.Length < GlobalHeaderLength)
        {
            issues.Add(new MiPlayPcapDecodeIssue(-1, "Classic pcap global header is incomplete."));
            return new MiPlayPcapDecodeResult(payloads, frames, issues);
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(pcap);
        var littleEndian = magic switch
        {
            MagicLittleEndianMicrosecond or MagicLittleEndianNanosecond => true,
            MagicBigEndianMicrosecond or MagicBigEndianNanosecond => false,
            _ => throw new FormatException("Unsupported pcap magic. Only classic tcpdump pcap is supported."),
        };

        var linkType = ReadUInt32(pcap.Slice(20, 4), littleEndian);
        if (linkType != 1)
        {
            throw new FormatException($"Unsupported pcap link type {linkType}; expected Ethernet link type 1.");
        }

        var cursor = GlobalHeaderLength;
        var packetIndex = 0;
        while (cursor < pcap.Length)
        {
            if (pcap.Length - cursor < PacketHeaderLength)
            {
                issues.Add(new MiPlayPcapDecodeIssue(packetIndex, "Trailing pcap packet header is incomplete."));
                break;
            }

            var timestampSeconds = ReadUInt32(pcap.Slice(cursor, 4), littleEndian);
            var timestampSubseconds = ReadUInt32(pcap.Slice(cursor + 4, 4), littleEndian);
            var includedLength = ReadUInt32(pcap.Slice(cursor + 8, 4), littleEndian);
            cursor += PacketHeaderLength;

            if (includedLength > int.MaxValue || pcap.Length - cursor < includedLength)
            {
                issues.Add(new MiPlayPcapDecodeIssue(packetIndex, "Pcap packet payload is truncated."));
                break;
            }

            var packet = pcap.Slice(cursor, (int)includedLength);
            cursor += (int)includedLength;

            if (TryDecodeTcpPayload(packet, out var payload, out var reason) && payload is not null)
            {
                var captured = payload with
                {
                    PacketIndex = packetIndex,
                    TimestampSeconds = timestampSeconds,
                    TimestampSubseconds = timestampSubseconds,
                };
                payloads.Add(captured);

                var decoded = MiPlayCapturedCommandStreamDecoder.Decode(captured.Payload);
                foreach (var frame in decoded.Frames)
                {
                    frames.Add(new MiPlayPcapCommandFrame(
                        packetIndex,
                        $"{captured.SourceAddress}:{captured.SourcePort}",
                        $"{captured.DestinationAddress}:{captured.DestinationPort}",
                        frame));
                }

                foreach (var issue in decoded.Issues)
                {
                    issues.Add(new MiPlayPcapDecodeIssue(
                        packetIndex,
                        $"MiPlay payload issue at TCP payload offset {issue.Offset}: {issue.Reason}"));
                }
            }
            else if (reason is not null)
            {
                issues.Add(new MiPlayPcapDecodeIssue(packetIndex, reason));
            }

            packetIndex++;
        }

        return new MiPlayPcapDecodeResult(payloads, frames, issues);
    }

    private static bool TryDecodeTcpPayload(
        ReadOnlySpan<byte> packet,
        out MiPlayPcapTcpPayload? payload,
        out string? reason)
    {
        payload = null;
        reason = null;

        if (packet.Length < EthernetHeaderLength + 20)
        {
            return false;
        }

        var etherType = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(12, 2));
        if (etherType != EthernetTypeIpv4)
        {
            return false;
        }

        var ipOffset = EthernetHeaderLength;
        var versionAndHeaderLength = packet[ipOffset];
        var version = versionAndHeaderLength >> 4;
        var ipHeaderLength = (versionAndHeaderLength & 0x0F) * 4;
        if (version != Ipv4Version || ipHeaderLength < 20 || packet.Length < ipOffset + ipHeaderLength)
        {
            reason = "Invalid IPv4 header in Ethernet packet.";
            return false;
        }

        if (packet[ipOffset + 9] != IpProtocolTcp)
        {
            return false;
        }

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(ipOffset + 2, 2));
        if (totalLength < ipHeaderLength || packet.Length < ipOffset + totalLength)
        {
            reason = "IPv4 total length is truncated in captured packet.";
            return false;
        }

        var tcpOffset = ipOffset + ipHeaderLength;
        if (totalLength - ipHeaderLength < 20 || packet.Length < tcpOffset + 20)
        {
            reason = "TCP header is incomplete.";
            return false;
        }

        var tcpHeaderLength = (packet[tcpOffset + 12] >> 4) * 4;
        if (tcpHeaderLength < 20 || totalLength - ipHeaderLength < tcpHeaderLength)
        {
            reason = "Invalid TCP data offset.";
            return false;
        }

        var payloadOffset = tcpOffset + tcpHeaderLength;
        var payloadLength = ipOffset + totalLength - payloadOffset;
        if (payloadLength <= 0)
        {
            return false;
        }

        payload = new MiPlayPcapTcpPayload(
            PacketIndex: -1,
            TimestampSeconds: 0,
            TimestampSubseconds: 0,
            SourceAddress: new IPAddress(packet.Slice(ipOffset + 12, 4).ToArray()).ToString(),
            SourcePort: BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(tcpOffset, 2)),
            DestinationAddress: new IPAddress(packet.Slice(ipOffset + 16, 4).ToArray()).ToString(),
            DestinationPort: BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(tcpOffset + 2, 2)),
            Payload: packet.Slice(payloadOffset, payloadLength).ToArray());
        return true;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);
}
