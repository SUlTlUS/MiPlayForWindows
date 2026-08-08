using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthOfficialCommandOrderEvidenceTests
{
    [Fact]
    public void ConstantsCapturePhoneFirmwareTraceArtifactsAndOffsets()
    {
        Assert.Contains("Mi13P_OS3.0.313", MiPlayPostAuthOfficialCommandOrderEvidence.PhoneFirmwareScope, StringComparison.Ordinal);
        Assert.EndsWith("phone_source_dex_cmdsession_xrefs.json", MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionXrefArtifact, StringComparison.Ordinal);
        Assert.EndsWith("phone_source_all_dex_ref_identity_trace.json", MiPlayPostAuthOfficialCommandOrderEvidence.RefIdentityTraceArtifact, StringComparison.Ordinal);
        Assert.EndsWith("mirroros3_command_name_map.json", MiPlayPostAuthOfficialCommandOrderEvidence.CommandNameMapArtifact, StringComparison.Ordinal);

        Assert.Equal(0x294780, MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionControlConnectCmdSessionJavaCodeOffset);
        Assert.Equal(0x294900, MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionControlCreateCmdSessionJavaCodeOffset);
        Assert.Equal(0x295014, MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionControlGetDeviceInfoJavaCodeOffset);
        Assert.Equal(0x295460, MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionControlOpenDeviceJavaCodeOffset);
        Assert.Equal(0x295C84, MiPlayPostAuthOfficialCommandOrderEvidence.CmdSessionControlSetPlaySourceJavaCodeOffset);
        Assert.Equal(0x27A8AC, MiPlayPostAuthOfficialCommandOrderEvidence.MiPlayAudioServiceStartCommandChannelCodeOffset);
        Assert.Equal(0x278360, MiPlayPostAuthOfficialCommandOrderEvidence.MiPlayAudioServiceCmdSessionSuccessCodeOffset);
        Assert.Equal(0x279F94, MiPlayPostAuthOfficialCommandOrderEvidence.MiPlayAudioServiceOnTopActiveSessionChangeCodeOffset);
        Assert.Equal(0x2B0B40, MiPlayPostAuthOfficialCommandOrderEvidence.MiplayMultiDisplayManageOnPlayCodeOffset);
        Assert.Equal(0x2B63E8, MiPlayPostAuthOfficialCommandOrderEvidence.MiplaySessionCtrProxyOnRefreshDeviceInfoCodeOffset);
        Assert.Equal(0x2C1988, MiPlayPostAuthOfficialCommandOrderEvidence.StatsUtilsSetPlaySourceCodeOffset);
        Assert.EndsWith("miplay-root-8899-reconnect-20260726-122421.pcap", MiPlayPostAuthOfficialCommandOrderEvidence.RuntimeRootTcpdumpArtifact, StringComparison.Ordinal);
        Assert.Contains("0x0058 -> 0x001e", MiPlayPostAuthOfficialCommandOrderEvidence.RuntimePostAuthOrder, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotSeparatesReadOnlySuccessPathFromLaterSetPlaySourceEvents()
    {
        var snapshot = MiPlayPostAuthOfficialCommandOrderEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.PhoneFirmwareDexCmdSessionControlLocalized);
        Assert.True(snapshot.ConnectCmdSessionCreatesNativeCmdHandler);
        Assert.True(snapshot.ControlMethodsShareCmdHandlerAndSessionType);
        Assert.True(snapshot.StartCommandChannelCallsConnectCmdSession);
        Assert.True(snapshot.CmdSessionSuccessCallsGetDeviceInfo);
        Assert.True(snapshot.DeviceRefreshCanCallGetDeviceInfoAgain);
        Assert.True(snapshot.SetPlaySourceCalledFromStatsOnPlayOrActiveSessionChange);
        Assert.True(snapshot.SetPlaySourceUsesDeviceRefMapsAndCmdSessionControlMap);
        Assert.True(snapshot.SetPlaySourceIsNotCalledDirectlyByCmdSessionSuccess);
        Assert.True(snapshot.OpenDeviceHasSeparateControlEntrypoints);
        Assert.True(snapshot.NativeCmdSourceSendClusterObserved);
        Assert.True(snapshot.CommandNameMapAlignsGetDeviceInfoSetPlaySourceAddMirrorOpen);
        Assert.True(snapshot.RootTcpdumpRuntimeOrderObserved);
        Assert.True(snapshot.RuntimeOrderIncludesLocalDeviceInfoBeforeGetDeviceInfo);
        Assert.True(snapshot.RuntimeOrderIncludesGetMirrorModeBeforeSetPlaySource);
        Assert.True(snapshot.CurrentMilinkNativeIdentifiesGetMirrorModePair);
        Assert.True(snapshot.RuntimeSetPlaySourceContinuesHeartbeatWithout0041InWindow);
        Assert.True(snapshot.NativeNoResetOfficialJsonSetPlaySourceRejected);
        Assert.True(snapshot.CurrentProbeSkippedOfficialGetDeviceInfoReadyContext);
        Assert.True(snapshot.NoNetworkOperationPerformed);

        Assert.Contains("getDeviceInfo", MiPlayPostAuthOfficialCommandOrderEvidence.OfficialPostConnectReadOnlyOrder, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", MiPlayPostAuthOfficialCommandOrderEvidence.OfficialSetPlaySourceEventOrder, StringComparison.Ordinal);
        Assert.Contains("0x0034 GetMirrorMode", MiPlayPostAuthOfficialCommandOrderEvidence.CommandIdAlignment, StringComparison.Ordinal);
        Assert.Contains("0x0041", MiPlayPostAuthOfficialCommandOrderEvidence.CommandIdAlignment, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentDecisionForbidsImmediateSetPlaySourceAndStandaloneGetDeviceInfo()
    {
        var decision = MiPlayPostAuthOfficialCommandOrderEvidence.Evaluate(
            MiPlayPostAuthOfficialCommandOrderEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanTreatImmediatePostAuthSetPlaySourceAsOfficial);
        Assert.False(decision.CanDesignNextReadOnlyDeviceInfoGate);
        Assert.Contains("Immediate post-auth SetPlaySource is not the official order", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("standalone post-auth getDeviceInfo is also too narrow", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058 -> 0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("GetMirrorMode/GetMirrorMode_Ack", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("not another generated 0x001e", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058/0x001e/0x001f/0x0034/0x0035 GetMirrorMode/0x0040", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSharedNativeHandlerBlocksReadOnlyGateDesign()
    {
        var snapshot = MiPlayPostAuthOfficialCommandOrderEvidence.CreateCurrentSnapshot() with
        {
            ControlMethodsShareCmdHandlerAndSessionType = false,
        };

        var decision = MiPlayPostAuthOfficialCommandOrderEvidence.Evaluate(snapshot);

        Assert.False(decision.CanTreatImmediatePostAuthSetPlaySourceAsOfficial);
        Assert.False(decision.CanDesignNextReadOnlyDeviceInfoGate);
        Assert.Contains("cmdHandler/sessionType", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkSideEffectsInvalidateStaticEvidence()
    {
        var snapshot = MiPlayPostAuthOfficialCommandOrderEvidence.CreateCurrentSnapshot() with
        {
            NoNetworkOperationPerformed = false,
        };

        var decision = MiPlayPostAuthOfficialCommandOrderEvidence.Evaluate(snapshot);

        Assert.False(decision.CanTreatImmediatePostAuthSetPlaySourceAsOfficial);
        Assert.False(decision.CanDesignNextReadOnlyDeviceInfoGate);
        Assert.Contains("offline-only", decision.Reason, StringComparison.Ordinal);
    }
}
