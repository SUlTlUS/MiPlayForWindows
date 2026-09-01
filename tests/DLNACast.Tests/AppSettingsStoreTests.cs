using DLNACast.Core.Storage;

namespace DLNACast.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task PersistsMiPlayTimeOffsetMilliseconds()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"dlnacast-settings-{Guid.NewGuid():N}");
        try
        {
            var store = new AppSettingsStore(basePath);
            await store.SaveAsync(new AppSettings(MiPlayTimeOffsetMilliseconds: 650));

            var loaded = await store.LoadAsync();

            Assert.Equal(650, loaded.MiPlayTimeOffsetMilliseconds);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task OlderSettingsKeepTheTabletPlaybackDelayDefault()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"dlnacast-settings-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(basePath);
            await File.WriteAllTextAsync(
                Path.Combine(basePath, "settings.json"),
                """
                {
                  "TransportMode": "MiPlay",
                  "SpeakerOnlyPlayback": true
                }
                """);

            var loaded = await new AppSettingsStore(basePath).LoadAsync();

            Assert.Equal(1_000, loaded.MiPlayTimeOffsetMilliseconds);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }
}
