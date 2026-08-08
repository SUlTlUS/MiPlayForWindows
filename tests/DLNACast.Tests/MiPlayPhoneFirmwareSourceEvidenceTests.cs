using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPhoneFirmwareSourceEvidenceTests
{
    [Fact]
    public void CatalogCapturesPhoneFirmwarePartitionsAndErofsCandidates()
    {
        var snapshot = MiPlayPhoneFirmwareSourceEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.FirmwareDirectoryCataloged);
        Assert.True(snapshot.LogicalProductAndSystemExtPartitionsExtracted);
        Assert.True(snapshot.MinimalErofsDirectoryIndexBuilt);
        Assert.True(snapshot.MirrorOs3CandidateLocalized);
        Assert.True(snapshot.MiLinkOs3CandidateLocalized);
        Assert.Contains("MirrorOS3", MiPlayPhoneFirmwareSourceEvidence.MirrorOs3Candidate, StringComparison.Ordinal);
        Assert.Contains("MiLinkOS3Cn", MiPlayPhoneFirmwareSourceEvidence.MiLinkOs3Candidate, StringComparison.Ordinal);
        Assert.Contains("system_ext_a", MiPlayPhoneFirmwareSourceEvidence.MediCastIoCandidate, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceSideLegacyCommandSessionBuilderAndWireIdsAreObservedOffline()
    {
        var snapshot = MiPlayPhoneFirmwareSourceEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.CmdSessionControlJniObserved);
        Assert.True(snapshot.CmdSourceAndCmdControlSymbolsObserved);
        Assert.True(snapshot.CreateCmdSessionAddrPortObserved);
        Assert.True(snapshot.SendOpenDeviceLogObserved);
        Assert.True(snapshot.SafetyAuthAckObserved);
        Assert.True(snapshot.CmdAuthObserved);
        Assert.True(snapshot.SafetyKeyDealAuthKeyAndAesIvObserved);
        Assert.True(snapshot.CmdTypeAndAckLoggingObserved);
        Assert.True(snapshot.NativeControlVersionObserved);
        Assert.True(snapshot.CandidateFilesExtractedFromErofs);
        Assert.True(snapshot.ApkZipIntegrityVerified);
        Assert.True(snapshot.DexCmdSessionXrefsRecovered);
        Assert.True(snapshot.OfficialSender0040BuilderLocalized);
        Assert.False(snapshot.AppInfoServiceNameToLegacy8899BridgeLocalized);
        Assert.True(snapshot.WireCommandIdsRecoveredFromPhoneFirmware);
    }

    [Fact]
    public void StaticContextsPreserveOffsetsCommandMapAndPayloadBuilderBoundary()
    {
        Assert.Contains("0x83f700c4", MiPlayPhoneFirmwareSourceEvidence.SourceCommandSessionContext, StringComparison.Ordinal);
        Assert.Contains("createCmdSession", MiPlayPhoneFirmwareSourceEvidence.SourceCommandSessionContext, StringComparison.Ordinal);
        Assert.Contains("send openDevice", MiPlayPhoneFirmwareSourceEvidence.SourceCommandSessionContext, StringComparison.Ordinal);
        Assert.Contains("3.2.5121919", MiPlayPhoneFirmwareSourceEvidence.SourceCommandSessionContext, StringComparison.Ordinal);
        Assert.Contains("Cmd_SafetyAuth_Ack", MiPlayPhoneFirmwareSourceEvidence.SafetyAuthContext, StringComparison.Ordinal);
        Assert.Contains("Cmd_Auth", MiPlayPhoneFirmwareSourceEvidence.SafetyAuthContext, StringComparison.Ordinal);
        Assert.Contains("not numeric wire-ID proof", MiPlayPhoneFirmwareSourceEvidence.OpenAckNameTableContext, StringComparison.Ordinal);
        Assert.Contains("SetPlaySource", MiPlayPhoneFirmwareSourceEvidence.RecoveredWireCommandIds, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", MiPlayPhoneFirmwareSourceEvidence.OfficialSetPlaySourceBuilder, StringComparison.Ordinal);
        Assert.Contains("JSONObject.putOpt", MiPlayPhoneFirmwareSourceEvidence.OfficialSetPlaySourcePayload, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionAllowsOfflinePayloadBuildButKeepsLiveBusinessFramesForbidden()
    {
        var decision = MiPlayPhoneFirmwareSourceEvidence.Evaluate(
            MiPlayPhoneFirmwareSourceEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanDesignLiveBusinessProbe);
        Assert.True(decision.CanBuildNonEmptySetPlaySource);
        Assert.Contains("source-side legacy MiPlay command stack", decision.StaticSourceConclusion, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", decision.MissingProof, StringComparison.Ordinal);
        Assert.Contains("SetPlaySource", decision.MissingProof, StringComparison.Ordinal);
        Assert.Contains("AppInfo/ServiceName/signature", decision.MissingProof, StringComparison.Ordinal);
        Assert.Contains("Offline construction", decision.Boundary, StringComparison.Ordinal);
        Assert.Contains("live non-empty 0x0040", decision.Boundary, StringComparison.Ordinal);
        Assert.Contains("open/media", decision.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedCompressedErofsCandidatesAreExtractedButFunctionLevelBoundaryRemains()
    {
        var snapshot = MiPlayPhoneFirmwareSourceEvidence.CreateCurrentSnapshot();

        Assert.False(snapshot.ErofsLayout3CompressedFilesRemainUnextracted);
        Assert.Contains("layout=3", MiPlayPhoneFirmwareSourceEvidence.ExtractionBoundary, StringComparison.Ordinal);
        Assert.Contains("function-level ordering", MiPlayPhoneFirmwareSourceEvidence.ExtractionBoundary, StringComparison.Ordinal);
    }
}