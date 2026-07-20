namespace DLNACast.Core.MiPlay;

/// <summary>
/// Offline model of Mi Connect Service static networking listener gates.
/// This mirrors JADX/native evidence for diagnostics only; it is not used by
/// outbound S12 probes and must not be treated as authorization to send frames.
/// </summary>
public static class MiPlayContinuityStaticNetworking
{
    public const string StaticNetworkingServiceListResourceKey = "static_networking_service_list";
    public const string NetworkingServiceListRootTag = "networking_service_list";
    public const string ServiceTag = "service";

    public const int SameAccountTrustLevel = 16;
    public const int DefaultTrustGroupTrustLevel = 32;
    public const int SharedAccountTrustLevel = 40;
    public const int EveryOneTrustLevel = 48;

    public static int ParseNetworkingServiceTrustLevel(string? trustLevelAttribute) =>
        trustLevelAttribute switch
        {
            "sameAccount" => SameAccountTrustLevel,
            "everyOne" => EveryOneTrustLevel,
            "sharedAccount" => SharedAccountTrustLevel,
            "trustGroup" or null => DefaultTrustGroupTrustLevel,
            _ => DefaultTrustGroupTrustLevel,
        };

    public static bool ShouldNotifyConnectionInitiated(
        int incomingConnectionTrustLevel,
        int componentTrustLevel) =>
        incomingConnectionTrustLevel <= componentTrustLevel;
}

public sealed record MiPlayContinuityServerConnectionOptions(
    MiPlayContinuityMediumType MediumMask,
    bool ConfirmRequired,
    int TrustLevel)
{
    public static MiPlayContinuityServerConnectionOptions ForRegisterNetbusListener(int trustLevel) =>
        new(
            MiPlayContinuityMediumTypes.RegisterNetbusListenerDefaultServerMediumMask,
            ConfirmRequired: true,
            trustLevel);
}

public static class MiPlayContinuityNativeVtableEvidence
{
    public const long ConnectionManagerWrapperVtable = 0xF92880;
    public const int ConnectionObjectVptrBias = 0x10;
    public const int RegisterNetbusListenerConnectionVtableSlotOffset = 0x50;
    public const long RegisterConnectionListenerRelocationOffset = 0xF928E0;
    public const long RegisterConnectionListenerSymbolAddress = 0x94E278;

    public static long RegisterNetbusListenerResolvedRelocationOffset =>
        ConnectionManagerWrapperVtable +
        ConnectionObjectVptrBias +
        RegisterNetbusListenerConnectionVtableSlotOffset;
}