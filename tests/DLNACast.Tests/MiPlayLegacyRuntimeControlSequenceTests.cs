using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyRuntimeControlSequenceTests
{
    [Fact]
    public void InterleavedVolumeAdvancesTheSharedHeartbeatSequence()
    {
        var sequence = new MiPlayLegacyRuntimeControlSequence();

        var firstHeartbeat = sequence.PrepareHeartbeat();
        var volume = sequence.PrepareSetVolume(24);
        var secondHeartbeat = sequence.PrepareHeartbeat();

        AssertCommand(firstHeartbeat, MiPlayProtocolConstants.HeartbeatCommand, 16, "");
        AssertCommand(volume, MiPlayProtocolConstants.SetVolumeCommand, 17, "00000018");
        AssertCommand(secondHeartbeat, MiPlayProtocolConstants.HeartbeatCommand, 18, "");
    }

    [Fact]
    public void PauseAndResumeShareTheRuntimeSequenceAndUseEmptyPayloads()
    {
        var sequence = new MiPlayLegacyRuntimeControlSequence();

        var pause = sequence.PreparePause();
        var resumePair = sequence.PrepareResumePair();
        var heartbeat = sequence.PrepareHeartbeat();

        AssertCommand(pause, MiPlayProtocolConstants.PauseCommand, 16, "");
        Assert.Equal(MiPlayProtocolConstants.PauseAcknowledgementCommand, pause.AcknowledgementCommand);
        Assert.False(pause.WaitForAcknowledgement);
        AssertCommand(resumePair.First, MiPlayProtocolConstants.ResumeCommand, 17, "");
        Assert.Equal(
            MiPlayProtocolConstants.ResumeAcknowledgementCommand,
            resumePair.First.AcknowledgementCommand);
        Assert.False(resumePair.First.WaitForAcknowledgement);
        AssertCommand(resumePair.Second, MiPlayProtocolConstants.ResumeCommand, 18, "");
        Assert.Equal(
            MiPlayProtocolConstants.ResumeAcknowledgementCommand,
            resumePair.Second.AcknowledgementCommand);
        Assert.False(resumePair.Second.WaitForAcknowledgement);
        Assert.Equal(29, MiPlayLegacyRuntimeControlSequence.CapturedResumeRepeatDelayMilliseconds);
        AssertCommand(heartbeat, MiPlayProtocolConstants.HeartbeatCommand, 19, "");
        Assert.True(heartbeat.WaitForAcknowledgement);
    }

    [Fact]
    public void WrapsAfterTheMaximumSequenceForLongRunningSessions()
    {
        var sequence = new MiPlayLegacyRuntimeControlSequence(ushort.MaxValue);

        Assert.Equal(ushort.MaxValue, sequence.PrepareHeartbeat().Sequence);
        Assert.Equal((ushort)0, sequence.PrepareHeartbeat().Sequence);
    }

    [Fact]
    public void AcceptsOnlySameSequenceSamePayloadSetVolumeAcknowledgement()
    {
        var command = new MiPlayLegacyRuntimeControlSequence().PrepareSetVolume(36);
        var accepted = Decode(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetVolumeAcknowledgementCommand,
            command.Sequence,
            MiPlaySetVolumePayloadCodec.Encode(36)));
        var wrongPayload = Decode(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetVolumeAcknowledgementCommand,
            command.Sequence,
            MiPlaySetVolumePayloadCodec.Encode(35)));
        var wrongSequence = Decode(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetVolumeAcknowledgementCommand,
            checked((ushort)(command.Sequence + 1)),
            MiPlaySetVolumePayloadCodec.Encode(36)));

        Assert.True(MiPlayLegacyRuntimeControlSequence.IsExpectedAcknowledgement(command, accepted));
        Assert.False(MiPlayLegacyRuntimeControlSequence.IsExpectedAcknowledgement(command, wrongPayload));
        Assert.False(MiPlayLegacyRuntimeControlSequence.IsExpectedAcknowledgement(command, wrongSequence));
    }

    private static void AssertCommand(
        MiPlayLegacyRuntimeControlCommand command,
        ushort expectedCommand,
        ushort expectedSequence,
        string expectedPayloadHex)
    {
        var frame = Decode(command.CommandFrame);
        Assert.Equal(expectedCommand, frame.Command);
        Assert.Equal(expectedSequence, frame.Sequence);
        Assert.Equal(expectedPayloadHex, Convert.ToHexString(frame.Payload));
    }

    private static MiPlayCommandFrame Decode(byte[] bytes)
    {
        Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed));
        Assert.Equal(bytes.Length, consumed);
        return Assert.IsType<MiPlayCommandFrame>(frame);
    }
}
