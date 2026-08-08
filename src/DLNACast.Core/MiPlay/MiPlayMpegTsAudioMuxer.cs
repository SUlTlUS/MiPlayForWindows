using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayMpegTsAccessUnit(
    byte[] TransportStream,
    int PacketCount,
    ulong PresentationTimestamp90Khz,
    ulong? ProgramClockReference90Khz,
    bool ContainsProgramTables);

/// <summary>
/// Minimal MPEG-TS muxer for the rooted phone's AAC-only WFD profile:
/// PAT PID 0x0000, PMT PID 0x0100, PCR PID 0x1000, and MPEG-2 AAC ADTS on
/// PID 0x1100/stream id 0xC0. One AAC access unit becomes one PES packet.
/// The caller owns splitting its complete TS packet sequence across the
/// captured 1-7-TS-packet RTP boundary.
/// </summary>
public sealed class MiPlayMpegTsAudioMuxer
{
    public const int PatPid = 0x0000;
    public const int PmtPid = 0x0100;
    public const int PcrPid = 0x1000;
    public const int AudioPid = 0x1100;
    public const int ProgramNumber = 1;
    public const byte AacStreamType = 0x0f;
    public const byte AudioStreamId = 0xc0;
    public const int PesBytesBeforeAdtsAccessUnit = 16;

    public static int GetMaximumAdtsAccessUnitLength(bool includeProgramTables)
    {
        var availableTsPackets = MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket -
                                 (includeProgramTables ? 3 : 0);
        return availableTsPackets * 184 - PesBytesBeforeAdtsAccessUnit;
    }

    private byte patContinuityCounter;
    private byte pmtContinuityCounter;
    private byte audioContinuityCounter;

    public MiPlayMpegTsAudioMuxer(
        byte initialPatContinuityCounter = 0,
        byte initialPmtContinuityCounter = 0,
        byte initialAudioContinuityCounter = 0)
    {
        ValidateContinuityCounter(initialPatContinuityCounter, nameof(initialPatContinuityCounter));
        ValidateContinuityCounter(initialPmtContinuityCounter, nameof(initialPmtContinuityCounter));
        ValidateContinuityCounter(initialAudioContinuityCounter, nameof(initialAudioContinuityCounter));
        patContinuityCounter = initialPatContinuityCounter;
        pmtContinuityCounter = initialPmtContinuityCounter;
        audioContinuityCounter = initialAudioContinuityCounter;
    }

    public MiPlayMpegTsAccessUnit MuxAdtsAccessUnit(
        ReadOnlySpan<byte> adtsAccessUnit,
        ulong presentationTimestamp90Khz,
        ulong? programClockReference90Khz = null)
    {
        if (adtsAccessUnit.Length < MiPlayAdtsHeader.Length ||
            adtsAccessUnit[0] != 0xff ||
            (adtsAccessUnit[1] & 0xf0) != 0xf0)
        {
            throw new ArgumentException("MiPlay MPEG-TS audio requires one complete ADTS access unit.", nameof(adtsAccessUnit));
        }

        var pes = BuildPes(adtsAccessUnit, presentationTimestamp90Khz);
        var packets = new List<byte[]>(10);
        if (programClockReference90Khz is ulong pcr)
        {
            packets.Add(BuildPatPacket());
            packets.Add(BuildPmtPacket());
            packets.Add(BuildPcrPacket(pcr));
        }

        PacketizePes(pes, packets);
        var transportStream = new byte[packets.Count * MiPlayProtocolConstants.MpegTsPacketLength];
        for (var index = 0; index < packets.Count; index++)
        {
            packets[index].CopyTo(transportStream, index * MiPlayProtocolConstants.MpegTsPacketLength);
        }

        return new MiPlayMpegTsAccessUnit(
            transportStream,
            packets.Count,
            presentationTimestamp90Khz,
            programClockReference90Khz,
            ContainsProgramTables: programClockReference90Khz.HasValue);
    }

    private byte[] BuildPatPacket()
    {
        Span<byte> section = stackalloc byte[16];
        section[0] = 0x00;
        section[1] = 0xb0;
        section[2] = 0x0d;
        BinaryPrimitives.WriteUInt16BigEndian(section.Slice(3, 2), 0);
        section[5] = 0xc3; // version 1, current_next_indicator 1
        section[6] = 0;
        section[7] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(section.Slice(8, 2), ProgramNumber);
        section[10] = (byte)(0xe0 | (PmtPid >> 8));
        section[11] = (byte)(PmtPid & 0xff);
        BinaryPrimitives.WriteUInt32BigEndian(section.Slice(12, 4), ComputeMpegCrc32(section[..12]));
        return BuildPsiPacket(PatPid, section, ref patContinuityCounter);
    }

    private byte[] BuildPmtPacket()
    {
        Span<byte> section = stackalloc byte[21];
        section[0] = 0x02;
        section[1] = 0xb0;
        section[2] = 0x12;
        BinaryPrimitives.WriteUInt16BigEndian(section.Slice(3, 2), ProgramNumber);
        section[5] = 0xc3; // version 1, current_next_indicator 1
        section[6] = 0;
        section[7] = 0;
        section[8] = (byte)(0xe0 | (PcrPid >> 8));
        section[9] = (byte)(PcrPid & 0xff);
        section[10] = 0xf0;
        section[11] = 0;
        section[12] = AacStreamType;
        section[13] = (byte)(0xe0 | (AudioPid >> 8));
        section[14] = (byte)(AudioPid & 0xff);
        section[15] = 0xf0;
        section[16] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(section.Slice(17, 4), ComputeMpegCrc32(section[..17]));
        return BuildPsiPacket(PmtPid, section, ref pmtContinuityCounter);
    }

