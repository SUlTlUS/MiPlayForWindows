namespace DLNACast.Core.Storage;

public sealed class AppLogger
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private readonly string _logDirectory;
    private readonly Lock _sync = new();

    public AppLogger(string basePath)
    {
        _logDirectory = Path.Combine(basePath, "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            RotateIfNeeded();
            var sanitized = message.Replace("\r", " ").Replace("\n", " ");
            File.AppendAllText(
                Path.Combine(_logDirectory, "dlnacast.log"),
                $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}");
        }
    }

    private void RotateIfNeeded()
    {
        var active = Path.Combine(_logDirectory, "dlnacast.log");
        if (!File.Exists(active) || new FileInfo(active).Length < MaximumLogBytes)
        {
            return;
        }

        var oldest = Path.Combine(_logDirectory, "dlnacast.5.log");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = 4; index >= 1; index--)
        {
            var source = Path.Combine(_logDirectory, index == 1 ? "dlnacast.log" : $"dlnacast.{index}.log");
            var destination = Path.Combine(_logDirectory, $"dlnacast.{index + 1}.log");
            if (File.Exists(source)) File.Move(source, destination, overwrite: true);
        }
    }
}
