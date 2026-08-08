using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyAudioSourceBootstrapProbeGuardTests
{
    [Fact]
    public void ExplicitGuardAllowsOnlyTheCapturedEightWriteNineFrameBoundary()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        var guard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
            IPAddress.Parse("192.168.10.4"),
            explicitlyAuthorized: true);
        var writes = ReproduceAllOutboundWrites(session);

        Assert.Equal(8, writes.Count);
        MiPlayLegacyAudioSourceWriteDecision? decision = null;
        foreach (var write in writes)
        {
            decision = guard.AuthorizeNextWrite(write);
            Assert.True(decision.CanSend, decision.Reason);
        }

        Assert.NotNull(decision);
        Assert.True(decision.BoundaryReached);
        Assert.Equal(8, decision.WritesAuthorized);
        Assert.Equal(9, decision.FramesAuthorized);
        Assert.False(guard.AuthorizeNextWrite(writes[^1]).CanSend);
    }

    [Fact]
    public void MissingAuthorizationRefusesTheFirstWrite()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        var first = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x1234,
            Encoding.ASCII.GetBytes("1234567890123456")));
        var guard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
            IPAddress.Parse("192.168.10.4"),
            explicitlyAuthorized: false);

        var decision = guard.AuthorizeNextWrite(first.OutboundWrites[0]);

        Assert.False(decision.CanSend);
        Assert.Equal(0, decision.WritesAuthorized);
        Assert.Equal(0, decision.FramesAuthorized);
    }

    [Fact]
    public void BusinessCommandPermanentlyStopsTheGuard()
    {
        var guard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
            IPAddress.Parse("192.168.10.4"),
            explicitlyAuthorized: true);
        var business = new MiPlayLegacyAudioSourceWrite(
            [MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.SetPlaySourceCommand, 0, [])]);

        var decision = guard.AuthorizeNextWrite(business);

        Assert.False(decision.CanSend);
        Assert.False(guard.AuthorizeNextWrite(business).CanSend);
    }

    [Fact]
    public void DryRunLedgerNamesAllWritesAndNoMediaBoundary()
    {
        var ledger = MiPlayLegacyAudioSourceBootstrapProbeGuard.CreateDryRunLedger();

        Assert.Equal(8, ledger.Count);
        Assert.Contains("0x0029", ledger[0], StringComparison.Ordinal);
        Assert.Contains("0x000e", ledger[5], StringComparison.Ordinal);
        Assert.Contains("0x0014", ledger[6], StringComparison.Ordinal);
        Assert.Contains("0x001c", ledger[7], StringComparison.Ordinal);
        Assert.DoesNotContain(ledger, line => line.Contains("0x0040", StringComparison.OrdinalIgnoreCase));
    }

    private static List<MiPlayLegacyAudioSourceWrite> ReproduceAllOutboundWrites(
        MiPlayLegacyAudioSourceSession session)
    {
        var writes = new List<MiPlayLegacyAudioSourceWrite>();
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x00be,
            Encoding.ASCII.GetBytes("1234567890123456"))));
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("receiver-version\0"))));
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            2,
            [])));
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")]))));
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            4,
            MiPlayLegacyStatusScalarCodec.Encode(2))));
        Add(session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            3,
            [])));

        return writes;

        void Add(MiPlayLegacyAudioSourceTransition transition) =>
            writes.AddRange(transition.OutboundWrites);
    }
}
