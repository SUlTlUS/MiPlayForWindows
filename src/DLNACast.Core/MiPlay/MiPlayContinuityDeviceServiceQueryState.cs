namespace DLNACast.Core.MiPlay;

public enum MiPlayContinuityDeviceInfoQueryShape
{
    LegacyDeviceInfo = 1,
    DeviceInfoV2 = 2,
}

public sealed record MiPlayContinuityDeviceInfoQueryPrerequisites(
    bool BinderCallingUidAvailable,
    string? DeviceId,
    bool ResultReceiverProvided,
    bool NativeResultReturned,
    bool NativeResultSuccess,
    bool DeviceInfoV2Provided);

public sealed record MiPlayContinuityServiceListQueryPrerequisites(
    bool BinderCallingUidAvailable,
    string? DeviceId,
    bool ResultReceiverProvided,
    bool NativeResultReturned,
    bool NativeResultSuccess);

/// <summary>
/// Offline model for com.xiaomi.continuity.netbus.service.DeviceService in Mi
/// Connect Service 5.1.251.10. It distinguishes the Binder/ResultReceiver
/// DeviceService queries from the legacy TCP 8899 SafetyData getDeviceInfo
/// command pair.
/// </summary>
public static class MiPlayContinuityDeviceServiceQueryState
{
    public const long NativeGetDeviceInfoJniAddress = 0x882550;
    public const long NativeGetServiceListJniAddress = 0x8834A4;

    public const long NativeGetDeviceInfoApiStringOffset = 0xFE8B6;
    public const long NativeGetServiceListApiStringOffset = 0xFEA01;
    public const long NativeJniDeviceInfoV2NativeToJavaStringOffset = 0x145750;
    public const long NativeGetDeviceInfoSymbolStringOffset = 0x1462B2;
    public const long NativeGetServiceListSymbolStringOffset = 0x146592;

    public const int ResultSuccessCode = 0;
    public const int ResultFailCode = -1;
    public const int NativeInvalidArgumentErrorCode = 0x2712;

    public const string ResultBundleKey = "result";
    public const string ErrorMessageBundleKey = "message";
    public const string NetBusGenericCallbackDataBundleKey = "data";

    public const int DeviceInfoBaseFieldCount = 12;
    public const int DeviceInfoV2LocalVersion = 4;
    public const int DeviceInfoV2ExtraStateFieldCount = 5;

    public static MiPlayIdmStateDecision EvaluateGetDeviceInfoQuery(
        MiPlayContinuityDeviceInfoQueryPrerequisites prerequisites,
        MiPlayContinuityDeviceInfoQueryShape queryShape)
    {
        if (!prerequisites.BinderCallingUidAvailable)
        {
            return new MiPlayIdmStateDecision(false, "DeviceService cannot call nativeGetDeviceInfo without Binder calling UID.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "DeviceService getDeviceInfo requires a non-empty deviceId.");
        }

        if (!prerequisites.ResultReceiverProvided)
        {
            return new MiPlayIdmStateDecision(false, "DeviceService getDeviceInfo requires a ResultReceiver.");
        }

        if (!prerequisites.NativeResultReturned)
        {
            return new MiPlayIdmStateDecision(false, "nativeGetDeviceInfo has not returned a Result<DeviceInfoV2>.");
        }

        if (!prerequisites.NativeResultSuccess)
        {
            return new MiPlayIdmStateDecision(false, "nativeGetDeviceInfo returned a non-success Result.");
        }

        if (!prerequisites.DeviceInfoV2Provided)
        {
            return new MiPlayIdmStateDecision(false, "nativeGetDeviceInfo did not provide DeviceInfoV2 data.");
        }

        var dataShape = queryShape == MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2
            ? "DeviceInfoV2"
            : "DeviceInfo converted from DeviceInfoV2";
        return new MiPlayIdmStateDecision(true, $"DeviceService can deliver {dataShape} through ResultReceiver key '{ResultBundleKey}'.");
    }

    public static MiPlayIdmStateDecision EvaluateGetServiceListQuery(
        MiPlayContinuityServiceListQueryPrerequisites prerequisites)
    {
        if (!prerequisites.BinderCallingUidAvailable)
        {
            return new MiPlayIdmStateDecision(false, "DeviceService cannot call nativeGetServiceList without Binder calling UID.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "DeviceService getServiceList requires a non-empty deviceId.");
        }

        if (!prerequisites.ResultReceiverProvided)
        {
            return new MiPlayIdmStateDecision(false, "DeviceService getServiceList requires a ResultReceiver.");
        }

        if (!prerequisites.NativeResultReturned)
        {
            return new MiPlayIdmStateDecision(false, "nativeGetServiceList has not returned a Result<String[]>.");
        }

        if (!prerequisites.NativeResultSuccess)
        {
            return new MiPlayIdmStateDecision(false, "nativeGetServiceList returned a non-success Result.");
        }

        return new MiPlayIdmStateDecision(true, "DeviceService can deliver the service list through ResultReceiver key 'result', using an empty list when native data is null.");
    }

    public static bool IsLegacyTcp8899GetDeviceInfoCommand(
        MiPlayContinuityDeviceInfoQueryShape _,
        ushort command) =>
        command == MiPlayProtocolConstants.GetDeviceInfoCommand &&
        false;

    public static bool CanReplaceLegacyTcp8899GetDeviceInfo(
        MiPlayContinuityDeviceInfoQueryPrerequisites prerequisites,
        MiPlayPostAuthGetDeviceInfoPrerequisites postAuthPrerequisites) =>
        prerequisites.BinderCallingUidAvailable &&
        !string.IsNullOrWhiteSpace(prerequisites.DeviceId) &&
        prerequisites.ResultReceiverProvided &&
        postAuthPrerequisites.ConnectionMode == MiPlayPostAuthConnectionMode.LegacyTcp8899 &&
        false;
}
