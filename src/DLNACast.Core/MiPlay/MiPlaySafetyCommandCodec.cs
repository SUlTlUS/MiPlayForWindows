namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySafetyCommand(
    ushort Command,
    ushort Sequence,
    bool IsAcknowledgement,
    byte[] JsonPayload);

/// <summary>
/// Composes the verified modern SafetyInfo/SafetyAuth payload envelope with the legacy '$' command frame.
/// It is intentionally an offline codec; it does not open TCP connections or send packets.
/// </summary>
public static class MiPlaySafetyCommandCodec
{
    public static byte[] Encode(
        ushort command,
        ushort sequence,
        ReadOnlySpan<byte> jsonPayload)
    {
        if (!IsSafetyCommand(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown MiPlay safety command.");
        }

        var envelope = MiPlaySafetyEnvelopeCodec.Encode(
            IsAcknowledgementCommand(command),
            MiPlayProtocolConstants.SafetyValueType,
            jsonPayload);
        return MiPlayCommandFrameCodec.Encode(command, sequence, envelope);
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out MiPlaySafetyCommand? command,
        out int bytesConsumed)
    {
        command = null;
        bytesConsumed = 0;

        if (!MiPlayCommandFrameCodec.TryDecode(data, out var frame, out var frameBytesConsumed) ||
            frame is null ||
            !IsSafetyCommand(frame.Command) ||
            !MiPlaySafetyEnvelopeCodec.TryDecode(frame.Payload, out var envelope, out var envelopeBytesConsumed) ||
            envelope is null ||
            envelopeBytesConsumed != frame.Payload.Length ||
            envelope.IsAcknowledgement != IsAcknowledgementCommand(frame.Command))
        {
            return false;
        }

        command = new MiPlaySafetyCommand(
            frame.Command,
            frame.Sequence,
            envelope.IsAcknowledgement,
            envelope.Payload);
        bytesConsumed = frameBytesConsumed;
        return true;
    }

    private static bool IsSafetyCommand(ushort command) =>
        command is MiPlayProtocolConstants.SafetyInfoCommand or
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand;

    private static bool IsAcknowledgementCommand(ushort command) =>
        command is MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand;
}

