using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayJavaOnSuccessGetDeviceInfoBoundaryTests
{
    [Fact]
    public void ConstantsCaptureAsyncResultAndDeviceServiceBinderEvidence()
    {
        Assert.Equal("com.xiaomi.continuity.netbus.AsyncResult", MiPlayJavaOnSuccessGetDeviceInfoBoundary.AsyncResultClassName);
        Assert.Equal("setSuccessListener", MiPlayJavaOnSuccessGetDeviceInfoBoundary.AsyncResultSuccessListenerMethod);
        Assert.Equal("success", MiPlayJavaOnSuccessGetDeviceInfoBoundary.AsyncResultSuccessMethod);
        Assert.Equal("OnSuccessListener.onSuccess", MiPlayJavaOnSuccessGetDeviceInfoBoundary.AsyncResultOnSuccessCallback);

        Assert.Equal("com.xiaomi.continuity.netbus.service.DeviceService", MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceClassName);
        Assert.Equal("com.xiaomi.continuity.netbus.IDeviceService", MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceBinderDescriptor);
        Assert.Equal(1, MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceGetDeviceInfoTransaction);
        Assert.Equal(10, MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceGetDeviceInfoV2Transaction);
        Assert.Equal("result", MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceResultBundleKey);
        Assert.Equal("message", MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceErrorMessageBundleKey);
        Assert.Equal("DeviceManagerNative.nativeGetDeviceInfo", MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceManagerNativeGetDeviceInfoMethod);

        Assert.Equal("DealSafetyDone", MiPlayJavaOnSuccessGetDeviceInfoBoundary.MissingDealSafetyDoneSymbol);
        Assert.Equal("SafetyDone", MiPlayJavaOnSuccessGetDeviceInfoBoundary.MissingSafetyDoneSymbol);
        Assert.Equal("SafetyAuth", MiPlayJavaOnSuccessGetDeviceInfoBoundary.MissingSafetyAuthSymbol);
        Assert.Equal("0x001e", MiPlayJavaOnSuccessGetDeviceInfoBoundary.LegacyTcpGetDeviceInfoCommandName);
        Assert.Equal("0x001f", MiPlayJavaOnSuccessGetDeviceInfoBoundary.LegacyTcpGetDeviceInfoAcknowledgementName);
    }

    [Fact]
    public void AsyncResultOnSuccessRequiresCompletionSuccessListenerExecutorAndPayload()
    {
        var accepted = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: true,
                ResultSucceeded: true,
                SuccessListenerRegistered: true,
                ExecutorAvailable: true,
                PayloadAvailable: true));

        Assert.True(accepted.CanProceed);

        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: false,
                ResultSucceeded: true,
                SuccessListenerRegistered: true,
                ExecutorAvailable: true,
                PayloadAvailable: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: true,
                ResultSucceeded: false,
                SuccessListenerRegistered: true,
                ExecutorAvailable: true,
                PayloadAvailable: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: true,
                ResultSucceeded: true,
                SuccessListenerRegistered: false,
                ExecutorAvailable: true,
                PayloadAvailable: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: true,
                ResultSucceeded: true,
                SuccessListenerRegistered: true,
                ExecutorAvailable: false,
                PayloadAvailable: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateAsyncResultOnSuccess(
            new MiPlayAsyncResultCallbackPrerequisites(
                ResultCompleted: true,
                ResultSucceeded: true,
                SuccessListenerRegistered: true,
                ExecutorAvailable: true,
                PayloadAvailable: false)).CanProceed);
    }

    [Fact]
    public void DeviceServiceGetDeviceInfoRequiresDeviceIdResultReceiverNativeManagerAndSuccess()
    {
        var accepted = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateDeviceServiceGetDeviceInfo(
            new MiPlayDeviceServiceGetDeviceInfoPrerequisites(
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeDeviceManagerAvailable: true,
                NativeGetDeviceInfoReturnedSuccess: true,
                DeviceInfoV2Requested: true));

        Assert.True(accepted.CanProceed);
        Assert.Contains("DeviceInfoV2", accepted.Reason, StringComparison.Ordinal);

        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateDeviceServiceGetDeviceInfo(
            new MiPlayDeviceServiceGetDeviceInfoPrerequisites(
                DeviceId: "",
                ResultReceiverProvided: true,
                NativeDeviceManagerAvailable: true,
                NativeGetDeviceInfoReturnedSuccess: true,
                DeviceInfoV2Requested: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateDeviceServiceGetDeviceInfo(
            new MiPlayDeviceServiceGetDeviceInfoPrerequisites(
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: false,
                NativeDeviceManagerAvailable: true,
                NativeGetDeviceInfoReturnedSuccess: true,
                DeviceInfoV2Requested: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateDeviceServiceGetDeviceInfo(
            new MiPlayDeviceServiceGetDeviceInfoPrerequisites(
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeDeviceManagerAvailable: false,
                NativeGetDeviceInfoReturnedSuccess: true,
                DeviceInfoV2Requested: true)).CanProceed);
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateDeviceServiceGetDeviceInfo(
            new MiPlayDeviceServiceGetDeviceInfoPrerequisites(
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeDeviceManagerAvailable: true,
                NativeGetDeviceInfoReturnedSuccess: false,
                DeviceInfoV2Requested: true)).CanProceed);
    }

    [Fact]
    public void CurrentMiConnectServiceJavaDoesNotProveSafetyDoneOnSuccessGetDeviceInfoTrace()
    {
        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.CurrentMiConnectServiceJavaCanProveSafetyDoneGetDeviceInfoChain());

        var decision = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            CreateCompleteTraceEvidence() with { DealSafetyDoneSymbolObserved = false });

        Assert.False(decision.CanProceed);
        Assert.Contains("DealSafetyDone", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyDoneTraceRequiresOnSuccessToCallDeviceServiceAndCarrySourceDeviceContext()
    {
        var complete = CreateCompleteTraceEvidence();

        Assert.True(MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(complete).CanProceed);

        var missingCall = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            complete with { OnSuccessCallsDeviceServiceGetDeviceInfo = false });
        var missingCaller = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            complete with { CallerPackageIdentityObserved = false });
        var missingDeviceId = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            complete with { DeviceIdFromDiscoveryContextObserved = false });
        var missingResultReceiver = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            complete with { ResultReceiverParsesDeviceInfo = false });

        Assert.False(missingCall.CanProceed);
        Assert.Contains("DeviceService.getDeviceInfo", missingCall.Reason, StringComparison.Ordinal);
        Assert.False(missingCaller.CanProceed);
        Assert.Contains("source caller", missingCaller.Reason, StringComparison.Ordinal);
        Assert.False(missingDeviceId.CanProceed);
        Assert.Contains("discovery DeviceInfoV2", missingDeviceId.Reason, StringComparison.Ordinal);
        Assert.False(missingResultReceiver.CanProceed);
        Assert.Contains("ResultReceiver", missingResultReceiver.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyDoneTraceStillRequiresExplicitLegacyTcpCommandBridge()
    {
        var missingBridge = MiPlayJavaOnSuccessGetDeviceInfoBoundary.EvaluateSafetyDoneOnSuccessGetDeviceInfoTrace(
            CreateCompleteTraceEvidence() with { LegacyTcp8899CommandBridgeObserved = false });

        Assert.False(missingBridge.CanProceed);
        Assert.Contains("legacy TCP 8899", missingBridge.Reason, StringComparison.Ordinal);

        Assert.False(MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceBinderCanExplainLegacyTcp8899GetDeviceInfo(
            binderDeviceInfoQueryObserved: true,
            legacyTcpCommandBridgeObserved: false));
        Assert.True(MiPlayJavaOnSuccessGetDeviceInfoBoundary.DeviceServiceBinderCanExplainLegacyTcp8899GetDeviceInfo(
            binderDeviceInfoQueryObserved: true,
            legacyTcpCommandBridgeObserved: true));
    }

    private static MiPlaySafetyDoneOnSuccessGetDeviceInfoTraceEvidence CreateCompleteTraceEvidence() =>
        new(
            DealSafetyDoneSymbolObserved: true,
            SafetyAuthSuccessCallbackObserved: true,
            JavaOnSuccessCallbackObserved: true,
            OnSuccessCallsDeviceServiceGetDeviceInfo: true,
            CallerPackageIdentityObserved: true,
            DeviceIdFromDiscoveryContextObserved: true,
            ResultReceiverParsesDeviceInfo: true,
            LegacyTcp8899CommandBridgeObserved: true);
}
