namespace DLNACast.Core.MiPlay;

public sealed record MiPlayWfdAudioPacket(
    ushort SequenceNumber,
    uint Timestamp90Khz,
    bool ContainsProgramTables,
    byte[] NormalizedAdtsAccessUnit,
    byte[] TransportStream,
    byte[] RtpPacket,
    byte[] WireFrame);

/// <summary>
/// Stateful AAC access-unit to MiPlay WFD wire packetizer. It reproduces the
/// rooted phone's one-AU-per-timestamp cadence, DEADBEEF SSRC, 90 kHz clock,
/// same-timestamp RTP fragmentation, and observed table refresh positions
/// without owning a network stream.
/// </summary>
public sealed class MiPlayWfdAudioPacketizer
{
    public const uint CapturedSynchronizationSource = 0xdead_beef;
    public const uint TimestampStep90Khz = 1_920;
    public const int CapturedAccessUnitDurationMicroseconds = 21_333;
    public const uint FirstPeriodicTableAccessUnitIndex = 13;
    public const uint PeriodicTableAccessUnitInterval = 5;

    private readonly MiPlayMpegTsAudioMuxer muxer;
    private readonly uint synchronizationSource;
    private readonly ulong initialProgramClockReference90Khz;
    private readonly uint initialTimestamp90Khz;
    private readonly uint firstPeriodicTableAccessUnitIndex;
    private readonly uint periodicTableAccessUnitInterval;
    private ushort sequenceNumber;
    private uint timestamp90Khz;
    private uint accessUnitIndex;

    public MiPlayWfdAudioPacketizer(
        ushort initialSequenceNumber = 0,
        uint initialTimestamp90Khz = 0,
        uint synchronizationSource = CapturedSynchronizationSource,
        ulong initialProgramClockReference90Khz = 0,
        byte initialPatContinuityCounter = 1,
        byte initialPmtContinuityCounter = 1,
        byte initialAudioContinuityCounter = 0,
        uint firstPeriodicTableAccessUnitIndex = FirstPeriodicTableAccessUnitIndex,
        uint periodicTableAccessUnitInterval = PeriodicTableAccessUnitInterval)
    {
        if (firstPeriodicTableAccessUnitIndex == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstPeriodicTableAccessUnitIndex));
        }
        if (periodicTableAccessUnitInterval == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodicTableAccessUnitInterval));
        }

        sequenceNumber = initialSequenceNumber;
        timestamp90Khz = initialTimestamp90Khz;
        this.initialTimestamp90Khz = initialTimestamp90Khz;
        this.synchronizationSource = synchronizationSource;
        this.initialProgramClockReference90Khz = initialProgramClockReference90Khz;
        this.firstPeriodicTableAccessUnitIndex = firstPeriodicTableAccessUnitIndex;
        this.periodicTableAccessUnitInterval = periodicTableAccessUnitInterval;
        muxer = new MiPlayMpegTsAudioMuxer(
            initialPatContinuityCounter,
            initialPmtContinuityCounter,
            initialAudioContinuityCounter);
    }

    public MiPlayWfdAudioPacket Packetize(ReadOnlySpan<byte> adtsAccessUnit)
    {
        var packets = PacketizeAccessUnit(adtsAccessUnit);
        if (packets.Count != 1)
        {
            throw new InvalidOperationException(
                "The AAC access unit requires multiple captured MiPlay RTP fragments; use PacketizeAccessUnit.");
        }

        return packets[0];
    }

    /// <summary>
    /// Packetizes one AAC access unit into one or more RTP packets. All
    /// fragments retain the access unit's RTP timestamp; only the final
    /// fragment has the marker bit, exactly as in the rooted-phone trace.
    /// </summary>
    public IReadOnlyList<MiPlayWfdAudioPacket> PacketizeAccessUnit(
        ReadOnlySpan<byte> adtsAccessUnit)
    {
        var normalized = MiPlayAdtsStreamParser.NormalizeMpeg2AacLc48KhzStereo(adtsAccessUnit);
        var includeProgramTables = ShouldIncludeProgramTables(
            accessUnitIndex,
            firstPeriodicTableAccessUnitIndex,
            periodicTableAccessUnitInterval);
        ulong? programClockReference = includeProgramTables
            ? initialProgramClockReference90Khz +
              CalculateCapturedTimestamp90Khz(accessUnitIndex)
            : null;
        var muxed = muxer.MuxAdtsAccessUnit(normalized, timestamp90Khz, programClockReference);
        var maximumPayloadLength = MiPlayRtpPacketCodec.MaximumMpegTsPayloadLength;
        var fragmentCount = (int)Math.Ceiling(
            muxed.TransportStream.Length / (double)maximumPayloadLength);
        var packets = new List<MiPlayWfdAudioPacket>(fragmentCount);
        for (var fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
        {
            var offset = fragmentIndex * maximumPayloadLength;
            var length = Math.Min(maximumPayloadLength, muxed.TransportStream.Length - offset);
            var transportStream = muxed.TransportStream.AsSpan(offset, length).ToArray();
            var marker = fragmentIndex == fragmentCount - 1;
            var rtp = MiPlayRtpPacketCodec.EncodeMpegTsPayload(
                sequenceNumber,
                timestamp90Khz,
                synchronizationSource,
                transportStream,
                marker);
            packets.Add(new MiPlayWfdAudioPacket(
                sequenceNumber,
                timestamp90Khz,
                muxed.ContainsProgramTables && fragmentIndex == 0,
                normalized,
                transportStream,
                rtp,
                MiPlayWfdInterleavedFrameCodec.Encode(rtp)));
            sequenceNumber++;
        }

        accessUnitIndex++;
        timestamp90Khz = unchecked(
            initialTimestamp90Khz + CalculateCapturedTimestamp90Khz(accessUnitIndex));
        return packets;
    }

    public static uint CalculateCapturedTimestamp90Khz(uint accessUnitIndex) =>
        unchecked((uint)(
            (ulong)accessUnitIndex * CapturedAccessUnitDurationMicroseconds * 90_000UL /
            1_000_000UL));

    public static bool ShouldIncludeProgramTables(
        uint accessUnitIndex,
        uint firstPeriodicTableAccessUnitIndex = FirstPeriodicTableAccessUnitIndex,
        uint periodicTableAccessUnitInterval = PeriodicTableAccessUnitInterval) =>
        accessUnitIndex == 0 ||
        (accessUnitIndex >= firstPeriodicTableAccessUnitIndex &&
         (accessUnitIndex - firstPeriodicTableAccessUnitIndex) % periodicTableAccessUnitInterval == 0);
}
