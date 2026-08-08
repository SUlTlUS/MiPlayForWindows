using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityDeviceServiceQueryStateTests
{
    [Fact]
    public void NativeDeviceServiceAddressesAndStringsMatchStaticEvidence()
    {
        Assert.Equal(0x882550, MiPlayContinuityDeviceServiceQueryState.NativeGetDeviceInfoJniAddress);
        Assert.Equal(0x8834A4, MiPlayContinuityDeviceServiceQueryState.NativeGetServiceListJniAddress);

        Assert.Equal(0xFE8B6, MiPlayContinuityDeviceServiceQueryState.NativeGetDeviceInfoApiStringOffset);
        Assert.Equal(0xFEA01, MiPlayContinuityDeviceServiceQueryState.NativeGetServiceListApiStringOffset);
        Assert.Equal(0x145750, MiPlayContinuityDeviceServiceQueryState.NativeJniDeviceInfoV2NativeToJavaStringOffset);
        Assert.Equal(0x1462B2, MiPlayContinuityDeviceServiceQueryState.NativeGetDeviceInfoSymbolStringOffset);
        Assert.Equal(0x146592, MiPlayContinuityDeviceServiceQueryState.NativeGetServiceListSymbolStringOffset);

        Assert.Equal(0, MiPlayContinuityDeviceServiceQueryState.ResultSuccessCode);
        Assert.Equal(-1, MiPlayContinuityDeviceServiceQueryState.ResultFailCode);
        Assert.Equal(0x2712, MiPlayContinuityDeviceServiceQueryState.NativeInvalidArgumentErrorCode);
    }

    [Fact]
    public void DeviceServiceResultBundleShapeIsDistinctFromNetBusGenericCallback()
    {
        Assert.Equal("result", MiPlayContinuityDeviceServiceQueryState.ResultBundleKey);
        Assert.Equal("message", MiPlayContinuityDeviceServiceQueryState.ErrorMessageBundleKey);
        Assert.Equal("data", MiPlayContinuityDeviceServiceQueryState.NetBusGenericCallbackDataBundleKey);
    }

    [Fact]
    public void DeviceInfoFieldShapeIncludesBaseFieldsAndV2SwitchState()
    {
        Assert.Equal(12, MiPlayContinuityDeviceServiceQueryState.DeviceInfoBaseFieldCount);
        Assert.Equal(4, MiPlayContinuityDeviceServiceQueryState.DeviceInfoV2LocalVersion);
        Assert.Equal(5, MiPlayContinuityDeviceServiceQueryState.DeviceInfoV2ExtraStateFieldCount);
    }

    [Fact]
    public void GetDeviceInfoRequiresBinderUidDeviceIdResultReceiverNativeSuccessAndDeviceInfoV2()
    {
        var accepted = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);

        Assert.True(accepted.CanProceed);

        var missingUid = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: false,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);
        var missingDeviceId = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);
        var missingReceiver = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: false,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);
        var nativeFailure = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: false,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);
        var missingData = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: false),
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2);

        Assert.False(missingUid.CanProceed);
        Assert.False(missingDeviceId.CanProceed);
        Assert.False(missingReceiver.CanProceed);
        Assert.False(nativeFailure.CanProceed);
        Assert.False(missingData.CanProceed);
    }

    [Fact]
    public void LegacyGetDeviceInfoConvertsDeviceInfoV2ToDeviceInfoButStillUsesBinderResultReceiver()
    {
        var accepted = MiPlayContinuityDeviceServiceQueryState.EvaluateGetDeviceInfoQuery(
            new MiPlayContinuityDeviceInfoQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true,
                DeviceInfoV2Provided: true),
            MiPlayContinuityDeviceInfoQueryShape.LegacyDeviceInfo);

        Assert.True(accepted.CanProceed);
        Assert.Contains("converted from DeviceInfoV2", accepted.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GetServiceListRequiresBinderUidDeviceIdResultReceiverAndNativeSuccessButAllowsEmptyNativeData()
    {
        var accepted = MiPlayContinuityDeviceServiceQueryState.EvaluateGetServiceListQuery(
            new MiPlayContinuityServiceListQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true));

        Assert.True(accepted.CanProceed);
        Assert.Contains("empty list", accepted.Reason, StringComparison.Ordinal);

        var missingDeviceId = MiPlayContinuityDeviceServiceQueryState.EvaluateGetServiceListQuery(
            new MiPlayContinuityServiceListQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: true));
        var nativeFailure = MiPlayContinuityDeviceServiceQueryState.EvaluateGetServiceListQuery(
            new MiPlayContinuityServiceListQueryPrerequisites(
                BinderCallingUidAvailable: true,
                DeviceId: "netbus-device-id",
                ResultReceiverProvided: true,
                NativeResultReturned: true,
                NativeResultSuccess: false));

        Assert.False(missingDeviceId.CanProceed);
        Assert.False(nativeFailure.CanProceed);
    }

    [Fact]
    public void DeviceServiceQueriesDoNotBecomeLegacyTcp8899Commands()
    {
        Assert.False(MiPlayContinuityDeviceServiceQueryState.IsLegacyTcp8899GetDeviceInfoCommand(
            MiPlayContinuityDeviceInfoQueryShape.DeviceInfoV2,
            MiPlayProtocolConstants.GetDeviceInfoCommand));

        var continuityPrerequisites = new MiPlayContinuityDeviceInfoQueryPrerequisites(
            BinderCallingUidAvailable: true,
            DeviceId: "netbus-device-id",
            ResultReceiverProvided: true,
            NativeResultReturned: true,
            NativeResultSuccess: true,
            DeviceInfoV2Provided: true);
        var postAuthPrerequisites = new MiPlayPostAuthGetDeviceInfoPrerequisites(
            MutualSafetyAuthVerified: true,
            CommandSessionListenerRegisteredBeforeSafetyDone: true,
            DealSafetyDoneListenerEventDelivered: true,
            JavaOnSuccessDispatched: true,
            SourceIdentityAvailable: true,
            DeviceContextAvailable: true,
            ConnectionMode: MiPlayPostAuthConnectionMode.LegacyTcp8899,
            NextCommandSequence: 4,
            ReadOnlyProbeBoundary: true);

        Assert.False(MiPlayContinuityDeviceServiceQueryState.CanReplaceLegacyTcp8899GetDeviceInfo(
            continuityPrerequisites,
            postAuthPrerequisites));
    }
}
