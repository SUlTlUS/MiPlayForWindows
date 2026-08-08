using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayContinuityChannelListenerStateTests
{
    [Fact]
    public void NativeChannelListenerAddressesAndStringsMatchStaticEvidence()
    {
        Assert.Equal(0x89D5B8, MiPlayContinuityChannelListenerState.NativeRegisterChannelListenerJniAddress);
        Assert.Equal(0x89D90C, MiPlayContinuityChannelListenerState.NativeUnregisterChannelListenerJniAddress);

        Assert.Equal(0xFFB16, MiPlayContinuityChannelListenerState.NativeRegisterChannelListenerApiStringOffset);
        Assert.Equal(0xFFB9D, MiPlayContinuityChannelListenerState.NativeUnregisterChannelListenerApiStringOffset);
        Assert.Equal(0x14CEBB, MiPlayContinuityChannelListenerState.NativeJniChannelListenerOnChannelCreatedStringOffset);
        Assert.Equal(0x14D5AA, MiPlayContinuityChannelListenerState.NativeRegisterChannelListenerSymbolStringOffset);
        Assert.Equal(0x14D60F, MiPlayContinuityChannelListenerState.NativeJniServerChannelOptionsJavaToNativeStringOffset);
        Assert.Equal(0x14D6CE, MiPlayContinuityChannelListenerState.NativeAddServerChannelListenerStringOffset);
        Assert.Equal(0x14D71F, MiPlayContinuityChannelListenerState.NativeRevertServerChannelListenerStringOffset);
        Assert.Equal(0x14D771, MiPlayContinuityChannelListenerState.NativeDeleteStoreServerChannelListenerStringOffset);
        Assert.Equal(0x14D7C8, MiPlayContinuityChannelListenerState.NativeUnregisterChannelListenerSymbolStringOffset);
        Assert.Equal(0x4D972F, MiPlayContinuityChannelListenerState.NativeChannelHandlerSetTransKeyStringOffset);
    }

    [Fact]
    public void ListenerTransactionsAndParcelableVersionsMatchJadxEvidence()
    {
        Assert.Equal(8, MiPlayContinuityChannelListenerState.IChannelInnerListenerGetFeaturesTransaction);
        Assert.Equal(2, MiPlayContinuityChannelListenerState.IChannelInnerListenerOnChannelCreatedTransaction);
        Assert.Equal(13, MiPlayContinuityChannelListenerState.IChannelInnerListenerOnChannelCreatedV2Transaction);
        Assert.Equal("channel.SDK_SUPPORT_USER_SECURITY_KEY", MiPlayContinuityChannelListenerState.UserSecurityKeyFeature);
        Assert.Equal(0, MiPlayContinuityChannelListenerState.ServerChannelOptionsV2LocalVersion);
        Assert.Equal(2, MiPlayContinuityChannelListenerState.ChannelInfoV2LocalVersion);
        Assert.Equal(32, MiPlayContinuityChannelListenerState.ChannelInfoTransKeyLength);
    }

    [Fact]
    public void RegisterChannelListenerRequiresServiceNameOptionsListenerPermissionAppInfoDeathLinkMapAndNativeSuccess()
    {
        var serviceName = new MiPlayContinuityServiceName(
            "com.xiaomi.mi_connect_service",
            "miplay-audio");
        var accepted = MiPlayContinuityChannelListenerState.EvaluateRegisterChannelListener(
            new MiPlayContinuityRegisterChannelListenerPrerequisites(
                ServiceName: serviceName,
                ServerChannelOptionsProvided: true,
                InnerListenerProvided: true,
                InternalBindPermissionGranted: true,
                AppInfoGeneratedFromBinderCaller: true,
                ListenerDeathLinked: true,
                ServiceListenerWeakReferenceInserted: true,
                NativeRegisterReturnedSuccess: true));

        Assert.True(accepted.CanProceed);

        var missingServiceName = MiPlayContinuityChannelListenerState.EvaluateRegisterChannelListener(
            new MiPlayContinuityRegisterChannelListenerPrerequisites(
                ServiceName: null,
                ServerChannelOptionsProvided: true,
                InnerListenerProvided: true,
                InternalBindPermissionGranted: true,
                AppInfoGeneratedFromBinderCaller: true,
                ListenerDeathLinked: true,
                ServiceListenerWeakReferenceInserted: true,
                NativeRegisterReturnedSuccess: true));
        var missingPermission = MiPlayContinuityChannelListenerState.EvaluateRegisterChannelListener(
            new MiPlayContinuityRegisterChannelListenerPrerequisites(
                ServiceName: serviceName,
                ServerChannelOptionsProvided: true,
                InnerListenerProvided: true,
                InternalBindPermissionGranted: false,
                AppInfoGeneratedFromBinderCaller: true,
                ListenerDeathLinked: true,
                ServiceListenerWeakReferenceInserted: true,
                NativeRegisterReturnedSuccess: true));
        var nativeFailure = MiPlayContinuityChannelListenerState.EvaluateRegisterChannelListener(
            new MiPlayContinuityRegisterChannelListenerPrerequisites(
                ServiceName: serviceName,
                ServerChannelOptionsProvided: true,
                InnerListenerProvided: true,
                InternalBindPermissionGranted: true,
                AppInfoGeneratedFromBinderCaller: true,
                ListenerDeathLinked: true,
                ServiceListenerWeakReferenceInserted: true,
                NativeRegisterReturnedSuccess: false));

        Assert.False(missingServiceName.CanProceed);
        Assert.False(missingPermission.CanProceed);
        Assert.False(nativeFailure.CanProceed);
        Assert.Contains("reverted", nativeFailure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelCreatedV2RequiresRegisteredListenerMatchingServiceChannelIdDeviceMediumTransKeyAndWipe()
    {
        var serviceName = new MiPlayContinuityServiceName(
            "com.xiaomi.mi_connect_service",
            "miplay-audio");
        var accepted = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: true,
                RegisteredServiceName: serviceName,
                CallbackServiceName: serviceName,
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: true,
                ChannelInfoV2Provided: true,
                TransKeyProvidedBeforeCallback: true,
                TransKeyWipedAfterCallback: true));

        Assert.True(accepted.CanProceed);
        Assert.Contains(nameof(MiPlayContinuityChannelCreatedCallbackShape.ChannelInfoV2), accepted.Reason, StringComparison.Ordinal);

        var missingCallback = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: false,
                RegisteredServiceName: serviceName,
                CallbackServiceName: serviceName,
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: true,
                ChannelInfoV2Provided: true,
                TransKeyProvidedBeforeCallback: true,
                TransKeyWipedAfterCallback: true));
        var mismatchedService = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: true,
                RegisteredServiceName: serviceName,
                CallbackServiceName: new MiPlayContinuityServiceName("other.pkg", "miplay-audio"),
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: true,
                ChannelInfoV2Provided: true,
                TransKeyProvidedBeforeCallback: true,
                TransKeyWipedAfterCallback: true));
        var missingTransKey = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: true,
                RegisteredServiceName: serviceName,
                CallbackServiceName: serviceName,
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: true,
                ChannelInfoV2Provided: true,
                TransKeyProvidedBeforeCallback: false,
                TransKeyWipedAfterCallback: true));
        var notWiped = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: true,
                RegisteredServiceName: serviceName,
                CallbackServiceName: serviceName,
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: true,
                ChannelInfoV2Provided: true,
                TransKeyProvidedBeforeCallback: true,
                TransKeyWipedAfterCallback: false));

        Assert.False(missingCallback.CanProceed);
        Assert.False(mismatchedService.CanProceed);
        Assert.False(missingTransKey.CanProceed);
        Assert.False(notWiped.CanProceed);
    }

    [Fact]
    public void ChannelCreatedWithoutUserSecurityKeyFeatureUsesLegacyChannelInfoBoundary()
    {
        var serviceName = new MiPlayContinuityServiceName(null, "miplay-audio");
        var accepted = MiPlayContinuityChannelListenerState.EvaluateChannelCreatedCallback(
            new MiPlayContinuityChannelCreatedPrerequisites(
                RegisteredServerListenerPresent: true,
                NativeOnChannelCreatedArrived: true,
                RegisteredServiceName: serviceName,
                CallbackServiceName: serviceName,
                ChannelId: 7,
                DeviceId: "netbus-device-id",
                MediumType: MiPlayContinuityMediumType.WifiLan,
                SupportsUserSecurityKeyFeature: false,
                ChannelInfoV2Provided: false,
                TransKeyProvidedBeforeCallback: false,
                TransKeyWipedAfterCallback: true));

        Assert.True(accepted.CanProceed);
        Assert.Contains(nameof(MiPlayContinuityChannelCreatedCallbackShape.LegacyChannelInfo), accepted.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeRegisterSuccessAloneDoesNotImplyChannelCreatedOrLegacyOnSuccess()
    {
        Assert.False(MiPlayContinuityChannelListenerState.RegistrationSuccessImpliesChannelCreatedCallback(
            nativeRegisterReturnedSuccess: true,
            nativeOnChannelCreatedArrived: false));
        Assert.True(MiPlayContinuityChannelListenerState.RegistrationSuccessImpliesChannelCreatedCallback(
            nativeRegisterReturnedSuccess: true,
            nativeOnChannelCreatedArrived: true));

        Assert.False(MiPlayContinuityChannelListenerState.CanStandInForLegacyDealSafetyDoneOnSuccess(
            continuityChannelCreated: true,
            connectionMode: MiPlayPostAuthConnectionMode.LegacyTcp8899));
        Assert.False(MiPlayContinuityChannelListenerState.CanStandInForLegacyDealSafetyDoneOnSuccess(
            continuityChannelCreated: true,
            connectionMode: MiPlayPostAuthConnectionMode.LyraContinuityChannel));
    }
}
