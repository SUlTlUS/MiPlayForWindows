using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayIdmCloudCtrlNativeBridgeTests
{
    [Fact]
    public void NativeCloudCtrlAddressesAndStringOffsetsMatchElfEvidence()
    {
        Assert.Equal(0x403B8, MiPlayIdmCloudCtrlNativeBridge.NativeUpdateCloudCtrlServiceConfigsJniAddress);
        Assert.Equal(0x40728, MiPlayIdmCloudCtrlNativeBridge.NativeGetCloudCtrlServiceConfigsHelperAddress);
        Assert.Equal(0x1A1F86, MiPlayIdmCloudCtrlNativeBridge.NativeUpdateCloudCtrlServiceConfigsStringOffset);
        Assert.Equal(0x1A1FDF, MiPlayIdmCloudCtrlNativeBridge.NativeGetCloudCtrlServiceConfigsStringOffset);
        Assert.Equal(0x1AEFB7, MiPlayIdmCloudCtrlNativeBridge.NativeCloudCtrlUpdateLogStringOffset);
        Assert.Equal(0x1B42CB, MiPlayIdmCloudCtrlNativeBridge.NativeServiceTypeIdLogStringOffset);
        Assert.Equal(0x1B42DB, MiPlayIdmCloudCtrlNativeBridge.NativeServiceTypeLogStringOffset);
    }

    [Fact]
    public void ServiceConfigsRepeatedFieldNumberMatchesGeneratedProtoEvidence()
    {
        Assert.Equal(1, MiPlayIdmCloudCtrlNativeBridge.CloudCtrlServiceConfigsRepeatedServiceConfigFieldNumber);
    }

    [Fact]
    public void NativeUpdateRequiresInitializedIdmNativeSerializedBytesAndParseSuccess()
    {
        var accepted = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeUpdate(
            new MiPlayIdmCloudCtrlNativeUpdatePrerequisites(
                IdmNativeInitialized: true,
                SerializedServiceConfigsProvided: true,
                ServiceConfigsParsed: true));

        Assert.True(accepted.CanProceed);

        var notInitialized = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeUpdate(
            new MiPlayIdmCloudCtrlNativeUpdatePrerequisites(
                IdmNativeInitialized: false,
                SerializedServiceConfigsProvided: true,
                ServiceConfigsParsed: true));
        var missingBytes = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeUpdate(
            new MiPlayIdmCloudCtrlNativeUpdatePrerequisites(
                IdmNativeInitialized: true,
                SerializedServiceConfigsProvided: false,
                ServiceConfigsParsed: true));
        var parseFailed = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeUpdate(
            new MiPlayIdmCloudCtrlNativeUpdatePrerequisites(
                IdmNativeInitialized: true,
                SerializedServiceConfigsProvided: true,
                ServiceConfigsParsed: false));

        Assert.False(notInitialized.CanProceed);
        Assert.False(missingBytes.CanProceed);
        Assert.False(parseFailed.CanProceed);
    }

    [Fact]
    public void NativeGetRequiresJavaCallbackSerializedBytesAndParseSuccess()
    {
        var accepted = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeGet(
            new MiPlayIdmCloudCtrlNativeGetPrerequisites(
                JavaCallbackAvailable: true,
                SerializedServiceConfigsReturned: true,
                ServiceConfigsParsed: true));

        Assert.True(accepted.CanProceed);

        var missingCallback = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeGet(
            new MiPlayIdmCloudCtrlNativeGetPrerequisites(
                JavaCallbackAvailable: false,
                SerializedServiceConfigsReturned: true,
                ServiceConfigsParsed: true));
        var missingBytes = MiPlayIdmCloudCtrlNativeBridge.EvaluateNativeGet(
            new MiPlayIdmCloudCtrlNativeGetPrerequisites(
                JavaCallbackAvailable: true,
                SerializedServiceConfigsReturned: false,
                ServiceConfigsParsed: true));

        Assert.False(missingCallback.CanProceed);
        Assert.False(missingBytes.CanProceed);
    }

    [Fact]
    public void NativeCloudCtrlBridgeStillDoesNotProvideRuntimeServiceId()
    {
        var created = MiPlayIdmCloudCtrlServiceConfigs.TryCreateServiceConfig(
            new MiPlayIdmCloudCtrlAppIntentRow(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                ServiceType: MiPlayIdmServiceTypes.MiPlayAudioUrn,
                IntentAction: null,
                IntentExtra: null),
            out var serviceConfig);

        Assert.True(created);
        Assert.NotNull(serviceConfig);
        Assert.False(MiPlayIdmCloudCtrlNativeBridge.NativeBridgeCanProvideRuntimeServiceId(serviceConfig));
    }
}
