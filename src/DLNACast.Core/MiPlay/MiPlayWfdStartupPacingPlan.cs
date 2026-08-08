namespace DLNACast.Core.MiPlay;

/// <summary>
/// Initial AAC access-unit send times measured from the clean rooted-phone
/// selection of the LX06 at 192.168.10.3. After the captured startup window,
/// the source continues at the nominal 1024/48000-second AAC cadence.
/// </summary>
public static class MiPlayWfdStartupPacingPlan
{
    private static readonly double[] CapturedDueMilliseconds =
    [
        0.000, 0.000, 0.812, 0.812, 3.463, 4.230,
        20.360, 22.199, 23.864, 40.903, 42.905, 62.727,
        84.784, 111.306, 128.829, 148.735, 170.391, 202.805,
        211.654,
    ];

    public const double NominalAccessUnitMilliseconds = 1024d / 48_000d * 1_000d;
    public static int CapturedAccessUnitCount => CapturedDueMilliseconds.Length;

    public static double GetDueAfterMilliseconds(long accessUnitIndex)
    {
        if (accessUnitIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessUnitIndex));
        }
        if (accessUnitIndex < CapturedDueMilliseconds.Length)
        {
            return CapturedDueMilliseconds[(int)accessUnitIndex];
        }

        return CapturedDueMilliseconds[^1] +
               (accessUnitIndex - (CapturedDueMilliseconds.Length - 1)) *
               NominalAccessUnitMilliseconds;
    }
}
