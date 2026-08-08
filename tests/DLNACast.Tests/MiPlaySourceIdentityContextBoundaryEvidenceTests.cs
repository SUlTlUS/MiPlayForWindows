using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySourceIdentityContextBoundaryEvidenceTests
{
    [Fact]
    public void CurrentBoundaryProvesTargetContextStaticIdentitySenderBuilderAndNativeNoResetNegativeButNotBridgeOrOrdering()
    {
        var snapshot = MiPlaySourceIdentityContextBoundaryEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.LegacyClearGetDeviceInfoAcknowledged);
        Assert.True(snapshot.LegacyDeviceInfoPayloadParsed);
        Assert.True(snapshot.TargetModelObserved);
        Assert.True(snapshot.TargetRomVersionObserved);
        Assert.True(snapshot.TargetAudioSupportObserved);
        Assert.True(snapshot.LocalSetLocalDeviceInfoJsonShapeAvailable);
        Assert.True(snapshot.AndroidAppInfoAvailable);
        Assert.True(snapshot.AndroidServiceNameAvailable);
        Assert.True(snapshot.AndroidSignatureAvailable);
        Assert.True(snapshot.AllExtractedPhoneDexIdentityTraceBuilt);
        Assert.True(snapshot.PackageUtilAppInfoGenerationRecovered);
        Assert.True(snapshot.PackageSignatureSha256FingerprintRecovered);
        Assert.True(snapshot.ServiceNameMergeStringRecovered);
        Assert.True(snapshot.AppInfoServiceNameCmdSessionControlAllDexIntersectionEmpty);
        Assert.True(snapshot.SetPlaySourcePayloadShapeLocalized);
        Assert.True(snapshot.OfficialSetPlaySourcePayloadBuilderLocalized);
        Assert.False(snapshot.SourceIdentityToLegacy8899BridgeLocalized);
        Assert.True(snapshot.NativeNoResetOfficialJsonSetPlaySourceRejected);
        Assert.True(snapshot.SourceContextOrOrderingAfterSetPlaySourceUnresolved);
        Assert.Contains("model=LX06", MiPlaySourceIdentityContextBoundaryEvidence.ProvenTargetContext, StringComparison.Ordinal);
        Assert.Contains("AppInfo", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedAndroidSourceIdentity, StringComparison.Ordinal);
        Assert.Contains("phone_source_all_dex_ref_identity_trace", MiPlaySourceIdentityContextBoundaryEvidence.AllDexIdentityTraceArtifact, StringComparison.Ordinal);
        Assert.Contains("getApkContentsSigners", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedPackageSignature, StringComparison.Ordinal);
        Assert.Contains("platformType=1", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedAppInfoGeneration, StringComparison.Ordinal);
        Assert.Contains("packageName:name", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedServiceNameMergeString, StringComparison.Ordinal);
        Assert.Contains("Cmd/AppInfo=0", MiPlaySourceIdentityContextBoundaryEvidence.AllDexIdentityIntersectionEvidence, StringComparison.Ordinal);
        Assert.Contains("0x0040", MiPlaySourceIdentityContextBoundaryEvidence.MissingSetPlaySourcePayloadShape, StringComparison.Ordinal);
        Assert.Contains("ref_channel", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedReceiverSetPlaySourcePayload, StringComparison.Ordinal);
        Assert.Contains("StatsUtils.setPlaySource", MiPlaySourceIdentityContextBoundaryEvidence.LocalizedOfficialSetPlaySourceBuilder, StringComparison.Ordinal);
        Assert.Contains("native-no-reset official JSON 0x0040", MiPlaySourceIdentityContextBoundaryEvidence.NativeNoResetOfficialJsonNegativeResult, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesLiveSetPlaySourceUntilLegacyBridgeAndStateTransitionAreKnown()
    {
        var decision = MiPlaySourceIdentityContextBoundaryEvidence.EvaluateNextSetPlaySourceProbe(
            MiPlaySourceIdentityContextBoundaryEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanDesignLiveSetPlaySourceProbe);
        Assert.Contains("AppInfo", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("ServiceName", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SHA-256", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source-side JSON builder", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("legacy 8899", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no caller intersection", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connectCmdSession2", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setLyraInfo", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source/session context or ordering", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordering/session state", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeAloneIsNoLongerEnoughAfterNativeNoResetOfficialJsonNegativeResult()
    {
        var bridgeOnly = MiPlaySourceIdentityContextBoundaryEvidence.CreateCurrentSnapshot() with
        {
            SourceIdentityToLegacy8899BridgeLocalized = true,
        };

        var decision = MiPlaySourceIdentityContextBoundaryEvidence.EvaluateNextSetPlaySourceProbe(bridgeOnly);

        Assert.False(decision.CanDesignLiveSetPlaySourceProbe);
        Assert.Contains("bridge alone is no longer enough", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("native-no-reset official JSON", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("command ordering", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("session context", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsOnlyAfterBridgeOrderingStateAndForbiddenBoundariesHold()
    {
        var ready = MiPlaySourceIdentityContextBoundaryEvidence.CreateCurrentSnapshot() with
        {
            SourceIdentityToLegacy8899BridgeLocalized = true,
            SourceContextOrOrderingAfterSetPlaySourceUnresolved = false,
        };

        var decision = MiPlaySourceIdentityContextBoundaryEvidence.EvaluateNextSetPlaySourceProbe(ready);

        Assert.True(decision.CanDesignLiveSetPlaySourceProbe);
        Assert.Contains("one-frame", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0058", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("media", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundaryExpansionStillRefusesEvenWithBridgeAndOrderingEvidence()
    {
        var unsafeSnapshot = MiPlaySourceIdentityContextBoundaryEvidence.CreateCurrentSnapshot() with
        {
            SourceIdentityToLegacy8899BridgeLocalized = true,
            SourceContextOrOrderingAfterSetPlaySourceUnresolved = false,
            ForbidCmdOpen = false,
        };

        var decision = MiPlaySourceIdentityContextBoundaryEvidence.EvaluateNextSetPlaySourceProbe(unsafeSnapshot);

        Assert.False(decision.CanDesignLiveSetPlaySourceProbe);
        Assert.Contains("0x0058/open/AddMirror/RTSP/media", decision.Reason, StringComparison.Ordinal);
    }
}