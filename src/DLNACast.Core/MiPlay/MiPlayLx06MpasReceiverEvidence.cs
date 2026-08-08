using System.Globalization;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLx06MpasReceiverSnapshot(
    bool MiplayInitScriptStartsMpas,
    bool MiplayInitScriptUsesProcdRespawn,
    bool MpasBinaryObserved,
    bool MpapBinaryObserved,
    bool MpasMdnsServiceTypeObserved,
    bool MpasMdnsRegisterPortImmediateObserved,
    bool MpasSecondaryTcp8899ServiceInitObserved,
    bool MpasDynamicDependencyListObserved,
    bool ReceiverDependencySafetyLayerNamesAbsent,
    bool MpapAuthGateStringObservedWithoutSafetyLayerNames,
    bool MpasModernSafetyOpcodeHandlersAbsent,
    bool ReceiverDependency1400HitsClassifiedAsBufferLengths,
    bool ReceiverModernSafetyHandlerRequires19413OrDynamicComponent,
    bool MpasCommandDispatcherObserved,
    bool MpasCommandHeaderOffsetsObserved,
    bool MpasAuthGateDealPacketObserved,
    bool MpasAuthCommandStringObservedAtGate,
    bool MpasAuthCommandIdObserved,
    bool MpasGetDeviceInfoRequestObserved,
    bool MpasGetDeviceInfoAckObserved,
    bool MpasGetDeviceInfoPreservesSequenceObserved,
    bool MpasGetDeviceInfoAsyncPreparePathObserved,
    bool MpasGetDeviceInfoDoesNotImmediatelyRejectUncachedInfo,
    bool MpasGetDeviceInfoAsyncCompletionCallbackObserved,
    bool MpasGetDeviceInfoAsyncCompletionSetsCachedFlag,
    bool MpasGetDeviceInfoAsyncCompletionSendsAck,
    bool MpasGetDeviceInfoAsyncCompletionUsesSavedSequence,
    bool MpasCtrlClientDealPacketGateObserved,
    bool MpasCtrlClientRequiresServerContext,
    bool MpasCtrlClientEnabledFlagInitialized,
    bool MpasCtrlClientEnabledFlagDefaultsTrue,
    bool MpasCtrlClientEnabledFlagDisableOnlyObservedOnRemoval,
    bool MpasCtrlClientRoutesToDoMpasCommand,
    bool MpasServerAppAddClientConstructsCtrlClient,
    bool MpasDoMpasCommandServiceNamePrecheckObserved,
    bool MpasDoMpasCommandServiceMismatchDoesNotBlockGetDeviceInfo,
    bool MpasServerAppAddClientBindsServerContext,
    bool MpasServerAppAddClientStartsAuthCountdown,
    bool MpasServerAppAddClientSendsCmdAuthThroughCtrlProtocol,
    bool MpasServerAppAuthAckDispatcherObserved,
    bool MpasServerAppAuthAckVerifiesChallengeResponse,
    bool MpasServerAppAuthAckSuccessSetsClientAuthFlag,
    bool MpasServerAppAuthAckFailureClosesHandle,
    bool MpasServerAppAuthAckSuccessCanEmitSyncPhoneStateNotify,
    bool MpasLiveAuthAckAcceptedBySyncPhoneStateNotifyObserved,
    bool MpasCtrlClientContextSetterObserved,
    bool MpasCtrlProtocolSendCommandHelperObserved,
    bool MpasUnhandledCommandQueuesWaitCmd,
    bool MpasCtrlProtocolFrameParserObserved,
    bool MpasCtrlProtocolHeaderBeforeCallbackObserved,
    bool MpasCtrlProtocolPayloadLengthObserved,
    bool MpasCtrlProtocolCallbackObserved,
    bool MpasCtrlProtocolVirtualDealPacketDispatchObserved,
    bool MpasCtrlProtocolBaseCallbackAdapterObserved,
    bool MpasCtrlClientEmbedsCtrlProtocol,
    bool MpasCtrlPipeEmbedsCtrlProtocol,
    bool MpasMiplayServiceCheckParserDispatchesDirectlyToDealPacket,
    bool MpasCtrlClientParserDispatchesDirectlyToDealPacket,
    bool MpasCtrlPipeParserDispatchesThroughOwnerContext,
    bool MpasCtrlProtocolCallbackInitiallyCleared,
    bool MpasCtrlProtocolBaseAdapterRequiresInstalledCallback,
    bool MpasSlaveDeviceDealPacketObserved,
    bool MpasSlaveDeviceAuthResponderObserved,
    bool MpasSlaveDeviceReceiveTableDefaultsGetDeviceInfo,
    bool MpasSlaveDeviceConstructedFromSeparatePath,
    bool MpasMiplayServiceCheckAuthSuccessCallbackObserved,
    bool MpasMiplayServiceCheckConstructorClearsAuthCompletionCallback,
    bool MpasMiplayServiceCheckConnectEvObserved,
    bool MpasMiplayServiceCheckConnectEvInstallsAuthSocket,
    bool MpasMiplayServiceCheckConnectEvInstallsSocketDataCallback,
    bool MpasMiplayServiceCheckConnectEvInstallsSocketStateCallback,
    bool MpasMiplayServiceCheckConnectOkStartsSocket,
    bool MpasMiplayServiceCheckConnectFailureSignalsFalse,
    bool MpasMiplayServiceCheckConnectEvDoesNotInstallResultCallback,
    bool MpasAuthSuccessDoesNotDirectlyEnterServerDispatcher,
    bool MpasOpenCommandObserved,
    bool MpasOpenStripsMirrorModeQueryBeforeWfdSearchObserved,
    bool MpasSenderInfoPreparedCmdOpenPathObserved,
    bool MpasAddMirrorAckCanRearmCmdOpenObserved,
    bool MpasSetPlaySourceServerCommandObserved,
    bool MpasSetPlaySourceAckBeforePayloadParseObserved,
    bool MpasSetPlaySourceEmptyPayloadAckOnlySafeObserved,
    bool MpasSetPlaySourceInternalPipeCommandObserved,
    bool MpasAddMirrorRequestCommandObserved,
    bool MpasExternalAddMirrorUnhandledByServerDispatcherObserved,
    bool MpasAddMirrorAckCommandIdObserved,
    bool MpasAddMirrorPayloadIdentityFragmentsObserved,
    bool MpasLocalAddMirrorPayloadFullyObserved,
    bool MpasLocalAddMirrorUsesLocalIpEndpoint,
    bool MpasLocalAddMirrorUsesDefault7236,
    bool MpasLocalAddMirrorIsLocalTrueObserved,
    bool MpasLocalCmdOpenBuiltFromLocalAddMirrorEndpointObserved,
    bool Candidate0058HandledByMpasServerDispatcher,
    bool MpapQuickAudioSinkObserved,
    bool MpapOpenMirrorClientObserved,
    bool MpapCmdOpenBridgeObserved,
    bool MpapCmdOpenSynthesizesWfdUrlObserved,
    bool MpapOpenMirrorClientParsesUrlPortObserved,
    bool MpapWfdClientConstructorParsesHostPortObserved,
    bool MpapSourceHostPortSessionKeysObserved,
    bool MpapRtspPresentationUrlTemplateObserved,
    bool MpapAudioDumpPathObserved);

/// <summary>
/// Offline-only static evidence from LX06 ROM 1.88.51 for the real MiPlay
/// audio receiver service. These constants intentionally model disassembly
/// facts and safety boundaries; they do not authorize any network or playback
/// operation.
/// </summary>
public static class MiPlayLx06MpasReceiverEvidence
{
    public const string FirmwareImageFileName = "mico_firmware_c0cb3a1a9_1.88.51.bin";
    public const string FirmwareSha256 =
        "A245370CA924BFB38AB9DE00CBBAB0E7A9513FD11F2E6A5907FDC1B3A8DE63EC";
    public const string FirmwareHardware = "LX06";
    public const string FirmwareRomVersion = "1.88.51";
    public const string FirmwareBuildDate = "2023-11-21";
    public const string CurrentObservedLx06Version = "1.94.13";

    public const string RootFsExtractionPath = "artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted";
    public const string MiplayInitScript = "etc/init.d/miplay";
    public const string MiplayInitCommand = "procd_set_param command /usr/bin/mpas";
    public const string MiplayInitRespawnCommand = "procd_set_param respawn 3600 5 0";
    public const string MiplayStopNotifyStatusCommand = "ubus call mediaplayer notify_mdplay_status '{\"status\":0}'";
    public const string MiplayStopRemoveFifoCommand = "rm -rf /tmp/multiroom.fifo";

    public const string MpasBinary = "usr/bin/mpas";
    public const int MpasBinarySize = 1_385_680;
    public const string MpasSha256 =
        "9336BA754E864DEE015CDEE688BC45631570133C8E64EF46EBEDD6800D805C43";

    public const string MpapBinary = "usr/bin/mpap";
    public const int MpapBinarySize = 1_544_328;
    public const string MpapSha256 =
        "BE0A07E405F28491871C2AA6397F8802FECBAC4279AEE61D01F05799C4C47481";

    public const string MpasMdnsServiceType = "_miplay_audio._tcp.local.";
    public const int MpasMdnsServiceTypeFileOffset = 0x12d058;
    public const int MpasMdnsServiceTypeVirtualAddress = 0x13d058;
    public const int MpasMdnsRegisterSetupAddress = 0x58d18;
    public const int MpasMdnsRegisterPortMoveAddress = 0x58d1c;
    public const int MpasMdnsRegisterServiceStringMoveLowAddress = 0x58d20;
    public const int MpasMdnsRegisterServiceStringMoveHighAddress = 0x58d28;
    public const int MpasMdnsRegisterPortImmediate = 0x22c3;
    public const int MpasMdnsRegisterPort = 8_899;
    public const string MpasMdnsInstanceString = "L16A-QT00521";
    public const int MpasMdnsInstanceStringVirtualAddress = 0x13d074;
    public const int MpasSecondaryTcp8899PortMoveAddress = 0x74a44;
    public const int MpasSecondaryTcp8899InitCallTarget = 0x40170;
    public const string MpasDynamicDependencyLibIdmSdk = "libidmsdk.so";
    public const string MpasDynamicDependencyLibIotdcmMiplay = "libiotdcm_miplay.so";
    public const string MpasDynamicDependencyLibIotdcm = "libiotdcm.so";
    public const string ReceiverSafetyLayerStringScanScope = "mpas, mpap, libiotdcm_miplay.so, libidmsdk.so, libiotdcm.so";
    public const string ReceiverSafetyLayerAbsentStringFamily = "SafetyData/SafetyAuth/SafetyInfo/DealSafety/CmdSource/SafetyKey";
    public const string ReceiverModernSafetyOpcodeScanScope = "mpas, mpap, libiotdcm_miplay.so, libidmsdk.so, libiotdcm.so";
    public const string ReceiverModernSafetyOpcodeFamily = "0x1400/0x1401/0x1402/0x1403";
    public const string ReceiverBasicFunctionReconstructionScope = "legacy 8899 auth, 0x001e/0x001f device-info, Cmd_Open 0x0000, mpap audio receiver bridge";
    public const int MpasAlignedModernSafetyOpcodeImmediateCount = 0;
    public const int MpapAlignedModernSafetyOpcodeImmediateCount = 0;
    public const int CheckedReceiverDependencyAlignedSafetyAckOpcodeImmediateCount = 0;
    public const int LibIotdcmMiplayAligned1400ImmediateAddress = 0x615d4;
    public const int LibIotdcmMiplayAligned1400ImmediateValue = 0x1400;
    public const int LibIdmSdkAligned1400ImmediateAddress = 0x0a9a60;
    public const int LibIdmSdkAligned1400ImmediateValue = 0x1400;
    public const string ReceiverDependency1400ImmediateMeaning = "5120-byte buffer/log length, not MiPlay 0x1400 command handler";

