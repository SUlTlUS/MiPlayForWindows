using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLx06IdmRuntimeBridgeBoundaryTests
{
    [Fact]
    public void ConstantsCaptureIdmRuntimeStringOffsets()
    {
        Assert.Equal("1.88.51", MiPlayLx06IdmRuntimeBridgeBoundary.FirmwareVersion);
        Assert.Equal("usr/bin/idmruntime", MiPlayLx06IdmRuntimeBridgeBoundary.IdmRuntimeBinary);
        Assert.Equal("etc/init.d/idmruntime", MiPlayLx06IdmRuntimeBridgeBoundary.IdmRuntimeInitScript);
        Assert.Equal("usr/bin/mpas", MiPlayLx06IdmRuntimeBridgeBoundary.MpasBinary);

        Assert.Equal(0x233485, MiPlayLx06IdmRuntimeBridgeBoundary.AddAppClientLogOffset);
        Assert.Equal(0x2334AD, MiPlayLx06IdmRuntimeBridgeBoundary.AddAppServerLogOffset);
        Assert.Equal(0x1FEDA3, MiPlayLx06IdmRuntimeBridgeBoundary.ConnectServiceEnterLogOffset);
        Assert.Equal(0x214805, MiPlayLx06IdmRuntimeBridgeBoundary.PairEndpointReadAppAuthLogOffset);
        Assert.Equal(0x2345AA, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthAttributeLogOffset);
        Assert.Equal(0x234727, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationLogOffset);
        Assert.Equal(0x234864, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationWithLevelLogOffset);
        Assert.Equal(0x235875, MiPlayLx06IdmRuntimeBridgeBoundary.MiConnectMdnsServiceStringOffset);
        Assert.Equal(0x20BBA0, MiPlayLx06IdmRuntimeBridgeBoundary.AppsDataTxtKeyStringOffset);
        Assert.Equal(0x236529, MiPlayLx06IdmRuntimeBridgeBoundary.ServiceNamePlusServiceTypeLogOffset);
        Assert.Equal(0x2366D9, MiPlayLx06IdmRuntimeBridgeBoundary.RegisterOneServiceSuccessLogOffset);
        Assert.Equal(0x2354A5, MiPlayLx06IdmRuntimeBridgeBoundary.UpdateAdvertisingLogOffset);
        Assert.Equal(0x218507, MiPlayLx06IdmRuntimeBridgeBoundary.SecureModeStringClusterOffset);
        Assert.Equal(0x205C0A, MiPlayLx06IdmRuntimeBridgeBoundary.MiPlayAudioServiceTypeUrnOffset);
        Assert.Equal(0x1FA9A5, MiPlayLx06IdmRuntimeBridgeBoundary.FirstAscii8899DigitTableHitOffset);

        Assert.Equal(0x10A35C, MiPlayLx06IdmRuntimeBridgeBoundary.PairEndpointReadAppAuthLogLoadAddress);
        Assert.Equal(0x10A364, MiPlayLx06IdmRuntimeBridgeBoundary.PairEndpointReadAppAuthLogResolveAddress);
        Assert.Equal(0x10A370, MiPlayLx06IdmRuntimeBridgeBoundary.PairEndpointReadAppAuthSyslogCallAddress);
        Assert.Equal(0x19D64C, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthAttributeLogLoadAddress);
        Assert.Equal(0x19D654, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthAttributeLogResolveAddress);
        Assert.Equal(0x19D660, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthAttributeSyslogCallAddress);
        Assert.Equal(0x19D664, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthModeCompareAddress);
        Assert.Equal(0x19D6B4, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthMode2CallbackCallAddress);
        Assert.Equal(0x19D6D4, MiPlayLx06IdmRuntimeBridgeBoundary.HandleAppAuthMode1CallbackCallAddress);
        Assert.Equal(13, MiPlayLx06IdmRuntimeBridgeBoundary.AppAuthEventIdImmediate);
        Assert.Equal(0x1999BC, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationWithLevelLogLoadAddress);
        Assert.Equal(0x1999CC, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationWithLevelLogResolveAddress);
        Assert.Equal(0x1999F4, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationAppAttrLogLoadAddress);
        Assert.Equal(0x1999FC, MiPlayLx06IdmRuntimeBridgeBoundary.SetAttributeNotificationAppAttrLogResolveAddress);
        Assert.Equal(0x1A50BC, MiPlayLx06IdmRuntimeBridgeBoundary.MiConnectMdnsServiceFirstLogLoadAddress);
        Assert.Equal(0x1A50C4, MiPlayLx06IdmRuntimeBridgeBoundary.MiConnectMdnsServiceFirstLogResolveAddress);
        Assert.Equal(0x1A547C, MiPlayLx06IdmRuntimeBridgeBoundary.MiConnectMdnsServiceSecondLogLoadAddress);
        Assert.Equal(0x1A5484, MiPlayLx06IdmRuntimeBridgeBoundary.MiConnectMdnsServiceSecondLogResolveAddress);
        Assert.Equal(0xD6F70, MiPlayLx06IdmRuntimeBridgeBoundary.AppsDataTxtKeyFirstLogLoadAddress);
        Assert.Equal(0xD6F78, MiPlayLx06IdmRuntimeBridgeBoundary.AppsDataTxtKeyFirstLogResolveAddress);
        Assert.Equal(0x1AA664, MiPlayLx06IdmRuntimeBridgeBoundary.AppsDataTxtKeySecondLogLoadAddress);
        Assert.Equal(0x1AA670, MiPlayLx06IdmRuntimeBridgeBoundary.AppsDataTxtKeySecondLogResolveAddress);
        Assert.Equal(0x1A9418, MiPlayLx06IdmRuntimeBridgeBoundary.ServiceNamePlusServiceTypeLogLoadAddress);
        Assert.Equal(0x1A9420, MiPlayLx06IdmRuntimeBridgeBoundary.ServiceNamePlusServiceTypeLogResolveAddress);
        Assert.Equal(0x1A9434, MiPlayLx06IdmRuntimeBridgeBoundary.ServiceNamePlusServiceTypeSyslogCallAddress);
        Assert.Equal(0x19748C, MiPlayLx06IdmRuntimeBridgeBoundary.AddAppServerLogLoadAddress);
        Assert.Equal(0x197494, MiPlayLx06IdmRuntimeBridgeBoundary.AddAppServerLogResolveAddress);
        Assert.Equal(0x1974A8, MiPlayLx06IdmRuntimeBridgeBoundary.AddAppServerSyslogCallAddress);
        Assert.Equal(0x3E76C, MiPlayLx06IdmRuntimeBridgeBoundary.SecureModeStringClusterLoadAddress);
        Assert.Equal(0x3E77C, MiPlayLx06IdmRuntimeBridgeBoundary.SecureModeStringClusterResolveAddress);
        Assert.Equal(0x3E780, MiPlayLx06IdmRuntimeBridgeBoundary.SecureModeStringTableInitAddress);
    }

    [Fact]
    public void IdmRuntimeIdentityAndAuthLayerIsAdjacentButNotA8899Bridge()
    {
        var snapshot = MiPlayLx06IdmRuntimeBridgeBoundary.CreateCurrentSnapshot();

        Assert.True(snapshot.IdmRuntimeInitScriptStartsSeparateProcess);
        Assert.True(snapshot.MpasLinksLibIdmSdk);
        Assert.True(snapshot.MiConnectMdnsServiceObserved);
        Assert.True(snapshot.AppsDataAdvertisementBuilderObserved);
        Assert.True(snapshot.ServiceNameAndServiceTypeAdvertisementObserved);
        Assert.True(snapshot.MiPlayAudioServiceTypeUrnObserved);
        Assert.True(snapshot.AppClientServerRegistryObserved);
        Assert.True(snapshot.AppAuthAttributeHandshakeObserved);
        Assert.True(snapshot.AttributeNotificationRegistrationObserved);
        Assert.True(snapshot.SecureTransportModesObserved);
        Assert.True(snapshot.AppAuthXrefsResolveToSyslogAndCallbacks);
        Assert.True(snapshot.AttributeNotificationXrefsResolveToHandlerLogging);
        Assert.True(snapshot.MiConnectAndAppsDataXrefsStayInAdvertisingBuilders);
        Assert.True(snapshot.Legacy8899BridgeStringsAbsent);
        Assert.True(snapshot.SafetyCommandStringsAbsent);
        Assert.True(snapshot.Ascii8899HitsClassifiedAsDigitTable);
        Assert.False(snapshot.ExplicitBridgeToMpas8899ServerAppObserved);
        Assert.False(snapshot.ModernSafetyOpcodeOwnerObserved);

        var identity = MiPlayLx06IdmRuntimeBridgeBoundary.EvaluateIdmRuntimeIdentityAndAuthLayer(snapshot);

        Assert.True(identity.CanProceed);
        Assert.Contains("_mi-connect._udp.", identity.Reason, StringComparison.Ordinal);
        Assert.Contains("appsData", identity.Reason, StringComparison.Ordinal);
        Assert.Contains("APP_ATTRIBUTE_ID_APP_AUTH", identity.Reason, StringComparison.Ordinal);
        Assert.Contains("miplay-audio", identity.Reason, StringComparison.Ordinal);
        Assert.Contains("recovered xrefs", identity.Reason, StringComparison.Ordinal);
        Assert.Contains("does not by itself prove", identity.Reason, StringComparison.Ordinal);

        var bridge = MiPlayLx06IdmRuntimeBridgeBoundary.EvaluateMpas8899BridgeFromIdmRuntime(snapshot);

        Assert.False(bridge.CanProceed);
        Assert.Contains("APP_AUTH attribute callbacks", bridge.Reason, StringComparison.Ordinal);
        Assert.Contains("digit-table", bridge.Reason, StringComparison.Ordinal);
        Assert.Contains("cannot justify another 8899 business-frame probe", bridge.Reason, StringComparison.Ordinal);
        Assert.Contains("0x1400..0x1403", MiPlayLx06IdmRuntimeBridgeBoundary.RemainingMissingBridge, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaimedBridgeStillRequiresModernSafetyOwner()
    {
        var bridgeOnly = MiPlayLx06IdmRuntimeBridgeBoundary.CreateCurrentSnapshot() with
        {
            ExplicitBridgeToMpas8899ServerAppObserved = true,
            ModernSafetyOpcodeOwnerObserved = false,
        };

        var decision = MiPlayLx06IdmRuntimeBridgeBoundary.EvaluateMpas8899BridgeFromIdmRuntime(bridgeOnly);

        Assert.False(decision.CanProceed);
        Assert.Contains("modern 0x1400..0x1403 owner", decision.Reason, StringComparison.Ordinal);

        var complete = MiPlayLx06IdmRuntimeBridgeBoundary.EvaluateMpas8899BridgeFromIdmRuntime(
            bridgeOnly with { ModernSafetyOpcodeOwnerObserved = true });

        Assert.True(complete.CanProceed);
        Assert.Contains("new one-frame read-only live validation", complete.Reason, StringComparison.Ordinal);
    }
}
