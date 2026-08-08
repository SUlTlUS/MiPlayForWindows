using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFfmpegAacEncoderTests
{
    [Fact]
    public void PinsRawPcmToMiPlayAdtsProcessContract()
    {
        var arguments = MiPlayFfmpegAacEncoder.CreateArgumentList();

        Assert.Equal(30, arguments.Count);
        ContainsInOrder(arguments, "-f", "s16le", "-ar", "44100", "-ac", "2", "-i", "pipe:0");
        ContainsInOrder(arguments, "-c:a", "aac", "-profile:a", "aac_low");
        ContainsInOrder(arguments, "-ar", "48000", "-ac", "2", "-b:a", "128000");
        ContainsInOrder(arguments, "-f", "adts", "-flush_packets", "1", "pipe:1");
    }

    [Fact]
    public void RejectsAMissingExplicitExecutableBeforeStartingAProcess()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            $"dlnacast-missing-ffmpeg-{Guid.NewGuid():N}",
            "ffmpeg.exe");

        Assert.Throws<FileNotFoundException>(() => MiPlayFfmpegAacEncoder.Start(missing));
    }

    [Fact]
    public void SupportsBoundedOfflineCodecAndBitrateProfiles()
    {
        var mediaFoundation = MiPlayFfmpegAacEncoder.CreateArgumentList(128_000, "aac_mf");

        ContainsInOrder(mediaFoundation, "-c:a", "aac_mf", "-b:a", "128000");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayFfmpegAacEncoder.CreateArgumentList(32_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayFfmpegAacEncoder.CreateArgumentList(codecName: "unknown"));
    }

    private static void ContainsInOrder(
        IReadOnlyList<string> values,
        params string[] expected)
    {
        var searchIndex = 0;
        foreach (var value in expected)
        {
            while (searchIndex < values.Count && values[searchIndex] != value)
            {
                searchIndex++;
            }
            Assert.True(searchIndex < values.Count, $"Missing ordered argument {value}.");
            searchIndex++;
        }
    }
}