    public const int MpasDoMpasCommandEntryAddress = 0x65730;
    public const string MpasDoMpasCommandMangledLambdaPrefix =
        "ServerApp13doMpasCommandESt10shared_ptrI10CtrlClientER17_tagCtrlCmdHeaderPvj";
    public const int MpasCommandHeaderCommandOffset = 1;
    public const int MpasCommandHeaderSequenceOffset = 3;

    public const string MpasAuthCommandString = "Cmd_Auth";
    public const int CmdAuth = 0x0028;
    public const int CmdAuthAck = 0x0029;
    public const int MpasAuthHeaderCommandReadAddress = 0x3ce3c;
    public const int MpasAuthCommandCompareAddress = 0x3ce40;
    public const int MpasAuthDealPacketLogAddress = 0x3ce74;
    public const string MpasAuthHandlerBoundary = "MiplayServiceCheck::DealPacket";
    public const int MpasMiplayServiceCheckConstructorAddress = 0x3d268;
    public const int MpasMiplayServiceCheckCtrlProtocolConstructorCallAddress = 0x3d284;
    public const int MpasMiplayServiceCheckVtableVirtualAddress = 0x1390ec;
    public const int MpasMiplayServiceCheckAuthSocketRefOffset = 0x78;
    public const int MpasMiplayServiceCheckAuthSocketRefClearAddress = 0x3d3b4;
    public const int MpasMiplayServiceCheckAuthCompletionCallbackManagerOffset = 0x8c;
    public const int MpasMiplayServiceCheckAuthCompletionCallbackInvokerOffset = 0x90;
    public const int MpasMiplayServiceCheckAuthCompletionCallbackManagerCheckAddress = 0x3ce90;
    public const int MpasMiplayServiceCheckAuthCompletionCallbackManagerClearAddress = 0x3d3c0;
    public const int MpasMiplayServiceCheckAuthCompletionCallbackInvokerAddress = 0x3cee8;
    public const int MpasMiplayServiceCheckConnectEvEntryAddress = 0x3d4e0;
    public const int MpasMiplayServiceCheckConnectEvSocketAllocationAddress = 0x3d5d0;
    public const int MpasMiplayServiceCheckConnectEvSocketAllocationSize = 0x0b4;
    public const int MpasMiplayServiceCheckConnectEvSocketConstructorCallAddress = 0x3d5e0;
    public const int MpasMiplayServiceCheckAuthSocketRefInstallAddress = 0x3d624;
    public const int MpasMiplayServiceCheckAuthSocketControlInstallAddress = 0x3d62c;
    public const int MpasMiplayServiceCheckSocketDataCallbackRegisterCallAddress = 0x3d6c8;
    public const int MpasMiplayServiceCheckSocketDataCallbackInvokerAddress = 0x3cefc;
    public const int MpasMiplayServiceCheckSocketDataCallbackManagerAddress = 0x3cc40;
    public const int MpasMiplayServiceCheckSocketStateCallbackRegisterCallAddress = 0x3d710;
    public const int MpasMiplayServiceCheckSocketStateCallbackInvokerAddress = 0x3ccb0;
    public const int MpasMiplayServiceCheckSocketStateCallbackManagerAddress = 0x3cc78;
    public const int MpasMiplayServiceCheckConnectStartCallAddress = 0x3d744;
    public const int MpasMiplayServiceCheckConnectStartSocketVtableOffset = 0x5c;
    public const int MpasMiplayServiceCheckActiveSocketOffset = 0x28;
    public const int MpasMiplayServiceCheckConnectStateHandlerAddress = 0x3ccb0;
    public const int MpasMiplayServiceCheckConnectOkLogAddress = 0x3ce14;
    public const int MpasMiplayServiceCheckConnectOkLogStringVirtualAddress = 0x1391e4;
    public const int MpasMiplayServiceCheckConnectFailedLogAddress = 0x3cd14;
    public const int MpasMiplayServiceCheckConnectFailedLogStringVirtualAddress = 0x13920c;
    public const int MpasMiplayServiceCheckConnectErrorLogAddress = 0x3cd74;
    public const int MpasMiplayServiceCheckConnectErrorLogStringVirtualAddress = 0x13923c;
    public const int MpasMiplayServiceCheckConnectClosedLogAddress = 0x3cde4;
    public const int MpasMiplayServiceCheckConnectClosedLogStringVirtualAddress = 0x139264;
    public const int MpasMiplayServiceCheckConnectFailureResultCallbackFalseAddress = 0x3cd44;
    public const int MpasMiplayServiceCheckConnectErrorResultCallbackFalseAddress = 0x3cdb8;
    public const int MpasMiplayServiceCheckConnectOkSocketStartAddress = 0x3ce30;

    public const string MpasGetDeviceInfoString = "Cmd_GetDeviceInfo";
    public const int MpasGetDeviceInfoStringFileOffset = 0x12e714;
    public const int MpasGetDeviceInfoStringVirtualAddress = 0x13e714;
    public const int MpasGetDeviceInfoDispatchCompareAddress = 0x6825c;
    public const int MpasGetDeviceInfoCachedFlagReadAddress = 0x68290;
    public const int MpasGetDeviceInfoCachedFlagOffset = 0x2c0;
    public const int MpasGetDeviceInfoAsyncPreparePathAddress = 0x69ad8;
    public const int MpasGetDeviceInfoAsyncCallbackAddress = 0x65320;
    public const int MpasGetDeviceInfoAsyncCacheFlagSetAddress = 0x65398;
    public const int MpasGetDeviceInfoAsyncSavedSequenceReadAddress = 0x6541c;
    public const int MpasGetDeviceInfoAsyncSavedSequenceOffset = 0x83;
    public const int MpasGetDeviceInfoAsyncAckCommandMoveAddress = 0x65430;
    public const int MpasGetDeviceInfoAckCommandMoveAddress = 0x68350;
    public const int MpasSendCommandHelperAddress = 0x368bc;

    public const int MpasCtrlClientConstructorAddress = 0x331ec;
    public const int MpasCtrlClientEnabledFlagInitAddress = 0x332f8;
    public const int MpasCtrlClientEnabledFlagSetterAddress = 0x329b4;
    public const int MpasCtrlClientEnabledFlagDisableOnRemovalCallAddress = 0x59a24;
    public const int MpasCtrlClientDealPacketEntryAddress = 0x3344c;
    public const int MpasCtrlClientContextPointerOffset = 0x0f4;
    public const int MpasCtrlClientDealPacketContextReadAddress = 0x3344c;
    public const int MpasCtrlClientServerContextSetterAddress = 0x329bc;
    public const int MpasCtrlClientVtableVirtualAddress = 0x137f80;
    public const int MpasCtrlClientStartAuthCountdownStringVirtualAddress = 0x138034;
    public const int MpasCtrlClientStartAuthCountdownLambdaStringVirtualAddress = 0x138090;
    public const int MpasCtrlClientStartAuthCountdownVtableOffset = 0x34;
    public const int MpasCtrlClientStartAuthCountdownTargetAddress = 0x49728;
    public const int MpasCtrlClientAcceptedSocketSetupVtableOffset = 0x58;
    public const int MpasCtrlClientAcceptedSocketSetupTargetAddress = 0x499a4;
    public const int MpasServerAppAddClientEntryAddress = 0x5de08;
    public const int MpasServerAppAddClientAllocatedCtrlClientSize = 368;
    public const int MpasServerAppAddClientAllocationAddress = 0x5de64;
    public const int MpasServerAppAddClientCtrlClientConstructorCallAddress = 0x5de74;
    public const int MpasServerAppAddClientAcceptedSocketSetupCallAddress = 0x5df28;
    public const int MpasServerAppAddClientServerContextSetterCallAddress = 0x5df34;
    public const int MpasServerAppAddClientStartAuthCountdownCallAddress = 0x5df44;
    public const int MpasServerAppAddClientAuthPayloadLengthStoreAddress = 0x5e00c;
    public const int MpasServerAppAddClientAuthPayloadPointerMoveAddress = 0x5e010;
    public const int MpasServerAppAddClientAuthSequenceMoveAddress = 0x5e018;
    public const int MpasServerAppAddClientAuthCommandMoveAddress = 0x5e01c;
    public const int MpasServerAppAddClientSendAuthCommandCallAddress = 0x5e020;
    public const int MpasCtrlProtocolSendCommandWithPayloadLengthHelperAddress = 0x367bc;
    public const int MpasServerAppAddClientAuthCommandId = 0x0028;
    public const int MpasServerAppAddClientAuthSequence = 0x0000;
    public const int MpasServerAppAuthAckDispatchCompareAddress = 0x65ea8;
    public const int MpasServerAppAuthAckBranchAddress = 0x65eb0;
    public const int MpasServerAppAuthAckClientChallengePointerReadAddress = 0x65ec4;
    public const int MpasServerAppAuthAckClientChallengeLengthReadAddress = 0x65ec8;
    public const int MpasServerAppAuthAckClientChallengePointerOffset = 0x0f8;
    public const int MpasServerAppAuthAckClientChallengeLengthOffset = 0x0fc;
    public const int MpasServerAppAuthAckIpadXorImmediate = 0x36363636;
    public const int MpasServerAppAuthAckOpadTransformXorImmediate = 0x6a6a6a6a;
    public const int MpasServerAppAuthAckDigestLength = 20;
    public const int MpasServerAppAuthAckLogStringVirtualAddress = 0x13ea20;
    public const string MpasServerAppAuthAckLogString = "[%d D %s %s:%d]Cmd_Auth_Ack(%d), authResult[%s]";
    public const int MpasServerAppAuthAckFalseStringVirtualAddress = 0x1375c4;
    public const string MpasServerAppAuthAckFalseString = "false";
    public const int MpasServerAppAuthAckTrueStringVirtualAddress = 0x138134;
    public const string MpasServerAppAuthAckTrueString = "true";
    public const int MpasCtrlClientAuthFlagOffset = 0x160;
    public const int MpasCtrlClientAuthFlagInitAddress = 0x332f0;
    public const int MpasServerAppAuthAckSuccessSetAuthFlagAddress = 0x663f4;
    public const int MpasServerAppAuthAckFailureBranchAddress = 0x6a5c4;
    public const int MpasServerAppAuthAckFailureLogStringVirtualAddress = 0x13ea50;
    public const string MpasServerAppAuthAckFailureLogString = "[%d D %s %s:%d]Cmd_Auth_Ack close Handle";
    public const int MpasServerAppAuthAckFailureCloseVtableOffset = 0x14;
    public const int MpasServerAppAuthAckFailureCloseVtableLoadAddress = 0x6a600;
    public const int MpasServerAppAuthAckFailureCloseCallAddress = 0x6a604;
    public const int MpasServerAppAuthAckSyncPhoneStateCheckCallAddress = 0x663f8;
    public const int MpasServerAppSyncPhoneStatePredicateAddress = 0x5bb5c;
    public const int MpasServerAppSyncPhoneStateStringVirtualAddress = 0x13d540;
    public const string MpasServerAppSyncPhoneStateString = "syncPhoneState";
    public const int MpasServerAppSyncPhoneStateModeByteOffset = 0x38b;
    public const int MpasServerAppSyncPhoneStateSequenceOffset = 0x212;
    public const int MpasServerAppSyncPhoneStateSendCommandMoveAddress = 0x664e4;
    public const int MpasServerAppSyncPhoneStateSendHelperAddress = 0x59ae4;
    public const int MpasServerAppSyncPhoneStateNotifyCommandId = 0x0022;
    public const string MpasLiveAcceptedAuthAckNotifyPayloadHex = "04 6d 6f 64 65 03 02";
    public const int MpasServerAppAddClientLogStringVirtualAddress = 0x13d5c4;
    public const int MpasServerAppAddClientServerAppStringVirtualAddress = 0x13db5c;
    public const string MpasServerAppAddClientLogString = "add client %d";
    public const int MpasCtrlClientDealPacketEnabledFlagOffset = 0x161;
    public const int MpasCtrlClientDealPacketEnabledFlagReadAddress = 0x33458;
    public const int MpasCtrlClientBridgeCallAddress = 0x33584;
    public const int MpasCtrlClientDoMpasCommandBridgeAddress = 0x6dae0;
    public const int MpasDoMpasCommandCallFromBridgeAddress = 0x6db58;
    public const int MpasDoMpasCommandFalseReturnCheckAddress = 0x6db70;
    public const int MpasDoMpasCommandServiceNamePrecheckStartAddress = 0x6575c;
    public const int MpasDoMpasCommandServiceNameGetNameCallAddress = 0x65774;
    public const int MpasDoMpasCommandServiceNameMemcmpAddress = 0x65b44;
    public const int MpasDoMpasCommandServiceMismatchEarlyCommandCheckAddress = 0x657bc;
    public const int MpasDoMpasCommandServiceMismatchAllowedCommandLow = 0x0004;
    public const int MpasDoMpasCommandServiceMismatchAllowedCommandHigh = 0x0006;
    public const int MpasDoMpasCommandMainSwitchEntryAddress = 0x65810;
    public const int MpasWaitCommandLogStringVirtualAddress = 0x13eb04;
    public const int MpasWaitCommandObjectAllocationAddress = 0x6dc38;
    public const int MpasWaitCommandListOffset = 0x0c8;
    public const int MpasWaitCommandCountOffset = 0x0d0;
    public const int MpasWaitCommandMaximumBeforeThrow = 16;
    public const int MpasThrowWaitingRequestLogStringVirtualAddress = 0x13eb28;

