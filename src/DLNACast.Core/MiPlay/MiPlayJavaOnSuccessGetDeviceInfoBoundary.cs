namespace DLNACast.Core.MiPlay;

public sealed record MiPlayAsyncResultCallbackPrerequisites(
    bool ResultCompleted,
    bool ResultSucceeded,
    bool SuccessListenerRegistered,
    bool ExecutorAvailable,
    bool PayloadAvailable);

public sealed record MiPlayDeviceServiceGetDeviceInfoPrerequisites(
    string? DeviceId,
    bool ResultReceiverProvided,
    bool NativeDeviceManagerAvailable,
    bool NativeGetDeviceInfoReturnedSuccess,
    bool DeviceInfoV2Requested);

public sealed record MiPlaySafetyDoneOnSuccessGetDeviceInfoTraceEvidence(
    bool DealSafetyDoneSymbolObserved,
    bool SafetyAuthSuccessCallbackObserved,
    bool JavaOnSuccessCallbackObserved,
    bool OnSuccessCallsDeviceServiceGetDeviceInfo,
    bool CallerPackageIdentityObserved,
    bool DeviceIdFromDiscoveryContextObserved,
    bool ResultReceiverParsesDeviceInfo,
    bool LegacyTcp8899CommandBridgeObserved);

/// <summary>
/// Offline model for the Java-side difference between a generic
/// AsyncResult.onSuccess callback, the NetBus DeviceService.getDeviceInfo
/// Binder API, and the missing SafetyAuth/DealSafetyDone -> getDeviceInfo
/// source-client call chain. It never maps Binder calls to TCP 8899 commands.
/// </summary>
public static class MiPlayJavaOnSuccessGetDeviceInfoBoundary
{
    public const string AsyncResultClassName = "com.xiaomi.continuity.netbus.AsyncResult";
    public const string AsyncResultSuccessListenerMethod = "setSuccessListener";
    public const string AsyncResultSuccessMethod = "success";
    public const string AsyncResultOnSuccessCallback = "OnSuccessListener.onSuccess";

    public const string DeviceServiceClassName = "com.xiaomi.continuity.netbus.service.DeviceService";
    public const string DeviceServiceBinderDescriptor = "com.xiaomi.continuity.netbus.IDeviceService";
    public const int DeviceServiceGetDeviceInfoTransaction = 1;
    public const int DeviceServiceGetDeviceInfoV2Transaction = 10;
    public const string DeviceServiceResultBundleKey = "result";
    public const string DeviceServiceErrorMessageBundleKey = "message";
    public const string DeviceManagerNativeGetDeviceInfoMethod = "DeviceManagerNative.nativeGetDeviceInfo";

    public const string MissingDealSafetyDoneSymbol = "DealSafetyDone";
    public const string MissingSafetyDoneSymbol = "SafetyDone";
    public const string MissingSafetyAuthSymbol = "SafetyAuth";
    public const string LegacyTcpGetDeviceInfoCommandName = "0x001e";
    public const string LegacyTcpGetDeviceInfoAcknowledgementName = "0x001f";

    public static MiPlayIdmStateDecision EvaluateAsyncResultOnSuccess(
        MiPlayAsyncResultCallbackPrerequisites prerequisites)
    {
        if (!prerequisites.ResultCompleted)
        {
            return new MiPlayIdmStateDecision(false, "AsyncResult has not completed.");
        }

        if (!prerequisites.ResultSucceeded)
        {
            return new MiPlayIdmStateDecision(false, "AsyncResult completed through the error path.");
        }

        if (!prerequisites.SuccessListenerRegistered)
        {
            return new MiPlayIdmStateDecision(false, "No AsyncResult success listener is registered.");
        }

        if (!prerequisites.ExecutorAvailable)
        {
            return new MiPlayIdmStateDecision(false, "No executor is available to dispatch the success listener.");
        }

        if (!prerequisites.PayloadAvailable)
        {
            return new MiPlayIdmStateDecision(false, "AsyncResult success has no payload for the listener.");
        }

        return new MiPlayIdmStateDecision(true, "AsyncResult can dispatch the generic onSuccess callback with its payload.");
    }

