namespace DLNACast.Core.MiPlay;

/// <summary>
/// Preserves the global access-unit index while filling a shared receiver's
/// startup runway. Late members replay a short retained window without joining
/// the live publisher barrier, then attach at one explicit future boundary.
/// </summary>
internal sealed class MiPlaySharedStartupAccessUnitBuffer
{
    // Tablet audioSender::WritePacket retains packets until the newest PTS is
    // at least 1,000,000 us ahead of the oldest, then evicts one packet. At
    // 48 kHz / 1024 samples this stabilizes at 47 AAC access units. This is
    // only the late-member replay history; initial group startup remains the
    // validated 14-AU runway.
    internal const int LateJoinRequiredAccessUnitCount = 47;
    private readonly Queue<MiPlaySharedIndexedAccessUnit> accessUnits;

    private MiPlaySharedStartupAccessUnitBuffer(
        Queue<MiPlaySharedIndexedAccessUnit> accessUnits)
    {
        this.accessUnits = accessUnits;
        FirstAccessUnitIndex = accessUnits.Peek().AccessUnitIndex;
    }

    internal int Count => accessUnits.Count;
    internal long FirstAccessUnitIndex { get; }

    internal static async Task<MiPlaySharedStartupAccessUnitBuffer> FillAsync(
        Func<CancellationToken, ValueTask<MiPlaySharedIndexedAccessUnit>> readAsync,
        int requiredAccessUnitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readAsync);
        if (requiredAccessUnitCount is < 1 or > MiPlaySharedAccessUnitBroadcast.RetainedAccessUnitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredAccessUnitCount));
        }
        var accessUnits = new Queue<MiPlaySharedIndexedAccessUnit>(requiredAccessUnitCount);
        while (accessUnits.Count < requiredAccessUnitCount)
        {
            accessUnits.Enqueue(await readAsync(cancellationToken).ConfigureAwait(false));
        }

        var indexes = accessUnits.Select(value => value.AccessUnitIndex).ToArray();
        for (var index = 1; index < indexes.Length; index++)
        {
            if (indexes[index] != indexes[index - 1] + 1)
            {
                throw new InvalidOperationException(
                    "The shared MiPlay startup runway must contain contiguous access units.");
            }
        }
        return new MiPlaySharedStartupAccessUnitBuffer(accessUnits);
    }

    internal bool TryRead(out MiPlaySharedIndexedAccessUnit accessUnit) =>
        accessUnits.TryDequeue(out accessUnit);
}