    public const int MpasCtrlProtocolFrameParserEntryAddress = 0x36a68;
    public const int MpasCtrlClientCtrlProtocolSubobjectOffset = 0x0b4;
    public const int MpasCtrlClientCtrlProtocolParserThunkAddress = 0x32464;
    public const int MpasCtrlPipeCtrlProtocolSubobjectOffset = 0x098;
    public const int MpasCtrlPipeCtrlProtocolParserThunkAddress = 0x33838;
    public const int MpasCtrlProtocolConstructorAddress = 0x33cb8;
    public const int MpasCtrlProtocolVtableVirtualAddress = 0x13835c;
    public const int MpasCtrlProtocolBaseDealPacketAdapterAddress = 0x33b90;
    public const int MpasCtrlProtocolBaseVtableDealPacketSlotOffset = 0x08;
    public const int MpasCtrlProtocolVirtualDealPacketCompareAddress = 0x36cac;
    public const int MpasCtrlProtocolVirtualDealPacketBranchAddress = 0x36d08;
    public const int MpasCtrlProtocolVirtualDealPacketCallAddress = 0x36d10;
    public const int MpasMiplayServiceCheckDealPacketVtableSlotAddress = 0x1390f4;
    public const int MpasMiplayServiceCheckDealPacketVtableTargetAddress = 0x3ce3c;
    public const int MpasMiplayServiceCheckParserDataCallbackCallAddress = 0x3cf48;
    public const int MpasCtrlClientCtrlProtocolConstructorCallAddress = 0x33294;
    public const int MpasCtrlClientCtrlProtocolSecondaryVtableStoreAddress = 0x332c0;
    public const int MpasCtrlClientCtrlProtocolSecondaryVtableVirtualAddress = 0x137ff4;
    public const int MpasCtrlClientCtrlProtocolSecondaryDealPacketThunkAddress = 0x33754;
    public const int MpasCtrlClientCtrlProtocolSecondaryThunkAdjust = 0x0b4;
    public const int MpasCtrlPipeCtrlProtocolConstructorCallAddress = 0x33a44;
    public const int MpasCtrlPipeCtrlProtocolSecondaryVtableStoreAddress = 0x33a54;
    public const int MpasCtrlPipeCtrlProtocolSecondaryVtableVirtualAddress = 0x13827c;
    public const int MpasCtrlPipeCtrlProtocolSecondaryDealPacketThunkAddress = 0x33908;
    public const int MpasCtrlPipeDealPacketEntryAddress = 0x33888;
    public const int MpasCtrlPipeOwnerContextOffset = 0x0d4;
    public const int MpasCtrlProtocolMinimumFrameLength = 9;
    public const int MpasCtrlProtocolHeaderCopyAddress = 0x36c50;
    public const int MpasCtrlProtocolBufferDataOffset = 4;
    public const int MpasCtrlProtocolWireCommandOffset = 1;
    public const int MpasCtrlProtocolWireSequenceOffset = 3;
    public const int MpasCtrlProtocolWirePayloadLengthOffset = 5;
    public const int MpasCtrlProtocolCommandFieldReadAddress = 0x36c60;
    public const int MpasCtrlProtocolSequenceFieldReadAddress = 0x36c64;
    public const int MpasCtrlProtocolPayloadLengthReadAddress = 0x36c68;
    public const int MpasCtrlProtocolCommandEndianSwapAddress = 0x36c70;
    public const int MpasCtrlProtocolSequenceEndianSwapAddress = 0x36c74;
    public const int MpasCtrlProtocolPayloadLengthEndianSwapAddress = 0x36c7c;
    public const int MpasCtrlProtocolCallbackManagerOffset = 0x34;
    public const int MpasCtrlProtocolCallbackInvokerOffset = 0x38;
    public const int MpasCtrlProtocolCallbackPointerOffset = 0x38;
    public const int MpasCtrlProtocolCallbackClearAddress = 0x33cd4;
    public const int MpasCtrlProtocolCallbackPresenceCheckAddress = 0x36cb4;
    public const int MpasCtrlProtocolCallbackInvokerLoadAddress = 0x36ce0;
    public const int MpasCtrlProtocolCallbackCallAddress = 0x36ce8;
    public const int MpasGenericFunctionAssignmentCallbackManagerStoreAddress = 0x48224;
    public const int MpasGenericFunctionAssignmentCallbackInvokerStoreAddress = 0x48230;

    public const string MpasSlaveDeviceName = "SlaveDevice";
    public const int MpasSlaveDeviceVtableVirtualAddress = 0x13ff70;
    public const int MpasSlaveDeviceConstructorAddress = 0x7dff4;
    public const int MpasSlaveDeviceConstructorCallAddress = 0x5f700;
    public const int MpasSlaveDeviceAllocationSize = 0x160;
    public const int MpasSlaveDeviceDealPacketAddress = 0x7e694;
    public const int MpasSlaveDeviceCommandRangeBase = 0x001a;
    public const int MpasSlaveDeviceCommandRangeCount = 30;
    public const int MpasSlaveDeviceDefaultReturnAddress = 0x7ed64;
    public const int MpasSlaveDeviceAuthBranchAddress = 0x7e79c;
    public const int MpasSlaveDeviceAuthLogStringVirtualAddress = 0x1405bc;
    public const string MpasSlaveDeviceAuthLogString = "SlaveDevice recv Cmd_Auth";
    public const int MpasSlaveDeviceAuthResponseCommandMoveAddress = 0x7ea90;
    public const int MpasSlaveDeviceAuthResponseSendCallAddress = 0x7ea98;
    public const int MpasSlaveDeviceAuthResponseCommandId = 0x0029;
    public const int MpasSlaveDeviceGetDeviceInfoTableTarget = 0x7ed64;
    public const int MpasSlaveDeviceGetDeviceInfoAckTableTarget = 0x7ed64;
    public const int MpasSlaveDeviceGetDeviceInfoMethodStringVirtualAddress = 0x13ff04;
    public const string MpasSlaveDeviceGetDeviceInfoMethodString = "getDeviceInfo";
    public const int MpasSlaveDeviceGetDeviceInfoLogStringVirtualAddress = 0x140368;
    public const string MpasSlaveDeviceGetDeviceInfoLogString = "SlaveDevice getDeviceInfo";

