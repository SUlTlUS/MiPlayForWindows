using System.Globalization;
using System.Net;
using System.Text;

namespace DLNACast.Core.MiPlay;

public enum MiPlayWfdSourceRtspPhase
{
    Created,
    AwaitingInitialOptionsExchange,
    AwaitingCapabilities,
    AwaitingSelectedParametersAcknowledgement,
    AwaitingSetupTriggerAcknowledgement,
    AwaitingSetup,
    AwaitingPlay,
    AwaitingTimeOffsetAcknowledgement,
    Ready,
    Stopped,
}

public sealed record MiPlayWfdSourceRtspTransition(
    bool Accepted,
    MiPlayWfdSourceRtspPhase Phase,
    IReadOnlyList<byte[]> OutboundMessages,
    bool Ready,
    bool SafeForNetworkUse,
    string Boundary);

/// <summary>
/// Pure state reconstruction of the reverse WFD/RTSP control connection opened
/// by an LX06 after legacy Cmd_Open. It owns no socket and emits only byte arrays.
/// </summary>
public sealed class MiPlayWfdSourceRtspSession
{
    private readonly IPAddress sourceAddress;
    private readonly int timerPort;
    private readonly string sessionId;
    private readonly bool waitForInitialTimeOffsetAcknowledgement;
    private readonly bool waitForRuntimeTimeOffsetAcknowledgement;
    private bool sourceOptionsAcknowledged;
    private bool receiverOptionsObserved;
    private bool initialTimeOffsetReplaced;
    private ulong? timeOffsetMicroseconds;
    private int nextRuntimeTimeOffsetCSeq = 6;
    private int lastWrittenRuntimeTimeOffsetCSeq;
    private int? pendingRuntimeTimeOffsetCSeq;
    private ulong? pendingRuntimeTimeOffsetMicroseconds;
    private MiPlayWfdSourceRtspPhase phase = MiPlayWfdSourceRtspPhase.Created;

    public MiPlayWfdSourceRtspSession(
        IPAddress sourceAddress,
        int timerPort,
        string sessionId,
        bool waitForInitialTimeOffsetAcknowledgement = true,
        bool waitForRuntimeTimeOffsetAcknowledgement = true)
    {
        ArgumentNullException.ThrowIfNull(sourceAddress);
        if (sourceAddress.GetAddressBytes().Length != 4)
        {
            throw new NotSupportedException("The captured MiPlay WFD source path supports IPv4 only.");
        }
        if (timerPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(timerPort));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (sessionId.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("The captured RTSP session id is decimal.", nameof(sessionId));
        }

        this.sourceAddress = sourceAddress;
        this.timerPort = timerPort;
        this.sessionId = sessionId;
        this.waitForInitialTimeOffsetAcknowledgement = waitForInitialTimeOffsetAcknowledgement;
        this.waitForRuntimeTimeOffsetAcknowledgement = waitForRuntimeTimeOffsetAcknowledgement;
    }

    public MiPlayWfdSourceRtspPhase Phase => phase;
    public ulong? TimeOffsetMicroseconds => timeOffsetMicroseconds;
    public bool RuntimeTimeOffsetPending => pendingRuntimeTimeOffsetCSeq is not null;

