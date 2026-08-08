using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayNoMediaRtspOpenPrerequisites(
    bool MutualSafetyAuthVerified,
    bool SafetyDataSessionCandidateAvailable,
    bool RtspListenerStartedBeforeCmdOpen,
    IPAddress SourceAddress,
    int SourcePort,
    int MirrorMode,
    ushort NextCommandSequence,
    bool NoMediaBoundary,
    bool Forbid0058);

public sealed record MiPlayNoMediaRtspOpenDecision(
    bool CanSend,
    string Reason,
    string? PayloadText = null,
    ushort? Command = null,
    ushort? Sequence = null);

public sealed record MiPlayNoMediaRtspFirstRequestDecision(bool IsUsefulEvidence, string Reason);

/// <summary>
/// Safety gate for the bounded live validation that only checks whether a Xiaomi
/// receiver opens a WFD/RTSP control connection after Cmd_Open. It does not
/// authorize media, RTP, playback, SETUP/PLAY replies, or audio frames.
/// </summary>
public static class MiPlayNoMediaRtspProbePolicy
{
    public static MiPlayNoMediaRtspOpenDecision EvaluateOpenReadiness(
        MiPlayNoMediaRtspOpenPrerequisites prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites.SourceAddress);

        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SafetyDataSessionCandidateAvailable)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "No verified SafetyData session candidate is available for Cmd_Open.");
        }

        if (!prerequisites.RtspListenerStartedBeforeCmdOpen)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "The no-media RTSP/WFD listener must be started before Cmd_Open is sent.");
        }

        if (prerequisites.SourceAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "The source WFD endpoint must use an IPv4 address.");
        }

        if (prerequisites.SourcePort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "The source WFD endpoint port is outside the TCP port range.");
        }

        if (prerequisites.MirrorMode < 0)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "mirrorMode must be non-negative.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "The next command sequence is not initialized.");
        }

        if (!prerequisites.NoMediaBoundary)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "The probe boundary must explicitly forbid media, RTP, playback, and audio frames.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlayNoMediaRtspOpenDecision(false, "0x0058 must remain forbidden for this Cmd_Open RTSP callback validation.");
        }

        var request = new MiPlayOpenDeviceRequest(
            prerequisites.SourceAddress,
            prerequisites.SourcePort,
            prerequisites.MirrorMode);
        return new MiPlayNoMediaRtspOpenDecision(
            true,
            "Ready to send exactly one SafetyData-wrapped Cmd_Open 0x0000 after the no-media RTSP/WFD listener is already active; stop after the first RTSP request and send no 0x0058, media, RTP, playback, or audio frames.",
            request.ToPayloadText(),
            MiPlayProtocolConstants.OpenDeviceCommand,
            prerequisites.NextCommandSequence);
    }

    public static MiPlayNoMediaRtspFirstRequestDecision EvaluateFirstRtspRequest(
        MiPlayRtspRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version != new Version(1, 0))
        {
            return new MiPlayNoMediaRtspFirstRequestDecision(false, "The callback is not RTSP/1.0.");
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return new MiPlayNoMediaRtspFirstRequestDecision(false, "The RTSP method is empty.");
        }

        if (!request.RequestTarget.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) &&
            !request.RequestTarget.StartsWith("wfd://", StringComparison.OrdinalIgnoreCase) &&
            request.RequestTarget != "*")
        {
            return new MiPlayNoMediaRtspFirstRequestDecision(false, "The callback target is not a WFD/RTSP target.");
        }

        return new MiPlayNoMediaRtspFirstRequestDecision(
            true,
            "The receiver opened the source WFD/RTSP control endpoint and sent a parseable first RTSP request. Stop here for the no-media validation.");
    }
}