    public const string MpasOpenCommandString = "Cmd_Open";
    public const int MpasOpenDispatchCommandId = 0x0000;
    public const int MpasOpenLogAddress = 0x667c4;
    public const int MpasOpenPayloadBranchAddress = 0x69c28;
    public const string MpasOpenMirrorModeMarkerString = "?mirrorMode=";
    public const int MpasOpenMirrorModeMarkerVirtualAddress = 0x13e604;
    public const int MpasOpenMirrorModeMarkerFindAddress = 0x69c7c;
    public const int MpasOpenMirrorModeMarkerLength = 12;
    public const int MpasOpenMirrorModeValueSubstringStartAddress = 0x69ca0;
    public const int MpasOpenMirrorModeValueSubstringCallAddress = 0x69cb4;
    public const int MpasOpenMirrorModeStrtolAddress = 0x69cd0;
    public const int MpasOpenModeOneBranchAddress = 0x6bf54;
    public const int MpasOpenModeTwoBranchAddress = 0x6c028;
    public const int MpasOpenModeStateTransitionHelperAddress = 0x702a0;
    public const string MpasOpenWfdUrlMarkerString = "wfd://";
    public const int MpasOpenWfdUrlMarkerVirtualAddress = 0x13e66c;
    public const int MpasOpenUrlWithoutMirrorModeSubstringStartAddress = 0x69da0;
    public const int MpasOpenUrlWithoutMirrorModeSubstringCallAddress = 0x69da8;
    public const int MpasOpenAssignUrlWithoutMirrorModeAddress = 0x69db8;
    public const int MpasOpenWfdUrlMarkerFindAddress = 0x69dcc;
    public const int MpasOpenHostPortSeparatorSearchAddress = 0x69e20;
    public const int MpasOpenHostCopyAddress = 0x69e38;
    public const string MpasOpenSourceChangedNotifyText = "seize";
    public const int MpasOpenSourceChangedNotifyTextVirtualAddress = 0x13e698;
    public const int MpasOpenSourceChangedNotifyCommandId = 0x0022;
    public const int MpasOpenSourceChangedNotifySendCallAddress = 0x69fb8;
    public const string MpasSenderInfoPreparedString = "sender-info-prepared";
    public const int MpasSenderInfoPreparedStringFileOffset = 0x12efac;
    public const int MpasSenderInfoPreparedStringVirtualAddress = 0x13efac;
    public const int MpasSenderInfoPreparedStringLoadAddress = 0x70d88;
    public const string MpasSenderInfoPreparedStatusLogTail = "sender-info-prepared index:%d port:%d valid:%d";
    public const int MpasSenderInfoPreparedStatusLogStringVirtualAddress = 0x13efcc;
    public const int MpasSenderInfoPreparedStatusLogLoadAddress = 0x70e38;
    public const string MpasSenderInfoPreparedLocalCmdOpenLogString =
        "on sender-info-prepared local send Cmdtype::Cmd_Open %s";
    public const int MpasSenderInfoPreparedLocalCmdOpenLogStringFileOffset = 0x12f098;
    public const int MpasSenderInfoPreparedLocalCmdOpenLogStringVirtualAddress = 0x13f098;
    public const int MpasSenderInfoPreparedLocalCmdOpenLogLoadAddress = 0x71328;
    public const string MpasSenderInfoPreparedSlaveCmdOpenLogString =
        "on sender-info-prepared pSlave send Cmdtype::Cmd_Open %s";
    public const int MpasSenderInfoPreparedSlaveCmdOpenLogStringFileOffset = 0x12f0e0;
    public const int MpasSenderInfoPreparedSlaveCmdOpenLogStringVirtualAddress = 0x13f0e0;
    public const int MpasSenderInfoPreparedSlaveCmdOpenLogLoadAddress = 0x71158;
    public const int MpasSenderInfoPreparedCmdOpenSendCallAddress = 0x711bc;
    public const int MpasSenderInfoPreparedCmdOpenSendHelperAddress = 0x367bc;
    public const int MpasSenderInfoPreparedCmdOpenCommandId = 0x0000;
    public const string MpasCmdAddMirrorAckString = "Cmd_AddMirror_Ack";
    public const int MpasCmdAddMirrorAckStringFileOffset = 0x12f460;
    public const int MpasCmdAddMirrorAckStringVirtualAddress = 0x13f460;
    public const string MpasCmdAddMirrorAckMasterCmdOpenLogString =
        "on Cmd_AddMirror_Ack master send Cmdtype::Cmd_Open";
    public const int MpasCmdAddMirrorAckMasterCmdOpenLogStringFileOffset = 0x12f4b4;
    public const int MpasCmdAddMirrorAckMasterCmdOpenLogStringVirtualAddress = 0x13f4b4;
    public const int MpasCmdAddMirrorAckMasterCmdOpenLogLoadAddress = 0x72210;
    public const int MpasCmdAddMirrorAckSequenceCompareAddress = 0x721e8;
    public const int MpasCmdAddMirrorAckPendingFlagClearAddress = 0x72230;
    public const int MpasCmdAddMirrorAckSavedSequenceStoreAddress = 0x72234;
    public const int MpasCmdAddMirrorAckSequenceResetAddress = 0x72238;
    public const int MpasSetPlaySourceDispatchCompareAddress = 0x65840;
    public const int MpasSetPlaySourceHandlerAddress = 0x66ad8;
    public const int MpasSetPlaySourceCommandId = 0x0040;
    public const int MpasSetPlaySourceAcknowledgementCommandId = 0x0041;
    public const int MpasSetPlaySourceAckSendCommandMoveAddress = 0x66b50;
    public const int MpasSetPlaySourceAckSendCallAddress = 0x66b58;
    public const int MpasSetPlaySourcePayloadPresenceCheckAddress = 0x66b70;
    public const int MpasSetPlaySourcePayloadGateReturnAddress = 0x657cc;
    public const int MpasSetPlaySourceJsonParseCallAddress = 0x66c70;
    public const int MpasSetPlaySourceFieldAssignRefChannelAddress = 0x67710;
    public const int MpasSetPlaySourceFieldAssignRefFunctionAddress = 0x67730;
    public const int MpasSetPlaySourceFieldAssignRefContentAddress = 0x67740;
    public const string MpasSetPlaySourceDataLengthLogString = "Cmd_SetPlaySource datalen[%d]";
    public const int MpasSetPlaySourceDataLengthLogStringVirtualAddress = 0x13df10;
    public const int MpasSetPlaySourceDataLengthLogLoadAddress = 0x66b08;
    public const string MpasSetPlaySourceJsonKeyRefChannel = "ref_channel";
    public const string MpasSetPlaySourceJsonKeyRefFunction = "ref_function";
    public const string MpasSetPlaySourceJsonKeyRefContent = "ref_content";
    public const int MpasSetPlaySourceJsonKeyRefChannelVirtualAddress = 0x13cb8c;
    public const int MpasSetPlaySourceJsonKeyRefFunctionVirtualAddress = 0x13cbc0;
    public const int MpasSetPlaySourceJsonKeyRefContentVirtualAddress = 0x13ccd8;
    public const int MpasSetPlaySourceJsonKeyRefChannelCompareAddress = 0x66d14;
    public const int MpasSetPlaySourceJsonKeyRefFunctionCompareAddress = 0x66ccc;
    public const int MpasSetPlaySourceJsonKeyRefContentCompareAddress = 0x66ce8;
    public const string MpasSetPlaySourceInternalPipeLogString = "setPlaySource Cmd_SetPlaySource[%s][%u]";
    public const int MpasSetPlaySourceInternalPipeLogStringVirtualAddress = 0x13fb74;
    public const int MpasSetPlaySourceInternalPipeLogLoadAddress = 0x74510;
    public const int MpasSetPlaySourceInternalPipeCommandId = 0x005a;
    public const int MpasSetPlaySourceInternalPipeSendCallAddress = 0x74544;
    public const string MpasCmdAddMirrorString = "Cmd_AddMirror";
    public const int MpasCmdAddMirrorCommandId = 0x002e;
    public const int MpasCmdAddMirrorAckCommandId = 0x002f;
    public const int MpasServerDispatcherAddMirrorLowerRangeCompareAddress = 0x65e9c;
    public const int MpasServerDispatcherAddMirrorHigherRangeCompareAddress = 0x666b8;
    public const int MpasServerDispatcherAddMirrorHigherRangeSecondCompareAddress = 0x666c0;
    public const int MpasServerDispatcherUnhandledCommandReturnFalseAddress = 0x667e8;
    public const int MpasCmdAddMirrorAckDispatchCompareAddress = 0x70a5c;
    public const int MpasCmdAddMirrorAckDispatchBranchAddress = 0x70a68;
    public const int MpasCmdAddMirrorSendCommandMoveAddress = 0x6e96c;
    public const int MpasCmdAddMirrorSendCallAddress = 0x6e970;
    public const int MpasCmdAddMirrorAlternateSendCommandMoveAddress = 0x6f1c8;
    public const int MpasCmdAddMirrorAlternateSendCallAddress = 0x6f1cc;
    public const int MpasCmdAddMirrorPendingFlagSetAddress = 0x6e948;
    public const int MpasCmdAddMirrorSavedSequenceStoreAddress = 0x6e94c;
    public const int MpasCmdAddMirrorSavedSequenceOffset = 0x32e;
    public const int MpasCmdAddMirrorPendingFlagOffset = 0x332;
    public const string MpasCmdAddMirrorPayloadFromFragment = "from:";
    public const string MpasCmdAddMirrorPayloadIsLocalFragment = "&islocal:";
    public const int MpasCmdAddMirrorPayloadFromFragmentVirtualAddress = 0x13ec74;
    public const int MpasCmdAddMirrorPayloadIsLocalFragmentVirtualAddress = 0x13ebc8;
    public const int MpasCmdAddMirrorPayloadFromAppendAddress = 0x6ef30;
    public const int MpasCmdAddMirrorPayloadIsLocalAppendAddress = 0x6ef68;
    public const int MpasCmdAddMirrorPayloadIsLocalFalseStringVirtualAddress = 0x141620;
    public const int MpasCmdAddMirrorPayloadIsLocalFalseStringFileOffset = 0x131620;
    public const string MpasCmdAddMirrorPayloadIsLocalFalseString = "0";
    public const int MpasLocalAddMirrorHelperEntryAddress = 0x6e620;
    public const int MpasLocalAddMirrorGetLocalIpCallAddress = 0x6e630;
    public const string MpasLocalAddMirrorDefaultPortSuffix = ":7236";
    public const int MpasLocalAddMirrorDefaultPortSuffixVirtualAddress = 0x13ebb8;
    public const int MpasLocalAddMirrorDefaultPortSuffixAppendAddress = 0x6e6c4;
    public const int MpasLocalAddMirrorDefaultPortImmediate = 0x1c44;
    public const int MpasLocalAddMirrorDefaultPort = 7_236;
    public const int MpasLocalAddMirrorDefaultPortStoreAddress = 0x6e6ec;
    public const int MpasLocalAddMirrorServerAppPortOffset = 0x34c;
    public const int MpasLocalAddMirrorServerAppEndpointOffset = 0x334;
    public const int MpasLocalAddMirrorEndpointAssignAddress = 0x6e6f0;
    public const string MpasLocalAddMirrorPayloadTemplate = "<local-ip>:7236&from:<local-ip>&islocal:1";
    public const string MpasLocalAddMirrorPayloadFromPrefixFragment = "&from:";
    public const int MpasLocalAddMirrorPayloadFromPrefixVirtualAddress = 0x13ebc0;
    public const int MpasLocalAddMirrorPayloadEndpointAppendAddress = 0x6e704;
    public const int MpasLocalAddMirrorPayloadFromPrefixAppendAddress = 0x6e728;
    public const int MpasLocalAddMirrorPayloadFromLocalIpAppendAddress = 0x6e740;
    public const int MpasLocalAddMirrorPayloadIsLocalAppendAddress = 0x6e7a0;
    public const string MpasLocalAddMirrorPayloadIsLocalTrueString = "1";
    public const int MpasLocalAddMirrorPayloadIsLocalTrueStringVirtualAddress = 0x1421ec;
    public const int MpasLocalAddMirrorPayloadIsLocalTrueAppendAddress = 0x6e814;
    public const string MpasLocalAddMirrorLogString = "addLocalMediaMirror Cmd_AddMirror[%s][%zu] %d";
    public const int MpasLocalAddMirrorLogStringVirtualAddress = 0x13ebd4;
    public const int MpasLocalAddMirrorLogLoadAddress = 0x6e910;
    public const int MpasLocalEndpointBuilderAddress = 0x567f4;
    public const int MpasLocalIpGetterAddress = 0x526e0;
    public const string MpasLocalIpErrorString = "local ip error";
    public const int MpasLocalIpErrorStringVirtualAddress = 0x13c774;
    public const int MpasLocalEndpointBuilderDefaultPortCompareAddress = 0x56850;
    public const int MpasLocalEndpointBuilderPortStoreAddress = 0x569b4;
    public const int MpasLocalEndpointBuilderPortIncrementAddress = 0x569c0;
    public const int MpasLocalEndpointBuilderErrorAssignAddress = 0x56ba8;
    public const int MpasSlaveDeviceMediaUrlSetupEntryAddress = 0x7c068;
    public const int MpasSlaveDeviceMediaUrlEndpointBuilderCallAddress = 0x7c0cc;
    public const int MpasSlaveDeviceMediaUrlPortSeedOffset = 0x0b8;
    public const int MpasSlaveDeviceMediaUrlOffset = 0x90;
    public const string MpasSlaveDeviceMediaUrlLogTail = "SlaveDevice m_strMediaUrl[%s]";
    public const int MpasSlaveDeviceMediaUrlLogTailVirtualAddress = 0x1403b4;
    public const int MpasSlaveDeviceMediaUrlLogLoadAddress = 0x7c160;
    public const int MpasSlaveDeviceMediaUrlLogCallAddress = 0x7c168;
    public const string MpasSlaveDeviceAddMirrorLogTail = "SlaveDevice addMirror";
    public const int MpasSlaveDeviceAddMirrorLogTailVirtualAddress = 0x1403e4;
    public const int MpasSlaveDeviceAddMirrorLogLoadAddress = 0x7c1b4;
    public const int MpasSlaveDeviceAddMirrorBuilderCallAddress = 0x7c2e8;
    public const int MpasStartLocalMediaClientEntryAddress = 0x6ea90;
    public const string MpasStartLocalMediaClientLogString = "startLocalMediaClient Cmd_Open[%s][%zu]";
    public const int MpasStartLocalMediaClientLogStringVirtualAddress = 0x13ec18;
    public const int MpasStartLocalMediaClientLogLoadAddress = 0x6ec20;
    public const int MpasStartLocalMediaClientSendCommandMoveAddress = 0x6ec60;
    public const int MpasStartLocalMediaClientSendCallAddress = 0x6ec64;
    public const int MpasAlternateOpenishBranchCommandId = 0x0036;

