using System.Buffers.Binary;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdAudioPacketizerTests
{
    [Fact]
    public void ProducesCapturedFirstAndSteadyStateWireShapes()
    {
        var packetizer = new MiPlayWfdAudioPacketizer(
            initialProgramClockReference90Khz: 866_913_276);
        var sourceAccessUnit = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);

        var first = packetizer.Packetize(sourceAccessUnit);
        var second = packetizer.Packetize(sourceAccessUnit);

        Assert.Equal((ushort)0, first.SequenceNumber);
        Assert.Equal((uint)0, first.Timestamp90Khz);
        Assert.True(first.ContainsProgramTables);
        Assert.Equal(1_332, first.WireFrame.Length);
        Assert.Equal(Convert.FromHexString("2400053080A1000000000000DEADBEEF"), first.WireFrame[..16]);
        Assert.Equal(0xf9, first.NormalizedAdtsAccessUnit[1]);

        Assert.Equal((ushort)1, second.SequenceNumber);
        Assert.Equal((uint)1_919, second.Timestamp90Khz);
        Assert.False(second.ContainsProgramTables);
        Assert.Equal(768, second.WireFrame.Length);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(second.RtpPacket.AsSpan(2, 2)));
        Assert.Equal((uint)1_919, BinaryPrimitives.ReadUInt32BigEndian(second.RtpPacket.AsSpan(4, 4)));
        Assert.Equal(0xdead_beefu, BinaryPrimitives.ReadUInt32BigEndian(second.RtpPacket.AsSpan(8, 4)));
    }

    [Fact]
    public void RefreshesProgramTablesAtCleanCapturedZeroThirteenThenFiveAccessUnitCadence()
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var accessUnit = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(128, 0);
        var packets = Enumerable.Range(0, 25)
            .Select(_ => packetizer.Packetize(accessUnit))
            .ToArray();

        Assert.Equal([0, 13, 18, 23], packets
            .Select((packet, index) => (packet, index))
            .Where(item => item.packet.ContainsProgramTables)
            .Select(item => item.index));
    }

    [Fact]
    public void FragmentsLargeTableBearingAccessUnitWithSameTimestampAndFinalMarker()
    {
        var packetizer = new MiPlayWfdAudioPacketizer(
            initialProgramClockReference90Khz: 866_913_276);
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(946, 0x5a);

        var fragments = packetizer.PacketizeAccessUnit(source);

        Assert.Equal(2, fragments.Count);
        Assert.Equal([(ushort)0, (ushort)1], fragments.Select(packet => packet.SequenceNumber));
        Assert.All(fragments, packet => Assert.Equal((uint)0, packet.Timestamp90Khz));
        Assert.True(fragments[0].ContainsProgramTables);
        Assert.False(fragments[1].ContainsProgramTables);
        Assert.Equal(7 * MiPlayProtocolConstants.MpegTsPacketLength, fragments[0].TransportStream.Length);
        Assert.Equal(2 * MiPlayProtocolConstants.MpegTsPacketLength, fragments[1].TransportStream.Length);
        Assert.Equal(0, fragments[0].RtpPacket[1] & 0x80);
        Assert.Equal(0x80, fragments[1].RtpPacket[1] & 0x80);

        var next = packetizer.Packetize(MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a));
        Assert.Equal((ushort)2, next.SequenceNumber);
        Assert.Equal((uint)1_919, next.Timestamp90Khz);
    }

    [Fact]
    public void PairPacketsShareAacAndRtpTimelineWhileKeepingPerSendLivePcr()
    {
        var firstPacketizer = new MiPlayWfdAudioPacketizer();
        var secondPacketizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);
        const ulong firstLivePcr = 5_299_007_740;
        const ulong secondLivePcr = 5_299_007_741;

        var first = Assert.Single(firstPacketizer.PacketizeAccessUnit(source, firstLivePcr));
        var second = Assert.Single(secondPacketizer.PacketizeAccessUnit(source, secondLivePcr));

        Assert.Equal(first.SequenceNumber, second.SequenceNumber);
        Assert.Equal(first.Timestamp90Khz, second.Timestamp90Khz);
        Assert.Equal(first.NormalizedAdtsAccessUnit, second.NormalizedAdtsAccessUnit);
        Assert.Equal(firstLivePcr, ReadFirstPcrBase(first.TransportStream));
        Assert.Equal(secondLivePcr, ReadFirstPcrBase(second.TransportStream));

        var firstWithoutPcr = first.TransportStream.ToArray();
        var secondWithoutPcr = second.TransportStream.ToArray();
        ClearFirstPcr(firstWithoutPcr);
        ClearFirstPcr(secondWithoutPcr);
        Assert.Equal(firstWithoutPcr, secondWithoutPcr);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1_919)]
    [InlineData(2, 3_839)]
    [InlineData(13, 24_959)]
    [InlineData(34, 65_278)]
    public void ReproducesCapturedMicrosecondQuantizedRtpTimestamps(uint index, uint expected)
    {
        Assert.Equal(expected, MiPlayWfdAudioPacketizer.CalculateCapturedTimestamp90Khz(index));
    }

    private static ulong ReadFirstPcrBase(byte[] transportStream)
    {
        var pcr = FindFirstPcr(transportStream);
        var bytes = transportStream.AsSpan(pcr, 6);
        return ((ulong)bytes[0] << 25) |
               ((ulong)bytes[1] << 17) |
               ((ulong)bytes[2] << 9) |
               ((ulong)bytes[3] << 1) |
               ((ulong)bytes[4] >> 7);
    }

    private static void ClearFirstPcr(byte[] transportStream)
    {
        var pcr = FindFirstPcr(transportStream);
        transportStream.AsSpan(pcr, 6).Clear();
    }

    private static int FindFirstPcr(byte[] transportStream)
    {
        for (var offset = 0; offset + MiPlayProtocolConstants.MpegTsPacketLength <= transportStream.Length;
             offset += MiPlayProtocolConstants.MpegTsPacketLength)
        {
            var packet = transportStream.AsSpan(offset, MiPlayProtocolConstants.MpegTsPacketLength);
            var adaptationFieldControl = (packet[3] >> 4) & 0x03;
            if (adaptationFieldControl is 2 or 3 && packet[4] >= 7 && (packet[5] & 0x10) != 0)
            {
                return offset + 6;
            }
        }
        throw new InvalidOperationException("The transport stream does not contain a PCR.");
    }
}
