using System.Net;

namespace DLNACast.Core.MiPlay;

public enum MiPlayLegacyPlaybackControlPhase
{
    Created,
    AwaitingIdentityRefreshAcknowledgements,
    AwaitingAccountAcknowledgement,
    AwaitingMirrorModeAcknowledgement,
    AwaitingHeartbeatAcknowledgement,
    AwaitingOpenPrerequisites,
    OpenPrepared,
    Stopped,
}

public sealed record MiPlayLegacyPlaybackOpenPrerequisites(
    bool TcpListenerBound,
    bool UdpTimerResponderBound,
    int ReverseConnectionCapacity,
    bool AacMpegTsPipelineReady);

public sealed record MiPlayLegacyPlaybackControlTransition(
    bool Accepted,
    MiPlayLegacyPlaybackControlPhase Phase,
    IReadOnlyList<MiPlayLegacyAudioSourceWrite> OutboundWrites,
    bool OpenPrepared,
    bool SafeForNetworkUse,
    string Boundary);

/// <summary>
/// Pure continuation from a completed legacy source bootstrap to the captured
/// SetPlaySource/Open boundary. It never emits AddMirror or media bytes.
/// </summary>
public sealed class MiPlayLegacyPlaybackControlSession
{
    public const ushort SourceNameSequence = 8;
    public const ushort GetDeviceInfoSequence = 9;
    public const ushort IsSameAccountSequence = 10;
    public const ushort GetMirrorModeSequence = 11;
    public const ushort HeartbeatSequence = 12;
    public const ushort SetPlaySourceSequence = 13;
    public const ushort OpenSequence = 14;

    private readonly string sourceName;
    private readonly IPAddress sourceAddress;
    private readonly int listenerPort;
    private bool sourceNameAcknowledged;
    private bool deviceInfoAcknowledged;
    private MiPlayLegacyPlaybackControlPhase phase = MiPlayLegacyPlaybackControlPhase.Created;

    public MiPlayLegacyPlaybackControlSession(
        MiPlayLegacyAudioSourceSession completedBootstrap,
        string sourceName,
        IPAddress sourceAddress,
        int listenerPort)
    {
        ArgumentNullException.ThrowIfNull(completedBootstrap);
        if (completedBootstrap.Phase != MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete)
        {
            throw new InvalidOperationException("Playback continuation requires a completed legacy source bootstrap.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(sourceAddress);
        if (sourceAddress.GetAddressBytes().Length != 4)
        {
            throw new NotSupportedException("The captured legacy playback route supports IPv4 only.");
        }
        if (listenerPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(listenerPort));
        }

        this.sourceName = sourceName;
        this.sourceAddress = sourceAddress;
        this.listenerPort = listenerPort;
    }

    public MiPlayLegacyPlaybackControlPhase Phase => phase;

    public MiPlayLegacyPlaybackControlTransition Start()
    {
        if (phase != MiPlayLegacyPlaybackControlPhase.Created)
        {
            return Reject("The playback continuation can be started only once.");
        }

        phase = MiPlayLegacyPlaybackControlPhase.AwaitingIdentityRefreshAcknowledgements;
        return Accept(
            [
                Write(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, SourceNameSequence,
                    MiPlayLocalDeviceInfoPayloadCodec.EncodeLegacySourceNameOnly(sourceName)),
                Write(MiPlayProtocolConstants.GetDeviceInfoCommand, GetDeviceInfoSequence, []),
            ],
            "Prepared the captured playback-time sourceName refresh followed by getDeviceInfo.");
    }

    public MiPlayLegacyPlaybackControlTransition ProcessInbound(ReadOnlySpan<byte> frameBytes)
    {
        if (phase is MiPlayLegacyPlaybackControlPhase.Created or
            MiPlayLegacyPlaybackControlPhase.OpenPrepared or
            MiPlayLegacyPlaybackControlPhase.Stopped)
        {
            return Reject("The playback continuation is not accepting inbound frames in its current phase.");
        }
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed) ||
            frame is null || consumed != frameBytes.Length)
        {
            return Stop("Inbound bytes are not exactly one complete MiPlay command frame.");
        }
        if (frame.Command == MiPlayProtocolConstants.NotifyCommand)
        {
            return Accept([], "Observed an interleaved receiver notification without advancing playback control.");
        }

