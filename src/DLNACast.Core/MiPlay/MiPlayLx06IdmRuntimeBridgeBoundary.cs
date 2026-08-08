namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLx06IdmRuntimeBridgeSnapshot(
    bool IdmRuntimeInitScriptStartsSeparateProcess,
    bool MpasLinksLibIdmSdk,
    bool MiConnectMdnsServiceObserved,
    bool AppsDataAdvertisementBuilderObserved,
    bool ServiceNameAndServiceTypeAdvertisementObserved,
    bool MiPlayAudioServiceTypeUrnObserved,
    bool AppClientServerRegistryObserved,
    bool AppAuthAttributeHandshakeObserved,
    bool AttributeNotificationRegistrationObserved,
    bool SecureTransportModesObserved,
    bool AppAuthXrefsResolveToSyslogAndCallbacks,
    bool AttributeNotificationXrefsResolveToHandlerLogging,
    bool MiConnectAndAppsDataXrefsStayInAdvertisingBuilders,
    bool Legacy8899BridgeStringsAbsent,
    bool SafetyCommandStringsAbsent,
    bool Ascii8899HitsClassifiedAsDigitTable,
    bool ExplicitBridgeToMpas8899ServerAppObserved,
    bool ModernSafetyOpcodeOwnerObserved);

/// <summary>
/// Offline-only boundary for the LX06 1.88.51 device-side IDM runtime strings.
/// It identifies the generic IDM advertisement/authentication layer adjacent
/// to mpas, while keeping that layer separate from the legacy TCP 8899 command
/// dispatcher until an explicit bridge is found.
/// </summary>
public static class MiPlayLx06IdmRuntimeBridgeBoundary
{
    public const string FirmwareVersion = "1.88.51";
    public const string IdmRuntimeBinary = "usr/bin/idmruntime";
    public const string IdmRuntimeInitScript = "etc/init.d/idmruntime";
    public const string MpasBinary = "usr/bin/mpas";

    public const int AddAppClientLogOffset = 0x233485;
    public const int AddAppServerLogOffset = 0x2334AD;
    public const int ConnectServiceEnterLogOffset = 0x1FEDA3;
    public const int PairEndpointReadAppAuthLogOffset = 0x214805;
    public const int HandleAppAuthAttributeLogOffset = 0x2345AA;
    public const int SetAttributeNotificationLogOffset = 0x234727;
    public const int SetAttributeNotificationWithLevelLogOffset = 0x234864;
    public const int MiConnectMdnsServiceStringOffset = 0x235875;
    public const int AppsDataTxtKeyStringOffset = 0x20BBA0;
    public const int ServiceNamePlusServiceTypeLogOffset = 0x236529;
    public const int RegisterOneServiceSuccessLogOffset = 0x2366D9;
    public const int UpdateAdvertisingLogOffset = 0x2354A5;
    public const int SecureModeStringClusterOffset = 0x218507;
    public const int MiPlayAudioServiceTypeUrnOffset = 0x205C0A;
    public const int FirstAscii8899DigitTableHitOffset = 0x1FA9A5;

    public const int PairEndpointReadAppAuthLogLoadAddress = 0x10A35C;
    public const int PairEndpointReadAppAuthLogResolveAddress = 0x10A364;
    public const int PairEndpointReadAppAuthSyslogCallAddress = 0x10A370;

    public const int HandleAppAuthAttributeLogLoadAddress = 0x19D64C;
    public const int HandleAppAuthAttributeLogResolveAddress = 0x19D654;
    public const int HandleAppAuthAttributeSyslogCallAddress = 0x19D660;
    public const int HandleAppAuthModeCompareAddress = 0x19D664;
    public const int HandleAppAuthMode2CallbackCallAddress = 0x19D6B4;
    public const int HandleAppAuthMode1CallbackCallAddress = 0x19D6D4;
    public const int AppAuthEventIdImmediate = 13;