    public const int CmdOpen = 0x0000;
    public const int CmdGetDeviceInfo = 0x001e;
    public const int CmdGetDeviceInfoAck = 0x001f;
    public const int CandidateLegacyProbe0058 = 0x0058;
    public const int CmdSpeakerRandomPlay = 0x0062;

    public const string MpapQuickAudioSinkString = "MiPlayQuick_AudioSink";
    public const string MpapOpenMirrorClientString = "OpenMirrorClient";
    public const string MpapDealPacketString = "DealPacket";
    public const string MpapAudioDumpPath = "/data/miplay/audio_dump";
    public const string MpapAacEldCodecString = "audio/mp4a-latm";
    public const string MpapCmdOpenString = "Cmd_Open";
    public const int MpapCmdOpenStringVirtualAddress = 0x15e694;
    public const string MpapCmdOpenPayloadLogString = "Cmd_Open:%s m_Layout:%d";
    public const int MpapCmdOpenPayloadLogStringVirtualAddress = 0x15e6ac;
    public const int MpapCmdOpenPayloadLogStringLoadAddress = 0x21398;
    public const string MpapCmdOpenMirrorModeMarkerString = "?mirrorMode=";
    public const int MpapCmdOpenMirrorModeMarkerVirtualAddress = 0x15e6d4;
    public const int MpapCmdOpenMirrorModeMarkerFindAddress = 0x213c8;
    public const int MpapCmdOpenMirrorModeMarkerLength = 12;
    public const string MpapCmdOpenWfdUrlMarkerString = "wfd://";
    public const int MpapCmdOpenWfdUrlMarkerVirtualAddress = 0x15e734;
    public const int MpapCmdOpenWfdUrlMarkerFindAddress = 0x2155c;
    public const string MpapCmdOpenDefaultWfdPortSuffix = ":7236";
    public const int MpapCmdOpenDefaultWfdPortSuffixVirtualAddress = 0x15e754;
    public const int MpapCmdOpenSynthesizeWfdUrlBranchAddress = 0x21cac;
    public const int MpapCmdOpenAppendWfdPrefixAddress = 0x21cf4;
    public const int MpapCmdOpenAppendDefaultPortAddress = 0x21d2c;
    public const int MpapOpenMirrorClientFunctionAddress = 0x1f900;
    public const int MpapOpenMirrorClientUrlLogAddress = 0x1f998;
    public const int MpapCmdOpenDirectOpenMirrorClientCallAddress = 0x21570;
    public const int MpapCmdOpenSynthesizedOpenMirrorClientCallAddress = 0x21d8c;
    public const string MpapOpenMirrorClientPortSeparatorString = ":";
    public const int MpapOpenMirrorClientPortSeparatorVirtualAddress = 0x15e5f8;
    public const int MpapOpenMirrorClientFindLastPortSeparatorAddress = 0x1fb50;
    public const int MpapOpenMirrorClientDefaultSourcePort = 7236;
    public const int MpapOpenMirrorClientDefaultSourcePortImmediate = 0x1c44;
    public const int MpapOpenMirrorClientDefaultSourcePortMoveAddress = 0x1fb58;
    public const int MpapOpenMirrorClientPortSubstringEraseAddress = 0x1fc4c;
    public const int MpapOpenMirrorClientPortStrtolAddress = 0x1fc64;
    public const int MpapWfdClientConstructorAddress = 0x6a588;
    public const int MpapWfdClientConstructorSkipWfdPrefixAddress = 0x6a668;
    public const int MpapWfdClientConstructorStrrchrAddress = 0x6a678;
    public const int MpapWfdClientConstructorHostCopyAddress = 0x6a6a4;
    public const int MpapWfdClientConstructorPortStrtolAddress = 0x6a6b4;
    public const int MpapWfdClientConstructorPortStoreAddress = 0x6a6bc;
    public const int MpapWfdClientConstructorDefaultPortMoveAddress = 0x6a6e4;
    public const int MpapWfdClientConstructorDefaultPortStoreAddress = 0x6a6ec;
    public const int MpapWfdClientHostOffset = 0x04;
    public const int MpapWfdClientPortOffset = 0x24;
    public const int MpapWfdClientContextOffset = 0x80;
    public const string MpapSourceHostKeyString = "sourceHost";
    public const int MpapSourceHostKeyVirtualAddress = 0x167998;
    public const string MpapSourcePortKeyString = "sourcePort";
    public const int MpapSourcePortKeyVirtualAddress = 0x1679a4;
    public const int MpapSourceHostSessionEmitAddress = 0x83548;
    public const int MpapSourcePortSessionEmitAddress = 0x83560;
    public const int MpapSourceHostObjectEmitAddress = 0x83998;
    public const int MpapSourcePortObjectEmitAddress = 0x839b0;
    public const int MpapSourceHostSessionParseAddress = 0x8aca0;
    public const int MpapSourcePortSessionParseAddress = 0x8acc0;
    public const string MpapRtspSchemeString = "rtsp://";
    public const int MpapRtspSchemeVirtualAddress = 0x16731c;
    public const string MpapRtspStreamUrlTemplateString = "rtsp://%s/wfd1.0/streamid=0";
    public const int MpapRtspStreamUrlTemplateVirtualAddress = 0x1686fc;
    public const int MpapRtspStreamUrlSnprintfAddress = 0x88058;
    public const string MpapWfdPresentationUrlString = "wfd_presentation_URL: rtsp://%s/wfd1.0/streamid=0 none";
    public const int MpapWfdPresentationUrlVirtualAddress = 0x16a544;
    public const int MpapWfdPresentationUrlBuildAddress = 0x927e8;

    public static MiPlayLx06MpasReceiverSnapshot CreateCurrentSnapshot() =>
        new(
            MiplayInitScriptStartsMpas: true,
            MiplayInitScriptUsesProcdRespawn: true,
            MpasBinaryObserved: true,
            MpapBinaryObserved: true,
            MpasMdnsServiceTypeObserved: true,
            MpasMdnsRegisterPortImmediateObserved: true,
            MpasSecondaryTcp8899ServiceInitObserved: true,
            MpasDynamicDependencyListObserved: true,
            ReceiverDependencySafetyLayerNamesAbsent: true,
            MpapAuthGateStringObservedWithoutSafetyLayerNames: true,
            MpasModernSafetyOpcodeHandlersAbsent: true,
            ReceiverDependency1400HitsClassifiedAsBufferLengths: true,
            ReceiverModernSafetyHandlerRequires19413OrDynamicComponent: true,
            MpasCommandDispatcherObserved: true,
            MpasCommandHeaderOffsetsObserved: true,
            MpasAuthGateDealPacketObserved: true,
            MpasAuthCommandStringObservedAtGate: true,
            MpasAuthCommandIdObserved: true,
            MpasGetDeviceInfoRequestObserved: true,
            MpasGetDeviceInfoAckObserved: true,
            MpasGetDeviceInfoPreservesSequenceObserved: true,
            MpasGetDeviceInfoAsyncPreparePathObserved: true,
            MpasGetDeviceInfoDoesNotImmediatelyRejectUncachedInfo: true,
            MpasGetDeviceInfoAsyncCompletionCallbackObserved: true,
            MpasGetDeviceInfoAsyncCompletionSetsCachedFlag: true,
            MpasGetDeviceInfoAsyncCompletionSendsAck: true,
            MpasGetDeviceInfoAsyncCompletionUsesSavedSequence: true,
            MpasCtrlClientDealPacketGateObserved: true,
            MpasCtrlClientRequiresServerContext: true,
            MpasCtrlClientEnabledFlagInitialized: true,
            MpasCtrlClientEnabledFlagDefaultsTrue: true,
            MpasCtrlClientEnabledFlagDisableOnlyObservedOnRemoval: true,
            MpasCtrlClientRoutesToDoMpasCommand: true,
            MpasServerAppAddClientConstructsCtrlClient: true,
            MpasDoMpasCommandServiceNamePrecheckObserved: true,
            MpasDoMpasCommandServiceMismatchDoesNotBlockGetDeviceInfo: true,
            MpasServerAppAddClientBindsServerContext: true,
            MpasServerAppAddClientStartsAuthCountdown: true,
            MpasServerAppAddClientSendsCmdAuthThroughCtrlProtocol: true,
            MpasServerAppAuthAckDispatcherObserved: true,
            MpasServerAppAuthAckVerifiesChallengeResponse: true,
            MpasServerAppAuthAckSuccessSetsClientAuthFlag: true,
            MpasServerAppAuthAckFailureClosesHandle: true,
            MpasServerAppAuthAckSuccessCanEmitSyncPhoneStateNotify: true,
            MpasLiveAuthAckAcceptedBySyncPhoneStateNotifyObserved: true,
            MpasCtrlClientContextSetterObserved: true,
            MpasCtrlProtocolSendCommandHelperObserved: true,
            MpasUnhandledCommandQueuesWaitCmd: true,
            MpasCtrlProtocolFrameParserObserved: true,
            MpasCtrlProtocolHeaderBeforeCallbackObserved: true,
            MpasCtrlProtocolPayloadLengthObserved: true,
            MpasCtrlProtocolCallbackObserved: true,
            MpasCtrlProtocolVirtualDealPacketDispatchObserved: true,
            MpasCtrlProtocolBaseCallbackAdapterObserved: true,
            MpasCtrlClientEmbedsCtrlProtocol: true,
            MpasCtrlPipeEmbedsCtrlProtocol: true,
            MpasMiplayServiceCheckParserDispatchesDirectlyToDealPacket: true,
            MpasCtrlClientParserDispatchesDirectlyToDealPacket: true,
            MpasCtrlPipeParserDispatchesThroughOwnerContext: true,
            MpasCtrlProtocolCallbackInitiallyCleared: true,
            MpasCtrlProtocolBaseAdapterRequiresInstalledCallback: true,
            MpasSlaveDeviceDealPacketObserved: true,
            MpasSlaveDeviceAuthResponderObserved: true,
            MpasSlaveDeviceReceiveTableDefaultsGetDeviceInfo: true,
            MpasSlaveDeviceConstructedFromSeparatePath: true,
            MpasMiplayServiceCheckAuthSuccessCallbackObserved: true,
            MpasMiplayServiceCheckConstructorClearsAuthCompletionCallback: true,
            MpasMiplayServiceCheckConnectEvObserved: true,
            MpasMiplayServiceCheckConnectEvInstallsAuthSocket: true,
            MpasMiplayServiceCheckConnectEvInstallsSocketDataCallback: true,
            MpasMiplayServiceCheckConnectEvInstallsSocketStateCallback: true,
            MpasMiplayServiceCheckConnectOkStartsSocket: true,
            MpasMiplayServiceCheckConnectFailureSignalsFalse: true,
            MpasMiplayServiceCheckConnectEvDoesNotInstallResultCallback: true,
            MpasAuthSuccessDoesNotDirectlyEnterServerDispatcher: true,
            MpasOpenCommandObserved: true,
            MpasOpenStripsMirrorModeQueryBeforeWfdSearchObserved: true,
            MpasSenderInfoPreparedCmdOpenPathObserved: true,
            MpasAddMirrorAckCanRearmCmdOpenObserved: true,
            MpasSetPlaySourceServerCommandObserved: true,
            MpasSetPlaySourceAckBeforePayloadParseObserved: true,
            MpasSetPlaySourceEmptyPayloadAckOnlySafeObserved: true,
            MpasSetPlaySourceInternalPipeCommandObserved: true,
            MpasAddMirrorRequestCommandObserved: true,
            MpasExternalAddMirrorUnhandledByServerDispatcherObserved: true,
            MpasAddMirrorAckCommandIdObserved: true,
            MpasAddMirrorPayloadIdentityFragmentsObserved: true,
            MpasLocalAddMirrorPayloadFullyObserved: true,
            MpasLocalAddMirrorUsesLocalIpEndpoint: true,
            MpasLocalAddMirrorUsesDefault7236: true,
            MpasLocalAddMirrorIsLocalTrueObserved: true,
            MpasLocalCmdOpenBuiltFromLocalAddMirrorEndpointObserved: true,
            Candidate0058HandledByMpasServerDispatcher: false,
            MpapQuickAudioSinkObserved: true,
            MpapOpenMirrorClientObserved: true,
            MpapCmdOpenBridgeObserved: true,
            MpapCmdOpenSynthesizesWfdUrlObserved: true,
            MpapOpenMirrorClientParsesUrlPortObserved: true,
            MpapWfdClientConstructorParsesHostPortObserved: true,
            MpapSourceHostPortSessionKeysObserved: true,
            MpapRtspPresentationUrlTemplateObserved: true,
            MpapAudioDumpPathObserved: true);

