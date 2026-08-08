using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLx06MpasReceiverEvidenceTests
{
    [Fact]
    public void ConstantsCaptureLx06ReceiverFirmwareIdentity()
    {
        Assert.Equal("mico_firmware_c0cb3a1a9_1.88.51.bin", MiPlayLx06MpasReceiverEvidence.FirmwareImageFileName);
        Assert.Equal(
            "A245370CA924BFB38AB9DE00CBBAB0E7A9513FD11F2E6A5907FDC1B3A8DE63EC",
            MiPlayLx06MpasReceiverEvidence.FirmwareSha256);
        Assert.Equal("LX06", MiPlayLx06MpasReceiverEvidence.FirmwareHardware);
        Assert.Equal("1.88.51", MiPlayLx06MpasReceiverEvidence.FirmwareRomVersion);
        Assert.Equal("2023-11-21", MiPlayLx06MpasReceiverEvidence.FirmwareBuildDate);
        Assert.Equal("1.94.13", MiPlayLx06MpasReceiverEvidence.CurrentObservedLx06Version);

        Assert.Equal(
            "artifacts/firmware/mico_lx06_1.88.51/rootfs-extracted",
            MiPlayLx06MpasReceiverEvidence.RootFsExtractionPath);
        Assert.Equal("etc/init.d/miplay", MiPlayLx06MpasReceiverEvidence.MiplayInitScript);
        Assert.Equal("procd_set_param command /usr/bin/mpas", MiPlayLx06MpasReceiverEvidence.MiplayInitCommand);
        Assert.Equal("procd_set_param respawn 3600 5 0", MiPlayLx06MpasReceiverEvidence.MiplayInitRespawnCommand);
        Assert.Equal("usr/bin/mpas", MiPlayLx06MpasReceiverEvidence.MpasBinary);
        Assert.Equal(1_385_680, MiPlayLx06MpasReceiverEvidence.MpasBinarySize);
        Assert.Equal(
            "9336BA754E864DEE015CDEE688BC45631570133C8E64EF46EBEDD6800D805C43",
            MiPlayLx06MpasReceiverEvidence.MpasSha256);
        Assert.Equal("usr/bin/mpap", MiPlayLx06MpasReceiverEvidence.MpapBinary);
        Assert.Equal(1_544_328, MiPlayLx06MpasReceiverEvidence.MpapBinarySize);
        Assert.Equal(
            "BE0A07E405F28491871C2AA6397F8802FECBAC4279AEE61D01F05799C4C47481",
            MiPlayLx06MpasReceiverEvidence.MpapSha256);
    }

    [Fact]
    public void MiplayInitStartsMpasAndPairedMpapIsPresent()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MiplayInitScriptStartsMpas);
        Assert.True(snapshot.MiplayInitScriptUsesProcdRespawn);
        Assert.True(snapshot.MpasBinaryObserved);
        Assert.True(snapshot.MpapBinaryObserved);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpasServiceStartup(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("/usr/bin/mpas", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("mpap", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MdnsAndTcpServiceInitializationProveMiplayAudioPort8899()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasMdnsRegisterPortImmediateObserved);
        Assert.True(snapshot.MpasSecondaryTcp8899ServiceInitObserved);
        Assert.Equal("_miplay_audio._tcp.local.", MiPlayLx06MpasReceiverEvidence.MpasMdnsServiceType);
        Assert.Equal(0x12d058, MiPlayLx06MpasReceiverEvidence.MpasMdnsServiceTypeFileOffset);
        Assert.Equal(0x13d058, MiPlayLx06MpasReceiverEvidence.MpasMdnsServiceTypeVirtualAddress);
        Assert.Equal(0x58d18, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterSetupAddress);
        Assert.Equal(0x58d1c, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterPortMoveAddress);
        Assert.Equal(0x58d20, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterServiceStringMoveLowAddress);
        Assert.Equal(0x58d28, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterServiceStringMoveHighAddress);
        Assert.Equal(0x22c3, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterPortImmediate);
        Assert.Equal(8_899, MiPlayLx06MpasReceiverEvidence.MpasMdnsRegisterPort);
        Assert.Equal(0x74a44, MiPlayLx06MpasReceiverEvidence.MpasSecondaryTcp8899PortMoveAddress);
        Assert.Equal(0x40170, MiPlayLx06MpasReceiverEvidence.MpasSecondaryTcp8899InitCallTarget);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMdnsRegistration(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x22c3", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("second", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("8899", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("without relying on ASCII digit-table", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiverDependencyScanDoesNotLocalizeSafetyDataLayerByStrings()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasDynamicDependencyListObserved);
        Assert.True(snapshot.ReceiverDependencySafetyLayerNamesAbsent);
        Assert.True(snapshot.MpapAuthGateStringObservedWithoutSafetyLayerNames);
        Assert.Equal("libidmsdk.so", MiPlayLx06MpasReceiverEvidence.MpasDynamicDependencyLibIdmSdk);
        Assert.Equal("libiotdcm_miplay.so", MiPlayLx06MpasReceiverEvidence.MpasDynamicDependencyLibIotdcmMiplay);
        Assert.Equal("libiotdcm.so", MiPlayLx06MpasReceiverEvidence.MpasDynamicDependencyLibIotdcm);
        Assert.Contains("mpas", MiPlayLx06MpasReceiverEvidence.ReceiverSafetyLayerStringScanScope, StringComparison.Ordinal);
        Assert.Contains("mpap", MiPlayLx06MpasReceiverEvidence.ReceiverSafetyLayerStringScanScope, StringComparison.Ordinal);
        Assert.Contains("SafetyData", MiPlayLx06MpasReceiverEvidence.ReceiverSafetyLayerAbsentStringFamily, StringComparison.Ordinal);
        Assert.Contains("CmdSource", MiPlayLx06MpasReceiverEvidence.ReceiverSafetyLayerAbsentStringFamily, StringComparison.Ordinal);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateReceiverSafetyLayerSymbolBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("libidmsdk.so", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("libiotdcm_miplay.so", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("absence-of-symbols", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("1.94.13", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void ModernSafetyOpcodesAreNotLocalizedIn18851ReceiverSet()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasModernSafetyOpcodeHandlersAbsent);
        Assert.True(snapshot.ReceiverDependency1400HitsClassifiedAsBufferLengths);
        Assert.True(snapshot.ReceiverModernSafetyHandlerRequires19413OrDynamicComponent);
        Assert.Equal("0x1400/0x1401/0x1402/0x1403", MiPlayLx06MpasReceiverEvidence.ReceiverModernSafetyOpcodeFamily);
        Assert.Equal(0, MiPlayLx06MpasReceiverEvidence.MpasAlignedModernSafetyOpcodeImmediateCount);
        Assert.Equal(0, MiPlayLx06MpasReceiverEvidence.MpapAlignedModernSafetyOpcodeImmediateCount);
        Assert.Equal(0, MiPlayLx06MpasReceiverEvidence.CheckedReceiverDependencyAlignedSafetyAckOpcodeImmediateCount);
        Assert.Equal(0x615d4, MiPlayLx06MpasReceiverEvidence.LibIotdcmMiplayAligned1400ImmediateAddress);
        Assert.Equal(0x1400, MiPlayLx06MpasReceiverEvidence.LibIotdcmMiplayAligned1400ImmediateValue);
        Assert.Equal(0x0a9a60, MiPlayLx06MpasReceiverEvidence.LibIdmSdkAligned1400ImmediateAddress);
        Assert.Equal(0x1400, MiPlayLx06MpasReceiverEvidence.LibIdmSdkAligned1400ImmediateValue);
        Assert.Contains("5120-byte", MiPlayLx06MpasReceiverEvidence.ReceiverDependency1400ImmediateMeaning, StringComparison.Ordinal);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateReceiverModernSafetyCommandBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x1400", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x1403", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("5120-byte", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("1.94.13", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("dynamic component", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void Legacy18851EvidenceSupportsBasicFunctionPathWithoutModernSafetyOwner()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Contains("legacy 8899 auth", MiPlayLx06MpasReceiverEvidence.ReceiverBasicFunctionReconstructionScope, StringComparison.Ordinal);
        Assert.Contains("0x001e/0x001f", MiPlayLx06MpasReceiverEvidence.ReceiverBasicFunctionReconstructionScope, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open 0x0000", MiPlayLx06MpasReceiverEvidence.ReceiverBasicFunctionReconstructionScope, StringComparison.Ordinal);
        Assert.Contains("mpap audio receiver bridge", MiPlayLx06MpasReceiverEvidence.ReceiverBasicFunctionReconstructionScope, StringComparison.Ordinal);
        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.CmdOpen);
        Assert.Equal(0x001e, MiPlayLx06MpasReceiverEvidence.CmdGetDeviceInfo);
        Assert.Equal(0x001f, MiPlayLx06MpasReceiverEvidence.CmdGetDeviceInfoAck);
        Assert.True(snapshot.MpasModernSafetyOpcodeHandlersAbsent);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateLegacyBasicFunctionBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("1.88.51", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("TCP 8899", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0028/0x0029", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001e->0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0000", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("mpap", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("only blocks exact current SafetyAuth compatibility", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void AuthCommand0028IsBeforeServerCommandDispatcher()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasAuthCommandIdObserved);
        Assert.Equal("Cmd_Auth", MiPlayLx06MpasReceiverEvidence.MpasAuthCommandString);
        Assert.Equal(0x0028, MiPlayLx06MpasReceiverEvidence.CmdAuth);
        Assert.Equal(0x0029, MiPlayLx06MpasReceiverEvidence.CmdAuthAck);
        Assert.Equal(0x3ce3c, MiPlayLx06MpasReceiverEvidence.MpasAuthHeaderCommandReadAddress);
        Assert.Equal(0x3ce40, MiPlayLx06MpasReceiverEvidence.MpasAuthCommandCompareAddress);
        Assert.Equal(0x3ce74, MiPlayLx06MpasReceiverEvidence.MpasAuthDealPacketLogAddress);
        Assert.Equal("MiplayServiceCheck::DealPacket", MiPlayLx06MpasReceiverEvidence.MpasAuthHandlerBoundary);
        Assert.Equal(0x65730, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandEntryAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateAuthGateBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x0028", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0029", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("before", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("ServerApp::doMpasCommand", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthSuccessOnlySignalsCompletionCallbackBeforePostAuthSessionInstall()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasMiplayServiceCheckAuthSuccessCallbackObserved);
        Assert.True(snapshot.MpasMiplayServiceCheckConstructorClearsAuthCompletionCallback);
        Assert.True(snapshot.MpasAuthSuccessDoesNotDirectlyEnterServerDispatcher);
        Assert.Equal(0x3d268, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConstructorAddress);
        Assert.Equal(0x3d284, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckCtrlProtocolConstructorCallAddress);
        Assert.Equal(0x1390ec, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckVtableVirtualAddress);
        Assert.Equal(0x78, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthSocketRefOffset);
        Assert.Equal(0x3d3b4, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthSocketRefClearAddress);
        Assert.Equal(0x8c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthCompletionCallbackManagerOffset);
        Assert.Equal(0x90, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthCompletionCallbackInvokerOffset);
        Assert.Equal(0x3ce90, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthCompletionCallbackManagerCheckAddress);
        Assert.Equal(0x3d3c0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthCompletionCallbackManagerClearAddress);
        Assert.Equal(0x3cee8, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthCompletionCallbackInvokerAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateAuthSuccessCallbackBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x3d268", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x3d3c0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x78", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x8c/+0x90", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("externally installed", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not ServerApp::doMpasCommand", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MiplayServiceCheckConnectEvInstallsSocketButNotResultCallback()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasMiplayServiceCheckConnectEvObserved);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectEvInstallsAuthSocket);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectEvInstallsSocketDataCallback);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectEvInstallsSocketStateCallback);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectOkStartsSocket);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectFailureSignalsFalse);
        Assert.True(snapshot.MpasMiplayServiceCheckConnectEvDoesNotInstallResultCallback);
        Assert.Equal(0x3d4e0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectEvEntryAddress);
        Assert.Equal(0x3d5d0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectEvSocketAllocationAddress);
        Assert.Equal(0x0b4, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectEvSocketAllocationSize);
        Assert.Equal(0x3d5e0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectEvSocketConstructorCallAddress);
        Assert.Equal(0x3d624, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthSocketRefInstallAddress);
        Assert.Equal(0x3d62c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckAuthSocketControlInstallAddress);
        Assert.Equal(0x3d6c8, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketDataCallbackRegisterCallAddress);
        Assert.Equal(0x3cefc, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketDataCallbackInvokerAddress);
        Assert.Equal(0x3cc40, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketDataCallbackManagerAddress);
        Assert.Equal(0x3d710, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketStateCallbackRegisterCallAddress);
        Assert.Equal(0x3ccb0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketStateCallbackInvokerAddress);
        Assert.Equal(0x3cc78, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckSocketStateCallbackManagerAddress);
        Assert.Equal(0x3d744, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectStartCallAddress);
        Assert.Equal(0x5c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectStartSocketVtableOffset);
        Assert.Equal(0x28, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckActiveSocketOffset);
        Assert.Equal(0x3ccb0, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectStateHandlerAddress);
        Assert.Equal(0x3ce14, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectOkLogAddress);
        Assert.Equal(0x1391e4, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectOkLogStringVirtualAddress);
        Assert.Equal(0x3cd14, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectFailedLogAddress);
        Assert.Equal(0x13920c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectFailedLogStringVirtualAddress);
        Assert.Equal(0x3cd74, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectErrorLogAddress);
        Assert.Equal(0x13923c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectErrorLogStringVirtualAddress);
        Assert.Equal(0x3cde4, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectClosedLogAddress);
        Assert.Equal(0x139264, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectClosedLogStringVirtualAddress);
        Assert.Equal(0x3cd44, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectFailureResultCallbackFalseAddress);
        Assert.Equal(0x3cdb8, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectErrorResultCallbackFalseAddress);
        Assert.Equal(0x3ce30, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckConnectOkSocketStartAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMiplayServiceCheckConnectEvBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x3d4e0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x78/+0x7c", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("CONNECT ok", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("false result callbacks", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Auth", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not install", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x8c/+0x90", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDeviceInfoMapsRequest001eToResponse001fOrAsyncPreparePath()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasGetDeviceInfoAsyncPreparePathObserved);
        Assert.True(snapshot.MpasGetDeviceInfoDoesNotImmediatelyRejectUncachedInfo);
        Assert.True(snapshot.MpasGetDeviceInfoAsyncCompletionCallbackObserved);
        Assert.True(snapshot.MpasGetDeviceInfoAsyncCompletionSetsCachedFlag);
        Assert.True(snapshot.MpasGetDeviceInfoAsyncCompletionSendsAck);
        Assert.True(snapshot.MpasGetDeviceInfoAsyncCompletionUsesSavedSequence);
        Assert.Equal(1, MiPlayLx06MpasReceiverEvidence.MpasCommandHeaderCommandOffset);
        Assert.Equal(3, MiPlayLx06MpasReceiverEvidence.MpasCommandHeaderSequenceOffset);
        Assert.Equal(0x001e, MiPlayLx06MpasReceiverEvidence.CmdGetDeviceInfo);
        Assert.Equal(0x001f, MiPlayLx06MpasReceiverEvidence.CmdGetDeviceInfoAck);
        Assert.Equal("Cmd_GetDeviceInfo", MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoString);
        Assert.Equal(0x6825c, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoDispatchCompareAddress);
        Assert.Equal(0x68290, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoCachedFlagReadAddress);
        Assert.Equal(0x2c0, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoCachedFlagOffset);
        Assert.Equal(0x69ad8, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncPreparePathAddress);
        Assert.Equal(0x65320, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncCallbackAddress);
        Assert.Equal(0x65398, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncCacheFlagSetAddress);
        Assert.Equal(0x6541c, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncSavedSequenceReadAddress);
        Assert.Equal(0x83, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncSavedSequenceOffset);
        Assert.Equal(0x65430, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAsyncAckCommandMoveAddress);
        Assert.Equal(0x68350, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoAckCommandMoveAddress);
        Assert.Equal(0x368bc, MiPlayLx06MpasReceiverEvidence.MpasSendCommandHelperAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateGetDeviceInfoCommandAlignment(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x368bc", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x65320", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("r0+0x2c0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("saved request sequence", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PostAuthCtrlClientEntryRequiresContextAndUnhandledCommandsCanQueue()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasCtrlClientDealPacketGateObserved);
        Assert.True(snapshot.MpasCtrlClientRequiresServerContext);
        Assert.True(snapshot.MpasCtrlClientEnabledFlagInitialized);
        Assert.True(snapshot.MpasCtrlClientEnabledFlagDefaultsTrue);
        Assert.True(snapshot.MpasCtrlClientEnabledFlagDisableOnlyObservedOnRemoval);
        Assert.True(snapshot.MpasCtrlClientRoutesToDoMpasCommand);
        Assert.True(snapshot.MpasUnhandledCommandQueuesWaitCmd);
        Assert.Equal(0x331ec, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientConstructorAddress);
        Assert.Equal(0x332f8, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientEnabledFlagInitAddress);
        Assert.Equal(0x329b4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientEnabledFlagSetterAddress);
        Assert.Equal(0x59a24, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientEnabledFlagDisableOnRemovalCallAddress);
        Assert.Equal(0x3344c, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientDealPacketEntryAddress);
        Assert.Equal(0x0f4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientContextPointerOffset);
        Assert.Equal(0x3344c, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientDealPacketContextReadAddress);
        Assert.Equal(0x161, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientDealPacketEnabledFlagOffset);
        Assert.Equal(0x33458, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientDealPacketEnabledFlagReadAddress);
        Assert.Equal(0x33584, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientBridgeCallAddress);
        Assert.Equal(0x6dae0, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientDoMpasCommandBridgeAddress);
        Assert.Equal(0x6db58, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandCallFromBridgeAddress);
        Assert.Equal(0x6db70, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandFalseReturnCheckAddress);
        Assert.Equal(0x13eb04, MiPlayLx06MpasReceiverEvidence.MpasWaitCommandLogStringVirtualAddress);
        Assert.Equal(0x6dc38, MiPlayLx06MpasReceiverEvidence.MpasWaitCommandObjectAllocationAddress);
        Assert.Equal(0x0c8, MiPlayLx06MpasReceiverEvidence.MpasWaitCommandListOffset);
        Assert.Equal(0x0d0, MiPlayLx06MpasReceiverEvidence.MpasWaitCommandCountOffset);
        Assert.Equal(16, MiPlayLx06MpasReceiverEvidence.MpasWaitCommandMaximumBeforeThrow);
        Assert.Equal(0x13eb28, MiPlayLx06MpasReceiverEvidence.MpasThrowWaitingRequestLogStringVirtualAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluatePostAuthCtrlClientEntryBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("+0xf4", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x161", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("defaults +0x161 true", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x59a24", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x6dae0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("waitCmd", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("instead of immediately closing", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DoMpasCommandPreSwitchServiceCheckDoesNotBlockGetDeviceInfo()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasDoMpasCommandServiceNamePrecheckObserved);
        Assert.True(snapshot.MpasDoMpasCommandServiceMismatchDoesNotBlockGetDeviceInfo);
        Assert.True(snapshot.MpasGetDeviceInfoRequestObserved);
        Assert.Equal(0x65730, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandEntryAddress);
        Assert.Equal(0x6575c, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceNamePrecheckStartAddress);
        Assert.Equal(0x65774, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceNameGetNameCallAddress);
        Assert.Equal(0x65b44, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceNameMemcmpAddress);
        Assert.Equal(0x657bc, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceMismatchEarlyCommandCheckAddress);
        Assert.Equal(0x0004, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceMismatchAllowedCommandLow);
        Assert.Equal(0x0006, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandServiceMismatchAllowedCommandHigh);
        Assert.Equal(0x65810, MiPlayLx06MpasReceiverEvidence.MpasDoMpasCommandMainSwitchEntryAddress);
        Assert.Equal(0x6825c, MiPlayLx06MpasReceiverEvidence.MpasGetDeviceInfoDispatchCompareAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateDoMpasCommandPreSwitchBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("service/name", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x6575c", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0004/0x0006", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not block 0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x6825c", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerAppAddClientBindsCtrlClientServerContextButNotSafetyDataRouting()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasServerAppAddClientConstructsCtrlClient);
        Assert.True(snapshot.MpasServerAppAddClientBindsServerContext);
        Assert.True(snapshot.MpasCtrlClientContextSetterObserved);
        Assert.Equal(0x329bc, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientServerContextSetterAddress);
        Assert.Equal(0x5de08, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientEntryAddress);
        Assert.Equal(368, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAllocatedCtrlClientSize);
        Assert.Equal(0x5de64, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAllocationAddress);
        Assert.Equal(0x5de74, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientCtrlClientConstructorCallAddress);
        Assert.Equal(0x5df34, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientServerContextSetterCallAddress);
        Assert.Equal(0x13d5c4, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientLogStringVirtualAddress);
        Assert.Equal(0x13db5c, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientServerAppStringVirtualAddress);
        Assert.Equal("add client %d", MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientLogString);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateCtrlClientContextBindingFromServerAppAddClient(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x5de08", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("368-byte CtrlClient", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x329bc", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("CtrlClient+0xf4", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafetyData bytes", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("same CtrlClient parser", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerAppAddClientStartsAuthBootstrapButStillNotPostAuthParserProof()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasServerAppAddClientStartsAuthCountdown);
        Assert.True(snapshot.MpasServerAppAddClientSendsCmdAuthThroughCtrlProtocol);
        Assert.True(snapshot.MpasCtrlProtocolSendCommandHelperObserved);
        Assert.Equal(0x137f80, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientVtableVirtualAddress);
        Assert.Equal(0x138034, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientStartAuthCountdownStringVirtualAddress);
        Assert.Equal(0x138090, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientStartAuthCountdownLambdaStringVirtualAddress);
        Assert.Equal(0x34, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientStartAuthCountdownVtableOffset);
        Assert.Equal(0x49728, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientStartAuthCountdownTargetAddress);
        Assert.Equal(0x58, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientAcceptedSocketSetupVtableOffset);
        Assert.Equal(0x499a4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientAcceptedSocketSetupTargetAddress);
        Assert.Equal(0x5df28, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAcceptedSocketSetupCallAddress);
        Assert.Equal(0x5df44, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientStartAuthCountdownCallAddress);
        Assert.Equal(0x5e00c, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthPayloadLengthStoreAddress);
        Assert.Equal(0x5e010, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthPayloadPointerMoveAddress);
        Assert.Equal(0x5e018, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthSequenceMoveAddress);
        Assert.Equal(0x5e01c, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthCommandMoveAddress);
        Assert.Equal(0x5e020, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientSendAuthCommandCallAddress);
        Assert.Equal(0x367bc, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolSendCommandWithPayloadLengthHelperAddress);
        Assert.Equal(0x0028, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthCommandId);
        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.MpasServerAppAddClientAuthSequence);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateServerAppAddClientAuthBootstrap(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x5df28", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("CtrlClient+0xf4", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("startAuthCountdown", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x367bc", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0028", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("auth/bootstrap", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not proof", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafetyData plaintext", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void ServerAppAuthAckSuccessSetsAuthFlagAndExplains0022Notify()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasServerAppAuthAckDispatcherObserved);
        Assert.True(snapshot.MpasServerAppAuthAckVerifiesChallengeResponse);
        Assert.True(snapshot.MpasServerAppAuthAckSuccessSetsClientAuthFlag);
        Assert.True(snapshot.MpasServerAppAuthAckFailureClosesHandle);
        Assert.True(snapshot.MpasServerAppAuthAckSuccessCanEmitSyncPhoneStateNotify);
        Assert.True(snapshot.MpasLiveAuthAckAcceptedBySyncPhoneStateNotifyObserved);
        Assert.Equal(0x0029, MiPlayLx06MpasReceiverEvidence.CmdAuthAck);
        Assert.Equal(0x65ea8, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckDispatchCompareAddress);
        Assert.Equal(0x65eb0, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckBranchAddress);
        Assert.Equal(0x65ec4, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckClientChallengePointerReadAddress);
        Assert.Equal(0x65ec8, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckClientChallengeLengthReadAddress);
        Assert.Equal(0x0f8, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckClientChallengePointerOffset);
        Assert.Equal(0x0fc, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckClientChallengeLengthOffset);
        Assert.Equal(0x36363636, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckIpadXorImmediate);
        Assert.Equal(0x6a6a6a6a, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckOpadTransformXorImmediate);
        Assert.Equal(20, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckDigestLength);
        Assert.Equal(0x13ea20, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckLogStringVirtualAddress);
        Assert.Equal("[%d D %s %s:%d]Cmd_Auth_Ack(%d), authResult[%s]", MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckLogString);
        Assert.Equal(0x1375c4, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFalseStringVirtualAddress);
        Assert.Equal("false", MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFalseString);
        Assert.Equal(0x138134, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckTrueStringVirtualAddress);
        Assert.Equal("true", MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckTrueString);
        Assert.Equal(0x160, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientAuthFlagOffset);
        Assert.Equal(0x332f0, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientAuthFlagInitAddress);
        Assert.Equal(0x663f4, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckSuccessSetAuthFlagAddress);
        Assert.Equal(0x6a5c4, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureBranchAddress);
        Assert.Equal(0x13ea50, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureLogStringVirtualAddress);
        Assert.Equal("[%d D %s %s:%d]Cmd_Auth_Ack close Handle", MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureLogString);
        Assert.Equal(0x14, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureCloseVtableOffset);
        Assert.Equal(0x6a600, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureCloseVtableLoadAddress);
        Assert.Equal(0x6a604, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckFailureCloseCallAddress);
        Assert.Equal(0x663f8, MiPlayLx06MpasReceiverEvidence.MpasServerAppAuthAckSyncPhoneStateCheckCallAddress);
        Assert.Equal(0x5bb5c, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStatePredicateAddress);
        Assert.Equal(0x13d540, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateStringVirtualAddress);
        Assert.Equal("syncPhoneState", MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateString);
        Assert.Equal(0x38b, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateModeByteOffset);
        Assert.Equal(0x212, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateSequenceOffset);
        Assert.Equal(0x664e4, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateSendCommandMoveAddress);
        Assert.Equal(0x59ae4, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateSendHelperAddress);
        Assert.Equal(0x0022, MiPlayLx06MpasReceiverEvidence.MpasServerAppSyncPhoneStateNotifyCommandId);
        Assert.Equal("04 6d 6f 64 65 03 02", MiPlayLx06MpasReceiverEvidence.MpasLiveAcceptedAuthAckNotifyPayloadHex);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateServerAppAuthAckAcceptanceBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x0029", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("CtrlClient+0x160=1", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("authResult=false", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0022 syncPhoneState", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("legacy auth ACK acceptance", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("downstream of legacy auth", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void CtrlProtocolIsEmbeddedInCtrlClientAndCtrlPipeSessionObjects()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasCtrlClientEmbedsCtrlProtocol);
        Assert.True(snapshot.MpasCtrlPipeEmbedsCtrlProtocol);
        Assert.True(snapshot.MpasCtrlClientParserDispatchesDirectlyToDealPacket);
        Assert.True(snapshot.MpasCtrlPipeParserDispatchesThroughOwnerContext);
        Assert.Equal(0x0b4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolSubobjectOffset);
        Assert.Equal(0x32464, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolParserThunkAddress);
        Assert.Equal(0x33294, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolConstructorCallAddress);
        Assert.Equal(0x332c0, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolSecondaryVtableStoreAddress);
        Assert.Equal(0x137ff4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolSecondaryVtableVirtualAddress);
        Assert.Equal(0x33754, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolSecondaryDealPacketThunkAddress);
        Assert.Equal(0x0b4, MiPlayLx06MpasReceiverEvidence.MpasCtrlClientCtrlProtocolSecondaryThunkAdjust);
        Assert.Equal(0x098, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolSubobjectOffset);
        Assert.Equal(0x33838, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolParserThunkAddress);
        Assert.Equal(0x33a44, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolConstructorCallAddress);
        Assert.Equal(0x33a54, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolSecondaryVtableStoreAddress);
        Assert.Equal(0x13827c, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolSecondaryVtableVirtualAddress);
        Assert.Equal(0x33908, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeCtrlProtocolSecondaryDealPacketThunkAddress);
        Assert.Equal(0x33888, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeDealPacketEntryAddress);
        Assert.Equal(0x0d4, MiPlayLx06MpasReceiverEvidence.MpasCtrlPipeOwnerContextOffset);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateCtrlProtocolEmbeddedInSessionObjects(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("+0xb4", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x32464", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x98", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33838", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("embedded parser", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33754", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33908", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not merely", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlProtocolVirtualDispatchUsesDealPacketBeforeBaseCallbackAdapter()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasCtrlProtocolCallbackInitiallyCleared);
        Assert.True(snapshot.MpasCtrlProtocolVirtualDealPacketDispatchObserved);
        Assert.True(snapshot.MpasCtrlProtocolBaseCallbackAdapterObserved);
        Assert.True(snapshot.MpasCtrlProtocolBaseAdapterRequiresInstalledCallback);
        Assert.True(snapshot.MpasMiplayServiceCheckParserDispatchesDirectlyToDealPacket);
        Assert.True(snapshot.MpasCtrlClientParserDispatchesDirectlyToDealPacket);
        Assert.True(snapshot.MpasCtrlPipeParserDispatchesThroughOwnerContext);
        Assert.Equal(0x33cb8, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolConstructorAddress);
        Assert.Equal(0x13835c, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolVtableVirtualAddress);
        Assert.Equal(0x34, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackManagerOffset);
        Assert.Equal(0x38, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackInvokerOffset);
        Assert.Equal(0x38, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackPointerOffset);
        Assert.Equal(0x33cd4, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackClearAddress);
        Assert.Equal(0x36cb4, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackPresenceCheckAddress);
        Assert.Equal(0x36ce0, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackInvokerLoadAddress);
        Assert.Equal(0x36ce8, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolCallbackCallAddress);
        Assert.Equal(0x33b90, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolBaseDealPacketAdapterAddress);
        Assert.Equal(0x08, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolBaseVtableDealPacketSlotOffset);
        Assert.Equal(0x36cac, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolVirtualDealPacketCompareAddress);
        Assert.Equal(0x36d08, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolVirtualDealPacketBranchAddress);
        Assert.Equal(0x36d10, MiPlayLx06MpasReceiverEvidence.MpasCtrlProtocolVirtualDealPacketCallAddress);
        Assert.Equal(0x1390f4, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckDealPacketVtableSlotAddress);
        Assert.Equal(0x3ce3c, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckDealPacketVtableTargetAddress);
        Assert.Equal(0x3cf48, MiPlayLx06MpasReceiverEvidence.MpasMiplayServiceCheckParserDataCallbackCallAddress);
        Assert.Equal(0x48224, MiPlayLx06MpasReceiverEvidence.MpasGenericFunctionAssignmentCallbackManagerStoreAddress);
        Assert.Equal(0x48230, MiPlayLx06MpasReceiverEvidence.MpasGenericFunctionAssignmentCallbackInvokerStoreAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateCtrlProtocolCallbackInstallationBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x33cb8", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x34", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x38", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33b90", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x3ce3c", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33754", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x33908", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not a generic CtrlProtocol callback installer", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SlaveDeviceCanAnswerAuthButDoesNotReceiveGetDeviceInfoAckPath()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasSlaveDeviceDealPacketObserved);
        Assert.True(snapshot.MpasSlaveDeviceAuthResponderObserved);
        Assert.True(snapshot.MpasSlaveDeviceReceiveTableDefaultsGetDeviceInfo);
        Assert.True(snapshot.MpasSlaveDeviceConstructedFromSeparatePath);
        Assert.Equal("SlaveDevice", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceName);
        Assert.Equal(0x13ff70, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceVtableVirtualAddress);
        Assert.Equal(0x7dff4, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceConstructorAddress);
        Assert.Equal(0x5f700, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceConstructorCallAddress);
        Assert.Equal(0x160, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAllocationSize);
        Assert.Equal(0x7e694, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceDealPacketAddress);
        Assert.Equal(0x001a, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceCommandRangeBase);
        Assert.Equal(30, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceCommandRangeCount);
        Assert.Equal(0x7ed64, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceDefaultReturnAddress);
        Assert.Equal(0x7e79c, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthBranchAddress);
        Assert.Equal(0x1405bc, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthLogStringVirtualAddress);
        Assert.Equal("SlaveDevice recv Cmd_Auth", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthLogString);
        Assert.Equal(0x7ea90, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthResponseCommandMoveAddress);
        Assert.Equal(0x7ea98, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthResponseSendCallAddress);
        Assert.Equal(0x0029, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAuthResponseCommandId);
        Assert.Equal(0x7ed64, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoTableTarget);
        Assert.Equal(0x7ed64, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoAckTableTarget);
        Assert.Equal(0x13ff04, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoMethodStringVirtualAddress);
        Assert.Equal("getDeviceInfo", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoMethodString);
        Assert.Equal(0x140368, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoLogStringVirtualAddress);
        Assert.Equal("SlaveDevice getDeviceInfo", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceGetDeviceInfoLogString);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateSlaveDeviceReceiveDispatcherBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x7e694", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0028", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0029", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001e/0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x7ed64", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x5f700", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not proof", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CmdOpenRequiresMirrorModeAndWfdPayloadShapeOffline()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.MpasOpenDispatchCommandId);
        Assert.Equal(0x69c28, MiPlayLx06MpasReceiverEvidence.MpasOpenPayloadBranchAddress);
        Assert.Equal("?mirrorMode=", MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeMarkerString);
        Assert.Equal(0x13e604, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeMarkerVirtualAddress);
        Assert.Equal(0x69c7c, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeMarkerFindAddress);
        Assert.Equal(12, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeMarkerLength);
        Assert.Equal(0x69ca0, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeValueSubstringStartAddress);
        Assert.Equal(0x69cb4, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeValueSubstringCallAddress);
        Assert.Equal(0x69cd0, MiPlayLx06MpasReceiverEvidence.MpasOpenMirrorModeStrtolAddress);
        Assert.Equal(0x6bf54, MiPlayLx06MpasReceiverEvidence.MpasOpenModeOneBranchAddress);
        Assert.Equal(0x6c028, MiPlayLx06MpasReceiverEvidence.MpasOpenModeTwoBranchAddress);
        Assert.Equal(0x702a0, MiPlayLx06MpasReceiverEvidence.MpasOpenModeStateTransitionHelperAddress);
        Assert.Equal("wfd://", MiPlayLx06MpasReceiverEvidence.MpasOpenWfdUrlMarkerString);
        Assert.Equal(0x13e66c, MiPlayLx06MpasReceiverEvidence.MpasOpenWfdUrlMarkerVirtualAddress);
        Assert.Equal(0x69da0, MiPlayLx06MpasReceiverEvidence.MpasOpenUrlWithoutMirrorModeSubstringStartAddress);
        Assert.Equal(0x69da8, MiPlayLx06MpasReceiverEvidence.MpasOpenUrlWithoutMirrorModeSubstringCallAddress);
        Assert.Equal(0x69db8, MiPlayLx06MpasReceiverEvidence.MpasOpenAssignUrlWithoutMirrorModeAddress);
        Assert.Equal(0x69dcc, MiPlayLx06MpasReceiverEvidence.MpasOpenWfdUrlMarkerFindAddress);
        Assert.Equal(0x69e20, MiPlayLx06MpasReceiverEvidence.MpasOpenHostPortSeparatorSearchAddress);
        Assert.Equal(0x69e38, MiPlayLx06MpasReceiverEvidence.MpasOpenHostCopyAddress);
        Assert.Equal("seize", MiPlayLx06MpasReceiverEvidence.MpasOpenSourceChangedNotifyText);
        Assert.Equal(0x13e698, MiPlayLx06MpasReceiverEvidence.MpasOpenSourceChangedNotifyTextVirtualAddress);
        Assert.Equal(0x0022, MiPlayLx06MpasReceiverEvidence.MpasOpenSourceChangedNotifyCommandId);
        Assert.Equal(0x69fb8, MiPlayLx06MpasReceiverEvidence.MpasOpenSourceChangedNotifySendCallAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpasOpenPayloadShape(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x0000", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not a bare empty open command", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("?mirrorMode=", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("mirrorMode 1", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("mirrorMode 2", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x69da8", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x69db8", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("wfd://", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0022 seize", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMediaLiveCmdOpenPayloadMatchesMpasParserOffline()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasOpenStripsMirrorModeQueryBeforeWfdSearchObserved);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpasOpenPayloadCompatibility(
            snapshot,
            "wfd://192.168.10.9:7236?mirrorMode=1");

        Assert.True(decision.CanProceed);
        Assert.Contains("mirrorMode 1", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("strips the query", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("wfd://192.168.10.9:7236", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not explained by URL query ordering", decision.Reason, StringComparison.Ordinal);

        var missingMarker = MiPlayLx06MpasReceiverEvidence.EvaluateMpasOpenPayloadCompatibility(
            snapshot,
            "wfd://192.168.10.9:7236");
        Assert.False(missingMarker.CanProceed);
        Assert.Contains("?mirrorMode=", missingMarker.Reason, StringComparison.Ordinal);

        var unsupportedMode = MiPlayLx06MpasReceiverEvidence.EvaluateMpasOpenPayloadCompatibility(
            snapshot,
            "wfd://192.168.10.9:7236?mirrorMode=3");
        Assert.False(unsupportedMode.CanProceed);
        Assert.Contains("mirrorMode 1 and mirrorMode 2", unsupportedMode.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SenderInfoAndAddMirrorPathsExplainWhyDirectCmdOpenMayBePrematureOffline()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasSenderInfoPreparedCmdOpenPathObserved);
        Assert.True(snapshot.MpasAddMirrorAckCanRearmCmdOpenObserved);
        Assert.Equal("sender-info-prepared", MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedString);
        Assert.Equal(0x12efac, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStringFileOffset);
        Assert.Equal(0x13efac, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStringVirtualAddress);
        Assert.Equal(0x70d88, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStringLoadAddress);
        Assert.Equal("sender-info-prepared index:%d port:%d valid:%d", MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStatusLogTail);
        Assert.Equal(0x13efcc, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStatusLogStringVirtualAddress);
        Assert.Equal(0x70e38, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedStatusLogLoadAddress);
        Assert.Equal("on sender-info-prepared local send Cmdtype::Cmd_Open %s", MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedLocalCmdOpenLogString);
        Assert.Equal(0x12f098, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedLocalCmdOpenLogStringFileOffset);
        Assert.Equal(0x13f098, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedLocalCmdOpenLogStringVirtualAddress);
        Assert.Equal(0x71328, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedLocalCmdOpenLogLoadAddress);
        Assert.Equal("on sender-info-prepared pSlave send Cmdtype::Cmd_Open %s", MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedSlaveCmdOpenLogString);
        Assert.Equal(0x12f0e0, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedSlaveCmdOpenLogStringFileOffset);
        Assert.Equal(0x13f0e0, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedSlaveCmdOpenLogStringVirtualAddress);
        Assert.Equal(0x71158, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedSlaveCmdOpenLogLoadAddress);
        Assert.Equal(0x711bc, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedCmdOpenSendCallAddress);
        Assert.Equal(0x367bc, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedCmdOpenSendHelperAddress);
        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.MpasSenderInfoPreparedCmdOpenCommandId);
        Assert.Equal("Cmd_AddMirror_Ack", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckString);
        Assert.Equal(0x12f460, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckStringFileOffset);
        Assert.Equal(0x13f460, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckStringVirtualAddress);
        Assert.Equal("on Cmd_AddMirror_Ack master send Cmdtype::Cmd_Open", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckMasterCmdOpenLogString);
        Assert.Equal(0x12f4b4, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckMasterCmdOpenLogStringFileOffset);
        Assert.Equal(0x13f4b4, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckMasterCmdOpenLogStringVirtualAddress);
        Assert.Equal(0x72210, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckMasterCmdOpenLogLoadAddress);
        Assert.Equal(0x721e8, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckSequenceCompareAddress);
        Assert.Equal(0x72230, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckPendingFlagClearAddress);
        Assert.Equal(0x72234, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckSavedSequenceStoreAddress);
        Assert.Equal(0x72238, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckSequenceResetAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpasOpenPreOpenContextHypothesis(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("sender-info-prepared", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x711bc", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x367bc", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_AddMirror_Ack", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x72210", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("missing source identity/device-info/add-mirror/session context", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not a payload query-order bug", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void SetPlaySourceAckOnlyProbeSupersedesExternalAddMirrorOnlyProbe()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MpasSetPlaySourceServerCommandObserved);
        Assert.True(snapshot.MpasSetPlaySourceAckBeforePayloadParseObserved);
        Assert.True(snapshot.MpasSetPlaySourceEmptyPayloadAckOnlySafeObserved);
        Assert.True(snapshot.MpasSetPlaySourceInternalPipeCommandObserved);
        Assert.True(snapshot.MpasAddMirrorRequestCommandObserved);
        Assert.True(snapshot.MpasExternalAddMirrorUnhandledByServerDispatcherObserved);
        Assert.True(snapshot.MpasAddMirrorAckCommandIdObserved);
        Assert.True(snapshot.MpasAddMirrorPayloadIdentityFragmentsObserved);
        Assert.True(snapshot.MpasLocalAddMirrorPayloadFullyObserved);
        Assert.True(snapshot.MpasLocalAddMirrorUsesLocalIpEndpoint);
        Assert.True(snapshot.MpasLocalAddMirrorUsesDefault7236);
        Assert.True(snapshot.MpasLocalAddMirrorIsLocalTrueObserved);
        Assert.True(snapshot.MpasLocalCmdOpenBuiltFromLocalAddMirrorEndpointObserved);
        Assert.Equal(0x65840, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceDispatchCompareAddress);
        Assert.Equal(0x66ad8, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceHandlerAddress);
        Assert.Equal(0x0040, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceCommandId);
        Assert.Equal(0x0041, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceAcknowledgementCommandId);
        Assert.Equal(0x66b50, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceAckSendCommandMoveAddress);
        Assert.Equal(0x66b58, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceAckSendCallAddress);
        Assert.Equal(0x66b70, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourcePayloadPresenceCheckAddress);
        Assert.Equal(0x657cc, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourcePayloadGateReturnAddress);
        Assert.Equal(0x66c70, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonParseCallAddress);
        Assert.Equal(0x67710, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceFieldAssignRefChannelAddress);
        Assert.Equal(0x67730, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceFieldAssignRefFunctionAddress);
        Assert.Equal(0x67740, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceFieldAssignRefContentAddress);
        Assert.Equal("Cmd_SetPlaySource datalen[%d]", MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceDataLengthLogString);
        Assert.Equal(0x13df10, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceDataLengthLogStringVirtualAddress);
        Assert.Equal(0x66b08, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceDataLengthLogLoadAddress);
        Assert.Equal("ref_channel", MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefChannel);
        Assert.Equal("ref_function", MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefFunction);
        Assert.Equal("ref_content", MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefContent);
        Assert.Equal(0x13cb8c, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefChannelVirtualAddress);
        Assert.Equal(0x13cbc0, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefFunctionVirtualAddress);
        Assert.Equal(0x13ccd8, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefContentVirtualAddress);
        Assert.Equal(0x66d14, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefChannelCompareAddress);
        Assert.Equal(0x66ccc, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefFunctionCompareAddress);
        Assert.Equal(0x66ce8, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceJsonKeyRefContentCompareAddress);
        Assert.Equal("setPlaySource Cmd_SetPlaySource[%s][%u]", MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceInternalPipeLogString);
        Assert.Equal(0x13fb74, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceInternalPipeLogStringVirtualAddress);
        Assert.Equal(0x74510, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceInternalPipeLogLoadAddress);
        Assert.Equal(0x005a, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceInternalPipeCommandId);
        Assert.Equal(0x74544, MiPlayLx06MpasReceiverEvidence.MpasSetPlaySourceInternalPipeSendCallAddress);
        Assert.Equal("Cmd_AddMirror", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorString);
        Assert.Equal(0x002e, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorCommandId);
        Assert.Equal(0x002f, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckCommandId);
        Assert.Equal(0x65e9c, MiPlayLx06MpasReceiverEvidence.MpasServerDispatcherAddMirrorLowerRangeCompareAddress);
        Assert.Equal(0x666b8, MiPlayLx06MpasReceiverEvidence.MpasServerDispatcherAddMirrorHigherRangeCompareAddress);
        Assert.Equal(0x666c0, MiPlayLx06MpasReceiverEvidence.MpasServerDispatcherAddMirrorHigherRangeSecondCompareAddress);
        Assert.Equal(0x667e8, MiPlayLx06MpasReceiverEvidence.MpasServerDispatcherUnhandledCommandReturnFalseAddress);
        Assert.Equal(0x70a5c, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckDispatchCompareAddress);
        Assert.Equal(0x70a68, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAckDispatchBranchAddress);
        Assert.Equal(0x6e96c, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorSendCommandMoveAddress);
        Assert.Equal(0x6e970, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorSendCallAddress);
        Assert.Equal(0x6f1c8, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAlternateSendCommandMoveAddress);
        Assert.Equal(0x6f1cc, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorAlternateSendCallAddress);
        Assert.Equal(0x6e948, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPendingFlagSetAddress);
        Assert.Equal(0x6e94c, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorSavedSequenceStoreAddress);
        Assert.Equal(0x32e, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorSavedSequenceOffset);
        Assert.Equal(0x332, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPendingFlagOffset);
        Assert.Equal("from:", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadFromFragment);
        Assert.Equal("&islocal:", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalFragment);
        Assert.Equal(0x13ec74, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadFromFragmentVirtualAddress);
        Assert.Equal(0x13ebc8, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalFragmentVirtualAddress);
        Assert.Equal(0x6ef30, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadFromAppendAddress);
        Assert.Equal(0x6ef68, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalAppendAddress);
        Assert.Equal(0x141620, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalFalseStringVirtualAddress);
        Assert.Equal(0x131620, MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalFalseStringFileOffset);
        Assert.Equal("0", MiPlayLx06MpasReceiverEvidence.MpasCmdAddMirrorPayloadIsLocalFalseString);
        Assert.Equal(0x6e620, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorHelperEntryAddress);
        Assert.Equal(0x6e630, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorGetLocalIpCallAddress);
        Assert.Equal(":7236", MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPortSuffix);
        Assert.Equal(0x13ebb8, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPortSuffixVirtualAddress);
        Assert.Equal(0x6e6c4, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPortSuffixAppendAddress);
        Assert.Equal(0x1c44, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPortImmediate);
        Assert.Equal(7_236, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPort);
        Assert.Equal(0x6e6ec, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorDefaultPortStoreAddress);
        Assert.Equal(0x34c, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorServerAppPortOffset);
        Assert.Equal(0x334, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorServerAppEndpointOffset);
        Assert.Equal(0x6e6f0, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorEndpointAssignAddress);
        Assert.Equal("<local-ip>:7236&from:<local-ip>&islocal:1", MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadTemplate);
        Assert.Equal("&from:", MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadFromPrefixFragment);
        Assert.Equal(0x13ebc0, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadFromPrefixVirtualAddress);
        Assert.Equal(0x6e704, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadEndpointAppendAddress);
        Assert.Equal(0x6e728, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadFromPrefixAppendAddress);
        Assert.Equal(0x6e740, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadFromLocalIpAppendAddress);
        Assert.Equal(0x6e7a0, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadIsLocalAppendAddress);
        Assert.Equal("1", MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadIsLocalTrueString);
        Assert.Equal(0x1421ec, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadIsLocalTrueStringVirtualAddress);
        Assert.Equal(0x6e814, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorPayloadIsLocalTrueAppendAddress);
        Assert.Equal("addLocalMediaMirror Cmd_AddMirror[%s][%zu] %d", MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorLogString);
        Assert.Equal(0x13ebd4, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorLogStringVirtualAddress);
        Assert.Equal(0x6e910, MiPlayLx06MpasReceiverEvidence.MpasLocalAddMirrorLogLoadAddress);
        Assert.Equal(0x567f4, MiPlayLx06MpasReceiverEvidence.MpasLocalEndpointBuilderAddress);
        Assert.Equal(0x526e0, MiPlayLx06MpasReceiverEvidence.MpasLocalIpGetterAddress);
        Assert.Equal("local ip error", MiPlayLx06MpasReceiverEvidence.MpasLocalIpErrorString);
        Assert.Equal(0x13c774, MiPlayLx06MpasReceiverEvidence.MpasLocalIpErrorStringVirtualAddress);
        Assert.Equal(0x56850, MiPlayLx06MpasReceiverEvidence.MpasLocalEndpointBuilderDefaultPortCompareAddress);
        Assert.Equal(0x569b4, MiPlayLx06MpasReceiverEvidence.MpasLocalEndpointBuilderPortStoreAddress);
        Assert.Equal(0x569c0, MiPlayLx06MpasReceiverEvidence.MpasLocalEndpointBuilderPortIncrementAddress);
        Assert.Equal(0x56ba8, MiPlayLx06MpasReceiverEvidence.MpasLocalEndpointBuilderErrorAssignAddress);
        Assert.Equal(0x7c068, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlSetupEntryAddress);
        Assert.Equal(0x7c0cc, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlEndpointBuilderCallAddress);
        Assert.Equal(0x0b8, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlPortSeedOffset);
        Assert.Equal(0x90, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlOffset);
        Assert.Equal("SlaveDevice m_strMediaUrl[%s]", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlLogTail);
        Assert.Equal(0x1403b4, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlLogTailVirtualAddress);
        Assert.Equal(0x7c160, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlLogLoadAddress);
        Assert.Equal(0x7c168, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceMediaUrlLogCallAddress);
        Assert.Equal("SlaveDevice addMirror", MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAddMirrorLogTail);
        Assert.Equal(0x1403e4, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAddMirrorLogTailVirtualAddress);
        Assert.Equal(0x7c1b4, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAddMirrorLogLoadAddress);
        Assert.Equal(0x7c2e8, MiPlayLx06MpasReceiverEvidence.MpasSlaveDeviceAddMirrorBuilderCallAddress);
        Assert.Equal(0x6ea90, MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientEntryAddress);
        Assert.Equal("startLocalMediaClient Cmd_Open[%s][%zu]", MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientLogString);
        Assert.Equal(0x13ec18, MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientLogStringVirtualAddress);
        Assert.Equal(0x6ec20, MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientLogLoadAddress);
        Assert.Equal(0x6ec60, MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientSendCommandMoveAddress);
        Assert.Equal(0x6ec64, MiPlayLx06MpasReceiverEvidence.MpasStartLocalMediaClientSendCallAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpasPreOpenCommandSequenceBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("0x0040", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0041", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x005a", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x002e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x002f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x32e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("+0x332", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x66b70", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x66c70", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x667e8", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("empty 0x0040 ACK-only", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize JSON source identity", decision.Reason, StringComparison.Ordinal);
    }
    [Fact]
    public void MpapCmdOpenBridgesWfdPayloadToOpenMirrorClientOffline()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Equal("Cmd_Open", MiPlayLx06MpasReceiverEvidence.MpapCmdOpenString);
        Assert.Equal(0x15e694, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenStringVirtualAddress);
        Assert.Equal("Cmd_Open:%s m_Layout:%d", MiPlayLx06MpasReceiverEvidence.MpapCmdOpenPayloadLogString);
        Assert.Equal(0x15e6ac, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenPayloadLogStringVirtualAddress);
        Assert.Equal(0x21398, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenPayloadLogStringLoadAddress);
        Assert.Equal("?mirrorMode=", MiPlayLx06MpasReceiverEvidence.MpapCmdOpenMirrorModeMarkerString);
        Assert.Equal(0x15e6d4, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenMirrorModeMarkerVirtualAddress);
        Assert.Equal(0x213c8, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenMirrorModeMarkerFindAddress);
        Assert.Equal(12, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenMirrorModeMarkerLength);
        Assert.Equal("wfd://", MiPlayLx06MpasReceiverEvidence.MpapCmdOpenWfdUrlMarkerString);
        Assert.Equal(0x15e734, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenWfdUrlMarkerVirtualAddress);
        Assert.Equal(0x2155c, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenWfdUrlMarkerFindAddress);
        Assert.Equal(":7236", MiPlayLx06MpasReceiverEvidence.MpapCmdOpenDefaultWfdPortSuffix);
        Assert.Equal(0x15e754, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenDefaultWfdPortSuffixVirtualAddress);
        Assert.Equal(0x21cac, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenSynthesizeWfdUrlBranchAddress);
        Assert.Equal(0x21cf4, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenAppendWfdPrefixAddress);
        Assert.Equal(0x21d2c, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenAppendDefaultPortAddress);
        Assert.Equal(0x1f900, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientFunctionAddress);
        Assert.Equal(0x1f998, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientUrlLogAddress);
        Assert.Equal(0x21570, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenDirectOpenMirrorClientCallAddress);
        Assert.Equal(0x21d8c, MiPlayLx06MpasReceiverEvidence.MpapCmdOpenSynthesizedOpenMirrorClientCallAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpapCmdOpenMirrorClientBridge(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("OpenMirrorClient 0x1f900", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("?mirrorMode=", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("wfd://", decision.Reason, StringComparison.Ordinal);
        Assert.Contains(":7236", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x21570", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x21d8c", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize live open", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MpapOpenMirrorClientParsesWfdHostPortAndRtspTemplateOffline()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Equal(":", MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientPortSeparatorString);
        Assert.Equal(0x15e5f8, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientPortSeparatorVirtualAddress);
        Assert.Equal(0x1fb50, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientFindLastPortSeparatorAddress);
        Assert.Equal(7236, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientDefaultSourcePort);
        Assert.Equal(0x1c44, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientDefaultSourcePortImmediate);
        Assert.Equal(0x1fb58, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientDefaultSourcePortMoveAddress);
        Assert.Equal(0x1fc4c, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientPortSubstringEraseAddress);
        Assert.Equal(0x1fc64, MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientPortStrtolAddress);

        Assert.Equal(0x6a588, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorAddress);
        Assert.Equal(0x6a668, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorSkipWfdPrefixAddress);
        Assert.Equal(0x6a678, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorStrrchrAddress);
        Assert.Equal(0x6a6a4, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorHostCopyAddress);
        Assert.Equal(0x6a6b4, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorPortStrtolAddress);
        Assert.Equal(0x6a6bc, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorPortStoreAddress);
        Assert.Equal(0x6a6e4, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorDefaultPortMoveAddress);
        Assert.Equal(0x6a6ec, MiPlayLx06MpasReceiverEvidence.MpapWfdClientConstructorDefaultPortStoreAddress);
        Assert.Equal(0x04, MiPlayLx06MpasReceiverEvidence.MpapWfdClientHostOffset);
        Assert.Equal(0x24, MiPlayLx06MpasReceiverEvidence.MpapWfdClientPortOffset);
        Assert.Equal(0x80, MiPlayLx06MpasReceiverEvidence.MpapWfdClientContextOffset);

        Assert.Equal("sourceHost", MiPlayLx06MpasReceiverEvidence.MpapSourceHostKeyString);
        Assert.Equal(0x167998, MiPlayLx06MpasReceiverEvidence.MpapSourceHostKeyVirtualAddress);
        Assert.Equal("sourcePort", MiPlayLx06MpasReceiverEvidence.MpapSourcePortKeyString);
        Assert.Equal(0x1679a4, MiPlayLx06MpasReceiverEvidence.MpapSourcePortKeyVirtualAddress);
        Assert.Equal(0x83548, MiPlayLx06MpasReceiverEvidence.MpapSourceHostSessionEmitAddress);
        Assert.Equal(0x83560, MiPlayLx06MpasReceiverEvidence.MpapSourcePortSessionEmitAddress);
        Assert.Equal(0x83998, MiPlayLx06MpasReceiverEvidence.MpapSourceHostObjectEmitAddress);
        Assert.Equal(0x839b0, MiPlayLx06MpasReceiverEvidence.MpapSourcePortObjectEmitAddress);
        Assert.Equal(0x8aca0, MiPlayLx06MpasReceiverEvidence.MpapSourceHostSessionParseAddress);
        Assert.Equal(0x8acc0, MiPlayLx06MpasReceiverEvidence.MpapSourcePortSessionParseAddress);

        Assert.Equal("rtsp://", MiPlayLx06MpasReceiverEvidence.MpapRtspSchemeString);
        Assert.Equal(0x16731c, MiPlayLx06MpasReceiverEvidence.MpapRtspSchemeVirtualAddress);
        Assert.Equal("rtsp://%s/wfd1.0/streamid=0", MiPlayLx06MpasReceiverEvidence.MpapRtspStreamUrlTemplateString);
        Assert.Equal(0x1686fc, MiPlayLx06MpasReceiverEvidence.MpapRtspStreamUrlTemplateVirtualAddress);
        Assert.Equal(0x88058, MiPlayLx06MpasReceiverEvidence.MpapRtspStreamUrlSnprintfAddress);
        Assert.Equal("wfd_presentation_URL: rtsp://%s/wfd1.0/streamid=0 none", MiPlayLx06MpasReceiverEvidence.MpapWfdPresentationUrlString);
        Assert.Equal(0x16a544, MiPlayLx06MpasReceiverEvidence.MpapWfdPresentationUrlVirtualAddress);
        Assert.Equal(0x927e8, MiPlayLx06MpasReceiverEvidence.MpapWfdPresentationUrlBuildAddress);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpapOpenMirrorClientUrlAndRtspBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("last ':'", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("7236", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("wfd:// prefix", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("host at +0x04", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("port at +0x24", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("sourceHost/sourcePort", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("rtsp://%s/wfd1.0/streamid=0", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("source side must provide a reachable WFD/RTSP endpoint shape", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize live open", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate0058DoesNotMapToMpasOpenDispatcher()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.CmdOpen);
        Assert.Equal(0x0000, MiPlayLx06MpasReceiverEvidence.MpasOpenDispatchCommandId);
        Assert.Equal(0x0036, MiPlayLx06MpasReceiverEvidence.MpasAlternateOpenishBranchCommandId);
        Assert.Equal(0x0058, MiPlayLx06MpasReceiverEvidence.CandidateLegacyProbe0058);
        Assert.Equal(0x0062, MiPlayLx06MpasReceiverEvidence.CmdSpeakerRandomPlay);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateCandidate0058AsMpasOpen(snapshot);

        Assert.False(decision.CanProceed);
        Assert.Contains("0x0058 is not handled", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open maps to 0x0000", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("remain gated", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MpapShowsAudioReceiverButDoesNotAuthorizeMediaProbe()
    {
        var snapshot = MiPlayLx06MpasReceiverEvidence.CreateCurrentSnapshot();

        Assert.Equal("MiPlayQuick_AudioSink", MiPlayLx06MpasReceiverEvidence.MpapQuickAudioSinkString);
        Assert.Equal("OpenMirrorClient", MiPlayLx06MpasReceiverEvidence.MpapOpenMirrorClientString);
        Assert.Equal("DealPacket", MiPlayLx06MpasReceiverEvidence.MpapDealPacketString);
        Assert.Equal("/data/miplay/audio_dump", MiPlayLx06MpasReceiverEvidence.MpapAudioDumpPath);
        Assert.Equal("audio/mp4a-latm", MiPlayLx06MpasReceiverEvidence.MpapAacEldCodecString);

        var decision = MiPlayLx06MpasReceiverEvidence.EvaluateMpapAudioReceiverBoundary(snapshot);

        Assert.True(decision.CanProceed);
        Assert.Contains("MiPlayQuick_AudioSink", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize media", decision.Reason, StringComparison.Ordinal);
    }
}