    private static byte[] BuildPsiPacket(int pid, ReadOnlySpan<byte> section, ref byte continuityCounter)
    {
        var packet = CreateStuffedPacket();
        WriteTsHeader(packet, pid, payloadUnitStart: true, adaptationFieldControl: 1, continuityCounter);
        packet[4] = 0; // pointer_field
        section.CopyTo(packet.AsSpan(5));
        continuityCounter = (byte)((continuityCounter + 1) & 0x0f);
        return packet;
    }

    private static byte[] BuildPcrPacket(ulong programClockReference90Khz)
    {
        var packet = CreateStuffedPacket();
        WriteTsHeader(packet, PcrPid, payloadUnitStart: true, adaptationFieldControl: 2, continuityCounter: 0);
        packet[4] = 183;
        packet[5] = 0x10;
        WritePcr(packet.AsSpan(6, 6), programClockReference90Khz);
        return packet;
    }

    private static byte[] BuildPes(ReadOnlySpan<byte> adtsAccessUnit, ulong presentationTimestamp90Khz)
    {
        const int optionalHeaderLength = 7;
        var pesPacketLength = 3 + optionalHeaderLength + adtsAccessUnit.Length;
        if (pesPacketLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(adtsAccessUnit));
        }

        var pes = new byte[6 + pesPacketLength];
        pes[0] = 0;
        pes[1] = 0;
        pes[2] = 1;
        pes[3] = AudioStreamId;
        BinaryPrimitives.WriteUInt16BigEndian(pes.AsSpan(4, 2), (ushort)pesPacketLength);
        pes[6] = 0x84; // MPEG-2 PES plus data_alignment_indicator
        pes[7] = 0x80; // PTS only
        pes[8] = optionalHeaderLength;
        WritePts(pes.AsSpan(9, 5), presentationTimestamp90Khz);
        pes[14] = 0xff;
        pes[15] = 0xff;
        adtsAccessUnit.CopyTo(pes.AsSpan(16));
        return pes;
    }

    private void PacketizePes(ReadOnlySpan<byte> pes, ICollection<byte[]> destination)
    {
        var offset = 0;
        var first = true;
        while (offset < pes.Length)
        {
            var remaining = pes.Length - offset;
            var payloadLength = Math.Min(184, remaining);
            var packet = CreateStuffedPacket();
            if (payloadLength == 184)
            {
                WriteTsHeader(packet, AudioPid, first, adaptationFieldControl: 1, audioContinuityCounter);
                pes.Slice(offset, payloadLength).CopyTo(packet.AsSpan(4));
            }
            else
            {
                WriteTsHeader(packet, AudioPid, first, adaptationFieldControl: 3, audioContinuityCounter);
                var adaptationFieldLength = 183 - payloadLength;
                packet[4] = (byte)adaptationFieldLength;
                if (adaptationFieldLength > 0)
                {
                    packet[5] = 0;
                }
                var payloadOffset = 5 + adaptationFieldLength;
                pes.Slice(offset, payloadLength).CopyTo(packet.AsSpan(payloadOffset));
            }

            destination.Add(packet);
            audioContinuityCounter = (byte)((audioContinuityCounter + 1) & 0x0f);
            offset += payloadLength;
            first = false;
        }
    }

    private static void WriteTsHeader(
        Span<byte> packet,
        int pid,
        bool payloadUnitStart,
        int adaptationFieldControl,
        byte continuityCounter)
    {
        packet[0] = 0x47;
        packet[1] = (byte)((payloadUnitStart ? 0x40 : 0) | ((pid >> 8) & 0x1f));
        packet[2] = (byte)pid;
        packet[3] = (byte)((adaptationFieldControl << 4) | continuityCounter);
    }

    private static void WritePts(Span<byte> destination, ulong timestamp90Khz)
    {
        var pts = timestamp90Khz & 0x1_ffff_ffffUL;
        destination[0] = (byte)(0x21 | ((pts >> 29) & 0x0e));
        destination[1] = (byte)(pts >> 22);
        destination[2] = (byte)(((pts >> 14) & 0xfe) | 1);
        destination[3] = (byte)(pts >> 7);
        destination[4] = (byte)(((pts << 1) & 0xfe) | 1);
    }

    private static void WritePcr(Span<byte> destination, ulong clock90Khz)
    {
        var pcrBase = clock90Khz & 0x1_ffff_ffffUL;
        destination[0] = (byte)(pcrBase >> 25);
        destination[1] = (byte)(pcrBase >> 17);
        destination[2] = (byte)(pcrBase >> 9);
        destination[3] = (byte)(pcrBase >> 1);
        destination[4] = (byte)(((pcrBase & 1) << 7) | 0x7e);
        destination[5] = 0;
    }

    private static uint ComputeMpegCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000_0000) != 0
                    ? (crc << 1) ^ 0x04c1_1db7
                    : crc << 1;
            }
        }
        return crc;
    }

    private static byte[] CreateStuffedPacket()
    {
        var packet = new byte[MiPlayProtocolConstants.MpegTsPacketLength];
        Array.Fill(packet, (byte)0xff);
        return packet;
    }

    private static void ValidateContinuityCounter(byte value, string parameterName)
    {
        if (value > 0x0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
