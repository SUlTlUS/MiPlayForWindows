using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayMpegTsAudioMuxerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(297)]
    [InlineData(299)]
    public void WritesAllNineExtensionBitsAndPreservesReservedBits(ushort extension)
    {
        var accessUnit = MiPlayAdtsHeader.Prepend(new byte[682]);
        var muxed = new MiPlayMpegTsAudioMuxer(1, 1, 0).MuxAdtsAccessUnit(
            accessUnit, 12_345, 8_589_934_591, extension);
        var legacy = new MiPlayMpegTsAudioMuxer(1, 1, 0).MuxAdtsAccessUnit(
            accessUnit, 12_345, 8_589_934_591);
        var pcr = muxed.TransportStream.AsSpan(2 * 188 + 6, 6);

        Assert.Equal(extension, muxed.ProgramClockReferenceExtension);
        Assert.Equal(extension, ((pcr[4] & 1) << 8) | pcr[5]);
        Assert.Equal(0x7e, pcr[4] & 0x7e);
        Assert.Equal(Convert.FromHexString("FFFFFFFF"), pcr[..4].ToArray());
        Assert.Equal(0x80, pcr[4] & 0x80);
        var withoutFraction = muxed.TransportStream.ToArray();
        withoutFraction[2 * 188 + 10] &= 0xfe;
        withoutFraction[2 * 188 + 11] = 0;
        Assert.Equal(legacy.TransportStream, withoutFraction);
    }

    [Fact]
    public void RejectsInvalidOrBaselessExtensionBeforeAdvancingContinuityCounters()
    {
        var source = MiPlayAdtsHeader.Prepend(new byte[682]);
        var muxer = new MiPlayMpegTsAudioMuxer(1, 1, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => muxer.MuxAdtsAccessUnit(source, 0, 1, 300));
        Assert.Throws<ArgumentException>(() => muxer.MuxAdtsAccessUnit(source, 0, null, 1));
        var afterInvalid = muxer.MuxAdtsAccessUnit(source, 0, 1, 297);
        var fresh = new MiPlayMpegTsAudioMuxer(1, 1, 0).MuxAdtsAccessUnit(source, 0, 1, 297);
        Assert.Equal(fresh.TransportStream, afterInvalid.TransportStream);
    }

    [Fact]
    public void PinsAdtsCapacityWithAndWithoutProgramTables()
    {
        Assert.Equal(720, MiPlayMpegTsAudioMuxer.GetMaximumAdtsAccessUnitLength(includeProgramTables: true));
        Assert.Equal(1_272, MiPlayMpegTsAudioMuxer.GetMaximumAdtsAccessUnitLength(includeProgramTables: false));
    }

    [Fact]
    public void FirstCapturedShapeProducesSevenPacketsAndExactProgramTables()
    {
        var accessUnit = MiPlayAdtsHeader.Prepend(new byte[682]);
        var muxer = new MiPlayMpegTsAudioMuxer(
            initialPatContinuityCounter: 1,
            initialPmtContinuityCounter: 1,
            initialAudioContinuityCounter: 0);

        var muxed = muxer.MuxAdtsAccessUnit(
            accessUnit,
            presentationTimestamp90Khz: 0,
            programClockReference90Khz: 866_913_276);

        Assert.True(muxed.ContainsProgramTables);
        Assert.Equal(7, muxed.PacketCount);
        Assert.Equal(7 * 188, muxed.TransportStream.Length);
        Assert.Equal(
            Convert.FromHexString("474000110000B00D0000C300000001E1002DF65295"),
            muxed.TransportStream[..21]);
        Assert.Equal(
            Convert.FromHexString("474100110002B0120001C30000F000F0000FF100F000A4279ACD"),
            muxed.TransportStream[188..(188 + 26)]);

        var pcrPacket = muxed.TransportStream[(2 * 188)..(3 * 188)];
        Assert.Equal(Convert.FromHexString("47500020B710"), pcrPacket[..6]);

        var firstAudioPacket = muxed.TransportStream[(3 * 188)..(4 * 188)];
        Assert.Equal(
            Convert.FromHexString("47510010000001C002BB8480072100010001FFFFFFF94C80563FFC"),
            firstAudioPacket[..27]);
    }

    [Fact]
    public void CapturedAccessUnitSizesFitOneRtpAndDollarFrame()
    {
        var muxer = new MiPlayMpegTsAudioMuxer(1, 1, 0);
        var first = muxer.MuxAdtsAccessUnit(
            MiPlayAdtsHeader.Prepend(new byte[682]),
            presentationTimestamp90Khz: 0,
            programClockReference90Khz: 866_913_276);
        var firstRtp = MiPlayRtpPacketCodec.EncodeMpegTsPayload(
            sequenceNumber: 0,
            timestamp: 0,
            synchronizationSource: 0xdead_beef,
            first.TransportStream,
            marker: true);
        var firstWire = MiPlayWfdInterleavedFrameCodec.Encode(firstRtp);

        Assert.Equal(1_328, firstRtp.Length);
        Assert.Equal(1_332, firstWire.Length);
        Assert.Equal(
            Convert.FromHexString("2400053080A1000000000000DEADBEEF"),
            firstWire[..16]);

        var second = muxer.MuxAdtsAccessUnit(
            MiPlayAdtsHeader.Prepend(new byte[682]),
            presentationTimestamp90Khz: 1_920);
        var secondRtp = MiPlayRtpPacketCodec.EncodeMpegTsPayload(
            sequenceNumber: 1,
            timestamp: 1_920,
            synchronizationSource: 0xdead_beef,
            second.TransportStream,
            marker: true);
        var secondWire = MiPlayWfdInterleavedFrameCodec.Encode(secondRtp);

        Assert.False(second.ContainsProgramTables);
        Assert.Equal(4, second.PacketCount);
        Assert.Equal(764, secondRtp.Length);
        Assert.Equal(768, secondWire.Length);
        Assert.Equal(0x14, second.TransportStream[3]);
        Assert.Equal(0x15, second.TransportStream[188 + 3]);
        Assert.Equal(0x16, second.TransportStream[(2 * 188) + 3]);
        Assert.Equal(0x37, second.TransportStream[(3 * 188) + 3]);
    }

    [Fact]
    public void RejectsNonAdtsButPreservesLargeAccessUnitsForRtpFragmentation()
    {
        var muxer = new MiPlayMpegTsAudioMuxer();

        Assert.Throws<ArgumentException>(() => muxer.MuxAdtsAccessUnit([0, 1, 2], 0));
        var large = muxer.MuxAdtsAccessUnit(
            MiPlayAdtsHeader.Prepend(new byte[1_500]),
            0,
            programClockReference90Khz: 1);

        Assert.True(large.PacketCount > MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket);
        Assert.Equal(large.PacketCount * MiPlayProtocolConstants.MpegTsPacketLength, large.TransportStream.Length);
    }
}
