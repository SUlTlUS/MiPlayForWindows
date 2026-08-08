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
}
