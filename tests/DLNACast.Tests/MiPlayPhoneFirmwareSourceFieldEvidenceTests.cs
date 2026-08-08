using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPhoneFirmwareSourceFieldEvidenceTests
{
    [Fact]
    public void RefChannelValuesMatchRecoveredGetRefChannelTable()
    {
        Assert.Equal("controlcenter", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(0));
        Assert.Equal("nearfield", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(1));
        Assert.Equal("xiaoai_phone", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(2));
        Assert.Equal("farfield", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(3));
        Assert.Equal("lockscreen", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(4));
        Assert.Equal("notification", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(5));
        Assert.Equal("playpage", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(6));
        Assert.Equal("world", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(7));
        Assert.Equal("relay_card", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(8));
        Assert.Equal("nfc", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(9));
        Assert.Equal("controlcenter", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefChannelOrDefault(99));
    }

    [Fact]
    public void RefContentPackageMapMatchesRecoveredSetRefContentConstants()
    {
        Assert.Equal("music_miui", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.miui.player"));
        Assert.Equal("music_wangyiyun", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.netease.cloudmusic"));
        Assert.Equal("music_qq", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.tencent.qqmusic"));
        Assert.Equal("music_kugou", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.kugou.android"));
        Assert.Equal("music_kuwo", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("cn.kuwo.player"));
        Assert.Equal("fm_himalaya", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.ximalaya.ting.android"));
        Assert.Equal("fm_qingting", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("fm.qingting.qtradio"));
        Assert.Equal("fm_lizhi", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.yibasan.lizhifm"));
        Assert.Equal("fm_dedao", MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("com.luojilab.player"));
        Assert.Equal(string.Empty, MiPlayPhoneFirmwareSourceFieldEvidence.GetRefContentOrEmpty("example.unknown"));
    }

    [Fact]
    public void RefFunctionValuesAreRecoveredFromStatsUtils()
    {
        Assert.Contains("single_room", MiPlayPhoneFirmwareSourceFieldEvidence.RefFunctionValues);
        Assert.Contains("multi_room", MiPlayPhoneFirmwareSourceFieldEvidence.RefFunctionValues);
        Assert.Contains("stereo", MiPlayPhoneFirmwareSourceFieldEvidence.RefFunctionValues);
        Assert.Equal(3, MiPlayPhoneFirmwareSourceFieldEvidence.RefFunctionValues.Count);
    }

    [Fact]
    public void SnapshotSeparatesLegacyCommandSessionFromLyraServiceNamePath()
    {
        var snapshot = MiPlayPhoneFirmwareSourceFieldEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.RefChannelFieldObserved);
        Assert.True(snapshot.RefChannelGetterFeedsSetPlaySource);
        Assert.True(snapshot.TopActiveSessionChangeUpdatesRefContentAndSetPlaySource);
        Assert.True(snapshot.StartCommandChannelCreatesLegacyCmdSessionControl);
        Assert.True(snapshot.OptionalLyraSecretKeyCommandObserved);
        Assert.True(snapshot.SecretKeyCommandCarriesLyraKeyMaterialOnly);
        Assert.True(snapshot.NativeConnectCmdSession2SecretKeyBridgeRecovered);
        Assert.True(snapshot.NativeSetLyraInfoParsesSecretKeyCommandOnly);
        Assert.True(snapshot.NativeSetPlaySourceCommandId0040Recovered);
        Assert.True(snapshot.StartCommandChannelHasNoServiceNameOrAppInfoReferences);
        Assert.True(snapshot.LyraContinuityServiceNamePathObservedSeparately);
        Assert.True(snapshot.CmdSessionSuccessTriggersGetDeviceInfo);
        Assert.Contains("CmdSessionControl", MiPlayPhoneFirmwareSourceFieldEvidence.LegacyCommandChannelPath, StringComparison.Ordinal);
        Assert.Contains("MiDevice.getMac", MiPlayPhoneFirmwareSourceFieldEvidence.LegacyCommandSessionInputs, StringComparison.Ordinal);
        Assert.Contains("SecretKeyCommand", MiPlayPhoneFirmwareSourceFieldEvidence.LegacyCommandSessionInputs, StringComparison.Ordinal);
        Assert.Contains("ContinuityChannelManager", MiPlayPhoneFirmwareSourceFieldEvidence.SeparateLyraServiceNamePath, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionAllowsOfflineExamplesOnlyAndKeepsLiveProbeForbidden()
    {
        var decision = MiPlayPhoneFirmwareSourceFieldEvidence.Evaluate(
            MiPlayPhoneFirmwareSourceFieldEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanBuildOfflineSetPlaySourcePayloadExamples);
        Assert.False(decision.CanAuthorizeLiveSetPlaySourceProbe);
        Assert.Contains("MiDevice.ref_channel", decision.SourceFieldConclusion, StringComparison.Ordinal);
        Assert.Contains("ref_content package mapping", decision.SourceFieldConclusion, StringComparison.Ordinal);
        Assert.Contains("SecretKeyCommand", decision.SourceFieldConclusion, StringComparison.Ordinal);
        Assert.Contains("cmd 0x40", decision.SourceFieldConclusion, StringComparison.Ordinal);
        Assert.Contains("wlan0ip/authKey/streamKey/streamIV", decision.SourceFieldConclusion, StringComparison.Ordinal);
        Assert.Contains("no current targeted DEX xref", decision.MissingBridge, StringComparison.Ordinal);
        Assert.Contains("optional Lyra key JSON bridge", decision.MissingBridge, StringComparison.Ordinal);
        Assert.Contains("live non-empty 0x0040", decision.Boundary, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Boundary, StringComparison.Ordinal);
    }
}