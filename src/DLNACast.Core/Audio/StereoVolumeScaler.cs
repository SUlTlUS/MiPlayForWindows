namespace DLNACast.Core.Audio;

public static class StereoVolumeScaler
{
    public static double GetMasterVolume(double leftVolume, double rightVolume) =>
        Math.Max(Clamp(leftVolume), Clamp(rightVolume));

    public static (double Left, double Right) ScaleToMaster(
        double leftVolume,
        double rightVolume,
        double requestedMasterVolume)
    {
        var left = Clamp(leftVolume);
        var right = Clamp(rightVolume);
        var requested = Clamp(requestedMasterVolume);
        var currentMaster = Math.Max(left, right);

        if (currentMaster <= double.Epsilon)
        {
            return (requested, requested);
        }

        var scale = requested / currentMaster;
        return (Clamp(left * scale), Clamp(right * scale));
    }

    private static double Clamp(double volume) => Math.Clamp(volume, 0, 100);
}
