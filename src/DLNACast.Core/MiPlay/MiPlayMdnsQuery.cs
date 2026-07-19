using System.Buffers.Binary;
using System.Text;

namespace DLNACast.Core.MiPlay;

public static class MiPlayMdnsQuery
{
    public const string ServiceName = "_mi-connect._udp.local";
    public const string LyraServiceName = "_lyra-mdns._udp.local";

    public static byte[] Create(bool requestUnicastResponse = true) =>
        Create(ServiceName, requestUnicastResponse);

    public static byte[] Create(string serviceName, bool requestUnicastResponse = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        using var stream = new MemoryStream();
        stream.Write(new byte[4]); // Transaction ID and flags are zero for mDNS queries.
        WriteUInt16(stream, 1); // QDCOUNT
        stream.Write(new byte[6]);
        WriteName(stream, serviceName.TrimEnd('.'));
        WriteUInt16(stream, 12); // PTR
        WriteUInt16(stream, requestUnicastResponse ? (ushort)0x8001 : (ushort)1); // QU + IN
        return stream.ToArray();
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "An mDNS label must contain between 1 and 63 bytes.");
            }

            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
