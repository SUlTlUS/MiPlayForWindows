using System.Net;
using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdSourceRtspSessionTests
{
    private static readonly DateTimeOffset Second43 =
        new(2026, 8, 7, 6, 12, 43, TimeSpan.Zero);
    private static readonly DateTimeOffset Second44 = Second43.AddSeconds(1);

    [Fact]
    public void ReplaysCapturedSixteenStepHandshakeToReadyWithoutSockets()
    {
        var address = IPAddress.Parse("192.168.10.58");
        var session = new MiPlayWfdSourceRtspSession(address, 36_524, "588290182");

        var start = session.Start(Second43);
        Assert.True(start.Accepted);
        Assert.Single(start.OutboundMessages);
        Assert.Equal(
            "2267F3241E03DB32D0AC89A2F3DFFDD2E6F7C685562677EDB21FFDEB61371749",
            Hash(start.OutboundMessages[0]));

        var optionsAck = session.ProcessInbound(
            Response(1), Second44, 9_633_364_443);
        Assert.True(optionsAck.Accepted);
        Assert.Empty(optionsAck.OutboundMessages);

        var receiverOptions = Request(
            "OPTIONS * RTSP/1.0",
            [
                new("CSeq", "1"),
                new("Require", "org.wfa.wfd1.0"),
                new("lib_version", "audio-speaker-mico-cloud 2.1.4052010"),
            ]);
        var initialComplete = session.ProcessInbound(receiverOptions, Second44, 9_633_364_443);
        Assert.True(initialComplete.Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingCapabilities, initialComplete.Phase);
        Assert.Equal(2, initialComplete.OutboundMessages.Count);
        Assert.Equal(
            [
                "E50C6B31A3CB83EEC7E9FE80B16978268F3E61A60AFE978E07317A15420BB004",
                "143274797AE02D907243A4B8313191C9741A97AB941A864CAE7A7D07FCA4F48B",
            ],
            initialComplete.OutboundMessages.Select(Hash));

        var capabilities = session.ProcessInbound(
            Response(2, Encoding.ASCII.GetBytes(MiPlayRealLegacyPlaybackSessionEvidence.ReceiverAudioCapabilities)),
            Second44,
            9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSelectedParametersAcknowledgement, capabilities.Phase);
        Assert.Equal(
            "083C3B99EEAB800AFC7BE01980804AC8B4F56EF667A5B8888419D6A062E28E16",
            Hash(Assert.Single(capabilities.OutboundMessages)));

        var selectedAck = session.ProcessInbound(Response(3), Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSetupTriggerAcknowledgement, selectedAck.Phase);
        Assert.Equal(
            "A300B2C10458DDED329697AEF7046C168FF16A6D725D3B2ED59758EA0FE9B63D",
            Hash(Assert.Single(selectedAck.OutboundMessages)));

        var triggerAck = session.ProcessInbound(Response(4), Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSetup, triggerAck.Phase);
        Assert.Empty(triggerAck.OutboundMessages);

        var setup = Request(
            "SETUP rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [
                new("CSeq", "2"),
                new("Transport", "RTP/AVP/TCP;interleaved=0-1"),
            ]);
        var setupResponse = session.ProcessInbound(setup, Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingPlay, setupResponse.Phase);
        Assert.Equal(
            "5911B64278E1B409599962B929E39F2B68001ED99D94F11E9245516163D06815",
            Hash(Assert.Single(setupResponse.OutboundMessages)));

        var play = Request(
            "PLAY rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [
                new("CSeq", "3"),
                new("Session", "588290182"),
            ]);
        var playResponse = session.ProcessInbound(play, Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement, playResponse.Phase);
        Assert.Equal(9_633_364_443UL, session.TimeOffsetMicroseconds);
        Assert.Equal(
            [
                "B0E11D4FA020823F5E710462F1E753821932828AAEE4C3C0FC3A7F355E481933",
                "265A36D5E5C75B73E02C7695956A8F1089C9CC0943E90E95DA212C3227961454",
            ],
            playResponse.OutboundMessages.Select(Hash));

        var ready = session.ProcessInbound(Response(5), Second44, 9_633_364_443);
        Assert.True(ready.Accepted);
        Assert.True(ready.Ready);
        Assert.False(ready.SafeForNetworkUse);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Ready, ready.Phase);
        Assert.Empty(ready.OutboundMessages);

        var runtimeTimeOffset = session.PrepareRuntimeTimeOffset(
            Second44,
            monotonicMicroseconds: 9_700_000_000);
        Assert.True(runtimeTimeOffset.Accepted);
        Assert.True(session.RuntimeTimeOffsetPending);
        var runtimeRequestBytes = Assert.Single(runtimeTimeOffset.OutboundMessages);
        Assert.True(MiPlayRtspWireMessageCodec.TryDecode(
            runtimeRequestBytes,
            out var runtimeRequest,
            out var runtimeRequestConsumed));
        Assert.NotNull(runtimeRequest);
        Assert.Equal(runtimeRequestBytes.Length, runtimeRequestConsumed);
        Assert.Equal("6", runtimeRequest.GetHeader("CSeq"));
        Assert.Equal("9700000000", runtimeRequest.GetHeader("TimeOffset"));

        var keepaliveWhileRuntimeTimeOffsetPending = session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [new("CSeq", "41"), new("Session", "588290182")]),
            Second44,
            9_700_000_000);
        Assert.True(keepaliveWhileRuntimeTimeOffsetPending.Accepted);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Single(keepaliveWhileRuntimeTimeOffsetPending.OutboundMessages);

        var runtimeAcknowledgement = session.ProcessInbound(
            Response(6),
            Second44,
            9_700_000_000);
        Assert.True(runtimeAcknowledgement.Accepted);
        Assert.True(runtimeAcknowledgement.Ready);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(9_700_000_000UL, session.TimeOffsetMicroseconds);

        var latency = session.ProcessInbound(
            Request(
                "VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0",
                [
                    new("CSeq", "4"),
                    new("Content-Type", "text/parameters"),
                    new("latency", "0"),
                    new("bitrate", "-1"),
                    new("rtpPacketNum", "1"),
                    new("Content-Length", "0"),
                ]),
            Second44,
            9_633_364_443);
        Assert.True(latency.Accepted);
        Assert.True(latency.Ready);
        Assert.Empty(latency.OutboundMessages);
        Assert.Contains("without replying", latency.Boundary, StringComparison.Ordinal);

        var keepalive = session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [
                    new("CSeq", "42"),
                    new("Session", "588290182"),
                ]),
            Second44,
            9_633_364_443);
        Assert.True(keepalive.Accepted);
        Assert.True(keepalive.Ready);
        var responseBytes = Assert.Single(keepalive.OutboundMessages);
        Assert.True(MiPlayRtspWireMessageCodec.TryDecode(responseBytes, out var response, out _));
        Assert.NotNull(response);
        Assert.Equal("RTSP/1.0 200 OK", response.StartLine);
        Assert.Equal("42", response.GetHeader("CSeq"));
        Assert.Equal("588290182;timeout=60", response.GetHeader("Session"));
        Assert.Contains("GET_PARAMETER", response.GetHeader("Public"), StringComparison.Ordinal);

        var unsupported = session.ProcessInbound(
            Request(
                "TEARDOWN rtsp://localhost/wfd1.0 RTSP/1.0",
                [new("CSeq", "43"), new("Session", "588290182")]),
            Second44,
            9_633_364_443);
        Assert.False(unsupported.Accepted);
        Assert.Contains(
            "received 'TEARDOWN rtsp://localhost/wfd1.0 RTSP/1.0'",
            unsupported.Boundary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InitialOptionsCanArriveBeforeSourceAcknowledgement()
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Parse("192.168.10.58"), 36_524, "588290182");
        session.Start(Second43);

        var receiverOptions = session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [
                    new("CSeq", "1"),
                    new("Require", "org.wfa.wfd1.0"),
                    new("lib_version", "audio-speaker-mico-cloud 2.1.4052010"),
                ]),
            Second44,
            1);
        Assert.Single(receiverOptions.OutboundMessages);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingInitialOptionsExchange, receiverOptions.Phase);

        var acknowledgement = session.ProcessInbound(Response(1), Second44, 1);
        Assert.Single(acknowledgement.OutboundMessages);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingCapabilities, acknowledgement.Phase);
    }

    [Fact]
    public void CapabilityMismatchStopsBeforeSelectingParameters()
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Parse("192.168.10.58"), 36_524, "588290182");
        session.Start(Second43);
        session.ProcessInbound(Response(1), Second44, 1);
        session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [
                    new("CSeq", "1"),
                    new("Require", "org.wfa.wfd1.0"),
                    new("lib_version", "receiver"),
                ]),
            Second44,
            1);

        var result = session.ProcessInbound(
            Response(2, "wfd_audio_codecs: LPCM 00000001 00\r\n"u8.ToArray()),
            Second44,
            1);

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundMessages);
    }

    [Fact]
    public void SharedMediaBecomesReadyOnlyAfterInitialTimeOffsetWasWritten()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: false);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement, session.Phase);
        var beforeSend = session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        Assert.False(beforeSend.Accepted);

        var sent = session.MarkInitialTimeOffsetSent();

        Assert.True(sent.Accepted);
        Assert.True(sent.Ready);
        Assert.Empty(sent.OutboundMessages);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Fact]
    public void SharedInitialTimeOffsetCanBeReplacedFromTheFirstOutputClockBeforeWrite()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: false);
        var replacement = session.ReplacePendingInitialTimeOffset(
            Second44,
            58_879_859_721);

        Assert.True(replacement.Accepted);
        Assert.False(replacement.Ready);
        Assert.Equal(58_879_859_721UL, session.TimeOffsetMicroseconds);
        Assert.Equal(
            MiPlayWfdSourceRtspMessages.EncodeTimeOffset(Second44, 58_879_859_721),
            Assert.Single(replacement.OutboundMessages));
        Assert.False(session.ReplacePendingInitialTimeOffset(Second44, 1).Accepted);
        Assert.True(session.MarkInitialTimeOffsetSent().Ready);
        Assert.False(session.ReplacePendingInitialTimeOffset(Second44, 1).Accepted);
    }

    [Fact]
    public void SingleReceiverCannotReplaceItsCapturedPlayClock()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: true);
        Assert.False(session.ReplacePendingInitialTimeOffset(Second44, 58_879_859_721).Accepted);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
        Assert.True(session.MarkInitialTimeOffsetSent().Accepted);
    }

    [Fact]
    public void SingleReceiverStillWaitsForInitialTimeOffsetAcknowledgement()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: true);
        var sent = session.MarkInitialTimeOffsetSent();

        Assert.True(sent.Accepted);
        Assert.False(sent.Ready);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement, session.Phase);
        Assert.True(session.ProcessInbound(Response(5), Second44, 1_000_000).Ready);
    }

    [Fact]
    public void SharedReadySessionAcceptsKeepaliveWithoutAnInitialAcknowledgement()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: false);
        session.MarkInitialTimeOffsetSent();

        var keepalive = session.ProcessInbound(
            Request("OPTIONS * RTSP/1.0", [new("CSeq", "42")]), Second44, 1_000_000);

        Assert.True(keepalive.Accepted);
        Assert.True(keepalive.Ready);
        Assert.Single(keepalive.OutboundMessages);
    }

    [Fact]
    public void LateInitialAckCannotCompleteANewerRuntimeRequestOrRestoreItsOldOffset()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: false);
        session.MarkInitialTimeOffsetSent();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);

        var lateInitial = session.ProcessInbound(Response(5), Second44, 2_000_000);
        Assert.True(lateInitial.Accepted);
        Assert.True(lateInitial.Ready);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);

        Assert.True(session.ProcessInbound(Response(6), Second44, 2_000_000).Accepted);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);

        Assert.True(session.ProcessInbound(Response(5), Second44, 2_000_000).Accepted);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Fact]
    public void MissingInitialAckDoesNotPreventLaterRuntimeUpdates()
    {
        var session = CreatePendingTimeOffsetSession(waitForAcknowledgement: false);
        session.MarkInitialTimeOffsetSent();

        Assert.True(session.PrepareRuntimeTimeOffset(Second44, 2_000_000).Accepted);
        Assert.True(session.ProcessInbound(Response(6), Second44, 2_000_000).Accepted);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
        Assert.False(session.RuntimeTimeOffsetPending);
    }

    [Fact]
    public void TimeOffsetSentNotificationCannotSkipTheHandshake()
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Loopback, 36_524, "588290182", waitForInitialTimeOffsetAcknowledgement: false);
        Assert.False(session.MarkInitialTimeOffsetSent().Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Created, session.Phase);
        session.Start(Second43);
        Assert.False(session.MarkInitialTimeOffsetSent().Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingInitialOptionsExchange, session.Phase);
    }

    [Fact]
    public async Task ThreeReceiversStartTogetherWithoutWaitingForTheSlowestInitialAck()
    {
        var sessions = Enumerable.Range(0, 3)
            .Select(_ => CreatePendingTimeOffsetSession(waitForAcknowledgement: false)).ToArray();
        var coordinator = new MiPlaySharedMediaCoordinator(3);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = lifetime.Token;
        await Task.WhenAll(Enumerable.Range(1, 3).Select(id =>
            coordinator.SynchronizeTimeOffsetAsync(id, 1_000_000, token)));
        foreach (var session in sessions) Assert.True(session.MarkInitialTimeOffsetSent().Ready);
        Assert.True(sessions[0].ProcessInbound(Response(5), Second44, 1_000_000).Accepted);
        // The other two receivers have not acknowledged. Their media still
        // reaches the shared barrier, but no member can skip the AAC runway.
        var starts = await Task.WhenAll(sessions.Select((session, index) =>
            coordinator.SynchronizeMediaAsync(index + 1, session.TimeOffsetMicroseconds!.Value, token)));
        Assert.Single(starts.Distinct());
        Assert.False(coordinator.IsMediaReady);
        await Task.WhenAll(Enumerable.Range(1, 3).Select(id =>
            coordinator.SynchronizeAccessUnitRunwayAsync(id, token)));
        Assert.True(coordinator.IsMediaReady);
    }

    [Fact]
    public void SharedRuntimeOffsetChangesOnlyAfterItsBytesWereWritten()
    {
        var session = CreateSharedRuntimeSession();
        var prepared = session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        Assert.True(prepared.Accepted);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
        Assert.False(session.PrepareRuntimeTimeOffset(Second44, 3_000_000).Accepted);
        Assert.Equal(MiPlayWfdSourceRtspMessages.EncodeTimeOffset(Second44, 2_000_000, 6),
            Assert.Single(prepared.OutboundMessages));

        Assert.True(session.MarkRuntimeTimeOffsetSent().Accepted);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
        Assert.False(session.MarkRuntimeTimeOffsetSent().Accepted);
        Assert.True(session.PrepareRuntimeTimeOffset(Second44, 3_000_000).Accepted);
    }

    [Fact]
    public void LateRuntimeReplyCannotConsumeANewerPreparedWrite()
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        session.PrepareRuntimeTimeOffset(Second44, 3_000_000);

        Assert.True(session.ProcessInbound(Response(6), Second44, 4_000_000).Accepted);
        Assert.True(session.ProcessInbound(Response(5), Second44, 4_000_000).Accepted);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
        Assert.True(session.MarkRuntimeTimeOffsetSent().Accepted);
        Assert.Equal(3_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Fact]
    public void ReorderedAndRepeatedRuntimeRepliesDoNotRestoreOldOffsets()
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        session.PrepareRuntimeTimeOffset(Second44, 3_000_000);
        session.MarkRuntimeTimeOffsetSent();
        foreach (var cseq in new[] { 7, 6, 5, 7, 6 })
        {
            var reply = session.ProcessInbound(Response(cseq), Second44, 9_000_000);
            Assert.True(reply.Accepted);
            Assert.True(reply.Ready);
            Assert.False(session.RuntimeTimeOffsetPending);
            Assert.Equal(3_000_000UL, session.TimeOffsetMicroseconds);
        }
        Assert.True(session.ProcessInbound(
            Request("OPTIONS * RTSP/1.0", [new("CSeq", "99")]), Second44, 9_000_000).Accepted);
    }

    [Fact]
    public void MissingRuntimeRepliesDoNotBlockSubsequentWrites()
    {
        var session = CreateSharedRuntimeSession();
        for (var index = 0; index < 1_000; index++)
        {
            var offset = 2_000_000UL + (ulong)index;
            var prepared = session.PrepareRuntimeTimeOffset(Second44, offset);
            Assert.True(prepared.Accepted);
            Assert.Equal(MiPlayWfdSourceRtspMessages.EncodeTimeOffset(Second44, offset, index + 6),
                Assert.Single(prepared.OutboundMessages));
            Assert.True(session.MarkRuntimeTimeOffsetSent().Accepted);
            Assert.False(session.RuntimeTimeOffsetPending);
        }
        Assert.True(session.ProcessInbound(Response(6), Second44, 9_000_000).Accepted);
        Assert.True(session.ProcessInbound(Response(1005), Second44, 9_000_000).Accepted);
        Assert.Equal(2_000_999UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void SharedRuntimeStillRejectsResponsesForUnknownSourceRequests(int cseq)
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        Assert.False(session.ProcessInbound(Response(cseq), Second44, 9_000_000).Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, session.Phase);
    }

    [Fact]
    public void AReplyCannotAcknowledgeAPreparedButUnwrittenSharedOffset()
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        Assert.False(session.ProcessInbound(Response(6), Second44, 9_000_000).Accepted);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData(5, "RTSP/1.0 200 OK", "receiver detail")]
    [InlineData(6, "RTSP/1.0 200 OK", "receiver detail")]
    [InlineData(5, "RTSP/1.0 500 Internal Server Error", "")]
    [InlineData(6, "RTSP/1.0 500 Internal Server Error", "")]
    [InlineData(5, "RTSP/1.0 400 Bad Request", "unsupported offset")]
    [InlineData(6, "RTSP/1.0 400 Bad Request", "unsupported offset")]
    [InlineData(5, "RTSP/1.0 204 No Content", "")]
    [InlineData(6, "RTSP/1.0 204 No Content", "")]
    [InlineData(5, "RTSP/1.0 200", "")]
    [InlineData(6, "RTSP/1.0 200 Accepted", "")]
    public void SharedTimeOffsetResponseCannotStopMediaOrConsumeANewerWrite(
        int cseq, string startLine, string body)
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        session.PrepareRuntimeTimeOffset(Second44, 3_000_000);
        var response = TimeOffsetResponse(cseq, startLine, body);

        var observed = session.ProcessInbound(response, Second44, 9_000_000);
        Assert.True(observed.Accepted);
        Assert.True(observed.Ready);
        Assert.Empty(observed.OutboundMessages);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);

        session.MarkRuntimeTimeOffsetSent();
        Assert.True(session.ProcessInbound(response, Second44, 9_000_000).Accepted);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(3_000_000UL, session.TimeOffsetMicroseconds);
        Assert.True(session.ProcessInbound(
            Request("OPTIONS * RTSP/1.0", [new("CSeq", "42")]), Second44, 9_000_000).Accepted);
    }

    [Theory]
    [InlineData("RTSP/1.0 500 Internal Server Error", "")]
    [InlineData("RTSP/1.0 200 OK", "receiver detail")]
    public void InitialSharedResponseIsOnlyAnObservationEvenWhenRuntimeStillUsesAckMode(
        string startLine, string body)
    {
        var session = CreatePendingTimeOffsetSession(false, true);
        session.MarkInitialTimeOffsetSent();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        var response = session.ProcessInbound(TimeOffsetResponse(5, startLine, body), Second44, 9_000_000);
        Assert.True(response.Ready);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
        Assert.True(session.ProcessInbound(Response(6), Second44, 9_000_000).Accepted);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData("RTSP/1.0")]
    [InlineData("RTSP/1.0 OK")]
    [InlineData("RTSP/1.0 20 OK")]
    [InlineData("RTSP/1.0 2000 OK")]
    [InlineData("RTSP/1.0 2xx OK")]
    [InlineData("RTSP/1.0 099 Invalid")]
    [InlineData("RTSP/1.0 600 Invalid")]
    [InlineData("RTSP/2.0 200 OK")]
    [InlineData("RTSP/1.0 200OK")]
    public void SharedTimeOffsetStillRejectsMalformedResponseLines(string startLine)
    {
        foreach (var cseq in new[] { 5, 6 })
        {
            var session = CreateSharedRuntimeSession();
            session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
            session.MarkRuntimeTimeOffsetSent();
            Assert.False(session.ProcessInbound(TimeOffsetResponse(cseq, startLine, ""), Second44, 9_000_000).Accepted);
            Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, session.Phase);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void NonSuccessResponsesStillRequireAnAlreadyWrittenTimeOffset(int cseq)
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        // CSeq 7 has been prepared but not sent: it must not be accepted either.
        session.PrepareRuntimeTimeOffset(Second44, 3_000_000);
        Assert.False(session.ProcessInbound(TimeOffsetResponse(cseq, "RTSP/1.0 500 Error", "detail"),
            Second44, 9_000_000).Accepted);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void ReceiverRequestCannotBeMistakenForATimeOffsetResponse(int cseq)
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        var response = session.ProcessInbound(
            Request("OPTIONS * RTSP/1.0", [new("CSeq", cseq.ToString())]), Second44, 9_000_000);
        Assert.True(response.Accepted);
        Assert.Single(response.OutboundMessages);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData(true, "RTSP/1.0 500 Error", "")]
    [InlineData(false, "RTSP/1.0 500 Error", "")]
    [InlineData(true, "RTSP/1.0 200 OK", "receiver detail")]
    [InlineData(false, "RTSP/1.0 200 OK", "receiver detail")]
    public void SingleSpeakerStillRequiresSuccessfulEmptyTimeOffsetAcknowledgements(
        bool initial, string startLine, string body)
    {
        var session = CreatePendingTimeOffsetSession(true, true);
        session.MarkInitialTimeOffsetSent();
        if (!initial)
        {
            session.ProcessInbound(Response(5), Second44, 1_000_000);
            session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
            session.MarkRuntimeTimeOffsetSent();
        }
        Assert.False(session.ProcessInbound(TimeOffsetResponse(initial ? 5 : 6, startLine, body),
            Second44, 9_000_000).Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, session.Phase);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
    }

    [Theory]
    [InlineData("RTSP/1.0 500 Error\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("RTSP/1.0 500 Error\r\nCSeq: -1\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("RTSP/1.0 500 Error\r\nCSeq: 5\r\nContent-Length: 3\r\n\r\nx")]
    [InlineData("RTSP/1.0 200 OK\r\nCSeq: 6\r\nContent-Length: 1\r\n\r\nxyz")]
    public void SharedTimeOffsetStillRequiresOneCompleteMessageWithAValidSequence(string wire)
    {
        var session = CreateSharedRuntimeSession();
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        session.MarkRuntimeTimeOffsetSent();
        Assert.False(session.ProcessInbound(Encoding.ASCII.GetBytes(wire), Second44, 9_000_000).Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, session.Phase);
    }

    [Fact]
    public void RuntimeSentNotificationCannotSkipTheInitialHandshake()
    {
        var session = CreatePendingTimeOffsetSession(false, false);
        Assert.False(session.MarkRuntimeTimeOffsetSent().Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement, session.Phase);
    }

    [Fact]
    public void SingleReceiverStillWaitsAfterRuntimeOffsetWasWritten()
    {
        var session = CreatePendingTimeOffsetSession(true);
        session.ProcessInbound(Response(5), Second44, 1_000_000);
        session.PrepareRuntimeTimeOffset(Second44, 2_000_000);
        Assert.True(session.MarkRuntimeTimeOffsetSent().Accepted);
        Assert.True(session.RuntimeTimeOffsetPending);
        Assert.Equal(1_000_000UL, session.TimeOffsetMicroseconds);
        Assert.False(session.PrepareRuntimeTimeOffset(Second44, 3_000_000).Accepted);
        Assert.True(session.ProcessInbound(Response(6), Second44, 2_000_000).Accepted);
        Assert.False(session.RuntimeTimeOffsetPending);
        Assert.Equal(2_000_000UL, session.TimeOffsetMicroseconds);
    }

    private static MiPlayWfdSourceRtspSession CreateSharedRuntimeSession()
    {
        var session = CreatePendingTimeOffsetSession(false, false);
        Assert.True(session.MarkInitialTimeOffsetSent().Ready);
        return session;
    }

    private static MiPlayWfdSourceRtspSession CreatePendingTimeOffsetSession(
        bool waitForAcknowledgement, bool waitForRuntimeAcknowledgement = true)
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Parse("192.168.10.58"), 36_524, "588290182", waitForAcknowledgement,
            waitForRuntimeAcknowledgement);
        session.Start(Second43);
        session.ProcessInbound(Response(1), Second44, 1_000_000);
        var options = session.ProcessInbound(Request(
            "OPTIONS * RTSP/1.0",
            [
                new("CSeq", "1"),
                new("Require", "org.wfa.wfd1.0"),
                new("lib_version", "audio-speaker-mico-cloud 2.1.4052010"),
            ]), Second44, 1_000_000);
        Assert.True(options.Accepted);
        session.ProcessInbound(Response(2, Encoding.ASCII.GetBytes(
            MiPlayRealLegacyPlaybackSessionEvidence.ReceiverAudioCapabilities)), Second44, 1_000_000);
        session.ProcessInbound(Response(3), Second44, 1_000_000);
        session.ProcessInbound(Response(4), Second44, 1_000_000);
        session.ProcessInbound(Request(
            "SETUP rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [new("CSeq", "2"), new("Transport", "RTP/AVP/TCP;interleaved=0-1")]), Second44, 1_000_000);
        var play = session.ProcessInbound(Request(
            "PLAY rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [new("CSeq", "3"), new("Session", "588290182")]), Second44, 1_000_000);
        Assert.True(play.Accepted);
        Assert.False(play.Ready);
        Assert.Equal(2, play.OutboundMessages.Count);
        return session;
    }

    private static byte[] Request(
        string startLine,
        IReadOnlyList<MiPlayRtspWireHeader> headers) =>
        MiPlayRtspWireMessageCodec.Encode(startLine, headers, []);

    private static byte[] Response(int cseq, byte[]? body = null)
    {
        body ??= [];
        var headers = new List<MiPlayRtspWireHeader> { new("CSeq", cseq.ToString()) };
        if (body.Length != 0)
        {
            headers.Add(new("Content-Type", "text/parameters"));
            headers.Add(new("Content-Length", body.Length.ToString()));
        }
        return MiPlayRtspWireMessageCodec.Encode("RTSP/1.0 200 OK", headers, body);
    }

    private static byte[] TimeOffsetResponse(int cseq, string startLine, string body)
    {
        var bytes = Encoding.ASCII.GetBytes(body);
        return MiPlayRtspWireMessageCodec.Encode(startLine,
            [new("CSeq", cseq.ToString()), new("Content-Length", bytes.Length.ToString())], bytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
