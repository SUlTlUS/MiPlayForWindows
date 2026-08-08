using System.Security.Cryptography;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacySourceStatusQueryEvidenceTests
{
    [Fact]
    public void StaticDispatcherMappingsMatchLiveReadOnlyQuerySet()
    {
        var snapshot = MiPlayLegacySourceStatusQueryEvidence.CreateCurrentSnapshot();

        Assert.Equal("LX06 1.88.51", snapshot.ReceiverFirmwareVersion);
        Assert.Equal(4, snapshot.Commands.Count);
        Assert.Equal(
            [0x000e, 0x0010, 0x0014, 0x001c],
            snapshot.Commands.Select(command => (int)command.RequestCommand));
        Assert.Equal(
            [0x000f, 0x0011, 0x0015, 0x001d],
            snapshot.Commands.Select(command => (int)command.NormalAcknowledgementCommand));
        Assert.True(snapshot.Commands.Single(command => command.Name == "Cmd_GetVolume").NormalAcknowledgementObservedOnRealReceiver);
        Assert.False(snapshot.Commands.Single(command => command.Name == "Cmd_GetPosition").RequestObservedOnRealSource);
        Assert.Contains("sends 0x0022", snapshot.Commands.Single(command => command.Name == "Cmd_GetMediaInfo").ModeTwoBehavior, StringComparison.Ordinal);
        Assert.True(snapshot.Commands.Single(command => command.Name == "Cmd_GetState").NormalAcknowledgementObservedOnRealReceiver);
        Assert.False(snapshot.RelativeOrderIsStable);
        Assert.False(snapshot.SafeForNetworkUse);
    }

    [Theory]
    [InlineData(0u, "8855508AADE16EC573D21E6A485DFD0A7624085C1A14B5ECDD6485DE0C6839A4")]
    [InlineData(2u, "89EEFC18FA4B815BD1ADED2F24EB28885993AA00B6D0171BF5005F9D39AAEA10")]
    [InlineData(24u, "0C36D96451AF41A365AA826EDD3EA6F0A5404659945AB9B1C77C097D8E43CBC8")]
    [InlineData(25u, "502454ACB4632DBE9F2B62CF8F53F82C4C309BDDE6FECDD24675DED6B5A12AB8")]
    public void FiveByteScalarCodecReproducesCapturedPayloadHashes(uint value, string expectedSha256)
    {
        var payload = MiPlayLegacyStatusScalarCodec.Encode(value);

        Assert.True(MiPlayLegacyStatusScalarCodec.TryDecode(payload, out var decoded));
        Assert.Equal(value, decoded);
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(payload)));
    }

    [Fact]
    public void ScalarCodecRejectsUnknownTagOrLength()
    {
        Assert.False(MiPlayLegacyStatusScalarCodec.TryDecode([1, 0, 0, 0, 2], out _));
        Assert.False(MiPlayLegacyStatusScalarCodec.TryDecode([0, 0, 0, 2], out _));
    }
}
