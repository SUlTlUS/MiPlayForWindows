using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayAacSilenceAccessUnitTests
{
    [Fact]
    public void GeneratedSilenceFramePassesTheCompleteOfflineWirePipeline()
    {
        var accessUnit = MiPlayAacSilenceAccessUnit.Create();
        var normalized = MiPlayAdtsStreamParser.NormalizeMpeg2AacLc48KhzStereo(accessUnit);
        var packet = new MiPlayWfdAudioPacketizer().Packetize(normalized);

        Assert.Equal(accessUnit, normalized);
        Assert.True(packet.ContainsProgramTables);
        Assert.Equal(4, packet.TransportStream.Length / MiPlayProtocolConstants.MpegTsPacketLength);
        Assert.Equal(768, packet.WireFrame.Length);
        Assert.Equal(Convert.FromHexString("240002FC80A1000000000000DEADBEEF"), packet.WireFrame[..16]);
    }
}