    public const int SetAttributeNotificationWithLevelLogLoadAddress = 0x1999BC;
    public const int SetAttributeNotificationWithLevelLogResolveAddress = 0x1999CC;
    public const int SetAttributeNotificationAppAttrLogLoadAddress = 0x1999F4;
    public const int SetAttributeNotificationAppAttrLogResolveAddress = 0x1999FC;

    public const int MiConnectMdnsServiceFirstLogLoadAddress = 0x1A50BC;
    public const int MiConnectMdnsServiceFirstLogResolveAddress = 0x1A50C4;
    public const int MiConnectMdnsServiceSecondLogLoadAddress = 0x1A547C;
    public const int MiConnectMdnsServiceSecondLogResolveAddress = 0x1A5484;

    public const int AppsDataTxtKeyFirstLogLoadAddress = 0xD6F70;
    public const int AppsDataTxtKeyFirstLogResolveAddress = 0xD6F78;
    public const int AppsDataTxtKeySecondLogLoadAddress = 0x1AA664;
    public const int AppsDataTxtKeySecondLogResolveAddress = 0x1AA670;

    public const int ServiceNamePlusServiceTypeLogLoadAddress = 0x1A9418;
    public const int ServiceNamePlusServiceTypeLogResolveAddress = 0x1A9420;
    public const int ServiceNamePlusServiceTypeSyslogCallAddress = 0x1A9434;

    public const int AddAppServerLogLoadAddress = 0x19748C;
    public const int AddAppServerLogResolveAddress = 0x197494;
    public const int AddAppServerSyslogCallAddress = 0x1974A8;

    public const int SecureModeStringClusterLoadAddress = 0x3E76C;
    public const int SecureModeStringClusterResolveAddress = 0x3E77C;
    public const int SecureModeStringTableInitAddress = 0x3E780;

    public const string MiConnectMdnsServiceString = "_mi-connect._udp.";
    public const string AppsDataTxtKeyString = "appsData=";
    public const string ServiceNamePlusServiceTypeLog = "serviceName+serviceType:%s";
    public const string MiPlayAudioServiceTypeUrn = MiPlayIdmServiceTypes.MiPlayAudioUrn;
    public const string AppAuthAttributeName = "APP_ATTRIBUTE_ID_APP_AUTH";
    public const string SecureModeStringCluster = "MC_MI_SEC_COMMMC_MI_SEC_COMM_TRANSMC_MI_SEC_NONEMC_MI_SEC_TRANS";
    public const string Ascii8899DigitTableContext =
        "8495051525354555657585960616263646566676869707172737475767778798081828384858687888990919293949596979899";
    public const string RemainingMissingBridge =
        "an explicit idmruntime/libidmsdk/libiotdcm_miplay bridge that hands an authenticated IDM/AppAuth endpoint to mpas TCP 8899 ServerApp::doMpasCommand or owns 0x1400..0x1403";

    public static MiPlayLx06IdmRuntimeBridgeSnapshot CreateCurrentSnapshot() =>
        new(
            IdmRuntimeInitScriptStartsSeparateProcess: true,
            MpasLinksLibIdmSdk: true,
            MiConnectMdnsServiceObserved: true,
            AppsDataAdvertisementBuilderObserved: true,
            ServiceNameAndServiceTypeAdvertisementObserved: true,
            MiPlayAudioServiceTypeUrnObserved: true,
            AppClientServerRegistryObserved: true,
            AppAuthAttributeHandshakeObserved: true,
            AttributeNotificationRegistrationObserved: true,
            SecureTransportModesObserved: true,
            AppAuthXrefsResolveToSyslogAndCallbacks: true,
            AttributeNotificationXrefsResolveToHandlerLogging: true,
            MiConnectAndAppsDataXrefsStayInAdvertisingBuilders: true,
            Legacy8899BridgeStringsAbsent: true,
            SafetyCommandStringsAbsent: true,
            Ascii8899HitsClassifiedAsDigitTable: true,
            ExplicitBridgeToMpas8899ServerAppObserved: false,
            ModernSafetyOpcodeOwnerObserved: false);