    public static MiPlayIdmStateDecision EvaluateDeviceServiceGetDeviceInfo(
        MiPlayDeviceServiceGetDeviceInfoPrerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "DeviceService.getDeviceInfo requires a device id string.");
        }

        if (!prerequisites.ResultReceiverProvided)
        {
            return new MiPlayIdmStateDecision(false, "DeviceService.getDeviceInfo requires a ResultReceiver.");
        }

        if (!prerequisites.NativeDeviceManagerAvailable)
        {
            return new MiPlayIdmStateDecision(false, "DeviceManagerNative is not available for the Binder query.");
        }

        if (!prerequisites.NativeGetDeviceInfoReturnedSuccess)
        {
            return new MiPlayIdmStateDecision(false, "DeviceManagerNative.nativeGetDeviceInfo did not return success.");
        }

        var shape = prerequisites.DeviceInfoV2Requested ? "DeviceInfoV2" : "DeviceInfo";
        return new MiPlayIdmStateDecision(true, $"DeviceService can return {shape} through ResultReceiver.");
    }

    public static MiPlayIdmStateDecision EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
        MiPlaySafetyDoneOnSuccessGetDeviceInfoTraceEvidence evidence)
    {
        if (!evidence.DealSafetyDoneSymbolObserved)
        {
            return new MiPlayIdmStateDecision(false, "No DealSafetyDone/SafetyDone Java symbol was observed in the current APK source.");
        }

        if (!evidence.SafetyAuthSuccessCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "No Java SafetyAuth success callback was observed.");
        }

        if (!evidence.JavaOnSuccessCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "No Java onSuccess callback candidate was observed for this SafetyAuth path.");
        }

        if (!evidence.OnSuccessCallsDeviceServiceGetDeviceInfo)
        {
            return new MiPlayIdmStateDecision(false, "The onSuccess callback is not proven to call DeviceService.getDeviceInfo/getDeviceInfoV2.");
        }

        if (!evidence.CallerPackageIdentityObserved)
        {
            return new MiPlayIdmStateDecision(false, "The source caller package identity for getDeviceInfo is missing.");
        }

        if (!evidence.DeviceIdFromDiscoveryContextObserved)
        {
            return new MiPlayIdmStateDecision(false, "The getDeviceInfo deviceId is not proven to come from discovery DeviceInfoV2 context.");
        }

        if (!evidence.ResultReceiverParsesDeviceInfo)
        {
            return new MiPlayIdmStateDecision(false, "The Java ResultReceiver parse path for DeviceInfo is not observed.");
        }

        if (!evidence.LegacyTcp8899CommandBridgeObserved)
        {
            return new MiPlayIdmStateDecision(false, "No bridge from the Java/Binder path to legacy TCP 8899 command 0x001e was observed.");
        }

        return new MiPlayIdmStateDecision(true, "The Java SafetyDone/onSuccess/getDeviceInfo trace is complete.");
    }

    public static bool CurrentMiConnectServiceJavaCanProveSafetyDoneGetDeviceInfoChain() =>
        EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            new MiPlaySafetyDoneOnSuccessGetDeviceInfoTraceEvidence(
                DealSafetyDoneSymbolObserved: false,
                SafetyAuthSuccessCallbackObserved: false,
                JavaOnSuccessCallbackObserved: true,
                OnSuccessCallsDeviceServiceGetDeviceInfo: false,
                CallerPackageIdentityObserved: false,
                DeviceIdFromDiscoveryContextObserved: false,
                ResultReceiverParsesDeviceInfo: false,
                LegacyTcp8899CommandBridgeObserved: false)).CanProceed;

    public static bool DeviceServiceBinderCanExplainLegacyTcp8899GetDeviceInfo(
        bool binderDeviceInfoQueryObserved,
        bool legacyTcpCommandBridgeObserved) =>
        binderDeviceInfoQueryObserved &&
        legacyTcpCommandBridgeObserved;
}
