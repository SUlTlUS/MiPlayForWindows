namespace DLNACast.App;

internal static class StartupTrace
{
    private static readonly Lock Gate = new();
    private static readonly string LogPath = GetLogPath();

    private static string GetLogPath()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "startup.log");
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLNACast",
                "startup.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Startup diagnostics must never become another startup failure.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write($"{message}{Environment.NewLine}{exception}");
}
