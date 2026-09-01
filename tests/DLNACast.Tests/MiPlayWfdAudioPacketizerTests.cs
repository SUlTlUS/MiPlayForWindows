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
    public void PairPacketsShareAacRtpTimelineAndLivePcr()
    {
        var firstPacketizer = new MiPlayWfdAudioPacketizer();
        var secondPacketizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);
        const ulong sharedLivePcr = 5_299_007_740;

        var first = Assert.Single(firstPacketizer.PacketizeAccessUnit(source, sharedLivePcr));
        var second = Assert.Single(secondPacketizer.PacketizeAccessUnit(source, sharedLivePcr));

        Assert.Equal(first.SequenceNumber, second.SequenceNumber);
        Assert.Equal(first.Timestamp90Khz, second.Timestamp90Khz);
        Assert.Equal(first.NormalizedAdtsAccessUnit, second.NormalizedAdtsAccessUnit);
        Assert.Equal(sharedLivePcr, ReadFirstPcrBase(first.TransportStream));
        Assert.Equal(sharedLivePcr, ReadFirstPcrBase(second.TransportStream));
        Assert.Equal(first.TransportStream, second.TransportStream);
    }

    [Fact]
    public void UsesCaptureDerivedPresentationTimestampForRtpAndPes()
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);

        var first = Assert.Single(packetizer.PacketizeAccessUnit(
            source,
            liveProgramClockReference90Khz: 900_000,
            presentationTimestamp90Khz: 0));
        var second = Assert.Single(packetizer.PacketizeAccessUnit(
            source,
            liveProgramClockReference90Khz: 901_919,
            presentationTimestamp90Khz: 1_917));

        Assert.Equal(0U, first.Timestamp90Khz);
        Assert.Equal(1_917U, second.Timestamp90Khz);
        Assert.Equal(3_837UL, packetizer.NextMediaPosition90Khz);
    }

    [Fact]
    public void LiveProgramClockJitterDoesNotChangeTheEncodedAacTimeline()
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);

        var first = Assert.Single(packetizer.PacketizeAccessUnit(
            source,
            liveProgramClockReference90Khz: 900_000));
        var second = Assert.Single(packetizer.PacketizeAccessUnit(
            source,
            liveProgramClockReference90Khz: 902_137));

        Assert.Equal(0U, first.Timestamp90Khz);
        Assert.Equal(1_919U, second.Timestamp90Khz);
        Assert.Equal(3_839UL, packetizer.NextMediaPosition90Khz);
        Assert.Equal(900_000UL, ReadFirstPcrBase(first.TransportStream));
    }

    [Theory]
    [InlineData(4_294_967_296UL)]
    [InlineData(8_589_934_592UL)]
    public void ExplicitOutputPtsKeepsRtpPesAndUnwrappedMediaPositionAcrossClockWraps(ulong wrapAt)
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);
        packetizer.AdvanceTimeline90Khz(90_000);

        for (ulong i = 0; i < 3; i++)
        {
            var pts = wrapAt - 90_000 - 6 + i * 1_920;
            var packet = Assert.Single(packetizer.PacketizeAccessUnit(source, 900_000, pts));
            var adjusted = pts + 90_000;
            Assert.Equal(unchecked((uint)adjusted), packet.Timestamp90Khz);
            Assert.Equal(unchecked((uint)adjusted), BinaryPrimitives.ReadUInt32BigEndian(packet.RtpPacket.AsSpan(4)));
            Assert.Equal(adjusted % (1UL << 33), ReadFirstPesPts(packet.TransportStream));
            Assert.Equal(adjusted + 1_920, packetizer.NextMediaPosition90Khz);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1_919)]
    [InlineData(2, 3_839)]
    [InlineData(13, 24_959)]
    [InlineData(18, 34_559)]
    [InlineData(34, 65_279)]
    [InlineData(100_000, 191_999_999)]
    public void PreservesCapturedStartupThenUsesExactAacSteadyStateTimestamps(
        uint index,
        uint expected)
    {
        Assert.Equal(expected, MiPlayWfdAudioPacketizer.CalculateCapturedTimestamp90Khz(index));
    }

    [Fact]
    public void AdvancesRtpAndProgramClockTogetherAfterAHostStall()
    {
        const ulong initialPcr = 866_913_276;
        var packetizer = new MiPlayWfdAudioPacketizer(
            initialProgramClockReference90Khz: initialPcr);
        packetizer.AdvanceTimeline90Khz(90_000);

        var packet = packetizer.Packetize(
            MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a));

        Assert.Equal(90_000U, packet.Timestamp90Khz);
        Assert.Equal(initialPcr + 90_000, ReadFirstPcrBase(packet.TransportStream));
        Assert.Equal(91_919UL, packetizer.NextMediaPosition90Khz);
    }

    [Fact]
    public void FractionalPcrChangesOnlyPcrBitsAndKeepsTablesFragmentationAndPts()
    {
        var precise = new MiPlayWfdAudioPacketizer();
        var legacy = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(946, 0x5a);
        for (uint i = 0; i < 30; i++)
        {
            if (i == 18)
            {
                precise.AdvanceTimeline90Khz(90_000);
                legacy.AdvanceTimeline90Khz(90_000);
            }
            var pcrBase = 8_589_934_590UL + i * 1920;
            var actual = precise.PacketizeAccessUnit(source, pcrBase, i * 1920, 297);
            var previous = legacy.PacketizeAccessUnit(source, pcrBase, i * 1920);
            Assert.Equal(previous.Count, actual.Count);
            for (var fragment = 0; fragment < actual.Count; fragment++)
            {
                var packet = actual[fragment];
                Assert.Equal(previous[fragment].Timestamp90Khz, packet.Timestamp90Khz);
                Assert.Equal(previous[fragment].ContainsProgramTables, packet.ContainsProgramTables);
                var normalizedWire = packet.WireFrame.ToArray();
                if (packet.ContainsProgramTables)
                {
                    var pcr = FindFirstPcr(packet.TransportStream);
                    Assert.Equal(pcrBase % (1UL << 33), ReadFirstPcrBase(packet.TransportStream));
                    Assert.Equal(297, ((packet.TransportStream[pcr + 4] & 1) << 8) | packet.TransportStream[pcr + 5]);
                    // Dollar header + fixed RTP header precede the TS bytes.
                    normalizedWire[16 + pcr + 4] &= 0xfe;
                    normalizedWire[16 + pcr + 5] = 0;
                }
                Assert.Equal(previous[fragment].WireFrame, normalizedWire);
            }
            Assert.Equal(legacy.NextMediaPosition90Khz, precise.NextMediaPosition90Khz);
        }
    }

    [Fact]
    public void ThreeReceiversKeepTheSameFullPcrAcrossLongPlaybackAndClockWrap()
    {
        var packetizers = Enumerable.Range(0, 3).Select(_ => new MiPlayWfdAudioPacketizer()).ToArray();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(128, 0x5a);
        const ulong startCaptureUs = 95_444_000_000;
        // Thirty virtual minutes across the 33-bit PCR rollover. This checks
        // clock arithmetic, not thirty minutes of hardware playback.
        for (ulong i = 0; i < 84_375; i++)
        {
            var captureUs = startCaptureUs + i * 1024 * 1_000_000 / 48_000;
            var pcr = MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(captureUs, 1_000_000);
            foreach (var packetizer in packetizers)
            {
                var packet = Assert.Single(packetizer.PacketizeAccessUnit(source, pcr / 300, i * 1920, (ushort)(pcr % 300)));
                if (packet.ContainsProgramTables)
                {
                    var offset = FindFirstPcr(packet.TransportStream);
                    var extension = ((packet.TransportStream[offset + 4] & 1) << 8) | packet.TransportStream[offset + 5];
                    Assert.Equal(pcr, ReadFirstPcrBase(packet.TransportStream) * 300 + (ulong)extension);
                }
            }
        }
    }

    [Fact]
    public void InvalidExtensionDoesNotAdvanceThePacketizer()
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(128, 0x5a);
        Assert.Throws<ArgumentOutOfRangeException>(() => packetizer.PacketizeAccessUnit(source, 1, 0, 300));
        Assert.Throws<ArgumentException>(() => packetizer.PacketizeAccessUnit(source, null, 0, 1));
        var actual = Assert.Single(packetizer.PacketizeAccessUnit(source, 1, 0, 297));
        var fresh = Assert.Single(new MiPlayWfdAudioPacketizer().PacketizeAccessUnit(source, 1, 0, 297));
        Assert.Equal(fresh.WireFrame, actual.WireFrame);
    }

    [Fact]
    public void SnapshotRestoresSequenceTimestampTablesAndTsContinuityForLateJoin()
    {
        var original = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(946, 0x5a);
        for (uint index = 0; index < 20; index++)
        {
            _ = original.PacketizeAccessUnit(
                source,
                liveProgramClockReference90Khz: 900_000 + index * 1_920,
                presentationTimestamp90Khz: index * 1_920,
                liveProgramClockReferenceExtension: 297);
        }

        var restored = new MiPlayWfdAudioPacketizer(original.CaptureSnapshot());
        var expected = original.PacketizeAccessUnit(source, 938_400, 38_400, 297);
        var actual = restored.PacketizeAccessUnit(source, 938_400, 38_400, 297);

        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].WireFrame, actual[index].WireFrame);
            Assert.Equal(expected[index].SequenceNumber, actual[index].SequenceNumber);
            Assert.Equal(expected[index].Timestamp90Khz, actual[index].Timestamp90Khz);
        }
        Assert.Equal(original.CaptureSnapshot(), restored.CaptureSnapshot());
    }

    [Fact]
    public void LateSenderKeepsItsOwnRtpAndTsContinuityAcrossHistoryToLiveJump()
    {
        var packetizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);
        MiPlayWfdAudioPacket? previous = null;
        for (ulong globalIndex = 100; globalIndex < 147; globalIndex++)
        {
            previous = Assert.Single(packetizer.PacketizeAccessUnit(
                source,
                liveProgramClockReference90Khz: 900_000 + globalIndex * 1_920,
                presentationTimestamp90Khz: globalIndex * 1_920));
        }

        var live = Assert.Single(packetizer.PacketizeAccessUnit(
            source,
            liveProgramClockReference90Khz: 1_200_000,
            presentationTimestamp90Khz: 150 * 1_920UL));

        Assert.NotNull(previous);
        Assert.Equal(unchecked((ushort)(previous.SequenceNumber + 1)), live.SequenceNumber);
        Assert.Equal(150 * 1_920U, live.Timestamp90Khz);
        Assert.Equal(48U, packetizer.AccessUnitIndex);
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

    private static ulong ReadFirstPesPts(byte[] transportStream)
    {
        for (var offset = 0; offset < transportStream.Length; offset += 188)
        {
            var ts = transportStream.AsSpan(offset, 188);
            var pid = ((ts[1] & 0x1f) << 8) | ts[2];
            if (pid != MiPlayMpegTsAudioMuxer.AudioPid || (ts[1] & 0x40) == 0) continue;
            var payload = 4 + (((ts[3] >> 4) & 3) == 3 ? 1 + ts[4] : 0);
            var pts = ts.Slice(payload + 9, 5);
            return ((ulong)(pts[0] & 0x0e) << 29) | ((ulong)pts[1] << 22) |
                   ((ulong)(pts[2] & 0xfe) << 14) | ((ulong)pts[3] << 7) | ((ulong)pts[4] >> 1);
        }
        throw new InvalidOperationException("No audio PES found.");
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
