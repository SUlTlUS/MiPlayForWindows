namespace DLNACast.Core.MiPlay;

public static class MiPlayFfmpegLocator
{
    public const string EnvironmentVariableName = "DLNACAST_FFMPEG";

    public static string? FindExecutable(
        string? environmentOverride = null,
        string? applicationDirectory = null,
        string? pathValue = null)
    {
        var explicitPath = environmentOverride ??
            Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (IsExecutable(explicitPath))
        {
            return Path.GetFullPath(explicitPath!);
        }

        var baseDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        var adjacent = Path.Combine(baseDirectory, "ffmpeg.exe");
        if (File.Exists(adjacent))
        {
            return Path.GetFullPath(adjacent);
        }

        var searchPath = pathValue ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
            }
        }
        return null;
    }

    public static string RequireExecutable()
    {
        return FindExecutable() ?? throw new FileNotFoundException(
            "MiPlay requires ffmpeg.exe with the Windows Media Foundation AAC encoder. " +
            $"Place ffmpeg.exe beside the app, add it to PATH, or set {EnvironmentVariableName}.");
    }

    private static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
