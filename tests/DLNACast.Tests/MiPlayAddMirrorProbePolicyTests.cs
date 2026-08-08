using System.Net;
using System.Net.Sockets;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayAddMirrorProbePolicyTests
{
    [Fact]
    public void RequestBuildsRecoveredLocalAddMirrorPayload()
    {
        var request = new MiPlayAddMirrorRequest(IPAddress.Parse("192.168.10.9"));

        Assert.Equal("192.168.10.9:7236&from:192.168.10.9&islocal:1", request.ToPayloadText());
        Assert.Equal(45, request.ToPayloadBytes().Length);
    }

    [Fact]
    public void RequestRejectsNonIpv4SourceAddress()
    {
        var request = new MiPlayAddMirrorRequest(IPAddress.IPv6Loopback);

        Assert.Throws<NotSupportedException>(() => request.ToPayloadText());
    }

    [Fact]
    public void ReadyDecisionAllowsOnlyOneAddMirrorFrameAfterMutualSafetyAuth()
    {
        var decision = MiPlayAddMirrorProbePolicy.EvaluateAddMirrorReadiness(
            new MiPlayAddMirrorPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                NextCommandSequence: 4,
                NoMediaBoundary: true,
                ForbidCmdOpen: true,
                Forbid0058: true));

        Assert.True(decision.CanSend);
        Assert.Equal(MiPlayProtocolConstants.AddMirrorCommand, decision.Command);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal("192.168.10.9:7236&from:192.168.10.9&islocal:1", decision.PayloadText);
        Assert.Contains("exactly one", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x002e", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observe for 0x002f", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no Cmd_Open", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessRefusesVariablePortOrOpenPermission()
    {
        var variablePort = MiPlayAddMirrorProbePolicy.EvaluateAddMirrorReadiness(
            new MiPlayAddMirrorPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: 7237,
                NextCommandSequence: 4,
                NoMediaBoundary: true,
                ForbidCmdOpen: true,
                Forbid0058: true));

        Assert.False(variablePort.CanSend);
        Assert.Contains("7236", variablePort.Reason, StringComparison.Ordinal);

        var permitsOpen = MiPlayAddMirrorProbePolicy.EvaluateAddMirrorReadiness(
            new MiPlayAddMirrorPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                NextCommandSequence: 4,
                NoMediaBoundary: true,
                ForbidCmdOpen: false,
                Forbid0058: true));

        Assert.False(permitsOpen.CanSend);
        Assert.Contains("Cmd_Open", permitsOpen.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyDataFrameKeepsOuterAddMirrorCommandAndSequence()
    {
        var cipher = new MiPlaySafetyDataSessionCipher(new byte[16], new byte[16]);
        var frameBytes = new MiPlayAddMirrorRequest(IPAddress.Parse("192.168.10.9"))
            .ToSafetyDataCommandFrame(4, cipher);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal(MiPlayProtocolConstants.AddMirrorCommand, frame.Command);
        Assert.Equal((ushort)4, frame.Sequence);
        Assert.NotEqual("192.168.10.9:7236&from:192.168.10.9&islocal:1", Encoding.UTF8.GetString(frame.Payload));
        Assert.True(cipher.TryDecryptVersion1(frame.Payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal("192.168.10.9:7236&from:192.168.10.9&islocal:1", Encoding.UTF8.GetString(decoded.Plaintext));
    }
}