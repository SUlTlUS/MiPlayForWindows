using System.Buffers.Binary;

namespace DLNACast.Core.Audio;

public static class WaveStreamHeader
{
    public static byte[] CreateIndefinitePcmHeader()
    {
        var header = new byte[44];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), uint.MaxValue);
        "WAVEfmt "u8.CopyTo(header.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), PcmFrameBuffer.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), PcmFrameBuffer.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(28),
            PcmFrameBuffer.SampleRate * PcmFrameBuffer.Channels * PcmFrameBuffer.BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(32),
            PcmFrameBuffer.Channels * PcmFrameBuffer.BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), PcmFrameBuffer.BitsPerSample);
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), uint.MaxValue);
        return header;
    }
}

