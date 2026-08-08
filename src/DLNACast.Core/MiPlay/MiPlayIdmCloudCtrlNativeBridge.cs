namespace DLNACast.Core.MiPlay;

public sealed record MiPlayIdmCloudCtrlNativeUpdatePrerequisites(
    bool IdmNativeInitialized,
    bool SerializedServiceConfigsProvided,
    bool ServiceConfigsParsed);

public sealed record MiPlayIdmCloudCtrlNativeGetPrerequisites(
    bool JavaCallbackAvailable,
    bool SerializedServiceConfigsReturned,
    bool ServiceConfigsParsed);

/// <summary>
/// Offline model for the native CloudCtrl bridge observed in
/// libidmservicemgr.so. It models only byte-array/protobuf boundaries and does
/// not imply package identity, Continuity ServiceName, listener, or runtime
/// serviceId availability.
/// </summary>
public static class MiPlayIdmCloudCtrlNativeBridge
{
    public const long NativeUpdateCloudCtrlServiceConfigsJniAddress = 0x403B8;
    public const long NativeGetCloudCtrlServiceConfigsHelperAddress = 0x40728;

    public const int CloudCtrlServiceConfigsRepeatedServiceConfigFieldNumber = 1;

    public const long NativeUpdateCloudCtrlServiceConfigsStringOffset = 0x1A1F86;
    public const long NativeGetCloudCtrlServiceConfigsStringOffset = 0x1A1FDF;
    public const long NativeCloudCtrlUpdateLogStringOffset = 0x1AEFB7;
    public const long NativeServiceTypeIdLogStringOffset = 0x1B42CB;
    public const long NativeServiceTypeLogStringOffset = 0x1B42DB;

    public static MiPlayIdmStateDecision EvaluateNativeUpdate(
        MiPlayIdmCloudCtrlNativeUpdatePrerequisites prerequisites)
    {
        if (!prerequisites.IdmNativeInitialized)
        {
            return new MiPlayIdmStateDecision(false, "IDMNative is not initialized for CloudCtrl update.");
        }

        if (!prerequisites.SerializedServiceConfigsProvided)
        {
            return new MiPlayIdmStateDecision(false, "The serialized CloudCtrl ServiceConfigs byte array is missing.");
        }

        if (!prerequisites.ServiceConfigsParsed)
        {
            return new MiPlayIdmStateDecision(false, "The native CloudCtrl ServiceConfigs parser did not accept the byte array.");
        }

        return new MiPlayIdmStateDecision(true, "The native CloudCtrl update boundary accepted serialized ServiceConfigs.");
    }

    public static MiPlayIdmStateDecision EvaluateNativeGet(
        MiPlayIdmCloudCtrlNativeGetPrerequisites prerequisites)
    {
        if (!prerequisites.JavaCallbackAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The Java getCloudCtrlServiceConfigs callback is unavailable.");
        }

        if (!prerequisites.SerializedServiceConfigsReturned)
        {
            return new MiPlayIdmStateDecision(false, "The Java callback did not return serialized ServiceConfigs.");
        }

        if (!prerequisites.ServiceConfigsParsed)
        {
            return new MiPlayIdmStateDecision(false, "The native CloudCtrl get path did not parse ServiceConfigs.");
        }

        return new MiPlayIdmStateDecision(true, "The native CloudCtrl get boundary returned parsed ServiceConfigs.");
    }

    public static bool NativeBridgeCanProvideRuntimeServiceId(
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        MiPlayIdmCloudCtrlServiceConfigs.ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;
}
