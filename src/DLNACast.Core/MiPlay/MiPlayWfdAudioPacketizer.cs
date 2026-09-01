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
    private ulong timestampOffset90Khz;
    private ulong nextMediaPosition90Khz;
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
        nextMediaPosition90Khz = initialTimestamp90Khz;
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

    internal MiPlayWfdAudioPacketizer(
        MiPlayWfdAudioPacketizerSnapshot snapshot,
        ulong initialProgramClockReference90Khz = 0,
        uint firstPeriodicTableAccessUnitIndex = FirstPeriodicTableAccessUnitIndex,
        uint periodicTableAccessUnitInterval = PeriodicTableAccessUnitInterval)
        : this(
            snapshot.SequenceNumber,
            snapshot.InitialTimestamp90Khz,
            snapshot.SynchronizationSource,
            initialProgramClockReference90Khz,
            snapshot.Continuity.PatContinuityCounter,
            snapshot.Continuity.PmtContinuityCounter,
            snapshot.Continuity.AudioContinuityCounter,
            firstPeriodicTableAccessUnitIndex,
            periodicTableAccessUnitInterval)
    {
        timestamp90Khz = snapshot.Timestamp90Khz;
        timestampOffset90Khz = snapshot.TimestampOffset90Khz;
        nextMediaPosition90Khz = snapshot.NextMediaPosition90Khz;
        accessUnitIndex = snapshot.AccessUnitIndex;
    }

    public ulong NextMediaPosition90Khz => nextMediaPosition90Khz;
    internal ushort NextSequenceNumber => sequenceNumber;
    internal uint AccessUnitIndex => accessUnitIndex;

    internal MiPlayWfdAudioPacketizerSnapshot CaptureSnapshot() =>
        new(
            sequenceNumber,
            timestamp90Khz,
            timestampOffset90Khz,
            nextMediaPosition90Khz,
            accessUnitIndex,
            initialTimestamp90Khz,
            synchronizationSource,
            muxer.CaptureContinuityState());

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
        ReadOnlySpan<byte> adtsAccessUnit,
        ulong? liveProgramClockReference90Khz = null,
        ulong? presentationTimestamp90Khz = null,
        ushort liveProgramClockReferenceExtension = 0)
    {
        if (liveProgramClockReferenceExtension >= 300)
        {
            throw new ArgumentOutOfRangeException(nameof(liveProgramClockReferenceExtension));
        }
        if (liveProgramClockReferenceExtension != 0 && !liveProgramClockReference90Khz.HasValue)
        {
            throw new ArgumentException(
                "A live PCR extension requires a live PCR base.", nameof(liveProgramClockReferenceExtension));
        }
        var presentationPosition90Khz = presentationTimestamp90Khz.HasValue
            ? checked(presentationTimestamp90Khz.Value + timestampOffset90Khz)
            : timestamp90Khz;
        if (presentationTimestamp90Khz.HasValue)
        {
            timestamp90Khz = unchecked((uint)presentationPosition90Khz);
        }
        var normalized = MiPlayAdtsStreamParser.NormalizeMpeg2AacLc48KhzStereo(adtsAccessUnit);
        var includeProgramTables = ShouldIncludeProgramTables(
            accessUnitIndex,
            firstPeriodicTableAccessUnitIndex,
            periodicTableAccessUnitInterval);
        ulong? programClockReference = includeProgramTables
            ? liveProgramClockReference90Khz ??
              (initialProgramClockReference90Khz +
               CalculateCapturedTimestamp90Khz(accessUnitIndex) +
               timestampOffset90Khz)
            : null;
        var muxed = muxer.MuxAdtsAccessUnit(
            normalized,
            presentationPosition90Khz,
            programClockReference,
            includeProgramTables ? liveProgramClockReferenceExtension : (ushort)0);
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

        var previousTimestamp90Khz = timestamp90Khz;
        accessUnitIndex++;
        if (presentationTimestamp90Khz.HasValue)
        {
            // RTP wraps at 32 bits; PES wraps at 33. Keep the media position
            // unwrapped so a later runtime TIME_OFFSET cannot jump at 13 hours.
            nextMediaPosition90Khz = checked(presentationPosition90Khz + TimestampStep90Khz);
            timestamp90Khz = unchecked((uint)nextMediaPosition90Khz);
        }
        else
        {
            timestamp90Khz = unchecked((uint)(
                initialTimestamp90Khz +
                CalculateCapturedTimestamp90Khz(accessUnitIndex) +
                timestampOffset90Khz));
            nextMediaPosition90Khz = checked(
                nextMediaPosition90Khz + unchecked(timestamp90Khz - previousTimestamp90Khz));
        }
        return packets;
    }

    public void AdvanceTimeline90Khz(uint timestampDelta90Khz)
    {
        timestampOffset90Khz = checked(timestampOffset90Khz + timestampDelta90Khz);
        timestamp90Khz = unchecked(timestamp90Khz + timestampDelta90Khz);
        nextMediaPosition90Khz = checked(nextMediaPosition90Khz + timestampDelta90Khz);
    }

    public static uint CalculateCapturedTimestamp90Khz(uint accessUnitIndex)
    {
        // Preserve the rooted-phone startup trace, then use the exact AAC
        // 1024/48000 cadence. Repeating the trace's 21,333-us quantization for
        // hours loses one third of a microsecond per frame and eventually
        // separates RTP/PTS from the sender's steady pacing clock.
        var capturedLastAccessUnitIndex = checked(
            (uint)(MiPlayWfdStartupPacingPlan.CapturedAccessUnitCount - 1));
        if (accessUnitIndex <= capturedLastAccessUnitIndex)
        {
            return CalculateMicrosecondQuantizedTimestamp90Khz(accessUnitIndex);
        }

        var capturedLastTimestamp =
            CalculateMicrosecondQuantizedTimestamp90Khz(capturedLastAccessUnitIndex);
        return unchecked((uint)(
            capturedLastTimestamp +
            ((ulong)accessUnitIndex - capturedLastAccessUnitIndex) * TimestampStep90Khz));
    }

    private static uint CalculateMicrosecondQuantizedTimestamp90Khz(uint accessUnitIndex) =>
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

internal readonly record struct MiPlayWfdAudioPacketizerSnapshot(
    ushort SequenceNumber,
    uint Timestamp90Khz,
    ulong TimestampOffset90Khz,
    ulong NextMediaPosition90Khz,
    uint AccessUnitIndex,
    uint InitialTimestamp90Khz,
    uint SynchronizationSource,
    MiPlayMpegTsContinuityState Continuity)
{
    internal static MiPlayWfdAudioPacketizerSnapshot Initial =>
        new MiPlayWfdAudioPacketizer().CaptureSnapshot();
}