    public static MiPlayIdmStateDecision EvaluateMpasServiceStartup(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MiplayInitScriptStartsMpas)
        {
            return new MiPlayIdmStateDecision(false, "The LX06 miplay init script does not prove /usr/bin/mpas startup.");
        }

        if (!snapshot.MiplayInitScriptUsesProcdRespawn)
        {
            return new MiPlayIdmStateDecision(false, "The mpas service respawn boundary is not proven.");
        }

        if (!snapshot.MpasBinaryObserved || !snapshot.MpapBinaryObserved)
        {
            return new MiPlayIdmStateDecision(false, "Both mpas control service and mpap audio process evidence are required.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "LX06 1.88.51 starts /usr/bin/mpas as a procd-respawned MiPlay receiver service and ships the paired mpap audio process.");
    }

    public static MiPlayIdmStateDecision EvaluateMdnsRegistration(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasMdnsServiceTypeObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas mDNS service type string is missing.");
        }

        if (!snapshot.MpasMdnsRegisterPortImmediateObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas mDNS registration call does not prove a port immediate.");
        }

        if (!snapshot.MpasSecondaryTcp8899ServiceInitObserved)
        {
            return new MiPlayIdmStateDecision(false, "The second mpas TCP 8899 service-initialization path is not proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpas registers _miplay_audio._tcp.local. with immediate 0x22c3 and has a second 0x22c3 service-init path, proving port 8899 statically without relying on ASCII digit-table hits.");
    }

    public static MiPlayIdmStateDecision EvaluateReceiverSafetyLayerSymbolBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasDynamicDependencyListObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas dynamic dependency boundary is not proven.");
        }

        if (!snapshot.ReceiverDependencySafetyLayerNamesAbsent)
        {
            return new MiPlayIdmStateDecision(false, "The receiver dependency string scan still has unresolved SafetyData/SafetyAuth hits.");
        }

        if (!snapshot.MpapAuthGateStringObservedWithoutSafetyLayerNames)
        {
            return new MiPlayIdmStateDecision(false, "The mpap auth-gate evidence has not been separated from SafetyData symbol evidence.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpas links libidmsdk.so and libiotdcm_miplay.so, but mpas/mpap and the checked receiver dependencies do not expose SafetyData/SafetyAuth/DealSafety/CmdSource strings; this is an absence-of-symbols boundary, so post-auth CBC/session routing still needs a 1.94.13 receiver binary or read-only process/file map.");
    }

