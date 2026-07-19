using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayMdnsDevice(
    string InstanceName,
    string FriendlyName,
    string? HostName,
    IPAddress? Address,
    int? Port,
    IReadOnlyDictionary<string, string> TxtRecords)
{
    public MiPlayMdnsCapabilities Capabilities => MiPlayMdnsCapabilities.Parse(TxtRecords);

    public MiPlayMicoAppData? MicoAppData => Capabilities.MicoAppData;
}

public static class MiPlayMdnsMessageParser
{
    private const ushort ARecord = 1;
    private const ushort PtrRecord = 12;
    private const ushort TxtRecord = 16;
    private const ushort SrvRecord = 33;

    public static IReadOnlyList<MiPlayMdnsDevice> Parse(
        ReadOnlySpan<byte> packet,
        IPAddress? sourceAddress = null) =>
        Parse(packet, MiPlayMdnsQuery.ServiceName, sourceAddress);

    public static IReadOnlyList<MiPlayMdnsDevice> Parse(
        ReadOnlySpan<byte> packet,
        string serviceName,
        IPAddress? sourceAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        serviceName = serviceName.TrimEnd('.');
        if (packet.Length < 12)
        {
            return [];
        }

        var data = packet.ToArray();
        var questionCount = ReadUInt16(data, 4);
        var answerCount = ReadUInt16(data, 6);
        var authorityCount = ReadUInt16(data, 8);
        var additionalCount = ReadUInt16(data, 10);
        var offset = 12;

        try
        {
            for (var index = 0; index < questionCount; index++)
            {
                ReadName(data, ref offset);
                EnsureAvailable(data, offset, 4);
                offset += 4;
            }

            var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var services = new Dictionary<string, (string Target, int Port)>(StringComparer.OrdinalIgnoreCase);
            var txtRecords = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
            var recordCount = checked(answerCount + authorityCount + additionalCount);

            for (var index = 0; index < recordCount; index++)
            {
                var owner = ReadName(data, ref offset);
                EnsureAvailable(data, offset, 10);
                var type = ReadUInt16(data, offset);
                var dataLength = ReadUInt16(data, offset + 8);
                offset += 10;
                EnsureAvailable(data, offset, dataLength);
                var recordEnd = offset + dataLength;

                switch (type)
                {
                    case PtrRecord:
                    {
                        var nameOffset = offset;
                        var instance = ReadName(data, ref nameOffset);
                        if (owner.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            instances.Add(instance);
                        }
                        break;
                    }
                    case SrvRecord when dataLength >= 6:
                    {
                        var port = ReadUInt16(data, offset + 4);
                        var targetOffset = offset + 6;
                        var target = ReadName(data, ref targetOffset);
                        services[owner] = (target, port);
                        instances.Add(owner);
                        break;
                    }
                    case TxtRecord:
                        txtRecords[owner] = ParseTxt(data.AsSpan(offset, dataLength));
                        instances.Add(owner);
                        break;
                    case ARecord when dataLength == 4:
                        addresses[owner] = new IPAddress(data.AsSpan(offset, 4));
                        break;
                }

                offset = recordEnd;
            }

            var suffix = "." + serviceName;
            return instances
                .Where(instance => instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .Select(instance =>
                {
                    services.TryGetValue(instance, out var service);
                    txtRecords.TryGetValue(instance, out var txt);
                    txt ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var address = !string.IsNullOrWhiteSpace(service.Target) && addresses.TryGetValue(service.Target, out var found)
                        ? found
                        : sourceAddress;
                    var friendlyName = txt.TryGetValue("name", out var advertisedName) && !string.IsNullOrWhiteSpace(advertisedName)
                        ? advertisedName
                        : instance[..^suffix.Length];
                    int? port = service.Port > 0 ? service.Port : ReadTxtPort(txt);

                    return new MiPlayMdnsDevice(
                        instance,
                        friendlyName,
                        string.IsNullOrWhiteSpace(service.Target) ? null : service.Target,
                        address,
                        port,
                        txt);
                })
                .OrderBy(device => device.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (FormatException)
        {
            return [];
        }
        catch (OverflowException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> ParseTxt(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (offset + length > data.Length)
            {
                throw new FormatException("Truncated mDNS TXT record.");
            }

            var entry = Encoding.UTF8.GetString(data.Slice(offset, length));
            offset += length;
            var separator = entry.IndexOf('=');
            if (separator < 0)
            {
                result[entry] = string.Empty;
            }
            else
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return result;
    }

    private static int? ReadTxtPort(IReadOnlyDictionary<string, string> txt) =>
        txt.TryGetValue("port", out var value) && int.TryParse(value, out var port) && port is > 0 and <= ushort.MaxValue
            ? port
            : null;

    private static string ReadName(byte[] data, ref int offset)
    {
        var labels = new List<string>();
        var cursor = offset;
        var resumeOffset = -1;
        var pointerCount = 0;

        while (true)
        {
            EnsureAvailable(data, cursor, 1);
            var length = data[cursor++];
            if (length == 0)
            {
                offset = resumeOffset >= 0 ? resumeOffset : cursor;
                return string.Join('.', labels);
            }

            if ((length & 0xc0) == 0xc0)
            {
                EnsureAvailable(data, cursor, 1);
                var pointer = ((length & 0x3f) << 8) | data[cursor++];
                if (pointer >= data.Length || ++pointerCount > 32)
                {
                    throw new FormatException("Invalid mDNS name compression pointer.");
                }

                resumeOffset = resumeOffset >= 0 ? resumeOffset : cursor;
                cursor = pointer;
                continue;
            }

            if ((length & 0xc0) != 0 || length > 63)
            {
                throw new FormatException("Invalid mDNS label length.");
            }

            EnsureAvailable(data, cursor, length);
            labels.Add(Encoding.UTF8.GetString(data, cursor, length));
            cursor += length;
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        EnsureAvailable(data, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    }

    private static void EnsureAvailable(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new FormatException("Truncated mDNS packet.");
        }
    }
}
