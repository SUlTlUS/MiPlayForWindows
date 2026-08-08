using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Builds a distinct, pre-auth-only MiPlay receiver identity for capturing
/// frames that an authorized phone voluntarily sends. It never reuses a real
/// speaker identity and only permits the verified legacy 0x0028 challenge.
/// </summary>
public sealed record MiPlayPassiveSenderCaptureProfile(
    IPAddress Address,
    string FriendlyName,
    string InstanceLabel,
    string HostLabel,
    Guid DeviceId)
{
    public const int MdnsPort = 5353;
    public const int AdvertisedCoapPort = 56_666;
    public const int AdvertisedControlPort = MiPlayProtocolConstants.DefaultControlPort;
    public const ushort ChallengeSequence = 0;
    public const string ChallengeText = "123456789";
    public const string DefaultFriendlyName = "DLNACast 真机捕获器";
    public const string DefaultInstanceLabel = "DLNACast-Capture";
    public const string DefaultHostLabel = "dlnacast-capture";
    public static readonly Guid DefaultDeviceId = new("7e6d22d5-5cb9-4e3c-a95f-110fd8f53d42");

    public string InstanceName => $"{InstanceLabel}.{MiPlayMdnsQuery.ServiceName}";
    public string HostName => $"{HostLabel}.local";

    public static MiPlayPassiveSenderCaptureProfile CreateDefault(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("The passive MiPlay capture profile requires a LAN IPv4 address.", nameof(address));
        }

        return new MiPlayPassiveSenderCaptureProfile(
            address,
            DefaultFriendlyName,
            DefaultInstanceLabel,
            DefaultHostLabel,
            DefaultDeviceId);
    }

    public byte[] BuildMdnsAnnouncement()
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0x8400);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 3);

        WriteRecord(stream, MiPlayMdnsQuery.ServiceName, 12, record =>
            WriteName(record, InstanceName));
        WriteRecord(stream, InstanceName, 33, record =>
        {
            WriteUInt16(record, 0);
            WriteUInt16(record, 0);
            WriteUInt16(record, AdvertisedCoapPort);
            WriteName(record, HostName);
        });
        WriteRecord(stream, InstanceName, 16, record =>
        {
            WriteTxt(record, $"name={FriendlyName}");
            WriteTxt(record, "version=65545");
            WriteTxt(record, "apps=[5]");
            WriteTxt(record, $"appsData={BuildAppData()}");
            WriteTxt(record, "dev=4");
            WriteTxt(record, "sec=2");
            WriteTxt(record, "flags=Ag==");
            WriteTxt(record, "idHash=RExO");
        });
        WriteRecord(stream, HostName, 1, record => record.Write(Address.GetAddressBytes()));
        return stream.ToArray();
    }

    public byte[] BuildLegacyChallengeFrame() =>
        MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            ChallengeSequence,
            Encoding.ASCII.GetBytes(ChallengeText));

    public static bool IsPermittedOutboundCommand(ushort command) =>
        command == MiPlayProtocolConstants.LegacySafetyChallengeCommand;

    private string BuildAppData()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\n\t\"mico\": {\n\t\t\"device_id\": \"" +
            DeviceId.ToString("D") +
            "\"\n\t}\n}\n");
        var appPayload = new byte[checked(25 + json.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(appPayload, 1155);
        BinaryPrimitives.WriteUInt16BigEndian(appPayload.AsSpan(2), AdvertisedControlPort);

        var deviceHash = SHA256.HashData(DeviceId.ToByteArray());
        appPayload[4] = (byte)((deviceHash[0] & 0xfc) | 0x02);
        deviceHash.AsSpan(1, 5).CopyTo(appPayload.AsSpan(5));
        appPayload[24] = 0;
        json.CopyTo(appPayload.AsSpan(25));

        if (appPayload.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("MiPlay mDNS appData exceeds the one-byte application payload limit.");
        }

        var container = new byte[checked(3 + appPayload.Length)];
        container[0] = 0x81;
        container[1] = 0;
        container[2] = (byte)appPayload.Length;
        appPayload.CopyTo(container.AsSpan(3));
        return Convert.ToBase64String(container);
    }

    private static void WriteRecord(Stream stream, string owner, ushort type, Action<MemoryStream> writeData)
    {
        WriteName(stream, owner);
        WriteUInt16(stream, type);
        WriteUInt16(stream, 0x8001);
        WriteUInt32(stream, 120);
        using var data = new MemoryStream();
        writeData(data);
        WriteUInt16(stream, checked((ushort)data.Length));
        data.Position = 0;
        data.CopyTo(stream);
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
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

    private static void WriteTxt(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "An mDNS TXT entry cannot exceed 255 bytes.");
        }

        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
