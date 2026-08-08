namespace DLNACast.Core.MiPlay;

public enum MiPlayContinuityChannelCreatedCallbackShape
{
    LegacyChannelInfo = 1,
    ChannelInfoV2 = 2,
}

public sealed record MiPlayContinuityRegisterChannelListenerPrerequisites(
    MiPlayContinuityServiceName? ServiceName,
    bool ServerChannelOptionsProvided,
    bool InnerListenerProvided,
    bool InternalBindPermissionGranted,
    bool AppInfoGeneratedFromBinderCaller,
    bool ListenerDeathLinked,
    bool ServiceListenerWeakReferenceInserted,
    bool NativeRegisterReturnedSuccess);

public sealed record MiPlayContinuityChannelCreatedPrerequisites(
    bool RegisteredServerListenerPresent,
    bool NativeOnChannelCreatedArrived,
    MiPlayContinuityServiceName? RegisteredServiceName,
    MiPlayContinuityServiceName? CallbackServiceName,
    int ChannelId,
    string? DeviceId,
    MiPlayContinuityMediumType MediumType,
    bool SupportsUserSecurityKeyFeature,
    bool ChannelInfoV2Provided,
    bool TransKeyProvidedBeforeCallback,
    bool TransKeyWipedAfterCallback);

/// <summary>
/// Offline model for Continuity channel-listener registration and channel
/// creation callbacks in Mi Connect Service 5.1.251.10. This is a Binder/native
/// listener state machine and is not the legacy TCP 8899 SafetyAuth/onSuccess
/// callback.
/// </summary>
public static class MiPlayContinuityChannelListenerState
{
    public const long NativeRegisterChannelListenerJniAddress = 0x89D5B8;
    public const long NativeUnregisterChannelListenerJniAddress = 0x89D90C;

    public const long NativeRegisterChannelListenerApiStringOffset = 0xFFB16;
    public const long NativeUnregisterChannelListenerApiStringOffset = 0xFFB9D;
    public const long NativeJniChannelListenerOnChannelCreatedStringOffset = 0x14CEBB;
    public const long NativeRegisterChannelListenerSymbolStringOffset = 0x14D5AA;
    public const long NativeJniServerChannelOptionsJavaToNativeStringOffset = 0x14D60F;
    public const long NativeAddServerChannelListenerStringOffset = 0x14D6CE;
    public const long NativeRevertServerChannelListenerStringOffset = 0x14D71F;
    public const long NativeDeleteStoreServerChannelListenerStringOffset = 0x14D771;
    public const long NativeUnregisterChannelListenerSymbolStringOffset = 0x14D7C8;
    public const long NativeChannelHandlerSetTransKeyStringOffset = 0x4D972F;

    public const int IChannelInnerListenerGetFeaturesTransaction = 8;
    public const int IChannelInnerListenerOnChannelCreatedTransaction = 2;
    public const int IChannelInnerListenerOnChannelCreatedV2Transaction = 13;

    public const string UserSecurityKeyFeature = "channel.SDK_SUPPORT_USER_SECURITY_KEY";
    public const int ServerChannelOptionsV2LocalVersion = 0;
    public const int ChannelInfoV2LocalVersion = 2;
    public const int ChannelInfoTransKeyLength = 32;

