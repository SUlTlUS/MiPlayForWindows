using System.Globalization;

namespace DLNACast.Core.MiPlay;

public enum MiConnectSecurityMode : byte
{
    None = 0,
    Communication = 1,
    Transport = 2,
    CommunicationAndTransport = 3,
}

/// <summary>
/// Decodes the public TXT capabilities advertised by Xiaomi's
/// _mi-connect._udp.local service. This does not establish a trusted session.
/// </summary>
public sealed record MiPlayMdnsCapabilities(
    int PackedVersion,
    int VersionMajor,
    int VersionMinor,
    IReadOnlyList<int> ApplicationIds,
    byte[] Flags,
    byte[] IdHash,
    int? DeviceType,
    MiConnectSecurityMode? SecurityMode,
    IReadOnlyDictionary<int, byte[]> ApplicationData,
    MiPlayMicoAppData? MicoAppData)
{
    public const int MiPlayAudioApplicationId = 5;
    public const string MiPlayAudioServiceType =
        "urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0";

    public bool SupportsMiPlayAudio => ApplicationIds.Contains(MiPlayAudioApplicationId);

    public bool RequiresTransportSecurity =>
        SecurityMode is MiConnectSecurityMode.Transport or MiConnectSecurityMode.CommunicationAndTransport;

    public MiPlayLegacyAppData? MiPlayAudioAppData =>
        ApplicationData.TryGetValue(MiPlayAudioApplicationId, out var data)
            ? MiPlayLegacyAppData.Parse(data)
            : null;

    public static MiPlayMdnsCapabilities Parse(IReadOnlyDictionary<string, string> txtRecords)
    {
        ArgumentNullException.ThrowIfNull(txtRecords);

        var packedVersion = ReadInteger(txtRecords, "version") ?? 0;
        var applicationIds = txtRecords.TryGetValue("apps", out var apps)
            ? ParseApplicationIds(apps)
            : [];
        var flags = ReadBase64(txtRecords, "flags");
        var idHash = ReadBase64(txtRecords, "idHash");
        var deviceType = ReadInteger(txtRecords, "dev");
        var securityValue = ReadInteger(txtRecords, "sec");
        var securityMode = securityValue is >= 0 and <= 3
            ? (MiConnectSecurityMode?)securityValue.Value
            : null;
        IReadOnlyDictionary<int, byte[]> applicationData = new Dictionary<int, byte[]>();
        if (txtRecords.TryGetValue("appsData", out var appDataValue))
        {
            MiPlayMdnsAppData.TryParse(appDataValue, applicationIds, out applicationData);
        }

        var micoAppData = applicationData.TryGetValue(MiPlayAudioApplicationId, out var miPlayData) &&
                          MiPlayMicoAppData.TryParse(miPlayData, out var appData)
            ? appData
            : null;

        return new MiPlayMdnsCapabilities(
            packedVersion,
            packedVersion >> 16,
            packedVersion & ushort.MaxValue,
            applicationIds,
            flags,
            idHash,
            deviceType,
            securityMode,
            applicationData,
            micoAppData);
    }

    private static IReadOnlyList<int> ParseApplicationIds(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        var result = new List<int>();
        foreach (var item in trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var applicationId) &&
                applicationId >= 0)
            {
                result.Add(applicationId);
            }
        }

        return result;
    }

    private static int? ReadInteger(IReadOnlyDictionary<string, string> records, string key) =>
        records.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static byte[] ReadBase64(IReadOnlyDictionary<string, string> records, string key)
    {
        if (!records.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
