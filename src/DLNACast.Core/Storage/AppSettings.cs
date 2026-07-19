using System.Text.Json;

namespace DLNACast.Core.Storage;

public sealed record AppSettings(
    string? LastRendererUdn = null,
    string CaptureMode = "SystemMix",
    string? LastSourceId = null,
    bool AllowMp3Fallback = true,
    bool MinimizeToTray = true);

public sealed class AppSettingsStore
{
    private readonly string _settingsPath;

    public AppSettingsStore(string? basePath = null)
    {
        BasePath = basePath ?? ResolveBasePath();
        Directory.CreateDirectory(BasePath);
        _settingsPath = Path.Combine(BasePath, "settings.json");
    }

    public string BasePath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken)
                       .ConfigureAwait(false) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var temporary = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private static string ResolveBasePath()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLNACast");
        }
    }
}

