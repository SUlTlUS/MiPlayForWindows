using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourcePayloadSemanticsEvidenceTests
{
    [Fact]
    public void ConstantsSeparateExternalSetPlaySourceFromPipeAndMirrorCommands()
    {
        Assert.Equal((ushort)0x0040, MiPlaySetPlaySourcePayloadSemanticsEvidence.ExternalSetPlaySourceCommand);
        Assert.Equal((ushort)0x0041, MiPlaySetPlaySourcePayloadSemanticsEvidence.ExternalSetPlaySourceAckCommand);
        Assert.Equal((ushort)0x005a, MiPlaySetPlaySourcePayloadSemanticsEvidence.InternalPipeSetPlaySourceCommand);
        Assert.Equal((ushort)0x002e, MiPlaySetPlaySourcePayloadSemanticsEvidence.CmdAddMirror);
        Assert.Equal((ushort)0x002f, MiPlaySetPlaySourcePayloadSemanticsEvidence.CmdAddMirrorAck);
        Assert.Equal((ushort)0x0000, MiPlaySetPlaySourcePayloadSemanticsEvidence.CmdOpen);
    }

    [Fact]
    public void ReceiverPayloadShapeAndOfficialSenderBuilderAreBothLocalizedOffline()
    {
        var snapshot = MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.ExternalSetPlaySource0040DispatchObserved);
        Assert.True(snapshot.SetPlaySource0041AckBeforePayloadParse);
        Assert.True(snapshot.NonEmptyPayloadRequiresJsonParse);
        Assert.True(snapshot.RefChannelKeyObserved);
        Assert.True(snapshot.RefFunctionKeyObserved);
        Assert.True(snapshot.RefContentKeyObserved);
        Assert.True(snapshot.RefFieldsAssignedAfterParse);
        Assert.True(snapshot.InternalPipeUses005aNotExternal0040);
        Assert.True(snapshot.OfficialSender0040BuilderLocalized);
        Assert.False(snapshot.SourceIdentityToLegacy8899BridgeLocalized);
        Assert.True(snapshot.Current19413NativeNoResetOfficialJsonRejected);
        Assert.Contains("ref_channel", MiPlaySetPlaySourcePayloadSemanticsEvidence.ReceiverPayloadShape, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", MiPlaySetPlaySourcePayloadSemanticsEvidence.PhoneFirmwareOfficial0040Builder, StringComparison.Ordinal);
        Assert.Contains("JSONObject.putOpt", MiPlaySetPlaySourcePayloadSemanticsEvidence.Official0040PayloadShape, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidSourceIdentityEvidenceIsLocalizedButDoesNotAuthorizeLegacy8899Payload()
    {
        var snapshot = MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.AndroidAppInfoGeneratedFromCallingUidPackageSignature);
        Assert.True(snapshot.AndroidSignatureIsSha256CertificateFingerprint);
        Assert.True(snapshot.AndroidPlatformTypeIsOne);
        Assert.True(snapshot.AndroidServiceNameMergeStringObserved);
        Assert.True(snapshot.AppInfoPassedToNativeChannelRegistration);
        Assert.True(snapshot.AppInfoPassedToNativeChannelCreation);
        Assert.Contains("AppInfo", MiPlaySetPlaySourcePayloadSemanticsEvidence.AndroidSourceIdentityShape, StringComparison.Ordinal);
        Assert.Contains("SHA-256", MiPlaySetPlaySourcePayloadSemanticsEvidence.SignatureAlgorithm, StringComparison.Ordinal);
    }

    [Fact]
    public void ApkNativeScanDoesNotExposeDirectLegacyIdentityBridge()
    {
        var snapshot = MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.ApkNativeDirectLegacySetPlaySourceStringsAbsent);
        Assert.True(snapshot.ApkNativeDirectReceiverRefKeysAbsent);
        Assert.True(snapshot.ApkNativeDirect8899BuilderStringsAbsent);
        Assert.True(snapshot.ApkNativeGenericMiplayTransportSymbolsObserved);
        Assert.Contains("libmicontinuity.so", MiPlaySetPlaySourcePayloadSemanticsEvidence.ApkNativeLegacyBridgeScanScope, StringComparison.Ordinal);
        Assert.Contains("ref_channel", MiPlaySetPlaySourcePayloadSemanticsEvidence.ApkNativeDirectLegacyBridgeNegativeStrings, StringComparison.Ordinal);
        Assert.Contains("MiplayTransport", MiPlaySetPlaySourcePayloadSemanticsEvidence.ApkNativeObservedTransportFamily, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentDecisionRefusesLiveNonEmptySetPlaySourceAfterNativeNoResetNegativeResult()
    {
        var decision = MiPlaySetPlaySourcePayloadSemanticsEvidence.Evaluate(
            MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanBuildLiveNonEmptySetPlaySource);
        Assert.Contains("0x0040", decision.StaticReceiverConclusion, StringComparison.Ordinal);
        Assert.Contains("ref_channel", decision.StaticReceiverConclusion, StringComparison.Ordinal);
        Assert.Contains("PackageUtil.generateAppInfo", decision.SourceIdentityConclusion, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", decision.SourceIdentityConclusion, StringComparison.Ordinal);
        Assert.Contains("MiplayTransport", decision.SourceIdentityConclusion, StringComparison.Ordinal);
        Assert.Contains("0x002e", decision.OfficialOrderConclusion, StringComparison.Ordinal);
        Assert.Contains("0x0000", decision.OfficialOrderConclusion, StringComparison.Ordinal);
        Assert.Contains("native-no-reset official minimal JSON 0x0040", decision.Boundary, StringComparison.Ordinal);
        Assert.Contains("Do not send non-empty 0x0040", decision.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalAddMirrorIsStillDirectionErrorNotOfficialNextStep()
    {
        var snapshot = MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.ExternalAddMirror002eUnhandledByServerDispatcher);
        Assert.True(snapshot.AddMirrorAck002fCanRearmCmdOpen);
        Assert.True(snapshot.SenderInfoPreparedCanSendCmdOpen0000);
        Assert.True(snapshot.LocalAddMirrorCanPrecedeLocalCmdOpen);
        Assert.Equal("<local-ip>:7236&from:<local-ip>&islocal:1", MiPlaySetPlaySourcePayloadSemanticsEvidence.LocalAddMirrorPayloadTemplate);
    }

    [Fact]
    public void NewNativeLegacyHitsWouldInvalidateCurrentBoundaryUntilClassified()
    {
        var changedNativeScan = MiPlaySetPlaySourcePayloadSemanticsEvidence.CreateCurrentSnapshot() with
        {
            ApkNativeDirectLegacySetPlaySourceStringsAbsent = false,
        };

        var decision = MiPlaySetPlaySourcePayloadSemanticsEvidence.Evaluate(changedNativeScan);

        Assert.False(decision.CanBuildLiveNonEmptySetPlaySource);
        Assert.Contains("native direct legacy bridge scan changed", decision.SourceIdentityConclusion, StringComparison.Ordinal);
        Assert.Contains("unstable", decision.Boundary, StringComparison.Ordinal);
    }
}