using System.Net;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayAddMirrorPrerequisites(
    bool MutualSafetyAuthVerified,
    bool SafetyDataSessionCandidateAvailable,
    IPAddress SourceAddress,
    int SourcePort,
    ushort NextCommandSequence,
    bool NoMediaBoundary,
    bool ForbidCmdOpen,
    bool Forbid0058);

public sealed record MiPlayAddMirrorDecision(
    bool CanSend,
    string Reason,
    string? PayloadText = null,
    ushort? Command = null,
    ushort? Sequence = null);

/// <summary>
/// Safety gate for the first bounded AddMirror-only validation on LX06/S12.
/// It authorizes at most one SafetyData-wrapped 0x002e frame and then observation
/// for 0x002f. It does not authorize Cmd_Open, 0x0058, RTSP, media, playback, or audio.
/// </summary>
public static class MiPlayAddMirrorProbePolicy
{
    public static MiPlayAddMirrorDecision EvaluateAddMirrorReadiness(
        MiPlayAddMirrorPrerequisites prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites.SourceAddress);

        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlayAddMirrorDecision(false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SafetyDataSessionCandidateAvailable)
        {
            return new MiPlayAddMirrorDecision(false, "No verified SafetyData session candidate is available for Cmd_AddMirror.");
        }

        if (prerequisites.SourceAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return new MiPlayAddMirrorDecision(false, "The AddMirror source endpoint must use an IPv4 address.");
        }

        if (prerequisites.SourcePort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return new MiPlayAddMirrorDecision(false, "The AddMirror source endpoint port is outside the TCP port range.");
        }

        if (prerequisites.SourcePort != MiPlayProtocolConstants.DefaultMediaPort)
        {
            return new MiPlayAddMirrorDecision(false, "The recovered LX06 local AddMirror helper hardcodes/defaults the source endpoint port to 7236; do not vary it for the first live probe.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayAddMirrorDecision(false, "The next command sequence is not initialized.");
        }

        if (!prerequisites.NoMediaBoundary)
        {
            return new MiPlayAddMirrorDecision(false, "The probe boundary must explicitly forbid media, RTSP, playback, and audio frames.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlayAddMirrorDecision(false, "Cmd_Open 0x0000 must remain forbidden for the AddMirror-only validation.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlayAddMirrorDecision(false, "0x0058 must remain forbidden for the AddMirror-only validation.");
        }

        var request = new MiPlayAddMirrorRequest(
            prerequisites.SourceAddress,
            prerequisites.SourcePort);
        return new MiPlayAddMirrorDecision(
            true,
            "Ready to send exactly one SafetyData-wrapped Cmd_AddMirror 0x002e with the recovered LX06 local payload and then only observe for 0x002f; send no Cmd_Open, 0x0058, RTSP, media, playback, audio, retry, or fallback control frame.",
            request.ToPayloadText(),
            MiPlayProtocolConstants.AddMirrorCommand,
            prerequisites.NextCommandSequence);
    }
}