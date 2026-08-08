using System.Diagnostics;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Streaming PCM-to-ADTS adapter for the MiPlay WFD source path. It owns only
/// a local FFmpeg child process and never opens a network endpoint.
/// </summary>
public sealed class MiPlayFfmpegAacEncoder : IAsyncDisposable
{
    public const int InputSampleRate = 44_100;
    public const int InputChannels = 2;
    public const int InputBitsPerSample = 16;
    public const int OutputSampleRate = 48_000;
    public const int OutputChannels = 2;
    public const int OutputBitRate = 128_000;

    private readonly Process process;
    private readonly MiPlayAdtsStreamParser parser = new();
    private readonly Queue<byte[]> readyAccessUnits = new();
    private readonly byte[] outputBuffer = new byte[16 * 1024];
    private readonly Task<string> standardErrorTask;
    private bool inputCompleted;
    private bool outputCompleted;
    private bool disposed;

    private MiPlayFfmpegAacEncoder(Process process)
    {
        this.process = process;
        standardErrorTask = process.StandardError.ReadToEndAsync();
    }

    public int ProcessId => process.Id;

    public static IReadOnlyList<string> CreateArgumentList(
        int outputBitRate = OutputBitRate,
        string codecName = "aac")
    {
        if (outputBitRate is < 64_000 or > 320_000)
        {
            throw new ArgumentOutOfRangeException(nameof(outputBitRate));
        }
        if (codecName is not ("aac" or "aac_mf"))
        {
            throw new ArgumentOutOfRangeException(nameof(codecName));
        }

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-f", "s16le",
            "-ar", InputSampleRate.ToString(),
            "-ac", InputChannels.ToString(),
            "-i", "pipe:0",
            "-map_metadata", "-1",
            "-vn",
            "-c:a", codecName,
            "-profile:a", codecName == "aac_mf" ? "1" : "aac_low",
            "-ar", OutputSampleRate.ToString(),
            "-ac", OutputChannels.ToString(),
            "-b:a", outputBitRate.ToString(),
            "-f", "adts",
            "-flush_packets", "1",
            "pipe:1",
        ];
    }

    public static MiPlayFfmpegAacEncoder Start(
        string executablePath,
        int outputBitRate = OutputBitRate,
        string codecName = "aac")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if ((Path.IsPathRooted(executablePath) || executablePath.Contains(Path.DirectorySeparatorChar)) &&
            !File.Exists(executablePath))
        {
            throw new FileNotFoundException("The requested FFmpeg executable does not exist.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in CreateArgumentList(outputBitRate, codecName))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("FFmpeg did not start.");
            }
            return new MiPlayFfmpegAacEncoder(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async ValueTask WritePcmAsync(
        ReadOnlyMemory<byte> pcm,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (inputCompleted)
        {
            throw new InvalidOperationException("The FFmpeg PCM input has already completed.");
        }
        if (pcm.IsEmpty || pcm.Length % (InputChannels * (InputBitsPerSample / 8)) != 0)
        {
            throw new ArgumentException("PCM input must contain complete stereo signed-16 samples.", nameof(pcm));
        }

        await process.StandardInput.BaseStream.WriteAsync(pcm, cancellationToken).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteInputAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (inputCompleted)
        {
            return;
        }

        await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        inputCompleted = true;
    }

    /// <summary>
    /// Returns one normalized MPEG-2 AAC-LC 48 kHz stereo ADTS access unit, or
    /// null after a clean FFmpeg EOF. This method has a single-reader contract.
    /// </summary>
    public async ValueTask<byte[]?> ReadAccessUnitAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (readyAccessUnits.Count != 0)
        {
            return readyAccessUnits.Dequeue();
        }
        if (outputCompleted)
        {
            return null;
        }

        while (readyAccessUnits.Count == 0)
        {
            var read = await process.StandardOutput.BaseStream
                .ReadAsync(outputBuffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                outputCompleted = true;
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var standardError = await standardErrorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"FFmpeg AAC encoder exited with code {process.ExitCode}: {standardError.Trim()}");
                }
                if (parser.PendingByteCount != 0)
                {
                    throw new InvalidDataException(
                        $"FFmpeg ended with {parser.PendingByteCount} incomplete ADTS bytes.");
                }
                return null;
            }

            foreach (var accessUnit in parser.Push(outputBuffer.AsSpan(0, read)))
            {
                readyAccessUnits.Enqueue(accessUnit);
            }
        }

        return readyAccessUnits.Dequeue();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        try
        {
            if (!inputCompleted)
            {
                process.StandardInput.Close();
                inputCompleted = true;
            }
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process may exit between HasExited and Kill.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