    public static MiPlayIdmStateDecision EvaluateReceiverModernSafetyCommandBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasModernSafetyOpcodeHandlersAbsent)
        {
            return new MiPlayIdmStateDecision(false, "The aligned modern SafetyInfo/SafetyAuth opcode scan has unresolved mpas/mpap handler hits.");
        }

        if (!snapshot.ReceiverDependency1400HitsClassifiedAsBufferLengths)
        {
            return new MiPlayIdmStateDecision(false, "The receiver dependency 0x1400 immediate hits have not been separated from MiPlay command opcodes.");
        }

        if (!snapshot.ReceiverModernSafetyHandlerRequires19413OrDynamicComponent)
        {
            return new MiPlayIdmStateDecision(false, "The current LX06 modern SafetyAuth handler boundary has not been tied to 1.94.13 or a dynamic component gap.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "LX06 1.88.51 mpas/mpap expose no aligned 0x1400/0x1401/0x1402/0x1403 command-handler immediates, and the checked receiver dependencies have no aligned 0x1401/0x1402/0x1403 Safety acknowledgement/challenge handlers; their aligned 0x1400 hits are 5120-byte buffer/log constants. Therefore the current live 0x1400->0x1401 and 0x1402->0x1403 SafetyAuth behavior needs a matching 1.94.13 receiver binary, OTA delta, or dynamic component/process map for exact modern compatibility, but that gap does not block a bounded 1.88.51 legacy/basic-function reconstruction path.");
    }

    public static MiPlayIdmStateDecision EvaluateLegacyBasicFunctionBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasMdnsRegisterPortImmediateObserved || !snapshot.MpasSecondaryTcp8899ServiceInitObserved)
        {
            return new MiPlayIdmStateDecision(false, "The 1.88.51 receiver service has not been tied to TCP 8899.");
        }

        if (!snapshot.MpasAuthCommandIdObserved || !snapshot.MpasServerAppAuthAckDispatcherObserved)
        {
            return new MiPlayIdmStateDecision(false, "The legacy 0x0028/0x0029 auth path is not complete.");
        }

        if (!snapshot.MpasGetDeviceInfoRequestObserved || !snapshot.MpasGetDeviceInfoAckObserved)
        {
            return new MiPlayIdmStateDecision(false, "The 0x001e/0x001f device-info pair is not complete.");
        }

        if (!snapshot.MpasOpenCommandObserved)
        {
            return new MiPlayIdmStateDecision(false, "The 1.88.51 Cmd_Open dispatcher evidence is incomplete.");
        }

        if (!snapshot.MpapQuickAudioSinkObserved || !snapshot.MpapOpenMirrorClientObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap audio receiver bridge evidence is incomplete.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "LX06 1.88.51 is sufficient evidence for a bounded legacy/basic-function reconstruction path: mpas advertises TCP 8899, has legacy 0x0028/0x0029 auth, maps 0x001e->0x001f device-info, maps Cmd_Open to 0x0000, and pairs with mpap MiPlayQuick_AudioSink/OpenMirrorClient. The missing modern 0x1400..0x1403 owner only blocks exact current SafetyAuth compatibility, not offline implementation of old-version basic functionality.");
    }

    public static MiPlayIdmStateDecision EvaluateAuthGateBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasAuthGateDealPacketObserved || !snapshot.MpasAuthCommandStringObservedAtGate)
        {
            return new MiPlayIdmStateDecision(false, "Cmd_Auth is not yet tied to the MiplayServiceCheck::DealPacket authentication gate.");
        }

        if (!snapshot.MpasAuthCommandIdObserved)
        {
            return new MiPlayIdmStateDecision(false, "The MiplayServiceCheck Cmd_Auth numeric command id is not proven.");
        }

        if (!snapshot.MpasCommandDispatcherObserved)
        {
            return new MiPlayIdmStateDecision(false, "The post-auth ServerApp command dispatcher boundary is not proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "Cmd_Auth is 0x0028 in MiplayServiceCheck::DealPacket, matching the observed 0x0029 reply, and it is handled before ServerApp::doMpasCommand.");
    }

    public static MiPlayIdmStateDecision EvaluateAuthSuccessCallbackBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasAuthGateDealPacketObserved || !snapshot.MpasAuthCommandIdObserved)
        {
            return new MiPlayIdmStateDecision(false, "The MiplayServiceCheck Cmd_Auth gate is not proven.");
        }

        if (!snapshot.MpasMiplayServiceCheckAuthSuccessCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "The MiplayServiceCheck auth-success completion callback is not proven.");
        }

        if (!snapshot.MpasMiplayServiceCheckConstructorClearsAuthCompletionCallback)
        {
            return new MiPlayIdmStateDecision(false, "The MiplayServiceCheck constructor callback-clear boundary is not proven.");
        }

        if (!snapshot.MpasAuthSuccessDoesNotDirectlyEnterServerDispatcher)
        {
            return new MiPlayIdmStateDecision(false, "The auth-success path has not been separated from ServerApp::doMpasCommand.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "MiplayServiceCheck construction at 0x3d268 calls CtrlProtocol construction at 0x3d284 and clears auth completion manager +0x8c at 0x3d3c0; after Cmd_Auth 0x0028 it calls its auth socket object at +0x78, then only invokes the externally installed completion callback at +0x8c/+0x90, not ServerApp::doMpasCommand.");
    }

    public static MiPlayIdmStateDecision EvaluateMiplayServiceCheckConnectEvBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasMiplayServiceCheckConnectEvObserved)
        {
            return new MiPlayIdmStateDecision(false, "MiplayServiceCheck::connectEv has not been located.");
        }

        if (!snapshot.MpasMiplayServiceCheckConnectEvInstallsAuthSocket)
        {
            return new MiPlayIdmStateDecision(false, "connectEv has not been tied to the auth socket +0x78/+0x7c installation.");
        }

        if (!snapshot.MpasMiplayServiceCheckConnectEvInstallsSocketDataCallback ||
            !snapshot.MpasMiplayServiceCheckConnectEvInstallsSocketStateCallback)
        {
            return new MiPlayIdmStateDecision(false, "connectEv has not been tied to both socket data and socket state callback registration.");
        }

        if (!snapshot.MpasMiplayServiceCheckConnectOkStartsSocket)
        {
            return new MiPlayIdmStateDecision(false, "The CONNECT ok state path has not been tied to starting the auth socket read loop.");
        }

        if (!snapshot.MpasMiplayServiceCheckConnectFailureSignalsFalse)
        {
            return new MiPlayIdmStateDecision(false, "The connect failure/error paths have not been tied to false result callbacks.");
        }

        if (!snapshot.MpasMiplayServiceCheckConnectEvDoesNotInstallResultCallback)
        {
            return new MiPlayIdmStateDecision(false, "connectEv is still suspected to install the result callback, so the listener boundary is not isolated.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "MiplayServiceCheck::connectEv at 0x3d4e0 allocates a 0xb4-byte auth socket, installs it at +0x78/+0x7c, registers socket data/state callbacks at 0x3d6c8/0x3d710, and starts the socket on CONNECT ok; false result callbacks are only on connect failure/error, while true completion still waits for Cmd_Auth, and connectEv does not install the +0x8c/+0x90 result listener.");
    }
    public static MiPlayIdmStateDecision EvaluateGetDeviceInfoCommandAlignment(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCommandDispatcherObserved || !snapshot.MpasCommandHeaderOffsetsObserved)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::doMpasCommand and _tagCtrlCmdHeader command/sequence offsets are not proven.");
        }

        if (!snapshot.MpasGetDeviceInfoRequestObserved || !snapshot.MpasGetDeviceInfoAckObserved)
        {
            return new MiPlayIdmStateDecision(false, "The 0x001e request to 0x001f response mapping is incomplete.");
        }

        if (!snapshot.MpasGetDeviceInfoPreservesSequenceObserved)
        {
            return new MiPlayIdmStateDecision(false, "The getDeviceInfo response has not been tied to the request sequence field.");
        }

        if (!snapshot.MpasGetDeviceInfoAsyncPreparePathObserved ||
            !snapshot.MpasGetDeviceInfoDoesNotImmediatelyRejectUncachedInfo)
        {
            return new MiPlayIdmStateDecision(false, "The uncached getDeviceInfo asynchronous preparation path is not proven.");
        }

        if (!snapshot.MpasGetDeviceInfoAsyncCompletionCallbackObserved ||
            !snapshot.MpasGetDeviceInfoAsyncCompletionSetsCachedFlag ||
            !snapshot.MpasGetDeviceInfoAsyncCompletionSendsAck ||
            !snapshot.MpasGetDeviceInfoAsyncCompletionUsesSavedSequence)
        {
            return new MiPlayIdmStateDecision(false, "The uncached getDeviceInfo async completion has not been tied back to a 0x001f acknowledgement.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpas accepts Cmd_GetDeviceInfo as 0x001e, emits 0x001f via helper 0x368bc when cached info is ready, and the 0x69ad8 async path completes at 0x65320 by setting r0+0x2c0 and sending 0x001f with the saved request sequence.");
    }

    public static MiPlayIdmStateDecision EvaluatePostAuthCtrlClientEntryBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCtrlClientDealPacketGateObserved ||
            !snapshot.MpasCtrlClientRequiresServerContext)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient::DealPacket entry gates are not proven.");
        }

        if (!snapshot.MpasCtrlClientEnabledFlagInitialized ||
            !snapshot.MpasCtrlClientEnabledFlagDefaultsTrue)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient enabled-flag initialization is not proven.");
        }

        if (!snapshot.MpasCtrlClientEnabledFlagDisableOnlyObservedOnRemoval)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient enabled-flag disable boundary is not isolated to removal cleanup.");
        }

        if (!snapshot.MpasCtrlClientRoutesToDoMpasCommand)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient::DealPacket has not been tied to ServerApp::doMpasCommand.");
        }

        if (!snapshot.MpasUnhandledCommandQueuesWaitCmd)
        {
            return new MiPlayIdmStateDecision(false, "The doMpasCommand false-return fallback has not been tied to waitCmd queuing.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "CtrlClient::DealPacket requires a non-null context at +0xf4 and enabled flag +0x161; the constructor defaults +0x161 true at 0x332f8 and the located false write at 0x59a24 is client-removal cleanup, then DealPacket reaches ServerApp::doMpasCommand through 0x6dae0; a false doMpasCommand result can queue waitCmd instead of immediately closing the TCP connection.");
    }

    public static MiPlayIdmStateDecision EvaluateDoMpasCommandPreSwitchBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasDoMpasCommandServiceNamePrecheckObserved)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::doMpasCommand service/name precheck has not been located.");
        }

        if (!snapshot.MpasDoMpasCommandServiceMismatchDoesNotBlockGetDeviceInfo ||
            !snapshot.MpasGetDeviceInfoRequestObserved)
        {
            return new MiPlayIdmStateDecision(false, "The pre-switch service/name branch may still block Cmd_GetDeviceInfo.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "ServerApp::doMpasCommand performs a service/name precheck around 0x6575c and memcmp around 0x65b44; a mismatch only short-circuits the 0x0004/0x0006 early-command case at 0x657bc, then other commands enter the main switch at 0x65810, so this precheck does not block 0x001e Cmd_GetDeviceInfo at 0x6825c.");
    }

    public static MiPlayIdmStateDecision EvaluateCtrlClientContextBindingFromServerAppAddClient(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCtrlClientRequiresServerContext)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient::DealPacket has not been proven to require the server context pointer.");
        }

        if (!snapshot.MpasServerAppAddClientConstructsCtrlClient)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::addClient has not been tied to CtrlClient allocation and construction.");
        }

        if (!snapshot.MpasServerAppAddClientBindsServerContext || !snapshot.MpasCtrlClientContextSetterObserved)
        {
            return new MiPlayIdmStateDecision(false, "The ServerApp::addClient path has not been tied to the CtrlClient +0xf4 context setter.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "ServerApp::addClient at 0x5de08 allocates a 368-byte CtrlClient at 0x5de64, calls the CtrlClient constructor at 0x5de74, then calls tiny setter 0x329bc at 0x5df34 to bind ServerApp into CtrlClient+0xf4; this proves the accept-path context binding but does not prove that post-auth SafetyData bytes are routed into this same CtrlClient parser.");
    }

    public static MiPlayIdmStateDecision EvaluateServerAppAddClientAuthBootstrap(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasServerAppAddClientConstructsCtrlClient ||
            !snapshot.MpasServerAppAddClientBindsServerContext)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::addClient has not been tied to a constructed, context-bound CtrlClient.");
        }

        if (!snapshot.MpasServerAppAddClientStartsAuthCountdown)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::addClient has not been tied to CtrlClient::startAuthCountdown.");
        }

        if (!snapshot.MpasCtrlProtocolSendCommandHelperObserved ||
            !snapshot.MpasServerAppAddClientSendsCmdAuthThroughCtrlProtocol)
        {
            return new MiPlayIdmStateDecision(false, "ServerApp::addClient has not been tied to sending Cmd_Auth through CtrlProtocol.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "After accepting a client, ServerApp::addClient calls CtrlClient vtable +0x58 at 0x5df28, binds ServerApp into CtrlClient+0xf4 at 0x5df34, starts CtrlClient::startAuthCountdown via vtable +0x34 at 0x5df44, then calls CtrlProtocol send helper 0x367bc at 0x5e020 with command 0x0028 and sequence 0; this is an auth/bootstrap send path, not proof that later SafetyData plaintext is delivered to the context-bound CtrlClient receive parser.");
    }

    public static MiPlayIdmStateDecision EvaluateServerAppAuthAckAcceptanceBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasServerAppAddClientSendsCmdAuthThroughCtrlProtocol ||
            !snapshot.MpasServerAppAuthAckDispatcherObserved)
        {
            return new MiPlayIdmStateDecision(false, "The ServerApp 0x0028 send path and 0x0029 Cmd_Auth_Ack dispatcher are not both proven.");
        }

        if (!snapshot.MpasServerAppAuthAckVerifiesChallengeResponse)
        {
            return new MiPlayIdmStateDecision(false, "Cmd_Auth_Ack has not been tied to the stored CtrlClient challenge-response verification material.");
        }

        if (!snapshot.MpasServerAppAuthAckSuccessSetsClientAuthFlag ||
            !snapshot.MpasServerAppAuthAckFailureClosesHandle)
        {
            return new MiPlayIdmStateDecision(false, "The Cmd_Auth_Ack true/false side effects are not both proven.");
        }

        if (!snapshot.MpasServerAppAuthAckSuccessCanEmitSyncPhoneStateNotify)
        {
            return new MiPlayIdmStateDecision(false, "The successful Cmd_Auth_Ack path has not been tied to the optional 0x0022 syncPhoneState notify send.");
        }

        if (!snapshot.MpasLiveAuthAckAcceptedBySyncPhoneStateNotifyObserved)
        {
            return new MiPlayIdmStateDecision(false, "No live 0x0029-to-0x0022 observation is recorded to pair with the static success branch.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "ServerApp::doMpasCommand dispatches 0x0029 Cmd_Auth_Ack at 0x65eb0, verifies the stored CtrlClient+0xf8/+0xfc challenge material with a SHA1-sized 20-byte HMAC path, closes the handle on authResult=false at 0x6a5c4, and on authResult=true writes CtrlClient+0x160=1 at 0x663f4; the same success branch can send 0x0022 syncPhoneState via 0x59ae4, so the live same-sequence 0x0029 followed by 0x0022 mode=2 proves legacy auth ACK acceptance and moves the missing 0x001f problem downstream of legacy auth.");
    }
    public static MiPlayIdmStateDecision EvaluateCtrlProtocolFrameParserHandoff(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCtrlProtocolFrameParserObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas CtrlProtocol frame parser has not been statically located.");
        }

        if (!snapshot.MpasCtrlProtocolHeaderBeforeCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas CtrlProtocol clear command header parse before callback is not proven.");
        }

        if (!snapshot.MpasCtrlProtocolPayloadLengthObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas CtrlProtocol payload-length parse is not proven.");
        }

        if (!snapshot.MpasCtrlProtocolCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas CtrlProtocol callback handoff is not proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpas CtrlProtocol parser at 0x36a68 waits for the 9-byte '$' frame header and normalizes command/sequence/payload length from wire offsets 1/3/5; it then compares vtable +0x8 with base adapter 0x33b90, using the +0x34/+0x38 callback only for the base adapter path and otherwise virtual-dispatching directly to the subclass or secondary DealPacket target at 0x36d10; the CtrlClient secondary path reaches CtrlClient::DealPacket and can then reach ServerApp::doMpasCommand.");
    }

    public static MiPlayIdmStateDecision EvaluateCtrlProtocolEmbeddedInSessionObjects(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCtrlProtocolFrameParserObserved)
        {
            return new MiPlayIdmStateDecision(false, "The CtrlProtocol parser entry is not proven.");
        }

        if (!snapshot.MpasCtrlClientEmbedsCtrlProtocol)
        {
            return new MiPlayIdmStateDecision(false, "CtrlClient has not been tied to an embedded CtrlProtocol parser subobject.");
        }

        if (!snapshot.MpasCtrlPipeEmbedsCtrlProtocol)
        {
            return new MiPlayIdmStateDecision(false, "CtrlPipe has not been tied to an embedded CtrlProtocol parser subobject.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "CtrlClient reaches CtrlProtocol through a +0xb4 parser thunk at 0x32464 and then secondary thunk 0x33754 subtracts 0xb4 into CtrlClient::DealPacket 0x3344c; CtrlPipe reaches the same parser through a +0x98 thunk at 0x33838 and secondary thunk 0x33908 returns to CtrlPipe::DealPacket 0x33888. Post-auth bytes must therefore land in the correct session object's embedded parser, not merely have a generic callback installed.");
    }

    public static MiPlayIdmStateDecision EvaluateCtrlProtocolCallbackInstallationBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasCtrlProtocolFrameParserObserved || !snapshot.MpasCtrlProtocolCallbackObserved)
        {
            return new MiPlayIdmStateDecision(false, "The CtrlProtocol parser callback handoff is not proven.");
        }

        if (!snapshot.MpasCtrlProtocolCallbackInitiallyCleared)
        {
            return new MiPlayIdmStateDecision(false, "The CtrlProtocol constructor has not been shown to clear the callback slot.");
        }

        if (!snapshot.MpasCtrlProtocolBaseAdapterRequiresInstalledCallback)
        {
            return new MiPlayIdmStateDecision(false, "The CtrlProtocol parser callback presence check is not proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "CtrlProtocol construction at 0x33cb8 clears the base callback manager at +0x34, and base adapter 0x33b90 requires +0x34/+0x38; however MiplayServiceCheck, CtrlClient, and CtrlPipe install subclass or secondary parser vtables that dispatch directly to DealPacket targets 0x3ce3c, 0x33754, and 0x33908. The missing post-auth evidence is now SafetyData/session routing into the context-bound CtrlClient parser, not a generic CtrlProtocol callback installer.");
    }

    public static MiPlayIdmStateDecision EvaluateSlaveDeviceReceiveDispatcherBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasSlaveDeviceDealPacketObserved)
        {
            return new MiPlayIdmStateDecision(false, "SlaveDevice::DealPacket has not been located.");
        }

        if (!snapshot.MpasSlaveDeviceAuthResponderObserved)
        {
            return new MiPlayIdmStateDecision(false, "SlaveDevice has not been tied to the 0x0028 -> 0x0029 auth response path.");
        }

        if (!snapshot.MpasSlaveDeviceReceiveTableDefaultsGetDeviceInfo)
        {
            return new MiPlayIdmStateDecision(false, "SlaveDevice receive dispatch may still handle 0x001e/0x001f.");
        }

        if (!snapshot.MpasSlaveDeviceConstructedFromSeparatePath)
        {
            return new MiPlayIdmStateDecision(false, "SlaveDevice construction has not been separated from ServerApp::addClient/CtrlClient.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "SlaveDevice::DealPacket at 0x7e694 has a 0x001a..0x0037 receive jump table: 0x0028 reaches 0x7e79c and sends 0x0029 at 0x7ea98, but 0x001e/0x001f both land on default return 0x7ed64. Its constructor is reached from a separate 0x5f700 path, so accepting Cmd_Auth is not proof that post-auth getDeviceInfo reaches ServerApp::doMpasCommand.");
    }

    public static MiPlayIdmStateDecision EvaluateMpasOpenPayloadShape(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasOpenCommandObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas Cmd_Open dispatcher evidence is missing.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "LX06 1.88.51 Cmd_Open is command 0x0000, but it is not a bare empty open command: the handler branches only when payload bytes are present, searches for ?mirrorMode= at 0x69c7c, parses the decimal mode with strtol at 0x69cd0, routes mirrorMode 1 to 0x6bf54 and mirrorMode 2 to 0x6c028 through helper 0x702a0, strips the ?mirrorMode query with substr at 0x69da8 and assignment at 0x69db8, then searches the stripped URL for wfd:// at 0x69dcc; when source changes it can emit a 0x0022 seize notification at 0x69fb8. This defines an offline payload target and does not authorize a live open or audio probe.");
    }

    public static MiPlayIdmStateDecision EvaluateMpasOpenPayloadCompatibility(
        MiPlayLx06MpasReceiverSnapshot snapshot,
        string payload)
    {
        if (!snapshot.MpasOpenCommandObserved || !snapshot.MpasOpenStripsMirrorModeQueryBeforeWfdSearchObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas Cmd_Open payload parser evidence is incomplete.");
        }

        var markerIndex = payload.IndexOf(MpasOpenMirrorModeMarkerString, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            return new MiPlayIdmStateDecision(false, "The payload does not place ?mirrorMode= after a source URL.");
        }

        var sourceUrl = payload[..markerIndex];
        if (!sourceUrl.StartsWith(MpasOpenWfdUrlMarkerString, StringComparison.Ordinal))
        {
            return new MiPlayIdmStateDecision(false, "The payload prefix that remains after mpas strips ?mirrorMode is not a wfd:// URL.");
        }

        var modeText = payload[(markerIndex + MpasOpenMirrorModeMarkerLength)..];
        if (!int.TryParse(modeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mirrorMode))
        {
            return new MiPlayIdmStateDecision(false, "The mirrorMode suffix is not the decimal value parsed by strtol.");
        }

        if (mirrorMode is not 1 and not 2)
        {
            return new MiPlayIdmStateDecision(false, "Only mirrorMode 1 and mirrorMode 2 have located static branch targets in this evidence set.");
        }

        return new MiPlayIdmStateDecision(
            true,
            $"The payload is compatible with the located mpas parser: mirrorMode {mirrorMode} is parsed from the suffix, mpas then strips the query at 0x69da8/0x69db8 and searches the remaining {sourceUrl} for wfd:// at 0x69dcc. A no-callback live result with this payload is therefore not explained by URL query ordering.");
    }

    public static MiPlayIdmStateDecision EvaluateMpasOpenPreOpenContextHypothesis(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasSenderInfoPreparedCmdOpenPathObserved)
        {
            return new MiPlayIdmStateDecision(false, "The sender-info-prepared Cmd_Open path has not been localized.");
        }

        if (!snapshot.MpasAddMirrorAckCanRearmCmdOpenObserved)
        {
            return new MiPlayIdmStateDecision(false, "The Cmd_AddMirror_Ack -> Cmd_Open path has not been localized.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpas has pre-open paths that can mediate Cmd_Open before the receiver reaches mpap: sender-info-prepared strings are loaded at 0x70d88/0x70e38, local and pSlave Cmd_Open logs are loaded at 0x71328/0x71158, command 0x0000 is sent through helper 0x367bc at 0x711bc, and Cmd_AddMirror_Ack can re-arm a master Cmd_Open path at 0x72210 after a sequence compare at 0x721e8. After a compatible direct Cmd_Open still closed 8899 without RTSP, the next testable hypothesis is missing source identity/device-info/add-mirror/session context, not a payload query-order bug.");
    }

    public static MiPlayIdmStateDecision EvaluateMpasPreOpenCommandSequenceBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasSetPlaySourceServerCommandObserved ||
            !snapshot.MpasSetPlaySourceAckBeforePayloadParseObserved ||
            !snapshot.MpasSetPlaySourceEmptyPayloadAckOnlySafeObserved ||
            !snapshot.MpasSetPlaySourceInternalPipeCommandObserved)
        {
            return new MiPlayIdmStateDecision(false, "The Cmd_SetPlaySource external ACK-before-parse and internal command split has not been localized.");
        }

        if (!snapshot.MpasAddMirrorRequestCommandObserved ||
            !snapshot.MpasExternalAddMirrorUnhandledByServerDispatcherObserved ||
            !snapshot.MpasAddMirrorAckCommandIdObserved ||
            !snapshot.MpasAddMirrorPayloadIdentityFragmentsObserved ||
            !snapshot.MpasLocalAddMirrorPayloadFullyObserved ||
            !snapshot.MpasLocalAddMirrorUsesLocalIpEndpoint ||
            !snapshot.MpasLocalAddMirrorUsesDefault7236 ||
            !snapshot.MpasLocalAddMirrorIsLocalTrueObserved ||
            !snapshot.MpasLocalCmdOpenBuiltFromLocalAddMirrorEndpointObserved)
        {
            return new MiPlayIdmStateDecision(false, "The Cmd_AddMirror request/ack state machine and local identity payload are not fully localized.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "Pre-open static evidence now explains the direct Cmd_Open and AddMirror-only failures: ServerApp handles external Cmd_SetPlaySource as 0x0040 and immediately acknowledges 0x0041 before the payload presence check at 0x66b70 or JSON parse at 0x66c70, so the previous empty 0x0040 ACK-only validation could test post-auth dispatcher reachability without source-identity mutation. A separate internal pipe helper sends 0x005a. Local AddMirror is emitted as 0x002e after storing the request sequence at +0x32e and setting pending +0x332, but the external ServerApp dispatcher compares 44 then 50/52 and routes command 46 to the unhandled false return at 0x667e8; only a separate matching 0x002f Cmd_AddMirror_Ack path can re-arm the master Cmd_Open path. After the ACK-only live validations closed without 0x0041, this static boundary no longer justifies repeating 0x0040; it points to the missing current 1.94.13 command-session bridge/handler owner. It still does not authorize JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, or audio.");
    }
    public static MiPlayIdmStateDecision EvaluateCandidate0058AsMpasOpen(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpasOpenCommandObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpas Cmd_Open dispatcher evidence is missing.");
        }

        if (snapshot.Candidate0058HandledByMpasServerDispatcher)
        {
            return new MiPlayIdmStateDecision(true, "0x0058 is handled by the mpas ServerApp dispatcher in this evidence set.");
        }

        return new MiPlayIdmStateDecision(
            false,
            "0x0058 is not handled by the LX06 1.88.51 mpas ServerApp::doMpasCommand dispatcher; Cmd_Open maps to 0x0000 here, so 0x0058 must remain gated.");
    }

    public static MiPlayIdmStateDecision EvaluateMpapCmdOpenMirrorClientBridge(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpapOpenMirrorClientObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap OpenMirrorClient evidence is missing.");
        }

        if (!snapshot.MpapCmdOpenBridgeObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap Cmd_Open bridge evidence is missing.");
        }

        if (!snapshot.MpapCmdOpenSynthesizesWfdUrlObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap synthesized WFD URL fallback has not been proven.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpap independently bridges Cmd_Open payloads into OpenMirrorClient 0x1f900: it logs Cmd_Open:%s m_Layout:%d, searches ?mirrorMode= at 0x213c8, searches wfd:// at 0x2155c, calls OpenMirrorClient directly at 0x21570 for WFD URLs, or enters 0x21cac to synthesize wfd:// + payload + :7236 before calling OpenMirrorClient at 0x21d8c. This strengthens the old/basic payload target but does not authorize live open, RTSP, playback, media, or audio-frame probes.");
    }

    public static MiPlayIdmStateDecision EvaluateMpapOpenMirrorClientUrlAndRtspBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpapOpenMirrorClientObserved || !snapshot.MpapOpenMirrorClientParsesUrlPortObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap OpenMirrorClient URL/port parsing evidence is missing.");
        }

        if (!snapshot.MpapWfdClientConstructorParsesHostPortObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap WFD client constructor host/port parser has not been proven.");
        }

        if (!snapshot.MpapSourceHostPortSessionKeysObserved || !snapshot.MpapRtspPresentationUrlTemplateObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap sourceHost/sourcePort or RTSP presentation URL evidence is incomplete.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "OpenMirrorClient parses the last ':' in the WFD URL at 0x1fb50, defaults missing ports to 0x1c44/7236 at 0x1fb58, and parses explicit ports with strtol at 0x1fc64. The WFD client constructor at 0x6a588 skips the wfd:// prefix at 0x6a668, splits host/port with strrchr at 0x6a678, stores host at +0x04 and port at +0x24, and also defaults to 7236 at 0x6a6e4/0x6a6ec. Later WFD session metadata uses sourceHost/sourcePort keys at 0x83548/0x83560 and 0x8aca0/0x8acc0, and RTSP setup uses rtsp://%s/wfd1.0/streamid=0 at 0x88058. This proves the source side must provide a reachable WFD/RTSP endpoint shape, but does not authorize live open, RTSP, playback, media, or audio-frame probes.");
    }

    public static MiPlayIdmStateDecision EvaluateMpapAudioReceiverBoundary(
        MiPlayLx06MpasReceiverSnapshot snapshot)
    {
        if (!snapshot.MpapQuickAudioSinkObserved || !snapshot.MpapOpenMirrorClientObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap quick audio sink or OpenMirrorClient evidence is missing.");
        }

        if (!snapshot.MpapAudioDumpPathObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mpap audio-dump/debug path evidence is missing.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "mpap contains the MiPlayQuick_AudioSink/OpenMirrorClient audio receiver path, but this static proof does not authorize media, RTSP, playback, or audio-frame probes.");
    }
}