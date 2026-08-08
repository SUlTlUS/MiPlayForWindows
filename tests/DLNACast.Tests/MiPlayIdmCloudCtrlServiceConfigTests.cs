using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayIdmCloudCtrlServiceConfigTests
{
    [Fact]
    public void AppIntentRowBecomesCloudCtrlServiceConfigPairOnly()
    {
        var row = new MiPlayIdmCloudCtrlAppIntentRow(
            ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            ServiceType: MiPlayIdmServiceTypes.MiPlayAudioUrn,
            IntentAction: "com.xiaomi.intent.MIPLAY_AUDIO",
            IntentExtra: "package.service.extra");

        var created = MiPlayIdmCloudCtrlServiceConfigs.TryCreateServiceConfig(
            row,
            out var serviceConfig);

        Assert.True(created);
        Assert.NotNull(serviceConfig);
        Assert.Equal(5, serviceConfig.ServiceTypeId);
        Assert.Equal(MiPlayIdmServiceTypes.MiPlayAudioUrn, serviceConfig.ServiceType);
        Assert.True(MiPlayIdmCloudCtrlServiceConfigs.ContainsOnlyCloudCtrlMappingFields(serviceConfig));
        Assert.False(MiPlayIdmCloudCtrlServiceConfigs.CanProvideContinuityPackageIdentity(serviceConfig));
        Assert.False(MiPlayIdmCloudCtrlServiceConfigs.CanProvideContinuityServiceName(serviceConfig));
        Assert.False(MiPlayIdmCloudCtrlServiceConfigs.CanProvideRuntimeServiceId(serviceConfig));
    }

    [Fact]
    public void EmptyServiceTypeIsSkippedLikeConfigMgrAndConverterUtil()
    {
        var created = MiPlayIdmCloudCtrlServiceConfigs.TryCreateServiceConfig(
            new MiPlayIdmCloudCtrlAppIntentRow(
                ApplicationId: MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
                ServiceType: "",
                IntentAction: null,
                IntentExtra: null),
            out var serviceConfig);

        Assert.False(created);
        Assert.Null(serviceConfig);
    }

    [Fact]
    public void CloudCtrlFieldNumbersMatchGeneratedProtoEvidence()
    {
        Assert.Equal(1, MiPlayIdmCloudCtrlServiceConfigs.CloudCtrlServiceConfigServiceTypeIdFieldNumber);
        Assert.Equal(2, MiPlayIdmCloudCtrlServiceConfigs.CloudCtrlServiceConfigServiceTypeFieldNumber);
    }

    [Fact]
    public void RoomQueriesMapAppIntentServiceTypeAndApplicationIdOnly()
    {
        Assert.Equal("app_intent", MiPlayIdmCloudCtrlServiceConfigs.RoomAppIntentTableName);
        Assert.Equal("serviceType", MiPlayIdmCloudCtrlServiceConfigs.RoomAppIntentServiceTypeColumn);
        Assert.Equal("appid", MiPlayIdmCloudCtrlServiceConfigs.RoomAppIntentApplicationIdColumn);
        Assert.Equal(
            "SELECT serviceType,appid FROM app_intent",
            MiPlayIdmCloudCtrlServiceConfigs.GetServiceConfigListSql);
        Assert.Equal(
            "SELECT serviceType FROM app_intent WHERE appid = ?",
            MiPlayIdmCloudCtrlServiceConfigs.GetServiceTypeByAppIdSql);
    }

    [Fact]
    public void MiPlayAudioCloudCtrlConfigStillDoesNotDeriveNetBusServiceId()
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
        Assert.Equal(5, serviceConfig.ServiceTypeId);
        Assert.False(MiPlayIdmCloudCtrlServiceConfigs.CanProvideRuntimeServiceId(serviceConfig));
    }
}
