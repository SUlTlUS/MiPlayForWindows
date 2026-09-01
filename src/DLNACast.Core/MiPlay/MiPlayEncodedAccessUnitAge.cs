namespace DLNACast.Core.MiPlay;

internal static class MiPlayEncodedAccessUnitAge
{
    public static double CalculateMilliseconds(
        ulong currentMonotonicMicroseconds,
        ulong presentationTimeMicroseconds)
    {
        if (currentMonotonicMicroseconds <= presentationTimeMicroseconds)
        {
            return 0;
        }

        return (currentMonotonicMicroseconds - presentationTimeMicroseconds) / 1_000d;
    }
}
