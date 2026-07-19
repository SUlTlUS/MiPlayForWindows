using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Offline RTP packet encoder for the MPEG-TS payload shape used by MiPlay audio.
/// It writes bytes only; timestamp cadence, pacing, sockets, and media sending are outside this helper.
/// </summary>
public static class MiPlayRtpPacketCodec
{
    public const int HeaderLength = MiPlayProtocolConstants.RtpHeaderLength;
    public const int MaximumMpegTsPayloadLength =
        MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket * MiPlayProtocolConstants.MpegTsPacketLength;

    public static byte[] EncodeMpegTsPayload(
        ushort sequenceNumber,
        uint timestamp,
        uint synchronizationSource,
        ReadOnlySpan<byte> mpegTsPayload,
        bool marker = false)
    {
        if (mpegTsPayload.IsEmpty ||
            mpegTsPayload.Length % MiPlayProtocolConstants.MpegTsPacketLength != 0 ||
            mpegTsPayload.Length > MaximumMpegTsPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mpegTsPayload),
                $"MiPlay RTP payloads must contain 1-{MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket} complete MPEG-TS packets.");
        }

        var packet = new byte[HeaderLength + mpegTsPayload.Length];
        packet[0] = 0x80; // RTP v2, no padding, extension, or CSRC list.
        packet[1] = (byte)((marker ? 0x80 : 0) | MiPlayProtocolConstants.MpegTsRtpPayloadType);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), sequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), synchronizationSource);
        mpegTsPayload.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }
}