using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyClearSetPlaySourceAckProbePolicyTests
{
    [Fact]
    public void ReadyDecisionAllowsOnlyOneEmptyClearSetPlaySourceAfterLegacyAuth()
    {
        var decision = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(CompletePrerequisites());

        Assert.True(decision.CanSend);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, decision.Command);
        Assert.Equal((ushort)2, decision.Sequence);
        Assert.Equal(0, decision.PlaintextPayloadLength);
        Assert.Contains("clear-text", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0040", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x0041", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no 0x1400", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SafetyData", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesMissingStaticAndLegacyAuthPrerequisites()
    {
        var missingLegacyAuth = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { LegacyChallengeAcknowledged = false });
        Assert.False(missingLegacyAuth.CanSend);
        Assert.Contains("0x0028", missingLegacyAuth.Reason, StringComparison.OrdinalIgnoreCase);

        var missingModernSafetyBoundary = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { MpasModernSafetyCommandConstantsAbsentObserved = false });
        Assert.False(missingModernSafetyBoundary.CanSend);
        Assert.Contains("0x1400", missingModernSafetyBoundary.Reason, StringComparison.OrdinalIgnoreCase);

        var missingDispatch = MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { MpasExternalSetPlaySourceDispatchObserved = false });
        Assert.False(missingDispatch.CanSend);
        Assert.Contains("0x0040", missingDispatch.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesModernSafetyOrEncryptedOrMediaBoundaryExpansion()
    {
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { NoModernSafetyInfoOrSafetyAuth = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { NoSafetyDataEncryption = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { EmptyPayloadOnly = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidCmdOpen = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { Forbid0058 = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidAddMirror = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidRtsp = false }).CanSend);
        Assert.False(MiPlayLegacyClearSetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidPlaybackOrAudio = false }).CanSend);
    }

    [Fact]
    public void ClearFrameKeepsOuterSetPlaySourceCommandAndEmptyPayload()
    {
        var frameBytes = MiPlaySetPlaySourceAckProbe.ToCommandFrame(sequence: 2);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
        Assert.Equal((ushort)2, frame.Sequence);
        Assert.Empty(frame.Payload);
    }

    private static MiPlayLegacyClearSetPlaySourceAckPrerequisites CompletePrerequisites() =>
        new(
            LegacyChallengeAcknowledged: true,
            NativeVersionBootstrapSent: true,
            MpasModernSafetyCommandConstantsAbsentObserved: true,
            MpasExternalSetPlaySourceDispatchObserved: true,
            MpasAcknowledgesBeforePayloadParse: true,
            NextCommandSequence: 2,
            EmptyPayloadOnly: true,
            NoModernSafetyInfoOrSafetyAuth: true,
            NoSafetyDataEncryption: true,
            NoMediaBoundary: true,
            ForbidCmdOpen: true,
            Forbid0058: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidPlaybackOrAudio: true);
}
