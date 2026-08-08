using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayIdmStartAdvertisingIdentityTests
{
    [Fact]
    public void NativeStartAdvertisingAddressesAndStringsMatchStaticEvidence()
    {
        Assert.Equal(0x3C118, MiPlayIdmStartAdvertisingIdentity.NativeStartAdvertisingJniAddress);
        Assert.Equal(0x92650, MiPlayIdmStartAdvertisingIdentity.NativeStartAdvertisingWorkerAddress);
        Assert.Equal(0x43BD8, MiPlayIdmStartAdvertisingIdentity.NativeStartAdvertisingPostResultHelperAddress);

        Assert.Equal(0x1A1FDF, MiPlayIdmStartAdvertisingIdentity.NativeStartAdvertisingSymbolStringOffset);
        Assert.Equal(0x1A2014, MiPlayIdmStartAdvertisingIdentity.NativeServiceIdIsNullStringOffset);
        Assert.Equal(0x1A7CBB, MiPlayIdmStartAdvertisingIdentity.NativeStartAdvertisingLogStringOffset);
        Assert.Equal(0x1AC74F, MiPlayIdmStartAdvertisingIdentity.NativeIdmAdvertisingResultServiceIdStringOffset);
        Assert.Equal(0x1B7A32, MiPlayIdmStartAdvertisingIdentity.NativeHandleStartAdvertisingStringOffset);
        Assert.Equal(0x1B7C5B, MiPlayIdmStartAdvertisingIdentity.NativeHandleStartAdvertisingUniqueServiceIdFailureStringOffset);
        Assert.Equal(0x1B7CAF, MiPlayIdmStartAdvertisingIdentity.NativeHandleStartAdvertisingUniqueServiceIdSuccessStringOffset);
    }

    [Fact]
    public void IpcOnAdvertisingResultWrapsIdmAdvertisingResultAsFieldOne()
    {
        Assert.Equal(1, MiPlayIdmStartAdvertisingIdentity.IpcOnAdvertisingResultIdmAdvertisingResultFieldNumber);
    }

    [Fact]
    public void V2StartAdvertisingReturnRequiresClientServiceProtoAppParamPrivateDataAndReturnString()
    {
        var accepted = MiPlayIdmStartAdvertisingIdentity.EvaluateV2StartAdvertisingReturn(
            new MiPlayIdmStartAdvertisingV2Prerequisites(
                ClientId: "server-proc-client",
                IdmServiceId: "seed-service-id",
                ServiceProtoSerialized: true,
                AppParamSerialized: true,
                PrivateDataSerialized: true,
                NativeReturnedString: "native-returned-string"));

        Assert.True(accepted.CanProceed);

        var missingClientId = MiPlayIdmStartAdvertisingIdentity.EvaluateV2StartAdvertisingReturn(
            new MiPlayIdmStartAdvertisingV2Prerequisites(
                ClientId: "",
                IdmServiceId: "seed-service-id",
                ServiceProtoSerialized: true,
                AppParamSerialized: true,
                PrivateDataSerialized: true,
                NativeReturnedString: "native-returned-string"));
        var missingServiceProto = MiPlayIdmStartAdvertisingIdentity.EvaluateV2StartAdvertisingReturn(
            new MiPlayIdmStartAdvertisingV2Prerequisites(
                ClientId: "server-proc-client",
                IdmServiceId: "seed-service-id",
                ServiceProtoSerialized: false,
                AppParamSerialized: true,
                PrivateDataSerialized: true,
                NativeReturnedString: "native-returned-string"));
        var missingReturnedString = MiPlayIdmStartAdvertisingIdentity.EvaluateV2StartAdvertisingReturn(
            new MiPlayIdmStartAdvertisingV2Prerequisites(
                ClientId: "server-proc-client",
                IdmServiceId: "seed-service-id",
                ServiceProtoSerialized: true,
                AppParamSerialized: true,
                PrivateDataSerialized: true,
                NativeReturnedString: ""));

        Assert.False(missingClientId.CanProceed);
        Assert.False(missingServiceProto.CanProceed);
        Assert.False(missingReturnedString.CanProceed);
    }

    [Fact]
    public void AdvertisingResultCallbackRequiresNativeCallbackParseIpcWrapperListenerSuccessAndServiceId()
    {
        var accepted = MiPlayIdmStartAdvertisingIdentity.EvaluateAdvertisingResultCallback(
            new MiPlayIdmAdvertisingResultCallbackPrerequisites(
                NativeCallbackArrived: true,
                IdmAdvertisingResultParsed: true,
                IpcOnAdvertisingResultWrapped: true,
                ServerProcCallbackAvailable: true,
                Status: 0,
                ServiceId: "advertised-service-id"));

        Assert.True(accepted.CanProceed);

        var missingCallback = MiPlayIdmStartAdvertisingIdentity.EvaluateAdvertisingResultCallback(
            new MiPlayIdmAdvertisingResultCallbackPrerequisites(
                NativeCallbackArrived: false,
                IdmAdvertisingResultParsed: true,
                IpcOnAdvertisingResultWrapped: true,
                ServerProcCallbackAvailable: true,
                Status: 0,
                ServiceId: "advertised-service-id"));
        var failureStatus = MiPlayIdmStartAdvertisingIdentity.EvaluateAdvertisingResultCallback(
            new MiPlayIdmAdvertisingResultCallbackPrerequisites(
                NativeCallbackArrived: true,
                IdmAdvertisingResultParsed: true,
                IpcOnAdvertisingResultWrapped: true,
                ServerProcCallbackAvailable: true,
                Status: -1,
                ServiceId: "advertised-service-id"));
        var missingListener = MiPlayIdmStartAdvertisingIdentity.EvaluateAdvertisingResultCallback(
            new MiPlayIdmAdvertisingResultCallbackPrerequisites(
                NativeCallbackArrived: true,
                IdmAdvertisingResultParsed: true,
                IpcOnAdvertisingResultWrapped: true,
                ServerProcCallbackAvailable: false,
                Status: 0,
                ServiceId: "advertised-service-id"));
        var missingServiceId = MiPlayIdmStartAdvertisingIdentity.EvaluateAdvertisingResultCallback(
            new MiPlayIdmAdvertisingResultCallbackPrerequisites(
                NativeCallbackArrived: true,
                IdmAdvertisingResultParsed: true,
                IpcOnAdvertisingResultWrapped: true,
                ServerProcCallbackAvailable: true,
                Status: 0,
                ServiceId: ""));

        Assert.False(missingCallback.CanProceed);
        Assert.False(failureStatus.CanProceed);
        Assert.False(missingListener.CanProceed);
        Assert.False(missingServiceId.CanProceed);
    }

    [Fact]
    public void NativeReturnedStringIsNotVerifiedRuntimeServiceIdUntilAdvertisingResultMatches()
    {
        Assert.False(MiPlayIdmStartAdvertisingIdentity.CanTreatReturnedStringAsVerifiedRuntimeServiceId(
            "native-returned-string",
            null));
        Assert.False(MiPlayIdmStartAdvertisingIdentity.CanTreatReturnedStringAsVerifiedRuntimeServiceId(
            "native-returned-string",
            "different-advertising-result-service-id"));
        Assert.True(MiPlayIdmStartAdvertisingIdentity.CanTreatReturnedStringAsVerifiedRuntimeServiceId(
            "runtime-service-id",
            "runtime-service-id"));
    }

    [Fact]
    public void VerifiedRuntimeServiceIdCannotBeDerivedFromDiscoveryOrCloudCtrl()
    {
        Assert.True(MiPlayIdmServiceType.TryParse(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var serviceType));
        Assert.NotNull(serviceType);

        var created = MiPlayIdmCloudCtrlServiceConfigs.TryCreateServiceConfig(
            new MiPlayIdmCloudCtrlAppIntentRow(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                ServiceType: MiPlayIdmServiceTypes.MiPlayAudioUrn,
                IntentAction: null,
                IntentExtra: null),
            out var cloudCtrlConfig);
        Assert.True(created);
        Assert.NotNull(cloudCtrlConfig);

        Assert.False(MiPlayIdmStartAdvertisingIdentity.CanDeriveVerifiedRuntimeServiceIdFromDiscoveryOrCloudCtrl(
            MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            serviceType,
            cloudCtrlConfig));
    }
}
