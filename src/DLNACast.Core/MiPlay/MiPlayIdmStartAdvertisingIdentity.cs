namespace DLNACast.Core.MiPlay;

public sealed record MiPlayIdmStartAdvertisingV2Prerequisites(
    string? ClientId,
    string? IdmServiceId,
    bool ServiceProtoSerialized,
    bool AppParamSerialized,
    bool PrivateDataSerialized,
    string? NativeReturnedString);

public sealed record MiPlayIdmAdvertisingResultCallbackPrerequisites(
    bool NativeCallbackArrived,
    bool IdmAdvertisingResultParsed,
    bool IpcOnAdvertisingResultWrapped,
    bool ServerProcCallbackAvailable,
    int Status,
    string? ServiceId);

/// <summary>
/// Offline model for the IDM startAdvertising identity boundary observed in
/// Mi Connect Service 5.1.251.10. It deliberately keeps the synchronous native
/// return string separate from the asynchronous IDMAdvertisingResult.serviceId
/// callback until both are observed and correlated.
/// </summary>
public static class MiPlayIdmStartAdvertisingIdentity
{
    public const long NativeStartAdvertisingJniAddress = 0x3C118;
    public const long NativeStartAdvertisingWorkerAddress = 0x92650;
    public const long NativeStartAdvertisingPostResultHelperAddress = 0x43BD8;

    public const long NativeStartAdvertisingSymbolStringOffset = 0x1A1FDF;
    public const long NativeServiceIdIsNullStringOffset = 0x1A2014;
    public const long NativeStartAdvertisingLogStringOffset = 0x1A7CBB;
    public const long NativeIdmAdvertisingResultServiceIdStringOffset = 0x1AC74F;
    public const long NativeHandleStartAdvertisingStringOffset = 0x1B7A32;
    public const long NativeHandleStartAdvertisingUniqueServiceIdFailureStringOffset = 0x1B7C5B;
    public const long NativeHandleStartAdvertisingUniqueServiceIdSuccessStringOffset = 0x1B7CAF;

    public const int IpcOnAdvertisingResultIdmAdvertisingResultFieldNumber = 1;

    public static MiPlayIdmStateDecision EvaluateV2StartAdvertisingReturn(
        MiPlayIdmStartAdvertisingV2Prerequisites prerequisites)
    {
        if (string.IsNullOrWhiteSpace(prerequisites.ClientId))
        {
            return new MiPlayIdmStateDecision(false, "The IDM V2 clientId is missing.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.IdmServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The IDMService.serviceId seed is missing.");
        }

        if (!prerequisites.ServiceProtoSerialized)
        {
            return new MiPlayIdmStateDecision(false, "The IDMService proto byte array was not provided to nativeStartAdvertising.");
        }

        if (!prerequisites.AppParamSerialized)
        {
            return new MiPlayIdmStateDecision(false, "The AppParam proto byte array was not provided to nativeStartAdvertising.");
        }

        if (!prerequisites.PrivateDataSerialized)
        {
            return new MiPlayIdmStateDecision(false, "The privateData byte array was not provided to nativeStartAdvertising.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.NativeReturnedString))
        {
            return new MiPlayIdmStateDecision(false, "nativeStartAdvertising did not return a non-empty Java string.");
        }

        return new MiPlayIdmStateDecision(true, "IDM V2 startAdvertising returned a non-empty native string, but it is not yet a verified runtime serviceId.");
    }

    public static MiPlayIdmStateDecision EvaluateAdvertisingResultCallback(
        MiPlayIdmAdvertisingResultCallbackPrerequisites prerequisites)
    {
        if (!prerequisites.NativeCallbackArrived)
        {
            return new MiPlayIdmStateDecision(false, "The native onAdvertisingResult callback has not arrived.");
        }

        if (!prerequisites.IdmAdvertisingResultParsed)
        {
            return new MiPlayIdmStateDecision(false, "The callback payload did not parse as IDMAdvertisingResult.");
        }

        if (!prerequisites.IpcOnAdvertisingResultWrapped)
        {
            return new MiPlayIdmStateDecision(false, "The parsed IDMAdvertisingResult was not wrapped into IPCParam.OnAdvertisingResult.");
        }

        if (!prerequisites.ServerProcCallbackAvailable)
        {
            return new MiPlayIdmStateDecision(false, "The IIDMServiceProcCallback listener is unavailable.");
        }

        if (prerequisites.Status != 0)
        {
            return new MiPlayIdmStateDecision(false, "The advertising result status is not success.");
        }

        if (string.IsNullOrWhiteSpace(prerequisites.ServiceId))
        {
            return new MiPlayIdmStateDecision(false, "The advertising result serviceId is missing.");
        }

        return new MiPlayIdmStateDecision(true, "The asynchronous advertising result accepted a serviceId through the registered listener.");
    }

    public static bool CanTreatReturnedStringAsVerifiedRuntimeServiceId(
        string? nativeReturnedString,
        string? advertisingResultServiceId) =>
        !string.IsNullOrWhiteSpace(nativeReturnedString) &&
        !string.IsNullOrWhiteSpace(advertisingResultServiceId) &&
        string.Equals(nativeReturnedString, advertisingResultServiceId, StringComparison.Ordinal);

    public static bool CanDeriveVerifiedRuntimeServiceIdFromDiscoveryOrCloudCtrl(
        int applicationId,
        MiPlayIdmServiceType serviceType,
        MiPlayIdmCloudCtrlServiceConfig serviceConfig) =>
        applicationId == MiPlayMdnsCapabilities.MiPlayAudioApplicationId &&
        serviceType.ServiceName == MiPlayIdmServiceTypes.MiPlayAudioServiceName &&
        MiPlayIdmCloudCtrlServiceConfigs.ContainsOnlyCloudCtrlMappingFields(serviceConfig) &&
        false;
}
