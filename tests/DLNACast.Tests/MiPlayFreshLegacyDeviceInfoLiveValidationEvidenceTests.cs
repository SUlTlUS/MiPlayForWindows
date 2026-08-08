using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyDeviceInfoLiveValidationEvidenceTests
{
    [Fact]
    public void CapturesAuthorizedFreshLegacyDeviceInfoProgression()
    {
        var evidence = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot();
        var decision = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.EvaluateLiveResult(evidence);

        Assert.True(decision.CanProceed);
        Assert.True(evidence.NoOtherOutboundFrames);
        Assert.True(evidence.StoppedImmediatelyAfterPositiveObservation);
        Assert.Equal("192.168.10.9", MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReceiverAddress);
        Assert.Equal("192.168.10.58", MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.SourceAddress);
        Assert.Equal(50_538, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.SourcePort);
        Assert.Contains("onDeviceInfo progression", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not authorize", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactOutboundFrameMatchesTheOfflineGoldenVector()
    {
        var plan = MiPlayFreshLegacyReceiverBootstrapPlanner.CreateOfflinePlan(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.GetDeviceInfoSequence);

        Assert.Equal(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.DeviceInfoAcknowledgementCommand);
        Assert.Equal(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.DeviceInfoAcknowledgementSequence, plan.GetDeviceInfoRequestSequence);
        Assert.Equal(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.DeviceInfoAcknowledgementPayloadLength, plan.DeviceInfoPayload.Length);
        Assert.Equal(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.DeviceInfoAcknowledgementFrameLength, plan.GetDeviceInfoAcknowledgementFrame.Length);
        Assert.Equal(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.DeviceInfoAcknowledgementFrameSha256, plan.GetDeviceInfoAcknowledgementFrameSha256);
        Assert.Equal(20, plan.DeviceInfoProfile.ToOrderedFields().Count);
    }

    [Fact]
    public void SourceAdvancedOnlyAfterTheDeviceInfoAcknowledgement()
    {
        Assert.Equal((ushort)0x0001, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.GetDeviceInfoSequence);
        Assert.Equal((ushort)0x0002, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoSequence);
        Assert.Equal(31, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoPayloadLength);
        Assert.Equal(19, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoPayloadLength);
        Assert.Equal(
            "DB75703B2F77B6BA8A63D0611104DA6DE1266A144B00D985B905B28CC9A23FC6",
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoFrameSha256);
    }

    [Fact]
    public void StaticDexPayloadReconstructionMatchesTheObservedFrameHash()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0);
        var frame = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame();

        Assert.Equal("{\"isSameAccount\":0}", Encoding.UTF8.GetString(payload));
        Assert.Equal(19, payload.Length);
        Assert.True(MiPlayLocalDeviceInfoPayloadCodec.TryDecodeIsSameAccount(payload, out var value));
        Assert.Equal(0, value);
        Assert.Equal(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoFrameSha256,
            Convert.ToHexString(SHA256.HashData(frame)));
        Assert.Equal(0x2b76c0, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.SetLocalDeviceInfoSameAccountMethodAddress);
        Assert.Equal(0x26ee20, MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.IsSameAccountToJsonMethodAddress);
    }

    [Fact]
    public void SameAccountDecoderRejectsStringExtraFieldAndMalformedJson()
    {
        Assert.False(MiPlayLocalDeviceInfoPayloadCodec.TryDecodeIsSameAccount(
            "{\"isSameAccount\":\"0\"}"u8,
            out _));
        Assert.False(MiPlayLocalDeviceInfoPayloadCodec.TryDecodeIsSameAccount(
            "{\"isSameAccount\":0,\"extra\":1}"u8,
            out _));
        Assert.False(MiPlayLocalDeviceInfoPayloadCodec.TryDecodeIsSameAccount(
            "{\"isSameAccount\":"u8,
            out _));

        var differentValue = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            3,
            MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(1));
        Assert.NotEqual(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.AdvancedSetLocalDeviceInfoFrameSha256,
            Convert.ToHexString(SHA256.HashData(differentValue)));
    }

    [Fact]
    public void MissingProgressionOrExpandedOutboundBoundaryInvalidatesEvidence()
    {
        var current = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.False(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            current with { ObservedAdvancedSetLocalDeviceInfoAfterAcknowledgement = false }).CanProceed);
        Assert.False(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            current with { NoOtherOutboundFrames = false }).CanProceed);
        Assert.False(MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.EvaluateLiveResult(
            current with { StoppedImmediatelyAfterPositiveObservation = false }).CanProceed);
    }
}