    public static MiPlayIdmStateDecision EvaluateRegisterChannelListener(
        MiPlayContinuityRegisterChannelListenerPrerequisites prerequisites)
    {
        if (prerequisites.ServiceName is null)
        {
            return new MiPlayIdmStateDecision(false, "The Continuity ServiceName is missing.");
        }

        if (!prerequisites.ServerChannelOptionsProvided)
        {
            return new MiPlayIdmStateDecision(false, "ServerChannelOptionsV2 is missing.");
        }

        if (!prerequisites.InnerListenerProvided)
        {
            return new MiPlayIdmStateDecision(false, "IChannelInnerListener is missing.");
        }

        if (!prerequisites.InternalBindPermissionGranted)
        {
            return new MiPlayIdmStateDecision(false, "The internal Continuity bind permission check failed.");
        }

        if (!prerequisites.AppInfoGeneratedFromBinderCaller)
        {
            return new MiPlayIdmStateDecision(false, "PackageUtil did not generate AppInfo from the Binder caller.");
        }

        if (!prerequisites.ListenerDeathLinked)
        {
            return new MiPlayIdmStateDecision(false, "The listener death recipient was not linked.");
        }

        if (!prerequisites.ServiceListenerWeakReferenceInserted)
        {
            return new MiPlayIdmStateDecision(false, "The service listener weak reference was not inserted before native registration.");
        }

        if (!prerequisites.NativeRegisterReturnedSuccess)
        {
            return new MiPlayIdmStateDecision(false, "nativeRegisterChannelListener returned a non-success result; the listener map must be reverted.");
        }

        return new MiPlayIdmStateDecision(true, "Continuity channel listener registration reached the native success boundary.");
    }

    public static MiPlayIdmStateDecision EvaluateChannelCreatedCallback(
        MiPlayContinuityChannelCreatedPrerequisites prerequisites)
    {
        if (!prerequisites.RegisteredServerListenerPresent)
        {
            return new MiPlayIdmStateDecision(false, "No registered server listener is present for the service.");
        }

        if (!prerequisites.NativeOnChannelCreatedArrived)
        {
            return new MiPlayIdmStateDecision(false, "The native onChannelCreated callback has not arrived.");
        }

        if (prerequisites.RegisteredServiceName is null || prerequisites.CallbackServiceName is null)
        {
            return new MiPlayIdmStateDecision(false, "The registered or callback ServiceName is missing.");
        }

        if (!string.Equals(
            prerequisites.RegisteredServiceName.ToMergedString(),
            prerequisites.CallbackServiceName.ToMergedString(),
            StringComparison.Ordinal))
        {
            return new MiPlayIdmStateDecision(false, "The callback ServiceName does not match the registered service.");
        }

        if (prerequisites.ChannelId <= 0)
        {
            return new MiPlayIdmStateDecision(false, "The created channel id is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.DeviceId))
        {
            return new MiPlayIdmStateDecision(false, "The created channel DeviceId is missing.");
        }

        if (prerequisites.MediumType == MiPlayContinuityMediumType.None)
        {
            return new MiPlayIdmStateDecision(false, "The created channel medium type is missing.");
        }

        if (prerequisites.SupportsUserSecurityKeyFeature && !prerequisites.ChannelInfoV2Provided)
        {
            return new MiPlayIdmStateDecision(false, "The listener supports user security keys but no ChannelInfoV2 was delivered.");
        }

        if (prerequisites.SupportsUserSecurityKeyFeature && !prerequisites.TransKeyProvidedBeforeCallback)
        {
            return new MiPlayIdmStateDecision(false, "ChannelInfoV2 did not expose a transKey before the listener callback.");
        }

        if (!prerequisites.TransKeyWipedAfterCallback)
        {
            return new MiPlayIdmStateDecision(false, "ChannelInfoV2 transKey was not wiped after listener dispatch.");
        }

        var shape = prerequisites.SupportsUserSecurityKeyFeature
            ? MiPlayContinuityChannelCreatedCallbackShape.ChannelInfoV2
            : MiPlayContinuityChannelCreatedCallbackShape.LegacyChannelInfo;
        return new MiPlayIdmStateDecision(true, $"Continuity channel-created callback reached the {shape} listener boundary.");
    }

    public static bool RegistrationSuccessImpliesChannelCreatedCallback(
        bool nativeRegisterReturnedSuccess,
        bool nativeOnChannelCreatedArrived) =>
        nativeRegisterReturnedSuccess &&
        nativeOnChannelCreatedArrived;

    public static bool CanStandInForLegacyDealSafetyDoneOnSuccess(
        bool continuityChannelCreated,
        MiPlayPostAuthConnectionMode connectionMode) =>
        continuityChannelCreated &&
        connectionMode == MiPlayPostAuthConnectionMode.LegacyTcp8899 &&
        false;
}
