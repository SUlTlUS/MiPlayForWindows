using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyAppData(
    int ControlPort,
    bool HasAdvertisedControlPort,
    bool SupportsLyra)
{
    /// <summary>
    /// Parses the legacy IDM appData layout used by MiPlay. Bytes 0-1 are a
    /// big-endian command port and byte 24 is the Lyra capability flag.
    /// </summary>
    public static MiPlayLegacyAppData Parse(byte[]? appData)
    {
        var data = appData.AsSpan();
        var hasPort = data.Length >= sizeof(ushort);
        var controlPort = hasPort
            ? BinaryPrimitives.ReadUInt16BigEndian(data)
            : MiPlayProtocolConstants.DefaultControlPort;
        var supportsLyra = data.Length >= 25 && data[24] == 1;

        return new MiPlayLegacyAppData(controlPort, hasPort, supportsLyra);
    }
}
