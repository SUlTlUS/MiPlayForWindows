using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceAckProbePolicyTests
{
    [Fact]
    public void EmptyAckProbeKeepsOuterSetPlaySourceCommandAndSequence()
    {
        var cipher = new MiPlaySafetyDataSessionCipher(new byte[16], new byte[16]);
        var frameBytes = MiPlaySetPlaySourceAckProbe.ToSafetyDataCommandFrame(4, cipher);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
        Assert.Equal((ushort)4, frame.Sequence);
        Assert.NotEmpty(frame.Payload);
        Assert.True(cipher.TryDecryptVersion1(frame.Payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Empty(decoded.Plaintext);
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(decoded.Plaintext));
    }

    [Fact]
    public void ReadyDecisionAllowsOnlyOneEmptySetPlaySourceFrameAfterMutualSafetyAuth()
    {
        var decision = MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(CompletePrerequisites());

        Assert.True(decision.CanSend);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, decision.Command);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal(0, decision.PlaintextPayloadLength);
        Assert.Contains("exactly one", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0040", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observe for 0x0041", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no JSON source identity", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("AddMirror", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessRejectsMissingStaticEvidenceOrNonEmptyPayloadPermission()
    {
        var missingDispatch = MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { MpasExternalSetPlaySourceDispatchObserved = false });

        Assert.False(missingDispatch.CanSend);
        Assert.Contains("0x0040", missingDispatch.Reason, StringComparison.OrdinalIgnoreCase);

        var missingAckBoundary = MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { MpasAcknowledgesBeforePayloadParse = false });

        Assert.False(missingAckBoundary.CanSend);
        Assert.Contains("ACK-before-payload-parse", missingAckBoundary.Reason, StringComparison.Ordinal);

        var nonEmptyPayloadAllowed = MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { EmptyPayloadOnly = false });

        Assert.False(nonEmptyPayloadAllowed.CanSend);
        Assert.Contains("empty plaintext", nonEmptyPayloadAllowed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessRejectsAnyWiderBusinessOrMediaBoundary()
    {
        Assert.False(MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidCmdOpen = false }).CanSend);
        Assert.False(MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { Forbid0058 = false }).CanSend);
        Assert.False(MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidAddMirror = false }).CanSend);
        Assert.False(MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidRtsp = false }).CanSend);
        Assert.False(MiPlaySetPlaySourceAckProbePolicy.EvaluateAckReadiness(
            CompletePrerequisites() with { ForbidPlaybackOrAudio = false }).CanSend);
    }

    private static MiPlaySetPlaySourceAckPrerequisites CompletePrerequisites() =>
        new(
            MutualSafetyAuthVerified: true,
            SafetyDataSessionCandidateAvailable: true,
            MpasExternalSetPlaySourceDispatchObserved: true,
            MpasAcknowledgesBeforePayloadParse: true,
            NextCommandSequence: 4,
            EmptyPayloadOnly: true,
            NoMediaBoundary: true,
            ForbidCmdOpen: true,
            Forbid0058: true,
            ForbidAddMirror: true,
            ForbidRtsp: true,
            ForbidPlaybackOrAudio: true);
}