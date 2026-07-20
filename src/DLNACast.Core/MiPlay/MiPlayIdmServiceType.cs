using System.Globalization;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Offline parser for IDM/ServiceManager service type URNs observed in Mi
/// Connect Service native libraries. These URNs identify IDM service types;
/// they are not Continuity ServiceName strings and are never sent by Probe.
/// </summary>
public sealed record MiPlayIdmServiceType(
    string? VendorNamespace,
    string ServiceName,
    int TypeId,
    string Version)
{
    public const string UrnScheme = "urn";
    public const string AiotSpec = "aiot-spec-v3";
    public const string ServiceSegment = "service";

    public bool HasVendorNamespace => VendorNamespace is not null;

    public string ToUrn() => VendorNamespace is null
        ? $"{UrnScheme}:{AiotSpec}:{ServiceSegment}:{ServiceName}:{TypeId:00000000}:{Version}"
        : $"{UrnScheme}:{AiotSpec}:{VendorNamespace}:{ServiceSegment}:{ServiceName}:{TypeId:00000000}:{Version}";

    public static bool TryParse(string? value, out MiPlayIdmServiceType? serviceType)
    {
        serviceType = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length is not 6 and not 7 ||
            !string.Equals(parts[0], UrnScheme, StringComparison.Ordinal) ||
            !string.Equals(parts[1], AiotSpec, StringComparison.Ordinal))
        {
            return false;
        }

        var offset = 2;
        string? vendorNamespace = null;
        if (!string.Equals(parts[offset], ServiceSegment, StringComparison.Ordinal))
        {
            vendorNamespace = parts[offset++];
        }

        if (parts.Length != offset + 4 ||
            string.IsNullOrEmpty(vendorNamespace) && parts.Length == 7 ||
            !string.Equals(parts[offset], ServiceSegment, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(parts[offset + 1]) ||
            string.IsNullOrEmpty(parts[offset + 3]) ||
            parts[offset + 2].Length != 8 ||
            !int.TryParse(parts[offset + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var typeId))
        {
            return false;
        }

        serviceType = new MiPlayIdmServiceType(
            vendorNamespace,
            parts[offset + 1],
            typeId,
            parts[offset + 3]);
        return true;
    }
}

public static class MiPlayIdmServiceTypes
{
    public const string XiaomiIdmVendorNamespace = "com.mi.idm";
    public const string MiPlayAudioServiceName = "miplay-audio";
    public const int MiPlayAudioTypeId = 17_803;
    public const string MiPlayAudioVersion = "1.0";
    public const string MiPlayAudioUrn =
        "urn:aiot-spec-v3:com.mi.idm:service:miplay-audio:00017803:1.0";

    /// <summary>
    /// Contiguous ServiceTypeIds.cc rodata strings observed in
    /// libidmservicemgr.so from Mi Connect Service 5.1.251.10.
    /// </summary>
    public static IReadOnlyList<string> ObservedServiceTypeIdsTable { get; } =
    [
        "urn:aiot-spec-v3:service:multi-screen-collaboration:00000001:1",
        "urn:aiot-spec-v3:com.mi.idm:service:micast-tv:00017802:1.0",
        MiPlayAudioUrn,
        "urn:aiot-spec-v3:service:input:00000001:1",
        "urn:aiot-spec-v3:service:handoff:00000001:1",
        "urn:aiot-spec-v3:service:idm-test:00000001:1",
        "urn:aiot-spec-v3:com.mi.idm:service:notification-local:00017804:1.0",
        "urn:aiot-spec-v3:com.mi.idm:service:mihome-hub:00017805:1.0",
        "urn:aiot-spec-v3:com.mi.idm:service:milink:00017806:1.0",
    ];
}