    public MiPlayWfdSourceRtspTransition Start(DateTimeOffset timestamp)
    {
        if (phase != MiPlayWfdSourceRtspPhase.Created)
        {
            return Reject("The RTSP source session can be started only once.");
        }

        phase = MiPlayWfdSourceRtspPhase.AwaitingInitialOptionsExchange;
        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeOptions(timestamp, sourceAddress, timerPort)],
            "Prepared the captured source OPTIONS with the UDP timer endpoint; awaiting both CSeq 1 exchanges.");
    }

    public MiPlayWfdSourceRtspTransition ProcessInbound(
        ReadOnlySpan<byte> wireBytes,
        DateTimeOffset timestamp,
        ulong monotonicMicroseconds)
    {
        if (phase is MiPlayWfdSourceRtspPhase.Created or MiPlayWfdSourceRtspPhase.Stopped)
        {
            return Reject("The RTSP session is not accepting inbound messages in its current phase.");
        }
        if (!MiPlayRtspWireMessageCodec.TryDecode(wireBytes, out var message, out var consumed) ||
            message is null ||
            consumed != wireBytes.Length)
        {
            return Stop("Inbound bytes are not exactly one complete RTSP message.");
        }

        return phase switch
        {
            MiPlayWfdSourceRtspPhase.AwaitingInitialOptionsExchange =>
                ProcessInitialOptions(message, timestamp),
            MiPlayWfdSourceRtspPhase.AwaitingCapabilities =>
                ProcessCapabilities(message, timestamp),
            MiPlayWfdSourceRtspPhase.AwaitingSelectedParametersAcknowledgement =>
                ProcessEmptyAcknowledgement(message, 3, timestamp, MiPlayWfdSourceRtspPhase.AwaitingSetupTriggerAcknowledgement,
                    MiPlayWfdSourceRtspMessages.EncodeSetupTrigger,
                    "Receiver accepted AAC/interleaved parameters; prepared the SETUP trigger."),
            MiPlayWfdSourceRtspPhase.AwaitingSetupTriggerAcknowledgement =>
                ProcessSetupTriggerAcknowledgement(message),
            MiPlayWfdSourceRtspPhase.AwaitingSetup =>
                ProcessSetup(message, timestamp),
            MiPlayWfdSourceRtspPhase.AwaitingPlay =>
                ProcessPlay(message, timestamp, monotonicMicroseconds),
            MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement =>
                ProcessTimeOffsetAcknowledgement(message),
            MiPlayWfdSourceRtspPhase.Ready =>
                ProcessReadyRequest(message, timestamp),
            _ => Stop("Unsupported RTSP phase."),
        };
    }

    public MiPlayWfdSourceRtspTransition PrepareRuntimeTimeOffset(
        DateTimeOffset timestamp,
        ulong monotonicMicroseconds)
    {
        if (phase != MiPlayWfdSourceRtspPhase.Ready)
        {
            return Reject("Runtime TIME_OFFSET can be updated only after RTSP is ready.");
        }
        if (pendingRuntimeTimeOffsetCSeq is not null)
        {
            return Reject("A runtime TIME_OFFSET acknowledgement is already pending.");
        }

        var cseq = nextRuntimeTimeOffsetCSeq++;
        pendingRuntimeTimeOffsetCSeq = cseq;
        pendingRuntimeTimeOffsetMicroseconds = monotonicMicroseconds;
        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeTimeOffset(timestamp, monotonicMicroseconds, cseq)],
            $"Prepared runtime TIME_OFFSET CSeq {cseq}; awaiting its write/acknowledgement completion.");
    }

    /// <summary>
    /// The RTSP owner calls this after the bytes were written, never at queue
    /// insertion. Shared playback follows native sendTimeOffset's asynchronous
    /// response handling; single-receiver callers retain the ACK gate.
    /// </summary>
    public MiPlayWfdSourceRtspTransition MarkRuntimeTimeOffsetSent()
    {
        if (phase != MiPlayWfdSourceRtspPhase.Ready || pendingRuntimeTimeOffsetCSeq is not int cseq)
        {
            return Reject("No prepared runtime TIME_OFFSET can be marked sent.");
        }
        if (waitForRuntimeTimeOffsetAcknowledgement)
        {
            return Accept([], "Runtime TIME_OFFSET was sent; preserving the acknowledgement gate.");
        }

        timeOffsetMicroseconds = pendingRuntimeTimeOffsetMicroseconds ??
            throw new InvalidOperationException("Runtime TIME_OFFSET lost its pending value.");
        lastWrittenRuntimeTimeOffsetCSeq = cseq;
        pendingRuntimeTimeOffsetCSeq = null;
        pendingRuntimeTimeOffsetMicroseconds = null;
        return Accept([], $"Runtime TIME_OFFSET CSeq {cseq} was written; shared media does not wait for its acknowledgement.");
    }

    /// <summary>
    /// Called only after the initial TIME_OFFSET bytes have been written. The
    /// tablet posts its offset asynchronously and its onReceiveTimeOffset is a
    /// no-op; shared media can start without waiting for that response. Keep
    /// the captured single-receiver acknowledgement gate as the default.
    /// </summary>
    public MiPlayWfdSourceRtspTransition MarkInitialTimeOffsetSent()
    {
        if (phase != MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement)
        {
            return Reject("Initial TIME_OFFSET can be marked sent only after PLAY prepared it.");
        }
        if (waitForInitialTimeOffsetAcknowledgement)
        {
            return Accept([], "Initial TIME_OFFSET was sent; preserving the captured acknowledgement gate.");
        }

        phase = MiPlayWfdSourceRtspPhase.Ready;
        return Accept([], "Initial TIME_OFFSET was sent; shared media does not wait for its acknowledgement.");
    }

    public MiPlayWfdSourceRtspTransition ReplacePendingInitialTimeOffset(
        DateTimeOffset timestamp,
        ulong synchronizedTimeOffsetMicroseconds)
    {
        if (phase != MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement)
        {
            return Reject("The initial TIME_OFFSET can be replaced only before its bytes are sent.");
        }
        if (waitForInitialTimeOffsetAcknowledgement)
        {
            return Reject("Single-receiver TIME_OFFSET keeps the captured PLAY clock.");
        }
        if (initialTimeOffsetReplaced)
        {
            return Reject("The shared initial TIME_OFFSET was already derived from its first output clock.");
        }

        timeOffsetMicroseconds = synchronizedTimeOffsetMicroseconds;
        initialTimeOffsetReplaced = true;
        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeTimeOffset(timestamp, synchronizedTimeOffsetMicroseconds)],
            "Prepared the tablet-style initial TIME_OFFSET from the first shared output clock.");
    }

    private MiPlayWfdSourceRtspTransition ProcessInitialOptions(
        MiPlayRtspWireMessage message,
        DateTimeOffset timestamp)
    {
        var outbound = new List<byte[]>();
        if (IsOk(message, 1))
        {
            if (sourceOptionsAcknowledged)
            {
                return Stop("The source OPTIONS acknowledgement was duplicated.");
            }
            sourceOptionsAcknowledged = true;
        }
        else if (message.StartLine.Equals("OPTIONS * RTSP/1.0", StringComparison.Ordinal))
        {
            if (receiverOptionsObserved || !HasCSeq(message, 1) ||
                !string.Equals(message.GetHeader("Require"), "org.wfa.wfd1.0", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.GetHeader("lib_version")))
            {
                return Stop("Receiver OPTIONS must be the one captured CSeq 1 WFD request with a lib_version.");
            }
            receiverOptionsObserved = true;
            outbound.Add(MiPlayWfdSourceRtspMessages.EncodeOptionsResponse(timestamp));
        }
        else
        {
            return Stop("Expected either the CSeq 1 source OPTIONS acknowledgement or receiver OPTIONS request.");
        }

        if (sourceOptionsAcknowledged && receiverOptionsObserved)
        {
            outbound.Add(MiPlayWfdSourceRtspMessages.EncodeCapabilityQuery(timestamp));
            phase = MiPlayWfdSourceRtspPhase.AwaitingCapabilities;
            return Accept(outbound, "Both CSeq 1 OPTIONS messages are verified; prepared the capability query.");
        }

        return Accept(outbound, "Accepted one half of the initial bidirectional OPTIONS exchange.");
    }

    private MiPlayWfdSourceRtspTransition ProcessCapabilities(
        MiPlayRtspWireMessage message,
        DateTimeOffset timestamp)
    {
        if (!IsOk(message, 2))
        {
            return Stop("Expected the receiver capability response as RTSP 200 CSeq 2.");
        }

        var body = Encoding.ASCII.GetString(message.Body);
        if (!body.Contains("wfd_audio_codecs: AAC 00000001 00", StringComparison.Ordinal) ||
            !body.Contains("wfd_video_formats: none", StringComparison.Ordinal) ||
            !body.Contains("RTP/AVP/TCP;interleaved mode=play", StringComparison.Ordinal))
        {
            return Stop("Receiver capabilities do not match the captured AAC-only interleaved profile.");
        }

        phase = MiPlayWfdSourceRtspPhase.AwaitingSelectedParametersAcknowledgement;
        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeSelectedParameters(timestamp, sourceAddress)],
            "Verified AAC-only capabilities and prepared the selected source parameters.");
    }

    private MiPlayWfdSourceRtspTransition ProcessEmptyAcknowledgement(
        MiPlayRtspWireMessage message,
        int cseq,
        DateTimeOffset timestamp,
        MiPlayWfdSourceRtspPhase nextPhase,
        Func<DateTimeOffset, byte[]> nextMessage,
        string boundary)
    {
        if (!IsOk(message, cseq) || message.Body.Length != 0)
        {
            return Stop($"Expected an empty RTSP 200 acknowledgement for CSeq {cseq.ToString(CultureInfo.InvariantCulture)}.");
        }

        phase = nextPhase;
        return Accept([nextMessage(timestamp)], boundary);
    }

    private MiPlayWfdSourceRtspTransition ProcessSetupTriggerAcknowledgement(MiPlayRtspWireMessage message)
    {
        if (!IsOk(message, 4) || message.Body.Length != 0)
        {
            return Stop("Expected the empty RTSP 200 CSeq 4 SETUP-trigger acknowledgement.");
        }

        phase = MiPlayWfdSourceRtspPhase.AwaitingSetup;
        return Accept([], "SETUP trigger was accepted; awaiting the receiver-initiated SETUP request.");
    }

    private MiPlayWfdSourceRtspTransition ProcessSetup(
        MiPlayRtspWireMessage message,
        DateTimeOffset timestamp)
    {
        if (!message.StartLine.StartsWith("SETUP ", StringComparison.Ordinal) || !HasCSeq(message, 2))
        {
            return Stop("Expected the receiver-initiated SETUP request with CSeq 2.");
        }
        var transport = message.GetHeader("Transport");
        if (string.IsNullOrWhiteSpace(transport) ||
            !transport.Contains("RTP/AVP/TCP;interleaved=0-1", StringComparison.Ordinal))
        {
            return Stop("SETUP did not request the captured RTP/AVP/TCP interleaved channel pair.");
        }

        phase = MiPlayWfdSourceRtspPhase.AwaitingPlay;
        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeSetupResponse(timestamp, 2, sessionId, transport)],
            "Accepted receiver SETUP and assigned the local RTSP session; awaiting PLAY.");
    }

    private MiPlayWfdSourceRtspTransition ProcessPlay(
        MiPlayRtspWireMessage message,
        DateTimeOffset timestamp,
        ulong monotonicMicroseconds)
    {
        if (!message.StartLine.StartsWith("PLAY ", StringComparison.Ordinal) ||
            !HasCSeq(message, 3) ||
            !SessionMatches(message.GetHeader("Session")))
        {
            return Stop("Expected receiver PLAY CSeq 3 for the assigned RTSP session.");
        }

        timeOffsetMicroseconds = monotonicMicroseconds;
        phase = MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement;
        return Accept(
            [
                MiPlayWfdSourceRtspMessages.EncodePlayResponse(timestamp, 3, sessionId),
                MiPlayWfdSourceRtspMessages.EncodeTimeOffset(timestamp, monotonicMicroseconds),
            ],
            "Accepted PLAY and prepared its response plus the captured CSeq 5 TIME_OFFSET request.");
    }

    private MiPlayWfdSourceRtspTransition ProcessTimeOffsetAcknowledgement(MiPlayRtspWireMessage message)
    {
        if (!IsOk(message, 5) || message.Body.Length != 0)
        {
            return Stop("Expected the empty RTSP 200 CSeq 5 TIME_OFFSET acknowledgement.");
        }

        phase = MiPlayWfdSourceRtspPhase.Ready;
        return Accept([], "Reverse WFD/RTSP control is ready for the separate AAC MPEG-TS/RTP audio channel.");
    }

    private MiPlayWfdSourceRtspTransition ProcessReadyRequest(
        MiPlayRtspWireMessage message,
        DateTimeOffset timestamp)
    {
        if (message.StartLine.StartsWith("RTSP/", StringComparison.Ordinal) &&
            TryGetCSeq(message, out var writtenCSeq) &&
            ((!waitForInitialTimeOffsetAcknowledgement && writtenCSeq == 5) ||
             (!waitForRuntimeTimeOffsetAcknowledgement &&
              writtenCSeq >= 6 && writtenCSeq <= lastWrittenRuntimeTimeOffsetCSeq)))
        {
            if (!IsWellFormedTimeOffsetResponse(message))
            {
                return Stop($"Invalid TIME_OFFSET response for written CSeq {writtenCSeq}.");
            }
            // Tablet onReceiveTimeOffset returns success without inspecting
            // status or body. This observes a response, not proof that the
            // receiver applied the offset; only our successful write commits it.
            // Only TIME_OFFSET uses source CSeq >= 6. A high-water mark keeps
            // missing replies bounded in memory, and includes only completed
            // writes. Neither CSeq 5 nor an old runtime reply can consume a
            // newer prepared request or replace the most recently written offset.
            return Accept([], $"Observed TIME_OFFSET response CSeq {writtenCSeq} without gating shared media.");
        }
        if (waitForRuntimeTimeOffsetAcknowledgement &&
            pendingRuntimeTimeOffsetCSeq is int pendingCSeq &&
            message.StartLine.StartsWith("RTSP/", StringComparison.Ordinal))
        {
            if (!IsOk(message, pendingCSeq) || message.Body.Length != 0)
            {
                return Stop(
                    $"Expected the empty RTSP 200 CSeq {pendingCSeq} runtime TIME_OFFSET acknowledgement.");
            }

            timeOffsetMicroseconds = pendingRuntimeTimeOffsetMicroseconds ??
                throw new InvalidOperationException("Runtime TIME_OFFSET lost its pending value.");
            pendingRuntimeTimeOffsetCSeq = null;
            pendingRuntimeTimeOffsetMicroseconds = null;
            return Accept([], $"Receiver acknowledged runtime TIME_OFFSET CSeq {pendingCSeq}.");
        }

        if (!TryGetCSeq(message, out var cseq))
        {
            return Stop("A ready-state receiver request omitted a valid CSeq.");
        }

        var method = message.StartLine.Split(' ', 2)[0];
        var supported = method.Equals("OPTIONS", StringComparison.Ordinal) ||
                        method.Equals("GET_PARAMETER", StringComparison.Ordinal) ||
                        method.Equals("VIDEO_LATENCY", StringComparison.Ordinal) ||
                        (method.Equals("SET_PARAMETER", StringComparison.Ordinal) &&
                         Encoding.ASCII.GetString(message.Body).Contains("wfd_idr_request", StringComparison.Ordinal));
        if (!supported)
        {
            return Stop(
                "Only OPTIONS/GET_PARAMETER keepalive, VIDEO_LATENCY, and IDR requests are " +
                $"accepted after RTSP readiness; received '{message.StartLine}'.");
        }

        if (method.Equals("VIDEO_LATENCY", StringComparison.Ordinal))
        {
            return Accept(
                [],
                "Observed captured VIDEO_LATENCY telemetry without replying, matching the rooted-phone source trace.");
        }

        if (method.Equals("OPTIONS", StringComparison.Ordinal))
        {
            return Accept(
                [MiPlayWfdSourceRtspMessages.EncodeOptionsResponse(timestamp, cseq, sessionId)],
                "Acknowledged a ready-state RTSP OPTIONS keepalive.");
        }

        return Accept(
            [MiPlayWfdSourceRtspMessages.EncodeReceiverRequestResponse(timestamp, cseq, sessionId)],
            $"Acknowledged captured ready-state receiver request {method}.");
    }

    private bool SessionMatches(string? value) =>
        value is not null &&
        (value.Equals(sessionId, StringComparison.Ordinal) ||
         value.StartsWith(sessionId + ";", StringComparison.Ordinal));

    private static bool IsOk(MiPlayRtspWireMessage message, int cseq) =>
        message.StartLine.Equals("RTSP/1.0 200 OK", StringComparison.Ordinal) && HasCSeq(message, cseq);

    private static bool IsWellFormedTimeOffsetResponse(MiPlayRtspWireMessage message)
    {
        // Keep the RTSP/1.0 envelope strict while accepting non-200 responses
        // and arbitrary, fully framed bodies for a known TIME_OFFSET only.
        var line = message.StartLine.AsSpan();
        return line.StartsWith("RTSP/1.0 ", StringComparison.Ordinal) &&
               line.Length >= 12 &&
               line[9] is >= '1' and <= '5' &&
               line[10] is >= '0' and <= '9' &&
               line[11] is >= '0' and <= '9' &&
               (line.Length == 12 || line[12] == ' ');
    }

    private static bool HasCSeq(MiPlayRtspWireMessage message, int expected) =>
        TryGetCSeq(message, out var actual) && actual == expected;

    private static bool TryGetCSeq(MiPlayRtspWireMessage message, out int cseq) =>
        int.TryParse(message.GetHeader("CSeq"), NumberStyles.None, CultureInfo.InvariantCulture, out cseq) && cseq >= 0;

    private MiPlayWfdSourceRtspTransition Accept(IReadOnlyList<byte[]> outbound, string boundary) =>
        new(true, phase, outbound, phase == MiPlayWfdSourceRtspPhase.Ready, SafeForNetworkUse: false, boundary);

    private MiPlayWfdSourceRtspTransition Reject(string boundary) =>
        new(false, phase, [], phase == MiPlayWfdSourceRtspPhase.Ready, SafeForNetworkUse: false, boundary);

    private MiPlayWfdSourceRtspTransition Stop(string boundary)
    {
        phase = MiPlayWfdSourceRtspPhase.Stopped;
        return Reject(boundary);
    }
}