        return phase switch
        {
            MiPlayLegacyPlaybackControlPhase.AwaitingIdentityRefreshAcknowledgements => ProcessIdentity(frame),
            MiPlayLegacyPlaybackControlPhase.AwaitingAccountAcknowledgement => ProcessAccount(frame),
            MiPlayLegacyPlaybackControlPhase.AwaitingMirrorModeAcknowledgement => ProcessMirrorMode(frame),
            MiPlayLegacyPlaybackControlPhase.AwaitingHeartbeatAcknowledgement => ProcessHeartbeat(frame),
            MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites =>
                Stop("No control acknowledgement is expected between SetPlaySource and Open readiness."),
            _ => Stop("Unsupported playback-control phase."),
        };
    }

    public MiPlayLegacyPlaybackControlTransition PrepareOpen(
        MiPlayLegacyPlaybackOpenPrerequisites prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites);
        if (phase != MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites)
        {
            return Reject("Open can be prepared only after the heartbeat acknowledgement and SetPlaySource write.");
        }
        if (!prerequisites.TcpListenerBound ||
            !prerequisites.UdpTimerResponderBound ||
            prerequisites.ReverseConnectionCapacity < 3 ||
            !prerequisites.AacMpegTsPipelineReady)
        {
            return Reject("Open remains gated on a bound TCP listener, bound UDP timer responder, three reverse connections, and a ready AAC MPEG-TS pipeline.");
        }

        var open = new MiPlayOpenDeviceRequest(sourceAddress, listenerPort).ToCommandFrame(OpenSequence);
        phase = MiPlayLegacyPlaybackControlPhase.OpenPrepared;
        return Accept(
            [new MiPlayLegacyAudioSourceWrite([open])],
            "Prepared the captured NUL-terminated Open frame. No AddMirror or Open acknowledgement is expected.");
    }

    private MiPlayLegacyPlaybackControlTransition ProcessIdentity(MiPlayCommandFrame frame)
    {
        if (frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
        {
            if (sourceNameAcknowledged || frame.Sequence != SourceNameSequence || frame.Payload.Length != 0)
            {
                return Stop("Playback-time sourceName acknowledgement must be empty sequence 8 and occur once.");
            }
            sourceNameAcknowledged = true;
        }
        else if (frame.Command == MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
        {
            if (deviceInfoAcknowledged || frame.Sequence != GetDeviceInfoSequence ||
                !MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(frame.Payload, out _, out var consumed) ||
                consumed != frame.Payload.Length)
            {
                return Stop("Playback-time device info must be parseable sequence 9 and occur once.");
            }
            deviceInfoAcknowledged = true;
        }
        else
        {
            return Stop("Expected the playback-time 0x0059 sequence 8 and 0x001f sequence 9 pair.");
        }

        if (!sourceNameAcknowledged || !deviceInfoAcknowledged)
        {
            return Accept([], "Accepted one half of the playback-time identity refresh pair.");
        }

        phase = MiPlayLegacyPlaybackControlPhase.AwaitingAccountAcknowledgement;
        return Accept(
            [Write(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, IsSameAccountSequence,
                MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0))],
            "Identity refresh is complete; prepared isSameAccount=0 sequence 10.");
    }

    private MiPlayLegacyPlaybackControlTransition ProcessAccount(MiPlayCommandFrame frame)
    {
        if (frame.Command != MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand ||
            frame.Sequence != IsSameAccountSequence || frame.Payload.Length != 0)
        {
            return Stop("Expected the empty account acknowledgement sequence 10.");
        }

        phase = MiPlayLegacyPlaybackControlPhase.AwaitingMirrorModeAcknowledgement;
        return Accept(
            [Write(MiPlayProtocolConstants.GetMirrorModeCommand, GetMirrorModeSequence, [])],
            "Account context was accepted; prepared getMirrorMode sequence 11.");
    }

    private MiPlayLegacyPlaybackControlTransition ProcessMirrorMode(MiPlayCommandFrame frame)
    {
        if (frame.Command != MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand ||
            frame.Sequence != GetMirrorModeSequence ||
            !MiPlayLegacyStatusScalarCodec.TryDecode(frame.Payload, out var mode) ||
            !MiPlayLegacyAudioSourceSession.IsObservedMirrorMode(mode))
        {
            return Stop("Expected an observed mode-1 or mode-2 mirror acknowledgement sequence 11.");
        }

        phase = MiPlayLegacyPlaybackControlPhase.AwaitingHeartbeatAcknowledgement;
        return Accept(
            [Write(MiPlayProtocolConstants.HeartbeatCommand, HeartbeatSequence, [])],
            $"Mode {mode} is verified; prepared heartbeat sequence 12.");
    }

    private MiPlayLegacyPlaybackControlTransition ProcessHeartbeat(MiPlayCommandFrame frame)
    {
        if (frame.Command != MiPlayProtocolConstants.HeartbeatAcknowledgementCommand ||
            frame.Sequence != HeartbeatSequence || frame.Payload.Length != 0)
        {
            return Stop("Expected the empty same-sequence heartbeat acknowledgement 0x001b sequence 12.");
        }

        var setPlaySource = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            SetPlaySourceSequence,
            MiPlaySetPlaySourcePayloadCodec.Encode(
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefChannel,
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefFunction,
                MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceRefContent));
        phase = MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites;
        return Accept(
            [new MiPlayLegacyAudioSourceWrite([setPlaySource])],
            "Heartbeat is verified; prepared the captured SetPlaySource sequence 13 without waiting for 0x0041.");
    }

    private static MiPlayLegacyAudioSourceWrite Write(ushort command, ushort sequence, byte[] payload) =>
        new([MiPlayCommandFrameCodec.Encode(command, sequence, payload)]);

    private MiPlayLegacyPlaybackControlTransition Accept(
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        string boundary) =>
        new(true, phase, writes, phase == MiPlayLegacyPlaybackControlPhase.OpenPrepared, SafeForNetworkUse: false, boundary);

    private MiPlayLegacyPlaybackControlTransition Reject(string boundary) =>
        new(false, phase, [], phase == MiPlayLegacyPlaybackControlPhase.OpenPrepared, SafeForNetworkUse: false, boundary);

    private MiPlayLegacyPlaybackControlTransition Stop(string boundary)
    {
        phase = MiPlayLegacyPlaybackControlPhase.Stopped;
        return Reject(boundary);
    }
}
