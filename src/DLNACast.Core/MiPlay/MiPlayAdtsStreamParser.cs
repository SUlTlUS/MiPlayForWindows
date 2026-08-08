namespace DLNACast.Core.MiPlay;

/// <summary>
/// Incremental parser for an encoder's ADTS byte stream. Complete access units
/// are normalized to the captured MPEG-2 AAC-LC, 48 kHz, stereo header shape.
/// </summary>
public sealed class MiPlayAdtsStreamParser
{
    private byte[] pending = new byte[8 * 1024];
    private int pendingCount;

    public int PendingByteCount => pendingCount;

    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(pendingCount + bytes.Length);
        bytes.CopyTo(pending.AsSpan(pendingCount));
        pendingCount += bytes.Length;

        var accessUnits = new List<byte[]>();
        var offset = 0;
        while (pendingCount - offset >= MiPlayAdtsHeader.Length)
        {
            var frame = pending.AsSpan(offset, pendingCount - offset);
            if (frame[0] != 0xff || (frame[1] & 0xf6) != 0xf0)
            {
                throw new InvalidDataException("The AAC encoder stream lost ADTS synchronization.");
            }

            var headerLength = (frame[1] & 1) != 0 ? 7 : 9;
            var frameLength = ((frame[3] & 0x03) << 11) | (frame[4] << 3) | (frame[5] >> 5);
            if (frameLength < headerLength || frameLength > 0x1fff)
            {
                throw new InvalidDataException("The ADTS frame length is outside its 13-bit boundary.");
            }
            if (frame.Length < frameLength)
            {
                break;
            }

            accessUnits.Add(NormalizeMpeg2AacLc48KhzStereo(frame[..frameLength]));
            offset += frameLength;
        }

        if (offset != 0)
        {
            pending.AsSpan(offset, pendingCount - offset).CopyTo(pending);
            pendingCount -= offset;
        }

        return accessUnits;
    }

    public static byte[] NormalizeMpeg2AacLc48KhzStereo(ReadOnlySpan<byte> accessUnit)
    {
        if (accessUnit.Length < MiPlayAdtsHeader.Length ||
            accessUnit[0] != 0xff ||
            (accessUnit[1] & 0xf6) != 0xf0)
        {
            throw new ArgumentException("One complete ADTS access unit is required.", nameof(accessUnit));
        }

        var headerLength = (accessUnit[1] & 1) != 0 ? 7 : 9;
        var frameLength = ((accessUnit[3] & 0x03) << 11) | (accessUnit[4] << 3) | (accessUnit[5] >> 5);
        var profile = accessUnit[2] >> 6;
        var frequencyIndex = (accessUnit[2] >> 2) & 0x0f;
        var channelConfiguration = ((accessUnit[2] & 1) << 2) | (accessUnit[3] >> 6);
        if (frameLength != accessUnit.Length ||
            accessUnit.Length < headerLength ||
            profile != 1 ||
            frequencyIndex != 3 ||
            channelConfiguration != 2)
        {
            throw new ArgumentException(
                "MiPlay requires one AAC-LC, 48 kHz, stereo ADTS access unit.",
                nameof(accessUnit));
        }

        return MiPlayAdtsHeader.Prepend(accessUnit[headerLength..]);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= pending.Length)
        {
            return;
        }

        var capacity = pending.Length;
        while (capacity < required)
        {
            capacity = checked(capacity * 2);
        }
        Array.Resize(ref pending, capacity);
    }
}