    public static MiPlayIdmStateDecision EvaluateIdmRuntimeIdentityAndAuthLayer(
        MiPlayLx06IdmRuntimeBridgeSnapshot snapshot)
    {
        if (!snapshot.IdmRuntimeInitScriptStartsSeparateProcess)
        {
            return new MiPlayIdmStateDecision(false, "The idmruntime procd service startup was not observed.");
        }

        if (!snapshot.MiConnectMdnsServiceObserved ||
            !snapshot.AppsDataAdvertisementBuilderObserved ||
            !snapshot.ServiceNameAndServiceTypeAdvertisementObserved ||
            !snapshot.MiPlayAudioServiceTypeUrnObserved)
        {
            return new MiPlayIdmStateDecision(false, "The idmruntime mDNS/appsData advertisement identity layer is incomplete.");
        }

        if (!snapshot.AppClientServerRegistryObserved ||
            !snapshot.AppAuthAttributeHandshakeObserved ||
            !snapshot.AttributeNotificationRegistrationObserved ||
            !snapshot.SecureTransportModesObserved ||
            !snapshot.AppAuthXrefsResolveToSyslogAndCallbacks ||
            !snapshot.AttributeNotificationXrefsResolveToHandlerLogging ||
            !snapshot.MiConnectAndAppsDataXrefsStayInAdvertisingBuilders)
        {
            return new MiPlayIdmStateDecision(false, "The idmruntime endpoint auth or attribute-notification layer is incomplete.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "LX06 1.88.51 idmruntime exposes a separate IDM identity/auth layer: _mi-connect._udp. advertisement, appsData builder, serviceName+serviceType logging, the miplay-audio IDM service-type URN, AddAppClient/AddAppServer registry, APP_ATTRIBUTE_ID_APP_AUTH handling, setAttributeNotification, and MC_MI_SEC modes. The recovered xrefs resolve to syslog, attribute callbacks, mDNS/TXT builders, or static security-mode tables. This identifies the next static search area but does not by itself prove the mpas 8899 business-command bridge.");
    }

    public static MiPlayIdmStateDecision EvaluateMpas8899BridgeFromIdmRuntime(
        MiPlayLx06IdmRuntimeBridgeSnapshot snapshot)
    {
        if (!snapshot.MpasLinksLibIdmSdk)
        {
            return new MiPlayIdmStateDecision(false, "mpas has not been shown to link libidmsdk.so.");
        }

        if (!snapshot.Legacy8899BridgeStringsAbsent ||
            !snapshot.SafetyCommandStringsAbsent ||
            !snapshot.Ascii8899HitsClassifiedAsDigitTable ||
            !snapshot.MiConnectAndAppsDataXrefsStayInAdvertisingBuilders)
        {
            return new MiPlayIdmStateDecision(false, "The idmruntime negative bridge/string classification is incomplete.");
        }

        if (!snapshot.ExplicitBridgeToMpas8899ServerAppObserved)
        {
            return new MiPlayIdmStateDecision(
                false,
                $"The idmruntime identity/auth evidence is adjacent to mpas and includes the miplay-audio IDM URN, but its located xrefs stay inside IDM mDNS/appsData advertisement, APP_AUTH attribute callbacks, setAttributeNotification logging, and MC_MI_SEC tables. It has no mpas/mpap/Cmd_/CtrlClient/ServerApp/Safety string evidence; its ASCII 8899 hits are classified as digit-table context. It still lacks {RemainingMissingBridge}. It cannot justify another 8899 business-frame probe.");
        }

        if (!snapshot.ModernSafetyOpcodeOwnerObserved)
        {
            return new MiPlayIdmStateDecision(
                false,
                "A bridge to mpas was claimed, but the modern 0x1400..0x1403 owner is still not localized.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The idmruntime bridge to mpas 8899 and the modern SafetyAuth owner are both localized; a new one-frame read-only live validation can be designed.");
    }
}
