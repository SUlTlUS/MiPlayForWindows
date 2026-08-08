using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayNoMediaRtspProbePolicyTests
{
    [Fact]
    public void ReadyDecisionBuildsOnlyOneCmdOpenPayloadBehindStartedListener()
    {
        var decision = MiPlayNoMediaRtspProbePolicy.EvaluateOpenReadiness(
            new MiPlayNoMediaRtspOpenPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                RtspListenerStartedBeforeCmdOpen: true,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                MirrorMode: 1,
                NextCommandSequence: 4,
                NoMediaBoundary: true,
                Forbid0058: true));

        Assert.True(decision.CanSend);
        Assert.Equal(MiPlayProtocolConstants.OpenDeviceCommand, decision.Command);
        Assert.Equal((ushort)4, decision.Sequence);
        Assert.Equal("wfd://192.168.10.9:7236?mirrorMode=1", decision.PayloadText);
        Assert.Contains("exactly one", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("no 0x0058", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("no-media", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenReadinessRefusesUntilListenerAndBoundariesArePresent()
    {
        var decision = MiPlayNoMediaRtspProbePolicy.EvaluateOpenReadiness(
            new MiPlayNoMediaRtspOpenPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                RtspListenerStartedBeforeCmdOpen: false,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                MirrorMode: 1,
                NextCommandSequence: 4,
                NoMediaBoundary: true,
                Forbid0058: true));

        Assert.False(decision.CanSend);
        Assert.Contains("listener", decision.Reason, StringComparison.OrdinalIgnoreCase);

        var unsafeDecision = MiPlayNoMediaRtspProbePolicy.EvaluateOpenReadiness(
            new MiPlayNoMediaRtspOpenPrerequisites(
                MutualSafetyAuthVerified: true,
                SafetyDataSessionCandidateAvailable: true,
                RtspListenerStartedBeforeCmdOpen: true,
                SourceAddress: IPAddress.Parse("192.168.10.9"),
                SourcePort: MiPlayProtocolConstants.DefaultMediaPort,
                MirrorMode: 1,
                NextCommandSequence: 4,
                NoMediaBoundary: false,
                Forbid0058: true));

        Assert.False(unsafeDecision.CanSend);
        Assert.Contains("forbid media", unsafeDecision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRtspRequestEvidenceStopsAtParseableCallback()
    {
        var bytes = Encoding.ASCII.GetBytes(
            "OPTIONS rtsp://localhost/wfd1.0 RTSP/1.0\r\n" +
            "CSeq: 1\r\n" +
            "Content-Length: 0\r\n\r\n");

        Assert.True(MiPlayRtspRequestCodec.TryDecode(bytes, out var request, out var consumed));
        Assert.NotNull(request);
        Assert.Equal(bytes.Length, consumed);

        var decision = MiPlayNoMediaRtspProbePolicy.EvaluateFirstRtspRequest(request);

        Assert.True(decision.IsUsefulEvidence);
        Assert.Contains("opened the source WFD/RTSP control endpoint", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Stop here", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRtspRequestRejectsNonWfdTarget()
    {
        var bytes = Encoding.ASCII.GetBytes(
            "OPTIONS http://example.invalid/path RTSP/1.0\r\n" +
            "CSeq: 1\r\n" +
            "Content-Length: 0\r\n\r\n");

        Assert.True(MiPlayRtspRequestCodec.TryDecode(bytes, out var request, out _));
        Assert.NotNull(request);

        var decision = MiPlayNoMediaRtspProbePolicy.EvaluateFirstRtspRequest(request);

        Assert.False(decision.IsUsefulEvidence);
        Assert.Contains("not a WFD/RTSP target", decision.Reason, StringComparison.Ordinal);
    }
}