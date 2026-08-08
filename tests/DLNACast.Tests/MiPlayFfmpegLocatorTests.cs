using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFfmpegLocatorTests
{
    [Fact]
    public void PrefersExplicitThenAdjacentThenPathExecutable()
    {
        var root = Directory.CreateTempSubdirectory("dlnacast-ffmpeg-");
        try
        {
            var explicitDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "explicit"));
            var appDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "app"));
            var pathDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "path"));
            var explicitExecutable = Touch(explicitDirectory.FullName);
            var adjacentExecutable = Touch(appDirectory.FullName);
            var pathExecutable = Touch(pathDirectory.FullName);

            Assert.Equal(Path.GetFullPath(explicitExecutable), MiPlayFfmpegLocator.FindExecutable(
                explicitExecutable,
                appDirectory.FullName,
                pathDirectory.FullName));
            Assert.Equal(Path.GetFullPath(adjacentExecutable), MiPlayFfmpegLocator.FindExecutable(
                Path.Combine(root.FullName, "missing.exe"),
                appDirectory.FullName,
                pathDirectory.FullName));

            File.Delete(adjacentExecutable);
            Assert.Equal(Path.GetFullPath(pathExecutable), MiPlayFfmpegLocator.FindExecutable(
                Path.Combine(root.FullName, "missing.exe"),
                appDirectory.FullName,
                pathDirectory.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static string Touch(string directory)
    {
        var path = Path.Combine(directory, "ffmpeg.exe");
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        return path;
    }
}
