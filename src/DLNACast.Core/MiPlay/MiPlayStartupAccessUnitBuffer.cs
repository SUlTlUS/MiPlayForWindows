namespace DLNACast.Core.MiPlay;

/// <summary>
/// Holds a small, ordered AAC runway before the first media write. aac_mf
/// produces several access units roughly every 80-95 ms; starting the pacing
/// clock from the first item of an otherwise empty batch makes the sender
/// repeatedly catch up in sub-millisecond bursts.
/// </summary>
internal sealed class MiPlayStartupAccessUnitBuffer
{
    // The captured tablet startup schedule consumes thirteen access units in
    // its first 85 ms. Keeping one more frame covers the measured 94-ms aac_mf
    // output interval, after which the ordinary 1024/48-kHz pacing is stable.
    internal const int RequiredAccessUnitCount = 14;

    private readonly Queue<MiPlayEncodedAccessUnit> accessUnits;

    private MiPlayStartupAccessUnitBuffer(Queue<MiPlayEncodedAccessUnit> accessUnits)
    {
        this.accessUnits = accessUnits;
    }

    internal int Count => accessUnits.Count;

    internal static async Task<MiPlayStartupAccessUnitBuffer> FillAsync(
        Func<CancellationToken, ValueTask<MiPlayEncodedAccessUnit>> readAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readAsync);
        var accessUnits = new Queue<MiPlayEncodedAccessUnit>(RequiredAccessUnitCount);
        while (accessUnits.Count < RequiredAccessUnitCount)
        {
            accessUnits.Enqueue(await readAsync(cancellationToken).ConfigureAwait(false));
        }
        return new MiPlayStartupAccessUnitBuffer(accessUnits);
    }

    internal bool TryRead(out MiPlayEncodedAccessUnit accessUnit) =>
        accessUnits.TryDequeue(out accessUnit);
}
