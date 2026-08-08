using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayIdmRuntimeServiceIdMapTests
{
    [Fact]
    public void NativeServerMapStringOffsetsMatchElfEvidence()
    {
        Assert.Equal(0x1AEA8B, MiPlayIdmRuntimeServiceIdMap.NativeGetRealServiceIdFailedStringOffset);
        Assert.Equal(0x1AF071, MiPlayIdmRuntimeServiceIdMap.NativeGetUnitServiceIdListByServerIdStringOffset);
        Assert.Equal(0x1AF0D4, MiPlayIdmRuntimeServiceIdMap.NativeGetIdmServerProcByServiceIdStringOffset);
        Assert.Equal(0x1AF0F0, MiPlayIdmRuntimeServiceIdMap.NativeAddServerMapServiceFormatStringOffset);
        Assert.Equal(0x1AF12B, MiPlayIdmRuntimeServiceIdMap.NativeAddServerMapServiceNameStringOffset);
        Assert.Equal(0x1AF13E, MiPlayIdmRuntimeServiceIdMap.NativeUpdateServerMapServiceFormatStringOffset);
        Assert.Equal(0x1AF20E, MiPlayIdmRuntimeServiceIdMap.NativeGetRealServiceIdFormatStringOffset);
        Assert.Equal(0x1AF23B, MiPlayIdmRuntimeServiceIdMap.NativeGetServiceIdStringOffset);
        Assert.Equal(0x1AF248, MiPlayIdmRuntimeServiceIdMap.NativeGetServiceUuidByServiceIdFormatStringOffset);
        Assert.Equal(0x1AF270, MiPlayIdmRuntimeServiceIdMap.NativeGetServiceUuidByServiceIdNameStringOffset);
    }

    [Fact]
    public void IdmServiceAndAdvertisingResultFieldNumbersMatchGeneratedProtoEvidence()
    {
        Assert.Equal(1, MiPlayIdmRuntimeServiceIdMap.IdmServiceServiceIdFieldNumber);
        Assert.Equal(2, MiPlayIdmRuntimeServiceIdMap.IdmServiceTypeFieldNumber);
        Assert.Equal(3, MiPlayIdmRuntimeServiceIdMap.IdmServiceNameFieldNumber);
        Assert.Equal(4, MiPlayIdmRuntimeServiceIdMap.IdmServiceEndpointFieldNumber);
        Assert.Equal(5, MiPlayIdmRuntimeServiceIdMap.IdmServiceOriginalServiceIdFieldNumber);
        Assert.Equal(6, MiPlayIdmRuntimeServiceIdMap.IdmServiceSuperTypeFieldNumber);
        Assert.Equal(7, MiPlayIdmRuntimeServiceIdMap.IdmServiceAppDataFieldNumber);

        Assert.Equal(1, MiPlayIdmRuntimeServiceIdMap.IdmAdvertisingResultStatusFieldNumber);
        Assert.Equal(2, MiPlayIdmRuntimeServiceIdMap.IdmAdvertisingResultServiceIdFieldNumber);
    }

    [Fact]
    public void ServerMapInsertRequiresServerProcServerIdServiceUuidRuntimeServiceIdAndAdvertisingResult()
    {
        var accepted = MiPlayIdmRuntimeServiceIdMap.EvaluateServerMapInsert(
            new MiPlayIdmRuntimeServiceIdMapPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "uuid-from-idm-service",
                RuntimeServiceId: "runtime-service-id",
                ServerProcRegistered: true,
                AdvertisingResultAccepted: true));

        Assert.True(accepted.CanProceed);

        var missingServerProc = MiPlayIdmRuntimeServiceIdMap.EvaluateServerMapInsert(
            new MiPlayIdmRuntimeServiceIdMapPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "uuid-from-idm-service",
                RuntimeServiceId: "runtime-service-id",
                ServerProcRegistered: false,
                AdvertisingResultAccepted: true));
        var missingUuid = MiPlayIdmRuntimeServiceIdMap.EvaluateServerMapInsert(
            new MiPlayIdmRuntimeServiceIdMapPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "",
                RuntimeServiceId: "runtime-service-id",
                ServerProcRegistered: true,
                AdvertisingResultAccepted: true));
        var missingResult = MiPlayIdmRuntimeServiceIdMap.EvaluateServerMapInsert(
            new MiPlayIdmRuntimeServiceIdMapPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "uuid-from-idm-service",
                RuntimeServiceId: "runtime-service-id",
                ServerProcRegistered: true,
                AdvertisingResultAccepted: false));

        Assert.False(missingServerProc.CanProceed);
        Assert.False(missingUuid.CanProceed);
        Assert.False(missingResult.CanProceed);
    }

    [Fact]
    public void RealServiceIdLookupRequiresServerIdServiceUuidAndExistingMapPair()
    {
        var accepted = MiPlayIdmRuntimeServiceIdMap.EvaluateRealServiceIdLookup(
            new MiPlayIdmRuntimeServiceLookupPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "uuid-from-idm-service",
                ServerMapContainsPair: true));

        Assert.True(accepted.CanProceed);

        var missingServerId = MiPlayIdmRuntimeServiceIdMap.EvaluateRealServiceIdLookup(
            new MiPlayIdmRuntimeServiceLookupPrerequisites(
                ServerId: "",
                ServiceUuid: "uuid-from-idm-service",
                ServerMapContainsPair: true));
        var missingPair = MiPlayIdmRuntimeServiceIdMap.EvaluateRealServiceIdLookup(
            new MiPlayIdmRuntimeServiceLookupPrerequisites(
                ServerId: "R4PX0HQT",
                ServiceUuid: "uuid-from-idm-service",
                ServerMapContainsPair: false));

        Assert.False(missingServerId.CanProceed);
        Assert.False(missingPair.CanProceed);
    }

    [Fact]
    public void RuntimeServiceIdCannotBeDerivedFromCloudCtrlOrDiscoveryIdentity()
    {
        var created = MiPlayIdmCloudCtrlServiceConfigs.TryCreateServiceConfig(
            new MiPlayIdmCloudCtrlAppIntentRow(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                ServiceType: MiPlayIdmServiceTypes.MiPlayAudioUrn,
                IntentAction: null,
                IntentExtra: null),
            out var cloudCtrlConfig);
        Assert.True(created);
        Assert.NotNull(cloudCtrlConfig);
        Assert.False(MiPlayIdmRuntimeServiceIdMap.CanDeriveFromCloudCtrlConfig(cloudCtrlConfig));

        Assert.True(MiPlayIdmServiceType.TryParse(
            MiPlayIdmServiceTypes.MiPlayAudioUrn,
            out var serviceType));
        Assert.NotNull(serviceType);

        Assert.False(MiPlayIdmRuntimeServiceIdMap.CanDeriveFromDiscoveryIdentity(
            MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            serviceType));
    }
}
